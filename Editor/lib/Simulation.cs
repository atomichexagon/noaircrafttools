using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;

using static hexagonest.Tools.Utilities;


namespace hexagonest.Tools
{
	public static class Simulation
	{
		public static (float rho, float mach) isa_atm(float altitude)
		{
			float t, p;
			if (altitude < 11000f)
			{
				t = 288.15f - altitude * 0.0065f;
				p = 101325f * Mathf.Pow(t / 288.15f, 5.2559f);
			} else {
				t = 216.65f;
				p = 22632f * Mathf.Exp(-Constants.G * (altitude - 11000f) / (287.05f * t));
			}
			return (p / (287.05f * t), Mathf.Sqrt(1.4f * 287.05f * t));
		}


		// simulation brainstorming
		// should be able to (automatically) handle control surfaces, high lift devices (if configured for alpha - check how that works), etc
		// should be able to accomodate different fuel loads and stores arrangements
		//
		// to go further, the wind tunnel will have to be able to modify an aircraft, so we're gonna want to make a wind tunnel scene
		// that allows importing aircraft (automatically?) for the more advanced modes.
		// this should also allow us to show visual aids, move control surfaces, etc
		//
		// desired result graphs:
		// pitch/yaw moment as a function of alpha/beta angle for a given airspeed/altitude
		// pitch input required for level flight as a function of airspeed


		public static (float cl, float cd) foil_coefficients(ref Airfoil[] airfoils, int airfoil_id, float alpha)
		{
			float cl = 0, cd = 0;
			if (airfoil_id < 0)
			{
				cl = 1.8f * Mathf.Sin(5f * alpha);
				cd = 1.5f * (1f - Mathf.Cos(2f * alpha)) + 0.02f;
			} else {
				Airfoil foil = airfoils[airfoil_id];
				cl = foil.lift.Evaluate(alpha);
				cd = foil.drag.Evaluate(alpha);
			}
			return (cl, cd);
		}


		// could be used to make a force visualizer, both vectors should be in world coordinates
		public struct ForcePos { public Vector3 force, pos; public ForcePos(Vector3 _force, Vector3 _pos) { force = _force; pos = _pos; } }

		// collects output forces in one place
		public class SimForces {
			public Vector3 root_position;
			public List<ForcePos> total = new List<ForcePos>(), lift = new List<ForcePos>(),
				drag = new List<ForcePos>(), torque = new List<ForcePos>();
			
			public void add(Vector3 _pos, Vector3 _total, Vector3 _lift, Vector3 _drag, Vector3 _torque)
			{
				Vector3 pos = _pos - root_position;
				total.Add(new ForcePos(_total, pos));
				lift.Add(new ForcePos(_lift, pos));
				drag.Add(new ForcePos(_drag, pos));
				torque.Add(new ForcePos(_torque, pos));
			}

			// shows lift, drag, torque
			public void visualize(AircraftData aircraft, Transform root_transform, Vector3 total_torque)
			{
				Transform root = aircraft.root.transform;

				Handles.color = Color.yellow;
				Vector3 cg = aircraft.physics.current_cg;
				Vector3 cg_relative = root_transform.TransformPoint(cg);
				Handles.SphereHandleCap(0, cg_relative, Quaternion.identity, 0.5f, EventType.Repaint);
				
				lift.ForEach(force => draw_arrow(root, force.pos, force.force, new Color(0.2f, 0.2f, 1.0f)));
				drag.ForEach(force => draw_arrow(root, force.pos, force.force, new Color(1.0f, 0.4f, 0.2f)));

				// pitch
				draw_torque(root_transform, new Vector3(0f, 0f, 0f), new Vector3(total_torque.x, 0f, 0f), new Color(1.0f, 0.4f, 0.2f));
				// roll
				draw_torque(root_transform, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, total_torque.z), new Color(0.2f, 0.2f, 1.0f));
				// yaw
				draw_torque(root_transform, new Vector3(0f, 0f, 0f), new Vector3(0f, total_torque.y, 0f), new Color(0.2f, 1.0f, 0.2f));
			}
		}


		public static void draw_arrow(Transform relative_to, Vector3 position, Vector3 vector, Color color, float force_scale = 0.25f, bool linear = false)
		{
			if (vector == Vector3.zero) return;
			Handles.color = color;
			Vector3 origin = relative_to.TransformPoint(position);
			Vector3 dir = relative_to.TransformDirection(vector);
			float len = vector.magnitude;
			if (!linear) len = Mathf.Log(1 + vector.magnitude);
			len = Mathf.Clamp(len * force_scale, 0.01f, 25f);
			Handles.ArrowHandleCap(0, origin, Quaternion.LookRotation(dir), len, EventType.Repaint);
		}


		public static void draw_torque(Transform relative_to, Vector3 position, Vector3 torque, Color color, float radius = 5f)
		{
			if (torque == Vector3.zero) return;
			float mag = torque.magnitude;
			Handles.color = color;
			Vector3 origin = relative_to.TransformPoint(position);
			Vector3 axis = torque / mag;
			axis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
			float sign = Vector3.Dot(torque, axis) > 0 ? 1 : -1;
			Vector3 from = Vector3.Cross(axis, Vector3.up);
			if (from.sqrMagnitude < 1e-4f) from = Vector3.Cross(axis, Vector3.right);
			from.Normalize();
			float sweep = sign * 300f * (1f - Mathf.Exp(-mag / 1e6f));//Mathf.Clamp(Mathf.Log(1 + mag) / 6, 0.01f, 1f);

			Handles.DrawWireArc(origin, axis, from, sweep, radius, 3f);

			Vector3 end = origin + Quaternion.AngleAxis(sweep, axis) * from * radius;
			Vector3 tangent = Vector3.Cross(axis, end - origin).normalized;
			Handles.ConeHandleCap(0, end, Quaternion.LookRotation(sign * tangent), radius * 0.15f, EventType.Repaint);
		}


		public struct SimResults { public Vector3 total, lift, drag, torque; public SimForces forces; }

		public static Quaternion aircraft_orientation(float alpha, float beta)
		{
			return Quaternion.Inverse(Quaternion.LookRotation(velocity_vector(alpha, beta), Vector3.up));
		}

		public static Vector3 velocity_vector(float alpha, float beta)
		{
			return (Quaternion.Euler(alpha * Mathf.Rad2Deg, beta * Mathf.Rad2Deg, 0f) * Vector3.forward);
		}


		// general purpose bisection solver that tries to find zero with the given evaluator
		public static float bisection_solver(float min, float max, int scan_steps, int bisect_steps, float tolerance, Func<float, float> evaluate)
		{
			float a = -1f, c = float.NaN;
			float t_a = evaluate(a);
			float t_c = 0f;

			float best_x = a, best_value = Mathf.Abs(t_a);

			// start by refining [a,c] a bit
			float range = max - min;
			for (int j = 1; j < scan_steps + 1; j++)
			{
				float x = min + (range * j / scan_steps);
				float t_x = evaluate(x);
				if (Mathf.Abs(t_x) < best_value) { best_x = x; best_value = Mathf.Abs(t_x); }
				if (t_a * t_x <= 0f) { c = x; t_c = t_x; break; }
				a = x; t_a = t_x;
			}

			if (!float.IsNaN(c)) {
				// straightforward bisection, calculate torque at a, then at midpoint
				// if it crosses zero, we overshot, refine the range to (a, b) and go again
				// if it doesn't cross zero, undershot, refine the range to (b, c) and go again
				for (int j = 0; j < bisect_steps; j++)
				{
					float b = (a + c) / 2f;
					float t_b = evaluate(b);
					if (Mathf.Abs(t_b) < tolerance) break; // that's probably close enough

					if ((t_a * t_b) <= 0f) c = b; // crossed zero, root between a,b
					else { a = b; t_a = t_b; }  // didn't cross zero, root between b,c
				}
				best_x = (a + c) / 2f;
			}
			return best_x;
		}


		public static float find_pitch_trim(AircraftData aircraft, AircraftState state, ControlInputs controls, int scan_steps = 5, int bisect_steps = 20)
		{
			return bisection_solver(-1f, 1f, scan_steps, bisect_steps, 1f, x => update_and_simulate(aircraft, state, controls.set_pitch(x)).torque.x);
		}


		public static (float alpha, float trim) find_level_flight_alpha(AircraftData aircraft, AircraftState state, ControlInputs controls,
			int scan_steps = 5, int bisect_steps = 20)
		{
			float aircraft_weight = aircraft.physics.current_mass * Constants.G;
			float last_trim = controls.pitch;
			Func<float, float> evaluator = (alpha) => {
				state.alpha = alpha * Mathf.Deg2Rad;
				float trim = find_pitch_trim(aircraft, state, controls);
				controls.set_pitch(trim); last_trim = trim;

				// TODO: rotate forces - results are going to be in aircraft's reference frame
				// (i'd do it now but it's 5am)
				SimResults results = aircraft_forces(aircraft, state);
				// using total allows for thrust to automatically be considered
				return results.total.y - aircraft_weight;
			};

			return (bisection_solver(-10f, 30f, scan_steps, bisect_steps, 10f, evaluator), last_trim);
		}


		public static float neutral_point_z(AircraftData aircraft, AircraftState state, ControlInputs controls)
		{
			const float d = 0.5f * Mathf.Deg2Rad;
			float initial_alpha = state.alpha;
			SimResults a = aircraft_forces(aircraft, state.set(state => state.alpha = initial_alpha - d));
			SimResults b = aircraft_forces(aircraft, state.set(state => state.alpha = initial_alpha + d));
			float dm = b.torque.x - a.torque.x;
			float df = b.total.y - a.total.y;
			return (aircraft.physics.current_cg.z - dm / df);
		}


		public static SimResults update_and_simulate(AircraftData aircraft, AircraftState state, ControlInputs controls)
		{
			aircraft.update(state, controls);
			return Simulation.aircraft_forces(aircraft, state);
		}

		
		// idk convenience i guess
		// v - airspeed (m/s)
		// h - altitude (meters)
		// alpha - pitch angle of attack, + nose up
		// beta - yaw angle of attack, + nose left (!)
		// log_spam - guess (please god do not enable this when sweeping)
		public static SimResults aircraft_forces(AircraftData aircraft, float v, float h, float alpha, float beta, bool log_spam = false)
		{ return aircraft_forces(aircraft, new AircraftState(v, h, alpha, beta), log_spam); }


		// resettable simulation counter
		public static int num_simulations = 0;

		// aircraft - guess
		// state - sim environment
		// log_spam - guess (please god do not enable this when sweeping)
		public static SimResults aircraft_forces(AircraftData aircraft, AircraftState state, bool log_spam = false)
		{
			num_simulations += 1;
			(float rho, float mach) = isa_atm(state.altitude);
			float v = state.airspeed, alpha = state.alpha, beta = state.beta;
			Vector3 v_dir = state.v_dir;
			Vector3 vel = state.velocity;
			float airspeed_sq = v * v;

			AircraftPhysics physics = aircraft.physics;
			Vector3 aircraft_cg = physics.current_cg;

			// actual useful results
			Vector3 sum_total = Vector3.zero, sum_lift = Vector3.zero, sum_drag = Vector3.zero, sum_torque = Vector3.zero;
			Airfoil[] foils = aircraft.airfoils;

			SimForces forces = new SimForces { root_position = aircraft.root.transform.position };
			
			foreach (Part part in aircraft.parts)
			{
				GameObject go = part.part_object;
				Transform lift_transform = part.lift_transform;

				// Pull a bunch of properties
				//float wing_area = s_part.FindProperty("wingArea").floatValue;
				//float drag_area = s_part.FindProperty("dragArea").floatValue;
				//float airflow_channeling = s_part.FindProperty("airflowChanneling").floatValue;
				//Vector3 center_of_lift = s_part.FindProperty("centerOfLift").vector3Value;
				//int foil_id = s_part.FindProperty("airfoil").intValue;

				PartPhysics part_phys = part.current_physics;

				float wing_area = part_phys.wing_area;
				float drag_area = part_phys.drag_area;
				float airflow_channeling = part_phys.airflow_channeling;
				Vector3 center_of_lift = part_phys.center_of_lift;
				int foil_id = part_phys.foil_id;

				// Implementation below derived from ILSpy decomposition of AeroJob_Math

				Vector3 air_velocity = vel;
				float wing_effectiveness = 1;
				Vector3 force_total = Vector3.zero;

				// Channeling - redirect airflow in direction of gameobject's transform.Forward()
				if (airflow_channeling > 0f)
				{
					Vector3 target = part.transform.forward;//transformValues.GetReadOnlyRef(otherTransformIndex).Forward();
					air_velocity = Vector3.RotateTowards(air_velocity, target, airflow_channeling, 0f);
				}
				// Alpha, coefficients
				Vector3 local_velocity = Quaternion.Inverse(lift_transform.rotation) * air_velocity;
				Vector3 normalized = Vector3.Cross(air_velocity, lift_transform.right).normalized;
				float local_alpha = Mathf.Atan2(local_velocity.y, local_velocity.z);
				(float lift_coef, float drag_coef) = foil_coefficients(ref foils, foil_id, local_alpha);

				// Drag
				float residual_drag_area = (drag_area + wing_area * 0.1f) * (1f - wing_effectiveness);
				float parasitic_drag = 0.5f * rho * airspeed_sq * 0.5f * (drag_area + residual_drag_area);
				float wing_drag = drag_coef * rho * airspeed_sq * 0.5f * wing_area * wing_effectiveness;
				Vector3 force_drag = -air_velocity.normalized * (wing_drag + parasitic_drag);

				// Transonic drag bump
				float airspeed = air_velocity.magnitude;
				if (airspeed > mach * 0.8f && airspeed < mach * 1.2f)
				{
					float bump_width = 0.2f;
					float bump_gain = 0.15f;
					float mach_offset = Mathf.Min(Mathf.Abs((mach - airspeed) / mach), bump_width);
					float k = (bump_width - mach_offset) / bump_width;
					force_drag *= 1f + k * k * k * bump_gain;
				}

				// Handle control surfaces
				// Technically shouldn't be here, this is handled outside of AeroPart simulation, but it's probably fine
				if (part.control_surface != null)
				{
					ControlSurface surface = part.control_surface;
					if (surface.split_amount != 0)
					{
						Vector3 surface_drag = v_dir * (-1f * surface.split_amount * surface.split_drag * rho * airspeed_sq);
						force_drag += surface_drag;
					}
				}

				// Final forces
				Vector3 force_lift = -normalized * (lift_coef * rho * airspeed_sq * 0.5f * wing_area * wing_effectiveness);
				force_total += force_lift + force_drag;

				// NaN guard
				if (!float.IsFinite(force_total.x) || !float.IsFinite(force_total.y) || !float.IsFinite(force_total.z))
				{
					force_total = Vector3.zero;
					Debug.LogWarning($"Out-of-bounds force while simulating AeroPart: {go.name}", go);
				}

				// Results
				if (force_total != Vector3.zero)
				{
					Vector3 part_torque = Vector3.zero;
					if (center_of_lift != Vector3.zero)
					{
						Vector3 lift_arm = lift_transform.rotation * center_of_lift;
						part_torque += Vector3.Cross(force_total, -lift_arm);
						if (log_spam) Debug.Log($"{go.name} | CoL: {center_of_lift}, arm: {lift_arm}, force: {force_total}, torque: {part_torque}", go);
					}

					Vector3 part_cg = part.world_cg;
					Vector3 part_arm = part_cg - aircraft_cg;
					part_torque += Vector3.Cross(part_arm, force_total);
					if (log_spam) Debug.Log($"{go.name}: A{part_arm}, L{force_lift}, D{force_drag}, T{part_torque}", go);

					sum_total += force_total; sum_lift += force_lift; sum_drag += force_drag; sum_torque += part_torque;

					// maybe torque should only include torque acting on the actual part, like from center_of_lift being nonzero?
					forces.add(part_cg, force_total, force_lift, force_drag, part_torque);
				}
			}

			return new SimResults { total = sum_total, lift = sum_lift, drag = sum_drag, torque = sum_torque, forces = forces };
		}
	}

//	// component to be added to the aircraft container
//	public class SimulationRootComponent : MonoBehavior
//	{
//		
//	}

	public static class SimEnvironment
	{
		const string sim_scene_path = "Assets/_hex/Editor/Simulator.unity";
		const string container_name = "aircraft_root";
		const string container_scene_path = "/aircraft_root";

		public static bool is_in_sim_scene = false; // safety check for functionality that will modify the aircraft


		public static Scene get_or_open()
		{
			var scene = SceneManager.GetSceneByPath(sim_scene_path);
			if (scene.IsValid() && scene.isLoaded) return scene;

			if (!System.IO.File.Exists(sim_scene_path))
			{
				System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(sim_scene_path));
				var created = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
				EditorSceneManager.SaveScene(created, sim_scene_path);
				return created;
			}
			return EditorSceneManager.OpenScene(sim_scene_path, OpenSceneMode.Single);
		}
		// close with EditorSceneManager.CloseScene(scene, true);


		// loads scene, removes existing aircraft (!) (if present), instantiates new one
		public static (GameObject container, GameObject aircraft, string status) setup_wind_tunnel(GameObject aircraft_prefab, bool remove_existing)
		{
			Scene wind_tunnel_scene = get_or_open();
			GameObject container = GameObject.Find(container_scene_path);

			if (container != null) // already existed, so we should check for an existing aircraft here
			{
				(GameObject existing_aircraft, GameObject prefab) = get_aircraft_in_tunnel();
				if (existing_aircraft != null)
				{
					if (remove_existing) UnityEngine.Object.DestroyImmediate(existing_aircraft);
					else return (null, null, "Aircraft already in wind tunnel scene");
				}
			}
			else container = new GameObject(container_name);

			GameObject new_aircraft = (GameObject)PrefabUtility.InstantiatePrefab(aircraft_prefab, wind_tunnel_scene);
			new_aircraft.transform.SetParent(container.transform);

			return (container, new_aircraft, "Loaded successfully");
		}


		public static GameObject spawn_prefab(GameObject prefab)
		{
			var scene = get_or_open();
			return (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
		}


		// tries to find an aircraft if we're currently in the wind tunnel
		public static (GameObject root, GameObject prefab) get_aircraft_in_tunnel()
		{
			if (EditorSceneManager.GetActiveScene().path != sim_scene_path) return (null, null);
			
			var container = GameObject.Find(container_scene_path);
			if (container == null) return (null, null);

			var aircraft = container.GetComponentInChildren(game_type("Aircraft"));
			GameObject prefab = null;
			if (aircraft != null && aircraft.gameObject != null) {
				prefab = AircraftUtils.get_prefab_from_root(aircraft.gameObject);
			}
			return (aircraft != null ? aircraft.gameObject : null, prefab);
		}


		// check if the aircraft is in the designated wind tunnel environment to avoid modifying aircraft templates
		public static bool check_obj_in_sim(GameObject obj_to_check)
			=> obj_to_check != null && obj_to_check.scene.IsValid() && obj_to_check.scene.path == sim_scene_path;


		// check if the aircraft is in the designated wind tunnel environment to avoid modifying aircraft templates
		public static bool check_in_sim_scene(AircraftData aircraft) => check_obj_in_sim(aircraft.root);


		public static GameObject check_in_prefab_no_dialog()
		{
			var stage = PrefabStageUtility.GetCurrentPrefabStage();
			if (stage == null || !stage.prefabContentsRoot)
			{
				return null;
			}
			return stage.prefabContentsRoot;
		}


		public static GameObject check_if_prefab_is_aircraft()
		{
			GameObject prefab_root = check_in_prefab_no_dialog();
			if (prefab_root != null) return prefab_root.GetComponent(game_type("Aircraft")) != null ? prefab_root : null;
			return null;
		}


		// check if we're in a prefab, for non-invasive simulations (and cg checks)
		public static GameObject check_in_prefab(string tool_name)
		{
			var stage = PrefabStageUtility.GetCurrentPrefabStage();
			if (stage == null || !stage.prefabContentsRoot)
			{
				EditorUtility.DisplayDialog("Prefab only", $"{tool_name} can only be used in prefabs.", "OK");
				return null;
			}
			return stage.prefabContentsRoot;
		}
	}
}
