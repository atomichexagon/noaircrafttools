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
	public class WindTunnel : EditorWindow
	{
		static AircraftData? current_aircraft = null;

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

		// aircraft configuration
		static bool show_aircraft_cfg = false;
		static float fuel_level = 30f;

		// state tracking for inputs
		static bool using_airspeed = true, using_mach = false;

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

		//static float mach_min = 0.8f;
		//static float mach_max = 1.2f;
		//static float mach_fixed = 0.9f;

		static float alpha_min = -45f;
		static float alpha_max = 45f;
		static float alpha_fixed = 5f;

		static float beta_min = -45f;
		static float beta_max = 45f;
		static float beta_fixed = 0f;


		static int simulation_steps = 81;

		static List<graph_series> simulation_series = new List<graph_series>();
		static bool has_simulation_data = false;
		static bool graph_updated = false;

		static Rect? graph_rect;

		static void find_aircraft()
		{
			GameObject prefab_root = SimEnvironment.check_in_prefab("Wind tunnel");
			if (prefab_root == null) return;

			AircraftData? aircraft = AircraftUtils.get_data_from_prefab(prefab_root);

			if (prefab_root != null && aircraft != null)
			{
				current_aircraft = aircraft;
				fuel_level = aircraft.fuel_fraction;
			}
		}

		[MenuItem("HexTools/Wind tunnel")]
		static void open()
		{
			find_aircraft();

			if (current_aircraft != null)
			{
				var window = GetWindow<WindTunnel>("Wind Tunnel");
				window.minSize = new Vector2(600f, 400f);
			}
		}

		private void aircraft_panel(AircraftData aircraft)
		{
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
				if (EditorGUI.EndChangeCheck()) aircraft.set_fuel_fraction(fuel_level / 100f);
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
					if (EditorGUI.EndChangeCheck()) aircraft.hardpoint_manager.set_group_mounted_option(group.manager_index, new_selection);
					EditorGUILayout.EndHorizontal();
				}
			}
			EditorGUILayout.EndVertical();

			EditorGUILayout.EndFoldoutHeaderGroup();
			EditorGUILayout.EndVertical();
		}

		static UnityEngine.Object aircraft_def;

		private void OnGUI()
		{
			aircraft_def = EditorGUILayout.ObjectField("Aircraft template", aircraft_def, game_type("AircraftDefinition"), false);
			if (current_aircraft == null) {
				EditorGUILayout.HelpBox("No aircraft loaded", MessageType.Info);

				if (GUILayout.Button("Check again", GUILayout.Height(28)))
					find_aircraft();
				return;
			}
			AircraftData aircraft = (AircraftData)current_aircraft;

			//current_mode = EditorGUILayout.Popup(current_mode, modes);
			current_mode = (simulation_mode)GUILayout.Toolbar((int)current_mode, mode_names);
			//EditorGUILayout.Space();

			aircraft_panel(aircraft);
			
			//EditorGUILayout.Space();

			EditorGUILayout.BeginVertical("GroupBox");
			EditorGUILayout.LabelField("Simulation parameters", EditorStyles.boldLabel);
			switch (current_mode)
			{
				case simulation_mode.angle_sweep:
					(float rho, float mach) = Simulation.isa_atm(altitude_fixed.current_value);

					bool airspeed_changed = airspeed_fixed.present();
					if (airspeed_changed) { mach_fixed.update_value(airspeed_fixed.current_value / mach); using_airspeed = true; using_mach = false; }

					bool mach_changed = mach_fixed.present_one("M", mach);
					if (mach_changed) { airspeed_fixed.update_value(mach_fixed.current_value * mach); using_airspeed = false; using_mach = true;  }

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

					bool params_changed = false;
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
					simulation_steps = EditorGUILayout.IntSlider("Steps", simulation_steps, 5, 500);
					break;
				default:
					EditorGUILayout.HelpBox($"{current_mode} not implemented yet.", MessageType.Info);
					break;
			}

			using (new EditorGUI.DisabledScope(current_mode == null))
			{
				if (GUILayout.Button("Simulate", GUILayout.Height(28)))
					run_simulation();
			}
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
			graph_updated = false;
			this.Repaint();
		}


		private void run_simulation()
		{
			find_aircraft();
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
			graph_updated = true;

			update_results();
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