using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

internal static class Rewards
{
	private const string LastFoodKey = "SnakewaySurfer_LastFoodUtc";

	// ---------------------------------------------------------------- Journey hauls (1.17)

	// 1.17 (run structure step 5): winning a Journey earns a small handful of its biome's basics,
	// given when you wake from the dream. Each star adds half again; a Journey already won gives
	// half. Item prefab names; ones this game version lacks are skipped.
	public static readonly string[] Hauls =
	{
		"Wood:10, Stone:10, Flint:5",
		"RoundLog:5, CopperOre:3, TinOre:2",
		"ElderBark:5, IronScrap:3, Guck:2",
		"SilverOre:3, Obsidian:4, FreezeGland:2",
		"BlackMetalScrap:3, Flax:5, Barley:5",
		"BlackMarble:4, Sap:2, BlackCore:1",
		"Grausten:5, FlametalOreNew:2, CharredBone:3",
		"FreezeGland:3, Crystal:3, Obsidian:3",
	};

	public const string PendingHaulKey = "SnakewaySurfer.PendingHaul";

	public static List<(GameObject prefab, int count)> Haul(int biome, int stars, bool repeat)
	{
		List<(GameObject, int)> haul = new();
		if (biome < 0 || biome >= Hauls.Length)
		{
			return haul;
		}
		float scale = (1f + 0.5f * Mathf.Clamp(stars, 0, 3)) * (repeat ? 0.5f : 1f);
		foreach (string entry in Pictures.SplitList(Hauls[biome]))
		{
			string[] bits = entry.Split(':');
			if (bits.Length != 2 || !int.TryParse(bits[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
			{
				continue;
			}
			GameObject? prefab = Pictures.Item(bits[0].Trim());
			if (prefab == null || prefab.GetComponent<ItemDrop>() == null)
			{
				continue;
			}
			haul.Add((prefab, Mathf.Max(1, Mathf.RoundToInt(n * scale))));
		}
		return haul;
	}

	public static string Describe(List<(GameObject prefab, int count)> haul) =>
		haul.Count == 0 ? "nothing" : string.Join(", ", haul.ConvertAll(h => $"{ItemName(h.prefab)} x{h.count}"));

	private static string ItemName(GameObject prefab)
	{
		string name = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
		return Localization.instance != null ? Localization.instance.Localize(name) : name;
	}

	// Kept on the character until you wake, so a run that ends badly doesn't lose it.
	public static void AddPending(Player player, List<(GameObject prefab, int count)> haul)
	{
		Dictionary<string, int> pending = Pending(player);
		foreach ((GameObject prefab, int count) in haul)
		{
			pending.TryGetValue(prefab.name, out int had);
			pending[prefab.name] = had + count;
		}
		List<string> parts = new();
		foreach (KeyValuePair<string, int> kv in pending)
		{
			parts.Add(kv.Key + ":" + kv.Value.ToString(CultureInfo.InvariantCulture));
		}
		player.m_customData[PendingHaulKey] = string.Join(",", parts);
	}

	private static Dictionary<string, int> Pending(Player player)
	{
		Dictionary<string, int> pending = new();
		if (player.m_customData.TryGetValue(PendingHaulKey, out string value))
		{
			foreach (string part in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string[] bits = part.Split(':');
				if (bits.Length == 2 && int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0)
				{
					pending.TryGetValue(bits[0], out int had);
					pending[bits[0]] = had + n;
				}
			}
		}
		return pending;
	}

	// On waking: into the bags, and what doesn't fit drops at your feet as ordinary items.
	public static string? GivePending(Player player)
	{
		if (player == null || !player.m_customData.ContainsKey(PendingHaulKey))
		{
			return null;
		}
		Dictionary<string, int> pending = Pending(player);
		player.m_customData.Remove(PendingHaulKey);
		List<(GameObject, int)> given = new();
		Inventory inventory = player.GetInventory();
		foreach (KeyValuePair<string, int> kv in pending)
		{
			GameObject? prefab = Pictures.Item(kv.Key);
			if (prefab == null || prefab.GetComponent<ItemDrop>() == null)
			{
				continue;
			}
			int dropped = 0;
			for (int i = 0; i < kv.Value; i++)
			{
				if (inventory.CanAddItem(prefab, 1))
				{
					inventory.AddItem(prefab, 1);
				}
				else
				{
					Object.Instantiate(prefab, player.transform.position + player.transform.forward + Vector3.up * (1f + 0.15f * dropped++), Quaternion.identity);
				}
			}
			given.Add((prefab, kv.Value));
		}
		if (given.Count == 0)
		{
			return null;
		}
		string text = Describe(given);
		Plugin.Log.LogInfo($"Journey reward given on waking: {text}.");
		return text;
	}

	// Gives Run XP for the distance and, when allowed, one food from the highest biome reached.
	// Returns the lines to show on the results screen.
	public static string Grant(Player player, float distance, int highestBiome, float seconds, List<BiomeDef> biomes, float blockRaise = 0f, float dodgeRaise = 0f, ICollection<int>? biomesRun = null)
	{
		List<string> lines = new();

		// Real XP (1.14, SAT): what running in the world gives for the same time, scaled.
		float xpSeconds = Plugin.MaxRunXpSeconds.Value > 0f ? Mathf.Min(seconds, Plugin.MaxRunXpSeconds.Value) : seconds;
		float raise = xpSeconds * Plugin.RunXpPerSecond.Value;
		if (raise > 0.001f)
		{
			float gained = AddSkillXp(player, Skills.SkillType.Run, raise);
			if (gained > 0.001f)
			{
				lines.Add($"Run skill +{gained:0.00} levels");
			}
		}
		// 1.16.1 (SAT): Blocking and Dodge XP as the game gives it for each block, parry and
		// dodge (Humanoid.BlockAttack raises Blocking by 1, or 2 for a parry).
		foreach ((Skills.SkillType type, float amount, string name) in new[] { (Skills.SkillType.Blocking, blockRaise, "Blocking"), (Skills.SkillType.Dodge, dodgeRaise, "Dodge") })
		{
			if (amount > 0.001f)
			{
				float gained = AddSkillXp(player, type, amount);
				if (gained > 0.001f)
				{
					lines.Add($"{name} skill +{gained:0.00} levels");
				}
			}
		}

		if (seconds < Plugin.MinRunSecondsForFood.Value)
		{
			lines.Add($"Run at least {Plugin.MinRunSecondsForFood.Value:0} seconds to earn food");
		}
		else
		{
			TimeSpan wait = FoodCooldownLeft(player);
			if (wait > TimeSpan.Zero)
			{
				lines.Add($"Next food reward in {Math.Ceiling(wait.TotalMinutes):0} min");
			}
			else
			{
				// 1.16.23 (SAT): one food from each biome you ran through, each its own biome's.
				List<string> found = new();
				if (biomesRun != null && biomesRun.Count > 0)
				{
					List<int> order = new(biomesRun);
					order.Sort();
					foreach (int b in order)
					{
						if (GiveFood(player, b, biomes, false) is string one)
						{
							found.Add(one);
						}
					}
				}
				if (found.Count == 0 && GiveFood(player, highestBiome, biomes, true) is string any)
				{
					found.Add(any);
				}
				if (found.Count > 0)
				{
					player.m_customData[LastFoodKey] = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
					lines.Add($"Found along the way: {string.Join(", ", found)}!");
				}
			}
		}
		// Golden shoes still owed from a boss rush (the bag was full).
		if (SurferShoes.TryDeliver(player) is string shoes)
		{
			lines.Add(shoes);
		}
		return string.Join("\n", lines);
	}

	// 1.16.79 (SAT: "Anytime the player does the draugr dungeon, they get an iron per star level
	// to their inventory. Same for the other materials", with "that 30 minute timer just like the
	// food items"): each dungeon cleared gives its own material, one more per star, at most once
	// per Food Cooldown Minutes (one timer for all dungeons, saved on the character).
	private const string LastLootKey = "SnakewaySurfer_LastLootUtc";

	public static string? GiveDungeonLoot(Player player, string items, int amount, string key = LastLootKey)
	{
		if (player == null || amount <= 0 || Cooldown(player, key) > TimeSpan.Zero)
		{
			return null;
		}
		GameObject? prefab = null;
		foreach (string entry in Pictures.SplitList(items))
		{
			prefab = Pictures.Item(entry);
			if (prefab != null)
			{
				break;
			}
		}
		if (prefab == null || prefab.GetComponent<ItemDrop>() is not ItemDrop drop)
		{
			return null;
		}
		Inventory inventory = player.GetInventory();
		for (int i = 0; i < amount; i++)
		{
			if (inventory.CanAddItem(prefab, 1))
			{
				inventory.AddItem(prefab, 1);
			}
			else
			{
				Object.Instantiate(prefab, player.transform.position + player.transform.forward + Vector3.up * (1f + 0.3f * i), Quaternion.identity);
			}
		}
		player.m_customData[key] = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
		string name = drop.m_itemData.m_shared.m_name;
		return (Localization.instance != null ? Localization.instance.Localize(name) : name) + (amount > 1 ? $" x{amount}" : "");
	}

	// 1.16.80: a boss's crafting drop at the camp after it, on its own 30-minute timer.
	public static string? GiveBossDrop(Player player, string items, int amount) => GiveDungeonLoot(player, items, amount, "SnakewaySurfer_LastBossDropUtc");

	public static TimeSpan LootCooldownLeft(Player player) => Cooldown(player, LastLootKey);

	private static TimeSpan Cooldown(Player player, string key)
	{
		if (!player.m_customData.TryGetValue(key, out string value)
			|| !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
		{
			return TimeSpan.Zero;
		}
		DateTime last = new DateTime(ticks, DateTimeKind.Utc);
		TimeSpan left = last.AddMinutes(Plugin.FoodCooldownMinutes.Value) - DateTime.UtcNow;
		return left.TotalMinutes > Plugin.FoodCooldownMinutes.Value ? TimeSpan.Zero : left;
	}

	private static TimeSpan FoodCooldownLeft(Player player)
	{
		if (!player.m_customData.TryGetValue(LastFoodKey, out string value)
			|| !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
		{
			return TimeSpan.Zero;
		}
		DateTime last = new DateTime(ticks, DateTimeKind.Utc);
		TimeSpan left = last.AddMinutes(Plugin.FoodCooldownMinutes.Value) - DateTime.UtcNow;
		// A clock that jumped backwards shouldn't lock the reward for days.
		return left.TotalMinutes > Plugin.FoodCooldownMinutes.Value ? TimeSpan.Zero : left;
	}

	// Adds Run XP as the game does while you run in the world: it raises the Run skill by
	// factor 1 for each second of running (Player.Update), so `raise` is in seconds of running.
	// Skills.RaiseSkill levels up at most once per call and resets the accumulator, so it is fed
	// in steps that stop at each level. Returns the levels gained (fractions included).
	private static float AddSkillXp(Player player, Skills.SkillType type, float raise)
	{
		Skills skills = player.m_skills;
		if (skills == null)
		{
			return 0f;
		}
		Skills.Skill skill = skills.GetSkill(type);
		float stepRate = skill.m_info.m_increseStep * Game.m_skillGainRate;
		if (stepRate <= 0f)
		{
			return 0f;
		}
		float before = skill.m_level + skill.m_accumulator / Mathf.Max(0.0001f, skill.GetNextLevelRequirement());
		float remaining = raise;
		for (int guard = 0; remaining > 0.0001f && skill.m_level < 100f && guard < 200; guard++)
		{
			float req = skill.GetNextLevelRequirement();
			if (req <= 0f)
			{
				break;
			}
			float toLevel = Mathf.Max(0f, req - skill.m_accumulator) / stepRate;
			if (remaining >= toLevel)
			{
				skills.RaiseSkill(type, toLevel + 0.0001f);
				remaining -= toLevel;
			}
			else
			{
				skills.RaiseSkill(type, remaining);
				remaining = 0f;
			}
		}
		float after = skill.m_level + skill.m_accumulator / Mathf.Max(0.0001f, skill.GetNextLevelRequirement());
		return after - before;
	}

	private static string? GiveFood(Player player, int highestBiome, List<BiomeDef> biomes, bool fallBack)
	{
		// That biome's food; with `fallBack`, a lower biome's if none of its foods exist.
		int top = Mathf.Clamp(highestBiome, 0, biomes.Count - 1);
		for (int b = top; b >= (fallBack ? 0 : top); b--)
		{
			List<GameObject> foods = new();
			foreach (string key in Pictures.SplitList(biomes[b].Foods))
			{
				GameObject? prefab = Pictures.Item(key);
				if (prefab != null)
				{
					foods.Add(prefab);
				}
			}
			if (foods.Count == 0)
			{
				continue;
			}
			GameObject food = foods[Random.Range(0, foods.Count)];
			Inventory inventory = player.GetInventory();
			if (inventory.CanAddItem(food, 1))
			{
				inventory.AddItem(food, 1);
			}
			else
			{
				// Full bags: drop it at the player's feet as a normal item everyone can see.
				Object.Instantiate(food, player.transform.position + player.transform.forward + Vector3.up, Quaternion.identity);
			}
			string name = food.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
			return Localization.instance != null ? Localization.instance.Localize(name) : name;
		}
		return null;
	}
}
