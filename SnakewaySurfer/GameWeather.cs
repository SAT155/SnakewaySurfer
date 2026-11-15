using System.Collections;
using System.Reflection;
using UnityEngine;

namespace SnakewaySurfer;

// Each biome's look from the game's own weather setup (EnvMan.m_biomes → its most likely
// environment's EnvSetup, daytime values): fog density and colour, ambient light, sun colour and
// strength. The lists are read by reflection so a renamed field only falls back to our values.
internal static class GameWeather
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

	public static void Apply(int index, string name, ref float fog, ref Color fogColor, ref Color ambient, ref Color sunColor, ref float sunIntensity, ref Color sky)
	{
		if (!Plugin.GameLighting.Value || EnvMan.instance == null || index < 0 || index >= s_order.Length)
		{
			return;
		}
		EnvSetup? env = MainEnv(s_order[index]);
		if (env == null)
		{
			Plugin.Log.LogInfo($"No game weather found for the {name}; using the minigame's own.");
			return;
		}
		fog = env.m_fogDensityDay;
		fogColor = env.m_fogColorDay;
		ambient = env.m_ambColorDay * Plugin.GameLightScale.Value;
		sunColor = env.m_sunColorDay;
		sunIntensity = env.m_lightIntensityDay * Plugin.GameLightScale.Value;
		// The far background fades into the fog, as the game's horizon does.
		sky = Color.Lerp(sky, env.m_fogColorDay, 0.7f);
		Plugin.Log.LogInfo($"{name} weather '{env.m_name}': fog {fog:0.000} {fogColor}, ambient {env.m_ambColorDay}, sun {sunColor} x{env.m_lightIntensityDay:0.00}");
	}

	// The biome's main weather, for its ambient sound (SurfAmbience.cs).
	public static EnvSetup? EnvOf(Heightmap.Biome biome)
	{
		try
		{
			return EnvMan.instance != null ? MainEnv(biome) : null;
		}
		catch (System.Exception)
		{
			return null;
		}
	}

	private static EnvSetup? MainEnv(Heightmap.Biome biome)
	{
		EnvMan man = EnvMan.instance;
		foreach (BiomeEnvSetup setup in man.m_biomes)
		{
			if (setup == null || setup.m_biome != biome)
			{
				continue;
			}
			IList? entries = Field(setup, "m_environments") as IList;
			if (entries == null)
			{
				return null;
			}
			object? best = null;
			float bestWeight = -1f;
			foreach (object entry in entries)
			{
				float w = Field(entry, "m_weight") is float f ? f : 1f;
				if (w > bestWeight)
				{
					best = entry;
					bestWeight = w;
				}
			}
			if (best == null)
			{
				return null;
			}
			if (Field(best, "m_env") is EnvSetup direct && direct != null)
			{
				return direct;
			}
			string envName = Field(best, "m_environment") as string ?? "";
			if (Field(man, "m_environments") is IList all)
			{
				foreach (object e in all)
				{
					if (e is EnvSetup es && es.m_name == envName)
					{
						return es;
					}
				}
			}
			return null;
		}
		return null;
	}

	private static object? Field(object? target, string name)
	{
		if (target == null)
		{
			return null;
		}
		FieldInfo? f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		return f?.GetValue(target);
	}
}
