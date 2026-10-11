#if UNITY_EDITOR
using System;
using System.Linq;
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



	// Author's note: I'm sorry in advance if you're good with C#. I imagine reading this will be a form of torture.
	public class WindTunnel : EditorWindow
	{
		static UnityEngine.Object aircraft_definition = null;
		static GameObject current_prefab; // aircraft template - don't operate on this!

		static GameObject current_container; // can be null, only relevant in wind tunnel scene
		static GameObject current_aircraft_object; // current working object
		static AircraftData current_aircraft;

		static bool in_wind_tunnel = false; // some features disabled in prefabs to avoid potentially messing with them

		// TODO: investigate possibility of refactoring things to move mode-specific functionality into WindTunnelMode
		// using before_sim_step and sim_step to allow for more complex simulations without bloating run_simulation()
		// maybe overkill, but it'd also allow others (that's you) to more easily expand functionality to fit their use cases
		// and the goal is for this tool to be useful so.. may as well

		//[Serializable]
		//public struct sim_parameters { float speed, altitude, alpha, beta };

		public class WindTunnelMode
		{
			public string sim_type, name, description;
			public bool controls_enabled; // aircraft controls
			public Dictionary<string, bool> parameters_shown;
		
			// the idea is that WindTunnel calls this before a step, and it outputs the parameters to feed into aircraft_forces()
			//public delegate sim_parameters before_sim_step();

			// and then this could define behavior that isn't just a one-shot calculation of that specific case, e.g. calculating trim
			//public delegate void sim_step();
		
			public WindTunnelMode(string _sim_type, string _name, string _description, bool _controls_enabled, string[] _parameters_shown)
			{
				sim_type = _sim_type; name = _name; description = _description; controls_enabled = _controls_enabled;
				parameters_shown = _parameters_shown.ToDictionary(param => param, param => true);
			}
		}

		// TODO: show descriptions somewhere
		static WindTunnelMode[] modes_available = new WindTunnelMode[] {
			new WindTunnelMode ("alpha_sweep", "Alpha sweep",
				"Sweeps alpha angles, fixed airspeed/altitude/beta. Calculates forces, L/D ratio, and aerodynamic moments.",
				true, new string[] { "airspeed_fixed", "altitude_fixed", "alpha_range", "beta_fixed" }
			),
			new WindTunnelMode ("beta_sweep", "Beta sweep",
				"Sweeps beta angles, fixed airspeed/altitude/alpha. Calculates forces, L/D ratio, and aerodynamic moments.",
				true, new string[] { "airspeed_fixed", "altitude_fixed", "beta_range", "alpha_fixed" }
			),
			new WindTunnelMode ("trim_sweep", "Trim sweep",
				"Sweeps alpha angles and finds zero-moment elevator input, fixed airspeed/altitude/beta. Calculates forces, L/D ratio, and aerodynamic moments.",
				true, new string[] { "airspeed_fixed", "altitude_fixed", "alpha_range", "beta_fixed" }
			),
			new WindTunnelMode ("deflection_sweep", "Deflection sweep",
				"Sweeps control range, fixed airspeed/altitude/alpha/beta. Calculates forces, L/D ratio, and aerodynamic moments.",
				true, new string[] { "airspeed_fixed", "altitude_fixed", "alpha_fixed", "beta_fixed" }
			),
		};

		static GUIContent[] mode_names = modes_available.Select(mode => new GUIContent(mode.name, mode.description)).ToArray();
		static WindTunnelMode current_mode = modes_available[0]; //simulation_mode.angle_sweep;
		static int current_mode_index = 0;

		// rerun simulation when parameters are changed
		static bool simulate_on_change = true;

		// direct controls
		static ControlInputsPanel input_panel = new ControlInputsPanel();
		static bool update_queued = false; // if gui_dt isn't high enough, it'll activate this to update when possible
		static float target_update_time = 1f/60f; // rate limits aircraft updates
		static float last_update_time = 0f; // for surface animation
		static bool animate_surfaces = true; // move surfaces in real-time instead of instantly(ish)

		// aircraft configuration
		static bool show_aircraft_cfg = false;
		static float fuel_level = 30f;

		// simulation parameters
		static ValueWithUnit airspeed_min = new ValueWithUnit("Airspeed (min)", "speed", 100f, "m/s", "km/h");
		static ValueWithUnit airspeed_max = new ValueWithUnit("Airspeed (max)", "speed", 200f, "m/s", "km/h");
		static ValueWithUnit airspeed_fixed = new ValueWithUnit("Airspeed", "speed", 100f, "m/s", "km/h");

		static bool using_mach = true; // true if airspeed was last changed by setting mach directly
		static float airspeed_min_mach = 0.8f;
		static float airspeed_max_mach = 1.2f;
		static float airspeed_fixed_mach = 0.3f;
		
		static ValueWithUnit altitude_min = new ValueWithUnit("Altitude (min)", "distance", 0f, "m", "ft");
		static ValueWithUnit altitude_max = new ValueWithUnit("Altitude (max)", "distance", 2500f, "m", "ft");
		static ValueWithUnit altitude_fixed = new ValueWithUnit("Altitude", "distance", 50f, "m", "ft");

		// not linked to airspeed parameters
		static ValueWithUnit mach_min = new ValueWithUnit("Mach (min)", "speed", 0.8f, "m/s", "km/h");
		static ValueWithUnit mach_max = new ValueWithUnit("Mach (max)", "speed", 1.2f, "m/s", "km/h");
		static ValueWithUnit mach_fixed = new ValueWithUnit("Mach", "speed", 0.3f, "m/s", "km/h");

		static float alpha_min = -45f;
		static float alpha_max = 45f;
		static float alpha_fixed = 5f;

		static float beta_min = -45f;
		static float beta_max = 45f;
		static float beta_fixed = 0f;

		static AircraftState current_params = new AircraftState(airspeed_fixed, altitude_fixed, alpha_fixed, beta_fixed);

		static int simulation_steps = 81;
		static bool simulating = false; // to lock out other behaviors while a sim is running
		static bool was_updating = false; // allows smooth control movement without calling expensive .update() every frame

		// display
		static List<graph_series> simulation_series = new List<graph_series>();
		static bool has_simulation_data = false;
		//static bool graph_updated = false;

		static Rect? graph_rect;

		// status shenanigans
		static string last_simenv_status = "";
		static bool tunnel_collision_check = false;

		static void OnEnable()
		{
			var atmos = Simulation.isa_atm(altitude_fixed.current_value);
			airspeed_min_mach = airspeed_min.current_value / atmos.mach;
			airspeed_max_mach = airspeed_max.current_value / atmos.mach;
			airspeed_fixed_mach = airspeed_fixed.current_value / atmos.mach;

			this.minSize = new Vector2(580f, 800f);
		}

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

		enum aircraft_panel_mode {
			info,
			loadout,
			controls
		}

		static GUIContent[] aircraft_panel_mode_names = new GUIContent[] {
			new GUIContent("Info", "Basic information"),
			new GUIContent("Loadout", "Fuel, hardpoints"),
			new GUIContent("Controls", "Flight controls")
		};

		static aircraft_panel_mode panel_mode = aircraft_panel_mode.info;

		private (bool value_changed, bool ctrls_changed) aircraft_panel(AircraftData aircraft)
		{
			bool value_changed = false, ctrls_changed = false;
			EditorGUILayout.BeginVertical("GroupBox");
			EditorGUILayout.LabelField("Aircraft configuration", EditorStyles.boldLabel);

			panel_mode = (aircraft_panel_mode)GUILayout.Toolbar((int)panel_mode, aircraft_panel_mode_names, "LargeButton");

			switch(panel_mode)
			{
				case aircraft_panel_mode.info:
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.LabelField($"Aircraft: {aircraft.param.FindProperty("aircraftName").stringValue}", GUILayout.Width(200f));
					GUILayout.FlexibleSpace();
					EditorGUILayout.LabelField($"{Math.Round(aircraft.physics.dry_mass, 2)}kg dry mass", GUILayout.Width(150f));
					EditorGUILayout.EndHorizontal();
					break;
				case aircraft_panel_mode.loadout:
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.LabelField("Fuel load", GUILayout.Width(160f));
					EditorGUILayout.LabelField($"{aircraft.physics.fuel_mass}kg / {aircraft.physics.max_fuel_mass}kg", GUILayout.Width(120f));
					EditorGUI.BeginChangeCheck();
					fuel_level = EditorGUILayout.Slider(fuel_level, 0f, 100f);
					if (EditorGUI.EndChangeCheck())
					{
						aircraft.set_fuel_fraction(fuel_level / 100f);
						value_changed = true;
					}
					EditorGUILayout.EndHorizontal();

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
					break;
				case aircraft_panel_mode.controls:
					//EditorGUILayout.BeginVertical("GroupBox");
					using (new EditorGUI.DisabledScope(!in_wind_tunnel))
					{
						ctrls_changed = input_panel.present(in_wind_tunnel);
					}
					//EditorGUILayout.EndVertical();
					break;
				default: goto case aircraft_panel_mode.info;
			}
			EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
			// useful stats
			EditorGUILayout.BeginHorizontal();
			GUILayout.Label($"{Math.Round(aircraft.physics.current_mass, 2)}kg current");
			GUILayout.FlexibleSpace();
			GUILayout.Label($"{Math.Round(aircraft.physics.wing_area, 2)}m² wing");
			GUILayout.FlexibleSpace();
			GUILayout.Label($"{Math.Round(aircraft.physics.drag_area, 2)}m² drag");
			GUILayout.FlexibleSpace();
			GUILayout.Label($"{Math.Round(aircraft.physics.current_mass / aircraft.physics.wing_area, 2)}kg/m² loading");
			EditorGUILayout.EndHorizontal();

			// EditorGUILayout.BeginVertical("GroupBox");
			// show_aircraft_cfg = EditorGUILayout.BeginFoldoutHeaderGroup(show_aircraft_cfg, "Hardpoint selection");
			// if (show_aircraft_cfg)
			// {
			// 	foreach (HardpointGroup group in aircraft.hardpoint_manager.hardpoint_groups)
			// 	{
			// 		EditorGUILayout.BeginHorizontal();
			// 		EditorGUILayout.LabelField($"{group.name}", GUILayout.Width(160f));
			// 		EditorGUILayout.LabelField($"{group.mass}kg", GUILayout.Width(120f), GUILayout.ExpandWidth(true));
			// 		EditorGUI.BeginChangeCheck();
			// 		int new_selection = EditorGUILayout.Popup(group.currently_mounted, group.option_names, GUILayout.Width(240f), GUILayout.ExpandWidth(true));
			// 		if (EditorGUI.EndChangeCheck())
			// 		{
			// 			aircraft.hardpoint_manager.set_group_mounted_option(group.manager_index, new_selection);
			// 			value_changed = true;
			// 		}
			// 		EditorGUILayout.EndHorizontal();
			// 	}
			// }
			// EditorGUILayout.EndVertical();
			// EditorGUILayout.EndFoldoutHeaderGroup();
			EditorGUILayout.EndVertical();

			return (value_changed, ctrls_changed);
		}

		private void OnGUI()
		{
			// toolbar
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			simulate_on_change = GUILayout.Toggle(simulate_on_change, new GUIContent("Auto run", "Automatically simulate when something changes"), EditorStyles.toolbarButton);
			animate_surfaces = GUILayout.Toggle(animate_surfaces, "Animate surfaces", EditorStyles.toolbarButton);
			GUILayout.FlexibleSpace();
			using (new EditorGUI.DisabledScope(current_aircraft == null))
			{
				if (GUILayout.Button("Mode select", EditorStyles.toolbarButton)) { current_aircraft = null; }
				if (GUILayout.Button("Rebuild", EditorStyles.toolbarButton)) // rebuild aircraft data to accomodate changes
				{
					current_aircraft.reset();
					if (current_prefab == null) current_prefab = AircraftUtils.get_prefab_from_root(current_aircraft_object);
					load_aircraft(current_aircraft_object, current_prefab);
				}
			}
			using (new EditorGUI.DisabledScope(current_aircraft == null || !SimEnvironment.check_in_sim_scene(current_aircraft)))
			{
				if (GUILayout.Button("Reset", EditorStyles.toolbarButton)) // 
				{
					if (current_prefab == null) current_prefab = AircraftUtils.get_prefab_from_root(current_aircraft_object);
					(GameObject container, GameObject new_aircraft, string status) = SimEnvironment.setup_wind_tunnel(current_prefab, true);
					last_simenv_status = status;
					load_aircraft(new_aircraft, current_prefab);
					new_aircraft.transform.localPosition = -current_aircraft.physics.dry_cg;
				}
			}
			EditorGUILayout.EndHorizontal();

			var selector_data = WindTunnel_Target.instance;
			if (this.minSize.x < selector_data.min_size.x) this.minSize = selector_data.min_size;
			GameObject selected_prefab = selector_data.aircraft_prefab;

			if (selected_prefab != null && selected_prefab != current_prefab)
			{
				current_aircraft = null;
			}

			if (current_aircraft == null) {
				// TODO: make this less terrible, dear lord
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
			float gui_dt = time_now - last_update_time;
			bool can_update = gui_dt > target_update_time;
			if (can_update) last_update_time = time_now;

			//current_mode = (simulation_mode)GUILayout.Toolbar((int)current_mode, mode_names);
			EditorGUI.BeginChangeCheck();
			current_mode_index = GUILayout.Toolbar(current_mode_index, mode_names);
			bool mode_changed = EditorGUI.EndChangeCheck();
			if (mode_changed) {
				current_mode = modes_available[current_mode_index];
			}

			(bool cfg_changed, bool ctrls_changed) = aircraft_panel(current_aircraft);

			EditorGUILayout.BeginVertical("GroupBox");
			EditorGUILayout.LabelField("Simulation parameters", EditorStyles.boldLabel);

			// present simulation parameters
			Dictionary<string, bool> show_params = current_mode.parameters_shown;
			bool params_changed = ctrls_changed || cfg_changed || mode_changed;

			(float rho, float mach) = Simulation.isa_atm(altitude_fixed.current_value);

			if (show_params.ContainsKey("airspeed_fixed"))
			{
				airspeed_fixed.present_extra(ref params_changed, ref airspeed_fixed_mach, ref using_mach, "", "M", 1f/mach);
				//if (airspeed_fixed.present(ref params_changed)) { mach_fixed.update_value(airspeed_fixed.current_value / mach); using_airspeed = true; }
				//if (mach_fixed.present_one(ref params_changed, "M")) { airspeed_fixed.update_value(mach_fixed.current_value * mach); using_airspeed = false; }
			}
			if (show_params.ContainsKey("airspeed_range"))
			{
				airspeed_min.present_extra(ref params_changed, ref airspeed_min_mach, ref using_mach, "", "M", 1f/mach);
				airspeed_max.present_extra(ref params_changed, ref airspeed_min_mach, ref using_mach, "", "M", 1f/mach);
				// if (airspeed_min.present(ref params_changed)) { mach_min.update_value(airspeed_min.current_value / mach); using_airspeed = true; }
				// if (airspeed_max.present(ref params_changed)) { mach_max.update_value(airspeed_max.current_value / mach); using_airspeed = true; }

				// if (mach_min.present_one(ref params_changed, "M")) { airspeed_min.update_value(mach_min.current_value * mach); using_airspeed = false; }
				// if (mach_max.present_one(ref params_changed, "M")) { airspeed_max.update_value(mach_max.current_value * mach); using_airspeed = false; }
			}

			// mach_fixed, mach_range omitted because airspeed_fixed and airspeed_range already provide mach input

			if (show_params.ContainsKey("altitude_fixed"))
			{
				if (altitude_fixed.present(ref params_changed))
				{
					var atmos = Simulation.isa_atm(altitude_fixed.current_value);
					if (show_params.ContainsKey("airspeed_fixed")) {
						if (using_mach) airspeed_fixed.update_value(airspeed_fixed_mach * atmos.mach);
					}
					if (show_params.ContainsKey("airspeed_range")) {
						if (using_mach) {
							airspeed_min.update_value(airspeed_min_mach * atmos.mach);
							airspeed_max.update_value(airspeed_max_mach * atmos.mach);
						}
					}
					// if (show_params.ContainsKey("airspeed_fixed")) {
					// 	if (using_airspeed) mach_fixed.update_value(airspeed_fixed.current_value / atmos.mach);
					// 	else mach_fixed.update_value(airspeed_fixed.current_value / atmos.mach);
					// }
					// if (show_params.ContainsKey("airspeed_range")) {
					// 	if (using_airspeed) {
					// 		mach_min.update_value(airspeed_min.current_value / atmos.mach);
					// 		mach_max.update_value(airspeed_max.current_value / atmos.mach);
					// 	} else {
					// 		airspeed_min.update_value(mach_min.current_value * atmos.mach);
					// 		airspeed_max.update_value(mach_max.current_value * atmos.mach);
					// 	}
					// }
				}
			}
			
			// if a mode is using altitude_range, it must decide if it wants to use mach (speed will vary with altitude) or airspeed
			// airspeed will give the same velocity at all altitudes, mach will be faster at sea level and slower high up
			if (show_params.ContainsKey("altitude_range"))
			{
				altitude_min.present(ref params_changed);
				altitude_max.present(ref params_changed);
			}

			if (show_params.ContainsKey("alpha_range")) UI.present_two(ref params_changed, "Alpha range", ref alpha_min, "° min", ref alpha_max, "° max");
			if (show_params.ContainsKey("beta_range")) UI.present_two(ref params_changed, "Beta range", ref beta_min, "° min", ref beta_max, "° max");

			if (show_params.ContainsKey("alpha_fixed")) UI.present_one(ref params_changed, "Fixed alpha", ref alpha_fixed, "°");
			if (show_params.ContainsKey("beta_fixed")) UI.present_one(ref params_changed, "Fixed beta", ref beta_fixed, "°");

			EditorGUI.BeginChangeCheck();
			simulation_steps = EditorGUILayout.IntSlider("Steps", simulation_steps, 5, 500);
			if (EditorGUI.EndChangeCheck()) params_changed = true;

			if (params_changed) current_params = new AircraftState(airspeed_fixed, altitude_fixed, alpha_fixed, beta_fixed);

			if (!simulating & (params_changed || was_updating || update_queued)) {
				// keeps itself running with was_updating to allow smooth movement
				if (can_update)
				{
					was_updating = current_aircraft.update(current_params, input_panel.inputs_obj, animate_surfaces ? gui_dt : 100f);
					update_queued = false;
				}
				else update_queued = true;
			}

			if (!simulating && simulate_on_change && (params_changed || was_updating))
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

			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUILayout.Label(last_simenv_status, "ToolbarLabel");
			GUILayout.FlexibleSpace();
			GUILayout.Label(in_wind_tunnel ? "In wind tunnel" : "In prefab", "ToolbarLabel");
			EditorGUILayout.EndHorizontal();

			update_results();
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


		private void run_simulation(float dt = 1f)
		{
			simulating = true;
			//find_aircraft();
			if (current_aircraft == null) { Debug.LogError("No aircraft selected, or no prefab available"); return; }

			List<graph_series> new_series = new List<graph_series>();

			// default visualizations, will only be displayed if the current mode populates them
			graph_series lift_series = new graph_series { name = "L", color = new Color(0.4f, 0.4f, 1.0f) };
			graph_series clift_series = new graph_series { name = "CL", color = new Color(0.4f, 0.5f, 1.0f) };
			graph_series drag_series = new graph_series { name = "D", color = new Color(1.0f, 0.4f, 0.2f) };
			graph_series cdrag_series = new graph_series { name = "CD", color = new Color(1.0f, 0.5f, 0.4f) };
			graph_series ld_series = new graph_series { name = "L/D", color = new Color(1.0f, 0.6f, 0.2f) };

			graph_series t_pitch_series = new graph_series { name = "T (pitch)", color = new Color(0.2f, 1.0f, 0.4f) };
			graph_series t_yaw_series = new graph_series { name = "T (yaw)", color = new Color(0.4f, 1.0f, 0.8f) };
			graph_series t_roll_series = new graph_series { name = "T (roll)", color = new Color(0.8f, 1.0f, 0.4f) };

			// mode-specific
			graph_series trim_series = new graph_series { name = "Trim", color = new Color(0.8f, 0.4f, 1.0f) };

			new_series.AddRange(new[] {
				lift_series, clift_series, drag_series, cdrag_series, ld_series,
				t_pitch_series, t_yaw_series, t_roll_series
			});

			// initial values
			float s_vel = airspeed_fixed.current_value;
			float s_alt = altitude_fixed.current_value;
			float s_alpha = alpha_fixed;
			float s_beta = beta_fixed;

			float min_index = float.MaxValue, max_index = -float.MaxValue;

			AircraftState sim_params = new AircraftState(s_vel, s_alt, s_alpha, s_beta);

			for (int i = 0; i < simulation_steps; i++)
			{
				switch(current_mode.sim_type)
				{
					case "trim_sweep":
					case "alpha_sweep": s_alpha = alpha_min + (i * ((alpha_max - alpha_min) / (simulation_steps - 1))); break;
					case "beta_sweep": s_beta = beta_min + (i * ((beta_max - beta_min) / (simulation_steps - 1))); break;
					default: Debug.LogWarning($"unrecognized sim type {current_mode.sim_type}"); break;
				}

				Simulation.SimResults results = default;
				float result_index = current_mode.sim_type switch
				{
					"alpha_sweep"	=> s_alpha,
					"beta_sweep"	=> s_beta,
					"trim_sweep"	=> s_alpha,
					_				=> s_alpha
				};

				// rebuild to accomodate state changes
				sim_params = new AircraftState(s_vel, s_alt, s_alpha, s_beta);

				switch (current_mode.sim_type)
				{
					case "trim_sweep":
						float a = -1f, c = float.NaN;
						current_aircraft.update(sim_params, new ControlInputs { throttle = 0.5f, pitch = a });
						float t_a = Simulation.aircraft_forces(current_aircraft, s_vel, s_alt, s_alpha * Mathf.Deg2Rad, s_beta * Mathf.Deg2Rad).torque.x;
						float t_c = 0f;

						float best_trim = a, best_torque = Mathf.Abs(t_a);

						// start by refining [a,c] a bit
						const int steps = 5;
						for (int j = 1; j < steps + 1; j++)
						{
							float x = -1f + (2f * j / steps);
							current_aircraft.update(sim_params, new ControlInputs { throttle = 0.5f, pitch = x });
							float t_x = Simulation.aircraft_forces(current_aircraft, s_vel, s_alt, s_alpha * Mathf.Deg2Rad, s_beta * Mathf.Deg2Rad).torque.x;
							if (Mathf.Abs(t_x) < best_torque) { best_trim = x; best_torque = Mathf.Abs(t_x); }
							if (t_a * t_x <= 0f) { c = x; t_c = t_x; break; }
							a = x; t_a = t_x;
						}

						float final_trim = best_trim;
						if (!float.IsNaN(c)) {
							// straightforward bisection, calculate torque at a, then at midpoint
							// if it crosses zero, we overshot, refine the range to (a, b) and go again
							// if it doesn't cross zero, undershot, refine the range to (b, c) and go again
							for (int j = 0; j < 30; j++)
							{
								float b = (a + c) / 2f;
								current_aircraft.update(sim_params, new ControlInputs { throttle = 0.5f, pitch = b });
								float t_b = Simulation.aircraft_forces(current_aircraft, s_vel, s_alt, s_alpha * Mathf.Deg2Rad, s_beta * Mathf.Deg2Rad).torque.x;
								if (Mathf.Abs(t_b) < 1f) break; // that's probably close enough

								if ((t_a * t_b) <= 0f) c = b; // crossed zero, root between a,b
								else { a = b; t_a = t_b; }  // didn't cross zero, root between b,c
							}
							final_trim = (a + c) / 2f;
						}

						current_aircraft.update(sim_params, new ControlInputs { throttle = 0.5f, pitch = final_trim });
						results = Simulation.aircraft_forces(current_aircraft, s_vel, s_alt, s_alpha * Mathf.Deg2Rad, s_beta * Mathf.Deg2Rad);

						trim_series.Add(new Vector2(result_index, final_trim));

						current_aircraft.update(sim_params, input_panel.inputs_obj, 100f);
						break;
					default:
						//current_aircraft.update(sim_params, input_panel.inputs_obj);
						results = Simulation.aircraft_forces(current_aircraft, s_vel, s_alt, s_alpha * Mathf.Deg2Rad, s_beta * Mathf.Deg2Rad);
						break;
				}
				
				float total_lift = results.lift.y;//Math.Sign(results.lift.y) * results.lift.magnitude;
				float total_drag = results.drag.magnitude;

				float dyn_pres = 0.5f * Simulation.isa_atm(s_alt).rho * (s_vel * s_vel);
				//Debug.Log($"Lift: {total_lift}, dyn pres: {dyn_pres}, wing area: {current_aircraft.physics.wing_area}");
				clift_series.Add(new Vector2(result_index, total_lift / (dyn_pres * current_aircraft.physics.wing_area)));
				cdrag_series.Add(new Vector2(result_index, total_drag / (dyn_pres * current_aircraft.physics.drag_area)));

				float lift_drag_ratio = total_lift / total_drag;

				// todo: figure out a good way for modes to add their own series outputs + choosing which of these to include
				lift_series.Add(new Vector2(result_index, total_lift));
				ld_series.Add(new Vector2(result_index, lift_drag_ratio));
				drag_series.Add(new Vector2(result_index, total_drag));
				t_pitch_series.Add(new Vector2(result_index, results.torque.x));
				t_yaw_series.Add(new Vector2(result_index, results.torque.y));
				t_roll_series.Add(new Vector2(result_index, results.torque.z));

				min_index = Mathf.Min(min_index, result_index); max_index = Mathf.Max(max_index, result_index);
			}

			Dictionary<string, bool> was_visible = simulation_series.ToDictionary(entry => entry.name, entry => entry.visible);

			float one_g_force = current_aircraft.physics.current_mass * Constants.G;
			graph_series one_g_line = new graph_series {
				name = "1G",
				color = Color.white,
				points = new Vector2[2]{ new Vector2(min_index, one_g_force), new Vector2(max_index, one_g_force) },
				visible = was_visible.ContainsKey("1G") ? was_visible["1G"] : true
			};
			new_series.Add(one_g_line);

			// modify series before presentation
			switch(current_mode.sim_type)
			{
				case "trim_sweep":
					new_series.Remove(lift_series);
					new_series.Remove(drag_series);
					new_series.Add(trim_series);
					float wing_pressure = current_aircraft.physics.wing_area * 0.5f * Simulation.isa_atm(s_alt).rho * (s_vel * s_vel);
					one_g_line.points = one_g_line.points.Select(point => new Vector2(point.x, point.y / wing_pressure)).ToArray();
					//one_g_line.name = "1G CL";
					break;
				default:
					break;
			}


			List<graph_series> series_compiled = new_series.Where(entry => (entry._points.Count > 0 || entry.points.Length > 0)).Select(entry => {
				entry.finish();
				entry.visible = was_visible.ContainsKey(entry.name) ? was_visible[entry.name] : true;
				return entry;
			}).ToList();

			(float x_min, float x_max, float y_min, float y_max) = UI.autofit_series(series_compiled);

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

			Simulation.SimResults results = Simulation.aircraft_forces(aircraft, 100, 50, 5 * Mathf.Deg2Rad, 0);
			Debug.Log($"Aircraft mass: {physics.dry_mass}, CG: {physics.dry_cg}");
			Debug.Log($"Total force: {results.total}, lift: {results.lift}, drag: {results.drag}");
			Debug.Log($"Lift/weight: {results.lift / (physics.dry_mass * Constants.G)}");
			Debug.Log($"Aerodynamic torque: {results.torque}");
		}
	}
}


#endif