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
	// 1.16.1 (Sam: Bonemass's landmark faced away and didn't read as a giant thing): a turn
	// for the backdrop beyond facing the runner, and how much bigger than the usual fit.
	public float BackdropTurn;
	public float BackdropSize = 1f;
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
			Minion = "skeleton", Location = "Bonemass", BackdropTurn = 180f, BackdropSize = 1.9f, Walls = 2.5f, Torch = "piece_groundtorch_green", Tint = new Color(0.45f, 0.75f, 0.2f),
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
			HitItem = "ArrowObsidian|ArrowIron|ArrowFlint", HitVerb = "The obsidian arrow strikes", Mode = "fly", Distance = 46f, Fly = 7f, MaxHeight = 6f, Aim = true,
			Location = "Dragonqueen", Walls = 9f, Torch = "piece_groundtorch_blue", Tint = new Color(0.6f, 0.85f, 1f),
			Env = "Moder|SnowStorm",
			Moves = new[]
			{
				// 1.16.2 (Sam): her ice volley from the air and her frost breath once she lands,
				// as in the game; no more logs and bars.
				new[] { "icevolley", "icevolley", "land" },
				new[] { "icevolley", "icewide", "land" },
				new[] { "icewide", "icevolley", "land", "icewide" },
			},
			StarMoves = new[] { "minions:drake", "meteors", "gap" },
		},
		new BossDef
		{
			Biome = 4, Name = "Yagluth", Prefab = "GoblinKing", Key = "defeated_goblinking", Item = "GoblinTotem", Amount = 5,
			HitItem = "ArrowFrost|StaffIceShards|FreezeGland", HitVerb = "The frost bites", Mode = "stand", Distance = 50f, MaxHeight = 9f, Aim = true,
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
		// 1.16.13 (gap doc): Fader, the Ashlands' boss, far ahead in his ring of pillars: rocks
		// falling from the burning sky, walls of flame, and lava pouring across the lanes. Only
		// in a lap that reaches the Ashlands (Loop Biomes 7 or more), once he's beaten in this world.
		new BossDef
		{
			Biome = 6, Name = "Fader", Prefab = "Fader", Key = "defeated_fader", Item = "BellFragment|Bell|FlametalNew|Flametal", Amount = 3,
			HitItem = "SwordDyrnwyn|SwordNiedhogg|SwordMistwalker|FreezeGland", HitVerb = "The blade strikes", Mode = "stand", Distance = 52f, MaxHeight = 10f,
			Minion = "charred_melee", Location = "FaderLocation", Walls = 6f, Torch = "piece_groundtorch_wood", Tint = new Color(1f, 0.4f, 0.1f),
			Env = "Fader|Ashlands_storm|Ashlands_ashrain",
			Moves = new[]
			{
				new[] { "meteors", "lava" },
				new[] { "firewall", "meteors", "lava" },
				new[] { "lava", "firewall", "meteors", "lava" },
			},
			StarMoves = new[] { "minions:charred_melee", "minions:charred_archer", "sweep" },
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

	// 1.16: `sea` is the ocean crossing before that biome.
	public static string Describe(int biome, int stars, bool sea) =>
		sea ? "the Ocean" + (stars > 0 ? $" ({stars} star{(stars > 1 ? "s" : "")})" : "") : Describe(biome, stars);

	// 1.16: the laps (stars) whose ocean crossing you've made in this world. Once made, the
	// Plains themselves open as a start. Progress from before the crossing existed (a checkpoint
	// past the Plains) counts as made.
	private static string LandedKey() => "SnakewaySurfer.Landed." + WorldId();

	public static bool Landed(Player player, int stars, int plains)
	{
		if (player.m_customData.TryGetValue(LandedKey(), out string value))
		{
			foreach (string part in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s == stars)
				{
					return true;
				}
			}
		}
		return Checkpoints(player).Exists(c => c.stars == stars && c.biome > plains);
	}

	// 1.16.79 (Sam): one ad hoc save in the middle of a biome (or at an island of the crossing),
	// to start again from after a fall. Only ever one: each new one replaces the last, and beating
	// a boss or making landfall clears it (those save real checkpoints). "biome,stars,sea,items,along".
	private static string ResumeKey() => "SnakewaySurfer.Resume." + WorldId();

	public static void SaveResume(Player player, int biome, int stars, bool sea, int items, float along)
	{
		player.m_customData[ResumeKey()] = string.Join(",",
			biome.ToString(CultureInfo.InvariantCulture), stars.ToString(CultureInfo.InvariantCulture), sea ? "1" : "0",
			items.ToString(CultureInfo.InvariantCulture), along.ToString("0.#", CultureInfo.InvariantCulture));
	}

	public static bool Resume(Player player, out int biome, out int stars, out bool sea, out int items, out float along)
	{
		biome = stars = items = 0;
		sea = false;
		along = 0f;
		if (player == null || !player.m_customData.TryGetValue(ResumeKey(), out string value))
		{
			return false;
		}
		string[] bits = value.Split(',');
		if (bits.Length != 5)
		{
			return false;
		}
		sea = bits[2] == "1";
		return int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out biome)
			&& int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out stars)
			&& int.TryParse(bits[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out items)
			&& float.TryParse(bits[4], NumberStyles.Float, CultureInfo.InvariantCulture, out along);
	}

	public static void ClearResume(Player player)
	{
		player?.m_customData.Remove(ResumeKey());
	}

	public static void SaveLanded(Player player, int stars)
	{
		if (player.m_customData.TryGetValue(LandedKey(), out string value))
		{
			foreach (string part in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s == stars)
				{
					return;
				}
			}
		}
		string add = stars.ToString(CultureInfo.InvariantCulture);
		player.m_customData[LandedKey()] = string.IsNullOrEmpty(value) ? add : value + "," + add;
	}

	// "surfsup", "surfsup swamp", "surfsup 3 1", "surfsup black forest 2": where to start.
	// Returns null and an explanation when the start isn't allowed.
	public static (int biome, int stars)? ParseStart(Player player, string[] args, int lap, out string error, out bool explicitStart, out bool sea)
	{
		error = "";
		explicitStart = false;
		sea = false;
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
		else if (what == "ocean" || what == "sea" || what == "crossing")
		{
			// The crossing before the Plains: its checkpoint is the Plains'.
			biome = 4;
			sea = true;
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

	// ---------------------------------------------------------------- testing (surfprogress)

	// 1.16.16: set once the first run (its dream) has been.
	public const string FirstRunKey = "SnakewaySurfer.FirstRun";

	private static int Lap() => Plugin.LoopBiomes.Value <= 0 ? Biomes.Defaults.Length : Mathf.Clamp(Plugin.LoopBiomes.Value, 1, Biomes.Defaults.Length);

	// 1.16.16 (Sam): "surfprogress": see, erase or set this character's progress.
	public static string Command(Player player, string[] args)
	{
		string verb = args.Length > 1 ? args[1].Trim().ToLowerInvariant() : "";
		if (verb == "wipe" || verb == "erase" || verb == "reset")
		{
			List<string> keys = new();
			foreach (string key in player.m_customData.Keys)
			{
				if (key.StartsWith("SnakewaySurfer", StringComparison.Ordinal))
				{
					keys.Add(key);
				}
			}
			foreach (string key in keys)
			{
				player.m_customData.Remove(key);
			}
			return $"surfprogress: erased {keys.Count} SnakewaySurfer record(s) from this character (checkpoints and crossings in every world, golden shoes, the food timer, the first-run dream). Next surfsup is a first run again.";
		}
		if (verb == "dream")
		{
			player.m_customData.Remove(FirstRunKey);
			return "surfprogress: the next surfsup is a first run again (the dream, no menu).";
		}
		if (verb == "set")
		{
			int lap = Lap();
			List<string> words = new();
			for (int i = 2; i < args.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(args[i]))
				{
					words.Add(args[i].Trim());
				}
			}
			int stars = 0;
			if (words.Count > 1 && int.TryParse(words[words.Count - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int st))
			{
				stars = Mathf.Clamp(st, 0, 3);
				words.RemoveAt(words.Count - 1);
			}
			string named = string.Join("", words).ToLowerInvariant();
			bool sea = named == "ocean" || named == "sea" || named == "crossing";
			int biome = ParseBiome(named);
			if (biome < 0 || biome >= lap)
			{
				return "surfprogress: name a biome (or its number, 1 = Meadows) and optionally stars, e.g. \"surfprogress set swamp\" or \"surfprogress set plains 1\".";
			}
			if (Plugin.LoopBiomes.Value <= 0)
			{
				stars = 0;
			}
			// Every checkpoint up to it: all of the earlier laps, and this lap up to the biome.
			List<string> parts = new();
			for (int s = 0; s <= stars; s++)
			{
				for (int b = 0; b < lap && (s < stars || b <= biome); b++)
				{
					if (b != 0 || s != 0)
					{
						parts.Add(b.ToString(CultureInfo.InvariantCulture) + ":" + s.ToString(CultureInfo.InvariantCulture));
					}
				}
			}
			player.m_customData.Remove(LandedKey());
			if (parts.Count > 0)
			{
				player.m_customData[Key()] = string.Join(",", parts);
			}
			else
			{
				player.m_customData.Remove(Key());
			}
			if (biome > SeaBiome || (biome == SeaBiome && !sea))
			{
				// Set to the Plains or beyond: the crossing on this lap is made too.
				SaveLanded(player, stars);
			}
			player.m_customData[FirstRunKey] = "1";
			int allowed = AllowedBiomes(lap);
			string gate = biome >= allowed ? $" (this world only lets runs reach the {Biomes.Defaults[allowed - 1].Name} until its bosses are beaten; Ignore Boss Gate opens the rest)" : "";
			return $"surfprogress: checkpoints in this world now run up to {Describe(biome, stars, sea)}{gate}.";
		}
		if (verb.Length > 0)
		{
			return "surfprogress: use \"surfprogress\", \"surfprogress wipe\", \"surfprogress set <biome> [stars]\" or \"surfprogress dream\".";
		}
		int all = AllowedBiomes(Lap());
		string first = player.m_customData.ContainsKey(FirstRunKey) ? "done" : "still to come";
		return $"surfprogress: checkpoints in this world: {ListCheckpoints(player, Lap())}. Runs here reach the {Biomes.Defaults[all - 1].Name}. First-run dream: {first}.";
	}

	// The Plains (its ocean crossing comes before it).
	private const int SeaBiome = 4;

	private static int ParseBiome(string text)
	{
		string what = text.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
		if (int.TryParse(what, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
		{
			return n - 1;
		}
		if (what == "ocean" || what == "sea" || what == "crossing")
		{
			return SeaBiome;
		}
		for (int i = 0; i < Biomes.Defaults.Length; i++)
		{
			string name = Biomes.Defaults[i].Name.Replace(" ", "").ToLowerInvariant();
			if (what.Length >= 3 && name.StartsWith(what))
			{
				return i;
			}
		}
		return -1;
	}
}
