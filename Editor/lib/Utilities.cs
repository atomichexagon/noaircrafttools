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
		public string name = "default";
		public Color color = new Color(1.0f, 1.0f, 1.0f);
		public List<Vector2> _points = new List<Vector2>();
		public Vector2[] points = new Vector2[0];
		public bool visible = true;

		public void add(Vector2 point)
		{
			_points.Add(point);
		}

		public void Add(Vector2 point)
		{
			this.add(point);
		}
		
		public graph_series finish()
		{
			if (_points.Count > 0) points = _points.ToArray();
			return this; // convenience
		}
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
				float w = EditorStyles.miniLabel.CalcSize(content).x + 16f; //28f;
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


		public static float field_width = 148f;
		public static float unit_width = 50f;

		public static float field_width_thin = 80f;
		public static float unit_width_thin = 50f;


		public static void present_separator(string text = "")
		{
			EditorGUILayout.BeginHorizontal();
			if (text != "")
			{
				EditorGUILayout.LabelField("", GUI.skin.horizontalSlider, GUILayout.ExpandWidth(true));
				GUILayout.Label(text, GUILayout.ExpandWidth(false));
			}
			EditorGUILayout.LabelField("", GUI.skin.horizontalSlider, GUILayout.ExpandWidth(true));
			EditorGUILayout.EndHorizontal();
		}


		// supports passing a reference bool so that change tracking doesn't require an extra statement
		public static bool present_field(ref bool change_tracker, ref float value, string unit = "", float conversion = 1f, float f_width = 0f, float u_width = 0f)
		{ bool this_changed = present_field(ref value, unit, conversion, f_width, u_width); change_tracker |= this_changed; return this_changed; }

		// generic float field presenter including optional unit and handling conversions automatically
		// pass "`" as unit to not draw the unit field
		public static bool present_field(ref float value, string unit = "", float conversion = 1f, float f_width = 0f, float u_width = 0f)
		{
			EditorGUI.BeginChangeCheck();
			float new_value = EditorGUILayout.FloatField("", value * conversion, GUILayout.Width(f_width == 0f ? field_width : f_width));
			bool this_changed = EditorGUI.EndChangeCheck();
			if (this_changed) value = new_value / conversion;
			if (unit != "`") EditorGUILayout.LabelField(unit, GUILayout.Width(u_width == 0f ? unit_width : u_width));
			return this_changed;
		}


		// supports passing a reference bool so that change tracking doesn't require an extra statement
		public static bool present_dropdown(ref bool change_tracker, ref int current_index, ref string[] options, float u_width = 0f)
		{ bool changed = present_dropdown(ref current_index, ref options, u_width); change_tracker |= changed; return changed; }

		public static bool present_dropdown(ref int current_index, ref string[] options, float u_width = 0f)
		{
			EditorGUI.BeginChangeCheck();
			current_index = EditorGUILayout.Popup(current_index, options, GUILayout.Width(u_width == 0f ? unit_width : u_width));
			return EditorGUI.EndChangeCheck();
		}


		// supports passing a reference bool so that change tracking doesn't require an extra statement
		public static bool present_one(ref bool change_tracker, string field_name, ref float value, string unit1)
		{ bool changed = present_one(field_name, ref value, unit1); change_tracker |= changed; return changed; }

		public static bool present_one(string field_name, ref float value, string unit1)
		{
			EditorGUILayout.BeginHorizontal();
			GUILayout.Label(field_name, GUILayout.ExpandWidth(true));
			GUILayout.FlexibleSpace();

			bool has_changed = false;
			present_field(ref has_changed, ref value, unit1);
			EditorGUILayout.EndHorizontal();

			return has_changed;
		}



		// supports passing a reference bool so that change tracking doesn't require an extra statement
		public static bool present_two(ref bool change_tracker, string field_name, ref float value1, string unit1, ref float value2, string unit2)
		{ bool changed = present_two(field_name, ref value1, unit1, ref value2, unit2); change_tracker |= changed; return changed; }

		public static bool present_two(string field_name, ref float value1, string unit1, ref float value2, string unit2)
		{
			EditorGUILayout.BeginHorizontal();
			GUILayout.Label(field_name, GUILayout.ExpandWidth(true));
			GUILayout.FlexibleSpace();

			bool has_changed = false;
			present_field(ref has_changed, ref value1, unit1);
			present_field(ref has_changed, ref value2, unit2);

			EditorGUILayout.EndHorizontal();

			return has_changed;
		}



		// supports passing a reference bool so that change tracking doesn't require an extra statement
		public static bool present_one_with_conversion(ref bool change_tracker, string field_name, ref float value1, string unit1, float conversion_factor, string unit2)
		{ bool changed = present_one_with_conversion(field_name, ref value1, unit1, conversion_factor, unit2); change_tracker |= changed; return changed; }

		public static bool present_one_with_conversion(string field_name, ref float value1, string unit1, float conversion_factor, string unit2)
		{
			EditorGUILayout.BeginHorizontal();
			GUILayout.Label(field_name, GUILayout.ExpandWidth(true));
			GUILayout.FlexibleSpace();

			bool has_changed = false;
			
			present_field(ref has_changed, ref value1, unit1);
			present_field(ref has_changed, ref value1, unit2, conversion_factor);

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


	public abstract class IParameter
	{
		public string field_name;
		public abstract bool present(ref bool change_tracker);
		public abstract bool present();
	}

	public abstract class IParameterFixed : IParameter
	{
		public float current_value;
		public virtual void update_value(float new_value) => current_value = new_value;
	}

	public abstract class IParameterRange : IParameter
	{
		public IParameterFixed min, max;
		public virtual void update_value(float new_min, float new_max)
		{
			min.update_value(new_min);
			max.update_value(new_max);
			verify_changes();
		}

		protected virtual void verify_changes()
		{
			if (min.current_value > max.current_value) max.update_value(min.current_value + 1);
			if (max.current_value < min.current_value) min.update_value(max.current_value - 1);
		}
	}



	[Serializable] public class ParameterRange : IParameterRange
	{
		public ParameterRange(string _field_name, float _initial_min, float _initial_max)
		{
			field_name = _field_name;
			min = new ParameterFixed(field_name, _initial_min);
			max = new ParameterFixed(field_name, _initial_max);
		}

		public override bool present(ref bool change_tracker)
		{ bool changed = present(); change_tracker |= changed; return changed; }

		public override bool present()
		{
			bool has_changed = false;
			EditorGUILayout.BeginHorizontal();
			GUILayout.Label(field_name, GUILayout.ExpandWidth(true));
			GUILayout.FlexibleSpace();
			UI.present_field(ref has_changed, ref min.current_value, "Min");
			UI.present_field(ref has_changed, ref max.current_value, "Max");
			EditorGUILayout.EndHorizontal();

			if (has_changed) verify_changes();
			return has_changed;
		}
	}


	// unitless fixed parameter
	[Serializable] public class ParameterFixed : IParameterFixed
	{
		public ParameterFixed(string _field_name, float _initial_value)
		{
			field_name = _field_name; current_value = _initial_value;
		}

		public override bool present(ref bool change_tracker)
		{ bool changed = present(); change_tracker |= changed; return changed; }

		public override bool present()
		{
			bool has_changed = false;
			EditorGUILayout.BeginHorizontal();
			GUILayout.Label(field_name, GUILayout.ExpandWidth(true));
			GUILayout.FlexibleSpace();
			UI.present_field(ref has_changed, ref current_value, "`");
			EditorGUILayout.EndHorizontal();

			if (has_changed) update_value(current_value);
			return has_changed;
		}
	}


	// parameter range with changeable units
	[Serializable] public class ParameterRangeUnit : IParameterRange
	{
		public string unit_type, reference_unit, current_unit;
		public ParameterFixedUnit _min, _max;
		public int current_unit_index;

		public ParameterRangeUnit(string _field_name, string _unit_type, float _initial_min, float _initial_max, string _reference_unit, string _current_unit)
		{
			field_name = _field_name; unit_type = _unit_type; reference_unit = _reference_unit; current_unit = _current_unit;
			_min = new ParameterFixedUnit($"{field_name} (min)", unit_type, _initial_min, reference_unit, current_unit);
			_max = new ParameterFixedUnit($"{field_name} (max)", unit_type, _initial_max, reference_unit, current_unit);

			min = (IParameterFixed)_min; max = (IParameterFixed)_max;
			current_unit_index = _min.current_unit_index;
		}

		protected override void verify_changes()
		{
			if (_min.current_value > _max.current_value) _max.update_value(_min.current_value + 1);
			if (_max.current_value < _min.current_value) _min.update_value(_max.current_value - 1);

			if (_min.current_unit_index != current_unit_index) { _max.set_unit_from_index(_min.current_unit_index); current_unit_index = _min.current_unit_index; }
			if (_max.current_unit_index != current_unit_index) { _min.set_unit_from_index(_max.current_unit_index); current_unit_index = _max.current_unit_index; }
		}

		// default (change tracking) presenter
		public override bool present(ref bool change_tracker)
		{ bool changed = present(); change_tracker |= changed; return changed; }

		// default presenter
		public override bool present()
		{
			return present_two();
		}


		// supports passing a reference bool so that change tracking doesn't require an extra statement
		public bool present_extra(ref bool change_tracker, ref bool used_extra_value, string unit1 = "", string unit_extra = "", float _extra_conversion = 1f)
		{ bool changed = present_extra(ref used_extra_value, unit1, unit_extra, _extra_conversion); change_tracker |= changed; return changed; }

		public bool present_extra(ref bool used_extra_value, string unit1 = "", string unit_extra = "", float _extra_conversion = 1f)
		{
			bool changed = false;
			_min.present_extra(ref changed, ref used_extra_value, unit1, unit_extra, _extra_conversion);
			_max.present_extra(ref changed, ref used_extra_value, unit1, unit_extra, _extra_conversion);

			if (changed) verify_changes();
			return changed;
		}


		// supports passing a reference bool so that change tracking doesn't require an extra statement
		public bool present_two(ref bool change_tracker, string unit1 = "", float conversion_mod = 1f)
		{ bool changed = present_two(unit1, conversion_mod); change_tracker |= changed; return changed; }

		public bool present_two(string unit1 = "", float conversion_mod = 1f)
		{
			bool changed = false;
			_min.present_two(ref changed, unit1, conversion_mod);
			_max.present_two(ref changed, unit1, conversion_mod);

			if (changed) verify_changes();
			return changed;
		}


		// supports passing a reference bool so that change tracking doesn't require an extra statement
		public bool present_one(ref bool change_tracker, string unit1 = "")
		{ bool changed = present_one(unit1); change_tracker |= changed; return changed; }

		public bool present_one(string unit1 = "")
		{
			bool changed = false;
			_min.present_one(ref changed, unit1);
			_max.present_one(ref changed, unit1);

			if (changed) verify_changes();
			return changed;
		}
	}


	// fixed parameter with changeable units
	[Serializable] public class ParameterFixedUnit : IParameterFixed
	{
		public string unit_type, reference_unit, current_unit;
		public string[] unit_options, original_unit_options; public int current_unit_index;
		public float converted_value, current_conversion, extra_value, extra_conversion;

		public ParameterFixedUnit(string _field_name, string _unit_type, float _initial_value, string _reference_unit, string _current_unit)
		{
			field_name = _field_name; unit_type = _unit_type; reference_unit = _reference_unit; current_unit = _current_unit;
			original_unit_options = Units.key_names[unit_type];
			// replace slashes with a similar looking character so the unit selection popup doesn't spawn submenus for no reason
			unit_options = original_unit_options.Select(option => option.Replace('/', '\u2044')).ToArray();
			set_unit_from_name(_current_unit);
			current_value = _initial_value; update_conversion();
			extra_value = _initial_value; extra_conversion = 1f;
		}

		public void update_conversion()
		{
			current_conversion = Units.conversions[unit_type][current_unit];
			update_value(current_value);
		}

		public override void update_value(float new_value)
		{
			current_value = new_value; converted_value = new_value * current_conversion;
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


		// default (change tracking) presenter
		public override bool present(ref bool change_tracker)
		{ bool changed = present(); change_tracker |= changed; return changed; }

		// default presenter
		public override bool present()
		{
			return present_two();
		}


		// supports passing a reference bool so that change tracking doesn't require an extra statement
		public bool present_extra(ref bool change_tracker, ref bool used_extra_value, string unit1 = "", string unit_extra = "", float _extra_conversion = 1f)
		{ bool changed = present_extra(ref used_extra_value, unit1, unit_extra, _extra_conversion); change_tracker |= changed; return changed; }

		// unconverted, converted, and an extra one to spice things up
		public bool present_extra(ref bool used_extra_value, string unit1 = "", string unit_extra = "", float _extra_conversion = 1f)
		{
			EditorGUILayout.BeginHorizontal();
			GUILayout.Label(field_name, GUILayout.ExpandWidth(true));
			GUILayout.FlexibleSpace();

			extra_conversion = _extra_conversion; // replace previous

			bool has_changed = false;

			bool changed_normal = UI.present_field(ref has_changed, ref current_value, (unit1 != "") ? unit1 : reference_unit, 1f, UI.field_width_thin, UI.unit_width_thin);
			changed_normal |= UI.present_field(ref has_changed, ref current_value, "`", current_conversion, UI.field_width_thin, UI.unit_width_thin);
			if (changed_normal) { extra_value = current_value * extra_conversion; used_extra_value = false; }
			if (UI.present_dropdown(ref has_changed, ref current_unit_index, ref unit_options, UI.unit_width_thin)) set_unit_from_index(current_unit_index);

			float new_extra = current_value * extra_conversion;
			if (UI.present_field(ref has_changed, ref new_extra, unit_extra, 1f, UI.field_width_thin, UI.unit_width_thin))
			{
				extra_value = new_extra; current_value = new_extra / extra_conversion;
				used_extra_value = true;
			}
			EditorGUILayout.EndHorizontal();

			if (has_changed) update_value(current_value);
			return has_changed;
		}


		// supports passing a reference bool so that change tracking doesn't require an extra statement
		public bool present_two(ref bool change_tracker, string unit1 = "", float conversion_mod = 1f)
		{ bool changed = present_two(unit1, conversion_mod); change_tracker |= changed; return changed; }

		// default present, unconverted and converted fields
		public bool present_two(string unit1 = "", float conversion_mod = 1f)
		{
			EditorGUILayout.BeginHorizontal();
			GUILayout.Label(field_name, GUILayout.ExpandWidth(true));
			GUILayout.FlexibleSpace();

			bool has_changed = false;
			UI.present_field(ref has_changed, ref current_value, (unit1 != "") ? unit1 : reference_unit);
			UI.present_field(ref has_changed, ref current_value, "`", current_conversion * conversion_mod);
			if (UI.present_dropdown(ref has_changed, ref current_unit_index, ref unit_options)) set_unit_from_index(current_unit_index);
			
			EditorGUILayout.EndHorizontal();

			if (has_changed) update_value(current_value);
			return has_changed;
		}
		

		// supports passing a reference bool so that change tracking doesn't require an extra statement
		public bool present_one(ref bool change_tracker, string unit1 = "")
		{ bool changed = present_one(unit1); change_tracker |= changed; return changed; }

		// present unconverted value
		public bool present_one(string unit1 = "")
		{
			bool has_changed = false;
			EditorGUILayout.BeginHorizontal();
			GUILayout.Label(field_name, GUILayout.ExpandWidth(true));
			GUILayout.FlexibleSpace();
			UI.present_field(ref has_changed, ref current_value, (unit1 != "") ? unit1 : reference_unit);
			EditorGUILayout.EndHorizontal();

			if (has_changed) update_value(current_value);
			return has_changed;
		}
	}


	[Serializable] public class ControlInputsPanel
	{
		public float pitch, roll, yaw, throttle, brake, custom_axis1;
		public bool gear, showing = false;
		public ControlInputs inputs_obj
		{
			get => new ControlInputs {
				pitch = pitch, roll = roll, yaw = yaw, throttle = throttle, brake = brake, custom_axis1 = custom_axis1, gear = gear
			};
		}

		public ControlInputsPanel(
			float _pitch = 0, float _roll = 0, float _yaw = 0, float _throttle = 0.5f,
			float _brake = 0, float _custom_axis1 = 0, bool _gear = false
		)
		{
			pitch = _pitch; roll = _roll; yaw = _yaw; throttle = _throttle; brake = _brake; custom_axis1 = _custom_axis1; gear = _gear;
		}

		public bool present(bool safe_to_edit = true, bool foldable = false)
		{
			EditorGUILayout.BeginHorizontal();
			string label_text = $"Aircraft controls{(!safe_to_edit ? " (unavailable outside of wind tunnel)" : "")}";
			if (foldable) showing = EditorGUILayout.Foldout(showing, label_text);
			else EditorGUILayout.LabelField(label_text);
			GUILayout.FlexibleSpace();
			EditorGUI.BeginChangeCheck();
			if (GUILayout.Button("Reset")) { pitch = 0; roll = 0; yaw = 0; throttle = 0.5f; brake = 0; custom_axis1 = 0; }
			EditorGUILayout.EndHorizontal();

			if (showing || !foldable)
			{
				pitch = EditorGUILayout.Slider("Pitch (+ down)", pitch, -1, 1);
				roll = EditorGUILayout.Slider("Roll (+ right)", roll, -1, 1);
				yaw = EditorGUILayout.Slider("Yaw (+ right)", yaw, -1, 1);
				throttle = EditorGUILayout.Slider("Throttle", throttle, 0, 1);
				brake = EditorGUILayout.Slider("Brake", brake, 0, 1);
				custom_axis1 = EditorGUILayout.Slider("CustomAxis1", custom_axis1, 0, 1);
				gear = EditorGUILayout.Toggle("Gear (not animated)", gear);
				return EditorGUI.EndChangeCheck();
			}
			EditorGUI.EndChangeCheck();
			return false;
		}
	}

	public static class AircraftUtils
	{
		public static AircraftData get_data_from_prefab(GameObject prefab_root)
		{
			return new AircraftData(prefab_root);
		}


		public static UnityEngine.Object get_definition_from_root(GameObject aircraft_root)
		{
			var aircraft_comp = aircraft_root.GetComponent(game_type("Aircraft"));
			if (aircraft_comp == null) return null;

			return find_prop<UnityEngine.Object>(new SerializedObject(aircraft_comp), "definition");
		}


		public static GameObject get_prefab_from_root(GameObject aircraft_root)
		{
			var def_obj = get_definition_from_root(aircraft_root);
			if (def_obj == null) return null;

			return find_prop<GameObject>(new SerializedObject(def_obj), "unitPrefab");
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

		public static SerializedProperty find_prop_rel(SerializedProperty s_prop, string field)
		{
			return s_prop.FindPropertyRelative(field);
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