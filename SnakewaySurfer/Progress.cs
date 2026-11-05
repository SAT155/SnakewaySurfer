using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SnakewaySurfer;

// One biome's boss in the minigame: the game's own summoning item and amount, the world key
// that says it has been beaten, and how its fight goes (SurfBoss.cs).
internal sealed class BossDef
{
	public int Biome;
	public string Name = "";
	public string Prefab = "";
	public string Key = "";
	public string Item = "";
	public int Amount;
	// What you pick up on the track to hurt it ("|" separates fallbacks).
	public string HitItem = "";
	public string HitVerb = "";
	// Where it is: "run" (ahead, running away), "stand" (far ahead, moves on when you get close),
	// "side" (beside the track, keeping pace), "fly" (ahead, high up).
	public string Mode = "run";
	public float Distance = 26f;
	public float Fly;
	public float MaxHeight = 8f;
	// A creature it calls in, for "minions".
	public string Minion = "";
	// Hits are aimed: grabbing the weapon stops the run and you aim it at the boss.
	public bool Aim;
	// Hits are thrown: the item is a spear you carry and throw down your lane (SurfThrow.cs).
	public bool Throw;
	// Its moves in each of the three phases (one more phase per hit taken).
	public string[][] Moves = new string[0][];
	// A new trick for each star lap, added to every phase (index 0 = 1 star). "move:creature"
	// names the creature for "minions".
	public string[] StarMoves = new string[0];
	public Color Tint = new Color(1f, 0.3f, 0.1f);
	// Its arena (SurfArena.cs): the game's own location for it, looming far ahead; how high the
	// land rises beside the track; and the torches lining the path.
	public string Location = "";
	public float Walls = 4f;
	public string Torch = "piece_groundtorch_wood";
	// Its weather (SurfFx.cs): the environment of its location's EnvZone, else this one;
	// thunder and lightning flashes; extra effects and sounds where its strikes land
	// ("|" separates them).
	public string Env = "";
	public bool Storm;
	public string StormSound = "";
	public string StrikeFx = "";
	public string StrikeSfx = "";
	// What its roots or vines look like (the Elder's): a creature or prefab name.
	public string Vines = "";
	// How far behind you it chases (0 = from its size).
	public float Chase;
}

internal static class Progress
{
	public static readonly BossDef[] Bosses =
	{
		new BossDef
		{
			Biome = 0, Name = "Eikthyr", Prefab = "Eikthyr", Key = "defeated_eikthyr", Item = "TrophyDeer", Amount = 2,
			HitItem = "SpearFlint|SpearBronze|Thunderstone", HitVerb = "The spear strikes", Throw = true, Mode = "run", Distance = 24f, MaxHeight = 4f,
			Location = "Eikthyrnir", Walls = 4f, Torch = "piece_groundtorch_blue", Tint = new Color(0.4f, 0.7f, 1f),
			Env = "Eikthyr|ThunderStorm", Storm = true, StormSound = "sfx_thunder", StrikeFx = "fx_Lightning|lightningAOE", StrikeSfx = "sfx_thunder|sfx_eikthyr_attack",
			Moves = new[]
			{
				new[] { "shot", "shot", "strike1" },
				new[] { "shot", "stomp", "strike1" },
				new[] { "stomp", "shot", "jumprow", "strike2" },
			},
			StarMoves = new[] { "dash", "shot2", "minions:deer" },
		},
		new BossDef
		{
			Biome = 1, Name = "The Elder", Prefab = "gd_king", Key = "defeated_gdking", Item = "AncientSeed", Amount = 3,
			HitItem = "SurtlingCore|Resin", HitVerb = "The surtling core bursts on", Mode = "stand", Distance = 46f, MaxHeight = 9f,
			Location = "GDKing", Walls = 7f, Torch = "piece_groundtorch_green", Tint = new Color(0.45f, 0.75f, 0.2f),
			Env = "GDKing|DeepForest Mist", StrikeFx = "fx_gdking_rootspawn|vfx_gdking_stomp", StrikeSfx = "sfx_tentaroot_attack|sfx_gdking_stomp", Vines = "TentaRoot",
			Moves = new[]
			{
				new[] { "strike1", "strike2", "wall" },
				new[] { "branches", "strike1", "strike2" },
				new[] { "branches", "slide", "gap", "strike2" },
			},
			StarMoves = new[] { "minions:greydwarf", "lob2", "branches" },
		},
		new BossDef
		{
			Biome = 2, Name = "Bonemass", Prefab = "Bonemass", Key = "defeated_bonemass", Item = "WitheredBone", Amount = 10,
			HitItem = "MaceIron|MaceBronze|Club", HitVerb = "The mace smashes", Mode = "side", Distance = 14f, MaxHeight = 7f,
			Minion = "skeleton", Location = "Bonemass", Walls = 2.5f, Torch = "piece_groundtorch_green", Tint = new Color(0.45f, 0.75f, 0.2f),
			Env = "Bonemass|SwampRain", StrikeFx = "fx_Bonemass_aoe_start", StrikeSfx = "sfx_Bonemass_throw_trigger|sfx_Bonemass_punch_start",
			Moves = new[]
			{
				new[] { "blobs" },
				new[] { "minions", "blobs" },
				new[] { "stomp", "blobs", "minions" },
			},
			StarMoves = new[] { "gaswall", "minions:blob", "dash" },
		},
		new BossDef
		{
			Biome = 3, Name = "Moder", Prefab = "Dragon", Key = "defeated_dragon", Item = "DragonEgg", Amount = 3,
			HitItem = "BoltIron|BoltBone|ArrowIron", HitVerb = "The ballista bolt strikes", Mode = "fly", Distance = 30f, Fly = 7f, MaxHeight = 6f, Aim = true,
			Location = "Dragonqueen", Walls = 9f, Torch = "piece_groundtorch_blue", Tint = new Color(0.6f, 0.85f, 1f),
			Env = "Moder|SnowStorm",
			Moves = new[]
			{
				new[] { "sweep" },
				new[] { "jumprow", "slide", "sweep" },
				new[] { "strike2", "sweep", "jumprow" },
			},
			StarMoves = new[] { "minions:drake", "meteors", "gap" },
		},
		new BossDef
		{
			Biome = 4, Name = "Yagluth", Prefab = "GoblinKing", Key = "defeated_goblinking", Item = "GoblinTotem", Amount = 5,
			HitItem = "ArrowFrost|StaffIceShards|FreezeGland", HitVerb = "The frost bites", Mode = "stand", Distance = 50f, MaxHeight = 9f,
			Location = "GoblinKing", Walls = 3f, Torch = "piece_groundtorch_wood", Tint = new Color(1f, 0.35f, 0.05f),
			Env = "GoblinKing|Heath clear",
			Moves = new[]
			{
				new[] { "meteors" },
				new[] { "beam", "meteors" },
				new[] { "lob2", "meteors", "beam" },
			},
			StarMoves = new[] { "minions:goblin", "firewall", "sweep" },
		},
		new BossDef
		{
			Biome = 5, Name = "The Queen", Prefab = "SeekerQueen", Key = "defeated_queen", Item = "DvergrKeyFragment", Amount = 3,
			HitItem = "BombLava|BombBile|StaffLightning", HitVerb = "The blast rocks", Mode = "side", Distance = 16f, MaxHeight = 8f,
			Minion = "seeker", Location = "Mistlands_DvergrBossEntrance1", Walls = 6f, Torch = "piece_groundtorch_mist", Tint = new Color(0.7f, 0.5f, 1f),
			Env = "Queen|Mistlands_thunder",
			Moves = new[]
			{
				new[] { "grab" },
				new[] { "lob2", "minions", "grab" },
				new[] { "slide", "grab", "lob2" },
			},
			StarMoves = new[] { "minions:tick", "branches", "dash" },
		},
	};

	public static BossDef? BossOf(int biome) => biome >= 0 && biome < Bosses.Length ? Bosses[biome] : null;

	// The index of the furthest boss beaten in this world (-1: none yet).
	public static int FurthestBoss()
	{
		int furthest = -1;
		for (int i = 0; i < Bosses.Length; i++)
		{
			if (Beaten(Bosses[i]))
			{
				furthest = i;
			}
		}
		return furthest;
	}

	public static bool Beaten(BossDef boss)
	{
		try
		{
			// Worlds with player-based keys keep boss kills on the character instead.
			return (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(boss.Key))
				|| (Player.m_localPlayer != null && Player.m_localPlayer.HaveUniqueKey(boss.Key));
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"Couldn't read world key {boss.Key}: {e.Message}");
			return false;
		}
	}

	// How many biomes of a lap you may run: the Meadows, plus one past each boss up to the
	// furthest one beaten in this world.
	public static int AllowedBiomes(int lap)
	{
		if (Plugin.IgnoreBossGate.Value)
		{
			return lap;
		}
		return Mathf.Clamp(FurthestBoss() + 2, 1, lap);
	}

	// ---------------------------------------------------------------- checkpoints

	// Saved on the character, one list per world: "biome:stars,biome:stars".
	private static string Key() => "SnakewaySurfer.Checkpoints." + WorldId();

	private static string WorldId()
	{
		try
		{
			if (ZNet.instance != null)
			{
				string name = ZNet.instance.GetWorldName();
				if (!string.IsNullOrEmpty(name))
				{
					return name;
				}
				long uid = ZNet.instance.GetWorldUID();
				if (uid != 0)
				{
					return uid.ToString(CultureInfo.InvariantCulture);
				}
			}
		}
		catch (Exception)
		{
		}
		return "world";
	}

	public static List<(int biome, int stars)> Checkpoints(Player player)
	{
		List<(int, int)> list = new() { (0, 0) };
		if (player.m_customData.TryGetValue(Key(), out string value))
		{
			foreach (string part in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string[] bits = part.Split(':');
				if (bits.Length == 2
					&& int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int b)
					&& int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)
					&& !list.Contains((b, s)))
				{
					list.Add((b, s));
				}
			}
		}
		return list;
	}

	public static void SaveCheckpoint(Player player, int biome, int stars)
	{
		List<(int biome, int stars)> list = Checkpoints(player);
		if (list.Contains((biome, stars)))
		{
			return;
		}
		list.Add((biome, stars));
		List<string> parts = new();
		foreach ((int b, int s) in list)
		{
			if (b != 0 || s != 0)
			{
				parts.Add(b.ToString(CultureInfo.InvariantCulture) + ":" + s.ToString(CultureInfo.InvariantCulture));
			}
		}
		player.m_customData[Key()] = string.Join(",", parts);
	}

	// The furthest checkpoint saved (more stars first, then the later biome).
	public static (int biome, int stars) Latest(Player player, int allowed)
	{
		(int biome, int stars) best = (0, 0);
		foreach ((int b, int s) in Checkpoints(player))
		{
			if (b < allowed && (s > best.stars || (s == best.stars && b > best.biome)))
			{
				best = (b, s);
			}
		}
		return best;
	}

	public static string Describe(int biome, int stars) =>
		Biomes.Defaults[Mathf.Clamp(biome, 0, Biomes.Defaults.Length - 1)].Name + (stars > 0 ? $" ({stars} star{(stars > 1 ? "s" : "")})" : "");

	// "surfsup", "surfsup swamp", "surfsup 3 1", "surfsup black forest 2": where to start.
	// Returns null and an explanation when the start isn't allowed.
	public static (int biome, int stars)? ParseStart(Player player, string[] args, int lap, out string error, out bool explicitStart)
	{
		error = "";
		explicitStart = false;
		int allowed = AllowedBiomes(lap);
		List<string> words = new();
		for (int i = 1; i < args.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(args[i]))
			{
				words.Add(args[i].Trim());
			}
		}
		if (words.Count == 0)
		{
			return Latest(player, allowed);
		}
		explicitStart = true;
		int stars = 0;
		if (words.Count > 1 && int.TryParse(words[words.Count - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int st) && st >= 0 && st <= 3)
		{
			stars = st;
			words.RemoveAt(words.Count - 1);
		}
		string what = string.Join("", words).Replace("_", "").Replace("-", "").ToLowerInvariant();
		int biome = -1;
		if (int.TryParse(what, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
		{
			biome = n - 1;
		}
		else if (what == "start")
		{
			biome = 0;
		}
		else
		{
			for (int i = 0; i < Biomes.Defaults.Length; i++)
			{
				string name = Biomes.Defaults[i].Name.Replace(" ", "").ToLowerInvariant();
				if (what.Length >= 3 && name.StartsWith(what))
				{
					biome = i;
					break;
				}
			}
		}
		if (biome < 0 || biome >= lap)
		{
			error = $"Unknown start \"{string.Join(" ", words)}\". Use a biome name or number (1 = Meadows) and optionally stars, e.g. \"surfsup swamp\" or \"surfsup 2 1\".";
			return null;
		}
		if (Plugin.IgnoreBossGate.Value)
		{
			return (biome, stars);
		}
		if (biome >= allowed)
		{
			BossDef? boss = BossOf(biome - 1);
			error = $"You can't reach the {Biomes.Defaults[biome].Name} yet: defeat {(boss != null ? boss.Name : "its boss")} in this world first.";
			return null;
		}
		if (!Checkpoints(player).Contains((biome, stars)))
		{
			error = $"No checkpoint at {Describe(biome, stars)} yet. Beat the boss before it in the minigame first. Yours: {ListCheckpoints(player, allowed)}.";
			return null;
		}
		return (biome, stars);
	}

	public static string ListCheckpoints(Player player, int allowed)
	{
		List<string> names = new();
		foreach ((int b, int s) in Checkpoints(player))
		{
			if (b < allowed)
			{
				names.Add(Describe(b, s));
			}
		}
		return string.Join(", ", names);
	}
}
