#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

//using Utilities.AircraftUtils;


namespace hexagonest.Tools
{
	public class BlueprinterCheckCG : EditorWindow
	{
//		[MenuItem("HexTools/Calculate CG")]
//		public static void calculate_cg()
//		{
//			var stage = PrefabStageUtility.GetCurrentPrefabStage();
//			if (stage == null || !stage.prefabContentsRoot)
//			{
//				EditorUtility.DisplayDialog("Evaluation tools can only be used in prefabs", "Open a Prefab first.", "OK");
//				return;
//			}
//			GameObject prefab_root = stage.prefabContentsRoot;
//
//			Type part_type = Utilities.game_type("AeroPart");
//
//			AircraftData aircraft = AircraftUtils.get_data_from_prefab(prefab_root);
//			Debug.Log($"Evaluating {prefab_root.name}");
//
//			Vector3 sum_mass = Vector3.zero, sum_lift = Vector3.zero, sum_drag = Vector3.zero;
//			float total_mass = 0, total_lift = 0, total_drag = 0;
//
//			Array parts = prefab_root.GetComponentsInChildren(part_type, true);
//			foreach (var p in parts)
//			{
//				GameObject go = ((Component)p).gameObject; Transform transform = go.transform;
//				var s_part = new SerializedObject((Component)p);
//
//				float mass = s_part.FindProperty("mass").floatValue;
//				sum_mass += mass * transform.position; total_mass += mass;
//
//				Vector3 center_of_lift = s_part.FindProperty("centerOfLift").vector3Value;
//				Vector3 lift_pos = transform.TransformVector(center_of_lift);
//
//				float wingArea = s_part.FindProperty("wingArea").floatValue;
//				sum_lift += wingArea * lift_pos; total_lift += wingArea;
//
//				float dragArea = s_part.FindProperty("dragArea").floatValue;
//				sum_drag += dragArea * transform.position; total_drag += dragArea;
//			}
//
//			Debug.Log($"Total mass: {total_mass}, wing area: {total_lift}, drag area: {total_drag}");
//			Vector3 center_mass = sum_mass / total_mass;
//			Vector3 center_lift = sum_lift / total_lift;
//			Vector3 center_drag = sum_drag / total_drag;
//
//			float stability_index = center_mass.z - center_lift.z;
//
//			Debug.Log($"Stability index: {stability_index}");
//
//			GameObject results_object = Utilities.get_child_or_make_empty(prefab_root, "sim_results").gameObject;
//
//			Transform center_mass_transform = Utilities.place_reference_empty(results_object, "center_mass", center_mass);
//			Transform center_lift_transform = Utilities.place_reference_empty(results_object, "center_lift", center_lift);
//			Transform center_drag_transform = Utilities.place_reference_empty(results_object, "center_drag", center_drag);
//
//		}
	}
}


#endif