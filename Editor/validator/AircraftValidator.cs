#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Search;
using UnityEditor.SceneManagement;

using hexagonest.Tools;
using static hexagonest.Tools.Utilities;


namespace hexagonest.Validator
{
	// Author's note: I'm sorry in advance if you're good with C#. I imagine reading this will be a form of torture.
	public class AircraftValidator : EditorWindow
	{
		private static UnityEngine.Object aircraft_definition = null;

		private static List<IValidator> sorted_validators = new List<IValidator>();
		private static bool ran_first_run = false;

		private static List<ValidatorResult> results = new List<ValidatorResult>();
		private static List<ValidatorResult> sorted_results = new List<ValidatorResult>();

		private static int num_passing = 0, num_warnings = 0, num_total = 0;
		private static float last_update_elapsed = 0f;
		private static float last_update_time = -1f;
		private static float validation_debounce = 0.5f;
		private static Status last_validation = Status.pass;

		private static Vector2 scroll = Vector2.zero;

		static Dictionary<Status, GUIStyle> status_styles = new Dictionary<Status, GUIStyle>();
		static readonly Dictionary<Status, Color> status_colors = new Dictionary<Status, Color>{
			[Status.skipped] 	= new Color(0.6f, 0.6f, 0.6f),
			[Status.pass] 		= new Color(0.4f, 0.8f, 0.4f),
			[Status.info] 		= new Color(0.5f, 0.7f, 1.0f),
			[Status.warn] 		= new Color(1.0f, 0.8f, 0.2f),
			[Status.fail] 		= new Color(1.0f, 0.5f, 0.2f),
			[Status.critical] 	= new Color(1.0f, 0.3f, 0.3f)
		};
		static readonly Dictionary<Status, string> status_text = new Dictionary<Status, string>{
			[Status.skipped] 	= "Skipped",
			[Status.pass]		= "Pass",
			[Status.info]		= "Info",
			[Status.warn]		= "Warn",
			[Status.fail]		= "Fail",
			[Status.critical]	= "Critical",
		};


		static EditorApplication.CallbackFunction stop_tick;


		public void OnDisable()
		{
			Highlighter.Stop();
		}


		[MenuItem("HexTools/Check aircraft")]
		public static void open_gui()
		{
			var window = EditorWindow.GetWindow<AircraftValidator>("Aircraft validator");
		}


		public void first_run()
		{
			if (ran_first_run) return;
			ran_first_run = true;
			rebuild_validator_cache();
		}


		static GUIStyle get_status_style(Status status)
		{
			if (!status_styles.TryGetValue(status, out var style) || style == null)
			{
				style = new GUIStyle(GUI.skin.label);
				style.fontStyle = FontStyle.Bold;
				style.normal.textColor = status_colors[status];
				status_styles[status] = style;
			}
			return style;
		}


		static void go_to(Entry entry)
		{
			if (entry.context == null) return;

			if (entry.context is not GameObject prefab || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(prefab)))
			{
				select(entry.context, entry.field);
				return;
			}

			AssetDatabase.OpenAsset(prefab);
			
			EditorApplication.delayCall += () =>
			{
				var root = PrefabStageUtility.GetCurrentPrefabStage()?.prefabContentsRoot;
				if (root == null) return;

				var tf = string.IsNullOrEmpty(entry.path) ? root.transform : root.transform.Find(entry.path);
				if (tf == null)
				{
					Debug.LogWarning($"Path '{entry.path}' not found in {prefab.name}");
					select(root, null);
					return;
				}

				UnityEngine.Object target = tf.gameObject;
				if (!string.IsNullOrEmpty(entry.component))
				{
					var comp = tf.GetComponents<Component>()
						.FirstOrDefault(c => c != null && c.GetType().Name == entry.component);
					if (comp != null) target = comp;
				}
				select(target, entry.field);
			};
		}


		static void select(UnityEngine.Object target, string field)
		{
			Selection.activeObject = target;
			EditorGUIUtility.PingObject(target);

			if (!string.IsNullOrEmpty(field))
			{
				highlight_field(target, field);
			}
		}


		static void highlight_field(UnityEngine.Object target, string field_path)
		{
			using var so = new SerializedObject(target);

			var parts = field_path.Split('.');
			// string current = "";
			// for (int i = 0; i < parts.Length - 1; i++)
			// {
			// 	current += (i > 0? "." : "") + parts[i];
			// 	var parent = so.FindProperty(current);
			// 	if (parent != null) parent.isExpanded = true;
			// }
			// so.ApplyModifiedPropertiesWithoutUndo();

			// var leaf = so.FindProperty(field_path);
			// string label = leaf != null ? leaf.displayName : ObjectNames.NicifyVariableName(parts[^1]);

			EditorApplication.delayCall += () => highlight_temporarily(parts[0], 3.0);
		}


		static void highlight_temporarily(string label, double seconds = 3.0)
		{
			if (stop_tick != null) EditorApplication.update -= stop_tick;
			Highlighter.Highlight("Inspector", label, HighlightSearchMode.Auto);

			double end = EditorApplication.timeSinceStartup + seconds;
			stop_tick = () =>
			{
				if (EditorApplication.timeSinceStartup < end) return;
				EditorApplication.update -= stop_tick;
				stop_tick = null;
				Highlighter.Stop();
			};
			EditorApplication.update += stop_tick;
		}


		static void rebuild_validator_cache()
		{
			sorted_validators = sort_validators(find_validators());
		}


		static List<IValidator> find_validators()
		{
			List<IValidator> validators = new List<IValidator>();
			foreach (var type in TypeCache.GetTypesDerivedFrom<IValidator>())
			{
				if (type.IsAbstract || type.IsInterface) continue;
				if (type.GetConstructor(Type.EmptyTypes) == null) continue;
				validators.Add((IValidator)Activator.CreateInstance(type));
			}
			return validators;
		}


		static List<IValidator> sort_validators(List<IValidator> validators)
		{
			Dictionary<Type, IValidator> by_type = validators.ToDictionary(v => v.GetType());
			List<IValidator> sorted = new List<IValidator>();
			Dictionary<Type, int> state = new Dictionary<Type, int>(); // 1 - visiting, 2 - done

			void visit(Type t)
			{
				if (state.TryGetValue(t, out var s))
				{
					if (s == 1) throw new InvalidOperationException($"Circular requirement: {t.Name}");
					return;
				}
				state[t] = 1;
				foreach (var req in t.GetCustomAttributes<RequiresAttribute>())
				{
					if (!by_type.ContainsKey(req.validator))
						throw new InvalidOperationException($"{t.Name} requires missing {req.validator.Name}");
					visit(req.validator);
				}
				state[t] = 2;
				sorted.Add(by_type[t]);
			}

			foreach (var t in by_type.Keys.OrderBy(t => t.Name)) visit(t);
			return sorted;
		}


		public static void validate_aircraft(UnityEngine.Object _aircraft_definition)
		{
			float time_now = Time.realtimeSinceStartup;
			float gui_dt = time_now - last_update_time;
			bool can_update = gui_dt > validation_debounce;
			if (can_update) last_update_time = time_now;
			else return;

			aircraft_definition = _aircraft_definition;

			int new_pass = 0, new_warnings = 0, new_total = 0;
			List<ValidatorResult> new_results = new List<ValidatorResult>();

			last_validation = Status.skipped;
			float validate_began = Time.realtimeSinceStartup;
			foreach (IValidator validator in sorted_validators)
			{
				ValidatorResult validator_results = validator.evaluate(aircraft_definition);
				new_results.Add(validator_results);
				new_pass += validator_results.num_passing; new_warnings += validator_results.num_warnings; new_total += validator_results.num_evaluated;
				if (validator_results.status > last_validation) last_validation = validator_results.status;
				if (validator_results.status == Status.critical) { Debug.LogError($"Validation failed: {validator.Name}"); break; }
			}
			float validate_ended = Time.realtimeSinceStartup;
			last_update_elapsed = validate_ended - validate_began;

			num_passing = new_pass; num_warnings = new_warnings; num_total = new_total;

			results = new_results;
			sorted_results = results.OrderByDescending(entry => entry.status)
				.ThenByDescending(entry => entry.num_warnings)
				//.ThenByDescending(entry => entry.num_entries)
				.ToList();
		}


		private void draw_result(ValidatorResult result)
		{
			bool first_drawn = true;
			EditorGUILayout.BeginVertical("box");
			var sorted = result.entries.OrderByDescending(entry => entry.status).ToList();
			sorted.ForEach(entry => { if(!first_drawn) entry.check_name = ""; draw_entry(entry); first_drawn = false; });
			if (result.status == Status.skipped) draw_row(first_drawn ? result.validator_name : "", Status.skipped, null, "Skipped"); // (no validation required)
			if (result.num_passing > 0) draw_row(first_drawn ? result.validator_name : "", Status.pass, null, $"{result.num_passing}/{result.num_entries} passed");
			EditorGUILayout.EndVertical();
		}


		private void draw_entry(Entry entry) => draw_row(entry.check_name, entry.status, entry.context, entry.message, entry.actions, "", "", "", entry);

		private void draw_row(string validator, Status status, UnityEngine.Object context, string message, List<Fix> actions = null, string str_status = "", string str_context = "", string str_actions = "", Entry entry = null)
		{
			var status_style = GUI.skin.label;
			if (str_status == "") status_style = get_status_style(status);
			string status_msg = (str_status != "" ? str_status : status_text[status]);
			EditorGUILayout.BeginHorizontal();
			GUILayout.Label($" {validator} ", GUILayout.Width(120));
			GUILayout.Label($" {status_msg} ", status_style, GUILayout.Width(60));
			string context_str = context != null ? context.name : "";
			bool context_button = false;
			if (context != null)
			{
				if (context is UnityEngine.Object mb) { context_button = true; context_str = mb.name; }
				if (context is GameObject go) { context_button = true; context_str = go.name; }//SearchUtils.GetHierarchyPath(go);
			} else if (str_context != "") context_str = str_context;

			if (context_button)
			{
				if (GUILayout.Button(context_str, "minibutton", GUILayout.Width(120), GUILayout.Height(EditorGUIUtility.singleLineHeight))) {
					go_to(entry);
				}
			} else  GUILayout.Label(context_str, EditorStyles.wordWrappedLabel, GUILayout.Width(120));
			GUILayout.Label($" {message} ", EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(true)); //GUILayout.Width(360), 

			GUILayout.FlexibleSpace();
			if (actions != null)
				foreach (Fix action in actions) {
					using (new EditorGUI.DisabledScope(action.used)) {
						if (GUILayout.Button(action.name, "minibutton", GUILayout.ExpandWidth(false), GUILayout.Height(EditorGUIUtility.singleLineHeight))) {
							bool success = action.action();
							//if (success) validate_aircraft(aircraft_definition);
							action.used = true;
						}
					}
				}
			if (str_actions != "")
			{
				GUILayout.Label($" {str_actions} ");
			}

			EditorGUILayout.EndHorizontal();
		}


		private void OnGUI()
		{
			if (!ran_first_run) first_run();
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			if (GUILayout.Button("Rebuild validators", EditorStyles.toolbarButton)) // rebuild aircraft data to accomodate changes
			{
				rebuild_validator_cache();
			}
			if (GUILayout.Button("Reset asset cache", EditorStyles.toolbarButton)) // rebuild asset cache
			{
				reset_asset_cache();
			}
			GUILayout.FlexibleSpace();
			GUILayout.Label($"{sorted_validators.Count} validators loaded");
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginVertical("GroupBox");
			aircraft_definition = EditorGUILayout.ObjectField("Aircraft definition", aircraft_definition, game_type("AircraftDefinition"), false);

			using (new EditorGUI.DisabledScope(aircraft_definition == null))
			{
				//EditorGUILayout.LabelField($"Selected aircraft: {((aircraft_definition != null) ? aircraft_definition.name : "n/a")}");
				if (GUILayout.Button("Validate aircraft"))
					validate_aircraft(aircraft_definition);
			}
			EditorGUILayout.EndVertical();

			EditorGUILayout.BeginVertical("GroupBox", GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
			draw_row("Validator", Status.pass, null, "Message", null, "Status", "Context", "Actions");
			scroll = EditorGUILayout.BeginScrollView(scroll);
			if (sorted_results.Count == 0)
			{
				EditorGUILayout.BeginHorizontal();
				GUILayout.Label("No results to show", "Badge");
				EditorGUILayout.EndHorizontal();
			} else {
				sorted_results.ForEach(results_entry => draw_result(results_entry));
			}
			EditorGUILayout.EndScrollView();

			string box_text = last_validation switch
			{
				Status.pass 	=> "Validation successful",
				Status.info 	=> "Validation successful, double check the Info messages",
				Status.warn		=> "Validation successful, check warnings",
				Status.fail		=> "Validation failed",
				Status.critical	=> "Validation failed, aircraft will fail to spawn or function",
				_				=> "what"
			};
			var box_type = last_validation switch
			{
				Status.pass						=> MessageType.None,
				Status.info						=> MessageType.Info,
				Status.warn						=> MessageType.Warning,
				Status.fail or Status.critical	=> MessageType.Error,
				_								=> MessageType.Error
			};
			EditorGUILayout.HelpBox(box_text, box_type);
			EditorGUILayout.EndVertical();
			
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUILayout.FlexibleSpace();
			GUILayout.Label(results.Count == 0 ? " No results yet " : $" {num_passing}/{num_total} passed, {num_warnings} warnings ", "ToolbarLabel");
			if (last_update_time != -1f)
				GUILayout.Label($" Last validation: {(last_update_elapsed * 1000):F2}ms ", "ToolbarLabel");
			EditorGUILayout.EndHorizontal();
		}
	}
}


#endif