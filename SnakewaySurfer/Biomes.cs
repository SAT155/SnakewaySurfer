using UnityEngine;

namespace SnakewaySurfer;

// One stretch of the run. Everything game-specific is a plain name list so it can be changed in
// the config without a new build. Monsters and foods are named by their localization key (the
// part after "$enemy_" / "$item_"), which the game ships for every creature and item, so the
// right prefab is found even where the prefab name differs (Deep North names were never seen).
internal sealed class BiomeDef
{
	public string Name = "";
	public Color Ground;
	public string Monsters = "";
	public string Flyers = "";
	// Used when the game's spawn tables can't be read; also the pre-1.5 defaults.
	public string FallbackMonsters = "";
	public string FallbackFlyers = "";
	public string Scenery = "";
	public string Logs = "";
	public string Foods = "";
	public string Giants = "";
	// Creatures that live at places in the biome (troll caves, crypts, villages) rather than in
	// the world spawn tables; added to an "auto" list. Same format as Monsters.
	public string Locations = "";
	// Homes put beside the track near their creatures: "Prefab: home, home; Prefab*: home"
	// (a trailing * matches a name start).
	public string Landmarks = "";
	// Plants, mushrooms, stones and bushes along the track edge (prefab names).
	public string Pickables = "";
}

internal static class Biomes
{
	public static readonly BiomeDef[] Defaults =
	{
		new BiomeDef
		{
			Name = "Meadows",
			Landmarks = "Neck: Pickable_Flint; Boar: Pickable_Mushroom",
			Pickables = "Pickable_Dandelion, Pickable_Mushroom, Pickable_Flint, Pickable_Stone, Pickable_Branch, RaspberryBush",
			Ground = new Color(0.36f, 0.52f, 0.22f),
			Monsters = "auto",
			FallbackMonsters = "greyling, boar, neck, deer",
			Flyers = "auto",
			FallbackFlyers = "",
			Scenery = "Beech1, Beech_small1, Beech_small2, Oak1, Bush01, Rock_3",
			Logs = "beech_log_half, Oak_log_half",
			Foods = "raspberries, honey, meat_cooked, deer_meat_cooked, boarjerky",
		},
		new BiomeDef
		{
			Name = "Black Forest",
			Landmarks = "Greydwarf*: Spawner_GreydwarfNest; Skeleton: BonePileSpawner; Troll: Pickable_Stone",
			Pickables = "Pickable_Mushroom_yellow, Pickable_Thistle, Pickable_SeedCarrot, Pickable_Branch, Pickable_Stone, BlueberryBush",
			Locations = "troll:1.2, skeleton:0.6",
			Ground = new Color(0.20f, 0.30f, 0.16f),
			Monsters = "auto",
			FallbackMonsters = "greydwarf, greydwarfbrute, greydwarfshaman, troll, skeleton, bjorn",
			Giants = "troll",
			Flyers = "auto",
			FallbackFlyers = "",
			Scenery = "FirTree, Pinetree_01, FirTree_small, FirTree_oldLog, rock4_forest",
			Logs = "FirTree_log_half, PineTree_log_half",
			Foods = "blueberries, carrotsoup, deerstew, queensjam, mushroomyellow",
		},
		new BiomeDef
		{
			Name = "Swamp",
			Landmarks = "Draugr*: Spawner_DraugrPile; Skeleton*: BonePileSpawner_swamp; Blob*: Pickable_Mushroom",
			Pickables = "Pickable_SeedTurnip, Pickable_Branch, Pickable_Mushroom",
			Locations = "draugr:0.8, draugrelite:0.4, skeletonpoison:0.4",
			Giants = "abomination",
			Ground = new Color(0.24f, 0.22f, 0.14f),
			Monsters = "auto",
			FallbackMonsters = "draugr, draugrelite, blob, abomination, wraith, skeletonpoison",
			Flyers = "auto",
			FallbackFlyers = "",
			Scenery = "SwampTree1, SwampTree2, SwampTree2_darkland, stubbe, Rock_4",
			Logs = "SwampTree1_log, SwampTree2_log",
			Foods = "sausages, turnipstew, blacksoup, shocklatesmoothie",
		},
		new BiomeDef
		{
			Name = "Mountains",
			Landmarks = "Hatchling: Pickable_DragonEgg; Fenring: Pickable_Obsidian",
			Pickables = "Pickable_Branch_Snow, Pickable_Stone, Pickable_Obsidian, Pickable_SeedOnion",
			Locations = "fenring:0.6, ulv:0.6",
			Ground = new Color(0.86f, 0.89f, 0.93f),
			Monsters = "auto",
			FallbackMonsters = "wolf, fenring, stonegolem, ulv, trollfrost",
			Flyers = "auto",
			FallbackFlyers = "drake, bat",
			Scenery = "Pinetree_Snow, SnowFirTree, Pinetree_Snow_dead, rock1_mountain, rock2_mountain, rock3_mountain",
			Logs = "PineTree_Snow_log_half, FirTree_Snow_log_half",
			Foods = "eyescream, onionsoup, wolfjerky, wolf_skewer, wolf_meat_cooked",
		},
		new BiomeDef
		{
			Name = "Plains",
			Landmarks = "Goblin*: goblin_totempole, goblin_banner, goblin_fence, goblin_trashpile",
			Pickables = "Pickable_Barley_Wild, Pickable_Flax_Wild, CloudberryBush, Pickable_Branch",
			Locations = "goblinbrute:0.5, goblinshaman:0.5",
			Ground = new Color(0.62f, 0.58f, 0.30f),
			Monsters = "auto",
			FallbackMonsters = "goblin, goblinbrute, goblinshaman, lox, unbjorn",
			Flyers = "auto",
			FallbackFlyers = "deathsquito",
			Scenery = "Birch1, Birch2, Birch1_aut, Birch2_aut, rock2_heath, rock4_heath, Bush01",
			Logs = "Birch_log_half",
			Foods = "bloodpudding, bread, fishwraps, loxpie, loxmeat_cooked, cloudberries",
		},
		new BiomeDef
		{
			Name = "Mistlands",
			Landmarks = "Dverger*: dvergrprops_crate, dvergrprops_barrel, dvergrprops_lantern_standing, dvergrprops_banner; Seeker*: Pickable_RoyalJelly; Tick: Pickable_RoyalJelly",
			Pickables = "Pickable_Mushroom_Magecap, Pickable_Mushroom_JotunPuffs, Pickable_DvergrLantern",
			Locations = "seekerbrute:0.4, seeker:0.8, tick:0.4",
			Ground = new Color(0.30f, 0.28f, 0.33f),
			Monsters = "auto",
			FallbackMonsters = "seeker, seekerbrute, tick, dvergr, dvergr_mage",
			Flyers = "auto",
			FallbackFlyers = "",
			Scenery = "YggaShoot1, YggaShoot2, YggaShoot3, YggaShoot_small1, rock1_mistlands",
			Logs = "yggashoot_log_half",
			Foods = "salad, mushroomomelette, seekeraspic, yggdrasilporridge, mistharesupreme, fishandbread, honeyglazedchicken, magicallystuffedmushroom",
		},
		new BiomeDef
		{
			Name = "Ashlands",
			Landmarks = "Charred*: Pickable_Charredskull",
			Pickables = "Pickable_Ashstone, Pickable_Charredskull, Pickable_SmokePuff",
			Ground = new Color(0.17f, 0.12f, 0.11f),
			Monsters = "auto",
			FallbackMonsters = "charred_melee, charred_archer, charred_mage, asksvin, morgen, fallenvalkyrie",
			Flyers = "auto",
			FallbackFlyers = "volture",
			Scenery = "AshlandsTree1, AshlandsTree3, AshlandsTree4, AshlandsTree5, AshlandsTreeStump1",
			Logs = "AshlandsTreeLogHalf1, AshlandsTreeLogHalf2",
			Foods = "scorchingmedley, sparklingshroomshake, piquantpie, roastedcrustpie, sizzlingberrybroth, marinatedgreens, spicymarmalade, mashedmeat",
		},
		new BiomeDef
		{
			Name = "Deep North",
			Landmarks = "",
			Pickables = "Pickable_Branch_Snow, Pickable_Snowball",
			Ground = new Color(0.80f, 0.88f, 0.95f),
			Monsters = "auto",
			FallbackMonsters = "frysling, jotun_warrior, jotun_witch, kvastur, moose, trollfrost, blobfrost, writhan",
			Flyers = "auto",
			FallbackFlyers = "bat",
			Scenery = "SnowFirTree, Pinetree_Snow, FirTree_oldLog_deepnorth, rock3_ice, Pinetree_Snow_dead",
			Logs = "PineTree_Snow_log_half, FirTree_Snow_log_half",
			Foods = "sealsoup, smokedmoosemeat, moose_meat_cooked, meatballsmashedpoteitr, oatmeallingonberryjam, blubber_cooked",
		},
	};
}
