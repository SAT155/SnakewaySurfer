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
	internal static ConfigEntry<string> TrackPiece = null!;
	internal static ConfigEntry<float> StageHeight = null!;
	internal static ConfigEntry<int> RenderLayer = null!;

	internal static ConfigEntry<bool> DebugLog = null!;
	internal static ConfigEntry<bool> Invincible = null!;
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
	internal static ConfigEntry<bool> IgnoreBossGate = null!;
	internal static ConfigEntry<bool> BossLandmarks = null!;
	internal static ConfigEntry<bool> BossChase = null!;
	internal static ConfigEntry<bool> BossWeather = null!;
	internal static ConfigEntry<bool> BossSummon = null!;
	internal static ConfigEntry<float> GiantChance = null!;
	internal static ConfigEntry<float> FogStrength = null!;
	internal static ConfigEntry<KeyCode> SprintKey = null!;
	internal static ConfigEntry<KeyCode> BlockKey = null!;
	internal static ConfigEntry<KeyCode> ThrowKey = null!;
	internal static ConfigEntry<float> SprintSpeed = null!;
	internal static ConfigEntry<float> MaxStamina = null!;
	internal static ConfigEntry<float> SprintStamina = null!;
	internal static ConfigEntry<float> BlockStamina = null!;
	internal static ConfigEntry<float> StaminaRegen = null!;
	internal static ConfigEntry<float> ParryWindow = null!;
	internal static ConfigEntry<bool> PopupText = null!;
	internal static ConfigEntry<bool> StartMenu = null!;
	internal static ConfigEntry<bool> ParryDuel = null!;
	internal static ConfigEntry<float> AmbientLight = null!;
	internal static ConfigEntry<float> HillHeight = null!;
	internal static ConfigEntry<float> TrackBrightness = null!;
	internal static ConfigEntry<bool> OceanCrossing = null!;
	internal static ConfigEntry<float> OceanLength = null!;
	internal static ConfigEntry<float> SailSpeed = null!;
	internal static ConfigEntry<bool> SeaSerpent = null!;

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
			"Stops the minigame and puts you back in the world. Stopping mid-run still counts the run for rewards.");

		StopWhenHurt = Config.Bind("1 - Run", "Stop When Hurt", true,
			"Stop the minigame as soon as something damages your character in the real world. Health lost because food is wearing off doesn't count.");
		HurtThreshold = Config.Bind("1 - Run", "Hurt Threshold", 1f,
			new ConfigDescription("Smallest health drop (in one frame) that counts as being hurt.", new AcceptableValueRange<float>(0.1f, 50f)));
		Sounds = Config.Bind("3 - Look", "Sounds", true,
			"Play sounds for the countdown, jumps, slides, lane changes, coins, new biomes and crashes. Only you hear them.");
		SoundVolume = Config.Bind("3 - Look", "Sound Volume", 0.8f,
			new ConfigDescription("Volume of the minigame sounds.", new AcceptableValueRange<float>(0f, 1f)));

		// Before 1.14 XP was given in levels, which is a lot of XP at a high skill (Sam). These two
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
			// 1.12: Sam found Run XP still too high at 0.25 levels per biome.
			if (Mathf.Approximately(RunLevelsPerBiome.Value, 0.25f))
			{
				RunLevelsPerBiome.Value = 0.08f;
			}
			balance.Value = 4;
		}
		if (balance.Value < 5)
		{
			// 1.14 (Sam): greydwarf nests are Black Forest things, not Meadows ones.
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
		GameTerrainShader = Config.Bind("3 - Look", "Game Terrain Shader", false,
			"Testing: draw the land with the game's own terrain shader. It doesn't draw on the minigame's stage (the ground shows the sky), so by default the land uses the game's ground textures on a plain shader (Game Ground Textures).");
		GameTerrainShape = Config.Bind("3 - Look", "Game Terrain Shape", true,
			"Shape the land beside the track like this world's real ground: each biome borrows a ring of terrain from a spot in your world where that biome is (the game's own height formulas, with its rivers, Mistlands cliffs and mountain slopes), plus the game's paint mask for the ground textures. Off: the minigame's own rolling hills.");
		GameGrass = Config.Bind("3 - Look", "Game Grass", true,
			"Grow the game's own grass and ground clutter beside the track, by the game's rules for each biome (and its grass quality setting).");
		TrackStyle = Config.Bind("3 - Look", "Track Style", "dirt",
			"With Game Terrain: \"dirt\" (a dirt path like a hoed one), \"paved\" (like the hoe's paved road) or \"stone\" (the Track Piece's stone floor).");
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
			"Hold to block a creature's attack with your shield (costs stamina per blocked hit, and you run a little slower). Press it just before the hit lands to parry: no stamina, and the attacker staggers. Logs, gas and tar can't be blocked.");
		ThrowKey = Config.Bind("2 - Keys", "Throw Key", KeyCode.Mouse0,
			"Throws a spear you picked up at the boss (Eikthyr): it flies down your lane, so throw when he's lined up with you.");
		SprintSpeed = Config.Bind("1 - Run", "Sprint Speed", 1.35f,
			new ConfigDescription("How much faster you run while sprinting.", new AcceptableValueRange<float>(1f, 2f)));
		MaxStamina = Config.Bind("1 - Run", "Stamina", 100f,
			new ConfigDescription("Stamina for sprinting and blocking. It recharges after a second of not using it.", new AcceptableValueRange<float>(10f, 500f)));
		SprintStamina = Config.Bind("1 - Run", "Sprint Stamina Per Second", 18f,
			new ConfigDescription("Stamina used each second of sprinting.", new AcceptableValueRange<float>(0f, 100f)));
		BlockStamina = Config.Bind("1 - Run", "Block Stamina", 30f,
			new ConfigDescription("Stamina used by each blocked hit (a parry is free). Without enough, the guard breaks and the hit gets through.", new AcceptableValueRange<float>(0f, 200f)));
		StaminaRegen = Config.Bind("1 - Run", "Stamina Regen Per Second", 22f,
			new ConfigDescription("Stamina recovered each second once you stop sprinting and blocking for a second.", new AcceptableValueRange<float>(1f, 200f)));
		ParryWindow = Config.Bind("1 - Run", "Parry Window", 0.3f,
			new ConfigDescription("Seconds before a hit in which pressing block parries it.", new AcceptableValueRange<float>(0.05f, 1f)));
		ParryDuel = Config.Bind("1 - Run", "Parry Duel", true,
			"Bosses (not Eikthyr or Moder) are hurt by parrying: grab the weapon on the track and the boss closes in and swings at your lane up to three times; parry a swing and your blow lands. Off: grabbing the weapon hurts the boss straight away.");
		StartMenu = Config.Bind("2 - Keys", "Start Menu", true,
			"surfsup (with nothing after it) opens a grid of the biomes and star laps you've reached, to pick where to start. Off: it starts at your latest checkpoint.");
		PopupText = Config.Bind("3 - Look", "Popup Text", false,
			"Show text pop-ups during the run (\"Entering ...\", boss messages, JUMP!/SLIDE!). Off: the run speaks through sounds, flashes and the HUD instead.");
		Swoopers = Config.Bind("1 - Run", "Swoopers", "deathsquito",
			"Flying creatures that wait beside the track and dive across the lanes at head height as you come (slide under them).");
		Ambushers = Config.Bind("1 - Run", "Ambushers", "abomination",
			"Creatures that lie hidden and burst up as you get close (in the lanes, and as the Swamp's straddling giant).");
		Smashers = Config.Bind("1 - Run", "Smashers", "greydwarfbrute:1, goblinbrute:3, seekerbrute:1, stonegolem:1",
			"Brutes that smash the ground: each blow sends a shockwave down all three lanes (jump it). The number is how many blows in a row (the goblin brute's berserk smash hits three times).");
		CreatureSchemas = Config.Bind("1 - Run", "Creature Schemas", Schemas.Defaults,
			"What each creature does, as \"Prefab: tags; Prefab: tags\" (a trailing * matches a name start). Tags: charger, waiter, switcher, swarm, herd, line, crosser, shooter, flyshooter, sweep, gas2, gas3, smash, slams, lob, fire, tar, bomb, tickdrop, leaper, headleap, hurdle, stomper, ambush, chaser, behind, helper, talk. Creatures not listed behave as before.");
		LoopBiomes = Config.Bind("1 - Run", "Loop Biomes", 6,
			new ConfigDescription("How many biomes one lap of the run has (6 = Meadows to the Mistlands). After the last one the run goes back to the Meadows with 1-star creatures, then 2, then 3. 0: no loop, the run goes through every biome and stays in the last.", new AcceptableValueRange<int>(0, 8)));
		BossFights = Config.Bind("1 - Run", "Boss Fights", true,
			"Each biome ends with its boss once you've beaten that boss in this world: some coins become its summoning item (the game's own amount, e.g. 2 deer trophies for Eikthyr), you stay in the biome until you have them all, then you face the boss. Beating it saves a checkpoint for the next biome (\"surfsup\" starts at your latest one; \"surfsup swamp\" or \"surfsup 3 1\" picks one). Off: no items or bosses, the run goes straight on.");
		Lives = Config.Bind("1 - Run", "Lives", 3,
			new ConfigDescription("Hits you can take before the run is over (three strikes and you're out). 1: the first hit ends it.", new AcceptableValueRange<int>(1, 9)));
		BossLandmarks = Config.Bind("1 - Run", "Boss Landmarks", true,
			"Boss arenas show the boss's own place from the game (Eikthyr's altar, the Elder's stones, Moder's peak...) looming far ahead, with the land rising beside a flatter path and torches lining it. Off: arenas only take on the boss's colour.");
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
		OceanCrossing = Config.Bind("1 - Run", "Ocean Crossing", true,
			"Between the Mountains and the Plains the run crosses the ocean: the track runs down a beach to a longship and the camera rises above it. A / D steer, W / S set the sail (oars, half, full), the Throw Key fires the bow ballista and the Block Key blocks and parries. The wind never stops and turns now and then: as in the game a sail is fastest with the wind from the side and useless heading into it (tack across it). In the middle a storm brings rolling waves (meet them bow or stern first, never side on), lightning and a sea serpent. Beating Moder saves a checkpoint at the crossing; making it opens the Plains in the start menu. Off: the Plains follow the Mountains as before.");
		OceanLength = Config.Bind("1 - Run", "Ocean Length", 900f,
			new ConfigDescription("Metres of sea before the serpent rises in the storm; the far shore is about a third as far again after it.", new AcceptableValueRange<float>(300f, 4000f)));
		SailSpeed = Config.Bind("1 - Run", "Sail Speed", 16f,
			new ConfigDescription("The longship's speed (m/s) under full sail with the wind from the side; half sail is about 60% of it, the oars a steady 6.", new AcceptableValueRange<float>(6f, 40f)));
		SeaSerpent = Config.Bind("1 - Run", "Sea Serpent", true,
			"A sea serpent rises in the storm and must be beaten with the ballista to reach the far shore. Off: the storm just passes.");

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
