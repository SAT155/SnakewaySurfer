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
	public const string PluginVersion = "1.16.0";
	// Filled in by the build with its run number, so logs and the self-test name the real release.
	public static readonly int BuildNumber = 0;
	public static string BuildVersion => BuildNumber > 0 ? PluginVersion.Substring(0, PluginVersion.LastIndexOf('.') + 1) + BuildNumber : PluginVersion + " (local build)";

	private static Plugin instance = null!;
	internal static ManualLogSource Log = null!;

	internal static ConfigEntry<float> BiomeLength = null!;
	internal static ConfigEntry<float> StartSpeed = null!;
	internal static ConfigEntry<float> SpeedPerBiome = null!;
	internal static ConfigEntry<float> MaxSpeed = null!;
	internal static ConfigEntry<float> MonsterMove = null!;
	internal static ConfigEntry<bool> RangedAttacks = null!;
	internal static ConfigEntry<float> RoadRoll = null!;
	internal static ConfigEntry<bool> SwampWater = null!;
	internal static ConfigEntry<float> JumpHeight = null!;
	internal static ConfigEntry<float> SlideTime = null!;
	internal static ConfigEntry<float> LaneSwitchSpeed = null!;
	internal static ConfigEntry<float> Countdown = null!;
	internal static ConfigEntry<KeyCode> ExitKey = null!;
	internal static ConfigEntry<bool> StopWhenHurt = null!;
	internal static ConfigEntry<float> HurtThreshold = null!;
	internal static ConfigEntry<bool> Sounds = null!;
	internal static ConfigEntry<float> SoundVolume = null!;

	internal static ConfigEntry<float> RunLevelsPerBiome = null!;
	internal static ConfigEntry<float> MaxRunLevels = null!;
	internal static ConfigEntry<float> RunXpPerSecond = null!;
	internal static ConfigEntry<float> MaxRunXpSeconds = null!;
	internal static ConfigEntry<float> FoodCooldownMinutes = null!;
	internal static ConfigEntry<float> MinRunSecondsForFood = null!;

	internal static ConfigEntry<string> CircletItem = null!;
	internal static ConfigEntry<float> CameraDistance = null!;
	internal static ConfigEntry<float> CameraHeight = null!;
	internal static ConfigEntry<float> CameraFov = null!;
	internal static ConfigEntry<float> LightIntensity = null!;
	internal static ConfigEntry<float> MonsterHeight = null!;
	internal static ConfigEntry<float> SceneryDensity = null!;
	internal static ConfigEntry<bool> GameVegetation = null!;
	internal static ConfigEntry<bool> FairPlacement = null!;
	internal static ConfigEntry<bool> FairWarning = null!;
	internal static ConfigEntry<float> MistAmount = null!;
	internal static ConfigEntry<bool> MistStorm = null!;
	internal static ConfigEntry<bool> DungeonRoof = null!;
	internal static ConfigEntry<float> AmbienceVolume = null!;
	internal static ConfigEntry<string> TrackPiece = null!;
	internal static ConfigEntry<float> StageHeight = null!;
	internal static ConfigEntry<int> RenderLayer = null!;

	internal static ConfigEntry<bool> DebugLog = null!;
	internal static ConfigEntry<bool> Invincible = null!;
	internal static ConfigEntry<bool> Autopilot = null!;
	internal static ConfigEntry<bool> DrawnRaven = null!;
	internal static ConfigEntry<bool> AllowPause = null!;
	internal static ConfigEntry<bool> GameLighting = null!;
	internal static ConfigEntry<bool> UseGameCamera = null!;
	internal static ConfigEntry<bool> GameTerrainShader = null!;
	internal static ConfigEntry<bool> GameTerrainShape = null!;
	internal static ConfigEntry<bool> GameGrass = null!;
	internal static ConfigEntry<string> TrackStyle = null!;
	internal static ConfigEntry<float> GameLightScale = null!;
	internal static ConfigEntry<KeyCode> PauseKey = null!;
	internal static ConfigEntry<string> Swoopers = null!;
	internal static ConfigEntry<string> Ambushers = null!;
	internal static ConfigEntry<string> Smashers = null!;
	internal static ConfigEntry<string> CreatureSchemas = null!;
	internal static ConfigEntry<int> LoopBiomes = null!;
	internal static ConfigEntry<float> GameSpeed = null!;
	internal static ConfigEntry<bool> GameGroundTextures = null!;

	private static ConfigEntry<string>[] s_monsters = null!;
	private static ConfigEntry<string>[] s_flyers = null!;
	private static ConfigEntry<string>[] s_scenery = null!;
	private static ConfigEntry<string>[] s_logs = null!;
	private static ConfigEntry<string>[] s_foods = null!;
	private static ConfigEntry<string>[] s_giants = null!;
	private static ConfigEntry<string>[] s_locations = null!;
	private static ConfigEntry<string>[] s_landmarks = null!;
	private static ConfigEntry<string>[] s_pickables = null!;
	internal static ConfigEntry<bool> BossFights = null!;
	internal static ConfigEntry<int> Lives = null!;
	internal static ConfigEntry<int> Difficulty = null!;
	internal static readonly string[] DifficultyNames = { "Gentle", "Normal", "Hard" };
	// 1.16.94 (SAT: "Adjustable difficulty in the menu might be nice"): Gentle gives two extra
	// lives and a slower run and monsters; Hard one life fewer and a faster run.
	internal static int Level => Mathf.Clamp(Difficulty.Value, 0, 2);
	internal static int StartLives => Mathf.Max(1, Lives.Value + (Level == 0 ? 2 : Level == 2 ? -1 : 0));
	internal static float SpeedScale => Level == 0 ? 0.85f : Level == 2 ? 1.15f : 1f;
	internal static float FoeScale => Level == 0 ? 0.7f : Level == 2 ? 1.25f : 1f;
	internal static ConfigEntry<bool> IgnoreBossGate = null!;
	internal static ConfigEntry<bool> BossLandmarks = null!;
	internal static ConfigEntry<float> MonumentSize = null!;
	internal static ConfigEntry<bool> MonumentSky = null!;
	internal static ConfigEntry<bool> BossChase = null!;
	internal static ConfigEntry<bool> BossWeather = null!;
	internal static ConfigEntry<bool> BossSummon = null!;
	internal static ConfigEntry<float> GiantChance = null!;
	internal static ConfigEntry<float> FogStrength = null!;
	internal static ConfigEntry<bool> DistanceHaze = null!;
	internal static ConfigEntry<bool> WaveGuide = null!;
	internal static ConfigEntry<float> RollStamina = null!;
	internal static ConfigEntry<float> JumpStamina = null!;
	internal static ConfigEntry<KeyCode> SprintKey = null!;
	internal static ConfigEntry<KeyCode> BlockKey = null!;
	internal static ConfigEntry<KeyCode> ThrowKey = null!;
	internal static ConfigEntry<float> SprintSpeed = null!;
	internal static ConfigEntry<float> MaxStamina = null!;
	internal static ConfigEntry<float> SprintStamina = null!;
	internal static ConfigEntry<float> BlockStamina = null!;
	internal static ConfigEntry<float> BlockHoldStamina = null!;
	internal static ConfigEntry<float> BlockSpeed = null!;
	internal static ConfigEntry<bool> SkillXp = null!;
	internal static ConfigEntry<float> StaminaRegen = null!;
	internal static ConfigEntry<float> ParryWindow = null!;
	internal static ConfigEntry<bool> PopupText = null!;
	internal static ConfigEntry<bool> StartMenu = null!;
	internal static ConfigEntry<bool> ParryDuel = null!;
	internal static ConfigEntry<float> AmbientLight = null!;
	internal static ConfigEntry<float> HillHeight = null!;
	internal static ConfigEntry<float> TrackBrightness = null!;
	internal static ConfigEntry<bool> OceanCrossing = null!;
	internal static ConfigEntry<bool> MidCheckpoints = null!;
	internal static ConfigEntry<bool> CampBreak = null!;
	internal static ConfigEntry<bool> PortalPiece = null!;
	internal static ConfigEntry<float> OceanLength = null!;
	internal static ConfigEntry<float> SailSpeed = null!;
	internal static ConfigEntry<bool> SeaSerpent = null!;
	internal static ConfigEntry<bool> Dungeons = null!;
	internal static ConfigEntry<bool> Traders = null!;
	internal static ConfigEntry<bool> Places = null!;
	internal static ConfigEntry<bool> Lava = null!;
	internal static ConfigEntry<bool> Raven = null!;
	internal static ConfigEntry<bool> LoadingScreen = null!;
	internal static ConfigEntry<bool> DreamIntro = null!;
	internal static ConfigEntry<bool> RavenStory = null!;
	internal static ConfigEntry<float> PortalMinutes = null!;
	internal static ConfigEntry<float> RavenMinutes = null!;
	internal static ConfigEntry<float> MuninDays = null!;
	internal static ConfigEntry<bool> DreamEverySleep = null!;
	internal static ConfigEntry<float> ViewDistance = null!;

	private void Awake()
	{
		instance = this;
		Log = Logger;
		// (BepInEx's own line always says 1.16.0; this is the build actually loaded.)
		Log.LogInfo($"SnakewaySurfer build {BuildVersion}.");

		BiomeLength = Config.Bind("1 - Run", "Biome Length", 500f,
			new ConfigDescription("Metres of track per biome. The run goes Meadows, Black Forest, Swamp, Mountains, Plains, Mistlands, Ashlands, Deep North, and Deep North never ends.",
				new AcceptableValueRange<float>(100f, 5000f)));
		StartSpeed = Config.Bind("1 - Run", "Start Speed", 9f,
			new ConfigDescription("Running speed at the start, in metres per second (a vanilla sprint is about 7).", new AcceptableValueRange<float>(3f, 30f)));
		SpeedPerBiome = Config.Bind("1 - Run", "Speed Per Biome", 1.5f,
			new ConfigDescription("How much faster you run for each biome you get through (it rises smoothly, not in steps).", new AcceptableValueRange<float>(0f, 10f)));
		MaxSpeed = Config.Bind("1 - Run", "Max Speed", 22f,
			new ConfigDescription("Running speed never goes above this.", new AcceptableValueRange<float>(5f, 50f)));
		MonsterMove = Config.Bind("1 - Run", "Monster Movement", 1f,
			new ConfigDescription("Some monsters come at you instead of standing still: fast ones (wolves, deer, fenrings...) charge, slow ones walk, a little more in later biomes. This scales their speed; 0 keeps them all still.",
				new AcceptableValueRange<float>(0f, 3f)));
		RangedAttacks = Config.Bind("1 - Run", "Ranged Attacks", true,
			"Monsters with a ranged attack (archers, stone-throwing greydwarfs, mages...) shoot down the track toward your lane. Slide under the shot or change lane.");
		RoadRoll = Config.Bind("3 - Look", "Road Roll", 1f,
			new ConfigDescription("How much the road rolls up and down; it gets stronger with every biome. 0 makes it flat.", new AcceptableValueRange<float>(0f, 3f)));
		SwampWater = Config.Bind("3 - Look", "Swamp Water", true,
			"In the Swamp you run through shallow water.");
		JumpHeight = Config.Bind("1 - Run", "Jump Height", 1.6f,
			new ConfigDescription("How high a jump goes, in metres. Logs are 0.7 m high.", new AcceptableValueRange<float>(0.8f, 4f)));
		SlideTime = Config.Bind("1 - Run", "Slide Time", 0.8f,
			new ConfigDescription("How long a slide lasts, in seconds.", new AcceptableValueRange<float>(0.3f, 2f)));
		LaneSwitchSpeed = Config.Bind("1 - Run", "Lane Switch Speed", 14f,
			new ConfigDescription("How fast you move sideways between lanes, in metres per second (lanes are 2.6 m apart).", new AcceptableValueRange<float>(4f, 40f)));
		Countdown = Config.Bind("1 - Run", "Countdown", 3f,
			new ConfigDescription("Seconds of countdown before each run.", new AcceptableValueRange<float>(0f, 10f)));
		ExitKey = Config.Bind("1 - Run", "Exit Key", KeyCode.Escape,
			"Mid-run: pauses (press again to carry on; Q while paused stops the run). On the start menu and after a fall: puts you back in the world. Stopping mid-run still counts the run for rewards.");

		StopWhenHurt = Config.Bind("1 - Run", "Stop When Hurt", true,
			"Stop the minigame as soon as something damages your character in the real world. Health lost because food is wearing off doesn't count.");
		HurtThreshold = Config.Bind("1 - Run", "Hurt Threshold", 1f,
			new ConfigDescription("Smallest health drop (in one frame) that counts as being hurt.", new AcceptableValueRange<float>(0.1f, 50f)));
		Sounds = Config.Bind("3 - Look", "Sounds", true,
			"Play sounds for the countdown, jumps, slides, lane changes, coins, new biomes and crashes. Only you hear them.");
		SoundVolume = Config.Bind("3 - Look", "Sound Volume", 0.8f,
			new ConfigDescription("Volume of the minigame sounds.", new AcceptableValueRange<float>(0f, 1f)));

		// Before 1.14 XP was given in levels, which is a lot of XP at a high skill (SAT). These two
		// stay only so old config files load; they do nothing now.
		RunLevelsPerBiome = Config.Bind("2 - Rewards", "Run Levels Per Biome", 0.08f,
			new ConfigDescription("Not used since 1.14 (see Run XP Per Second).", new AcceptableValueRange<float>(0f, 10f)));
		MaxRunLevels = Config.Bind("2 - Rewards", "Max Run Levels Per Run", 0.75f,
			new ConfigDescription("Not used since 1.14 (see Max Run XP Seconds).", new AcceptableValueRange<float>(0f, 10f)));
		RunXpPerSecond = Config.Bind("2 - Rewards", "Run XP Per Second", 0.5f,
			new ConfigDescription("Run skill XP for each second of the run, as real XP (the game's own skill gain, so it takes longer to level as your skill gets high). 1 = the same as running in the world for that long. 0 turns XP off.",
				new AcceptableValueRange<float>(0f, 5f)));
		MaxRunXpSeconds = Config.Bind("2 - Rewards", "Max Run XP Seconds", 300f,
			new ConfigDescription("Seconds of a run that count for XP at most (longer runs give no more). 0 = no limit.",
				new AcceptableValueRange<float>(0f, 3600f)));
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
		FogStrength = Config.Bind("3 - Look", "Fog Strength", 1f,
			new ConfigDescription("How thick the minigame's fog is (each biome has its own; the Swamp and Mistlands are thickest). 0 turns it off.", new AcceptableValueRange<float>(0f, 4f)));
		WaveGuide = Config.Bind("3 - Look", "Wave Guide", false,
			"At sea, a band through the ship while a rolling wave comes at her side on (waves no longer do damage, so it's off by default).");
		DistanceHaze = Config.Bind("3 - Look", "Distance Haze", true,
			"Thin sheets of mist across the far end of the view, so the end of the track fades away even where the game's own fog doesn't reach the minigame.");
		AmbientLight = Config.Bind("3 - Look", "Ambient Light", 0.45f,
			new ConfigDescription("Brightness of the light that comes from the sky colour everywhere (shadows get darker as it goes down).", new AcceptableValueRange<float>(0f, 2f)));
		HillHeight = Config.Bind("3 - Look", "Hill Height", 1f,
			new ConfigDescription("How tall the hills beside the track are (each biome has its own; Mountains are highest). 0 makes the land flat.", new AcceptableValueRange<float>(0f, 3f)));
		TrackBrightness = Config.Bind("3 - Look", "Track Brightness", 0.6f,
			new ConfigDescription("Darkens the paved track (1 = the build piece's own colour, which looked washed out).", new AcceptableValueRange<float>(0.1f, 1.5f)));
		GiantChance = Config.Bind("1 - Run", "Giant Chance", 0.22f,
			new ConfigDescription("Chance that a row of obstacles is a troll set piece instead, in biomes that have Giants (the Black Forest by default).", new AcceptableValueRange<float>(0f, 1f)));
		LightIntensity = Config.Bind("3 - Look", "Light Intensity", 0.45f,
			new ConfigDescription("Brightness of the minigame's own sun.", new AcceptableValueRange<float>(0f, 4f)));
		MonsterHeight = Config.Bind("3 - Look", "Monster Height", 9f,
			new ConfigDescription("Monsters in the lanes are their real size, but anything taller than this (or much wider than a lane) is shrunk to fit.", new AcceptableValueRange<float>(1f, 20f)));
		SceneryDensity = Config.Bind("3 - Look", "Scenery Density", 1f,
			new ConfigDescription("How many trees and rocks line the track. Lower it if the minigame runs slowly.", new AcceptableValueRange<float>(0.2f, 3f)));
		MistAmount = Config.Bind("3 - Look", "Mistlands Mist", 1f,
			new ConfigDescription("How much of the game's own mist drifts over the Mistlands (thick beside the track, thin over the lanes). 0 turns it off.", new AcceptableValueRange<float>(0f, 2f)));
		DungeonRoof = Config.Bind("3 - Look", "Dungeon Roof", true,
			"Burial chambers, sunken crypts, frost caves and infested mines have a roof, and the camera stays under it. Off = open to the dark above, with the camera as high as outside.");
		MistStorm = Config.Bind("3 - Look", "Mistlands Storm", true,
			"The Mistlands' own thunderstorm hangs over the open Mistlands stretches: clouds that flicker white and violet, with thunder after.");
		AmbienceVolume = Config.Bind("4 - Sound", "Biome Ambience", 1f,
			new ConfigDescription("How loud each biome's own background sound is (its wind and weather, its birds and night noises, and distant creatures such as a Gjall in the Mistlands). 0 turns it off.", new AcceptableValueRange<float>(0f, 2f)));
		FairPlacement = Config.Bind("1 - Run", "Fair Placement", true,
			"Every row of creatures and obstacles is checked before you reach it: if no runner could get through it with the jumps, rolls and lane changes the game allows, the piece that closes the way is taken out. Off = rows are left exactly as they were laid out.");
		FairWarning = Config.Bind("1 - Run", "Fair Warning", true,
			"An attack can only hurt you if it was announced: a lane marker on your lane, or the attack itself in view, for at least 0.4 seconds. Anything quicker than that misses. Off = attacks hit whether announced or not.");
		GameVegetation = Config.Bind("3 - Look", "Game Vegetation", true,
			"Line the track with what the game itself grows in each biome (its trees, bushes, rocks, cliffs and roots, in the game's own proportions), instead of each biome's short Scenery list.");
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
		s_giants = new ConfigEntry<string>[Biomes.Defaults.Length];
		s_locations = new ConfigEntry<string>[Biomes.Defaults.Length];
		s_landmarks = new ConfigEntry<string>[Biomes.Defaults.Length];
		s_pickables = new ConfigEntry<string>[Biomes.Defaults.Length];
		for (int i = 0; i < Biomes.Defaults.Length; i++)
		{
			BiomeDef d = Biomes.Defaults[i];
			string section = $"4.{i + 1} - {d.Name}";
			s_monsters[i] = Config.Bind(section, "Monsters", d.Monsters,
				"Monsters in the lanes. \"auto\" uses the game's own spawn tables for this biome (the creatures that really live here, as common as they really are, in their group sizes). Or a comma-separated list of creature names as in the game's text keys (\"greydwarfbrute\" for $enemy_greydwarfbrute) or prefab names, each optionally with :weight and :group size (\"greydwarf:6:3\"). Unknown names are skipped and logged.");
			s_flyers[i] = Config.Bind(section, "Flyers", d.Flyers,
				"Flying monsters at head height; slide under them. \"auto\": the flying creatures from this biome's spawn tables. Same naming as Monsters. Empty: hanging logs only.");
			s_scenery[i] = Config.Bind(section, "Scenery", d.Scenery,
				"Trees and rocks beside the track (prefab names).");
			s_logs[i] = Config.Bind(section, "Logs", d.Logs,
				"Log prefabs used for things to jump over and hanging logs to slide under.");
			s_giants[i] = Config.Bind(section, "Giants", d.Giants,
				"Big monsters used for set pieces: one beside the track slams a log down across the outer lane as you run past, or one jumps down straddling the track so you must slide between its legs. Same naming as Monsters. Empty: no set pieces in this biome.");
			s_locations[i] = Config.Bind(section, "Location Monsters", d.Locations,
				"With Monsters on \"auto\": creatures that live at places in this biome (troll caves, crypts, villages) instead of the world spawn tables, added with these weights. Same naming as Monsters.");
			s_landmarks[i] = Config.Bind(section, "Landmarks", d.Landmarks,
				"Creature homes put beside the track now and then near those creatures, as \"Prefab: home, home; Prefab*: home\" (creature prefab names; a trailing * matches a name start; homes are prefab names such as Spawner_GreydwarfNest or goblin_totempole).");
			s_pickables[i] = Config.Bind(section, "Pickables", d.Pickables,
				"Plants, mushrooms, stones and bushes along the track edge (prefab names). Empty: none.");
			s_foods[i] = Config.Bind(section, "Foods", d.Foods,
				"Foods you can win when this is the furthest biome you reached; one is picked at random. Item names as in the game's text keys (\"carrotsoup\" for $item_carrotsoup) or prefab names.");
		}

		// Defaults changed in a release: overwrite an old default once (a value someone set
		// themselves stays).
		ConfigEntry<int> balance = Config.Bind("9 - Debug", "Balance Version", 0,
			"Internal: which release's default changes have been applied to this file. Leave it alone.");
		if (balance.Value < 1)
		{
			if (Mathf.Approximately(RunLevelsPerBiome.Value, 1f))
			{
				RunLevelsPerBiome.Value = 0.25f;
			}
			balance.Value = 1;
		}
		if (balance.Value < 2)
		{
			// 1.4.9: monsters keep their real size instead of all being squeezed to 2.2 m.
			if (Mathf.Approximately(MonsterHeight.Value, 2.2f))
			{
				MonsterHeight.Value = 4.2f;
			}
			balance.Value = 2;
		}
		if (balance.Value < 3)
		{
			// 1.5: creatures come from the game's spawn tables, and big ones keep their size.
			for (int i = 0; i < Biomes.Defaults.Length; i++)
			{
				if (s_monsters[i].Value == Biomes.Defaults[i].FallbackMonsters)
				{
					s_monsters[i].Value = "auto";
				}
				if (s_flyers[i].Value == Biomes.Defaults[i].FallbackFlyers)
				{
					s_flyers[i].Value = "auto";
				}
			}
			if (s_giants[2].Value == "")
			{
				s_giants[2].Value = "abomination";
			}
			if (MonsterHeight.Value <= 4.2f)
			{
				MonsterHeight.Value = 9f;
			}
			balance.Value = 3;
		}
		if (balance.Value < 4)
		{
			// 1.12: SAT found Run XP still too high at 0.25 levels per biome.
			if (Mathf.Approximately(RunLevelsPerBiome.Value, 0.25f))
			{
				RunLevelsPerBiome.Value = 0.08f;
			}
			balance.Value = 4;
		}
		if (balance.Value < 5)
		{
			// 1.14 (SAT): greydwarf nests are Black Forest things, not Meadows ones.
			if (s_landmarks[0].Value == "Greyling: Spawner_GreydwarfNest")
			{
				s_landmarks[0].Value = Biomes.Defaults[0].Landmarks;
			}
			balance.Value = 5;
		}

		DebugLog = Config.Bind("9 - Debug", "Log Details", false,
			"Log which monsters, scenery and logs were found for each biome when a run starts.");
		Invincible = Config.Bind("9 - Debug", "Invincible", false,
			"Testing: run through everything instead of crashing (a banner says what would have got you).");
		Autopilot = Config.Bind("9 - Debug", "Autopilot", false,
			"Testing: the run plays itself (lanes, jumps, rolls, dungeon corners and digging; no blocking, bosses or sailing). The surftest command turns this on by itself.");
		DrawnRaven = Config.Bind("3 - Look", "Drawn Raven Picture", false,
			"Show the ravens as the drawn picture that ships with the mod instead of a picture taken from their model in your game. (The drawn one is also used whenever the other can't be made.)");
		GameTerrainShader = Config.Bind("3 - Look", "Game Terrain Shader", false,
			"Testing: draw the land with the game's own terrain shader. It doesn't draw on the minigame's stage (the ground shows the sky), so by default the land uses the game's ground textures on a plain shader (Game Ground Textures).");
		GameTerrainShape = Config.Bind("3 - Look", "Game Terrain Shape", true,
			"Shape the land beside the track like this world's real ground: each biome borrows a ring of terrain from a spot in your world where that biome is (the game's own height formulas, with its rivers, Mistlands cliffs and mountain slopes), plus the game's paint mask for the ground textures. Off: the minigame's own rolling hills.");
		GameGrass = Config.Bind("3 - Look", "Game Grass", true,
			"Grow the game's own grass and ground clutter beside the track, by the game's rules for each biome (and its grass quality setting).");
		TrackStyle = Config.Bind("3 - Look", "Track Style", "dirt",
			"\"dirt\" (the game's dirt ground), \"paved\" (its paved road), \"stone\" (the Track Piece's stone floor), or \"hoed\" / \"hoed paved\" (drawn by the game's terrain shader like a hoed path; it can look very dark).");
		UseGameCamera = Config.Bind("3 - Look", "Use Game Camera", true,
			"Draw the minigame through the game's own camera, with its post effects (bloom, colour grading, ambient occlusion) and sky. Off: the minigame's own plain camera.");
		GameLighting = Config.Bind("3 - Look", "Game Lighting", true,
			"Light and fog each biome the way the game's own daytime weather for that biome does (fog density and colour, ambient light, sun). Off: the minigame's own simpler values (Fog Strength, Ambient Light and Light Intensity).");
		GameLightScale = Config.Bind("3 - Look", "Game Light Scale", 0.8f,
			new ConfigDescription("With Game Lighting: scales the sun and ambient light (the game's shaders and post effects differ from the minigame's).", new AcceptableValueRange<float>(0.1f, 3f)));
		AllowPause = Config.Bind("2 - Keys", "Allow Pause", true,
			"The Pause Key freezes the minigame: the run, creatures, effects and sounds. It also pauses by itself while the game's menu (Esc) is open.");
		PauseKey = Config.Bind("2 - Keys", "Pause Key", KeyCode.P,
			"Pauses and resumes the minigame.");
		SprintKey = Config.Bind("2 - Keys", "Sprint Key", KeyCode.LeftShift,
			"Hold to sprint: faster, so you can get past an attack before it lands. Uses stamina.");
		BlockKey = Config.Bind("2 - Keys", "Block Key", KeyCode.Mouse1,
			"Hold to block a creature's attack with your shield (holding it drains stamina, each blocked hit costs more, and you slow down, which helps to pick a way through). Press it just before the hit lands to parry: no stamina, and the attacker staggers. Logs, gas and tar can't be blocked, and a bat in the face knocks your guard down for a moment.");
		ThrowKey = Config.Bind("2 - Keys", "Throw Key", KeyCode.Mouse0,
			"Throws a spear you picked up at the boss (Eikthyr): it flies down your lane, so throw when he's lined up with you.");
		SprintSpeed = Config.Bind("1 - Run", "Sprint Speed", 1.35f,
			new ConfigDescription("How much faster you run while sprinting.", new AcceptableValueRange<float>(1f, 2f)));
		MaxStamina = Config.Bind("1 - Run", "Stamina", 100f,
			new ConfigDescription("Stamina for jumping, rolling, sprinting and blocking. It recharges after a second of not using it.", new AcceptableValueRange<float>(10f, 500f)));
		JumpStamina = Config.Bind("1 - Run", "Jump Stamina", 8f,
			new ConfigDescription("Stamina used by each jump, as in the game. Without enough you can't jump. 0: jumps are free.", new AcceptableValueRange<float>(0f, 100f)));
		SprintStamina = Config.Bind("1 - Run", "Sprint Stamina Per Second", 18f,
			new ConfigDescription("Stamina used each second of sprinting.", new AcceptableValueRange<float>(0f, 100f)));
		RollStamina = Config.Bind("1 - Run", "Roll Stamina", 12f,
			new ConfigDescription("Stamina used by each roll (and each dive from the air). Without enough you can't roll.", new AcceptableValueRange<float>(0f, 100f)));
		BlockStamina = Config.Bind("1 - Run", "Block Stamina", 30f,
			new ConfigDescription("Stamina used by each blocked hit (a parry is free). Without enough, the guard breaks and the hit gets through.", new AcceptableValueRange<float>(0f, 200f)));
		BlockHoldStamina = Config.Bind("1 - Run", "Block Hold Stamina Per Second", 8f,
			new ConfigDescription("Stamina used each second the block is held. At none left the guard stays up, but blocked hits break through it (a parry still works).", new AcceptableValueRange<float>(0f, 100f)));
		BlockSpeed = Config.Bind("1 - Run", "Block Speed", 0.75f,
			new ConfigDescription("How fast you run behind your shield, from your normal speed (it eases in and out).", new AcceptableValueRange<float>(0.3f, 1f)));
		SkillXp = Config.Bind("2 - Rewards", "Block And Dodge XP", true,
			"Blocking and Dodge skill XP as the game gives it: 1 for each blocked hit, 2 for each parry (Blocking), and 1 for each attack you roll under, jump or step out of the way of (Dodge). Given with the Run XP when the run ends.");
		StaminaRegen = Config.Bind("1 - Run", "Stamina Regen Per Second", 22f,
			new ConfigDescription("Stamina recovered each second once you stop jumping, rolling, sprinting and blocking for a second.", new AcceptableValueRange<float>(1f, 200f)));
		ParryWindow = Config.Bind("1 - Run", "Parry Window", 0.3f,
			new ConfigDescription("Seconds before a hit in which pressing block parries it.", new AcceptableValueRange<float>(0.05f, 1f)));
		ParryDuel = Config.Bind("1 - Run", "Parry Duel", true,
			"Bosses (not Eikthyr or Moder) are hurt by parrying: grab the weapon on the track and the boss closes in and swings at your lane up to three times; parry a swing and your blow lands. Off: grabbing the weapon hurts the boss straight away.");
		StartMenu = Config.Bind("2 - Keys", "Start Menu", true,
			"surfsup (with nothing after it) opens a grid of the biomes and star laps you've reached, to pick where to start. Off: it starts at your latest checkpoint.");
		PopupText = Config.Bind("3 - Look", "Popup Text", false,
			"Show text pop-ups during the run (\"Entering ...\", boss messages, JUMP!/SLIDE!). Off: the run speaks through sounds, flashes and the HUD instead.");
		Raven = Config.Bind("3 - Look", "Hugin Messages", true,
			"The run's messages (entering a biome, a boss's items, lives left, a boss beaten) come from Hugin, the tutorial raven: he flies in at the top right of the screen, says it in the game's own speech popup, and flies off. Off: no messages, unless Popup Text is on.");
		LoadingScreen = Config.Bind("3 - Look", "Portal Loading Screen", true,
			"Runs load behind the game's own portal loading screen (the swirl you see when you teleport), fading in when the run is ready. Off: the run just starts.");
		ViewDistance = Config.Bind("3 - Look", "View Distance", 230f,
			"How far ahead (metres) the track, trees and places are built, 100 to 400. Further shows more but costs frame rate; the biome's fog still hides the far end.");
		DreamIntro = Config.Bind("3 - Look", "First-Run Dream", true,
			"The very first run (per character) starts with a short dream, in the game's own dream text, instead of the start menu. Space wakes you early. \"surfprogress dream\" sees it again.");
		RavenStory = Config.Bind("4 - Ravens", "Raven Story", true,
			"The way into the dream run: a strange portal turns up near you; step through and Hugin starts nagging you to go to sleep (more often and ruder the longer you stay up; after a day his brother Munin joins in and they talk about you). Sleep, and you dream the run. Off: no portal and no ravens (surfsup still works).");
		PortalMinutes = Config.Bind("4 - Ravens", "Portal After Minutes", 5f,
			new ConfigDescription("Minutes of play (per session) before the strange portal appears near you, until you've stepped through it once.", new AcceptableValueRange<float>(0f, 120f)));
		RavenMinutes = Config.Bind("4 - Ravens", "Raven Chat Minutes", 4f,
			new ConfigDescription("About how many minutes between the ravens' remarks (Hugin comes back sooner the longer you stay awake).", new AcceptableValueRange<float>(0.5f, 60f)));
		MuninDays = Config.Bind("4 - Ravens", "Munin After Days", 1f,
			new ConfigDescription("In-game days awake before Munin joins Hugin.", new AcceptableValueRange<float>(0.05f, 20f)));
		DreamEverySleep = Config.Bind("4 - Ravens", "Dream Every Sleep", true,
			"After the first time, every sleep dreams the run too. Off: only the first sleep after the ravens' nagging does.");
		Swoopers = Config.Bind("1 - Run", "Swoopers", "deathsquito",
			"Flying creatures that wait beside the track and dive across the lanes at head height as you come (slide under them).");
		Ambushers = Config.Bind("1 - Run", "Ambushers", "abomination",
			"Creatures that lie hidden and burst up as you get close (in the lanes, and as the Swamp's straddling giant).");
		Smashers = Config.Bind("1 - Run", "Smashers", "greydwarfbrute:1, goblinbrute:3, seekerbrute:1, stonegolem:1",
			"Brutes that smash the ground: each blow sends a shockwave down all three lanes (jump it). The number is how many blows in a row (the goblin brute's berserk smash hits three times).");
		CreatureSchemas = Config.Bind("1 - Run", "Creature Schemas", Schemas.Defaults,
			"What each creature does, as \"Prefab: tags; Prefab: tags\" (a trailing * matches a name start). Tags: charger, waiter, switcher, swarm, herd, line, crosser, shooter, flyshooter, sweep, gas2, gas3, smash, slams, lob, fire, tar, bomb, tickdrop, leaper, headleap, hurdle, stomper, ambush, chaser, behind, helper, talk, sideline. Creatures not listed behave as before.");
		if (balance.Value < 6)
		{
			// 1.16.1 (SAT's test): draugr archers on the swamp's sidelines, leeches weaving and
			// swimming up from behind, wolves low enough to jump.
			CreatureSchemas.Value = Schemas.Upgrade(CreatureSchemas.Value);
			balance.Value = 6;
		}
		if (balance.Value < 7)
		{
			// 1.16.6: the frost cave's cultists breathe fire down the lanes.
			CreatureSchemas.Value = Schemas.Upgrade(CreatureSchemas.Value);
			balance.Value = 7;
		}
		if (balance.Value < 8)
		{
			// 1.16.9: charred archers keep to the sides and shoot; their mages throw fire.
			CreatureSchemas.Value = Schemas.Upgrade(CreatureSchemas.Value);
			balance.Value = 8;
		}
		LoopBiomes = Config.Bind("1 - Run", "Loop Biomes", 6,
			new ConfigDescription("How many biomes one lap of the run has (6 = Meadows to the Mistlands). After the last one the run goes back to the Meadows with 1-star creatures, then 2, then 3. 0: no loop, the run goes through every biome and stays in the last.", new AcceptableValueRange<int>(0, 8)));
		BossFights = Config.Bind("1 - Run", "Boss Fights", true,
			"Each biome ends with its boss once you've beaten that boss in this world: some coins become its summoning item (the game's own amount, e.g. 2 deer trophies for Eikthyr), you stay in the biome until you have them all, then you face the boss. Beating it saves a checkpoint for the next biome (\"surfsup\" starts at your latest one; \"surfsup swamp\" or \"surfsup 3 1\" picks one). Off: no items or bosses, the run goes straight on.");
		Lives = Config.Bind("1 - Run", "Lives", 3,
			new ConfigDescription("Hits you can take before the run is over (three strikes and you're out). 1: the first hit ends it.", new AcceptableValueRange<int>(1, 9)));
		Difficulty = Config.Bind("1 - Run", "Difficulty", 1,
			new ConfigDescription("0 Gentle (two extra lives, slower run and monsters), 1 Normal, 2 Hard (one life fewer, faster run and monsters). Also picked at the bottom of the run menu (click it or press Tab).", new AcceptableValueRange<int>(0, 2)));
		BossLandmarks = Config.Bind("1 - Run", "Boss Landmarks", true,
			"Boss arenas show the boss's own place from the game (Eikthyr's altar, the Elder's stones, Moder's peak...) looming far ahead, with the land rising beside a flatter path and torches lining it. Off: arenas only take on the boss's colour.");
		MonumentSize = Config.Bind("1 - Run", "Boss Landmark Size", 2f,
			"How big the boss's place looms on the skyline ahead of its arena, 1 to 3. 1: as before 1.16.21. 2 (default): extra large, further off and towering over the land, with the air clearing a little more so it shows.");
		MonumentSky = Config.Bind("1 - Run", "Boss Landmark On Sky", true,
			"The boss's place is drawn once into a flat picture that stands far off on the skyline like part of the sky (it can't reach into the track). Off: the place itself as a 3D model, nearer.");
		BossChase = Config.Bind("1 - Run", "Boss Chase", true,
			"Eikthyr chases you from behind: the camera swings round to face him, and his attacks come up the lanes behind you (red lanes mark where they will land). Left and right follow the screen while it faces back. The other bosses wait ahead. Off: Eikthyr stays ahead too.");
		BossWeather = Config.Bind("3 - Look", "Boss Weather", true,
			"A boss arena takes on the weather of the boss's own place in the game: Eikthyr's thunderstorm with lightning and thunder, the Elder's forest mist, Bonemass's rain, Moder's snowstorm...");
		BossSummon = Config.Bind("1 - Run", "Boss Summoning", true,
			"A boss is summoned as in the game: its summoning items float up, the altar's sparkles and sounds play, and then the boss forms. Off: it just turns up.");
		IgnoreBossGate = Config.Bind("9 - Debug", "Ignore Boss Gate", false,
			"Testing: the run normally only reaches as far as the furthest boss beaten in this world (the Meadows only, until Eikthyr is dead). On: every biome and boss is open, and surfsup can start anywhere.");
		GameSpeed = Config.Bind("9 - Debug", "Game Speed", 1f,
			new ConfigDescription("Testing: speeds the whole minigame up (2 = twice as fast), to reach later biomes quickly. Distance and rewards count as normal.", new AcceptableValueRange<float>(0.25f, 8f)));
		GameGroundTextures = Config.Bind("3 - Look", "Game Ground Textures", true,
			"Use the game's own terrain textures for the land beside the track when they can be found (else a generated texture tinted per biome).");
		PortalPiece = Config.Bind("4 - Ravens", "Strange Portal Piece", true,
			"A Strange Portal to build with the hammer (Misc tab, 10 ectoplasm and 10 feathers, no workbench needed). Use it to choose where it leads: one of your checkpoints (walk in and run from there) or the dream (the ravens' strange portal: sleep to dream the run). Needs a restart to change.");
		CampBreak = Config.Bind("1 - Run", "Campfire Break", true,
			"After each biome boss (and after the serpent of the deep, which you harpoon and tow ashore) you make camp: a fire, food and mead, more of a camp at higher stars, your lives restored and any crafting drop the boss left. Then you choose to keep dreaming or open your eyes (back to the game, progress saved). Off: the run just goes on.");
		MidCheckpoints = Config.Bind("1 - Run", "Mid-Biome Checkpoints", true,
			"Halfway through each biome a ward stone lights up as you pass, and another once you hold all of the boss's summoning items; at sea, mooring at an island counts. Only the latest one is kept. After a fall there, C on the crash screen starts again from it. Off: only the checkpoints after bosses.");
		OceanCrossing = Config.Bind("1 - Run", "Ocean Crossing", true,
			"Between the Mountains and the Plains the run crosses the ocean: the track runs down a beach to a longship and the camera rises above it. A / D steer, W / S set the sail (oars, half, full), the Throw Key fires the bow ballista and the Block Key blocks and parries. The wind never stops and turns now and then: as in the game a sail is fastest with the wind from the side and useless heading into it (tack across it). In the middle a storm brings rolling waves (meet them bow or stern first, never side on), lightning and a sea serpent. Beating Moder saves a checkpoint at the crossing; making it opens the Plains in the start menu. Off: the Plains follow the Mountains as before.");
		OceanLength = Config.Bind("1 - Run", "Ocean Length", 900f,
			new ConfigDescription("Metres of sea before the serpent rises in the storm; the far shore is about a third as far again after it.", new AcceptableValueRange<float>(300f, 4000f)));
		SailSpeed = Config.Bind("1 - Run", "Sail Speed", 16f,
			new ConfigDescription("The longship's speed (m/s) under full sail with the wind from the side; half sail is about 60% of it, the oars a steady 6.", new AcceptableValueRange<float>(6f, 40f)));
		SeaSerpent = Config.Bind("1 - Run", "Sea Serpent", true,
			"A sea serpent rises in the storm and must be beaten with the ballista to reach the far shore. Off: the storm just passes.");

		Dungeons = Config.Bind("1 - Run", "Dungeons", true,
			"Once in each stretch of the Black Forest the track runs into a burial chamber, once in each stretch of the Swamp into a sunken crypt, once in each stretch of the Mountains into a frost cave, and once in each stretch of the Mistlands into an infested mine: dark halls seen from above, lit by the torch you carry. The halls turn 90 degrees: press A or D toward the opening as you reach a corner (a torch burns on the wall ahead and the camera starts to swing round), or run into the wall. Burial chambers: skeletons, bone piles that raise more as you come, ghosts drifting in through the walls. Sunken crypts: draugr and blobs, flooded halls with leeches (slower going), mud piles to jump, grave mounds that raise draugr, and iron gates that crash down as you come (roll under them). Frost caves: rough rock lit by glowing crystals, ulvs, fenrings, cultists breathing fire, bats, and icicles that crack loose over your lane as you come (jump the spike or move). Infested mines: black marble halls with dvergr lanterns, seekers, egg sacs that burst, and ticks that drop from the roof onto your lane (a red mark as one lets go). The Black Forest's burial chambers take turns with troll caves: mossy rock and blue mushrooms, greydwarfs, one troll waiting in the longest hall, and rocks falling from the roof as it roars. Charred fortresses (Ashlands, when Loop Biomes reaches it): grausten halls lit by lava lanterns, charred warriors, archers and mages, spawners that raise more, spiked beams, flametal gates that drop (roll under) and channels of lava across the halls (jump them). Old chests are worth ten coins. Off: those biomes run on as before.");
		Traders = Config.Bind("1 - Run", "Traders", true,
			"Now and then one of the game's traders stands by the track in front of their camp: Haldor in the Black Forest, the Bog Witch in the Swamp, and Hildir in the Meadows from the 1-star lap on. Coins and a gold mark lead along the lane on their side; run past them in that lane and they give you your lives back, or one extra if you have them all. Off: no traders.");
		Places = Config.Bind("1 - Run", "Places", true,
			"Each biome's own places stand beside the track now and then, as in the world: abandoned houses, stone circles and ruined towers in the Meadows, tower ruins and greydwarf camps in the Black Forest, draugr huts and sunken ruins in the Swamp, graves, drake nests and towers in the Mountains, standing stones, tar pits and fuling camps in the Plains, giant skeletons, dvergr towers and swords in stone in the Mistlands, charred ruins and morgen holes in the Ashlands. Some have one of their own waiting in the lane by them. Off: only the Swamp's crypt entrances and fire spouts.");
		Lava = Config.Bind("1 - Run", "Lava", true,
			"In the open Ashlands some rows are lava instead: a glowing channel across the track (jump it) or a pool along one or two lanes (keep out of it), with glowing cracks in the ash beside the track. Off: none (the charred fortresses keep theirs).");

		new Harmony(PluginGUID).PatchAll(Assembly.GetExecutingAssembly());
		GameObject story = new GameObject("SnakewaySurfer_Story");
		DontDestroyOnLoad(story);
		story.AddComponent<SurfStory>();
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
			Giants = s_giants[i].Value,
			Locations = s_locations[i].Value,
			Landmarks = s_landmarks[i].Value,
			Pickables = s_pickables[i].Value,
		};
	}

	// Terminal.InitTerminal fills the command table (and can run again on scene loads), so the
	// command is added after it each time, as ValheimMapMarker does.
	[HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
	private static class InitTerminalPatch
	{
		private static void Postfix()
		{
			new Terminal.ConsoleCommand("surfsup", "surfsup [biome] [stars] - kill some time: run the Snakeway through the biomes, dodging monsters on 3 lanes (starts at your latest checkpoint, or the biome you name)", args =>
			{
				Player player = Player.m_localPlayer;
				if (player == null)
				{
					args.Context.AddString("Load into a world first.");
					return;
				}
				SurfGame.Begin(player, args.Context, args.Args);
			});
			new Terminal.ConsoleCommand("surfprogress", "surfprogress [wipe | set <biome> [stars] | dream] - testing: show this character's SnakewaySurfer progress, erase all of it, set the checkpoints reached (e.g. \"surfprogress set plains 1\"), or see the first-run dream again", args =>
			{
				Player player = Player.m_localPlayer;
				if (player == null)
				{
					args.Context.AddString("Load into a world first.");
					return;
				}
				if (SurfGame.Active != null)
				{
					args.Context.AddString("Stop the run first.");
					return;
				}
				args.Context.AddString(Progress.Command(player, args.Args));
			});
			new Terminal.ConsoleCommand("surfstory", "surfstory [reset | portal | hugin [days awake] | munin [episode] | next] - testing: the ravens' story (the strange portal, Hugin's nagging, Munin's episodes)", args =>
			{
				Player player = Player.m_localPlayer;
				if (player == null)
				{
					args.Context.AddString("Load into a world first.");
					return;
				}
				args.Context.AddString(SurfStory.Command(player, args.Args));
			});
			new Terminal.ConsoleCommand("surfportal", "surfportal - testing: the ravens' strange portal, right now, in front of you", args =>
			{
				Player player = Player.m_localPlayer;
				if (player == null)
				{
					args.Context.AddString("Load into a world first.");
					return;
				}
				args.Context.AddString(SurfStory.Command(player, new[] { "surfstory", "portal" }));
			});
			new Terminal.ConsoleCommand("surftest", "surftest [quick] [biome] | stop - testing: the run plays itself through the menu, every biome, dungeon, boss and the crossing, saving screenshots and a report to BepInEx/SnakewaySurferTest (about 20 minutes; \"quick\" is open track only, a biome's name does just that biome)", args =>
			{
				Player player = Player.m_localPlayer;
				if (player == null)
				{
					args.Context.AddString("Load into a world first.");
					return;
				}
				if (args.Args.Length > 1 && args.Args[1].ToLowerInvariant() == "stop")
				{
					SurfGame.Stop();
					args.Context.AddString("Self-test stopped. Its report so far is in BepInEx/SnakewaySurferTest.");
					return;
				}
				if (SurfGame.Active != null)
				{
					args.Context.AddString("Stop the run first.");
					return;
				}
				SurfGame.TourRequest = args.Args;
				SurfGame.Begin(player, args.Context);
				if (SurfGame.Active == null)
				{
					SurfGame.TourRequest = null;
					return;
				}
				args.Context.AddString("Self-test started: hands off the keyboard until it says it's finished. Close this console (F5) so it can run.");
			});
			new Terminal.ConsoleCommand("surfkit", "surfkit - testing: saves pictures and parts lists of the game's own dungeon rooms, then takes you round each biome for pictures and a count of what stands there (a few minutes, you can't be hurt and are brought back), to BepInEx/SnakewaySurferTest", args =>
			{
				if (Player.m_localPlayer == null)
				{
					args.Context.AddString("Load into a world first.");
					return;
				}
				if (SurfGame.Active != null)
				{
					args.Context.AddString("Stop the run first.");
					return;
				}
				Kit.Run(args.Context);
			});
			new Terminal.ConsoleCommand("surfprobe", "surfprobe - write this spot's weather, fog, light, camera effects, terrain material and nearby creatures to the log (for tuning SnakewaySurfer)", args =>
			{
				Probe.Run(args.Context);
			});
			new Terminal.ConsoleCommand("surfcompare", "surfcompare - put the minigame's version of the creature you're looking at beside the real one and log both (size, pose, animation); again to remove it", args =>
			{
				Compare.Run(args.Context);
			});
		}
	}

	private void OnDestroy()
	{
		SurfGame.Stop();
	}
}
