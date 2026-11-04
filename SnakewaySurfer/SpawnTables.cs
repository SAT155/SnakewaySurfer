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
	private static readonly Heightmap.Biome[] s_order =
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

	public static List<SpawnEntry> ForBiome(int index)
	{
		List<SpawnEntry> result = new();
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
				Character c = d.m_prefab.GetComponent<Character>();
				if (c == null || c is Player || c.m_boss)
				{
					continue;
				}
				// How many of it are around on average: the cap times the chance per try,
				// tries per minute.
				float weight = Mathf.Max(0.01f, d.m_spawnChance / 100f) * Mathf.Max(1, d.m_maxSpawned) * 60f / Mathf.Max(1f, d.m_spawnInterval);
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
					weight *= 0.6f;
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
		return result;
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
