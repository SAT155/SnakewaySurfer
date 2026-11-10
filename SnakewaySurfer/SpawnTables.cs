using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// A creature that can turn up in a biome, with how common it is and how big its groups are.
internal sealed class SpawnEntry
{
	public GameObject Prefab = null!;
	public string Label = "";
	public float Weight = 1f;
	public int GroupMin = 1;
	public int GroupMax = 1;
	public bool Flying;
}

// The game's own world spawn tables (SpawnSystemList.m_spawners), so each biome gets the
// creatures the game really spawns there, as often as it spawns them, in its group sizes.
internal static class SpawnTables
{
	internal static readonly Heightmap.Biome[] s_order =
	{
		Heightmap.Biome.Meadows,
		Heightmap.Biome.BlackForest,
		Heightmap.Biome.Swamp,
		Heightmap.Biome.Mountain,
		Heightmap.Biome.Plains,
		Heightmap.Biome.Mistlands,
		Heightmap.Biome.AshLands,
		Heightmap.Biome.DeepNorth,
	};

	// Creatures whose entries are spread evenly over many biomes (mod and late-game "anywhere"
	// spawns, one entry per biome) don't belong to any of them; a creature that lives mostly in one
	// biome isn't kept where it only strays in.
	public static List<SpawnEntry> ForBiome(int index)
	{
		List<SpawnEntry> result = new();
		if (index < 0 || index >= s_order.Length)
		{
			return result;
		}
		List<SpawnEntry>[] all = new List<SpawnEntry>[s_order.Length];
		Dictionary<GameObject, (int count, float max, float min)> spread = new();
		for (int i = 0; i < s_order.Length; i++)
		{
			all[i] = RawForBiome(i, i == index);
			foreach (SpawnEntry e in all[i])
			{
				spread[e.Prefab] = spread.TryGetValue(e.Prefab, out var t) ? (t.count + 1, Mathf.Max(t.max, e.Weight), Mathf.Min(t.min, e.Weight)) : (1, e.Weight, e.Weight);
			}
		}
		List<string> dropped = new();
		foreach (SpawnEntry e in all[index])
		{
			var t = spread[e.Prefab];
			bool keep = t.count >= 4 ? t.max >= 2f * t.min && e.Weight >= 0.5f * t.max : e.Weight >= 0.1f * t.max;
			if (keep)
			{
				result.Add(e);
			}
			else
			{
				dropped.Add($"{e.Prefab.name} ({t.count} biomes)");
			}
		}
		if (dropped.Count > 0)
		{
			Plugin.Log.LogInfo($"{s_order[index]}: left out {string.Join(", ", dropped)}");
		}
		return result;
	}

	private static List<SpawnEntry> RawForBiome(int index, bool log)
	{
		List<SpawnEntry> result = new();
		List<string> raw = new();
		if (index < 0 || index >= s_order.Length)
		{
			return result;
		}
		Heightmap.Biome biome = s_order[index];
		Dictionary<GameObject, SpawnEntry> byPrefab = new();
		HashSet<SpawnSystem.SpawnData> seen = new();
		foreach (SpawnSystemList list in Resources.FindObjectsOfTypeAll<SpawnSystemList>())
		{
			if (list == null || list.m_spawners == null)
			{
				continue;
			}
			foreach (SpawnSystem.SpawnData d in list.m_spawners)
			{
				if (d == null || !seen.Add(d) || !d.m_enabled || d.m_prefab == null || (d.m_biome & biome) == 0)
				{
					continue;
				}
				// Event-only and "everywhere" entries (late-game invasions that can turn up in any
				// biome) aren't what lives here.
				if (!string.IsNullOrEmpty(d.m_requiredPersistentEvent) || BiomeCount(d.m_biome) >= 4)
				{
					continue;
				}
				Character c = d.m_prefab.GetComponent<Character>();
				if (c == null || c is Player || c.m_boss)
				{
					continue;
				}
				// 1.16.150 (Sam: a greydwarf shaman's poison in the 0-star Meadows): forest folk that
				// stray to the Meadows' edge in the game don't belong to the Meadows here.
				if (biome == Heightmap.Biome.Meadows
					&& (d.m_prefab.name.StartsWith("Greydwarf") || d.m_prefab.name.StartsWith("Skeleton") || d.m_prefab.name.StartsWith("Troll")))
				{
					continue;
				}
				// How many of it are around on average: the cap times the chance per try,
				// tries per minute.
				// How many of it are around: mostly its cap (spawners refill up to it), a little
				// more or less by how often it tries and how likely each try is.
				float rate = Mathf.Max(0.01f, d.m_spawnChance / 100f) * 600f / Mathf.Max(1f, d.m_spawnInterval);
				// Capped at 6 so one creature with a big cap (swamp leeches: 10) doesn't crowd out the rest.
				float weight = Mathf.Clamp(d.m_maxSpawned, 1, 6) * Mathf.Clamp(Mathf.Sqrt(rate), 0.25f, 1.5f);
				if (log)
				{
					string flags = (d.m_spawnAtDay ? "" : " night") + (d.m_spawnAtNight ? "" : " day")
						+ (d.m_requiredEnvironments != null && d.m_requiredEnvironments.Count > 0 ? " weather" : "")
						+ (string.IsNullOrEmpty(d.m_requiredGlobalKey) ? "" : " key " + d.m_requiredGlobalKey);
					raw.Add($"{d.m_prefab.name}(max {d.m_maxSpawned} chance {d.m_spawnChance:0} every {d.m_spawnInterval:0}s group {d.m_groupSizeMin}-{d.m_groupSizeMax}{flags})");
				}
				if (!d.m_spawnAtDay)
				{
					weight *= 0.35f;
				}
				if (d.m_requiredEnvironments != null && d.m_requiredEnvironments.Count > 0)
				{
					weight *= 0.35f;
				}
				if (!string.IsNullOrEmpty(d.m_requiredGlobalKey))
				{
					weight *= 0.3f;
				}
				if (!byPrefab.TryGetValue(d.m_prefab, out SpawnEntry e))
				{
					e = new SpawnEntry
					{
						Prefab = d.m_prefab,
						Label = Label(d.m_prefab),
						Weight = 0f,
						GroupMin = Mathf.Max(1, d.m_groupSizeMin),
						GroupMax = Mathf.Max(1, d.m_groupSizeMax),
						Flying = c.m_flying,
					};
					byPrefab[d.m_prefab] = e;
				}
				e.Weight += weight;
				e.GroupMax = Mathf.Max(e.GroupMax, d.m_groupSizeMax);
			}
		}
		result.AddRange(byPrefab.Values);
		if (log)
		{
			Plugin.Log.LogInfo($"{s_order[index]} spawn entries: {string.Join(", ", raw)}");
		}
		return result;
	}

	private static int BiomeCount(Heightmap.Biome biome)
	{
		int n = 0;
		foreach (Heightmap.Biome b in s_order)
		{
			if ((biome & b) != 0)
			{
				n++;
			}
		}
		return n;
	}

	public static string Label(GameObject prefab)
	{
		Character c = prefab.GetComponent<Character>();
		string name = c != null ? c.m_name : prefab.name;
		return Localization.instance != null ? Localization.instance.Localize(name) : name;
	}

	// "greydwarf:6:3, troll:1" = name, optional weight, optional biggest group size.
	public static List<SpawnEntry> Parse(string list, List<string> missing)
	{
		List<SpawnEntry> result = new();
		foreach (string item in Pictures.SplitList(list))
		{
			string[] parts = item.Split(':');
			GameObject? prefab = Pictures.Monster(parts[0].Trim());
			if (prefab == null)
			{
				missing.Add(parts[0].Trim());
				continue;
			}
			float weight = parts.Length > 1 && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float w) ? w : 1f;
			int group = parts.Length > 2 && int.TryParse(parts[2].Trim(), out int g) ? g : 1;
			Character c = prefab.GetComponent<Character>();
			result.Add(new SpawnEntry { Prefab = prefab, Label = Label(prefab), Weight = weight, GroupMin = 1, GroupMax = Mathf.Max(1, group), Flying = c != null && c.m_flying });
		}
		return result;
	}

	public static SpawnEntry? Pick(List<SpawnEntry> list)
	{
		if (list.Count == 0)
		{
			return null;
		}
		float total = 0f;
		foreach (SpawnEntry e in list)
		{
			total += e.Weight;
		}
		float r = Random.value * total;
		foreach (SpawnEntry e in list)
		{
			r -= e.Weight;
			if (r <= 0f)
			{
				return e;
			}
		}
		return list[list.Count - 1];
	}

	public static bool Contains(string list, GameObject prefab)
	{
		foreach (string key in Pictures.SplitList(list))
		{
			GameObject? p = Pictures.Monster(key);
			if (p == prefab || string.Equals(key, prefab.name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}
}
