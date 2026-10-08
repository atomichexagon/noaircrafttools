using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

using static hexagonest.Tools.Utilities;


namespace hexagonest.Tools
{
	// constants
	public static class Constants {
		public const float G = 9.81f;
	}


	public class graph_series
	{
		public string name;
		public Color color;
		public Vector2[] points;
		public bool visible = true;
	}


	public static class UI
	{
		public static (float x_min, float x_max, float y_min, float y_max) autofit_series(List<graph_series> series)
		{
			float x_min = float.MaxValue, x_max = float.MinValue;
			float y_min = float.MaxValue, y_max = float.MinValue;
			foreach (var s in series)
				foreach (var p in s.points) {
					x_min = Mathf.Min(x_min, p.x);
					x_max = Mathf.Max(x_max, p.x);

					y_min = Mathf.Min(y_min, p.y);
					y_max = Mathf.Max(y_max, p.y);
				}
			//float x_pad = (x_max - x_min) * 0.05f;
			//x_min -= x_pad; x_max += x_pad;
			//float y_pad = (y_max - y_min) * 0.05f;
			//y_min -= y_pad; y_max += y_pad;

			return (x_min, x_max, y_min, y_max);
		}


		static void fit_range(List<graph_series> series, ref float x_min, ref float x_max, ref float y_min, ref float y_max)
		{
			bool auto_x = float.IsNaN(x_min) || float.IsNaN(x_max);
			bool auto_y = float.IsNaN(y_min) || float.IsNaN(y_max);
			if (!auto_x && !auto_y) return;

			float lx = float.MaxValue, hx = float.MinValue, ly = float.MaxValue, hy = float.MinValue;
			foreach (var s in series)
			{
				if (!s.visible) continue;
				foreach (var p in s.points)
				{
					lx = Mathf.Min(lx, p.x); hx = Mathf.Max(hx, p.x);
					ly = Mathf.Min(ly, p.y); hy = Mathf.Max(hy, p.y);
				}
			}
			if (lx > hx) { lx = 0; hx = 1; ly = 0; hy = 1; } // nothing visible

			float pad_y = Mathf.Max((hy - ly) * 0.05f, 1e-6f);
			if (auto_x) { x_min = lx; x_max = hx; }
			if (auto_y) { y_min = ly - pad_y; y_max = hy + pad_y; }
		}

		public static void draw_graph(Rect r, List<Vector2[]> series, Color[] colors, float x_min, float x_max, float y_min, float y_max)
		{
			EditorGUI.DrawRect(r, new Color(0.15f, 0.15f, 0.15f));

			Vector3 Map(Vector2 p) => new Vector3(
				r.x + Mathf.InverseLerp(x_min, x_max, p.x) * r.width,
				r.yMax - Mathf.InverseLerp(y_min, y_max, p.y) * r.height, 0);

			// zero line
			Handles.color = Color.gray;
			Handles.DrawLine(Map(new Vector2(x_min, 0)), Map(new Vector2(x_max, 0)));

			GUI.BeginClip(r, -r.position, Vector2.zero, false); // keeps lines inside the rect
			for (int s = 0; s < series.Count; s++)
			{
				Handles.color = colors[s];
				var pts = System.Array.ConvertAll(series[s], p => Map(p));
				Handles.DrawAAPolyLine(2f, pts);
			}
			GUI.EndClip();
		}


		public static void draw_graph2(Rect outer, List<graph_series> series, float x_min, float x_max, float y_min, float y_max)
		{
			const float left = 55f, bottom = 20f, pad = 8f, legend_h = 20f;
			Rect r = new Rect(outer.x + left, outer.y + pad + legend_h, outer.width - left - pad, outer.height - bottom - pad - legend_h);

			EditorGUI.DrawRect(outer, new Color(0.12f, 0.12f, 0.12f));
			EditorGUI.DrawRect(r, new Color(0.17f, 0.17f, 0.17f));
			// legend (click to toggle)
			float lx = outer.x + left;
			foreach (var s in series)
			{
				var content = new GUIContent(s.name);
				float w = EditorStyles.miniLabel.CalcSize(content).x + 28f;
				Rect item = new Rect(lx, outer.y + 4, w, 16);
				EditorGUI.DrawRect(new Rect(item.x + 2, item.y + 4, 8, 8), s.visible ? s.color : new Color(s.color.r, s.color.g, s.color.b, 0.25f));
				s.visible = GUI.Toggle(new Rect(item.x + 14, item.y, w - 14, 16), s.visible, content, EditorStyles.miniLabel);
				lx += w + 6f;
			}

			fit_range(series, ref x_min, ref x_max, ref y_min, ref y_max);
			if (x_max <= x_min || y_max <= y_min) return;

			Vector3 Map(Vector2 p) => new Vector3(
				r.x + Mathf.InverseLerp(x_min, x_max, p.x) * r.width,
				r.yMax - Mathf.InverseLerp(y_min, y_max, p.y) * r.height, 0);

			var x_style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter };
			var y_style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
			Color grid = new Color(1, 1, 1, 0.08f);

			// X ticks + vertical gridlines
			float x_step = nice_step((x_max - x_min) / 8f);
			int xi0 = Mathf.CeilToInt(x_min / x_step), xi1 = Mathf.FloorToInt(x_max / x_step);
			for (int i = xi0; i <= xi1; i++)
			{
				float x = i * x_step;
				float px = Map(new Vector2(x, y_min)).x;
				Handles.color = grid;
				Handles.DrawLine(new Vector3(px, r.y), new Vector3(px, r.yMax));
				GUI.Label(new Rect(px - 25, r.yMax + 2, 50, 16), x.ToString("0.##"), x_style);
			}

			// Y ticks + horizontal gridlines
			float y_step = nice_step((y_max - y_min) / 6f);
			int yi0 = Mathf.CeilToInt(y_min / y_step), yi1 = Mathf.FloorToInt(y_max / y_step);
			for (int i = yi0; i <= yi1; i++)
			{
				float y = i * y_step;
				float py = Map(new Vector2(x_min, y)).y;
				Handles.color = grid;
				Handles.DrawLine(new Vector3(r.x, py), new Vector3(r.xMax, py));
				GUI.Label(new Rect(outer.x, py - 8, left - 4, 16), y.ToString("0.##"), y_style);
			}

			// zero axes (x = 0 and y = 0)
			Handles.color = new Color(1, 1, 1, 0.45f);
			if (x_min < 0 && x_max > 0)
				Handles.DrawLine(Map(new Vector2(0, y_min)), Map(new Vector2(0, y_max)));
			if (y_min < 0 && y_max > 0)
				Handles.DrawLine(Map(new Vector2(x_min, 0)), Map(new Vector2(x_max, 0)));

			// data
			GUI.BeginClip(r, -r.position, Vector2.zero, false); // keeps lines inside the rect
			for (int i = 0; i < series.Count; i++)
			{
				var s = series[i];
				if (!s.visible || s.points.Length < 2) continue;
				Handles.color = s.color;
				Handles.DrawAAPolyLine(2f, System.Array.ConvertAll(s.points, p => Map(p)));
			}
			GUI.EndClip();
		}

		static float nice_step(float raw)
		{
			if (raw <= 0f) return 1f;
			float p = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(raw)));
			float f = raw / p;
			return (f < 1.5f ? 1f : f < 3.5f ? 2f : f < 7.5f ? 5f : 10f) * p;
		}


		public static float field_width = 100f;
		public static float unit_width = 50f;

		public static bool present_one(string field_name, ref float value, string unit1)
		{
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField(field_name);

			bool has_changed = false;

			EditorGUI.BeginChangeCheck();
			value = EditorGUILayout.DelayedFloatField(value, GUILayout.Width(field_width)); //, GUILayout.Width(75f)
			if (EditorGUI.EndChangeCheck()) {
				has_changed = true;
			}
			EditorGUILayout.LabelField(unit1, GUILayout.Width(unit_width));
			EditorGUILayout.EndHorizontal();

			return has_changed;
		}


		public static bool present_two(string field_name, ref float value1, string unit1, ref float value2, string unit2)
		{
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField(field_name);

			bool has_changed = false;

			EditorGUI.BeginChangeCheck();
			value1 = EditorGUILayout.DelayedFloatField(value1, GUILayout.Width(field_width)); //, GUILayout.Width(75f)
			if (EditorGUI.EndChangeCheck()) has_changed = true;
			EditorGUILayout.LabelField(unit1, GUILayout.Width(unit_width));

			EditorGUI.BeginChangeCheck();
			value2 = EditorGUILayout.DelayedFloatField(value2, GUILayout.Width(field_width)); //, GUILayout.Width(75f)
			if (EditorGUI.EndChangeCheck()) has_changed = true;
			EditorGUILayout.LabelField(unit2, GUILayout.Width(unit_width));

			EditorGUILayout.EndHorizontal();

			return has_changed;
		}


		public static bool present_one_with_conversion(string field_name, ref float value1, string unit1, float conversion_factor, string unit2)
		{
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField(field_name);

			bool has_changed = false;

			EditorGUI.BeginChangeCheck();
			value1 = EditorGUILayout.DelayedFloatField(value1, GUILayout.Width(field_width)); //, GUILayout.Width(75f)
			if (EditorGUI.EndChangeCheck()) {
				has_changed = true;
			}
			EditorGUILayout.LabelField(unit1, GUILayout.Width(unit_width));

			EditorGUI.BeginChangeCheck();
			float value_converted = EditorGUILayout.DelayedFloatField(value1 * conversion_factor, GUILayout.Width(field_width)); //, GUILayout.Width(75f)
			if (EditorGUI.EndChangeCheck())
			{
				has_changed = true;
				value1 = value_converted / conversion_factor;
			}
			EditorGUILayout.LabelField(unit2, GUILayout.Width(unit_width));
			EditorGUILayout.EndHorizontal();

			return has_changed;
		}
	}


	// unit conversion
	public static class Units
	{
		public static Dictionary<string, float> speed = new Dictionary<string, float>{
			["m/s"] = 1f,
			["km/h"] = 1f/(1000f / (60f*60f)),
			["kts"] = 1.94384f,
			["mph"] = 2.23694f,
			["ft/s"] = 3.28084f,
		};

		public static Dictionary<string, float> distance = new Dictionary<string, float>{
			["m"] = 1f,
			["km"] = 1000f,
			["ft"] = 3.28084f,
			["mi"] = 5280f / 3.28084f,
		};

		public static Dictionary<string, Dictionary<string, float>> conversions = new Dictionary<string, Dictionary<string, float>>{
			["speed"] = speed,
			["distance"] = distance
		};

		public static Dictionary<string, string[]> key_names = new Dictionary<string, string[]>{
			["speed"] = speed.Keys.ToArray(),
			["distance"] = distance.Keys.ToArray(),
		};
	}


	[Serializable] public class ValueWithUnit
	{
		public string field_name; public string unit_type; public string reference_unit;
		public string[] unit_options; public string[] original_unit_options; public int current_unit_index; public string current_unit;
		public float current_value; public float converted_value;
		public float current_conversion;

		public ValueWithUnit(string set_field_name, string set_unit_type, float set_initial_value, string set_reference_unit, string set_current_unit)
		{
			field_name = set_field_name; unit_type = set_unit_type; reference_unit = set_reference_unit; current_unit = set_current_unit;
			original_unit_options = Units.key_names[unit_type];
			// replace slashes with a similar looking character so the unit selection popup doesn't spawn submenus for no reason
			unit_options = original_unit_options.Select(option => option.Replace('/', '\u2044')).ToArray();
			set_unit_from_name(set_current_unit);
			current_value = set_initial_value; update_conversion();
		}

		public float update_conversion()
		{
			current_conversion = Units.conversions[unit_type][current_unit];
			return update_value(current_value);
		}

		public float update_value(float new_value)
		{
			current_value = new_value; converted_value = new_value * current_conversion;
			return converted_value;
		}

		public void set_unit_from_name(string new_unit)
		{
			int new_index = Array.IndexOf(original_unit_options, new_unit);
			if (new_index > 0)
			{
				current_unit_index = new_index; current_unit = original_unit_options[new_index];
				update_conversion();
			}
		}

		public void set_unit_from_index(int unit_index)
		{
			string new_unit = original_unit_options[unit_index];
			current_unit_index = unit_index; current_unit = new_unit;
			update_conversion();
		}

		public bool present(string unit1 = "", float conversion_mod = 1f)
		{
			EditorGUILayout.BeginHorizontal(); EditorGUILayout.LabelField(field_name);

			bool has_changed = false;

			EditorGUI.BeginChangeCheck();
			current_value = EditorGUILayout.DelayedFloatField("", current_value, GUILayout.Width(UI.field_width)); //, GUILayout.Width(75f)
			if (EditorGUI.EndChangeCheck()) { has_changed = true; }
			EditorGUILayout.LabelField(((unit1 != "") ? unit1 : reference_unit), GUILayout.Width(UI.unit_width));

			EditorGUI.BeginChangeCheck();
			float value_converted = EditorGUILayout.DelayedFloatField("", converted_value * conversion_mod, GUILayout.Width(UI.field_width)); //, GUILayout.Width(75f)
			if (EditorGUI.EndChangeCheck()) { has_changed = true; current_value = (value_converted / conversion_mod) / current_conversion; }

			EditorGUI.BeginChangeCheck();
			int new_selection = EditorGUILayout.Popup(current_unit_index, unit_options, GUILayout.Width(UI.unit_width));
			if (EditorGUI.EndChangeCheck()) set_unit_from_index(new_selection);
			//EditorGUILayout.LabelField(current_unit, GUILayout.Width(UI.unit_width));
			
			EditorGUILayout.EndHorizontal();

			if (has_changed) update_value(current_value);
			return has_changed;
		}

		public bool present_one(string unit1 = "", float conversion_mod = 1f)
		{
			EditorGUILayout.BeginHorizontal(); EditorGUILayout.LabelField(field_name);

			bool has_changed = false;

			EditorGUI.BeginChangeCheck();
			current_value = EditorGUILayout.DelayedFloatField("", current_value, GUILayout.Width(UI.field_width)); //, GUILayout.Width(75f)
			if (EditorGUI.EndChangeCheck()) { has_changed = true; }
			EditorGUILayout.LabelField(((unit1 != "") ? unit1 : reference_unit), GUILayout.Width(UI.unit_width));

			EditorGUILayout.EndHorizontal();
			if (has_changed) update_value(current_value);
			return has_changed;
		}
	}

	public static class AircraftUtils
	{
		public static AircraftData get_data_from_prefab(GameObject prefab_root)
		{
			return new AircraftData(prefab_root);
		}
	}

	public static class Utilities
	{
		public class cg_finder
		{
			public Vector3 running_total; public float sum;
			
			public void add(Vector3 pos, float weight)
			{
				running_total += pos * weight; sum += weight;
			}
			public (Vector3 pos, float sum) average()
			{
				return (running_total / sum, sum);
			}
		}

		public static Type game_type(string class_name) => Assembly.Load("Assembly-CSharp").GetType(class_name);


		public static T find_prop<T>(SerializedObject s_obj, string field)
		{
			return (T)s_obj.FindProperty(field).boxedValue;
		}


		public static SerializedProperty find_prop(SerializedObject s_obj, string field)
		{
			return s_obj.FindProperty(field);
		}


		public static T find_prop_rel<T>(SerializedProperty s_prop, string field)
		{
			return (T)s_prop.FindPropertyRelative(field).boxedValue;
		}


		public static Transform get_child_or_make_empty(GameObject parent, string child_name)
		{
			Transform child = parent.transform.Find(child_name);

			if (child == null)
			{
				GameObject newChild = new GameObject(child_name);
				child = newChild.transform;
				child.SetParent(parent.transform, false);
			}

			return child;
		}


		public static Transform place_reference_empty(GameObject parent, string child_name, Vector3 child_position)
		{
			Transform child = get_child_or_make_empty(parent, child_name);
			child.position = child_position;
			return child;
		}
	}
}