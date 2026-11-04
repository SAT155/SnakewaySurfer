using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SnakewaySurfer;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
	public const string PluginGUID = "com.snakesquad.snakewaysurfer";
	public const string PluginName = "SnakewaySurfer";
	public const string PluginVersion = "1.0.0";

	private static Plugin instance = null!;
	internal static ManualLogSource Log = null!;

	internal static ConfigEntry<float> BiomeLength = null!;
	internal static ConfigEntry<float> StartSpeed = null!;
	internal static ConfigEntry<float> SpeedPerBiome = null!;
	internal static ConfigEntry<float> MaxSpeed = null!;
	internal static ConfigEntry<float> MonsterApproach = null!;
	internal static ConfigEntry<float> JumpHeight = null!;
	internal static ConfigEntry<float> SlideTime = null!;
	internal static ConfigEntry<float> LaneSwitchSpeed = null!;
	internal static ConfigEntry<float> Countdown = null!;
	internal static ConfigEntry<KeyCode> ExitKey = null!;

	internal static ConfigEntry<float> RunLevelsPerBiome = null!;
	internal static ConfigEntry<float> FoodCooldownMinutes = null!;
	internal static ConfigEntry<float> MinRunSecondsForFood = null!;

	internal static ConfigEntry<string> CircletItem = null!;
	internal static ConfigEntry<float> CameraDistance = null!;
	internal static ConfigEntry<float> CameraHeight = null!;
	internal static ConfigEntry<float> CameraFov = null!;
	internal static ConfigEntry<float> LightIntensity = null!;
	internal static ConfigEntry<float> MonsterHeight = null!;
	internal static ConfigEntry<float> SceneryDensity = null!;
	internal static ConfigEntry<string> TrackPiece = null!;
	internal static ConfigEntry<float> StageHeight = null!;
	internal static ConfigEntry<int> RenderLayer = null!;

	internal static ConfigEntry<bool> DebugLog = null!;

	private static ConfigEntry<string>[] s_monsters = null!;
	private static ConfigEntry<string>[] s_flyers = null!;
	private static ConfigEntry<string>[] s_scenery = null!;
	private static ConfigEntry<string>[] s_logs = null!;
	private static ConfigEntry<string>[] s_foods = null!;

	private void Awake()
	{
		instance = this;
		Log = Logger;

		BiomeLength = Config.Bind("1 - Run", "Biome Length", 500f,
			new ConfigDescription("Metres of track per biome. The run goes Meadows, Black Forest, Swamp, Mountains, Plains, Mistlands, Ashlands, Deep North, and Deep North never ends.",
				new AcceptableValueRange<float>(100f, 5000f)));
		StartSpeed = Config.Bind("1 - Run", "Start Speed", 9f,
			new ConfigDescription("Running speed at the start, in metres per second (a vanilla sprint is about 7).", new AcceptableValueRange<float>(3f, 30f)));
		SpeedPerBiome = Config.Bind("1 - Run", "Speed Per Biome", 1.5f,
			new ConfigDescription("How much faster you run for each biome you get through (it rises smoothly, not in steps).", new AcceptableValueRange<float>(0f, 10f)));
		MaxSpeed = Config.Bind("1 - Run", "Max Speed", 22f,
			new ConfigDescription("Running speed never goes above this.", new AcceptableValueRange<float>(5f, 50f)));
		MonsterApproach = Config.Bind("1 - Run", "Monster Charge Per Biome", 0.8f,
			new ConfigDescription("Some monsters run at you instead of standing still. Their speed is this times the biome number (Meadows 0, Black Forest 1, ... Deep North 7), so the Meadows ones always stand still.",
				new AcceptableValueRange<float>(0f, 5f)));
		JumpHeight = Config.Bind("1 - Run", "Jump Height", 1.6f,
			new ConfigDescription("How high a jump goes, in metres. Logs are 0.7 m high.", new AcceptableValueRange<float>(0.8f, 4f)));
		SlideTime = Config.Bind("1 - Run", "Slide Time", 0.8f,
			new ConfigDescription("How long a slide lasts, in seconds.", new AcceptableValueRange<float>(0.3f, 2f)));
		LaneSwitchSpeed = Config.Bind("1 - Run", "Lane Switch Speed", 14f,
			new ConfigDescription("How fast you move sideways between lanes, in metres per second (lanes are 2.6 m apart).", new AcceptableValueRange<float>(4f, 40f)));
		Countdown = Config.Bind("1 - Run", "Countdown", 3f,
			new ConfigDescription("Seconds of countdown before each run.", new AcceptableValueRange<float>(0f, 10f)));
		ExitKey = Config.Bind("1 - Run", "Exit Key", KeyCode.Escape,
			"Stops the minigame and puts you back in the world. Stopping mid-run still counts the run for rewards.");

		RunLevelsPerBiome = Config.Bind("2 - Rewards", "Run Levels Per Biome", 1f,
			new ConfigDescription("Run skill levels earned for each biome's length of track (fractions count, so half a biome gives half). It's levels at your current Run skill, so it slows down as your skill gets high, like vanilla. 0 turns XP off.",
				new AcceptableValueRange<float>(0f, 10f)));
		FoodCooldownMinutes = Config.Bind("2 - Rewards", "Food Cooldown Minutes", 30f,
			new ConfigDescription("Real-time minutes after a food reward before the next run can give food again (saved on your character, so relogging doesn't reset it). XP has no cooldown.",
				new AcceptableValueRange<float>(0f, 1440f)));
		MinRunSecondsForFood = Config.Bind("2 - Rewards", "Min Run Seconds For Food", 30f,
			new ConfigDescription("A run must last at least this many seconds to give food.", new AcceptableValueRange<float>(0f, 600f)));

		CircletItem = Config.Bind("3 - Look", "Circlet Item", "HelmetDverger",
			"Helmet item other players see on you while you surf (prefab name). HelmetDverger is the Dverger circlet. Empty: keep your own helmet.");
		CameraDistance = Config.Bind("3 - Look", "Camera Distance", 6.5f,
			new ConfigDescription("How far behind you the minigame camera sits.", new AcceptableValueRange<float>(2f, 20f)));
		CameraHeight = Config.Bind("3 - Look", "Camera Height", 3.4f,
			new ConfigDescription("How high above the track the minigame camera sits.", new AcceptableValueRange<float>(0.5f, 15f)));
		CameraFov = Config.Bind("3 - Look", "Camera Field Of View", 65f,
			new ConfigDescription("Minigame camera field of view in degrees.", new AcceptableValueRange<float>(30f, 110f)));
		LightIntensity = Config.Bind("3 - Look", "Light Intensity", 1.1f,
			new ConfigDescription("Brightness of the minigame's own sun.", new AcceptableValueRange<float>(0f, 4f)));
		MonsterHeight = Config.Bind("3 - Look", "Monster Height", 2.2f,
			new ConfigDescription("Monsters are resized to be at most this tall (and 2.2 m wide), so trolls and lox fit in a lane.", new AcceptableValueRange<float>(1f, 6f)));
		SceneryDensity = Config.Bind("3 - Look", "Scenery Density", 1f,
			new ConfigDescription("How many trees and rocks line the track. Lower it if the minigame runs slowly.", new AcceptableValueRange<float>(0.2f, 3f)));
		TrackPiece = Config.Bind("3 - Look", "Track Piece", "stone_floor_2x2",
			"Build piece whose material paves the track (prefab name, e.g. stone_floor_2x2, wood_floor).");
		StageHeight = Config.Bind("3 - Look", "Stage Height", 4000f,
			new ConfigDescription("How far above you the (invisible to everyone else) minigame track is built.", new AcceptableValueRange<float>(1000f, 9000f)));
		RenderLayer = Config.Bind("3 - Look", "Render Layer", 30,
			new ConfigDescription("Unity layer the minigame draws on. Only change it if another mod uses layer 30 and things show up wrong.", new AcceptableValueRange<int>(8, 31)));

		s_monsters = new ConfigEntry<string>[Biomes.Defaults.Length];
		s_flyers = new ConfigEntry<string>[Biomes.Defaults.Length];
		s_scenery = new ConfigEntry<string>[Biomes.Defaults.Length];
		s_logs = new ConfigEntry<string>[Biomes.Defaults.Length];
		s_foods = new ConfigEntry<string>[Biomes.Defaults.Length];
		for (int i = 0; i < Biomes.Defaults.Length; i++)
		{
			BiomeDef d = Biomes.Defaults[i];
			string section = $"4.{i + 1} - {d.Name}";
			s_monsters[i] = Config.Bind(section, "Monsters", d.Monsters,
				"Monsters standing (or charging) in the lanes; dodge them by changing lane. Comma-separated creature names as in the game's text keys (\"greydwarfbrute\" for $enemy_greydwarfbrute) or prefab names. Unknown names are skipped and logged.");
			s_flyers[i] = Config.Bind(section, "Flyers", d.Flyers,
				"Flying monsters at head height; slide under them. Same naming as Monsters. Empty: hanging logs only.");
			s_scenery[i] = Config.Bind(section, "Scenery", d.Scenery,
				"Trees and rocks beside the track (prefab names).");
			s_logs[i] = Config.Bind(section, "Logs", d.Logs,
				"Log prefabs used for things to jump over and hanging logs to slide under.");
			s_foods[i] = Config.Bind(section, "Foods", d.Foods,
				"Foods you can win when this is the furthest biome you reached; one is picked at random. Item names as in the game's text keys (\"carrotsoup\" for $item_carrotsoup) or prefab names.");
		}

		DebugLog = Config.Bind("9 - Debug", "Log Details", false,
			"Log which monsters, scenery and logs were found for each biome when a run starts.");

		new Harmony(PluginGUID).PatchAll(Assembly.GetExecutingAssembly());
	}

	internal static BiomeDef BiomeConfig(int i)
	{
		BiomeDef d = Biomes.Defaults[i];
		return new BiomeDef
		{
			Name = d.Name,
			Ground = d.Ground,
			Monsters = s_monsters[i].Value,
			Flyers = s_flyers[i].Value,
			Scenery = s_scenery[i].Value,
			Logs = s_logs[i].Value,
			Foods = s_foods[i].Value,
		};
	}

	// Terminal.InitTerminal fills the command table (and can run again on scene loads), so the
	// command is added after it each time, as ValheimMapMarker does.
	[HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
	private static class InitTerminalPatch
	{
		private static void Postfix()
		{
			new Terminal.ConsoleCommand("surfsup", "surfsup - kill some time: run the Snakeway through every biome, dodging monsters on 3 lanes", args =>
			{
				Player player = Player.m_localPlayer;
				if (player == null)
				{
					args.Context.AddString("Load into a world first.");
					return;
				}
				SurfGame.Begin(player, args.Context);
			});
		}
	}

	private void OnDestroy()
	{
		SurfGame.Stop();
	}
}
