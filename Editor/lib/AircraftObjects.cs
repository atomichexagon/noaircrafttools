using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using static hexagonest.Tools.Utilities;


namespace hexagonest.Tools
{
	// input simulator
	[Serializable] public struct ControlInputs { public float pitch, roll, yaw, throttle, brake, custom_axis1; }



	[Serializable] public class AircraftData
	{
		public GameObject root;
		public SerializedObject aircraft, def, param;
		public AircraftPhysics physics;
		public Part[] parts;
		public Tank[] tanks;
		public float fuel_fraction;
		public Airfoil[] airfoils;
		public HardpointManager hardpoint_manager;
		public bool has_hardpoints = false;

		public AircraftData(GameObject aircraft_root) {
			root = aircraft_root;
			aircraft = new SerializedObject(root.GetComponent(game_type("Aircraft")));
			def = new SerializedObject(find_prop<UnityEngine.Object>(aircraft, "definition"));
			param = new SerializedObject(find_prop<UnityEngine.Object>(def, "aircraftParameters"));
			physics = new AircraftPhysics(this);

			List<Part> aircraft_parts = new List<Part>();
			List<Tank> aircraft_tanks = new List<Tank>();

			foreach (Component aero_part in root.GetComponentsInChildren(game_type("AeroPart"), true))
			{
				GameObject go = aero_part.gameObject;

				Part new_part = new Part(this, go, aero_part);
				aircraft_parts.Add(new_part);
				if (new_part.fuel_tank != null)
				{
					aircraft_tanks.Add((Tank)new_part.fuel_tank);
				}
			}

			parts = aircraft_parts.ToArray();
			tanks = aircraft_tanks.ToArray();

			// process airfoils
			List<Airfoil> aircraft_foils = new List<Airfoil>();
			var param_airfoils = find_prop(param, "airfoils");
			for (int i = 0; i < param_airfoils.arraySize; i++)
			{
				var foil = param_airfoils.GetArrayElementAtIndex(i);
				string name = find_prop_rel<string>(foil, "name");
				AnimationCurve lift = find_prop_rel<AnimationCurve>(foil, "liftCoef");
				AnimationCurve drag = find_prop_rel<AnimationCurve>(foil, "dragCoef");
				aircraft_foils.Add(new Airfoil { name = name, lift = lift, drag = drag });
			}

			var weapon_manager_obj = find_prop<Component>(aircraft, "weaponManager");
			if (weapon_manager_obj != null)
			{
				var s_weps = new SerializedObject(weapon_manager_obj);
				hardpoint_manager = new HardpointManager(physics, s_weps);
			}
			airfoils = aircraft_foils.ToArray();
			physics.recalculate();
		}

		public void set_fuel_fraction(float new_fuel_fraction)
		{
			foreach (Tank tank in tanks)
			{
				tank.set_fuel_fraction(new_fuel_fraction);
			}
			fuel_fraction = new_fuel_fraction;
			physics.quick_recalculate();
		}
	}


	[Serializable] public struct Airfoil
	{
		public string name; public AnimationCurve lift, drag;
	}



	// structs for simulation use cases
	[Serializable] public class AircraftPhysics
	{
		public AircraftData parent_aircraft;
		public Vector3 dry_cg, fuel_cg, max_fuel_cg, stores_cg, current_cg;
		public float dry_mass, fuel_mass, max_fuel_mass, stores_mass, current_mass;

		public AircraftPhysics(AircraftData set_parent_aircraft) => parent_aircraft = set_parent_aircraft;

		public void recalculate()
		{
			cg_finder dry = new cg_finder(), fuel = new cg_finder(), max_fuel = new cg_finder(), current = new cg_finder();

			foreach (Part part in parent_aircraft.parts)
			{
				PartPhysics part_phys = part.current_physics;
				Vector3 part_cg = part_phys.cg;

				dry.add(part_cg, part_phys.dry_mass + part_phys.extra_mass);
				fuel.add(part_cg, part_phys.fuel_mass);
				current.add(part_cg, part_phys.mass);

				if (part.fuel_tank != null)
				{
					max_fuel.add(part_cg, ((Tank)part.fuel_tank).capacity);
				}
			}

			(dry_cg, dry_mass) = dry.average();
			(fuel_cg, fuel_mass) = fuel.average();
			(max_fuel_cg, max_fuel_mass) = max_fuel.average();
			current.add(stores_cg, stores_mass);
			(current_cg, current_mass) = current.average();

			parent_aircraft.fuel_fraction = fuel_mass / max_fuel_mass;
		}

		public (Vector3 fuel_cg, float fuel_mass) calculate_fuel_mass(float new_fuel_fraction)
		{
			float new_fuel_mass = max_fuel_mass * new_fuel_fraction;
			return (max_fuel_cg, new_fuel_mass);
		}

		// this doesn't seem useful but AircraftTools uses it so whatever
		// doesn't set values! just returns a cg and mass from fuel fraction
		public (Vector3 wet_cg, float wet_mass) calculate_wet_mass(float new_fuel_fraction)
		{
			(Vector3 new_fuel_cg, float new_fuel_mass) = calculate_fuel_mass(new_fuel_fraction);
			cg_finder wet = new cg_finder();
			wet.add(dry_cg, dry_mass);
			wet.add(new_fuel_cg, new_fuel_mass);
			return wet.average();
		}

		// sets values, pretty much just used when the only change is fuel fraction
		// anything else requires the more expensive recalculate()
		// not that performance is really a concern in the editor but.. we're here now whatever
		public void quick_recalculate()
		{
			(Vector3 new_fuel_cg, float new_fuel_mass) = calculate_fuel_mass(parent_aircraft.fuel_fraction);
			cg_finder current = new cg_finder();
			current.add(dry_cg, dry_mass);
			current.add(new_fuel_cg, new_fuel_mass);
			current.add(stores_cg, stores_mass);

			fuel_cg = new_fuel_cg; fuel_mass = new_fuel_mass;

			(current_cg, current_mass) = current.average();
		}

		public void set_stores_phys(Vector3 new_stores_cg, float new_stores_mass)
		{
			stores_cg = new_stores_cg; stores_mass = new_stores_mass;
			// stores updates are more intrusive than fuel and can add extra mass to specific parts
			// so the more thorough recalculation is needed
			recalculate();
		}
	}



	[Serializable] public class ControlSurface
	{
		public AircraftData parent_aircraft;
		public GameObject surface_object;
		public Part attached_part; // Attached Surface
		public GameObject surface_hinge; // Visible Mesh
		public float pitch_response, yaw_response, roll_response, brake_response, servo_speed, split_drag;
		public bool is_flap, locked; // locked is for potential future WingSweepController integration


	}



	[Serializable] public class Part
	{
		public AircraftData parent_aircraft;
		public GameObject part_object;
		public Transform transform, lift_transform;
		public SerializedObject aero_part;
		public PartPhysics original_physics, current_physics;
		public Tank fuel_tank;
		public Vector3 world_cg;
		public float mass;

		public Part(AircraftData parent, GameObject go, Component aero_comp)
		{
			parent_aircraft = parent;
			part_object = go; transform = go.transform;
			aero_part = new SerializedObject(aero_comp);

			Transform set_lift_transform = find_prop<Transform>(aero_part, "liftNormal");
			lift_transform = set_lift_transform != null ? set_lift_transform : transform;

			world_cg = transform.position;
			Transform cg_transform = find_prop<Transform>(aero_part, "centerOfMass");
			if (cg_transform != null)
			{
				world_cg = ((Transform)cg_transform).position;
			} else {
				// TODO: Make this a utility function to find the CG of an arbitrary GameObject with a collider.
				MeshCollider part_col = go.GetComponent<MeshCollider>();
				if (part_col != null && part_col.sharedMesh != null)
				{
					// using a placeholder GameObject, giving it the same mesh, a collider, and a rigidbody
					// to find the rigidbody's center of mass, since that's what the game uses
					GameObject probe = new GameObject("ColliderProbe");
					probe.transform.SetPositionAndRotation(transform.position, transform.rotation);
					MeshCollider probe_col = probe.AddComponent<MeshCollider>();
					probe_col.sharedMesh = part_col.sharedMesh; probe_col.convex = true;
					Rigidbody probe_rb = probe.AddComponent<Rigidbody>();
					Physics.SyncTransforms();
					probe_rb.ResetCenterOfMass();
					world_cg = probe_rb.worldCenterOfMass;
					UnityEngine.Object.DestroyImmediate(probe);
				}
			}

			mass = find_prop<float>(aero_part, "mass");
			float dry_mass = mass, fuel_mass = 0;

			Component part_tank = go.GetComponent(game_type("FuelTank"));
			if (part_tank != null)
			{
				var s_tank = new SerializedObject(part_tank);
				float tank_mass = find_prop<float>(s_tank, "fuelMass"); // current load
				float tank_capacity = find_prop<float>(s_tank, "fuelCapacity"); // max load
				dry_mass -= tank_mass; fuel_mass += tank_mass;

				fuel_tank = new Tank(this, tank_mass, tank_capacity);
			}

			original_physics = new PartPhysics {
				wing_area = find_prop<float>(aero_part, "wingArea"),
				drag_area = find_prop<float>(aero_part, "dragArea"),
				airflow_channeling = find_prop<float>(aero_part, "airflowChanneling"),
				center_of_lift = find_prop<Vector3>(aero_part, "centerOfLift"),
				foil_id = find_prop<int>(aero_part, "airfoil"),
				cg = world_cg,
				dry_mass = dry_mass, fuel_mass = fuel_mass, extra_mass = 0f
				//mass = mass, 
			};
			current_physics = original_physics;
		}

		public void reset_physics()
		{
			current_physics = original_physics;
		}
	}


	[Serializable] public class PartPhysics
	{
		//public AircraftData parent_aircraft;
		public float wing_area, drag_area, airflow_channeling;
		public Vector3 cg, center_of_lift; public int foil_id;
		public float dry_mass, fuel_mass, extra_mass;
		public float mass { get => dry_mass + fuel_mass + extra_mass; }
		//public float mass;
	}


	[Serializable] public class Tank
	{
		public Part associated_part;
		public float capacity;
		private float _mass;
		public float mass {
			get => _mass;
			set { _mass = value; associated_part.current_physics.fuel_mass = value; }
		}

		public Tank(Part set_part, float set_mass, float set_capacity)
		{
			associated_part = set_part;
			_mass = set_mass;
			capacity = set_capacity;
		}

		public void set_fuel_fraction(float new_fuel_fraction)
		{
			mass = capacity * new_fuel_fraction;
		}
	}
}