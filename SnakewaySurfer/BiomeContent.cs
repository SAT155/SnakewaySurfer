using System;
using System.Collections.Generic;

namespace SnakewaySurfer;

// The biome content tables (architecture step 1.16.x, step 2): one table per biome that says what
// belongs there. Who lives there and what kind of challenge each one is (its schema), how stars
// change them, which dungeons open off its track and which traders keep a stall. The game's spawn
// tables only supply prefabs, weights and group sizes for the creatures listed here; anything else
// they list is left out (and logged), so a creature can't turn up in the wrong biome. Balance is
// a one-line edit here.
//
// It knows nothing of Unity, so the same file is compiled into the checks that run on every build
// (SnakewaySurfer.Tests).
//
// Settings still win where they exist: "Creature Schemas" defaults to the schemas below (SAT picks
// them creature by creature), and a biome's own monster list in the settings is used as given.

// One creature in a biome. Prefab: its prefab name; a trailing * matches a name start.
internal sealed class Native
{
	public string Prefab = "";
	// Its schema tags (Schemas.cs), the default for the "Creature Schemas" setting. Empty: none
	// here (another biome's row gives it, or the creature has none).
	public string Schema = "";

	public Native(string prefab, string schema = "")
	{
		Prefab = prefab;
		Schema = schema;
	}

	public bool Matches(string prefab) =>
		Prefab.EndsWith("*")
			? prefab.StartsWith(Prefab.Substring(0, Prefab.Length - 1), StringComparison.OrdinalIgnoreCase)
			: prefab.Equals(Prefab, StringComparison.OrdinalIgnoreCase);
}

// A trader's stall in a biome, on laps from MinStars to MaxStars (SurfTraders.cs).
internal sealed class TraderStall
{
	public string Prefab = "";
	public int MinStars;
	public int MaxStars = 99;
}

internal sealed class BiomeContent
{
	public string Biome = "";
	// Empty: anything the game's tables give (a biome whose prefab names were never seen).
	public Native[] Natives = Array.Empty<Native>();
	// Creatures that come as a weaving swarm at 0 stars on starred laps (SurfGame.StarTraits).
	public string[] StarSwarmers = Array.Empty<string>();
	// Dungeon themes by name (SurfCryptThemes.cs), taken in turn.
	public string[] Dungeons = Array.Empty<string>();
	public TraderStall[] Traders = Array.Empty<TraderStall>();

	public bool IsNative(string prefab)
	{
		if (Natives.Length == 0)
		{
			return true;
		}
		foreach (Native n in Natives)
		{
			if (n.Matches(prefab))
			{
				return true;
			}
		}
		return false;
	}

	public bool Swarms(string prefab) => Array.Exists(StarSwarmers, s => s.Equals(prefab, StringComparison.OrdinalIgnoreCase));

	public TraderStall? TraderAt(int stars) => Array.Find(Traders, t => stars >= t.MinStars && stars <= t.MaxStars);
}

internal static class BiomeContents
{
	public static readonly BiomeContent[] All =
	{
		new BiomeContent
		{
			Biome = "Meadows",
			// 1.16.150 (SAT: a greydwarf shaman's poison in the 0-star Meadows): forest folk that
			// stray to the Meadows' edge in the game don't belong to the Meadows here.
			Natives = new[]
			{
				new Native("Boar", "hurdle charger"), new Native("Neck", "hurdle waiter"), new Native("Greyling", "hurdle charger"),
				new Native("Deer", "crosser"), new Native("Hare", "crosser"),
			},
			// 1.16.150 (SAT never met a trader to try the shop): Haldor also keeps a stall in the
			// 0-star Meadows, until Hildir takes over from 1 star.
			Traders = new[] { new TraderStall { Prefab = "Hildir", MinStars = 1 }, new TraderStall { Prefab = "Haldor", MaxStars = 0 } },
		},
		new BiomeContent
		{
			Biome = "Black Forest",
			Natives = new[]
			{
				new Native("Greydwarf", "swarm charger"), new Native("Greydwarf_Elite", "sweep stomper waiter"), new Native("Greydwarf_Shaman", "gas2 waiter"),
				new Native("Troll", "lob smash stomper charger"), new Native("Skeleton", "shooter waiter"), new Native("Skeleton_NoArcher", "waiter"),
				new Native("Bjorn", "charger sweep"),
				new Native("Greydwarf*"), new Native("Skeleton*"), new Native("Ghost"), new Native("Greyling"), new Native("Deer"), new Native("Boar"),
			},
			StarSwarmers = new[] { "Greydwarf" },
			Dungeons = new[] { "burial chamber", "troll cave" },
			Traders = new[] { new TraderStall { Prefab = "Haldor" } },
		},
		new BiomeContent
		{
			Biome = "Swamp",
			Natives = new[]
			{
				new Native("Skeleton_Poison", "waiter"),
				new Native("Draugr", "shooter waiter charger sideline"), new Native("Draugr_Elite", "waiter charger"), new Native("Draugr_Ranged", "shooter waiter sideline"),
				new Native("Blob", "hurdle gas3 waiter"), new Native("BlobElite", "hurdle gas3 waiter"), new Native("Leech", "hurdle swarm switcher behind"),
				new Native("Abomination", "ambush smash sweep"), new Native("Wraith", "chaser"),
				new Native("Draugr*"), new Native("Skeleton*"), new Native("Leech*"), new Native("Surtling"), new Native("Bat"), new Native("Ghost"),
			},
			Dungeons = new[] { "sunken crypt" },
			Traders = new[] { new TraderStall { Prefab = "BogWitch" } },
		},
		new BiomeContent
		{
			Biome = "Mountains",
			Natives = new[]
			{
				new Native("Wolf", "hurdle switcher swarm charger"), new Native("Fenring", "leaper"), new Native("Ulv", "leaper"),
				new Native("Fenring_Cultist", "fire waiter"), new Native("StoneGolem", "ambush smash"), new Native("Hatchling", "flyshooter"),
				new Native("Wolf*"), new Native("Fenring*"), new Native("Bat"),
			},
			Dungeons = new[] { "frost cave" },
		},
		new BiomeContent
		{
			Biome = "Plains",
			Natives = new[]
			{
				new Native("Goblin", "swarm shooter charger line"), new Native("GoblinArcher", "shooter waiter"), new Native("GoblinBrute", "slams stomper waiter"),
				new Native("GoblinShaman", "fire waiter"), new Native("Lox", "herd stomper charger"), new Native("Deathsquito", "behind"),
				new Native("BlobTar", "shooter tar hurdle waiter"), new Native("Unbjorn", "charger sweep gas2"),
				new Native("Goblin*"), new Native("Lox*"),
			},
		},
		new BiomeContent
		{
			Biome = "Mistlands",
			Natives = new[]
			{
				new Native("Seeker", "switcher swarm leaper charger"), new Native("SeekerBrute", "charger sweep"), new Native("Tick", "hurdle swarm headleap charger"),
				new Native("Gjall", "bomb tickdrop"),
				new Native("Dverger*", "helper talk"),
				new Native("Seeker*"), new Native("Hare"),
			},
			Dungeons = new[] { "infested mine" },
		},
		new BiomeContent
		{
			Biome = "Ashlands",
			Natives = new[]
			{
				new Native("Charred_Archer", "shooter waiter sideline"), new Native("Charred_Mage", "fire waiter"),
				new Native("Charred*"), new Native("Asksvin*"), new Native("Morgen*"), new Native("FallenValkyrie"), new Native("Volture"),
				new Native("BlobLava"), new Native("Bonemaw*"),
			},
			Dungeons = new[] { "charred fortress" },
		},
		// The Deep North's prefab names were never seen: anything its tables give.
		new BiomeContent { Biome = "Deep North" },
	};

	public static BiomeContent? For(string biome) => Array.Find(All, c => c.Biome == biome);

	// The default for the "Creature Schemas" setting: every schema the tables give, once each.
	public static string SchemaDefaults()
	{
		List<string> parts = new();
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		foreach (BiomeContent c in All)
		{
			foreach (Native n in c.Natives)
			{
				if (n.Schema.Length > 0 && seen.Add(n.Prefab))
				{
					parts.Add($"{n.Prefab}: {n.Schema}");
				}
			}
		}
		return string.Join("; ", parts);
	}
}
