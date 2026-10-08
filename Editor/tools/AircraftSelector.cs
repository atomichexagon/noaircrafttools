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
	public static class AircraftSelector
	{
		public abstract class Target
		{
			public abstract UnityEngine.Object aircraft_definition { get; set; }
			public abstract GameObject aircraft_prefab { get; set; }
			public abstract Type target_window { get; }
			public abstract string target_name { get; }
		}

		private static Target target_data;

		public static void open_with_target(Target target_struct)
		{
			target_data = target_struct;
			var window = EditorWindow.GetWindow<AircraftSelectorWindow>("Select aircraft");
		}

		public class AircraftSelectorWindow : EditorWindow
		{
			static UnityEngine.Object aircraft_def;

			private void finish(UnityEngine.Object aircraft_definition)
			{
				target_data.aircraft_definition = aircraft_definition;
				target_data.aircraft_prefab = find_prop<GameObject>(new SerializedObject(aircraft_definition), "unitPrefab");
				var window = GetWindow(target_data.target_window, false, target_data.target_name);
				this.Close();
			}

			private void OnGUI()
			{
				EditorGUILayout.LabelField($"Target: {target_data.target_name}");

				EditorGUILayout.BeginVertical("GroupBox");
				EditorGUILayout.LabelField("Select definition");
				aircraft_def = EditorGUILayout.ObjectField("Aircraft template", aircraft_def, game_type("AircraftDefinition"), false);
				string aircraft_def_name = (aircraft_def != null) ? aircraft_def.name : "n/a";

				using (new EditorGUI.DisabledScope(aircraft_def == null))
				{
					EditorGUILayout.LabelField("Selected aircraft:", aircraft_def_name);
					if (GUILayout.Button("Use selected definition", GUILayout.Height(28)))
						finish(aircraft_def);
				}
				EditorGUILayout.EndVertical();

				// Check if we're in a valid prefab
				GameObject prefab_root = SimEnvironment.check_in_prefab_no_dialog();

				UnityEngine.Object def_from_prefab = null;
				string def_from_prefab_name = "n/a";
				if (prefab_root != null)
				{
					var aircraft_comp = prefab_root.GetComponent(game_type("Aircraft"));
					if (aircraft_comp != null) {
						var s_aircraft = new SerializedObject(aircraft_comp);
						var def_obj = find_prop<UnityEngine.Object>(s_aircraft, "definition");
						if (def_obj != null) {
							def_from_prefab = def_obj;
							def_from_prefab_name = def_obj.name;
						}
					}
				}

				EditorGUILayout.BeginVertical("GroupBox");
				EditorGUILayout.LabelField("Select from current prefab");
				using (new EditorGUI.DisabledScope(def_from_prefab == null))
				{
					EditorGUILayout.LabelField("Aircraft in current prefab:", def_from_prefab_name);
					if (GUILayout.Button("Use definition from prefab", GUILayout.Height(28)))
						finish(def_from_prefab);
				}
				EditorGUILayout.EndVertical();
				Repaint();
			}
		}
	}
}
#endif