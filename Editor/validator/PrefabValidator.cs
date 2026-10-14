#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEditor;

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

			var ser_aircraft = new SerializedObject(aircraft_comp);

			bool def_ref = ValidationUtils.check_reference(ser_aircraft, "definition", aircraft_definition);
			results.critical("Aircraft", "definition", !def_ref, "Aircraft.Definition not set to aircraft definition object", prefab,
				new List<Fix>{ new Fix($"Fix", () => Debug.Log("set Aircraft.Definition")) });
			if (!def_ref) return results;


			// Cockpit
			Component cockpit_part = find_prop<Component>(ser_aircraft, "cockpit");
			results.critical("Aircraft", "cockpit", cockpit_part == null, "Aircraft.cockpit not set", prefab);
			if (cockpit_part == null) return results;

			Component[] cockpit_comps = prefab.GetComponentsInChildren(game_type("Cockpit"), true);
			results.critical("", "", cockpit_comps.Length == 0, "No objects with Cockpit component", prefab);
			if (cockpit_comps.Length == 0) return results;

			results.critical("", "", cockpit_comps.Length > 1, "Multiple objects with Cockpit component", prefab);
			if (cockpit_comps.Length > 1) return results;

			Component cockpit_comp = cockpit_comps[0];
			
			var ser_cockpit = new SerializedObject(cockpit_comp);
			var go_cockpit = cockpit_comp.gameObject;
			//Debug.Log(find_prop<UnityEngine.Object>(ser_cockpit, "aircraft"));
			bool cockpit_aircraft_ref = ValidationUtils.check_reference(ser_cockpit, "aircraft", aircraft_comp);
			results.critical("Cockpit", "aircraft", !cockpit_aircraft_ref, "Cockpit.Aircraft not set", go_cockpit,
				new List<Fix>{ new Fix($"Fix", () => Debug.Log("set Cockpit.aircraft")) });
			if (!cockpit_aircraft_ref) return results;


			// WeaponManager
			Component weapon_manager = find_prop<Component>(ser_aircraft, "weaponManager");
			results.critical("Aircraft", "weaponManager", weapon_manager == null, "Aircraft.WeaponManager not set", prefab);
			if (weapon_manager == null) return results;
			
			var ser_wep = new SerializedObject(weapon_manager);
			bool wep_aircraft_ref = ValidationUtils.check_reference(ser_wep, "aircraft", aircraft_comp);
			results.critical("WeaponManager", "aircraft", !wep_aircraft_ref, "WeaponManager.Aircraft not set", prefab,
				new List<Fix>{ new Fix($"Fix", () => Debug.Log("set WeaponManager.aircraft")) });
			if (!wep_aircraft_ref) return results;
			

			
			
			results.fail("Aircraft", "MapUniqueName", find_prop<string>(ser_aircraft, "MapUniqueName") != "", "MapUniqueName should be blank", prefab,
				new List<Fix>{ new Fix($"Clear field", () => Debug.Log("clear MapUniqueName")) });
				
			results.fail("Aircraft", "MapHQ", find_prop<UnityEngine.Object>(ser_aircraft, "MapHQ") != null, "MapHQ should be blank", prefab,
				new List<Fix>{ new Fix($"Clear field", () => Debug.Log("clear MapHQ")) });

			results.fail("Aircraft", "MapAirbase", find_prop<UnityEngine.Object>(ser_aircraft, "MapAirbase") != null, "MapAirbase should be blank", prefab,
				new List<Fix>{ new Fix($"Clear field", () => Debug.Log("clear MapAirbase")) });

			results.info("Aircraft", "ecmIntensity", find_prop<float>(ser_aircraft, "ecmIntensity") > 0, "EcmIntensity should be zero", prefab,
				new List<Fix>{ new Fix($"Clear field", () => Debug.Log("clear ecmIntensity")) });

			var pilots = find_prop(ser_aircraft, "pilots");
			results.info("Aircraft", "pilots", pilots.arraySize == 0, "must have at least one pilot (i think)", prefab);



			string net_transform = "NuclearOption.NetworkTransforms.AircraftNetworkTransform";
			Component network_transform = prefab.GetComponent(game_type(net_transform));
			results.critical("", "", network_transform == null, "missing AircraftNetworkTransform component on root", prefab);
			if (network_transform == null) return results;

			var ser_net = new SerializedObject(network_transform);
			bool net_aircraft_ref = ValidationUtils.check_reference(ser_net, "Aircraft", aircraft_comp);
			results.critical(net_transform, "Aircraft", !net_aircraft_ref, "AircraftNetworkTransform.Aircraft not set", prefab,
				new List<Fix>{ new Fix($"Fix", () => Debug.Log("set AircraftNetworkTransform.Aircraft")) });
			if (!net_aircraft_ref) return results;

			Component aero_part = prefab.GetComponent(game_type("AeroPart"));
			results.critical("AeroPart", "", aero_part == null, "missing AeroPart component on root", prefab);
			if (aero_part == null) return results;


			var dupes = prefab.GetComponentsInChildren<Transform>(true)
				.GroupBy(t => hierarchy_path(t, prefab.transform))
				.Where(g => g.Key != "" && g.Count() > 1);
			foreach (var g in dupes)
				results.warn("", "", true, $"Duplicate part names at '{g.Key}'", prefab);

			return results;
		}
	}



	// verifies prefab root
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
				results.critical("AeroPart", "parentUnit", !part_aircraft_ref, "ParentUnit not set", go,
					new List<Fix>{ new Fix($"Fix", () => Debug.Log("set AeroPart.parentUnit")) });
				if (!part_aircraft_ref) return results;

				int airfoil_id = find_prop<int>(ser_part, "airfoil");
				results.critical("AeroPart", "airfoil", airfoil_id >= num_airfoils, $"Airfoil index out of bounds ({airfoil_id}/{num_airfoils-1})", go);
				if (airfoil_id >= num_airfoils) return results;

				results.fail("AeroPart", "mass", find_prop<float>(ser_part, "mass") <= 0, "Mass <= 0", go);
				results.fail("AeroPart", "wingArea", find_prop<float>(ser_part, "wingArea") < 0, "WingArea < 0", go); // that's not right
				results.fail("AeroPart", "dragArea", find_prop<float>(ser_part, "dragArea") <= 0, "DragArea <= 0", go);

				results.info("AeroPart", "hitSound", find_prop<UnityEngine.Object>(ser_part, "hitSound") == null, "HitSound not set", go,
					new List<Fix>{ new Fix($"Use default", () => Debug.Log("set AeroPart.hitSound")) });

				results.warn("AeroPart", "airfoil", airfoil_id > 0 && find_prop<float>(ser_part, "wingArea") == 0, $"Wing area 0 with airfoil id {airfoil_id}", go);
			}

			return results;
		}
	}
}

#endif