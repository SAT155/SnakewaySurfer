using System.Collections.Generic;

namespace SnakewaySurfer;

// What really stands in each biome: counted by the `surfkit` biome tour within 64 m of a spot in
// a real world (2026-11-11). The game's vegetation table says what it tries to place; this says
// what ended up there, so the minigame's verges take their mix and their crowding from it
// (SurfVegetation.cs). Pickables, effects and location parts are left out.
internal static class Census
{
	// The area counted, in square metres (a circle of 64 m).
	public const float Area = 12868f;

	public static readonly Dictionary<string, (string name, int count)[]> ByBiome = new()
	{
		["Meadows"] = new[]
		{
			("Bush01", 143), ("Beech1", 84), ("Rock_4", 83), ("Beech_small1", 71),
			("Beech_small2", 71), ("Rock_3", 25), ("Birch1", 8), ("FirTree_oldLog", 5),
			("stubbe", 4), ("Birch2", 3), ("Rock_7", 2), ("RockDolmen_2", 1),
			("stone_wall_2x1", 1), ("StatueDeer", 1), ("Oak1", 1),
		},
		["BlackForest"] = new[]
		{
			("shrub_2", 251), ("Pinetree_01", 124), ("FirTree_small", 61), ("Rock_4", 57),
			("stone_wall_2x1", 26), ("Rock_3", 19), ("stubbe", 14), ("FirTree", 11),
			("FirTree_oldLog", 9), ("rock4_forest", 4), ("Crow", 4), ("BlackForestLocationMusic", 3),
			("TreasureChest_blackforest", 2), ("DG_ForestCrypt", 1), ("Stone", 1), ("rock4_copper", 1),
		},
		["Swamp"] = new[]
		{
			("SwampTree1", 58), ("Rock_4", 41), ("SwampTree2", 23), ("FirTree_small_dead", 23),
			("shrub_2_heath", 15), ("Flies", 14), ("FirTree_oldLog", 7), ("wood_pole2", 6),
			("TreasureChest_sunkencrypt", 6), ("Coal", 4), ("root07", 4), ("root08", 3),
			("wood_wall_roof_top_45", 3), ("mudpile_beacon", 2), ("DG_SunkenCrypt", 2), ("sunken_crypt_gate", 2),
			("stubbe", 2), ("root12", 2), ("StatueEvil", 2), ("root11", 1),
			("sfx_imp_death", 1), ("SwampTree2_log", 1),
		},
		["Mountain"] = new[]
		{
			("Rock_4", 27), ("MineRock_Obsidian", 14), ("FirTree_small", 10), ("rock2_mountain", 10),
			("Rock_3", 10), ("rock3_mountain_1", 5), ("rock1_mountain", 5), ("silvervein", 1),
			("rock3_mountain", 1),
		},
		["Plains"] = new[]
		{
			("Bush02_en", 30), ("Rock_4_plains", 29), ("Bush01_heath", 17), ("rock4_heath", 10),
			("Birch1_aut", 5), ("BoneFragments", 5), ("lox_ribs", 3), ("Birch2_aut", 3),
			("Tar", 3), ("Stone", 3), ("shrub_2_heath", 2), ("TrophyGrowth", 1),
			("TarLiquid", 1),
		},
		["Mistlands"] = new[]
		{
			("cliff_mistlands2", 68), ("YggaShoot_small1", 35), ("rock_mistlands1", 8), ("YggaShoot2", 5),
			("cliff_mistlands1", 4), ("YggaShoot1", 3), ("YggaShoot3", 2), ("giant_sword2", 1),
			("YggdrasilRoot", 1), ("TreasureChest_dvergrtower", 1),
		},
		["AshLands"] = new[]
		{
			("cliff_ashlands6", 29), ("UnstableLavaRock", 1),
		},
		["DeepNorth"] = new[]
		{
			("SnowFirTree_small", 51), ("Pinetree_Snow_dead", 40), ("Bush01_deepnorth", 38), ("SnowFirTree", 36),
			("Pinetree_Snow", 31), ("Rock_4_deepnorth", 14), ("FrozenSkeleton_Pose2", 7), ("Rock_7_deepnorth", 6),
			("FrozenSkeleton_Pose1", 6), ("FrozenGD", 5), ("FirTree_oldLog_deepnorth", 3), ("Rock_3_deepnorth", 2),
			("StumpHole", 2), ("StumpLog", 2),
		},
	};
}
