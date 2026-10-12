using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using static hexagonest.Tools.Utilities;


namespace hexagonest.Tools
{
	// input simulator
	[Serializable] public class ControlInputs
	{
		public float pitch = 0, roll = 0, yaw = 0, throttle = 0, brake = 0, custom_axis1 = 0;
		public bool gear = false; // blow my head off
		// TODO: simulate gear drag?
	}

	// for objects wearing a context hat
	[Serializable] public class AircraftState
	{
		public float airspeed = 100f, altitude = 50f, alpha = 0f, beta = 0f; // NOT in degrees!
		public Vector3 velocity = new Vector3(0f, 0f, 50f);

		// from numbers (in degrees)
		public AircraftState(float _airspeed, float _altitude, float _alpha, float _beta)
		{
			airspeed = _airspeed; altitude = _altitude;
			alpha = _alpha * Mathf.Deg2Rad; beta = _beta * Mathf.Deg2Rad;
			velocity = Simulation.velocity_vector(_airspeed, alpha * Mathf.Deg2Rad, beta * Mathf.Deg2Rad);
		}

		// from params (also in degrees)
		public AircraftState(ParameterFixedUnit _airspeed, ParameterFixedUnit _altitude, float _alpha, float _beta)
			: this(_airspeed.current_value, _altitude.current_value, _alpha, _beta) {}
	}

	// to simplify aircraft update logic
	public abstract class Simulated
	{
		// return true if something changed to trigger a sim update (if auto run is on)
		public abstract bool update(ref AircraftState context, ref ControlInputs inputs, float dt);

		// reset any transformations or changes to the actual gameobjects/transforms
		public abstract void reset();
	}



	[Serializable] public class AircraftData
	{
		public GameObject root;
		public SerializedObject aircraft, def, param;
		public AircraftPhysics physics;
		public Part[] parts;
		public Dictionary<Component, Part> part_lookup;
		public ControlSurface[] control_surfaces;
		public HighLiftDevice[] high_lift_devices;
		public Tank[] tanks;
		public float fuel_fraction;
		public Airfoil[] airfoils;
		public HardpointManager hardpoint_manager;
		public Simulated[] simulated_objects;
		public AircraftState last_aircraft_state;
		public ControlInputs last_controls_state;

		public AircraftData(GameObject aircraft_root) {
			root = aircraft_root;
			aircraft = new SerializedObject(root.GetComponent(game_type("Aircraft")));
			def = new SerializedObject(find_prop<UnityEngine.Object>(aircraft, "definition"));
			param = new SerializedObject(find_prop<UnityEngine.Object>(def, "aircraftParameters"));
			physics = new AircraftPhysics(this);

			part_lookup = new Dictionary<Component, Part>();
			List<Part> aircraft_parts = new List<Part>();
			List<Tank> aircraft_tanks = new List<Tank>();
			List<ControlSurface> aircraft_surfaces = new List<ControlSurface>();

			foreach (Component aero_part in root.GetComponentsInChildren(game_type("AeroPart"), true))
			{
				GameObject go = aero_part.gameObject;

				Part new_part = new Part(this, go, aero_part);
				part_lookup[aero_part] = new_part;

				aircraft_parts.Add(new_part);
				if (new_part.fuel_tank != null)
				{
					aircraft_tanks.Add((Tank)new_part.fuel_tank);
				}
			}
			parts = aircraft_parts.ToArray();
			tanks = aircraft_tanks.ToArray();

			List<Simulated> simulated_list = new List<Simulated>();

			// SweptWingController

			control_surfaces = root.GetComponentsInChildren(game_type("ControlSurface"), true)
				.Select(comp => new ControlSurface(this, comp))
				.ToArray();
			simulated_list.AddRange(control_surfaces);

			// ControlSurfacePhysics?

			high_lift_devices = root.GetComponentsInChildren(game_type("HighLiftDevice"), true)
				.Select(comp => new HighLiftDevice(this, comp))
				.ToArray();
			simulated_list.AddRange(high_lift_devices);

			// Gear?
			
			simulated_objects = simulated_list.ToArray();

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
			airfoils = aircraft_foils.ToArray();

			var weapon_manager_obj = find_prop<Component>(aircraft, "weaponManager");
			if (weapon_manager_obj != null)
			{
				var s_weps = new SerializedObject(weapon_manager_obj);
				hardpoint_manager = new HardpointManager(physics, s_weps);
			}
			physics.recalculate();
		}

		public void set_fuel_fraction(float new_fuel_fraction)
		{
			Array.ForEach(tanks, tank => tank.set_fuel_fraction(new_fuel_fraction));
			fuel_fraction = new_fuel_fraction;
			physics.quick_recalculate();
		}

		// updates everything and forces AircraftPhysics to recalculate afterwards to be as accurate as possible
		public bool update(AircraftState context, ControlInputs inputs, float dt = 100f)
		{
			bool changed = false;
			Array.ForEach(simulated_objects, sim_obj => changed = sim_obj.update(ref context, ref inputs, dt) ? true : changed);
			if (changed) physics.recalculate(); // gonna be slow
			return changed;
		}

		// only updates control surfaces
		public bool set_controls(ControlInputs new_controls, float dt = 100f)
		{
			bool surfaces_moved = false;
			Array.ForEach(control_surfaces, surface => surfaces_moved = surface.actuate(ref new_controls, dt) ? true : surfaces_moved);
			last_controls_state = new_controls;
			return surfaces_moved;
		}

		// reset all simulated components
		public void reset()
		{
			Array.ForEach(simulated_objects, sim_obj => sim_obj.reset());
		}
	}


	[Serializable] public class Airfoil
	{
		public string name; public AnimationCurve lift, drag;
	}



	// structs for simulation use cases
	[Serializable] public class AircraftPhysics
	{
		public AircraftData parent_aircraft;
		public Vector3 dry_cg, fuel_cg, max_fuel_cg, stores_cg, current_cg;
		public float dry_mass, fuel_mass, max_fuel_mass, stores_mass, current_mass;
		public float wing_area, drag_area;

		public AircraftPhysics(AircraftData set_parent_aircraft) => parent_aircraft = set_parent_aircraft;

		public void recalculate()
		{
			cg_finder dry = new cg_finder(), fuel = new cg_finder(), max_fuel = new cg_finder(), current = new cg_finder();

			float new_wing_area = 0, new_drag_area = 0;

			foreach (Part part in parent_aircraft.parts)
			{
				PartPhysics part_phys = part.current_physics;
				Vector3 part_cg = part_phys.cg;

				new_wing_area += part_phys.wing_area;
				new_drag_area += part_phys.drag_area;

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

			wing_area = new_wing_area;
			drag_area = new_drag_area;

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

	// TODO: SwingWingController
	// [Serializable] public class SwingWingController : Simulated
	// {
	// 	public float position;

	// 	public override bool update(ref AircraftState context, ref ControlInputs inputs, float dt)
	// 	{
	// 		return false;
	// 	}

	// 	public override void reset()
	// 	{

	// 	}
	// }

	[Serializable] public class HighLiftDevice : Simulated
	{
		public AircraftData parent_aircraft; // only really needed to look for attached_part
		public Part attached_part;
		public Transform device_transform;
		//public SwingWingController swing_controller; // oh no
		public float speed_deploy, speed_retract, alpha_min, alpha_factor, deployed_area;
		public float part_area_retracted, part_area_deployed;
		public float position = 0f; // current state

		public struct MovingPart {
			public bool move, rotate;
			public Transform transform;
			public Vector3 position_retracted, position_deployed, angles_retracted, angles_deployed;

			public void animate(float amount)
			{
				if (move) transform.localPosition = Vector3.Lerp(position_retracted, position_deployed, amount);
				if (rotate) transform.localEulerAngles = Vector3.Lerp(angles_retracted, angles_deployed, amount);
			}
		}
		public MovingPart[] moving_parts;

		public HighLiftDevice(AircraftData parent, Component device_comp)
		{
			parent_aircraft = parent;
			var s_device = new SerializedObject(device_comp);
			device_transform = device_comp.gameObject.transform;

			speed_deploy = find_prop<float>(s_device, "speedDeployed");
			speed_retract = find_prop<float>(s_device, "speedRetracted");
			alpha_min = find_prop<float>(s_device, "alphaMin");
			alpha_factor = find_prop<float>(s_device, "alphaFactor");
			deployed_area = find_prop<float>(s_device, "deployedArea");
			var s_moving_parts = find_prop(s_device, "movingParts");

			attached_part = parent_aircraft.part_lookup[find_prop<Component>(s_device, "aeroPart")];
			part_area_retracted = attached_part.original_physics.wing_area;
			part_area_deployed = part_area_retracted + deployed_area;

			moving_parts = new MovingPart[s_moving_parts.arraySize];
			for (int j = 0; j < s_moving_parts.arraySize; j++) {
				var part_data = s_moving_parts.GetArrayElementAtIndex(j);

				moving_parts[j] = new MovingPart {
					move = find_prop_rel<bool>(part_data, "move"),
					rotate = find_prop_rel<bool>(part_data, "rotate"),
					transform = find_prop_rel<Transform>(part_data, "transform"),
					position_retracted = find_prop_rel<Vector3>(part_data, "positionRetracted"),
					position_deployed = find_prop_rel<Vector3>(part_data, "positionDeployed"),
					angles_retracted = find_prop_rel<Vector3>(part_data, "anglesRetracted"),
					angles_deployed = find_prop_rel<Vector3>(part_data, "anglesDeployed")
				};
			}
		}

		public override bool update(ref AircraftState context, ref ControlInputs inputs, float dt)
		{
			float target_position = Mathf.Clamp01(1f - Mathf.Max(context.airspeed - speed_deploy, 0f) / (speed_retract - speed_deploy));
			// TODO: since this checks swing_controller, we should probably make sure swing_controller updates before high lift devices
			// (once that exists)
			// if (swing_controller != null && swing_controller.position > 0f)
			// {
			// 	num = 0f;
			// }
			if (alpha_factor > 0f)
			{
				Vector3 rel_vel = device_transform.InverseTransformDirection(context.velocity);
				float f = Mathf.Atan2(rel_vel.y, rel_vel.z) * -Mathf.Rad2Deg;
				target_position += Mathf.Clamp01(Mathf.Abs(f) - alpha_min) * alpha_factor;
			}
			bool changed = false;
			if (Mathf.Abs(target_position - position) > 0.001f)
			{
				position += Mathf.Clamp(target_position - position, -dt, dt);
				attached_part.current_physics.wing_area = Mathf.Lerp(part_area_retracted, part_area_deployed, position);
				
				if (SimEnvironment.check_in_sim_scene(parent_aircraft))
				{
					Array.ForEach(moving_parts, part => part.animate(position));
					changed = true;
				}
			}
			//if (changed) Debug.Log($"Flap {device_transform.gameObject} updated");
			return changed;
		}

		public override void reset()
		{
			if (!SimEnvironment.check_in_sim_scene(parent_aircraft)) return;
			Array.ForEach(moving_parts, part => part.animate(0f));
		}
	}



	[Serializable] public class ControlSurface : Simulated
	{
		public AircraftData parent_aircraft; // only really needed to look for attached_part
		public GameObject surface_object;
		public Part attached_part; // Attached Surface
		public GameObject visible_mesh;
		public Transform split_upper, split_lower;
		public float pitch_response, roll_response, yaw_response, brake_response, servo_speed;
		public float split_drag, max_split, yaw_split;

		public bool is_flap, locked; // locked is for potential future WingSweepController integration
		public float current_pitch, current_roll, current_yaw, split_amount, last_angle, last_split;

		public Quaternion resting_rotation, resting_split_rotation;

		public ControlSurface(AircraftData parent, Component surface_comp)
		{
			parent_aircraft = parent;
			surface_object = surface_comp.gameObject;
			var s_surface = new SerializedObject(surface_comp);

			attached_part = parent_aircraft.part_lookup[find_prop<Component>(s_surface, "attachedSurface")];
			visible_mesh = find_prop<GameObject>(s_surface, "visibleMesh");
			resting_rotation = visible_mesh.transform.localRotation;

			pitch_response = find_prop<float>(s_surface, "pitchRange");
			roll_response = find_prop<float>(s_surface, "rollRange");
			yaw_response = find_prop<float>(s_surface, "yawRange");
			brake_response = find_prop<float>(s_surface, "brakeRange");
			is_flap = find_prop<bool>(s_surface, "flap");
			servo_speed = find_prop<float>(s_surface, "servoSpeed");
			split_drag = find_prop<float>(s_surface, "splitDrag");
			max_split = find_prop<float>(s_surface, "maxSplit");
			yaw_split = find_prop<float>(s_surface, "yawSplitFactor");

			if (max_split > 0)
			{
				split_upper = find_prop<Transform>(s_surface, "splitUpper");
				split_lower = find_prop<Transform>(s_surface, "splitLower");

				resting_split_rotation = split_upper.transform.localRotation;
			}
			attached_part.control_surface = this;
		}

		public override bool update(ref AircraftState context, ref ControlInputs inputs, float dt)
		{
			return actuate(ref inputs, dt);
		}

		// dt is here for futureproofing i guess, in the horrible event that we end up doing dynamic sims
		public bool actuate(ref ControlInputs inputs, float dt = 100f)
		{
			float servo_delta = servo_speed * dt;
			if (!is_flap)
			{
				float pitch_delta = inputs.pitch * pitch_response - current_pitch;
				current_pitch += Mathf.Clamp(pitch_delta, -servo_delta, servo_delta);
				current_pitch = Mathf.Clamp(current_pitch, -Mathf.Abs(pitch_response), Mathf.Abs(pitch_response));

				float roll_delta = inputs.roll * roll_response - current_roll;
				current_roll += Mathf.Clamp(roll_delta, -servo_delta, servo_delta);
				current_roll = Mathf.Clamp(current_roll, -Mathf.Abs(roll_response), Mathf.Abs(roll_response));

				float yaw_delta = inputs.yaw * yaw_response - current_yaw;
				current_yaw += Mathf.Clamp(yaw_delta, -servo_delta, servo_delta);
				current_yaw = Mathf.Clamp(current_yaw, -Mathf.Abs(yaw_response), Mathf.Abs(yaw_response));
			} else { // this behavior is really weird, but it's how the game does it, so..
				if (inputs.gear)
				{
					current_pitch -= servo_delta;
				} else {
					current_pitch += servo_delta;
				}
				current_pitch = Mathf.Clamp(current_pitch, -pitch_response, 0f);
			}
			float main_angle = current_pitch + current_roll + current_yaw;
			bool safe_to_edit = SimEnvironment.check_in_sim_scene(parent_aircraft);
			if (safe_to_edit && main_angle != last_angle) visible_mesh.transform.localRotation = resting_rotation * Quaternion.AngleAxis(main_angle, Vector3.right);

			if (max_split > 0f)
			{
				float split_target = inputs.throttle == 0f ? max_split : 0f;
				split_target = Mathf.Clamp(split_target + yaw_split * inputs.yaw * max_split, 0f, max_split);
				split_amount += Mathf.Clamp(split_target - split_amount, -servo_delta, servo_delta);
				if (safe_to_edit)
				{
					split_upper.transform.localRotation = resting_split_rotation * Quaternion.AngleAxis(split_amount, Vector3.right);
					split_lower.transform.localRotation = resting_split_rotation * Quaternion.AngleAxis(-split_amount, Vector3.right);
				}
			}

			bool changed = (last_angle != main_angle) || (last_split != split_amount);
			last_angle = main_angle; last_split = split_amount;
			//if (changed) Debug.Log($"Surface {visible_mesh} updated");
			return changed;
		}

		public override void reset()
		{
			if (!SimEnvironment.check_in_sim_scene(parent_aircraft)) return;
			visible_mesh.transform.localRotation = resting_rotation;
			if (max_split > 0f)
			{
				split_upper.transform.localRotation = resting_split_rotation;
				split_lower.transform.localRotation = resting_split_rotation;
			}
		}
	}



	[Serializable] public class Part
	{
		public AircraftData parent_aircraft;
		public GameObject part_object;
		public Transform transform, lift_transform;
		public SerializedObject aero_part;
		public PartPhysics original_physics, current_physics;
		public ControlSurface control_surface;
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
			
			// must be passed in externally, because i don't know if all control surfaces are part of the same
			// gameobject as the aeropart (which is also the case with the fuel tank, but... it is what it is)
			// this can probably be changed later once the rules around that are discovered
			control_surface = null;

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