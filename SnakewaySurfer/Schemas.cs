using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// What kind of challenge each creature poses, from the "Creature Schemas" setting (Sam picked
// these creature by creature). A creature can have several tags; where they are alternatives
// (waiter or charger, smash or sweep) one is picked each time it turns up.
//
// Movement: charger (comes at you down its lane), waiter (stands, may taunt), switcher (weaves
// between lanes). Formations: swarm (a group spread over the lanes), herd (a group crossing the
// track), line (two lanes at once, find the gap), crosser (bolts across the track).
// Attacks: shooter (its own projectile at your lane), flyshooter (the same from the air), sweep
// (hits its lane and a neighbour, both marked), gas2 / gas3 (a cloud over two marked lanes, or
// all three: jump or slide through), smash (a shockwave down all lanes, jump it), slams (marked
// lane slams then a shockwave), lob (a rock landing ahead of you over two marked lanes, jump it),
// fire (a burning patch on two or three marked lanes, jump it), tar (a slowing patch on two marked
// lanes), bomb (fire bombs from the air on marked lanes), tickdrop (drops ticks onto the lanes),
// leaper (leaps into your lane, landing spot marked), headleap (some leap at head height: slide).
// Others: hurdle (short enough to jump), stomper (heavy, shakes the camera), ambush (hidden until
// close), chaser (comes from behind and swipes your lane), behind (some dive in from behind with a
// buzz: slide; ground creatures swim or creep up), helper (friendly, beside the track, sometimes
// clears a threat ahead), talk (shouts its lines as you pass), sideline (1.16.1: stands beside the
// track and shoots at your lane as you come).
internal sealed class Schema
{
	public readonly HashSet<string> Tags = new(StringComparer.OrdinalIgnoreCase);

	public bool Has(string tag) => Tags.Contains(tag);

	// One of the given tags this creature has, picked at random (null if none).
	public string? PickOf(params string[] options)
	{
		List<string> have = new();
		foreach (string o in options)
		{
			if (Tags.Contains(o))
			{
				have.Add(o);
			}
		}
		return have.Count == 0 ? null : have[Random.Range(0, have.Count)];
	}
}

internal static class Schemas
{
	public const string Defaults =
		"Boar: hurdle charger; Neck: hurdle waiter; Greyling: hurdle charger; Deer: crosser; Hare: crosser; " +
		"Greydwarf: swarm charger; Greydwarf_Elite: sweep stomper waiter; Greydwarf_Shaman: gas2 waiter; " +
		"Troll: lob smash stomper charger; Skeleton: shooter waiter; Skeleton_NoArcher: waiter; Skeleton_Poison: waiter; " +
		"Bjorn: charger sweep; " +
		"Draugr: shooter waiter charger sideline; Draugr_Elite: waiter charger; Draugr_Ranged: shooter waiter sideline; " +
		"Blob: hurdle gas3 waiter; BlobElite: hurdle gas3 waiter; Leech: hurdle swarm switcher behind; " +
		"Abomination: ambush smash sweep; Wraith: chaser; " +
		"Wolf: hurdle switcher swarm charger; Fenring: leaper; Ulv: leaper; Fenring_Cultist: fire waiter; StoneGolem: ambush smash; Hatchling: flyshooter; " +
		"Goblin: swarm shooter charger line; GoblinArcher: shooter waiter; GoblinBrute: slams stomper waiter; GoblinShaman: fire waiter; " +
		"Lox: herd stomper charger; Deathsquito: behind; BlobTar: shooter tar hurdle waiter; Unbjorn: charger sweep gas2; " +
		"Seeker: switcher swarm leaper charger; SeekerBrute: charger sweep; Tick: hurdle swarm headleap charger; Gjall: bomb tickdrop; " +
		"Dverger*: helper talk";

	// 1.16.1 (Sam's test): entries changed since earlier releases, swapped in where a settings
	// file still has the old one (an entry someone changed themselves is left alone).
	private static readonly (string old, string now)[] s_changed =
	{
		("Draugr: shooter waiter charger", "Draugr: shooter waiter charger sideline"),
		("Draugr_Ranged: shooter waiter", "Draugr_Ranged: shooter waiter sideline"),
		("Leech: hurdle swarm waiter", "Leech: hurdle swarm switcher behind"),
		("Wolf: switcher swarm charger", "Wolf: hurdle switcher swarm charger"),
	};

	public static string Upgrade(string value)
	{
		List<string> parts = new(value.Split(';'));
		for (int i = 0; i < parts.Count; i++)
		{
			string entry = parts[i].Trim();
			foreach ((string old, string now) in s_changed)
			{
				if (string.Equals(entry, old, StringComparison.OrdinalIgnoreCase))
				{
					parts[i] = parts[i].Replace(entry, now);
				}
			}
		}
		// 1.16.6: creatures added since, appended to a settings file that doesn't name them.
		foreach (string added in s_added)
		{
			string key = added.Substring(0, added.IndexOf(':')).Trim();
			if (!parts.Exists(p => p.IndexOf(':') > 0 && string.Equals(p.Substring(0, p.IndexOf(':')).Trim(), key, StringComparison.OrdinalIgnoreCase)))
			{
				parts.Add(" " + added);
			}
		}
		return string.Join(";", parts);
	}

	private static readonly string[] s_added =
	{
		"Fenring_Cultist: fire waiter",
	};

	private static string s_parsedFrom = "\u0001";
	private static readonly List<(string key, bool prefix, Schema schema)> s_rules = new();
	private static readonly Dictionary<GameObject, Schema?> s_cache = new();

	public static Schema? For(GameObject prefab)
	{
		string setting = Plugin.CreatureSchemas.Value;
		if (setting != s_parsedFrom)
		{
			Parse(setting);
		}
		if (s_cache.TryGetValue(prefab, out Schema? cached))
		{
			return cached;
		}
		Schema? found = null;
		foreach ((string key, bool prefix, Schema schema) in s_rules)
		{
			bool match = prefix
				? prefab.name.StartsWith(key, StringComparison.OrdinalIgnoreCase)
				: string.Equals(prefab.name, key, StringComparison.OrdinalIgnoreCase) || Pictures.Monster(key) == prefab;
			if (match)
			{
				found = schema;
				break;
			}
		}
		s_cache[prefab] = found;
		return found;
	}

	private static void Parse(string setting)
	{
		s_parsedFrom = setting;
		s_rules.Clear();
		s_cache.Clear();
		foreach (string part in setting.Split(';'))
		{
			int colon = part.IndexOf(':');
			if (colon <= 0)
			{
				continue;
			}
			string key = part.Substring(0, colon).Trim();
			bool prefix = key.EndsWith("*");
			if (prefix)
			{
				key = key.TrimEnd('*');
			}
			Schema schema = new();
			foreach (string tag in part.Substring(colon + 1).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				schema.Tags.Add(tag.Trim());
			}
			if (key.Length > 0 && schema.Tags.Count > 0)
			{
				s_rules.Add((key, prefix, schema));
			}
		}
	}
}
