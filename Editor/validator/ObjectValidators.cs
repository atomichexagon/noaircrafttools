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
	class AircraftDefinitionValidator : IValidator
	{
		public string Name => "AircraftDefinition";
		
		public ValidatorResult evaluate(UnityEngine.Object aircraft_definition)
		{
			ValidatorResult results = new ValidatorResult(Name);

			results.critical("", "", aircraft_definition == null, "Aircraft definition null", null);
			if (aircraft_definition == null) return results; // how?

			using var ser_def = new SerializedObject(aircraft_definition);

			GameObject prefab = find_prop<GameObject>(ser_def, "unitPrefab");
			results.critical("", "unitPrefab", prefab == null, "missing UnitPrefab", aircraft_definition);
			if (prefab == null) return results;

			UnityEngine.Object parameters = find_prop<UnityEngine.Object>(ser_def, "aircraftParameters");
			results.critical("", "aircraftParameters", parameters == null, "missing AircraftParameters", aircraft_definition);
			if (parameters == null) return results;

			string json_key = find_prop<string>(ser_def, "jsonKey");
			results.critical("", "jsonKey", json_key == "", "missing JsonKey", aircraft_definition);
			if (json_key == "") return results;

			results.warn("", "unitName", find_prop<string>(ser_def, "unitName") == "", "missing UnitName", aircraft_definition);
			results.warn("", "code", find_prop<string>(ser_def, "code") == "", "missing Code", aircraft_definition);
			//results.info(find_prop<string>(ser_def, "bogeyName") == "", "missing BogeyName (a lot of vanilla aircraft are as well)", aircraft_definition);
			results.info("", "description", find_prop<string>(ser_def, "description") == "", "missing Description", aircraft_definition);

			results.warn("", "radarSize", find_prop<float>(ser_def, "radarSize") <= 0, "RadarSize <= 0", aircraft_definition);
			results.warn("", "mass", find_prop<float>(ser_def, "mass") <= 0, "Mass <= 0", aircraft_definition);
			results.warn("", "value", find_prop<float>(ser_def, "value") <= 0, "Value <= 0", aircraft_definition);
			
			results.info("", "length", find_prop<float>(ser_def, "length") <= 0, "Length <= 0", aircraft_definition);
			results.info("", "width", find_prop<float>(ser_def, "width") <= 0, "Width <= 0", aircraft_definition);
			results.info("", "height", find_prop<float>(ser_def, "height") <= 0, "Height <= 0", aircraft_definition);
			results.info("", "manpower", find_prop<float>(ser_def, "manpower") <= 0, "Manpower <= 0", aircraft_definition);
			results.info("", "armorTier", find_prop<float>(ser_def, "armorTier") <= 0, "ArmorTier <= 0", aircraft_definition);
			results.info("", "damageTolerance", find_prop<float>(ser_def, "damageTolerance") <= 0, "DamageTolerance <= 0", aircraft_definition);

			results.warn("", "friendlyIcon", find_prop<UnityEngine.Object>(ser_def, "friendlyIcon") == null, "missing FriendlyIcon", aircraft_definition);
			results.warn("", "hostileIcon", find_prop<UnityEngine.Object>(ser_def, "hostileIcon") == null, "missing HostileIcon", aircraft_definition);
			results.warn("", "mapIcon", find_prop<UnityEngine.Object>(ser_def, "mapIcon") == null, "missing MapIcon", aircraft_definition);

			var aircraft_info = find_prop(ser_def, "aircraftInfo");
			float empty_weight = find_prop_rel<float>(aircraft_info, "emptyWeight");
			float max_weight = find_prop_rel<float>(aircraft_info, "maxWeight");
			float stall_speed = find_prop_rel<float>(aircraft_info, "stallSpeed");
			float max_speed = find_prop_rel<float>(aircraft_info, "maxSpeed");
			results.info("", "aircraftInfo", empty_weight <= 0, "AircraftInfo.EmptyWeight <= 0", aircraft_definition);
			results.info("", "aircraftInfo", max_speed <= 0, "AircraftInfo.MaxSpeed <= 0", aircraft_definition);
			results.info("", "aircraftInfo", stall_speed <= 0, "AircraftInfo.StallSpeed <= 0 (ignore if VTOL)", aircraft_definition);
			results.info("", "aircraftInfo", find_prop_rel<float>(aircraft_info, "maneuverability") <= 0, "AircraftInfo.Maneuverability <= 0 (max G loading)", aircraft_definition);
			results.info("", "aircraftInfo", max_weight <= 0, "AircraftInfo.MaxWeight <= 0", aircraft_definition);
			// =)
			results.info("", "aircraftInfo", max_weight < empty_weight, "AircraftInfo.MaxWeight is less than EmptyWeight");
			results.info("", "aircraftInfo", max_speed < stall_speed, "AircraftInfo.MaxSpeed is less than StallSpeed");

			var type_identity = find_prop(ser_def, "typeIdentity");
			float type_surface = find_prop_rel<float>(type_identity, "surface");
			float type_air = find_prop_rel<float>(type_identity, "air");
			float type_missile = find_prop_rel<float>(type_identity, "missile");
			float type_radar = find_prop_rel<float>(type_identity, "radar");
			float type_strategic = find_prop_rel<float>(type_identity, "strategic");
			results.warn("", "type_identity", type_surface == 0 && type_air == 0 && type_missile == 0 && type_radar == 0 && type_strategic == 0,
				"missing TypeIdentity (all set to zero)", aircraft_definition);
			results.info("", "type_identity.air", type_air == 0, "TypeIdentity.Air set to zero", aircraft_definition);

			var role_identity = find_prop(ser_def, "roleIdentity");
			var role_surface = find_prop_rel<float>(role_identity, "antiSurface");
			var role_air = find_prop_rel<float>(role_identity, "antiAir");
			var role_missile = find_prop_rel<float>(role_identity, "antiMissile");
			var role_radar = find_prop_rel<float>(role_identity, "antiRadar");
			results.warn("", "roleIdentity", role_surface == 0 && role_air == 0 && role_missile == 0 && role_radar == 0,
				"missing RoleIdentity (all set to zero)", aircraft_definition);

			return results;
		}
	}


	[Requires(typeof(AircraftDefinitionValidator))]
	class AircraftParametersValidator : IValidator
	{
		public string Name => "AircraftParameters";
		public ValidatorResult evaluate(UnityEngine.Object aircraft_definition)
		{
			ValidatorResult results = new ValidatorResult(Name);
			using var ser_def = new SerializedObject(aircraft_definition);
			var aircraft_parameters = find_prop<UnityEngine.Object>(ser_def, "aircraftParameters");

			results.critical("", "aircraftParameters", aircraft_parameters == null, "Aircraft parameters null", null);
			if (aircraft_parameters == null) return results; // how?

			using var ser_params = new SerializedObject(aircraft_parameters);

			var loadouts = find_prop(ser_params, "loadouts");
			results.critical("", "loadouts", loadouts.arraySize < 2, "must have at least 2 Loadouts", aircraft_parameters);
			if (loadouts.arraySize < 2) return results;

			var std_loadouts = find_prop(ser_params, "StandardLoadouts");
			results.warn("", "StandardLoadouts", std_loadouts.arraySize < 1, "should have at least 1 StandardLoadout", aircraft_parameters);
			// TODO: StandardLoadouts
			//if (std_loadouts.arraySize < 1) return results;
			
			// liveries
			var liveries = find_prop(ser_params, "liveries");
			results.critical("", "liveries", liveries.arraySize < 1, "must have at least 1 Livery", aircraft_parameters);
			if (liveries.arraySize < 1) return results;
			// overkill, probably..
			HashSet<string> factions_available = new HashSet<string>();
			bool is_faction_specific = false;
			for (int i = 0; i < liveries.arraySize; i++)
			{
				var livery = liveries.GetArrayElementAtIndex(i);
				var livery_asset = find_prop_rel<AssetReference>(livery, "assetReference");
				results.fail("", $"liveries.Array.data[{i}].assetReference", livery_asset == null, $"livery {i} missing asset", aircraft_parameters);
				results.warn("", $"liveries.Array.data[{i}].name", find_prop_rel<string>(livery, "name") == "", $"livery {i} missing name", aircraft_parameters);
				var faction_obj = find_prop_rel<UnityEngine.Object>(livery, "faction");
				if (faction_obj != null) {
					is_faction_specific = true;
					var ser_faction = new SerializedObject(faction_obj);
					factions_available.Add(find_prop<string>(ser_faction, "factionTag"));
				}
				results.info("", $"liveries.Array.data[{i}].faction", faction_obj == null, $"livery {i} missing faction (ignore if intended to be universal)", aircraft_parameters);
			}
			// this is ass
			string missing_faction = "";
			if (is_faction_specific) {
				if (!factions_available.Contains("BDF")) missing_faction += "BDF";
				if (!factions_available.Contains("PALA")) missing_faction += $"{(missing_faction != "" ? "," : "")}PALA";
			}
			results.fail("", "liveries", missing_faction != "", $"liveries are faction specific and {missing_faction} does not have any", aircraft_parameters);

			
			var airfoils = find_prop(ser_params, "airfoils");
			results.info("", "airfoils", airfoils.arraySize == 0, "no airfoils defined", aircraft_parameters);
			// for (int i = 0; i < airfoils.arraySize; i++)
			// {
			// 	var foil = airfoils.GetArrayElementAtIndex(i);
			// }

			var status_display = find_prop<UnityEngine.Object>(ser_params, "StatusDisplay");
			results.critical("", "StatusDisplay", status_display == null, "missing StatusDisplay", aircraft_parameters);
			if (status_display == null) return results;

			results.warn("", "HUDExtras", find_prop<UnityEngine.Object>(ser_params, "HUDExtras") == null, "missing HUDExtras", aircraft_parameters);




			string aircraft_name = find_prop<string>(ser_params, "aircraftName");
			results.warn("", "aircraftName", aircraft_name == "", "missing AircraftName", aircraft_parameters);
			string unit_name = find_prop<string>(ser_def, "unitName");
			results.info("", "aircraftName", aircraft_name != unit_name,
				$"AircraftName ({aircraft_name}) doesn't match def's UnitName ({unit_name})", aircraft_parameters); //, new List<Fix>{
				//	new Fix($"Use {aircraft_name}", () => Debug.Log($"use {aircraft_name}")),
				//	new Fix($"Use {unit_name}", () => Debug.Log($"use {unit_name}"))
				//});

			//results.info(find_prop<int>(ser_params, "rankRequired") <= 0, "RankRequired <= 0", aircraft_parameters);
			results.info("", "DefaultFuelLevel", find_prop<float>(ser_params, "DefaultFuelLevel") <= 0, "DefaultFuelLevel <= 0", aircraft_parameters);
			results.warn("", "takeoffMusic", find_prop<UnityEngine.Object>(ser_params, "takeoffMusic") == null, "missing TakeoffMusic", aircraft_parameters);

			var aoa_effects = find_prop(ser_params, "AoAEffects");
			results.warn("", "AoAEffects.AudioClip", find_prop_rel<UnityEngine.Object>(aoa_effects, "AudioClip") == null, "missing AoAEffects.AudioClip", aircraft_parameters);
			results.warn("", "AoAEffects.OnsetSpeed", find_prop_rel<float>(aoa_effects, "OnsetSpeed") <= 0, "AoAEffects.OnsetSpeed <= 0", aircraft_parameters);
			results.warn("", "AoAEffects.FullVolumeSpeed", find_prop_rel<float>(aoa_effects, "FullVolumeSpeed") <= 0, "AoAEffects.FullVolumeSpeed <= 0", aircraft_parameters);
			results.warn("", "AoAEffects.OnsetAlpha", find_prop_rel<float>(aoa_effects, "OnsetAlpha") <= 0, "AoAEffects.OnsetAlpha <= 0", aircraft_parameters);
			results.warn("", "AoAEffects.FullVolumeAlpha", find_prop_rel<float>(aoa_effects, "FullVolumeAlpha") <= 0, "AoAEffects.FullVolumeAlpha <= 0", aircraft_parameters);
			results.info("", "AoAEffects.ShakeFactor", find_prop_rel<float>(aoa_effects, "ShakeFactor") <= 0, "AoAEffects.ShakeFactor <= 0", aircraft_parameters);

			float g_limit = find_prop<float>(ser_params, "aircraftGLimit");
			var aircraft_info = find_prop(ser_def, "aircraftInfo");
			float maneuverability = find_prop_rel<float>(aircraft_info, "maneuverability");
			results.warn("", "aircraftGLimit", g_limit <= 0, "AircraftGLimit <= 0", aircraft_parameters);
			results.info("", "aircraftGLimit", g_limit != maneuverability,
				$"AircraftGLimit ({g_limit:F2}) doesn't match def's Maneuverability ({maneuverability:F2})", aircraft_parameters);//, new List<Fix>{
				//	new Fix($"Use {g_limit:F2}", () => Debug.Log($"use {g_limit}")),
				//	new Fix($"Use {maneuverability:F2}", () => Debug.Log($"use {maneuverability}"))
				//});

			results.info("", "PIDReferenceAirspeed", find_prop<float>(ser_params, "PIDReferenceAirspeed") <= 0, "PIDReferenceAirspeed <= 0", aircraft_parameters);
			results.info("", "maxSpeed", find_prop<float>(ser_params, "maxSpeed") <= 0, "MaxSpeed <= 0", aircraft_parameters);
			bool is_vtol = find_prop<bool>(ser_params, "verticalLanding");
			results.info("", "takeoffSpeed", !is_vtol && find_prop<float>(ser_params, "takeoffSpeed") <= 0, "TakeoffSpeed <= 0 (not marked as VTOL)", aircraft_parameters);
			results.info("", "takeoffDistance", !is_vtol && find_prop<float>(ser_params, "takeoffDistance") <= 0, "TakeoffDistance <= 0 (not marked as VTOL)", aircraft_parameters);
			results.info("", "turningRadius", find_prop<float>(ser_params, "turningRadius") <= 0, "TurningRadius <= 0", aircraft_parameters);
			results.info("", "cornerSpeed", find_prop<float>(ser_params, "cornerSpeed") <= 0, "CornerSpeed <= 0", aircraft_parameters);
			results.info("", "approachSpeed", find_prop<float>(ser_params, "approachSpeed") <= 0, "ApproachSpeed <= 0", aircraft_parameters);
			results.info("", "landingSpeed", find_prop<float>(ser_params, "landingSpeed") <= 0, "LandingSpeed <= 0", aircraft_parameters);
			results.info("", "shortLandingSpeed", find_prop<float>(ser_params, "shortLandingSpeed") <= 0, "ShortLandingSpeed <= 0", aircraft_parameters);
			results.info("", "cruiseThrottle", find_prop<float>(ser_params, "cruiseThrottle") <= 0, "CruiseThrottle <= 0", aircraft_parameters);
			results.info("", "minimumRadarAlt", find_prop<float>(ser_params, "minimumRadarAlt") <= 0, "MinimumRadarAlt <= 0", aircraft_parameters);
			results.info("", "groundTurningRadius", find_prop<float>(ser_params, "groundTurningRadius") <= 0, "GroundTurningRadius <= 0", aircraft_parameters);

			return results;
		}
	}
}

#endif