using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using static hexagonest.Tools.Utilities;


namespace hexagonest.Tools
{
	// this is gonna be a pain in the dick
	// WeaponManager has a list of WeaponMounts
	// WeaponMount points to a Prefab (prefab) and WeaponInfo (info)
	// as well as emptyMass/emptyDrag/drag
	// Prefab offers the offset of the payload's mass (painfully)
	//	 in Prefab we look for MountedCargo(s), MountedMissile(s), or just go fuck ourselves i guess
	// Info offers massPerRound
	// known cases:
	// • Cargo: potentially multiple MountedCargo objects in the same prefab. CG probably comes from collider
	// • Missiles/bombs: same as above but MountedMissile
	//		also pylons can keep some extra mass and drag because go fuck yourself
	// • ECM pod: emptyMass more like empty go fuck yourself. CG comes from collider probably
	// • Gun: emptyMass seems most important, then Ammo * info.massPerRound
	//		frankly i have no idea where the mass goes on these. they do not have colliders
	[Serializable] public class HardpointManager
	{
		public AircraftPhysics parent_physics; public HardpointGroup[] hardpoint_groups;
		public Vector3 stores_cg; public float stores_mass;

		public HardpointManager(AircraftPhysics set_parent_physics, SerializedObject weapon_manager)
		{
			var manager_groups = weapon_manager.FindProperty("hardpointSets");
			HardpointGroup[] new_hardpoint_groups = new HardpointGroup[manager_groups.arraySize];

			for (int i = 0; i < manager_groups.arraySize; i++)
			{
				var hardpoint_group = manager_groups.GetArrayElementAtIndex(i);
				string name = hardpoint_group.FindPropertyRelative("name").stringValue;
				var precluding_hardpoints = hardpoint_group.FindPropertyRelative("precludingHardpointSets");
				bool symmetry_with_prev = hardpoint_group.FindPropertyRelative("SymmetryWithPrev").boolValue;
				string symmetry_name = hardpoint_group.FindPropertyRelative("SymmetryName").stringValue;
				var weapon_options = hardpoint_group.FindPropertyRelative("weaponOptions");
				var hardpoints = hardpoint_group.FindPropertyRelative("hardpoints");

				int[] precluding_hardpoints_array = new int[precluding_hardpoints.arraySize];
				for (int j = 0; j < precluding_hardpoints.arraySize; j++) { precluding_hardpoints_array[j] = precluding_hardpoints.GetArrayElementAtIndex(j).intValue; }

				SerializedObject[] weapon_options_array = new SerializedObject[weapon_options.arraySize];
				string[] weapon_names_array = new string[weapon_options.arraySize];
				for (int j = 0; j < weapon_options.arraySize; j++) {
					var option = weapon_options.GetArrayElementAtIndex(j).objectReferenceValue;
					if (option != null)
					{
						var s_option = new SerializedObject(option);
						weapon_options_array[j] = s_option;
						weapon_names_array[j] = s_option.FindProperty("jsonKey").stringValue;
					}
					else
					{
						weapon_options_array[j] = null;
						weapon_names_array[j] = "None";
					}
				}

				Hardpoint[] hardpoints_array = new Hardpoint[hardpoints.arraySize];
				for (int j = 0; j < hardpoints.arraySize; j++) {
					var hardpoint_data = hardpoints.GetArrayElementAtIndex(j);
					Transform hardpoint_transform = (Transform)hardpoint_data.FindPropertyRelative("transform").objectReferenceValue;
					Component hardpoint_part = (Component)hardpoint_data.FindPropertyRelative("part").objectReferenceValue;
					var s_part = new SerializedObject(hardpoint_part);

					hardpoints_array[j] = new Hardpoint {
						transform = hardpoint_transform, aero_part = s_part
					};
				}

				new_hardpoint_groups[i] = new HardpointGroup {
					manager_index = i,
					name = name, precluding_hardpoints = precluding_hardpoints_array,
					symmetry_with_prev = symmetry_with_prev, symmetry_name = symmetry_name,
					weapon_options = weapon_options_array, option_names = weapon_names_array,
					currently_mounted = 0,
					hardpoints = hardpoints_array,
					cg = Vector3.zero, mass = 0f,
				};
			}
			hardpoint_groups = new_hardpoint_groups;
			parent_physics = set_parent_physics;
			stores_cg = Vector3.zero; stores_mass = 0f;
		}

		public (Vector3 stores_cg, float stores_mass) recalculate_cg()
		{
			Vector3 sum_mass = Vector3.zero; float total_mass = 0f;
			foreach (HardpointGroup group in hardpoint_groups) {
				sum_mass += group.cg * group.mass;
				total_mass += group.mass;
			}
			if (total_mass > 0f) { stores_cg = sum_mass / total_mass; stores_mass = total_mass; }
			else { stores_cg = sum_mass; stores_mass = total_mass; }

			parent_physics.set_stores_phys(stores_cg, stores_mass);

			return (stores_cg, stores_mass);
		}

		public void set_group_mounted_option(int group, int option)
		{
			Debug.Log($"[HardpointManager] set group {group} to {option}");
			hardpoint_groups[group].set_mounted_option(option);
			recalculate_cg();
		}
	}

	[Serializable] public class HardpointGroup
	{
		public int manager_index;
		public string name; public int[] precluding_hardpoints;
		public bool symmetry_with_prev; public string symmetry_name;
		public SerializedObject[] weapon_options; public string[] option_names;
		public int currently_mounted;
		public Hardpoint[] hardpoints;
		public Vector3 cg; public float mass;

		public void set_mounted_option(int option)
		{
			currently_mounted = option;
			// TODO: this
		}
	}

	[Serializable] public struct Hardpoint
	{
		public Transform transform; public SerializedObject aero_part;
		public Part associated_part;
	}
}
