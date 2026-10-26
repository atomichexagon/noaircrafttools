#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;

using hexagonest.Tools;
using static hexagonest.Tools.Utilities;


namespace hexagonest.Validator
{
	internal static class ValidationUtils
	{
		// only use if prefab is guaranteed to exist (e.g. in a validator using [Requires(typeof(AircraftDefinitionValidator))])
		static public GameObject get_aircraft_root(UnityEngine.Object aircraft_definition)
		{
			if (aircraft_definition == null) return null;
			var ser_def = new SerializedObject(aircraft_definition);
			return find_prop<GameObject>(ser_def, "unitPrefab");
		}


		static public bool check_reference(Component component, string field_name, UnityEngine.Object other_comp)
			=> check_reference(new SerializedObject(component), field_name, other_comp);

		static public bool check_reference(SerializedObject ser_comp, string field_name, UnityEngine.Object other_comp)
		{
			return (find_prop<UnityEngine.Object>(ser_comp, field_name) == other_comp);
		}


		static public T resolve_stage<T>(T asset_obj) where T : UnityEngine.Object
		{
			if (asset_obj is not Component asset_comp) return asset_obj;

			var stage = PrefabStageUtility.GetCurrentPrefabStage();
			if (stage == null) return asset_obj;

			var asset_root = asset_comp.transform.root;
			if (AssetDatabase.GetAssetPath(asset_root.gameObject) != stage.assetPath) return asset_obj;

			var path = AnimationUtility.CalculateTransformPath(asset_comp.transform, asset_root);
			var tf = string.IsNullOrEmpty(path) ? stage.prefabContentsRoot.transform : stage.prefabContentsRoot.transform.Find(path);
			if (tf == null) return asset_obj;

			var all_asset = asset_comp.GetComponents(asset_comp.GetType());
			int index = Array.IndexOf(all_asset, asset_comp);
			var all_stage = tf.GetComponents(asset_comp.GetType());
			return index >= 0 && index < all_stage.Length ? (T)(UnityEngine.Object)all_stage[index] : asset_obj;
		}


		static public bool update_object(UnityEngine.Object asset_obj, Action<SerializedObject> update, string action_taken)
		{
			var target = resolve_stage(asset_obj);
			var ser_obj = new SerializedObject(target);
			update(ser_obj);
			ser_obj.ApplyModifiedProperties();
			EditorUtility.SetDirty(target);

			var stage = PrefabStageUtility.GetCurrentPrefabStage();
			if (target is Component && stage != null) EditorSceneManager.MarkSceneDirty(stage.scene);
			else if (EditorUtility.IsPersistent(target)) AssetDatabase.SaveAssets();

			InternalEditorUtility.RepaintAllViews();
			return true;
		}

		static public bool update_field(UnityEngine.Object asset_obj, string field_name, Action<SerializedProperty> update, string action_taken)
			=> update_object(asset_obj, obj => update(find_prop(obj, field_name)), action_taken);

		static public bool update_field(UnityEngine.Object asset_obj, string field_name, UnityEngine.Object asset_value, string action_taken)
			=> update_field(asset_obj, field_name, prop => prop.objectReferenceValue = resolve_stage(asset_value), action_taken);

		static public bool update_field(UnityEngine.Object asset_obj, string field_name, string value, string action_taken)
			=> update_field(asset_obj, field_name, prop => prop.stringValue = value, action_taken);

		static public bool update_field(UnityEngine.Object asset_obj, string field_name, float value, string action_taken)
			=> update_field(asset_obj, field_name, prop => prop.floatValue = value, action_taken);

		static public bool update_field(UnityEngine.Object asset_obj, string field_name, int value, string action_taken)
			=> update_field(asset_obj, field_name, prop => prop.intValue = value, action_taken);
	}

	// verifies prefab root
	[Requires(typeof(AircraftDefinitionValidator))]
	class Prefab_AircraftRootValidator : IValidator
	{
		public string Name => "AircraftRoot";
		
		public ValidatorResult evaluate(UnityEngine.Object aircraft_definition)
		{
			GameObject prefab = ValidationUtils.get_aircraft_root(aircraft_definition);
			ValidatorResult results = new ValidatorResult(Name, prefab.transform);

			using var ser_def = new SerializedObject(aircraft_definition);

			Component aircraft_comp = prefab.GetComponent(game_type("Aircraft"));
			results.critical("", "", aircraft_comp == null, "missing Aircraft component on root", prefab);
			if (aircraft_comp == null) return results;

			// Check aircraft component
			var ser_aircraft = new SerializedObject(aircraft_comp);

			bool def_ref = ValidationUtils.check_reference(ser_aircraft, "definition", aircraft_definition);
			results.critical("Aircraft", "definition", !def_ref, "Aircraft.Definition missing or wrong", prefab,
				new List<Fix>{ new Fix($"Auto set", () => ValidationUtils.update_field(aircraft_comp, "definition", aircraft_definition, "auto set Aircraft.definition")) });
			if (!def_ref) return results;
			
			Component cockpit_part = find_prop<Component>(ser_aircraft, "cockpit");
			results.critical("Aircraft", "cockpit", cockpit_part == null, "missing Aircraft.Cockpit", prefab);
			if (cockpit_part == null) return results;

			Component[] wm_comps = prefab.GetComponentsInChildren(game_type("WeaponManager"), true);
			results.critical("", "", wm_comps.Length == 0, "no objects with WeaponManager component", prefab);
			if (wm_comps.Length == 0) return results;
			results.critical("", "", wm_comps.Length > 1, "multiple objects with WeaponManager component", prefab);
			if (wm_comps.Length > 1) return results;

			Component wm_comp = wm_comps[0];

			Component weapon_manager = find_prop<Component>(ser_aircraft, "weaponManager");
			results.critical("Aircraft", "weaponManager", weapon_manager == null, "missing Aircraft.WeaponManager", prefab,
				wm_comp != null ? new List<Fix>{ new Fix($"Auto set (found: {wm_comp.gameObject.name})",
				() => ValidationUtils.update_field(aircraft_comp, "weaponManager", wm_comp, "auto set Aircraft.weaponManager")) } : null);
			if (weapon_manager == null) return results;
			
			results.fail("Aircraft", "MapUniqueName", find_prop<string>(ser_aircraft, "MapUniqueName") != "", "MapUniqueName should be blank", prefab,
				new List<Fix>{ new Fix("Clear field", () => ValidationUtils.update_field(aircraft_comp, "MapUniqueName", "", "fix Aircraft.MapUniqueName")) });
			results.fail("Aircraft", "MapHQ", find_prop<UnityEngine.Object>(ser_aircraft, "MapHQ") != null, "MapHQ should be blank", prefab,
				new List<Fix>{ new Fix("Clear field", () => ValidationUtils.update_field(aircraft_comp, "MapHQ", (UnityEngine.Object)null, "fix Aircraft.MapHQ")) });
			results.fail("Aircraft", "MapAirbase", find_prop<UnityEngine.Object>(ser_aircraft, "MapAirbase") != null, "MapAirbase should be blank", prefab,
				new List<Fix>{ new Fix("Clear field", () => ValidationUtils.update_field(aircraft_comp, "MapAirbase", (UnityEngine.Object)null, "fix Aircraft.MapAirbase")) });
			results.info("Aircraft", "ecmIntensity", find_prop<float>(ser_aircraft, "ecmIntensity") > 0, "EcmIntensity should be zero", prefab,
				new List<Fix>{ new Fix("Clear field", () => ValidationUtils.update_field(aircraft_comp, "ecmIntensity", 0f, "fix Aircraft.ecmIntensity")) });

			// aircraft network settings
			bool sync_from = !check_list(find_prop_enums(ser_aircraft, "SyncSettings.From", game_type("Mirage.SyncFrom")), new List<string>{"Server"});
			results.warn("Aircraft", "SyncSettings", sync_from, "Aircraft.SyncSettings.From should be left default (Server)", prefab);
			bool sync_to = !check_list(find_prop_enums(ser_aircraft, "SyncSettings.To", game_type("Mirage.SyncTo")), new List<string>{"Owner", "ObserversOnly", "OwnerAndObservers"});
			results.warn("Aircraft", "SyncSettings", sync_to, "Aircraft.SyncSettings.To should be left default (OwnerAndObservers)", prefab);
			bool sync_timing = !check_list(find_prop_enums(ser_aircraft, "SyncSettings.Timing", game_type("Mirage.SyncTiming")), new List<string>{"Variable"});
			results.warn("Aircraft", "SyncSettings", sync_timing, "Aircraft.SyncSettings.Timing should be left default (Variable)", prefab);
			bool sync_interval = !fuzzy_eq(find_prop<float>(ser_aircraft, "SyncSettings.Interval"), 0.1f, 0.001f);
			results.warn("Aircraft", "SyncSettings", sync_interval, "Aircraft.SyncSettings.Interval should be left default (0.1)", prefab);

			bool sync_failed = sync_from || sync_to || sync_timing || sync_interval;
			results.warn("Aircraft", "SyncSettings", sync_failed, "Aircraft.SyncSettings misconfigured", prefab,
				new List<Fix>{ new Fix("Set default", () => ValidationUtils.update_object(aircraft_comp, obj => {
					set_enum(find_prop(obj, "SyncSettings.From"), game_type("Mirage.SyncFrom"), "Server");
					set_enum(find_prop(obj, "SyncSettings.To"), game_type("Mirage.SyncTo"), "OwnerAndObservers");
					set_enum(find_prop(obj, "SyncSettings.Timing"), game_type("Mirage.SyncTiming"), "Variable");
					find_prop(obj, "SyncSettings.Interval").floatValue = 0.1f;
				}, "set Aircraft.SyncSettings to default"))});

			
			// Check components on root
			Component aero_part = prefab.GetComponent(game_type("AeroPart"));
			results.critical("AeroPart", "", aero_part == null, "missing AeroPart component on root", prefab);
			if (aero_part == null) return results;

			Rigidbody rigidbody = prefab.GetComponent<Rigidbody>();
			results.critical("", "", rigidbody == null, "missing Rigidbody on root", prefab);
			if (rigidbody == null) return results;

			Collider[] colliders = prefab.GetComponents<Collider>();
			results.critical("", "", colliders.Length == 0, "missing Collider (any type) on root", prefab);
			if (colliders.Length == 0) return results;

			
			// Cockpit
			Component[] cockpit_comps = prefab.GetComponentsInChildren(game_type("Cockpit"), true);
			results.critical("", "", cockpit_comps.Length == 0, "no objects with Cockpit component", prefab);
			if (cockpit_comps.Length == 0) return results;
			results.critical("", "", cockpit_comps.Length > 1, "multiple objects with Cockpit component", prefab);
			if (cockpit_comps.Length > 1) return results;

			Component cockpit_comp = cockpit_comps[0];

			var ser_cockpit = new SerializedObject(cockpit_comp);
			var go_cockpit = cockpit_comp.gameObject;
			bool cockpit_aircraft_ref = ValidationUtils.check_reference(ser_cockpit, "aircraft", aircraft_comp);
			results.critical("Cockpit", "aircraft", !cockpit_aircraft_ref, "Cockpit.Aircraft missing or wrong", go_cockpit,
				new List<Fix>{ new Fix($"Auto set", () => ValidationUtils.update_field(cockpit_comp, "aircraft", aircraft_comp, "auto set Cockpit.aircraft")) });
			if (!cockpit_aircraft_ref) return results;

			// NetworkIdentity
			string net_identity = "Mirage.NetworkIdentity";
			Component network_identity = prefab.GetComponent(game_type(net_identity));
			results.critical("", "", network_identity == null, "missing NetworkIdentity component on root", prefab);
			if (network_identity == null) return results;

			using var ser_net_ident = new SerializedObject(network_identity);
			bool net_send_pos = find_prop<bool>(ser_net_ident, "SpawnSettings.SendPosition") != true;
			results.warn("NetworkIdentity", "SpawnSettings", net_send_pos, "NetworkIdentity.SendPosition should be left default (true)", prefab);
			bool net_send_rot = find_prop<bool>(ser_net_ident, "SpawnSettings.SendRotation") != true;
			results.warn("NetworkIdentity", "SpawnSettings", net_send_rot, "NetworkIdentity.SendRotation should be left default (true)", prefab);
			bool net_send_scale = find_prop<bool>(ser_net_ident, "SpawnSettings.SendScale") != false;
			results.warn("NetworkIdentity", "SpawnSettings", net_send_scale, "NetworkIdentity.SendScale should be left default (false)", prefab);
			bool net_send_name = find_prop<bool>(ser_net_ident, "SpawnSettings.SendName") != false;
			results.warn("NetworkIdentity", "SpawnSettings", net_send_name, "NetworkIdentity.SendName should be left default (false)", prefab);
			bool net_send_active = !check_list(find_prop_enums(ser_net_ident, "SpawnSettings.SendActive", game_type("Mirage.SyncActiveOption")), new List<string>{"ForceEnable"});
			results.warn("NetworkIdentity", "SpawnSettings", net_send_active, "NetworkIdentity.SendActive should be left default (ForceEnable)", prefab);
			
			bool net_ident_failed = net_send_pos || net_send_rot || net_send_scale || net_send_name || net_send_active;
			results.warn("NetworkIdentity", "SpawnSettings", net_ident_failed, "NetworkIdentity.SpawnSettings misconfigured", prefab,
				new List<Fix>{ new Fix("Set default", () => ValidationUtils.update_object(network_identity, obj => {
					find_prop(obj, "SpawnSettings.SendPosition").boolValue = true;
					find_prop(obj, "SpawnSettings.SendRotation").boolValue = true;
					find_prop(obj, "SpawnSettings.SendScale").boolValue = false;
					find_prop(obj, "SpawnSettings.SendName").boolValue = false;
					set_enum(find_prop(obj, "SpawnSettings.SendActive"), game_type("Mirage.SyncActiveOption"), "ForceEnable");
				}, "set NetworkIdentity.SpawnSettings to default"))});


			// AircraftNetworkTransform
			string net_transform = "NuclearOption.NetworkTransforms.AircraftNetworkTransform";
			Component network_transform = prefab.GetComponent(game_type(net_transform));
			results.critical("", "", network_transform == null, "missing AircraftNetworkTransform component on root", prefab);
			if (network_transform == null) return results;

			var ser_net = new SerializedObject(network_transform);
			bool net_aircraft_ref = ValidationUtils.check_reference(ser_net, "Aircraft", aircraft_comp);
			results.critical(net_transform, "Aircraft", !net_aircraft_ref, "AircraftNetworkTransform.Aircraft missing or wrong", prefab,
				new List<Fix>{ new Fix($"Auto set", () => ValidationUtils.update_field(network_transform, "Aircraft", aircraft_comp, "auto set AircraftNetworkTransform.Aircraft")) });
			if (!net_aircraft_ref) return results;

			bool net_sync_from = !check_list(find_prop_enums(ser_net, "SyncSettings.From", game_type("Mirage.SyncFrom")), new List<string>{"Server"});
			results.warn("AircraftNetworkTransform", "SyncSettings", net_sync_from, "AircraftNetworkTransform.SyncSettings.From should be left default (Server)", prefab);
			bool net_sync_to = !check_list(find_prop_enums(ser_net, "SyncSettings.To", game_type("Mirage.SyncTo")), new List<string>{"Owner", "ObserversOnly", "OwnerAndObservers"});
			results.warn("AircraftNetworkTransform", "SyncSettings", net_sync_to, "AircraftNetworkTransform.SyncSettings.To should be left default (OwnerAndObservers)", prefab);
			bool net_sync_timing = !check_list(find_prop_enums(ser_net, "SyncSettings.Timing", game_type("Mirage.SyncTiming")), new List<string>{"Variable"});
			results.warn("AircraftNetworkTransform", "SyncSettings", net_sync_timing, "AircraftNetworkTransform.SyncSettings.Timing should be left default (Variable)", prefab);
			bool net_sync_interval = !fuzzy_eq(find_prop<float>(ser_net, "SyncSettings.Interval"), 0.05f, 0.001f);
			results.warn("AircraftNetworkTransform", "SyncSettings", net_sync_interval, "AircraftNetworkTransform.SyncSettings.Interval should be left default (0.05)", prefab);
			
			bool net_sync_failed = net_sync_from || net_sync_to || net_sync_timing || net_sync_interval;
			results.warn("AircraftNetworkTransform", "SyncSettings", net_sync_failed, "AircraftNetworkTransform.SyncSettings misconfigured", prefab,
				new List<Fix>{ new Fix("Set default", () => ValidationUtils.update_object(network_transform, obj => {
					set_enum(find_prop(obj, "SyncSettings.From"), game_type("Mirage.SyncFrom"), "Server");
					set_enum(find_prop(obj, "SyncSettings.To"), game_type("Mirage.SyncTo"), "OwnerAndObservers");
					set_enum(find_prop(obj, "SyncSettings.Timing"), game_type("Mirage.SyncTiming"), "Variable");
					find_prop(obj, "SyncSettings.Interval").floatValue = 0.05f;
				}, "set AircraftNetworkTransform.SyncSettings to default"))});

			results.warn("Aircraft", "extrapolationFactor", !fuzzy_eq(find_prop<float>(ser_net, "extrapolationFactor"), 1f, 0.001f), "AircraftNetworkTransform.ExtrapolationFactor should be left default (1)", prefab,
				new List<Fix>{ new Fix("Set default", () => ValidationUtils.update_field(network_transform, "extrapolationFactor", 1f, "fix AircraftNetworkTransform.extrapolationFactor")) });
			results.warn("Aircraft", "SendInputsInterval", find_prop<int>(ser_net, "SendInputsInterval") != 4, "AircraftNetworkTransform.SendInputsInterval should be left default (4)", prefab,
				new List<Fix>{ new Fix("Set default", () => ValidationUtils.update_field(network_transform, "SendInputsInterval", (int)4, "fix AircraftNetworkTransform.SendInputsInterval")) });
			
			// Pilots
			var pilots = find_prop(ser_aircraft, "pilots");
			results.info("Aircraft", "pilots", pilots.arraySize == 0, "must have at least one pilot (i think)", prefab);

			// duplicate name checker
			var dupes = prefab.GetComponentsInChildren<Transform>(true)
				.GroupBy(t => hierarchy_path(t, prefab.transform))
				.Where(g => g.Key != "" && g.Count() > 1);
			foreach (var g in dupes)
				results.warn("", "", true, $"Duplicate part names at '{g.Key}'", prefab);

			return results;
		}
	}




	[Requires(typeof(AircraftDefinitionValidator))]
	[Requires(typeof(AircraftParametersValidator))]
	[Requires(typeof(Prefab_AircraftRootValidator))]
	class Prefab_AeroPartValidator : IValidator
	{
		public string Name => "AeroParts";
		
		public ValidatorResult evaluate(UnityEngine.Object aircraft_definition)
		{
			GameObject prefab = ValidationUtils.get_aircraft_root(aircraft_definition);
			ValidatorResult results = new ValidatorResult(Name, prefab.transform);

			using var ser_def = new SerializedObject(aircraft_definition);
			var aircraft_parameters = find_prop<UnityEngine.Object>(ser_def, "aircraftParameters");
			using var ser_params = new SerializedObject(aircraft_parameters);

			var airfoils = find_prop(ser_params, "airfoils");
			int num_airfoils = airfoils.arraySize;

			Component aircraft_comp = prefab.GetComponent(game_type("Aircraft"));

			foreach (Component aero_part in prefab.GetComponentsInChildren(game_type("AeroPart"), true))
			{
				GameObject go = aero_part.gameObject;
				var ser_part = new SerializedObject(aero_part);

				bool part_aircraft_ref = ValidationUtils.check_reference(ser_part, "parentUnit", aircraft_comp);
				results.critical("AeroPart", "parentUnit", !part_aircraft_ref, "ParentUnit missing", go,
					new List<Fix>{ new Fix($"Auto set", () => ValidationUtils.update_field(aero_part, "parentUnit", aircraft_comp, "auto set AeroPart.parentUnit")) });
				if (!part_aircraft_ref) return results;

				int airfoil_id = find_prop<int>(ser_part, "airfoil");
				results.critical("AeroPart", "airfoil", airfoil_id >= num_airfoils, $"Airfoil index out of bounds ({airfoil_id}/{num_airfoils-1})", go);
				if (airfoil_id >= num_airfoils) return results;

				results.fail("AeroPart", "mass", find_prop<float>(ser_part, "mass") <= 0, "Mass <= 0", go);
				results.fail("AeroPart", "wingArea", find_prop<float>(ser_part, "wingArea") < 0, "WingArea < 0", go); // zero is fine, <0?
				// this should be a fail but vanilla aircraft do it constantly
				results.warn("AeroPart", "dragArea", find_prop<float>(ser_part, "dragArea") <= 0, "DragArea <= 0", go);
				// validate the Ibis to see how much respect vanilla aircraft have for "rules" in this case
				AudioClip bullet_impact = find_asset<AudioClip>("bulletImpact1_PLACEHOLDER");
				results.info("AeroPart", "hitSound", find_prop<UnityEngine.Object>(ser_part, "hitSound") == null, "HitSound missing", go,
					new List<Fix>{ new Fix($"Set default (bulletImpact1_PLACEHOLDER)", () => ValidationUtils.update_field(aero_part, "hitSound", bullet_impact, "fix AeroPart.hitSound")) });

				results.warn("AeroPart", "airfoil", airfoil_id > 0 && find_prop<float>(ser_part, "wingArea") == 0, $"Wing area 0 with airfoil id {airfoil_id}", go);
			}

			return results;
		}
	}




	[Requires(typeof(AircraftParametersValidator))]
	[Requires(typeof(Prefab_AircraftRootValidator))]
	[Requires(typeof(Prefab_AeroPartValidator))]
	class Prefab_WeaponManagerValidator : IValidator
	{
		public string Name => "WeaponManager";

		public ValidatorResult evaluate(UnityEngine.Object aircraft_definition)
		{
			GameObject prefab = ValidationUtils.get_aircraft_root(aircraft_definition);
			ValidatorResult results = new ValidatorResult(Name, prefab.transform);

			Component aircraft_comp = prefab.GetComponent(game_type("Aircraft"));
			var ser_aircraft = new SerializedObject(aircraft_comp);
			Component weapon_manager = find_prop<Component>(ser_aircraft, "weaponManager");
			GameObject wep_go = weapon_manager.gameObject;
			
			var ser_wep = new SerializedObject(weapon_manager);
			bool wep_aircraft_ref = ValidationUtils.check_reference(ser_wep, "aircraft", aircraft_comp);
			results.critical("WeaponManager", "aircraft", !wep_aircraft_ref, "Aircraft reference missing or wrong", wep_go,
				new List<Fix>{ new Fix($"Auto set", () => ValidationUtils.update_field(weapon_manager, "aircraft", aircraft_comp, "auto set WeaponManager.aircraft")) });
			if (!wep_aircraft_ref) return results;

			var hardpoint_sets = find_prop(ser_wep, "hardpointSets");
			int num_sets = hardpoint_sets.arraySize;

			// check StandardLoadouts
			using var ser_def = new SerializedObject(aircraft_definition);
			var params_obj = find_prop<UnityEngine.Object>(ser_def, "aircraftParameters");
			using var ser_params = new SerializedObject(params_obj);
			var std_loadouts = find_prop(ser_params, "StandardLoadouts");
			if (std_loadouts.arraySize > 0)
			{
				for (int i = 0; i < std_loadouts.arraySize; i++)
				{
					int loadout_size = find_prop_rel(get_element(std_loadouts, i), "loadout.weapons").arraySize;
					results.critical("", "StandardLoadouts", loadout_size != num_sets, $"StandardLoadout {i} size ({loadout_size}) must match number of HardpointSets ({num_sets})", params_obj);
					if (loadout_size != num_sets) return results;
				}
			}

			Dictionary<int, List<int>> precluding = new Dictionary<int, List<int>>();
			Dictionary<int, string> indexes_taken = new Dictionary<int, string>();

			for (int i = 0; i < num_sets; i++)
			{
				var hardpoint_group = hardpoint_sets.GetArrayElementAtIndex(i);
				string name = find_prop_rel<string>(hardpoint_group, "name");

				results.warn("WeaponManager", "hardpointSets", name == "", $"Set {i}: missing Name", wep_go);

				var precluding_hardpoints = find_prop_rel(hardpoint_group, "precludingHardpointSets");
				for (int j = 0; j < precluding_hardpoints.arraySize; j++)
				{
					int blocker = precluding_hardpoints.GetArrayElementAtIndex(j).intValue;
					results.critical("WeaponManager", "hardpointSets", blocker == i, $"Set {i}: precludes itself", wep_go);
					if (blocker == i) return results;

					if (!precluding.ContainsKey(blocker)) precluding[blocker] = new List<int>();
					precluding[blocker].Add(i);
				}

				bool symmetry_with_prev = find_prop_rel<bool>(hardpoint_group, "SymmetryWithPrev");
				results.critical("WeaponManager", "hardpointSets", symmetry_with_prev && (i == 0), "Set 0: SymmetryWithPrev enabled, no previous", wep_go);
				if (symmetry_with_prev && (i == 0)) return results;

				string symmetry_name = find_prop_rel<string>(hardpoint_group, "SymmetryName");
				results.warn("WeaponManager", "hardpointSets", symmetry_with_prev && symmetry_name == "", $"Set {i}: missing SymmetryName, SymmetryWithPrev enabled", wep_go);
				
				var weapon_options = find_prop_rel(hardpoint_group, "weaponOptions");
				results.warn("WeaponManager", "hardpointSets", weapon_options.arraySize == 0, $"Set {i}: WeaponOptions empty", wep_go);
				bool is_first_empty = get_element<UnityEngine.Object>(weapon_options, 0) == null;
				results.critical("WeaponManager", "hardpointSets", !is_first_empty, $"Set {i}: WeaponOptions first element must be empty", wep_go);
				if (!is_first_empty) return results;

				var hardpoints = find_prop_rel(hardpoint_group, "hardpoints");
				for (int j = 0; j < hardpoints.arraySize; j++)
				{
					var hardpoint = get_element(hardpoints, j);

					bool has_transform = find_prop_rel<Transform>(hardpoint, "transform") != null;
					results.critical("WeaponManager", "hardpointSets", !has_transform, $"Set {i}: Hardpoint {j} missing Transform", wep_go);
					if (!has_transform) return results;

					bool has_part = find_prop_rel<Component>(hardpoint, "part") != null;
					results.critical("WeaponManager", "hardpointSets", !has_part, $"Set {i}: Hardpoint {j} missing Part", wep_go);
					if (!has_part) return results;

					var bay_doors = find_prop_rel(hardpoint, "bayDoors");
					for (int k = 0; k < bay_doors.arraySize; k++)
					{
						Component comp = get_element<Component>(bay_doors, k);
						results.critical("WeaponManager", "hardpointSets", comp == null, $"Set {i}: Hardpoint {j} BayDoor {k} is null", wep_go);
						if (comp == null) return results;
					}

					bool has_bay_doors = bay_doors.arraySize > 0;
					float door_duration = find_prop_rel<float>(hardpoint, "doorOpenDuration");
					results.warn("WeaponManager", "hardpointSets", has_bay_doors && door_duration <= 0, $"Set {i}: Hardpoint {j} has BayDoors but DoorOpenDuration is zero", wep_go);

					var pylon_options = find_prop_rel(hardpoint, "pylonOptions"); // TODO: PylonOptions
					var pylon = find_prop_rel<Component>(hardpoint, "Pylon"); // TODO: Pylon
					var plug = find_prop_rel<Component>(hardpoint, "Plug"); // TODO: Plug
					var built_in_weapons = find_prop_rel(hardpoint, "BuiltInWeapons"); // TODO: BuiltInWeapons
					var built_in_turrets = find_prop_rel(hardpoint, "BuiltInTurrets"); // TODO: BuiltInTurrets

					int hardpoint_index = find_prop_rel<int>(hardpoint, "HardpointIndex");
					bool index_taken = indexes_taken.TryGetValue(hardpoint_index, out string taken_by);
					if (!index_taken) indexes_taken[hardpoint_index] = $"point {j} in set {i}";
					results.warn("WeaponManager", "hardpointSets", index_taken, $"Set {i}: Hardpoint {j}'s HardpointIndex ({hardpoint_index}) already in use by {taken_by}", wep_go);
				}
			}

			foreach (var (blocking_set, blocked_sets) in precluding)
			{
				foreach (int blocked_set in blocked_sets)
				{
					bool does_set_proclude = precluding.TryGetValue(blocked_set, out var set_precludes);
					bool is_block_mutual = (does_set_proclude && set_precludes != null ? set_precludes.Contains(blocking_set) : false);
					results.warn("WeaponManager", "hardpointSets", !is_block_mutual, $"Set {blocking_set} precludes set {blocked_set}, but {blocked_set} does not preclude {blocking_set}", wep_go);
				}
			}

			return results;
		}
	}




	[Requires(typeof(Prefab_AeroPartValidator))]
	class Prefab_HighLiftDeviceValidator : IValidator
	{
		public string Name => "HighLiftDevice";

		public ValidatorResult evaluate(UnityEngine.Object aircraft_definition)
		{
			GameObject prefab = ValidationUtils.get_aircraft_root(aircraft_definition);
			ValidatorResult results = new ValidatorResult(Name, prefab.transform);

			Component aircraft_comp = prefab.GetComponent(game_type("Aircraft"));

			Component[] comps = prefab.GetComponentsInChildren(game_type("HighLiftDevice"), true);
			// results.info("", "", comps.Length > 0, $"[placeholder] {comps.Length} {Name} found");

			return results;
		}
	}




	[Requires(typeof(Prefab_AeroPartValidator))]
	class Prefab_ControlSurfaceValidator : IValidator
	{
		public string Name => "ControlSurface";

		public ValidatorResult evaluate(UnityEngine.Object aircraft_definition)
		{
			GameObject prefab = ValidationUtils.get_aircraft_root(aircraft_definition);
			ValidatorResult results = new ValidatorResult(Name, prefab.transform);

			Component aircraft_comp = prefab.GetComponent(game_type("Aircraft"));

			Component[] comps = prefab.GetComponentsInChildren(game_type("ControlSurface"), true);
			// results.info("", "", comps.Length > 0, $"[placeholder] {comps.Length} {Name} found");

			return results;
		}
	}




	[Requires(typeof(Prefab_AeroPartValidator))]
	class Prefab_FuelTankValidator : IValidator
	{
		public string Name => "FuelTank";

		public ValidatorResult evaluate(UnityEngine.Object aircraft_definition)
		{
			GameObject prefab = ValidationUtils.get_aircraft_root(aircraft_definition);
			ValidatorResult results = new ValidatorResult(Name, prefab.transform);

			using var ser_def = new SerializedObject(aircraft_definition);
			var aircraft_parameters = find_prop<UnityEngine.Object>(ser_def, "aircraftParameters");
			using var ser_params = new SerializedObject(aircraft_parameters);

			float default_fuel = find_prop<float>(ser_params, "DefaultFuelLevel");
			string default_fuel_text = $"{(default_fuel*100):F2}%";

			Component aircraft_comp = prefab.GetComponent(game_type("Aircraft"));

			Component[] comps = prefab.GetComponentsInChildren(game_type("FuelTank"), true);

			float total_fuel_capacity = 0;

			// effect defaults
			GameObject fuel_leak = find_asset<GameObject>("fuelLeak_PLACEHOLDER");

			GameObject fire_tiny = find_asset<GameObject>("fire_tiny_PLACEHOLDER");
			GameObject fire_small = find_asset<GameObject>("fire_small_PLACEHOLDER");
			GameObject fire_med = find_asset<GameObject>("fire_med_PLACEHOLDER");
			GameObject fire_large = find_asset<GameObject>("fire_large_PLACEHOLDER");

			GameObject fireball_med = find_asset<GameObject>("fireball_medium_PLACEHOLDER");
			GameObject fireball_large = find_asset<GameObject>("fireball_large_PLACEHOLDER");


			Dictionary<Component, List<Component>> tank_connections = new Dictionary<Component, List<Component>>();

			foreach (Component tank in comps)
			{
				GameObject tank_go = tank.gameObject;
				var ser_tank = new SerializedObject(tank);

				Component aero_part = tank_go.GetComponent(game_type("AeroPart"));

				Component current_part = find_prop<Component>(ser_tank, "part");
				results.critical("FuelTank", "part", current_part == null, "Part reference missing", tank_go,
					aero_part != null ? new List<Fix>{ new Fix($"Auto set (found: {aero_part.gameObject.name})",
					() => ValidationUtils.update_field(tank, "part", aero_part, "auto set FuelTank.part")) } : null);
				if (current_part == null) return results;

				results.warn("FuelTank", "part", current_part != aero_part, "Part reference potentially wrong (not using part in same object)", tank_go,
					new List<Fix>{ new Fix($"Auto set", () => ValidationUtils.update_field(tank, "part", aero_part, "auto set FuelTank.part")) });

				float fuel_capacity = find_prop<float>(ser_tank, "fuelCapacity");
				float fuel_mass = find_prop<float>(ser_tank, "fuelMass");

				total_fuel_capacity += fuel_capacity;

				results.warn("FuelTank", "fuelCapacity", fuel_capacity <= 0, "FuelCapacity <= 0", tank_go);
				results.info("FuelTank", "fuelMass", !fuzzy_eq(fuel_mass, fuel_capacity * default_fuel), $"FuelMass not {default_fuel_text} of capacity (from params)", tank_go,
					new List<Fix>{ new Fix($"Set to {default_fuel_text} fill", () => ValidationUtils.update_field(tank, "fuelMass", fuel_capacity * default_fuel, $"fill FuelTank.FuelMass to {default_fuel_text}")) });
				results.warn("FuelTank", "fuelMass", fuel_mass > fuel_capacity, "FuelMass > FuelCapacity", tank_go);

				var connected_tanks = find_prop(ser_tank, "connectedTanks");
				List<Component> connected = new List<Component>();

				for (int i = 0; i < connected_tanks.arraySize; i++)
				{
					Component other_tank = get_element<Component>(connected_tanks, i);
					results.critical("FuelTank", $"connectedTanks.Array.data[{i}]", other_tank == null, $"Connected tank {i} is null", tank_go);
					if (other_tank == null) return results;

					connected.Add(other_tank);
				}
				tank_connections[tank] = connected;

				results.info("FuelTank", "leakThreshold", find_prop<float>(ser_tank, "leakThreshold") <= 0, "LeakThreshold <= 0", tank_go);
				results.info("FuelTank", "leakPerHP", find_prop<float>(ser_tank, "leakPerHP") <= 0, "LeakPerHP <= 0", tank_go);
				results.info("FuelTank", "maxLeakRate", find_prop<float>(ser_tank, "maxLeakRate") <= 0, "MaxLeakRate <= 0", tank_go);
				results.info("FuelTank", "ruptureGMin", find_prop<float>(ser_tank, "ruptureGMin") <= 0, "RuptureGMin <= 0", tank_go);
				results.info("FuelTank", "ruptureGMax", find_prop<float>(ser_tank, "ruptureGMax") <= 0, "RuptureGMax <= 0", tank_go);
				results.info("FuelTank", "ignitionGMin", find_prop<float>(ser_tank, "ignitionGMin") <= 0, "IgnitionGMin <= 0", tank_go);
				results.info("FuelTank", "ignitionGMax", find_prop<float>(ser_tank, "ignitionGMax") <= 0, "IgnitionGMax <= 0", tank_go);
				results.info("FuelTank", "ignitionPierceMin", find_prop<float>(ser_tank, "ignitionPierceMin") <= 0, "IgnitionPierceMin <= 0", tank_go);
				results.info("FuelTank", "ignitionPierceMax", find_prop<float>(ser_tank, "ignitionPierceMax") <= 0, "IgnitionPierceMax <= 0", tank_go);
				results.info("FuelTank", "ignitionBlastMin", find_prop<float>(ser_tank, "ignitionBlastMin") <= 0, "IgnitionBlastMin <= 0", tank_go);
				results.info("FuelTank", "ignitionBlastMax", find_prop<float>(ser_tank, "ignitionBlastMax") <= 0, "IgnitionBlastMax <= 0", tank_go);
				results.info("FuelTank", "fireIntensity", find_prop<float>(ser_tank, "fireIntensity") <= 0, "FireIntensity <= 0", tank_go);

				// effects
				results.fail("FuelTank", "leakEffect", find_prop<GameObject>(ser_tank, "leakEffect") == null, "missing LeakEffect", tank_go,
					new List<Fix>{ new Fix("Set default (fuelLeak_PLACEHOLDER)", () => ValidationUtils.update_field(tank, "leakEffect", fuel_leak, "use fuelLeak_PLACEHOLDER for FuelTank.leakEffect")) });
				results.fail("FuelTank", "fireEffect", find_prop<GameObject>(ser_tank, "fireEffect") == null, "missing FireEffect", tank_go,
					new List<Fix>{
						new Fix("fire_tiny", () => ValidationUtils.update_field(tank, "fireEffect", fire_tiny, "use fire_tiny_PLACEHOLDER for FuelTank.fireEffect")),
						new Fix("fire_small", () => ValidationUtils.update_field(tank, "fireEffect", fire_small, "use fire_small_PLACEHOLDER for FuelTank.fireEffect")),
						new Fix("fire_med", () => ValidationUtils.update_field(tank, "fireEffect", fire_med, "use fire_med_PLACEHOLDER for FuelTank.fireEffect")),
						new Fix("fire_large", () => ValidationUtils.update_field(tank, "fireEffect", fire_large, "use fire_large_PLACEHOLDER for FuelTank.fireEffect"))
					});
				results.info("FuelTank", "fireball", find_prop<GameObject>(ser_tank, "fireball") == null, "missing Fireball", tank_go,
					new List<Fix>{
						new Fix("fireball_med", () => ValidationUtils.update_field(tank, "fireball", fireball_med, "use fireball_med_PLACEHOLDER for FuelTank.fireball")),
						new Fix("fireball_large", () => ValidationUtils.update_field(tank, "fireball", fireball_large, "use fireball_large_PLACEHOLDER for FuelTank.fireball"))
					});
			}

			foreach (var (origin_tank, connected_tanks) in tank_connections)
			{
				foreach (var connected_tank in connected_tanks)
				{
					bool connected_has_connections = tank_connections.TryGetValue(connected_tank, out var other_tank_connections);
					bool is_connection_mutual = (connected_has_connections && other_tank_connections != null ? other_tank_connections.Contains(origin_tank) : false);
					results.info("FuelTank", "connectedTanks", !is_connection_mutual, $"Tank {origin_tank.gameObject.name} connects to {connected_tank.gameObject.name}, but connection is not mutual (this is fine if the second tank cannot break off before the first one, e.g. wing tank->center tank)", connected_tank.gameObject);
				}
			}

			results.fail("", "", total_fuel_capacity <= 0, "Aircraft fuel capacity <= 0");
			return results;
		}
	}




	[Requires(typeof(AircraftParametersValidator))]
	[Requires(typeof(Prefab_AircraftRootValidator))]
	[Requires(typeof(Prefab_AeroPartValidator))]
	class Prefab_TurbineEngineValidator : IValidator
	{
		public string Name => "TurbineEngine";

		public ValidatorResult evaluate(UnityEngine.Object aircraft_definition)
		{
			GameObject prefab = ValidationUtils.get_aircraft_root(aircraft_definition);
			ValidatorResult results = new ValidatorResult(Name, prefab.transform);

			Component aircraft_comp = prefab.GetComponent(game_type("Aircraft"));

			Component[] turbine_comps = prefab.GetComponentsInChildren(game_type("TurbineEngine"), true);

			foreach (Component turbine in turbine_comps)
			{
				GameObject turbine_go = turbine.gameObject;
				var ser_turbine = new SerializedObject(turbine);

				bool turbine_aircraft_ref = ValidationUtils.check_reference(ser_turbine, "aircraft", aircraft_comp);
				results.critical("TurbineEngine", "aircraft", !turbine_aircraft_ref, "Aircraft reference missing or wrong", turbine_go,
					new List<Fix>{ new Fix($"Auto set", () => ValidationUtils.update_field(turbine, "aircraft", aircraft_comp, "auto set TurbineEngine.aircraft")) });
				if (!turbine_aircraft_ref) return results;

				var ir_source = find_prop(ser_turbine, "IRSourceTransform");
				results.critical("TurbineEngine", "IRSourceTransform", ir_source == null, "missing IRSourceTransform", turbine_go);
				if (ir_source == null) return results;

				float max_rpm = find_prop<float>(ser_turbine, "maxRPM");
				float min_rpm = find_prop<float>(ser_turbine, "minRPM");
				results.fail("TurbineEngine", "maxRPM", max_rpm <= 0, "MaxRPM <= 0", turbine_go);
				results.fail("TurbineEngine", "minRPM", min_rpm <= 0, "MinRPM <= 0", turbine_go);
				results.fail("TurbineEngine", "maxRPM", max_rpm < min_rpm, "MaxRPM < MinRPM", turbine_go);
				results.fail("TurbineEngine", "maxPower", find_prop<float>(ser_turbine, "maxPower") <= 0, "MaxPower <= 0", turbine_go);
				results.warn("TurbineEngine", "spoolUpTime", find_prop<float>(ser_turbine, "spoolUpTime") <= 0, "SpoolUpTime <= 0", turbine_go);
				results.warn("TurbineEngine", "startupTime", find_prop<float>(ser_turbine, "startupTime") <= 0, "StartupTime <= 0", turbine_go);
				results.info("TurbineEngine", "currentPower", find_prop<float>(ser_turbine, "currentPower") != 0, "CurrentPower is set during runtime and should be left at zero", turbine_go);
				results.info("TurbineEngine", "powerRatio", find_prop<float>(ser_turbine, "powerRatio") != 0, "PowerRatio is set during runtime and should be left at zero", turbine_go);
				results.info("TurbineEngine", "RPMRatio", find_prop<float>(ser_turbine, "RPMRatio") != 0, "RPMRatio is set during runtime and should be left at zero", turbine_go);
				results.info("TurbineEngine", "startedAmount", find_prop<float>(ser_turbine, "startedAmount") != 0, "StartedAmount is set during runtime and should be left at zero", turbine_go);
				results.info("TurbineEngine", "throttle", find_prop<float>(ser_turbine, "throttle") != 0, "Throttle is set during runtime and should be left at zero", turbine_go);
				results.warn("TurbineEngine", "pitch", find_prop<float>(ser_turbine, "pitch") <= 0, "Pitch <= 0", turbine_go);
				results.warn("TurbineEngine", "volume", find_prop<float>(ser_turbine, "volume") <= 0, "Volume <= 0", turbine_go);
				results.warn("TurbineEngine", "startupAudio", find_prop<UnityEngine.Object>(ser_turbine, "startupAudio") == null, "missing StartupAudio", turbine_go);
				var heat_haze = find_prop(ser_turbine, "heatHazeParticles");
				results.info("TurbineEngine", "heatHazeParticles", heat_haze.arraySize == 0, "missing HeatHazeParticles", turbine_go);
				results.warn("TurbineEngine", "maxFuelConsumption", find_prop<float>(ser_turbine, "maxFuelConsumption") <= 0, "MaxFuelConsumption <= 0", turbine_go);
				float ir_min = find_prop<float>(ser_turbine, "IRMin");
				float ir_max = find_prop<float>(ser_turbine, "IRMax");
				results.warn("TurbineEngine", "IRMin", ir_min <= 0, "IRMin <= 0", turbine_go);
				results.warn("TurbineEngine", "IRMax", ir_max <= 0, "IRMax <= 0", turbine_go);
				results.warn("TurbineEngine", "IRMax", ir_max < ir_min, "IRMax < IRMin", turbine_go);
				results.fail("TurbineEngine", "criticalParts", find_prop(ser_turbine, "criticalParts").arraySize == 0, "missing CriticalParts (engine won't take damage)", turbine_go);
				results.warn("TurbineEngine", "criticalDamageThreshold", find_prop<float>(ser_turbine, "criticalDamageThreshold") <= 0, "CriticalDamageThreshold <= 0", turbine_go);
				bool fail_transform = find_prop<Transform>(ser_turbine, "failureTransform") != null;
				results.info("TurbineEngine", "failureTransform", !fail_transform, "missing FailureTransform", turbine_go);
				bool fail_effect = find_prop<UnityEngine.Object>(ser_turbine, "failureEffect") != null;
				results.info("TurbineEngine", "failureEffect", !fail_effect, "missing FailureEffect", turbine_go);
				results.info("TurbineEngine", "failureMessage", find_prop<string>(ser_turbine, "failureMessage") == "", "missing FailureMessage", turbine_go);
				results.info("TurbineEngine", "failureMessageAudio", find_prop<UnityEngine.Object>(ser_turbine, "failureMessageAudio") == null, "missing FailureMessageAudio", turbine_go);

				results.fail("TurbineEngine", "criticalParts", fail_transform && (!fail_effect), "FailTransform present but missing FailEffect", turbine_go);

				// TODO: Check same GameObject for Audio Source and verify AudioClip and Output
			}
			return results;
		}
	}




	[Requires(typeof(AircraftParametersValidator))]
	[Requires(typeof(Prefab_AircraftRootValidator))]
	class Prefab_TransmissionValidator : IValidator
	{
		public string Name => "Transmission";

		public ValidatorResult evaluate(UnityEngine.Object aircraft_definition)
		{
			GameObject prefab = ValidationUtils.get_aircraft_root(aircraft_definition);
			ValidatorResult results = new ValidatorResult(Name, prefab.transform);

			Component aircraft_comp = prefab.GetComponent(game_type("Aircraft"));
			var ser_aircraft = new SerializedObject(aircraft_comp);
			Component cockpit_part = find_prop<Component>(ser_aircraft, "cockpit");

			Component[] transmission_comps = prefab.GetComponentsInChildren(game_type("Transmission"), true);

			foreach (Component transmission in transmission_comps)
			{
				GameObject trans_go = transmission.gameObject;
				var ser_trans = new SerializedObject(transmission);

				var power_sources = find_prop(ser_trans, "powerSources");
				results.critical("Transmission", "powerSources", power_sources.arraySize == 0, "PowerSources empty", trans_go);
				if (power_sources.arraySize == 0) return results;

				for (int i = 0; i < power_sources.arraySize; i++)
				{
					GameObject power_source = get_element<GameObject>(power_sources, 0);
					results.critical("Transmission", $"powerSources.Array.data[{i}]", power_source == null, $"PowerSource {i} null", trans_go);
					if (power_source == null) return results;

					var engines = power_source.GetComponents(game_type("IPowerSource"));
					// foreach (var engine_comp in engines)
					// {
					// 	results.info("", "", true, $"{trans_go.name}: {engine_comp.GetType().Name}");
					// }
					results.critical("Transmission", $"powerSources.Array.data[{i}]", engines.Length == 0, $"PowerSource {i} ({power_source.name}) does not have a valid PowerSource component", trans_go);
					if (engines.Length == 0) return results;
				}
			}

			return results;
		}
	}




	[Requires(typeof(Prefab_TransmissionValidator))]
	class Prefab_ConstantSpeedPropValidator : IValidator
	{
		public string Name => "ConstantSpeedProp";

		public ValidatorResult evaluate(UnityEngine.Object aircraft_definition)
		{
			GameObject prefab = ValidationUtils.get_aircraft_root(aircraft_definition);
			ValidatorResult results = new ValidatorResult(Name, prefab.transform);
			Component aircraft_comp = prefab.GetComponent(game_type("Aircraft"));

			Component[] comps = prefab.GetComponentsInChildren(game_type("ConstantSpeedProp"), true);
			// results.info("", "", comps.Length > 0, $"[placeholder] {comps.Length} {Name} found");

			return results;
		}
	}




	[Requires(typeof(Prefab_AircraftRootValidator))]
	[Requires(typeof(Prefab_AeroPartValidator))]
	class Prefab_LandingGearValidator : IValidator
	{
		public string Name => "LandingGear";

		public ValidatorResult evaluate(UnityEngine.Object aircraft_definition)
		{
			GameObject prefab = ValidationUtils.get_aircraft_root(aircraft_definition);
			ValidatorResult results = new ValidatorResult(Name, prefab.transform);

			Component aircraft_comp = prefab.GetComponent(game_type("Aircraft"));
			var ser_aircraft = new SerializedObject(aircraft_comp);
			Component cockpit_part = find_prop<Component>(ser_aircraft, "cockpit");

			Component[] gear_comps = prefab.GetComponentsInChildren(game_type("LandingGear"), true);

			int num_steering = 0, num_braked = 0, num_brake_steering = 0, num_total = 0;

			foreach (Component gear in gear_comps)
			{
				GameObject gear_go = gear.gameObject;
				var ser_gear = new SerializedObject(gear);

				bool gear_aircraft_ref = ValidationUtils.check_reference(ser_gear, "aircraft", aircraft_comp);
				results.critical("LandingGear", "aircraft", !gear_aircraft_ref, "Aircraft reference missing or wrong", gear_go,
					new List<Fix>{ new Fix($"Auto set", () => ValidationUtils.update_field(gear, "aircraft", aircraft_comp, "auto set LandingGear.aircraft")) });
				if (!gear_aircraft_ref) return results;

				var attached_part = find_prop<Component>(ser_gear, "attachedPart");
				results.critical("LandingGear", "attachedPart", attached_part == null, "missing AttachedPart", gear_go);
				if (attached_part == null) return results;

				Transform gear_hinge = find_prop<Transform>(ser_gear, "gearHinge");
				results.critical("LandingGear", "gearHinge", gear_hinge == null, "missing GearHinge", gear_go);
				if (gear_hinge == null) return results;

				GameObject bumpstop = find_prop<GameObject>(ser_gear, "bumpStop");
				results.critical("LandingGear", "bumpStop", bumpstop == null, "missing BumpStop", gear_go);
				if (bumpstop == null) return results;

				GameObject unsprung = find_prop<GameObject>(ser_gear, "unsprung");
				results.critical("LandingGear", "unsprung", unsprung == null, "missing Unsprung", gear_go);
				if (unsprung == null) return results;

				float wheel_radius = find_prop<float>(ser_gear, "wheelRadius");
				results.critical("LandingGear", "wheelRadius", wheel_radius <= 0, "WheelRadius <= 0", gear_go);
				if (wheel_radius <= 0) return results;

				Transform axle = find_prop<Transform>(ser_gear, "axle");
				results.critical("LandingGear", "axle", axle == null, "missing Axle", gear_go);
				if (axle == null) return results;

				var gear_collider = find_prop<Component>(ser_gear, "gearCollider");
				results.critical("LandingGear", "gearCollider", gear_collider == null, "missing GearCollider", gear_go);
				if (gear_collider == null) return results;
				// TODO: ensure gear collider is disabled

				var tire_noise = find_prop<Component>(ser_gear, "tireNoiseSound");
				results.critical("LandingGear", "tireNoiseSound", tire_noise == null, "missing TireNoiseSound", gear_go);
				if (tire_noise == null) return results;

				var tire_skid = find_prop<Component>(ser_gear, "tireSkidSound");
				results.critical("LandingGear", "tireSkidSound", tire_skid == null, "missing TireSkidSound", gear_go);
				if (tire_skid == null) return results;
				results.warn("LandingGear", "tireSkidSound", tire_noise == tire_skid, "TireNoise and TireSkid point to the same AudioSource - right click the associated GameObject, click Properties, and using that window you can drag each AudioSource to the correct property", gear_go);


				var wheel_array = find_prop(ser_gear, "wheels");
				// this is actually a perfectly valid setup (as long as dust is set - checked below), but.. what on earth are you doing? skids?
				results.info("LandingGear", "wheels", wheel_array.arraySize == 0, "no Wheels (ignore if intended)", gear_go);

				List<Transform> wheels = new List<Transform>();

				for (int i = 0; i < wheel_array.arraySize; i++)
				{
					Transform wheel = get_element<Transform>(wheel_array, i);
					results.critical("LandingGear", $"wheels.Array.data[{i}]", wheel == null, $"Wheel {i} is null", gear_go);
					if (wheel == null) return results;

					wheels.Add(wheel);
				}

				var dust = find_prop<Component>(ser_gear, "dust");
				var wheel_dust = get_element<Transform>(wheel_array, 0)?.GetComponent<ParticleSystem>();

				results.critical("LandingGear", "dust", dust == null && wheel_dust == null, "missing Dust and no ParticleSystem found in Wheel 0", gear_go);
				if (dust == null && wheel_dust == null) return results;
				results.warn("LandingGear", "dust", dust == null && wheel_dust != null, "missing Dust but found ParticleSystem in Wheel 0 (should be set explicitly)", gear_go,
					new List<Fix>{ new Fix($"Use that", () => ValidationUtils.update_field(gear, "dust", wheel_dust, "fix LandingGear.dust")) });
					// TODO: test LandingGear.dust auto fix

				// TODO: joints

				results.warn("LandingGear", "extendedDrag", find_prop<float>(ser_gear, "extendedDrag") <= 0, "ExtendedDrag <= 0", gear_go);
				results.fail("LandingGear", "suspensionTravel", find_prop<float>(ser_gear, "suspensionTravel") <= 0, "SuspensionTravel <= 0", gear_go);
				results.fail("LandingGear", "springRate", find_prop<float>(ser_gear, "springRate") <= 0, "SpringRate <= 0", gear_go);
				results.fail("LandingGear", "dampingRate", find_prop<float>(ser_gear, "dampingRate") <= 0, "DampingRate <= 0", gear_go);
				results.info("LandingGear", "castPoint", find_prop<Transform>(ser_gear, "castPoint") == null, "missing CastPoint (game will use BumpStop instead)", gear_go);
				results.fail("LandingGear", "frictionCoef", find_prop<float>(ser_gear, "frictionCoef") <= 0, "FrictionCoef <= 0", gear_go);
				results.fail("LandingGear", "response", find_prop<float>(ser_gear, "response") <= 0, "Response <= 0", gear_go);
				results.fail("LandingGear", "contactArea", find_prop<float>(ser_gear, "contactArea") <= 0, "ContactArea <= 0", gear_go);
				results.fail("LandingGear", "rollingResistance", find_prop<float>(ser_gear, "rollingResistance") <= 0, "RollingResistance <= 0", gear_go);

				float max_compression = find_prop<float>(ser_gear, "maxCompression");
				results.fail("LandingGear", "maxCompression", max_compression <= 0, "MaxCompression <= 0", gear_go);
				float gear_mass = find_prop<float>(ser_gear, "mass");
				results.fail("LandingGear", "mass", gear_mass <= 0, "Mass <= 0", gear_go);
				// TODO: gear_mass > attached_part.mass
				AudioClip metal_tear = find_asset<AudioClip>("metal_tear1_PLACEHOLDER");
				results.warn("LandingGear", "breakSound", find_prop<UnityEngine.Object>(ser_gear, "breakSound") == null, "missing BreakSound", gear_go,
					new List<Fix>{ new Fix($"Set default (metal_tear1_PLACEHOLDER)", () => ValidationUtils.update_field(gear, "breakSound", metal_tear, "fix LandingGear.breakSound")) });
				AudioClip fold_sound = find_asset<AudioClip>("gearfold_PLACEHOLDER");
				results.warn("LandingGear", "foldSound", find_prop<UnityEngine.Object>(ser_gear, "foldSound") == null, "missing FoldSound", gear_go,
					new List<Fix>{ new Fix($"Set default (gearfold_PLACEHOLDER)", () => ValidationUtils.update_field(gear, "foldSound", fold_sound, "fix LandingGear.foldSound")) });
				results.warn("LandingGear", "foldVolume", find_prop<float>(ser_gear, "foldVolume") <= 0, "FoldVolume <= 0", gear_go,
					new List<Fix>{ new Fix("Set default (0.3)", () => ValidationUtils.update_field(gear, "foldVolume", 0.3f, "fix LandingGear.foldVolume")) });
				AudioClip latch_sound = find_asset<AudioClip>("latch1_PLACEHOLDER");
				results.warn("LandingGear", "latchSound", find_prop<UnityEngine.Object>(ser_gear, "latchSound") == null, "missing LatchSound", gear_go,
					new List<Fix>{ new Fix($"Set default (latch1_PLACEHOLDER)", () => ValidationUtils.update_field(gear, "latchSound", latch_sound, "fix LandingGear.latchSound")) });
				results.warn("LandingGear", "latchVolume", find_prop<float>(ser_gear, "latchVolume") <= 0, "LatchVolume <= 0", gear_go,
					new List<Fix>{ new Fix("Set default (0.3)", () => ValidationUtils.update_field(gear, "latchVolume", 0.3f, "fix LandingGear.latchVolume")) });
				results.fail("LandingGear", "foldDegrees", find_prop<float>(ser_gear, "foldDegrees") == 0, "FoldDegrees = 0", gear_go);
				results.fail("LandingGear", "foldSpeed", find_prop<float>(ser_gear, "foldSpeed") <= 0, "FoldSpeed <= 0", gear_go);
				Transform strut_rotation_transform = find_prop<Transform>(ser_gear, "strutRotationTransform"); // strut rotation is around the Y axis
				float strut_rotation = find_prop<float>(ser_gear, "strutRotation");
				results.info("LandingGear", "strutRotationTransform", strut_rotation != 0 && strut_rotation_transform == null, "StrutRotation != 0 but missing StrutRotationTransform (game will use Unsprung instead)", gear_go);

				// TODO: moving parts
				// TODO: gear doors

				bool steering = find_prop<bool>(ser_gear, "steering"); if (steering) num_steering += 1;
				bool braked = find_prop<bool>(ser_gear, "braked"); if (braked) num_braked += 1;
				results.warn("LandingGear", "steeringLock", steering && find_prop<float>(ser_gear, "steeringLock") == 0, "Steering enabled, SteeringLock == 0", gear_go);
				results.warn("LandingGear", "steeringSpeed", steering && find_prop<float>(ser_gear, "steeringSpeed") <= 0, "Steering enabled, SteeringSpeed <= 0", gear_go);
				results.warn("LandingGear", "aligningStrength", !steering && find_prop<float>(ser_gear, "aligningStrength") != 0, "AligningStrength != 0 but Steering is false", gear_go);
				float differential_brake = find_prop<float>(ser_gear, "differentialBrakeFactor");
				if (braked && differential_brake != 0) num_brake_steering += 1;
				results.warn("LandingGear", "braked", !braked && differential_brake != 0, "DifferentialBrakeFactor != 0 but Braked is false", gear_go);

				// game context:
				// axle doesn't need to be in line with wheels, but must be oriented correctly (X right, Y up, Z forward)
				// wheels rotate on the X axis, which should be facing right (default)
				// suspension movement
				//		on ground: unsprung.transform.position = bumpStop.transform.position - bumpStop.transform.up * (suspensionTravel - compressionDistance - wheelRadius)
				//		in the air: unsprung.transform.position = bumpStop.transform.position - unsprung.transform.up * (suspensionTravel - wheelRadius)
				//			both cases use a different up vector.. make sure bumpStop.up and unsprung.up are the same
				// raycasts: (castPoint.position, castPoint.position - castPoint.up * suspensionTravel)

				// therefore, check:
				//		axle orientation (X right, Y up, Z forward)
				//		wheel orientation (X right, dot(wheel.right, axle.right))
				// 		bumpStop should be (suspensionTravel - wheelRadius) above unsprung
				// 		castPoint should be (suspensionTravel - wheelRadius) above unsprung
				//		unsprung should be at the same Y as the wheels

				Transform gear_transform = gear_go.transform;
				float alignment_precise = 0.1f; // degrees
				float alignment_meh = 1f; // degrees

				bool axle_forward = Vector3.Dot(axle.forward, gear_transform.forward) > 0;
				results.warn("", "", !axle_forward, "Axle forwards (Z) is backwards", axle);
				bool axle_up = Vector3.Dot(axle.up, gear_transform.up) > 0;
				results.warn("", "", !axle_up, "Axle up (Y) is backwards", axle);
				bool axle_right = Vector3.Dot(axle.right, gear_transform.right) > 0;
				results.warn("", "", !axle_right, "Axle right (X) is backwards", axle);
				
				float axle_forward_angle = project_signed_angle(axle.forward, gear_transform.forward, gear_transform.up);
				results.warn("", "", axle_forward && axle_forward_angle > alignment_precise, $"Axle forwards misaligned ({axle_forward_angle:F2}°) left/right", axle);
				// float axle_up_angle = Vector3.SignedAngle(axle.up, gear_transform.up, gear_transform.right);
				// results.warn("", "", axle_up && axle_up_angle > alignment_meh, $"Axle up misaligned ({axle_up_angle:F2}°) forwards/backwards", axle);
				// float axle_right_angle = Vector3.SignedAngle(axle.right, gear_transform.right, gear_transform.up);
				// results.warn("", "", axle_right && axle_right_angle > alignment_precise, $"Axle right misaligned ({axle_right_angle:F2}°) forwards/backwards", axle);


				



				for (int i = 0; i < wheel_array.arraySize; i++)
				{
					Transform wheel = get_element<Transform>(wheel_array, i);
					float wheel_angle = project_signed_angle(wheel.right, axle.right, Vector3.up);
					results.warn("", "", wheel_angle > alignment_precise, $"Wheel {i} misaligned ({wheel_angle:F2}°) left/right", wheel.gameObject);
				}
				
				num_total += 1;
			}

			results.info("LandingGear", "", num_total == 0, "No landing gear (ignore if intended)");
			results.warn("LandingGear", "", num_total > 0 && num_steering == 0 && num_brake_steering == 0, "No steering wheels");
			results.info("LandingGear", "", num_total > 0 && num_steering == 0 && num_brake_steering > 0, "No steering wheels (steering brakes present)");
			results.warn("LandingGear", "", num_total > 0 && num_braked == 0, "No braked wheels");

			return results;
		}
	}
}

#endif