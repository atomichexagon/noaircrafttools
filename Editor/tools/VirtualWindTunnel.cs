#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

using static hexagonest.Tools.Utilities;


namespace hexagonest.Tools
{
	// used by AircraftSelector
	public class WindTunnel_Target : AircraftSelector.Target
	{
		public override UnityEngine.Object aircraft_definition { get; set; }
		public override GameObject aircraft_prefab { get; set; }
		public override Type target_window => typeof(WindTunnel);
		public override string target_name => "Wind Tunnel";
		public static WindTunnel_Target instance = new WindTunnel_Target();
		private WindTunnel_Target() {}
	}

	public class WindTunnel : EditorWindow
	{
		static UnityEngine.Object aircraft_definition = null;
		static GameObject current_prefab; // aircraft template - don't operate on this!

		static GameObject current_container; // can be null, only relevant in wind tunnel scene
		static GameObject current_aircraft_object; // current working object
		static AircraftData current_aircraft;

		static bool in_wind_tunnel = false; // some features disabled in prefabs to avoid potentially messing with them

		enum simulation_mode {
			angle_sweep, // choose between alpha or beta
			mach_sweep, // unimplemented
			static_stability, // find aircraft's aerodynamic neutral point/static margin
		};

		static readonly string[] mode_names = {
			"Angle sweep",
			"Mach sweep",
			"Static stability"
		};

		static simulation_mode current_mode = simulation_mode.angle_sweep;

		enum angle_mode { alpha, beta };
		static readonly string[] angle_mode_names = { "Alpha", "Beta" };
		static angle_mode angle_sweep_mode = angle_mode.alpha;

		// rerun simulation when parameters are changed
		static bool simulate_on_change = true;

		// direct controls
		static ControlInputsPanel input_panel = new ControlInputsPanel();
		static float last_gui_time = 0f; // for surface animation
		static bool animate_surfaces = true; // move surfaces in real-time instead of instantly(ish)

		// aircraft configuration
		static bool show_aircraft_cfg = false;
		static float fuel_level = 30f;

		// state tracking for inputs
		static bool using_airspeed = true; // true if airspeed (m/s or converted) was last changed, false if mach was last changed

		// simulation parameters
		static ValueWithUnit airspeed_min = new ValueWithUnit("Airspeed", "speed", 100f, "m/s", "km/h");
		static ValueWithUnit airspeed_max = new ValueWithUnit("Airspeed", "speed", 200f, "m/s", "km/h");
		static ValueWithUnit airspeed_fixed = new ValueWithUnit("Airspeed", "speed", 100f, "m/s", "km/h");
		
		static ValueWithUnit altitude_min = new ValueWithUnit("Altitude", "distance", 0f, "m", "ft");
		static ValueWithUnit altitude_max = new ValueWithUnit("Altitude", "distance", 2500f, "m", "ft");
		static ValueWithUnit altitude_fixed = new ValueWithUnit("Altitude", "distance", 50f, "m", "ft");

		static ValueWithUnit mach_min = new ValueWithUnit("Mach", "speed", 0.8f, "m/s", "km/h");
		static ValueWithUnit mach_max = new ValueWithUnit("Mach", "speed", 1.2f, "m/s", "km/h");
		static ValueWithUnit mach_fixed = new ValueWithUnit("Mach", "speed", 0.3f, "m/s", "km/h");

		static float alpha_min = -45f;
		static float alpha_max = 45f;
		static float alpha_fixed = 5f;

		static float beta_min = -45f;
		static float beta_max = 45f;
		static float beta_fixed = 0f;

		static int simulation_steps = 81;
		static bool simulating = false; // to lock out other behaviors while a sim is running

		// display
		static List<graph_series> simulation_series = new List<graph_series>();
		static bool has_simulation_data = false;
		//static bool graph_updated = false;

		static Rect? graph_rect;

		// status shenanigans
		static string last_simenv_status = "";
		static bool tunnel_collision_check = false;

		static void find_aircraft()
		{
			GameObject prefab_root = SimEnvironment.check_in_prefab("Wind tunnel");
			if (prefab_root == null) return;

			AircraftData aircraft = AircraftUtils.get_data_from_prefab(prefab_root);

			if (prefab_root != null && aircraft != null)
			{
				current_aircraft = aircraft;
				fuel_level = aircraft.fuel_fraction;
			}
		}

		// effectively replaces load_aircraft() but intentionally loading now
		static void load_aircraft(GameObject aircraft_root, GameObject prefab_used)
		{
			current_aircraft = new AircraftData(aircraft_root);
			current_aircraft_object = aircraft_root;
			current_prefab = prefab_used;

			fuel_level = current_aircraft.fuel_fraction;

			tunnel_collision_check = false;
		}

		[MenuItem("HexTools/Wind tunnel")]
		static void open()
		{
			AircraftSelector.open_with_target(WindTunnel_Target.instance);
			//find_aircraft();
			//if (current_aircraft != null)
			//{
			//	var window = GetWindow<WindTunnel>("Wind Tunnel");
			//	window.minSize = new Vector2(600f, 400f);
			//}
		}

		private bool aircraft_panel(AircraftData aircraft)
		{
			bool value_changed = false;
			EditorGUILayout.BeginVertical("GroupBox");
			EditorGUILayout.LabelField("Aircraft configuration", EditorStyles.boldLabel);

			EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField("Aircraft", GUILayout.Width(100f));
				EditorGUILayout.LabelField(aircraft.param.FindProperty("aircraftName").stringValue, GUILayout.Width(100f));
			EditorGUILayout.EndHorizontal();
			
			EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField("Dry mass", GUILayout.Width(100f));
				EditorGUILayout.LabelField($"{aircraft.physics.dry_mass}kg", GUILayout.Width(120f));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField("Fuel load", GUILayout.Width(100f));
				EditorGUILayout.LabelField($"{aircraft.physics.fuel_mass}kg / {aircraft.physics.max_fuel_mass}kg", GUILayout.Width(120f));
				EditorGUI.BeginChangeCheck();
				fuel_level = EditorGUILayout.Slider(fuel_level, 0f, 100f);
				if (EditorGUI.EndChangeCheck())
				{
					aircraft.set_fuel_fraction(fuel_level / 100f);
					value_changed = true;
				}
			EditorGUILayout.EndHorizontal();
			
			EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField("Current mass", GUILayout.Width(100f));
				EditorGUILayout.LabelField($"{aircraft.physics.current_mass}kg", GUILayout.Width(120f));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginVertical("GroupBox");
			show_aircraft_cfg = EditorGUILayout.BeginFoldoutHeaderGroup(show_aircraft_cfg, "Hardpoint selection");
			if (show_aircraft_cfg)
			{
				foreach (HardpointGroup group in aircraft.hardpoint_manager.hardpoint_groups)
				{
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.LabelField($"{group.name}", GUILayout.Width(160f));
					EditorGUILayout.LabelField($"{group.mass}kg", GUILayout.Width(120f), GUILayout.ExpandWidth(true));
					EditorGUI.BeginChangeCheck();
					int new_selection = EditorGUILayout.Popup(group.currently_mounted, group.option_names, GUILayout.Width(240f), GUILayout.ExpandWidth(true));
					if (EditorGUI.EndChangeCheck())
					{
						aircraft.hardpoint_manager.set_group_mounted_option(group.manager_index, new_selection);
						value_changed = true;
					}
					EditorGUILayout.EndHorizontal();
				}
			}
			EditorGUILayout.EndVertical();

			EditorGUILayout.EndFoldoutHeaderGroup();
			EditorGUILayout.EndVertical();

			return value_changed;
		}

		private void OnGUI()
		{
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			//if (GUILayout.Button("Reset", EditorStyles.toolbarButton))
			//{
			//	Debug.Log("Reset");
			//}
			simulate_on_change = GUILayout.Toggle(simulate_on_change, "Run on change", EditorStyles.toolbarButton);
			animate_surfaces = GUILayout.Toggle(animate_surfaces, "Animate surfaces", EditorStyles.toolbarButton);
			GUILayout.FlexibleSpace();
			GUILayout.Label(last_simenv_status, "ToolbarLabel");
			GUILayout.Label(in_wind_tunnel ? "Wind tunnel" : "Prefab (some features disabled)", "ToolbarLabel");
			
			using (new EditorGUI.DisabledScope(current_aircraft == null))
			{
				if (GUILayout.Button("Mode select", EditorStyles.toolbarButton))
				{
					current_aircraft = null;
				}
			}
			EditorGUILayout.EndHorizontal();

			var selector_data = WindTunnel_Target.instance;
			GameObject selected_prefab = selector_data.aircraft_prefab;

			if (selected_prefab != null && selected_prefab != current_prefab)
			{
				current_aircraft = null;
			}

			if (current_aircraft == null) {
				if (selected_prefab != null)
				{
					EditorGUILayout.LabelField($"An aircraft ({selected_prefab.name}) is available.");
					EditorGUILayout.Space();

					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.LabelField("Bring to wind tunnel scene (will replace whatever's there)");
					if (GUILayout.Button(!tunnel_collision_check ? "Test in wind tunnel scene" : "Replace aircraft currently in tunnel", "AC Button"))
					{
						if (tunnel_collision_check) EditorGUILayout.LabelField("An aircraft was found in the wind tunnel. Click again to replace it. (Irreversible!)");
						(GameObject container, GameObject new_aircraft, string status) = SimEnvironment.setup_wind_tunnel(selected_prefab, tunnel_collision_check);
						last_simenv_status = status;
						if (new_aircraft != null) {
							load_aircraft(new_aircraft, selected_prefab);
							new_aircraft.transform.localPosition = -current_aircraft.physics.dry_cg;
						} else tunnel_collision_check = true;
					}
					EditorGUILayout.EndHorizontal();

					EditorGUILayout.Space();
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.LabelField("Test in prefab (visualizations will be unavailable)");
					if (GUILayout.Button("Test in prefab", "AC Button"))
					{
						bool is_in_prefab = (SimEnvironment.check_in_prefab_no_dialog() == selected_prefab);
						if (!is_in_prefab) AssetDatabase.OpenAsset(selected_prefab);
						last_simenv_status = "";
						load_aircraft(selected_prefab, selected_prefab);
					}
					EditorGUILayout.EndHorizontal();
				}
				else
				{
					EditorGUILayout.LabelField("Selecting an aircraft seems to have gone wrong.");
					EditorGUILayout.LabelField("Close this window and reopen it via the toolbar.");

					GameObject we_might_be_in_a_prefab_anyway = SimEnvironment.check_if_prefab_is_aircraft();
					if (we_might_be_in_a_prefab_anyway != null)
					{
						if (GUILayout.Button("Or just use the prefab you're in", "AC Button"))
							load_aircraft(we_might_be_in_a_prefab_anyway, we_might_be_in_a_prefab_anyway);
					}

					GameObject we_might_also_be_in_the_tunnel = SimEnvironment.get_aircraft_in_tunnel();
					if (we_might_also_be_in_the_tunnel != null)
					{
						GameObject prefab_from_root = AircraftUtils.get_prefab_from_root(we_might_also_be_in_the_tunnel);
						if (GUILayout.Button("Use aircraft currently in tunnel", "AC Button"))
							load_aircraft(we_might_also_be_in_the_tunnel, prefab_from_root);
							// TODO: grab previous inputs and replace input_panel?
					}
				}
				return;
			}
			in_wind_tunnel = SimEnvironment.check_in_sim_scene(current_aircraft);
			SimEnvironment.is_in_sim_scene = in_wind_tunnel;

			float time_now = Time.realtimeSinceStartup;
			float gui_dt = time_now - last_gui_time;
			last_gui_time = time_now;

			current_mode = (simulation_mode)GUILayout.Toolbar((int)current_mode, mode_names);

			bool cfg_changed = aircraft_panel(current_aircraft);

			EditorGUILayout.BeginVertical("GroupBox");
			EditorGUILayout.LabelField("Simulation parameters", EditorStyles.boldLabel);

			bool ctrls_changed = false;
			EditorGUILayout.BeginVertical("GroupBox");
			using (new EditorGUI.DisabledScope(!in_wind_tunnel))
			{
				ctrls_changed = input_panel.present(in_wind_tunnel);
			}
			EditorGUILayout.EndVertical();
			bool surfaces_moved = false;
			if (ctrls_changed || (animate_surfaces && !simulating))
				surfaces_moved = current_aircraft.set_controls(input_panel.inputs_obj, animate_surfaces ? gui_dt : 100f);

			bool params_changed = ctrls_changed || cfg_changed;
			switch (current_mode)
			{
				case simulation_mode.angle_sweep:
					(float rho, float mach) = Simulation.isa_atm(altitude_fixed.current_value);

					bool airspeed_changed = airspeed_fixed.present();
					if (airspeed_changed) { mach_fixed.update_value(airspeed_fixed.current_value / mach); using_airspeed = true; }

					bool mach_changed = mach_fixed.present_one("M", mach);
					if (mach_changed) { airspeed_fixed.update_value(mach_fixed.current_value * mach); using_airspeed = false; }

					bool altitude_changed = altitude_fixed.present();
					if (altitude_changed)
					{
						var atmos = Simulation.isa_atm(altitude_fixed.current_value);
						if (using_airspeed) mach_fixed.update_value(airspeed_fixed.current_value / atmos.mach);
						else airspeed_fixed.update_value(mach_fixed.current_value * atmos.mach);
					}

					//EditorGUILayout.BeginHorizontal();
					//EditorGUILayout.LabelField("Air density", $"{rho}kg/m³");
					//EditorGUILayout.LabelField("Speed of sound", $"{mach}m/s");
					//EditorGUILayout.LabelField("Dyn. pressure", $"{0.5 * rho * (airspeed_fixed.current_value * airspeed_fixed.current_value)}Pa");
					//EditorGUILayout.EndHorizontal();

					params_changed = params_changed || airspeed_changed || mach_changed || altitude_changed || cfg_changed;
					//EditorGUILayout.BeginHorizontal();
					//EditorGUILayout.LabelField("Sweep mode");
					//angle_sweep_mode = (angle_mode)GUILayout.Toolbar((int)angle_sweep_mode, angle_mode_names);
					//EditorGUILayout.EndHorizontal();
					angle_sweep_mode = (angle_mode)EditorGUILayout.EnumPopup("Sweep mode", angle_sweep_mode);
					bool alpha_changed = false, beta_changed = false;
					switch (angle_sweep_mode)
					{
						case angle_mode.alpha:
							alpha_changed = UI.present_two("Alpha range", ref alpha_min, "° min", ref alpha_max, "° max");
							if (alpha_changed) params_changed = true;
							beta_changed = UI.present_one("Fixed beta", ref beta_fixed, "°");
							if (beta_changed) params_changed = true;
							break;
						case angle_mode.beta:
							beta_changed = UI.present_two("Beta range", ref beta_min, "° min", ref beta_max, "° max");
							if (beta_changed) params_changed = true;
							alpha_changed = UI.present_one("Fixed alpha", ref alpha_fixed, "°");
							if (alpha_changed) params_changed = true;
							break;
						default:
							EditorGUILayout.HelpBox($"{angle_sweep_mode} unrecognized", MessageType.Info);
							break;
					}

					//alpha_min = EditorGUILayout.FloatField("Min alpha (deg)", alpha_min);
					//alpha_max = EditorGUILayout.FloatField("Max alpha (deg)", alpha_max);
					break;
				default:
					EditorGUILayout.HelpBox($"{current_mode} not implemented yet.", MessageType.Info);
					break;
			}

			EditorGUI.BeginChangeCheck();
			simulation_steps = EditorGUILayout.IntSlider("Steps", simulation_steps, 5, 500);
			if (EditorGUI.EndChangeCheck()) params_changed = true;

			if ((simulate_on_change && params_changed) || (animate_surfaces && surfaces_moved && !simulating))
			{
				run_simulation();
			}

			//using (new EditorGUI.DisabledScope(current_mode == null))
			//{
				if (GUILayout.Button("Simulate", GUILayout.Height(28)))
					run_simulation();
			//}
			EditorGUILayout.EndVertical();
			//EditorGUILayout.Space();

			graph_rect = GUILayoutUtility.GetRect(100, 100, 250, 10000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
			EditorGUI.DrawRect((Rect)graph_rect, new Color(0.15f, 0.15f, 0.15f));

			update_results();


			//airspeed_fixed = EditorGUILayout.DelayedFloatField("(m/s)", airspeed_fixed);
			//altitude_fixed = EditorGUILayout.DelayedFloatField("Altitude (m):", altitude_fixed);
		}


		private void update_results()
		{
			(float x_min, float x_max, float y_min, float y_max) = UI.autofit_series(simulation_series);
			if (graph_rect != null && has_simulation_data) UI.draw_graph2((Rect)graph_rect, simulation_series, float.NaN, float.NaN, float.NaN, float.NaN); //x_min, x_max, y_min, y_max
			//if (graph_updated) {
			//	Debug.Log($"Graph: xmin {x_min}, xmax {x_max}, ymin {y_min}, ymax {y_max}");
			//}
			//graph_updated = false;
			this.Repaint();
		}


		private void run_simulation()
		{
			simulating = true;
			//find_aircraft();
			if (current_aircraft == null) { Debug.LogError("No aircraft selected, or no prefab available"); return; }

			List<(string name, List<Vector2> points, Color color)> new_series = new List<(string name, List<Vector2> points, Color color)>();

			switch(current_mode)
			{
				case simulation_mode.angle_sweep:
					float speed = airspeed_fixed.current_value;
					float alt = altitude_fixed.current_value;

					float alpha_step = (alpha_max - alpha_min) / (simulation_steps - 1);
					float beta_step = (beta_max - beta_min) / (simulation_steps - 1);

					// lift is purely y-axis, drag purely -z, torque uhh.. x axis only?
					List<Vector2> lift_series = new List<Vector2>(), drag_series = new List<Vector2>();
					List<Vector2> torque_pitch_series = new List<Vector2>(), torque_yaw_series = new List<Vector2>(), torque_roll_series = new List<Vector2>();

					for (int i = 0; i < simulation_steps; i++)
					{
						float this_alpha = angle_sweep_mode == angle_mode.alpha ? (alpha_min + i * (alpha_step)) : alpha_fixed;
						float this_beta = angle_sweep_mode == angle_mode.beta ? (beta_min + i * (beta_step)) : beta_fixed;
						(Vector3 total, Vector3 lift, Vector3 drag, Vector3 torque) = Simulation.aircraft_forces((AircraftData)current_aircraft, speed, alt, this_alpha * Mathf.Deg2Rad, this_beta * Mathf.Deg2Rad);
						
						float result_index = angle_sweep_mode == angle_mode.alpha ? this_alpha : this_beta;

						lift_series.Add(new Vector2(result_index, total.y));
						drag_series.Add(new Vector2(result_index, drag.magnitude));
						torque_pitch_series.Add(new Vector2(result_index, torque.x));
						torque_yaw_series.Add(new Vector2(result_index, torque.y));
						torque_roll_series.Add(new Vector2(result_index, torque.z));
					}
					
					new_series.Add((name: "Lift", points: lift_series, color: new Color(0.2f, 0.2f, 1.0f)));
					new_series.Add((name: "Drag", points: drag_series, color: new Color(1.0f, 0.2f, 0.2f)));
					new_series.Add((name: "T (pitch, +down)", points: torque_pitch_series, color: new Color(0.2f, 1.0f, 0.2f)));
					new_series.Add((name: "T (yaw, +right)", points: torque_yaw_series, color: new Color(0.2f, 1.0f, 0.8f)));
					new_series.Add((name: "T (roll, +right)", points: torque_roll_series, color: new Color(0.8f, 1.0f, 0.2f)));
					
					break;
				default:
					Debug.Log($"run_simulation() doesn't know how to simulate {current_mode}.");
					break;
			}

			List<graph_series> series_compiled = new List<graph_series>();

			Dictionary<string, bool> was_visible = new Dictionary<string, bool>();
			if (has_simulation_data)
			{
				foreach (graph_series series in simulation_series)
				{
					was_visible[series.name] = series.visible;
				}
			}

			foreach ((string name, List<Vector2> points, Color color) series in new_series)
			{
				series_compiled.Add(new graph_series {
					name = series.name,
					points = series.points.ToArray(),
					color = series.color,
					visible = was_visible.ContainsKey(series.name) ? was_visible[series.name] : true
				});
			}

			(float x_min, float x_max, float y_min, float y_max) = UI.autofit_series(series_compiled);

			float one_g_force = ((AircraftData)current_aircraft).physics.current_mass * Constants.G;
			graph_series one_g_line = new graph_series {
				name = "1G",
				color = Color.white,
				points = new Vector2[2]{ new Vector2(x_min, one_g_force), new Vector2(x_max, one_g_force) },
				visible = was_visible.ContainsKey("1G") ? was_visible["1G"] : true
			};
			series_compiled.Add(one_g_line);

			simulation_series = series_compiled;
			has_simulation_data = true;
			//graph_updated = true;

			//update_results();
			simulating = false;
		}

		[MenuItem("HexTools/Stability test")]
		private static void stability_test()
		{
			GameObject prefab_root = SimEnvironment.check_in_prefab("Stability test"); if (prefab_root == null) return;
			AircraftData aircraft = AircraftUtils.get_data_from_prefab(prefab_root); AircraftPhysics physics = aircraft.physics;

			(Vector3 total, Vector3 lift, Vector3 drag, Vector3 torque) = Simulation.aircraft_forces(aircraft, 100, 50, 5 * Mathf.Deg2Rad, 0);
			Debug.Log($"Aircraft mass: {physics.dry_mass}, CG: {physics.dry_cg}");
			Debug.Log($"Total force: {total}, lift: {lift}, drag: {drag}");
			Debug.Log($"Lift/weight: {lift / (physics.dry_mass * Constants.G)}");
			Debug.Log($"Aerodynamic torque: {torque}");
		}
	}
}


#endif