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
	public string Scenery = "";
	public string Logs = "";
	public string Foods = "";
}

internal static class Biomes
{
	public static readonly BiomeDef[] Defaults =
	{
		new BiomeDef
		{
			Name = "Meadows",
			Ground = new Color(0.36f, 0.52f, 0.22f),
			Monsters = "greyling, boar, neck, deer",
			Flyers = "",
			Scenery = "Beech1, Beech_small1, Beech_small2, Oak1, Bush01, Rock_3",
			Logs = "beech_log_half, Oak_log_half",
			Foods = "raspberries, honey, meat_cooked, deer_meat_cooked, boarjerky",
		},
		new BiomeDef
		{
			Name = "Black Forest",
			Ground = new Color(0.20f, 0.30f, 0.16f),
			Monsters = "greydwarf, greydwarfbrute, greydwarfshaman, troll, skeleton, bjorn",
			Flyers = "",
			Scenery = "FirTree, Pinetree_01, FirTree_small, FirTree_oldLog, rock4_forest",
			Logs = "FirTree_log_half, PineTree_log_half",
			Foods = "blueberries, carrotsoup, deerstew, queensjam, mushroomyellow",
		},
		new BiomeDef
		{
			Name = "Swamp",
			Ground = new Color(0.24f, 0.22f, 0.14f),
			Monsters = "draugr, draugrelite, blob, abomination, wraith, skeletonpoison",
			Flyers = "",
			Scenery = "SwampTree1, SwampTree2, SwampTree2_darkland, stubbe, Rock_4",
			Logs = "SwampTree1_log, SwampTree2_log",
			Foods = "sausages, turnipstew, blacksoup, shocklatesmoothie",
		},
		new BiomeDef
		{
			Name = "Mountains",
			Ground = new Color(0.86f, 0.89f, 0.93f),
			Monsters = "wolf, fenring, stonegolem, ulv, trollfrost",
			Flyers = "drake, bat",
			Scenery = "Pinetree_Snow, SnowFirTree, Pinetree_Snow_dead, rock1_mountain, rock2_mountain, rock3_mountain",
			Logs = "PineTree_Snow_log_half, FirTree_Snow_log_half",
			Foods = "eyescream, onionsoup, wolfjerky, wolf_skewer, wolf_meat_cooked",
		},
		new BiomeDef
		{
			Name = "Plains",
			Ground = new Color(0.62f, 0.58f, 0.30f),
			Monsters = "goblin, goblinbrute, goblinshaman, lox, unbjorn",
			Flyers = "deathsquito",
			Scenery = "Birch1, Birch2, Birch1_aut, Birch2_aut, rock2_heath, rock4_heath, Bush01",
			Logs = "Birch_log_half",
			Foods = "bloodpudding, bread, fishwraps, loxpie, loxmeat_cooked, cloudberries",
		},
		new BiomeDef
		{
			Name = "Mistlands",
			Ground = new Color(0.30f, 0.28f, 0.33f),
			Monsters = "seeker, seekerbrute, tick, dvergr, dvergr_mage",
			Flyers = "",
			Scenery = "YggaShoot1, YggaShoot2, YggaShoot3, YggaShoot_small1, rock1_mistlands",
			Logs = "yggashoot_log_half",
			Foods = "salad, mushroomomelette, seekeraspic, yggdrasilporridge, mistharesupreme, fishandbread, honeyglazedchicken, magicallystuffedmushroom",
		},
		new BiomeDef
		{
			Name = "Ashlands",
			Ground = new Color(0.17f, 0.12f, 0.11f),
			Monsters = "charred_melee, charred_archer, charred_mage, asksvin, morgen, fallenvalkyrie",
			Flyers = "volture",
			Scenery = "AshlandsTree1, AshlandsTree3, AshlandsTree4, AshlandsTree5, AshlandsTreeStump1",
			Logs = "AshlandsTreeLogHalf1, AshlandsTreeLogHalf2",
			Foods = "scorchingmedley, sparklingshroomshake, piquantpie, roastedcrustpie, sizzlingberrybroth, marinatedgreens, spicymarmalade, mashedmeat",
		},
		new BiomeDef
		{
			Name = "Deep North",
			Ground = new Color(0.80f, 0.88f, 0.95f),
			Monsters = "frysling, jotun_warrior, jotun_witch, kvastur, moose, trollfrost, blobfrost, writhan",
			Flyers = "bat",
			Scenery = "SnowFirTree, Pinetree_Snow, FirTree_oldLog_deepnorth, rock3_ice, Pinetree_Snow_dead",
			Logs = "PineTree_Snow_log_half, FirTree_Snow_log_half",
			Foods = "sealsoup, smokedmoosemeat, moose_meat_cooked, meatballsmashedpoteitr, oatmeallingonberryjam, blubber_cooked",
		},
	};
}
