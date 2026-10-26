#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;


namespace hexagonest.Tools
{
	public class AircraftTools : EditorWindow
	{
		[MenuItem("HexTools/Calculate CG")]
		public static void calculate_cg()
		{
			GameObject prefab_root = SimEnvironment.check_in_prefab("Calculate CG"); if (prefab_root == null) return;
			AircraftData aircraft = AircraftUtils.get_data_from_prefab(prefab_root); AircraftPhysics physics = aircraft.physics;

			(Vector3 wet_cg, float wet_mass) = physics.calculate_wet_mass(1);
			Debug.Log($"Aircraft {prefab_root.name} weighs: {physics.dry_mass}kg dry, {wet_mass}kg wet ({physics.max_fuel_mass}kg fuel)");
			GameObject results_object = Utilities.get_child_or_make_empty(prefab_root, "sim_results").gameObject;

			Utilities.place_reference_empty(results_object, "dry_cg", physics.dry_cg);
			if (physics.max_fuel_mass > 0) Utilities.place_reference_empty(results_object, "fuel_cg", physics.max_fuel_cg);
			if(physics.max_fuel_mass > 0) Utilities.place_reference_empty(results_object, "wet_cg", wet_cg);
		}
	}
}


#endif