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

		public class WindTunnelMode
		{
			public string sim_type, name, description;
			public bool controls_enabled; // aircraft controls
			public HashSet<string> parameters_shown; // shows fixed values

			public string[] sweep_params; // available sweep modes
			public int current_sweep;
			public string sweep_param; // currently selected

			//public delegate AircraftState before_sim_step();
			//public string[] channels;

			//public delegate SimResults sim_step(); // must replace Simulation.aircraft_forces() call
		
			public WindTunnelMode(string _sim_type, string _name, string _description, bool _controls_enabled, string[] _parameters_shown,
				string[] _sweep_params)
			{
				sim_type = _sim_type; name = _name; description = _description; controls_enabled = _controls_enabled;
				parameters_shown = new HashSet<string>(_parameters_shown);
				sweep_params = _sweep_params;
				current_sweep = 0; sweep_param = sweep_params[0];
			}
		}

		// TODO: show descriptions somewhere
		static WindTunnelMode[] modes_available = new WindTunnelMode[] {
			new WindTunnelMode ( "force_sweep", "Forces",
				"Evaluates forces on the aircraft through the selected sweep range.",
				true, new string[] { "airspeed", "altitude", "alpha", "beta" }, new string[] { "alpha", "beta", "airspeed" }
			),
			new WindTunnelMode ( "trim_sweep", "Trim finder",
				"In alpha sweep mode, finds the trim setting where pitch torque is zero."
				+ "\nIn airspeed sweep mode, finds the required alpha to maintain 1G, and then finds the trim setting to zero out pitch torque.",
				true, new string[] { "airspeed", "altitude", "beta" }, new string[] { "alpha", "airspeed" }
			),
			new WindTunnelMode ( "deflection_sweep", "Control authority",
				"Evaluates forces on the aircraft across the selected control's full range.",
				true, new string[] { "airspeed", "altitude", "alpha", "beta" }, new string[] { "pitch", "roll", "yaw" }
			),
			new WindTunnelMode ( "stability_index", "Stability index",
				"Evaluates stability index across alpha range",
				true, new string[] { "airspeed", "altitude", "alpha", "beta" }, new string[] { "alpha" }
			),
			new WindTunnelMode ( "visualizer", "Visualizer", // TODO: make this its own tool?
				"Visualize forces acting on the aircraft. Does not run simulations.",
				true, new string[] { "airspeed", "altitude", "alpha", "beta" }, new string[] { "visualizer" }
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
		static bool using_mach = false; // true if airspeed was last changed by setting mach directly

		static Dictionary<string, IParameterFixed> p_fixed = new Dictionary<string, IParameterFixed>{
			["airspeed"] = new ParameterFixedUnit("Airspeed", "speed", 100f, "m/s", "km/h"),
			["altitude"] = new ParameterFixedUnit("Altitude", "distance", 50f, "m", "ft"),
			["alpha"] = new ParameterFixed("Alpha", 5f),
			["beta"] = new ParameterFixed("Beta", 0f),
		};

		static Dictionary<string, IParameterRange> p_sweep = new Dictionary<string, IParameterRange>{
			["airspeed"] = new ParameterRangeUnit("Airspeed", "speed", 100f, 200f, "m/s", "km/h"),
			["altitude"] = new ParameterRangeUnit("Altitude (min)", "distance", 0f, 2500f, "m", "ft"),
			["alpha"] = new ParameterRange("Alpha", -45f, 45f),
			["beta"] = new ParameterRange("Beta", -45f, 45f),
		};

		// not linked to airspeed parameters
		//static ParameterFixedUnit mach_fixed = new ParameterFixedUnit("Mach", "speed", 0.3f, "m/s", "km/h");
		//static ParameterRangeUnit mach_range = new ParameterRangeUnit("Mach (min)", "speed", 0.8f, 1.2f, "m/s", "km/h");

		static AircraftState current_params = new AircraftState(
			p_fixed["airspeed"].current_value, p_fixed["altitude"].current_value,
			p_fixed["alpha"].current_value, p_fixed["beta"].current_value
		);

		static int simulation_steps = 81;
		static bool simulating = false; // to lock out other behaviors while a sim is running
		static bool was_updating = false; // allows smooth control movement without calling expensive .update() every frame

		// display
		static List<graph_series> simulation_series = new List<graph_series>();
		static Dictionary<string, bool> series_visible = new Dictionary<string, bool>();
		static bool has_simulation_data = false;

		static Simulation.SimResults last_results;
		static bool has_results = false;
		static bool was_visualizing = false;
		//static bool graph_updated = false;

		static Rect? graph_rect;

		// status shenanigans
		static string last_simenv_status = "";
		static bool tunnel_collision_check = false;
		static float last_sim_time = -1f;
		static int last_sim_count = 0;

		void OnEnable()
		{
			this.minSize = new Vector2(580f, 800f);
			SceneView.duringSceneGui += show_visuals;
		}

		void OnDisable()
		{
			SceneView.duringSceneGui -= show_visuals;
			if (current_container != null) current_container.transform.rotation = Quaternion.identity;
			if (current_aircraft == null) return;
			current_aircraft.reset();
		}

		static void show_visuals(SceneView view)
		{
			if (current_aircraft == null || !in_wind_tunnel) return;
			if (current_mode.sim_type != "visualizer") return;

			last_results.forces.visualize(current_aircraft, current_container.transform, last_results.torque);
			// velocity
			Simulation.draw_arrow(current_container.transform, Vector3.zero, current_params.velocity, Color.white, 1f, true);
			// net acceleration (vertically)
			Vector3 y_accel = new Vector3(0f, (last_results.total.y - (current_aircraft.physics.current_mass * Constants.G)) / current_aircraft.physics.current_mass, 0f);
			y_accel = Quaternion.Inverse(Simulation.aircraft_orientation(current_params.alpha, current_params.beta)) * y_accel;
			Simulation.draw_arrow(current_container.transform, current_aircraft.physics.current_cg, y_accel, Color.white, 1f);
		}

		static void load_aircraft(GameObject aircraft_root, GameObject prefab_used)
		{
			current_aircraft = new AircraftData(aircraft_root);
			current_aircraft_object = aircraft_root;
			current_prefab = prefab_used;

			if (aircraft_root != prefab_used) current_container = aircraft_root.transform.parent.gameObject;

			fuel_level = current_aircraft.fuel_fraction;

			tunnel_collision_check = false;
		}

		static void unload_aircraft()
		{
			current_aircraft = null;
			if (current_container != null) current_container.transform.rotation = Quaternion.identity;
			current_aircraft_object = null;
			has_results = false;
		}

		[MenuItem("HexTools/Wind tunnel")]
		static void open()
		{
			AircraftSelector.open_with_target(WindTunnel_Target.instance);
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
			UI.present_separator();
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
				if (GUILayout.Button("Mode select", EditorStyles.toolbarButton)) unload_aircraft();
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
					current_container = container;
					last_simenv_status = status;
					load_aircraft(new_aircraft, current_prefab);
					new_aircraft.transform.localPosition = -current_aircraft.physics.dry_cg;
				}
			}
			EditorGUILayout.EndHorizontal();

			var selector_data = WindTunnel_Target.instance;
			GameObject selected_prefab = selector_data.aircraft_prefab;

			if (selected_prefab != null && selected_prefab != current_prefab) unload_aircraft();

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
						current_container = container;
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

					GameObject aircraft_prefab = SimEnvironment.check_if_prefab_is_aircraft();
					if (aircraft_prefab != null)
					{
						if (GUILayout.Button("Or just use the prefab you're in", "AC Button"))
							load_aircraft(aircraft_prefab, aircraft_prefab);
					}

					(GameObject aircraft_tunnel, GameObject prefab_from_root) = SimEnvironment.get_aircraft_in_tunnel();
					if (aircraft_tunnel != null)
					{
						if (GUILayout.Button("Use aircraft currently in tunnel", "AC Button"))
							load_aircraft(aircraft_tunnel, prefab_from_root);
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
			HashSet<string> show_params = current_mode.parameters_shown;
			bool params_changed = ctrls_changed || cfg_changed || mode_changed;

			if (current_mode.sweep_params.Length > 0)
			{
				EditorGUI.BeginChangeCheck();
				current_mode.current_sweep = GUILayout.Toolbar(current_mode.current_sweep, current_mode.sweep_params);
				if (EditorGUI.EndChangeCheck()) {
					params_changed = true;
					current_mode.sweep_param = current_mode.sweep_params[current_mode.current_sweep];
				}
			}

			// Sweep param
			if (p_sweep.ContainsKey(current_mode.sweep_param))
				p_sweep[current_mode.sweep_param].present(ref params_changed);

			// Fixed params
			foreach (string parameter in show_params)
			{
				if (current_mode.sweep_param != parameter) p_fixed[parameter].present(ref params_changed);
			}

			EditorGUI.BeginChangeCheck();
			simulation_steps = EditorGUILayout.IntSlider("Steps", simulation_steps, 5, 500);
			if (EditorGUI.EndChangeCheck()) params_changed = true;

			if (params_changed) current_params = new AircraftState(
				p_fixed["airspeed"].current_value, p_fixed["altitude"].current_value,
				p_fixed["alpha"].current_value, p_fixed["beta"].current_value
			);

			if (!simulating && (params_changed || was_updating || update_queued)) {
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
			if (last_sim_time != -1f)
				GUILayout.Label($"Last simulation: {Math.Round(last_sim_time * 1000, 2)}ms ({last_sim_count} steps)", "ToolbarLabel");
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
			if (current_aircraft == null) { Debug.LogError("No aircraft selected, or no prefab available"); return; }

			simulation_series.ForEach(series => series_visible[series.name] = series.visible);

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
			// reused as the pitch angle line for trim_sweep's airspeed sweep
			graph_series one_g_series = new graph_series { name = "1G", color = Color.white, };
			graph_series stability_series = new graph_series { name = "Stability index", color = Color.white, };

			new_series.AddRange(new[] {
				lift_series, clift_series, drag_series, cdrag_series, ld_series,
				t_pitch_series, t_yaw_series, t_roll_series, trim_series, one_g_series,
				stability_series
			});

			// initial values
			float s_vel = p_fixed["airspeed"].current_value;
			float s_alt = p_fixed["altitude"].current_value;
			float s_alpha = p_fixed["alpha"].current_value;
			float s_beta = p_fixed["beta"].current_value;

			float min_index = float.MaxValue, max_index = -float.MaxValue;

			AircraftState sim_params = new AircraftState(s_vel, s_alt, s_alpha, s_beta);

			string sweep_param = current_mode.sweep_param;
			float sweep_min, sweep_max;
			if (p_sweep.ContainsKey(sweep_param))
			{
				IParameterRange sweep_range = p_sweep[sweep_param];
				sweep_min = sweep_range.min.current_value;
				sweep_max = sweep_range.max.current_value;
			} else
			{
				sweep_min = -1; sweep_max = 1; // pitch/roll/yaw (or the range isn't set up right)
			}


			float simulation_began = Time.realtimeSinceStartup;
			float simulation_ended;
			Simulation.num_simulations = 0;
			if (sweep_param == "visualizer")
			{
				if (SimEnvironment.check_in_sim_scene(current_aircraft)) current_container.transform.rotation = Quaternion.identity;

				last_results = Simulation.aircraft_forces(current_aircraft, sim_params);
				//last_results = Simulation.update_and_simulate(current_aircraft, sim_params, input_panel.inputs_obj);
				has_results = true;

				if (SimEnvironment.check_in_sim_scene(current_aircraft))
				{
					current_container.transform.rotation = Simulation.aircraft_orientation(sim_params.alpha, sim_params.beta);
				}

				simulation_ended = Time.realtimeSinceStartup;
				last_sim_time = simulation_ended - simulation_began;
				last_sim_count = Simulation.num_simulations;
				was_visualizing = true;
				simulating = false;
				return;
			} else if (was_visualizing) {
				if (SimEnvironment.check_in_sim_scene(current_aircraft)) current_container.transform.rotation = Quaternion.identity;
			}
			for (int i = 0; i < simulation_steps; i++)
			{
				float sweep_value = sweep_min + (i * ((sweep_max - sweep_min) / (simulation_steps - 1)));
				switch(sweep_param)
				{
					case "alpha": s_alpha = sweep_value; break;
					case "beta": s_beta = sweep_value; break;
					case "airspeed": s_vel = sweep_value; break;
					case "altitude": s_alt = sweep_value; break;
					case "pitch":
					case "yaw":
					case "roll": break;
					default: Debug.LogWarning($"unrecognized sweep {sweep_param}"); break;
				}

				Simulation.SimResults results = default;
				float result_index = sweep_param switch
				{
					"alpha"		=> s_alpha,
					"beta"		=> s_beta,
					"airspeed"	=> s_vel,
					"altitude"	=> s_alt,
					_			=> sweep_value
				};

				// rebuild to accomodate state changes
				sim_params = new AircraftState(s_vel, s_alt, s_alpha, s_beta);

				switch (current_mode.sim_type)
				{
					case "trim_sweep":
						float final_trim;
						if (sweep_param == "alpha")
						{
							final_trim = Simulation.find_pitch_trim(current_aircraft, sim_params, input_panel.inputs_obj);
						} else {
							(float level_alpha, float trim) = Simulation.find_level_flight_alpha(current_aircraft, sim_params, input_panel.inputs_obj);
							final_trim = trim;
							sim_params.alpha = level_alpha * Mathf.Deg2Rad;
							one_g_series.Add(new Vector2(result_index, level_alpha));
						}
						results = Simulation.update_and_simulate(current_aircraft, sim_params, input_panel.inputs_obj.set_pitch(final_trim));

						trim_series.Add(new Vector2(result_index, final_trim));
						break;
					case "deflection_sweep":
						float s_pitch = sweep_param == "pitch" ? sweep_value : input_panel.pitch;
						float s_roll = sweep_param == "roll" ? sweep_value : input_panel.roll;
						float s_yaw = sweep_param == "yaw" ? sweep_value : input_panel.yaw;
						current_aircraft.update(sim_params, new ControlInputs { pitch = s_pitch, roll = s_roll, yaw = s_yaw });
						results = Simulation.aircraft_forces(current_aircraft, sim_params);
						break;
					case "stability_index":
						float stability_index = Simulation.neutral_point_z(current_aircraft, sim_params, input_panel.inputs_obj);
						stability_series.Add(new Vector2(result_index, stability_index));
						break;
					default:
						if (sweep_param == "airspeed") current_aircraft.update(sim_params, input_panel.inputs_obj);
						results = Simulation.aircraft_forces(current_aircraft, sim_params);
						break;
				}

				if (results.total != Vector3.zero)
				{
					float total_lift = Math.Sign(results.lift.y) * results.lift.magnitude;
					float total_drag = results.drag.magnitude;
					float total_area = current_aircraft.physics.wing_area + current_aircraft.physics.drag_area;

					float dyn_pres = 0.5f * Simulation.isa_atm(s_alt).rho * (s_vel * s_vel);
					//Debug.Log($"Lift: {total_lift}, dyn pres: {dyn_pres}, wing area: {current_aircraft.physics.wing_area}");
					float cl = total_lift / (dyn_pres * current_aircraft.physics.wing_area);
					float cd = total_drag / (dyn_pres * total_area);

					clift_series.Add(new Vector2(result_index, cl));
					cdrag_series.Add(new Vector2(result_index, cd));

					float lift_drag_ratio = cl / cd; // total_lift / total_drag;

					// todo: figure out a good way for modes to add their own series outputs + choosing which of these to include
					lift_series.Add(new Vector2(result_index, total_lift));
					ld_series.Add(new Vector2(result_index, lift_drag_ratio));
					drag_series.Add(new Vector2(result_index, total_drag));
					t_pitch_series.Add(new Vector2(result_index, results.torque.x));
					t_yaw_series.Add(new Vector2(result_index, results.torque.y));
					t_roll_series.Add(new Vector2(result_index, results.torque.z));
				}

				min_index = Mathf.Min(min_index, result_index); max_index = Mathf.Max(max_index, result_index);
			}
			simulation_ended = Time.realtimeSinceStartup;
			last_sim_time = simulation_ended - simulation_began;
			last_sim_count = Simulation.num_simulations;

			// float one_g_force = current_aircraft.physics.current_mass * Constants.G;
			// graph_series one_g_line = new graph_series {
			// 	name = "1G",
			// 	color = Color.white,
			// 	points = new Vector2[2]{ new Vector2(min_index, one_g_force), new Vector2(max_index, one_g_force) },
			// 	visible = was_visible.ContainsKey("1G") ? was_visible["1G"] : true
			// };
			// new_series.Add(one_g_line);

			// modify series before presentation
			switch(current_mode.sim_type)
			{
				case "trim_sweep":
					new_series.Remove(lift_series);
					new_series.Remove(drag_series);
					//float wing_pressure = current_aircraft.physics.wing_area * 0.5f * Simulation.isa_atm(s_alt).rho * (s_vel * s_vel);
					//one_g_line.points = one_g_line.points.Select(point => new Vector2(point.x, point.y / wing_pressure)).ToArray();
					//one_g_line.name = "1G CL";
					// reset aircraft to original configured state
					current_aircraft.update(current_params, input_panel.inputs_obj, 100f);
					break;
				case "deflection_sweep":
					new_series.Remove(lift_series);
					new_series.Remove(drag_series);
					new_series.Remove(one_g_series);
					// reset aircraft to original configured state
					current_aircraft.update(current_params, input_panel.inputs_obj, 100f);
					break;
				default:
					if (current_mode.sweep_param == "airspeed") current_aircraft.update(current_params, input_panel.inputs_obj, 100f);
					break;
			}


			List<graph_series> series_compiled = new_series.Where(entry => (entry._points.Count > 0 || entry.points.Length > 0)).Select(entry => {
				entry.finish();
				entry.visible = series_visible.ContainsKey(entry.name) ? series_visible[entry.name] : true;
				return entry;
			}).ToList();

			//(float x_min, float x_max, float y_min, float y_max) = UI.autofit_series(series_compiled);

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