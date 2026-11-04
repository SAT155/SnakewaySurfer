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

	// Gives Run XP for the distance and, when allowed, one food from the highest biome reached.
	// Returns the lines to show on the results screen.
	public static string Grant(Player player, float distance, int highestBiome, float seconds, List<BiomeDef> biomes)
	{
		List<string> lines = new();

		float levels = distance / Plugin.BiomeLength.Value * Plugin.RunLevelsPerBiome.Value;
		if (levels > 0.001f)
		{
			float gained = AddRunLevels(player, levels);
			if (gained > 0.001f)
			{
				lines.Add($"Run skill +{gained:0.00} levels");
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
				string? food = GiveFood(player, highestBiome, biomes);
				if (food != null)
				{
					player.m_customData[LastFoodKey] = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
					lines.Add($"Found some {food} along the way!");
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

	// Adds a number of levels' worth of Run XP (fractions carry), using the game's own skill
	// raise so level-up messages and effects play. Skills.RaiseSkill adds
	// m_increseStep * factor * Game.m_skillGainRate and levels up at most once per call, resetting
	// the accumulator, so it is fed one level at a time.
	private static float AddRunLevels(Player player, float levels)
	{
		Skills skills = player.m_skills;
		if (skills == null)
		{
			return 0f;
		}
		Skills.Skill skill = skills.GetSkill(Skills.SkillType.Run);
		float stepRate = skill.m_info.m_increseStep * Game.m_skillGainRate;
		if (stepRate <= 0f)
		{
			return 0f;
		}
		float before = skill.m_level + skill.m_accumulator / Mathf.Max(0.0001f, skill.GetNextLevelRequirement());
		float remaining = levels;
		for (int guard = 0; remaining > 0.0001f && skill.m_level < 100f && guard < 200; guard++)
		{
			float req = skill.GetNextLevelRequirement();
			if (req <= 0f)
			{
				break;
			}
			float left = Mathf.Max(0f, req - skill.m_accumulator);
			float want = remaining * req;
			if (want >= left)
			{
				skills.RaiseSkill(Skills.SkillType.Run, (left + 0.0001f) / stepRate);
				remaining -= left / req;
			}
			else
			{
				skills.RaiseSkill(Skills.SkillType.Run, want / stepRate);
				remaining = 0f;
			}
		}
		float after = skill.m_level + skill.m_accumulator / Mathf.Max(0.0001f, skill.GetNextLevelRequirement());
		return after - before;
	}

	private static string? GiveFood(Player player, int highestBiome, List<BiomeDef> biomes)
	{
		// Highest biome first; fall back to lower ones if none of its foods exist.
		for (int b = Mathf.Clamp(highestBiome, 0, biomes.Count - 1); b >= 0; b--)
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
