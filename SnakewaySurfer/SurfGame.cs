using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// The minigame itself. Everything here is local to the player who typed "surfsup": the track is
// built from lifeless copies of vanilla prefabs far above the player, on a render layer only our
// own camera draws, so nothing is networked and nothing touches the world. Other players only
// see what PlayerHooks does to the real character (running in place, Dverger circlet).
internal sealed partial class SurfGame : MonoBehaviour
{
	public static SurfGame? Active;

	// True while the character should look like it's running to everyone else.
	public static bool RunningInPlace => Active != null && Active.m_state == State.Running;

	private enum State
	{
		Countdown,
		Running,
		Crashed,
	}

	private enum Kind
	{
		Block,
		Low,
		High,
		Coin,
		Scenery,
		Ground,
		// Tar: slows you down while you run through it.
		Slow,
		// A boss's summoning item, and what you hurt the boss with (SurfBoss.cs).
		Item,
		BossHit,
	}

	private sealed class Thing
	{
		public GameObject Go = null!;
		public Kind Kind;
		public int Lane;
		public float Z;
		public float Y;
		public float HalfLength;
		public float Speed;
		public string Label = "";
		public string CrashText = "";
		public bool Collidable = true;
		// Set pieces (trolls): what moves when the player gets close.
		public Special Special;
		public Transform? Moving;
		public float TriggerDistance;
		public bool Triggered;
		public float Progress;
		public int Side;
		// Height above the road, which rolls; moving things follow it.
		public float BaseY;
		// Shots fly across lanes toward where you were, so they keep their own x.
		public bool FreeX;
		public float X;
		public float TargetX;
		public float XSpeed;
		// Creatures: their own attack, played when you come close.
		public Animator? Anim;
		public CreatureAttack? Attack;
		public CreatureInfo? Info;
		// Short creatures can be jumped over: you clear them above this height (0 = can't).
		public float ClearHeight;
		public float Height;
		public bool Swoop;
		public bool Sleeping;
		public bool Stomper;
		// A straddling giant that bursts up out of the ground instead of leaping in.
		public bool Rise;
		// > 0: removed after this many seconds.
		public float Life;
		// Ground smashes left in this brute's attack; each sends a shockwave down the lanes.
		public int Smashes;
		public bool Alerted;
		// Speed and animation pace once it notices you (it idles until then).
		public float MoveSpeed;
		public float Pace;
		public float StepTimer;
		public bool Attacked;
		public float Cooldown;
		public float FireIn = -1f;
		// Creature schemas (Schemas.cs).
		public Schema? Schema;
		// Gas over all three lanes: passed by jumping or sliding.
		public bool JumpOrSlide;
		// The attack this one does when it lands: "sweep", "gas2", "gas3", "slams", ... or "".
		public string Move = "";
		// Area attacks from range: "lob", "fire", "tar", "bomb", "tickdrop", or "".
		public string Ranged = "";
		public int[] HitLanes = new int[0];
		public readonly List<GameObject> Markers = new();
		public bool Switcher;
		public float SwitchTimer;
		public bool Crosser;
		public bool Leaper;
		// Bats (SurfBats.cs): a nuisance, not a crash.
		public bool Nuisance;
		public float Seed;
		public int Phase;
		public float PhaseTime;
		public float FromX;
		public float FromY;
		public float FromZ;
		public float ToZ;
		public bool Chaser;
		public bool ChaseHigh;
		public bool Helper;
		public int Level = 1;
		// Branching paths (SurfBranches.cs): a root to slide under that carries a torch, and
		// which branch section a thing belongs to.
		public bool Torch;
		public int Group = -1;
		// Boss chases (SurfChase.cs): "JUMP!"/"SLIDE!" shown as it reaches you, and a charge
		// that starts after a moment.
		public string Hint = "";
		public float ChargeIn = -1f;
		public float ChargeSpeed;
	}

	private enum Special
	{
		None,
		Swing,
		Drop,
	}

	private sealed class BiomeRuntime
	{
		public BiomeDef Def = null!;
		public Color Sky;
		public Material Ground = null!;
		public readonly List<SpawnEntry> Monsters = new();
		public readonly List<SpawnEntry> Flyers = new();
		public readonly List<GameObject> Scenery = new();
		public readonly List<GameObject> Logs = new();
		public readonly List<(GameObject prefab, string label)> Giants = new();
		public float Fog;
		// From the game's own weather for this biome (EnvSetup), when it can be read.
		public Color FogColor;
		public Color Ambient;
		public Color SunColor = new Color(1f, 0.96f, 0.88f);
		public float SunIntensity;
		public bool Water;
		// Creature prefab name (or name start) -> its homes (nests, totems, bone piles).
		public readonly List<(string key, bool prefix, List<GameObject> homes)> Landmarks = new();
		public readonly List<GameObject> Pickables = new();
	}

	private static readonly Color[] s_sky =
	{
		new Color(0.56f, 0.76f, 0.95f),
		new Color(0.42f, 0.56f, 0.66f),
		new Color(0.46f, 0.50f, 0.42f),
		new Color(0.78f, 0.86f, 0.96f),
		new Color(0.82f, 0.80f, 0.64f),
		new Color(0.56f, 0.52f, 0.62f),
		new Color(0.48f, 0.22f, 0.16f),
		new Color(0.72f, 0.82f, 0.92f),
	};

	// Exponential-squared fog density per biome, thicker in the Swamp and Mistlands as in the game.
	private static readonly float[] s_fog = { 0.011f, 0.017f, 0.026f, 0.013f, 0.009f, 0.04f, 0.02f, 0.022f };

	private const float LaneWidth = 2.6f;
	private const float TrackHalf = LaneWidth * 1.5f + 0.4f;
	private const float StraddleStartX = TrackHalf + 7f;
	// Height of the hills beside the track, per biome.
	private static readonly float[] s_hills = { 3.5f, 7f, 4.5f, 16f, 2.5f, 10f, 6f, 12f };
	// How far the swamp water stands above the track.
	private const float WaterDepth = 0.3f;
	private const float Gravity = 32f;
	private const float GroundTile = 40f;
	private const float AheadDistance = 150f;

	private Player m_player = null!;
	private int m_layer;
	private Transform m_stage = null!;
	private Transform m_templates = null!;
	private Camera m_camera = null!;
	private Light m_light = null!;
	private Camera? m_mainCamera;
	private Camera? m_ownCamera;
	private bool m_usingGameCamera;
	private SurfCameraFog? m_mainFog;
	private Color m_mainBackground;
	private float[] m_mainCullDistances = new float[32];
	private Vector3 m_camFinal;
	private Quaternion m_camFinalRot = Quaternion.identity;

	// GameCamera.LateUpdate moves the main camera every frame; put it back on our track.
	public void ApplyGameCamera()
	{
		if (!m_usingGameCamera || m_mainCamera == null)
		{
			return;
		}
		m_mainCamera.transform.SetPositionAndRotation(m_camFinal, m_camFinalRot);
		m_mainCamera.fieldOfView = Fov;
		if (GameCamera.instance != null && GameCamera.instance.m_skyCamera != null)
		{
			GameCamera.instance.m_skyCamera.fieldOfView = Fov;
			GameCamera.instance.m_skyCamera.transform.rotation = m_camFinalRot;
		}
	}
	// Widens a little while you sprint.
	private float Fov => Plugin.CameraFov.Value + 7f * Mathf.SmoothStep(0f, 1f, m_sprintLook);

	private int m_mainMask;
	private GameObject? m_hudRoot;
	private bool m_hudWasActive;

	private GameObject? m_avatar;
	private Animator? m_avatarAnim;
	private readonly HashSet<int> m_avatarParams = new();
	private float m_runAnimSpeed = 7f;

	private readonly List<BiomeRuntime> m_biomes = new();
	private readonly Dictionary<GameObject, GameObject> m_pictureOf = new();
	private GameObject? m_coinTemplate;
	private Material? m_trackMaterial;
	private Material? m_waterMaterial;
	private Texture2D? m_groundTexture;
	private Mesh? m_cube;

	private readonly List<Thing> m_things = new();
	private State m_state;
	private float m_stateTime;
	private float m_z;
	private float m_x;
	private float m_y;
	private float m_vy;
	private int m_lane = 1;
	private float m_slide;
	private float m_speed;
	private float m_runTime;
	private int m_coins;
	private int m_highestBiome;
	private float m_nextRowZ;
	private float m_nextSceneryZ;
	private float m_nextGroundZ;
	private int m_shownBiome = -1;
	private int m_lives = 3;
	private float m_hurtTime;
	private Renderer[] m_avatarRenderers = new Renderer[0];
	private string m_banner = "";
	private float m_bannerTime;
	// Shown even with Popup Text off (pause, rewards, the invincible test setting).
	private bool m_bannerKeep;
	private string m_crashText = "";
	private string m_rewardText = "";
	private float m_lastHealth;
	private bool m_consoleWasVisible;
	private Vector3 m_camPos;
	private bool m_camPlaced;
	private SurfAudio m_audio = null!;
	private int m_lastTick;
	private float m_stepTimer;

	public static void Begin(Player player, Terminal? terminal, string[]? args = null)
	{
		void Say(string text)
		{
			if (terminal != null)
			{
				terminal.AddString(text);
			}
			player.Message(MessageHud.MessageType.Center, text);
		}

		if (Active != null)
		{
			Say("You're already surfing.");
			return;
		}
		if (player.IsDead() || player.IsTeleporting())
		{
			Say("Not right now.");
			return;
		}
		if (player.IsAttached() || player.GetDoodadController() != null)
		{
			Say("Stand up first, then type surfsup.");
			return;
		}
		if (player.InWater() || !player.IsOnGround())
		{
			Say("Find solid ground first.");
			return;
		}

		int lap = Plugin.LoopBiomes.Value <= 0 ? Biomes.Defaults.Length : Mathf.Clamp(Plugin.LoopBiomes.Value, 1, Biomes.Defaults.Length);
		(int biome, int stars)? start = Progress.ParseStart(player, args ?? new string[0], lap, out string error, out bool explicitStart);
		if (start == null)
		{
			Say(error);
			return;
		}
		if (terminal != null && !Plugin.IgnoreBossGate.Value)
		{
			int allowed = Progress.AllowedBiomes(lap);
			BossDef? gate = Progress.BossOf(allowed - 1);
			if (allowed < lap && gate != null)
			{
				terminal.AddString($"surfsup: this world's run reaches the {Biomes.Defaults[allowed - 1].Name}; defeat {gate.Name} in the world to go further.");
			}
		}

		GameObject go = new GameObject("SnakewaySurfer_Game");
		SurfGame game = go.AddComponent<SurfGame>();
		game.m_startBiome = start.Value.biome;
		game.m_startStars = start.Value.stars;
		game.m_explicitStart = explicitStart;
		game.Init(player);
	}

	public static void Stop()
	{
		if (Active != null)
		{
			Destroy(Active.gameObject);
		}
	}

	private void Init(Player player)
	{
		Active = this;
		m_player = player;
		m_layer = Plugin.RenderLayer.Value;
		m_runAnimSpeed = player.m_runSpeed > 0f ? player.m_runSpeed : 7f;

		if (Console.instance != null && Console.IsVisible())
		{
			Console.instance.m_chatWindow.gameObject.SetActive(false);
		}

		Vector3 origin = player.transform.position + Vector3.up * Plugin.StageHeight.Value;
		m_stage = new GameObject("Stage").transform;
		m_stage.SetParent(transform, false);
		m_stage.position = origin;

		GameObject holder = new GameObject("Templates");
		holder.SetActive(false);
		holder.transform.SetParent(transform, false);
		m_templates = holder.transform;

		BuildMaterials();
		ResolveBiomes();

		GameObject camGo = new GameObject("SurfCamera");
		camGo.transform.SetParent(transform, false);
		m_camera = camGo.AddComponent<Camera>();
		m_camera.cullingMask = 1 << m_layer;
		m_camera.clearFlags = CameraClearFlags.SolidColor;
		m_camera.backgroundColor = s_sky[0];
		m_camera.fieldOfView = Plugin.CameraFov.Value;
		m_camera.nearClipPlane = 0.1f;
		m_camera.farClipPlane = 400f;
		camGo.AddComponent<SurfCameraFog>().Game = this;
		m_audio = new SurfAudio(camGo);

		m_mainCamera = Camera.main;
		if (m_mainCamera != null)
		{
			m_camera.depth = m_mainCamera.depth + 10f;
			m_mainMask = m_mainCamera.cullingMask;
			m_mainBackground = m_mainCamera.backgroundColor;
			m_mainCullDistances = m_mainCamera.layerCullDistances;
			if (Plugin.UseGameCamera.Value)
			{
				// Draw through the game's own camera, so its post effects (bloom, colour
				// grading, ambient occlusion, haze) and sky apply like in the real world.
				m_ownCamera = m_camera;
				m_ownCamera.enabled = false;
				m_camera = m_mainCamera;
				m_usingGameCamera = true;
				m_mainCamera.cullingMask = 1 << m_layer;
				float[] distances = (float[])m_mainCullDistances.Clone();
				if (m_layer < distances.Length)
				{
					distances[m_layer] = 0f;
				}
				m_mainCamera.layerCullDistances = distances;
				m_mainFog = m_mainCamera.gameObject.AddComponent<SurfCameraFog>();
				m_mainFog.Game = this;
			}
			else
			{
				// The world isn't on screen anyway; this saves drawing it twice.
				m_mainCamera.cullingMask = 0;
			}
		}

		GameObject lightGo = new GameObject("SurfSun");
		lightGo.transform.SetParent(transform, false);
		lightGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
		m_light = lightGo.AddComponent<Light>();
		m_light.type = LightType.Directional;
		m_light.intensity = Plugin.LightIntensity.Value;
		m_light.color = new Color(1f, 0.96f, 0.88f);
		m_light.shadows = LightShadows.Soft;
		m_light.cullingMask = 1 << m_layer;

		if (Hud.instance != null && Hud.instance.m_rootObject != null)
		{
			m_hudRoot = Hud.instance.m_rootObject;
			m_hudWasActive = m_hudRoot.activeSelf;
		}

		PlayerHooks.RefreshHelmet(player);
		ResetRun();
		Plugin.Log.LogInfo($"Surf's up: stage at {origin}, layer {m_layer}.");
	}

	private void OnDestroy()
	{
		if (Active == this)
		{
			Active = null;
		}
		if (m_paused)
		{
			AudioListener.pause = false;
		}
		DestroySchemaObjects();
		ReleaseLocations();
		if (m_mainCamera != null)
		{
			m_mainCamera.cullingMask = m_mainMask;
			if (m_usingGameCamera)
			{
				m_mainCamera.layerCullDistances = m_mainCullDistances;
				m_mainCamera.backgroundColor = m_mainBackground;
			}
		}
		if (m_mainFog != null)
		{
			Destroy(m_mainFog);
		}
		if (m_hudRoot != null)
		{
			m_hudRoot.SetActive(m_hudWasActive);
		}
		if (m_player != null)
		{
			PlayerHooks.RefreshHelmet(m_player);
		}
		if (m_cube != null)
		{
			Destroy(m_cube);
		}
		if (m_groundTexture != null)
		{
			Destroy(m_groundTexture);
		}
		foreach (Object o in new Object?[] { m_terrainLand, m_terrainTrack, m_landMask, m_trackMask })
		{
			if (o != null)
			{
				Destroy(o);
			}
		}
		Plugin.Log.LogInfo("Surf's over.");
	}

	// ---------------------------------------------------------------- setup

	private static Shader? FindPlainShader()
	{
		foreach (string name in new[] { "Standard", "Legacy Shaders/Diffuse", "Diffuse", "Unlit/Color", "Sprites/Default" })
		{
			Shader shader = Shader.Find(name);
			if (shader != null && shader.isSupported)
			{
				return shader;
			}
		}
		return null;
	}

	private static Material? BorrowMaterial(string prefabName)
	{
		GameObject? prefab = Pictures.Prefab(prefabName);
		if (prefab == null)
		{
			return null;
		}
		foreach (MeshRenderer r in prefab.GetComponentsInChildren<MeshRenderer>(true))
		{
			if (r.sharedMaterial != null)
			{
				return new Material(r.sharedMaterial);
			}
		}
		return null;
	}

	private void BuildMaterials()
	{
		GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
		m_cube = Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
		Destroy(cube);

		// The build piece's own shader didn't draw on our generated track mesh (1.4.6: invisible
		// from most angles, glaring white otherwise), so only its texture is borrowed, onto the
		// plain shader the land uses.
		Material? borrowed = BorrowMaterial(Plugin.TrackPiece.Value) ?? BorrowMaterial("stone_floor_2x2") ?? BorrowMaterial("wood_floor");
		Shader? plain = FindPlainShader();
		if (plain != null)
		{
			m_trackMaterial = new Material(plain) { name = "SurfTrack" };
			if (borrowed != null && borrowed.HasProperty("_MainTex") && borrowed.mainTexture != null && m_trackMaterial.HasProperty("_MainTex"))
			{
				m_trackMaterial.mainTexture = borrowed.mainTexture;
			}
			if (m_trackMaterial.HasProperty("_Color"))
			{
				m_trackMaterial.color = new Color(0.78f, 0.76f, 0.72f) * Plugin.TrackBrightness.Value;
			}
			if (m_trackMaterial.HasProperty("_Glossiness"))
			{
				m_trackMaterial.SetFloat("_Glossiness", 0.1f);
			}
		}
		else
		{
			m_trackMaterial = borrowed;
		}
		if (borrowed != null && borrowed != m_trackMaterial)
		{
			Destroy(borrowed);
		}
		m_groundTexture = MakeGroundTexture();
		MakeTerrainMaterials();
		string style = Plugin.TrackStyle.Value.Trim().ToLowerInvariant();
		if (m_terrainTrack == null && m_trackMaterial != null && style != "stone" && Plugin.GameGroundTextures.Value
			&& TerrainSlice(style == "paved" ? new Color(0.42f, 0.42f, 0.42f) : new Color(0.36f, 0.27f, 0.18f)) is Texture2D path)
		{
			// A dirt (or paved) path: the game's own ground texture for it.
			m_trackMaterial.mainTexture = path;
			m_trackMaterial.mainTextureScale = new Vector2(2f, 3f);
			if (m_trackMaterial.HasProperty("_Color"))
			{
				m_trackMaterial.color = new Color(0.85f, 0.85f, 0.85f) * Plugin.TrackBrightness.Value;
			}
		}
		m_waterMaterial = MakeWaterMaterial();
	}

	// A tileable mottled grey texture (value noise over a few octaves) that the biome colour
	// tints, so the land isn't one flat colour. Made here because the game's terrain shader
	// needs its own splat maps and can't be borrowed for a plain mesh.
	private static Texture2D MakeGroundTexture()
	{
		const int size = 256;
		System.Random rng = new System.Random(20261104);
		int[] cells = { 4, 8, 16, 32, 64 };
		float[] weights = { 0.32f, 0.26f, 0.18f, 0.13f, 0.11f };
		float[][] lattices = new float[cells.Length][];
		for (int o = 0; o < cells.Length; o++)
		{
			lattices[o] = new float[cells[o] * cells[o]];
			for (int i = 0; i < lattices[o].Length; i++)
			{
				lattices[o][i] = (float)rng.NextDouble();
			}
		}
		Color[] pixels = new Color[size * size];
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float v = 0f;
				for (int o = 0; o < cells.Length; o++)
				{
					int c = cells[o];
					float fx = x * c / (float)size;
					float fy = y * c / (float)size;
					int x0 = Mathf.FloorToInt(fx);
					int y0 = Mathf.FloorToInt(fy);
					float tx = Mathf.SmoothStep(0f, 1f, fx - x0);
					float ty = Mathf.SmoothStep(0f, 1f, fy - y0);
					float[] l = lattices[o];
					float a = l[(y0 % c) * c + x0 % c];
					float b = l[(y0 % c) * c + (x0 + 1) % c];
					float d = l[((y0 + 1) % c) * c + x0 % c];
					float e = l[((y0 + 1) % c) * c + (x0 + 1) % c];
					v += weights[o] * Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(d, e, tx), ty);
				}
				float speckle = ((float)rng.NextDouble() - 0.5f) * 0.08f;
				float g = Mathf.Clamp01(0.82f + (v - 0.5f) * 0.9f + speckle);
				pixels[y * size + x] = new Color(g, g, g, 1f);
			}
		}
		Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
		tex.name = "SurfGround";
		tex.wrapMode = TextureWrapMode.Repeat;
		tex.filterMode = FilterMode.Trilinear;
		tex.anisoLevel = 4;
		tex.SetPixels(pixels);
		tex.Apply(true);
		return tex;
	}

	// The world's terrain material (from any loaded Heightmap) carries the game's ground
	// textures; a plain 2D one whose property name fits the biome is used for our land.
	private static Dictionary<string, Texture>? s_terrainTextures;

	private static Texture? TerrainTexture(string biome)
	{
		if (!Plugin.GameGroundTextures.Value)
		{
			return null;
		}
		if (s_terrainTextures == null)
		{
			s_terrainTextures = new Dictionary<string, Texture>();
			Heightmap hm = Object.FindObjectOfType<Heightmap>();
			Material? mat = hm != null ? (hm.m_material != null ? hm.m_material : hm.GetComponent<MeshRenderer>()?.sharedMaterial) : null;
			if (mat != null)
			{
				List<string> seen = new();
				foreach (string prop in mat.GetTexturePropertyNames())
				{
					Texture tex = mat.GetTexture(prop);
					seen.Add($"{prop}={(tex != null ? tex.GetType().Name + " " + tex.name : "none")}");
					if (tex is Texture2D)
					{
						s_terrainTextures[prop.ToLowerInvariant()] = tex;
					}
				}
				Plugin.Log.LogInfo($"Terrain material {mat.name} ({mat.shader?.name}) textures: {string.Join(", ", seen)}");
			}
			else
			{
				Plugin.Log.LogInfo("No terrain material found; using generated ground textures.");
			}
		}
		// The game's ground textures live in a texture array (_DiffuseArrayTex): the layer whose
		// colour is closest to the biome's ground.
		if (BiomeGroundColor(biome) is Color want && TerrainSlice(want) is Texture2D slice)
		{
			return slice;
		}
		string[] keys = biome switch
		{
			"Meadows" => new[] { "grass" },
			"Black Forest" => new[] { "forest", "grass" },
			"Swamp" => new[] { "swamp", "mud", "dirt" },
			"Mountains" => new[] { "snow" },
			"Plains" => new[] { "heath", "plains", "grass" },
			"Mistlands" => new[] { "mist", "rock" },
			"Ashlands" => new[] { "ash", "lava", "rock" },
			"Deep North" => new[] { "ice", "snow" },
			_ => new[] { "grass" },
		};
		foreach (string key in keys)
		{
			foreach (KeyValuePair<string, Texture> kv in s_terrainTextures)
			{
				if (kv.Key.Contains(key) && !kv.Key.Contains("normal") && !kv.Key.Contains("bump") && !kv.Key.Contains("mask"))
				{
					return kv.Value;
				}
			}
		}
		return null;
	}

	private static List<(Texture2D tex, Color avg)>? s_slices;

	private static Color? BiomeGroundColor(string biome)
	{
		foreach (BiomeDef d in Biomes.Defaults)
		{
			if (d.Name == biome)
			{
				return d.Ground;
			}
		}
		return null;
	}

	// Each layer of the game's terrain texture array copied out on the GPU (the array isn't
	// readable from scripts), with its average colour. The terrain's own shader doesn't draw on
	// our stage (1.11-1.12: the ground showed the sky), so these go on a plain shader instead.
	private static void LoadSlices()
	{
		if (s_slices != null)
		{
			return;
		}
		s_slices = new List<(Texture2D, Color)>();
		try
		{
			Heightmap hm = Object.FindObjectOfType<Heightmap>();
			Material? mat = hm != null ? hm.m_material : null;
			Texture2DArray? array = mat != null && mat.HasProperty("_DiffuseArrayTex") ? mat.GetTexture("_DiffuseArrayTex") as Texture2DArray : null;
			if (array == null)
			{
				Plugin.Log.LogInfo("No terrain texture array found; using generated ground textures.");
				return;
			}
			List<string> seen = new();
			for (int i = 0; i < array.depth; i++)
			{
				Texture2D tex = new Texture2D(array.width, array.height, array.format, array.mipmapCount, false)
				{
					name = $"terrain_layer_{i}",
					wrapMode = TextureWrapMode.Repeat,
					filterMode = FilterMode.Trilinear,
					anisoLevel = 4,
				};
				for (int mip = 0; mip < array.mipmapCount; mip++)
				{
					Graphics.CopyTexture(array, i, mip, tex, 0, mip);
				}
				Color avg = AverageColor(tex);
				s_slices.Add((tex, avg));
				seen.Add($"{i}=({avg.r:0.00},{avg.g:0.00},{avg.b:0.00})");
			}
			Plugin.Log.LogInfo($"Terrain texture array {array.name}: {array.depth} layers {array.width}x{array.height} {array.format}, average colours {string.Join(" ", seen)}.");
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Couldn't copy the terrain textures: {e.Message}");
		}
	}

	private static Color AverageColor(Texture tex)
	{
		RenderTexture rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
		RenderTexture previous = RenderTexture.active;
		Texture2D read = new Texture2D(8, 8, TextureFormat.RGBA32, false);
		try
		{
			Graphics.Blit(tex, rt);
			RenderTexture.active = rt;
			read.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
			read.Apply();
			Color sum = Color.black;
			foreach (Color c in read.GetPixels())
			{
				sum += c;
			}
			return sum / 64f;
		}
		finally
		{
			RenderTexture.active = previous;
			RenderTexture.ReleaseTemporary(rt);
			Object.Destroy(read);
		}
	}

	// The terrain layer closest in colour to `want` (by hue and brightness), or null.
	private static Texture2D? TerrainSlice(Color want)
	{
		LoadSlices();
		Texture2D? best = null;
		float bestD = float.MaxValue;
		Color.RGBToHSV(want, out float wh, out float ws, out float wv);
		foreach ((Texture2D tex, Color avg) in s_slices!)
		{
			Color.RGBToHSV(avg, out float h, out float s, out float v);
			float dh = Mathf.Abs(h - wh);
			dh = Mathf.Min(dh, 1f - dh) * Mathf.Min(s, ws);
			float d = dh * dh * 4f + (s - ws) * (s - ws) + (v - wv) * (v - wv) * 1.5f;
			if (d < bestD)
			{
				bestD = d;
				best = tex;
			}
		}
		return best;
	}

	private Material? m_terrainLand;
	private Material? m_terrainTrack;
	private Texture2D? m_landMask;
	private Texture2D? m_trackMask;

	// The game's own terrain material (from any loaded Heightmap): real biome ground textures,
	// and the track becomes a path painted into it through the "cleared" mask, like a hoed or
	// paved path in the world. The mask channels: red dirt, green cultivated, blue paved.
	private void MakeTerrainMaterials()
	{
		if (!Plugin.GameTerrainShader.Value)
		{
			return;
		}
		Heightmap hm = Object.FindObjectOfType<Heightmap>();
		Material? source = hm != null ? hm.m_material : null;
		if (source == null)
		{
			Plugin.Log.LogInfo("No terrain material found; using the minigame's own ground.");
			return;
		}
		m_landMask = MaskTexture(new Color(0f, 0f, 0f, 1f), new Color(0f, 0f, 0f, 1f));
		m_terrainLand = new Material(source) { name = "SurfTerrain" };
		PrepareTerrain(m_terrainLand, source);
		// The shader shades by height above the sea (wet sand at the shore, and so on); the stage
		// floats thousands of metres up, so move its sea to just below the road.
		float sea = m_stage != null ? m_stage.position.y - 5f : source.GetFloat("_WaterLevel");
		m_terrainLand.SetTexture("_ClearedMaskTex", m_landMask);
		if (m_terrainLand.HasProperty("_WaterLevel"))
		{
			m_terrainLand.SetFloat("_WaterLevel", sea);
		}
		string style = Plugin.TrackStyle.Value.Trim().ToLowerInvariant();
		if (style != "stone")
		{
			Color path = style == "paved" ? new Color(0f, 0f, 1f, 1f) : new Color(1f, 0f, 0f, 1f);
			m_trackMask = MaskTexture(new Color(0f, 0f, 0f, 1f), path);
			m_terrainTrack = new Material(source) { name = "SurfTerrainPath" };
			PrepareTerrain(m_terrainTrack, source);
			m_terrainTrack.SetTexture("_ClearedMaskTex", m_trackMask);
			if (m_terrainTrack.HasProperty("_WaterLevel"))
			{
				m_terrainTrack.SetFloat("_WaterLevel", sea);
			}
		}
		Plugin.Log.LogInfo($"Using the game's terrain material '{source.name}' ({source.shader.name}) for the land{(m_terrainTrack != null ? " and a " + style + " path" : "")}.");
	}

	// What a Heightmap sets on its own copy of the material besides the paint mask: the ocean
	// depth at its corners (_depth, none here) and, per renderer, _LodHideDistance. That one is
	// for the distant-terrain LODs, which hide themselves within that distance of the player;
	// a fresh copy keeps the material's default, which hid our whole ground (you saw the sky
	// through it). 0 = never hidden, as on a real near tile.
	private static void PrepareTerrain(Material m, Material source)
	{
		if (m.HasProperty(LodHide))
		{
			Plugin.Log.LogInfo($"Terrain material {source.name}: _LodHideDistance was {m.GetFloat(LodHide):0.#}; keywords [{string.Join(" ", m.shaderKeywords)}].");
			m.SetFloat(LodHide, 0f);
		}
		m.SetFloatArray("_depth", new float[4]);
	}

	internal const string LodHide = "_LodHideDistance";

	// 16 x 1: the edges are the plain ground, the middle the path colour, faded between.
	private static Texture2D MaskTexture(Color edge, Color middle)
	{
		Texture2D tex = new Texture2D(16, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SurfMask" };
		for (int x = 0; x < 16; x++)
		{
			float d = Mathf.Abs((x + 0.5f) / 16f - 0.5f) * 2f;
			tex.SetPixel(x, 0, Color.Lerp(middle, edge, Mathf.SmoothStep(0f, 1f, (d - 0.7f) / 0.3f)));
		}
		tex.Apply();
		return tex;
	}

	private static Material? MakeWaterMaterial()
	{
		Shader? shader = FindPlainShader();
		if (shader == null)
		{
			return null;
		}
		Material mat = new Material(shader) { name = "SurfWater" };
		if (mat.HasProperty("_Color"))
		{
			mat.color = new Color(0.32f, 0.38f, 0.26f, 0.7f);
		}
		if (shader.name == "Standard")
		{
			// The Standard shader's "Fade" mode, set up by hand as its editor would.
			mat.SetFloat("_Mode", 2f);
			mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
			mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
			mat.SetInt("_ZWrite", 0);
			mat.DisableKeyword("_ALPHATEST_ON");
			mat.EnableKeyword("_ALPHABLEND_ON");
			mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
			mat.renderQueue = 3000;
			mat.SetFloat("_Glossiness", 0.9f);
			mat.SetFloat("_Metallic", 0f);
		}
		return mat;
	}

	private Material MakeGroundMaterial(Color color, Texture? texture = null)
	{
		Shader? shader = FindPlainShader();
		Material mat;
		if (shader != null)
		{
			mat = new Material(shader);
		}
		else if (m_trackMaterial != null)
		{
			mat = new Material(m_trackMaterial);
		}
		else
		{
			mat = new Material(Shader.Find("Hidden/InternalErrorShader"));
		}
		if (mat.HasProperty("_Color"))
		{
			// The noise texture averages about 0.82, so lift the colour to keep the biome's
			// brightness; the game's own terrain textures already have their colour.
			mat.color = texture != null ? new Color(0.85f, 0.85f, 0.85f) : color * 1.2f;
		}
		if ((texture ?? m_groundTexture) != null && mat.HasProperty("_MainTex"))
		{
			mat.mainTexture = texture ?? m_groundTexture;
			if (texture != null && texture.name.StartsWith("terrain_layer_"))
			{
				// uv is in 10 m units: the game's ground repeats every few metres.
				mat.mainTextureScale = new Vector2(3.5f, 3.5f);
			}
		}
		if (mat.HasProperty("_Glossiness"))
		{
			mat.SetFloat("_Glossiness", 0.05f);
		}
		return mat;
	}

	private void ResolveBiomes()
	{
		Pictures.ClearCaches();
		List<string> missing = new();
		for (int i = 0; i < Biomes.Defaults.Length; i++)
		{
			BiomeDef def = Plugin.BiomeConfig(i);
			BiomeRuntime rt = new BiomeRuntime { Def = def, Sky = s_sky[Mathf.Min(i, s_sky.Length - 1)], Fog = s_fog[Mathf.Min(i, s_fog.Length - 1)], Ground = MakeGroundMaterial(def.Ground, TerrainTexture(def.Name)) };
			rt.FogColor = rt.Sky;
			rt.Ambient = rt.Sky * Plugin.AmbientLight.Value;
			rt.SunIntensity = Plugin.LightIntensity.Value;
			GameWeather.Apply(i, rt.Def.Name, ref rt.Fog, ref rt.FogColor, ref rt.Ambient, ref rt.SunColor, ref rt.SunIntensity, ref rt.Sky);
			rt.Water = Plugin.SwampWater.Value && def.Name == "Swamp" && m_waterMaterial != null;
			foreach (string key in Pictures.SplitList(def.Giants))
			{
				GameObject? prefab = Pictures.Monster(key);
				if (prefab != null)
				{
					rt.Giants.Add((prefab, MonsterLabel(prefab)));
				}
				else
				{
					missing.Add(key);
				}
			}
			bool autoMonsters = def.Monsters.Trim().Equals("auto", System.StringComparison.OrdinalIgnoreCase);
			bool autoFlyers = def.Flyers.Trim().Equals("auto", System.StringComparison.OrdinalIgnoreCase);
			List<SpawnEntry> table = autoMonsters || autoFlyers ? SpawnTables.ForBiome(i) : new List<SpawnEntry>();
			if (table.Count == 0 && (autoMonsters || autoFlyers))
			{
				Plugin.Log.LogWarning($"No spawn table found for the {def.Name}; using the built-in creature list.");
				table = SpawnTables.Parse(Biomes.Defaults[i].FallbackMonsters + ", " + Biomes.Defaults[i].FallbackFlyers, missing);
			}
			if (autoMonsters)
			{
				foreach (SpawnEntry extra in SpawnTables.Parse(def.Locations, missing))
				{
					SpawnEntry? same = table.Find(e => e.Prefab == extra.Prefab);
					if (same != null)
					{
						same.Weight += extra.Weight;
					}
					else
					{
						extra.GroupMax = 1;
						table.Add(extra);
					}
				}
			}
			foreach (SpawnEntry e in autoMonsters ? table : SpawnTables.Parse(def.Monsters, missing))
			{
				if (!e.Flying)
				{
					rt.Monsters.Add(e);
				}
				else if (!autoFlyers || autoMonsters)
				{
					rt.Flyers.Add(e);
				}
			}
			// 1.14 (Sam: the swamp was mostly abominations): giants come only as the rare giant
			// set piece (Giant Chance), not also as everyday creatures from the spawn table.
			int giantsRemoved = rt.Monsters.RemoveAll(m => rt.Giants.Exists(g => g.prefab == m.Prefab));
			if (giantsRemoved > 0 && rt.Monsters.Count == 0)
			{
				rt.Monsters.AddRange(SpawnTables.Parse(Biomes.Defaults[i].FallbackMonsters, missing).FindAll(m => !m.Flying && !rt.Giants.Exists(g => g.prefab == m.Prefab)));
			}
			if (autoFlyers && !autoMonsters)
			{
				rt.Flyers.AddRange(table.FindAll(e => e.Flying));
			}
			if (autoFlyers && def.Name == "Swamp" && !rt.Flyers.Exists(f => IsBat(f.Prefab)) && Pictures.Monster("Bat") is GameObject bat)
			{
				// Swamps have their bats (Sam), in swarms (SurfBats.cs).
				rt.Flyers.Add(new SpawnEntry { Prefab = bat, Label = SpawnTables.Label(bat), Weight = 1f, Flying = true });
			}
			else if (!autoFlyers)
			{
				rt.Flyers.Clear();
				rt.Flyers.AddRange(SpawnTables.Parse(def.Flyers, missing));
			}
			foreach (string name in Pictures.SplitList(def.Pickables))
			{
				GameObject? prefab = Pictures.Prefab(name);
				if (prefab != null)
				{
					rt.Pickables.Add(prefab);
				}
				else
				{
					missing.Add(name);
				}
			}
			foreach (string part in def.Landmarks.Split(';'))
			{
				int colon = part.IndexOf(':');
				if (colon <= 0)
				{
					continue;
				}
				string key = part.Substring(0, colon).Trim();
				bool prefix = key.EndsWith("*");
				List<GameObject> homes = new();
				foreach (string name in Pictures.SplitList(part.Substring(colon + 1)))
				{
					GameObject? prefab = Pictures.Prefab(name);
					if (prefab != null)
					{
						homes.Add(prefab);
					}
					else
					{
						missing.Add(name);
					}
				}
				if (homes.Count > 0)
				{
					rt.Landmarks.Add((key.TrimEnd('*'), prefix, homes));
				}
			}
			foreach (string name in Pictures.SplitList(def.Scenery))
			{
				GameObject? prefab = Pictures.Prefab(name);
				if (prefab != null)
				{
					rt.Scenery.Add(prefab);
				}
				else
				{
					missing.Add(name);
				}
			}
			foreach (string name in Pictures.SplitList(def.Logs))
			{
				GameObject? prefab = Pictures.Prefab(name);
				if (prefab != null)
				{
					rt.Logs.Add(prefab);
				}
				else
				{
					missing.Add(name);
				}
			}
			m_biomes.Add(rt);
			{
				Plugin.Log.LogInfo($"{def.Name}: monsters [{string.Join(", ", rt.Monsters.ConvertAll(m => $"{m.Prefab.name} {m.Weight:0.0} x{m.GroupMin}-{m.GroupMax}"))}], flyers [{string.Join(", ", rt.Flyers.ConvertAll(m => $"{m.Prefab.name} {m.Weight:0.0}"))}], {rt.Scenery.Count} scenery, {rt.Logs.Count} logs");
			}
		}
		if (missing.Count > 0)
		{
			Plugin.Log.LogWarning("Not found in this game version (skipped): " + string.Join(", ", missing));
		}

		GameObject? coins = Pictures.Prefab("Coins");
		if (coins != null)
		{
			m_coinTemplate = Picture(coins);
		}
		FindTerrainSources();
	}

	private static string MonsterLabel(GameObject prefab)
	{
		Character c = prefab.GetComponent<Character>();
		string name = c != null ? c.m_name : prefab.name;
		return Localization.instance != null ? Localization.instance.Localize(name) : name;
	}

	private GameObject Picture(GameObject prefab)
	{
		if (!m_pictureOf.TryGetValue(prefab, out GameObject picture) || picture == null)
		{
			picture = Pictures.MakePicture(prefab, m_templates, m_layer);
			m_pictureOf[prefab] = picture;
		}
		return picture;
	}

	private void MakeAvatar()
	{
		if (m_avatar != null || m_player == null || m_player.m_animator == null)
		{
			return;
		}
		GameObject visual = m_player.m_animator.gameObject;
		GameObject avatar = Pictures.MakePicture(visual, m_templates, m_layer);
		avatar.transform.SetParent(m_stage, false);
		avatar.transform.localPosition = new Vector3(m_x, m_y, m_z);
		avatar.transform.localRotation = Quaternion.identity;
		avatar.transform.localScale = Vector3.one;
		m_avatar = avatar;
		m_avatarAnim = avatar.GetComponent<Animator>() ?? avatar.GetComponentInChildren<Animator>();
		// Only the ones that show (hidden gear stays hidden when the blink ends).
		m_avatarRenderers = System.Array.FindAll(avatar.GetComponentsInChildren<Renderer>(true), r => r.enabled);
		m_avatarParams.Clear();
		if (m_avatarAnim != null)
		{
			foreach (AnimatorControllerParameter p in m_avatarAnim.parameters)
			{
				m_avatarParams.Add(p.nameHash);
			}
		}
	}

	// ---------------------------------------------------------------- run lifecycle

	private void ResetRun()
	{
		foreach (Thing t in m_things)
		{
			DestroyThing(t);
		}
		m_things.Clear();
		m_state = State.Countdown;
		m_stateTime = 0f;
		m_z = 0f;
		m_x = 0f;
		m_y = 0f;
		m_vy = 0f;
		m_lane = 1;
		m_slide = 0f;
		m_runTime = 0f;
		m_coins = 0;
		m_highestBiome = 0;
		m_shownBiome = -1;
		m_nextRowZ = 45f;
		m_nextSceneryZ = -20f;
		m_nextGroundZ = -GroundTile;
		m_crashText = "";
		m_rewardText = "";
		m_lastHealth = m_player.GetHealth();
		m_speed = Plugin.StartSpeed.Value;
		m_camPlaced = false;
		m_envPlaced = false;
		m_lastTick = 0;
		m_lastLandmarkZ = -999f;
		m_lives = Plugin.Lives.Value;
		m_dividers.Clear();
		m_nextTorchZ = 0f;
		m_torchCount = 0;
		DestroyBackdrop();
		m_backdropShow = 0f;
		m_hurtTime = 0f;
		m_aimTime = 0f;
		m_camBack = 0f;
		m_hint = "";
		ResetMoves();
		ResetSpears();
		if (!m_explicitStart && m_player != null)
		{
			// Again from the latest checkpoint (a boss beaten this run moves it on).
			int lap = Plugin.LoopBiomes.Value <= 0 ? m_biomes.Count : Mathf.Clamp(Plugin.LoopBiomes.Value, 1, m_biomes.Count);
			(m_startBiome, m_startStars) = Progress.Latest(m_player, Progress.AllowedBiomes(lap));
		}
		ResetSegments();
		SpawnAhead();
		PlaceAvatar(0f);
	}

	private void Crash(string why)
	{
		if (m_state != State.Running)
		{
			return;
		}
		m_state = State.Crashed;
		m_stateTime = 0f;
		m_crashText = why;
		SetTrigger("stagger");
		m_rewardText = Rewards.Grant(m_player, m_z + m_bonusDistance, m_highestBiome, m_runTime, m_biomes.ConvertAll(b => b.Def));
		Plugin.Log.LogInfo($"Run over after {m_z:0} m ({m_runTime:0} s, {m_coins} coins, biome {m_highestBiome}): {why}");
	}

	private void Quit()
	{
		if (m_state == State.Running)
		{
			Crash("You stopped for a breather.");
		}
		if (!string.IsNullOrEmpty(m_rewardText) && m_player != null)
		{
			m_player.Message(MessageHud.MessageType.Center, m_rewardText.Replace("\n", "   "));
		}
		Destroy(gameObject);
	}

	// ---------------------------------------------------------------- per frame

	private bool KeyDown(params KeyCode[] keys)
	{
		foreach (KeyCode k in keys)
		{
			if (Input.GetKeyDown(k))
			{
				return true;
			}
		}
		return false;
	}

	private void Update()
	{
		if (m_player == null || m_player.IsDead() || m_player.IsTeleporting() || Player.m_localPlayer != m_player)
		{
			Destroy(gameObject);
			return;
		}

		bool consoleVisible = Console.IsVisible();
		bool canInput = !consoleVisible && !m_consoleWasVisible && !(Chat.instance != null && Chat.instance.HasFocus());
		m_consoleWasVisible = consoleVisible;

		// Getting hurt in the real world ends the game straight away, so nobody gets eaten
		// while surfing. Health also falls when food wears off, but then it only follows the
		// shrinking max health down, so a drop that leaves health at its max isn't a hit.
		float health = m_player.GetHealth();
		if (Plugin.StopWhenHurt.Value && health < m_lastHealth - Plugin.HurtThreshold.Value
			&& health < m_player.GetMaxHealth() - 0.5f)
		{
			Plugin.Log.LogInfo($"Real-world damage: health {m_lastHealth:0.0} -> {health:0.0} (max {m_player.GetMaxHealth():0.0}).");
			Crash("Something hit you in the real world!");
			Quit();
			return;
		}
		m_lastHealth = health;

		if (canInput && Input.GetKeyDown(Plugin.ExitKey.Value))
		{
			Quit();
			return;
		}

		if (canInput && Plugin.AllowPause.Value && Input.GetKeyDown(Plugin.PauseKey.Value))
		{
			m_menuPaused = false;
			SetPaused(!m_paused);
		}
		// The game's menu (Esc) pauses too, and resumes when it closes.
		bool menu = Menu.IsVisible();
		if (menu && !m_paused)
		{
			m_menuPaused = true;
			SetPaused(true);
		}
		else if (!menu && m_paused && m_menuPaused)
		{
			m_menuPaused = false;
			SetPaused(false);
		}
		if (m_paused)
		{
			PlaceCamera(0f);
			return;
		}

		float dt = Mathf.Min(Time.deltaTime, 0.05f) * Plugin.GameSpeed.Value;
		m_stateTime += dt;
		m_bannerTime -= dt;
		if (m_bannerTime <= 0f)
		{
			m_bannerKeep = false;
		}
		m_iconPulse = Mathf.MoveTowards(m_iconPulse, 0f, dt * 2f);
		m_flash2 = Mathf.MoveTowards(m_flash2, 0f, dt * 1.6f);

		if (m_avatar == null && m_stateTime > 0.2f)
		{
			// One beat late, so the circlet has been put on the character we copy.
			MakeAvatar();
		}

		switch (m_state)
		{
			case State.Countdown:
			{
				int tick = Mathf.CeilToInt(Plugin.Countdown.Value - m_stateTime);
				if (tick != m_lastTick && tick > 0)
				{
					m_audio.Play(Sfx.Tick);
				}
				m_lastTick = tick;
				if (m_stateTime >= Plugin.Countdown.Value)
				{
					m_state = State.Running;
					m_stateTime = 0f;
					m_audio.Play(Sfx.Go);
				}
				break;
			}
			case State.Running:
				if (m_aimTime > 0f)
				{
					UpdateAim(dt, canInput);
				}
				else
				{
					UpdateRun(dt, canInput);
				}
				break;
			case State.Crashed:
				if (canInput && m_stateTime > 0.8f && KeyDown(KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter))
				{
					ResetRun();
				}
				break;
		}

		if (m_aimTime <= 0f)
		{
			// Aiming the ballista stops the run, and everything on the track with it.
			UpdateThings(dt);
		}
		UpdateRipples(dt);
		PlaceAvatar(dt);
		PlaceCamera(dt);
	}

	private bool m_paused;
	private bool m_menuPaused;
	private readonly Dictionary<Animator, float> m_pausedAnimators = new();

	// Freezes the run: animations, particle effects and sounds included.
	private void SetPaused(bool paused)
	{
		m_paused = paused;
		if (paused)
		{
			m_pausedAnimators.Clear();
			foreach (Animator a in m_stage.GetComponentsInChildren<Animator>())
			{
				m_pausedAnimators[a] = a.speed;
				a.speed = 0f;
			}
			foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>())
			{
				if (ps.isPlaying)
				{
					ps.Pause(false);
				}
			}
			AudioListener.pause = true;
		}
		else
		{
			foreach (KeyValuePair<Animator, float> kv in m_pausedAnimators)
			{
				if (kv.Key != null)
				{
					kv.Key.speed = kv.Value > 0f ? kv.Value : 1f;
				}
			}
			m_pausedAnimators.Clear();
			foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>())
			{
				if (ps.isPaused)
				{
					ps.Play(false);
				}
			}
			AudioListener.pause = false;
		}
		m_banner = paused ? $"Paused\n{Plugin.PauseKey.Value} to carry on" : "";
		m_bannerTime = paused ? 9999f : 0f;
	}

	private void LateUpdate()
	{
		DrawGrass();
		// The game may switch its HUD back on (Hud.Update); keep it hidden while we're up.
		if (m_hudRoot != null && m_hudRoot.activeSelf)
		{
			m_hudRoot.SetActive(false);
		}
	}

	// The run's stretches (SurfBoss.cs): biomes, boss arenas, and laps with stars after the last
	// biome this world allows.
	private int BiomeAt(float z) => SegAt(z).Biome;

	// 0 on the first lap, then 1, 2 and 3 stars.
	private int StarsAt(float z) => SegAt(z).Stars;

	private static float LaneX(int lane) => (lane - 1) * LaneWidth;

	private void UpdateRun(float dt, bool canInput)
	{
		m_runTime += dt;
		float biomeProgress = SpeedProgressAt(m_z);
		m_speed = Mathf.Min(Plugin.StartSpeed.Value + Plugin.SpeedPerBiome.Value * biomeProgress, Plugin.MaxSpeed.Value);
		if (m_slowTime > 0f)
		{
			m_slowTime -= dt;
			m_speed *= 0.6f;
		}
		m_speed *= UpdateMoves(dt, canInput);
		UpdateSpears(dt, canInput);

		bool grounded = m_y <= 0.001f;
		if (canInput)
		{
			// Left and right as seen on the screen (swapped while the camera faces back).
			int step = 0;
			if (KeyDown(KeyCode.A, KeyCode.LeftArrow))
			{
				step = Mirrored ? 1 : -1;
			}
			else if (KeyDown(KeyCode.D, KeyCode.RightArrow))
			{
				step = Mirrored ? -1 : 1;
			}
			int to = m_lane + step;
			if (step != 0 && to >= m_minLane && to <= m_maxLane && CanSwitch(m_lane, to))
			{
				m_audio.Play(Sfx.Lane, 0.5f, 1.15f);
				m_lane = to;
			}
			if (KeyDown(KeyCode.W, KeyCode.UpArrow, KeyCode.Space))
			{
				if (grounded)
				{
					m_vy = Mathf.Sqrt(2f * Gravity * Plugin.JumpHeight.Value);
					m_slide = 0f;
					SetTrigger("jump");
					m_audio.Play(Sfx.Jump);
				}
			}
			if (KeyDown(KeyCode.S, KeyCode.DownArrow, KeyCode.LeftControl, KeyCode.C))
			{
				if (grounded)
				{
					m_slide = Plugin.SlideTime.Value;
					m_audio.Play(Sfx.Slide);
				}
				else
				{
					m_vy = Mathf.Min(m_vy, -18f);
					m_slide = Plugin.SlideTime.Value;
				}
			}
		}

		m_vy -= Gravity * dt;
		m_y += m_vy * dt;
		if (m_y < 0f)
		{
			if (m_vy < -4f)
			{
				m_audio.Play(Sfx.Land, 0.8f);
				Splash(1f);
			}
			m_y = 0f;
			m_vy = 0f;
		}
		else if (m_y <= 0.001f)
		{
			// Footsteps, quicker as the run speeds up.
			m_stepTimer -= dt;
			if (m_stepTimer <= 0f)
			{
				m_stepTimer = Mathf.Clamp(3.2f / Mathf.Max(1f, m_speed), 0.14f, 0.4f);
				m_audio.Play(Sfx.Step(m_biomes[BiomeAt(m_z)].Def.Name), 0.35f, 1f, true);
				Splash(0.3f);
			}
		}
		if (m_y <= 0.001f)
		{
			m_slide = Mathf.Max(0f, m_slide - dt);
		}
		m_hurtTime = Mathf.Max(0f, m_hurtTime - dt);
		m_x = Mathf.MoveTowards(m_x, LaneX(m_lane), Plugin.LaneSwitchSpeed.Value * dt);
		m_z += m_speed * dt;

		Segment seg = SegAt(m_z);
		int biome = seg.Biome;
		m_highestBiome = Mathf.Max(m_highestBiome, biome);
		int shown = seg.Boss ? -1 : biome + 100 * seg.Stars;
		if (!seg.Boss && shown != m_shownBiome)
		{
			bool first = m_shownBiome == -1;
			m_shownBiome = shown;
			int stars = seg.Stars;
			string starText = stars > 0 ? $" ({stars} star{(stars > 1 ? "s" : "")})" : "";
			m_banner = first ? m_biomes[biome].Def.Name + starText : "Entering " + m_biomes[biome].Def.Name + starText + "!";
			m_bannerTime = 2.5f;
			if (!first)
			{
				m_audio.Play(Sfx.Biome);
			}
		}

		CheckCollisions();
		if (m_state != State.Running)
		{
			return;
		}
		UpdateStretch(dt);
		SpawnAhead();
		UpdateHints();
	}

	private void CheckCollisions()
	{
		bool sliding = m_slide > 0f && m_y < 0.4f;
		for (int i = m_things.Count - 1; i >= 0; i--)
		{
			Thing t = m_things[i];
			if (t.Kind == Kind.Scenery || t.Kind == Kind.Ground || !t.Collidable)
			{
				continue;
			}
			if (Mathf.Abs(t.Z - m_z) > t.HalfLength + 0.35f || Mathf.Abs(m_x - (t.FreeX ? t.X : LaneX(t.Lane))) > LaneWidth * 0.45f)
			{
				continue;
			}
			switch (t.Kind)
			{
				case Kind.Coin:
					if (m_y < 2.6f)
					{
						m_coins++;
						m_audio.Play(Sfx.Coin, 1f, 1f + 0.03f * (m_coins % 5));
						Destroy(t.Go);
						m_things.RemoveAt(i);
					}
					break;
				case Kind.Item:
				case Kind.BossHit:
					if (m_y < 2.6f)
					{
						bool hit = t.Kind == Kind.BossHit;
						Destroy(t.Go);
						m_things.RemoveAt(i);
						// These change the run (and the list), so stop looking this frame.
						if (hit && m_boss != null && m_boss.Aim)
						{
							StartAim();
						}
						else if (hit && m_boss != null && m_boss.Throw)
						{
							PickUpSpear();
						}
						else if (hit)
						{
							BossHit();
						}
						else
						{
							PickedUpItem();
						}
						return;
					}
					break;
				case Kind.Slow:
					if (m_y < 0.5f)
					{
						if (m_slowTime <= 0f)
						{
							m_banner = "Stuck in tar!";
							m_bannerTime = 1.2f;
							m_audio.Play(Sfx.Tar, 1f, 0.8f);
							Flash(new Color(0.1f, 0.08f, 0.06f), 0.5f);
						}
						m_slowTime = 1.2f;
					}
					break;
				case Kind.Block:
					if (t.ClearHeight > 0f && m_y > t.ClearHeight)
					{
						break;
					}
					if (t.JumpOrSlide && (m_y > 0.7f || sliding))
					{
						break;
					}
					m_audio.Play(Sfx.HitMonster);
					Hit(t, t.CrashText.Length > 0 ? t.CrashText : $"A {t.Label} got you in the {m_biomes[BiomeAt(m_z)].Def.Name}!");
					return;
				case Kind.Low:
					if (m_y < 0.75f)
					{
						m_audio.Play(t.CrashText.Length > 0 ? Sfx.HitMonster : Sfx.HitLog);
						Hit(t, t.CrashText.Length > 0 ? t.CrashText : $"You tripped over a log in the {m_biomes[BiomeAt(m_z)].Def.Name}!");
						return;
					}
					break;
				case Kind.High:
					if (sliding && t.Torch)
					{
						t.Torch = false;
						BurnBranch(t);
						return;
					}
					if (t.Nuisance)
					{
						if (!sliding)
						{
							BatHit(t);
						}
						break;
					}
					if (!sliding)
					{
						m_audio.Play(string.IsNullOrEmpty(t.Label) ? Sfx.HitLog : Sfx.HitMonster);
						Hit(t, t.CrashText.Length > 0 ? t.CrashText : string.IsNullOrEmpty(t.Label)
							? $"You ran into a hanging log in the {m_biomes[BiomeAt(m_z)].Def.Name}!"
							: $"A {t.Label} swooped into you in the {m_biomes[BiomeAt(m_z)].Def.Name}!");
						return;
					}
					break;
			}
		}
	}

	// A crash, unless the Invincible test setting is on: then the thing is just passed through.
	private void Hit(Thing t, string why)
	{
		// Blocks and parries work in Invincible mode too, so they can be practised.
		if (m_hurtTime <= 0f && TryBlock(t))
		{
			return;
		}
		if (Plugin.Invincible.Value)
		{
			t.Collidable = false;
			m_banner = "(invincible) " + why;
			m_bannerTime = 1.2f;
			m_bannerKeep = true;
			return;
		}
		if (m_hurtTime > 0f)
		{
			return;
		}
		// The game's red damage flash and your character's hurt sound.
		Flash(new Color(0.85f, 0.05f, 0.02f), 0.9f);
		PlayHurt();
		if (m_lives > 1)
		{
			// Three strikes and you're out: a hit costs a life and a moment to recover.
			m_lives--;
			m_hurtTime = 1.6f;
			t.Collidable = false;
			m_shake = Mathf.Max(m_shake, 0.35f);
			SetTrigger("stagger");
			m_banner = $"Ouch! {m_lives} {(m_lives == 1 ? "life" : "lives")} left";
			m_bannerTime = 1.5f;
			return;
		}
		Crash(why);
	}

	private void UpdateThings(float dt)
	{
		// While a boss chases you the camera looks back: keep what's behind in sight.
		float behind = m_z - (m_camBack > 0f || ChaseAhead() ? 90f : 25f);
		for (int i = m_things.Count - 1; i >= 0; i--)
		{
			Thing t = m_things[i];
			if (t.Go == null)
			{
				m_things.RemoveAt(i);
				continue;
			}
			if (m_state == State.Running && i == m_things.Count - 1)
			{
				UpdateSchemaWorld(dt);
				UpdateAnimWatches(dt);
			}
			if (m_state == State.Running && t.Special != Special.None)
			{
				UpdateSetPiece(t, dt);
			}
			else if (m_state == State.Running && t.Info != null)
			{
				if (!UpdateSchema(t, dt))
				{
					UpdateCreature(t, dt);
				}
			}
			if (m_state == State.Running && t.ChargeIn > 0f)
			{
				t.ChargeIn -= dt;
				if (t.ChargeIn <= 0f)
				{
					t.Speed = t.ChargeSpeed;
				}
			}
			// Positive speed comes toward you; negative (a chasing boss's attacks) catches up from behind.
			if (m_state == State.Running && t.Speed != 0f)
			{
				t.Z -= t.Speed * dt;
				Vector3 p = t.Go.transform.localPosition;
				if (t.FreeX)
				{
					t.X = Mathf.MoveTowards(t.X, t.TargetX, t.XSpeed * dt);
					p.x = t.X;
				}
				t.Go.transform.localPosition = new Vector3(p.x, RoadY(t.Z) + t.BaseY, t.Z);
			}
			if (t.Kind == Kind.Coin || t.Kind == Kind.Item || t.Kind == Kind.BossHit)
			{
				t.Go.transform.Rotate(0f, 180f * dt, 0f, Space.World);
			}
			if (m_state == State.Running && t.Kind == Kind.Block && t.Moving != null)
			{
				UpdateStrike(t, dt);
			}
			if (t.Life > 0f && m_state == State.Running)
			{
				t.Life -= dt;
				if (t.Life <= 0f)
				{
					DestroyThing(t);
					m_things.RemoveAt(i);
					continue;
				}
			}
			float end = t.Kind == Kind.Ground ? t.Z + GroundTile * 0.5f : t.Z + t.HalfLength;
			if (end < behind)
			{
				DestroyThing(t);
				m_things.RemoveAt(i);
			}
		}
	}

	// ---------------------------------------------------------------- spawning

	private void SpawnAhead()
	{
		float horizon = m_z + AheadDistance;
		while (m_nextGroundZ < horizon + GroundTile)
		{
			SpawnGround(m_nextGroundZ + GroundTile * 0.5f);
			m_nextGroundZ += GroundTile;
		}
		while (m_nextSceneryZ < horizon + 40f)
		{
			SpawnScenery(m_nextSceneryZ, -1);
			SpawnScenery(m_nextSceneryZ + Random.Range(0f, 3f), 1);
			MaybePickable(m_nextSceneryZ + Random.Range(0f, 3f), Random.value < 0.5f ? -1 : 1);
			m_nextSceneryZ += Random.Range(3.5f, 8f) / Mathf.Max(0.2f, Plugin.SceneryDensity.Value);
		}
		SpawnArenaTorches(horizon);
		while (m_nextRowZ < horizon)
		{
			if (SegAt(m_nextRowZ).Boss)
			{
				// The boss lays out its own attacks.
				m_nextRowZ += 10f;
				continue;
			}
			SpawnRow(m_nextRowZ);
			float difficulty = Mathf.Clamp01(ProgressAt(m_nextRowZ) / m_biomes.Count);
			m_nextRowZ += Mathf.Lerp(30f, 16f, difficulty) + Random.Range(0f, 9f);
		}
	}

	private void SpawnGround(float centerZ)
	{
		BiomeRuntime b = m_biomes[BiomeAt(centerZ)];
		float trackWidth = LaneWidth * 3f + 0.8f;
		GameObject root = new GameObject("Ground");
		root.layer = m_layer;
		root.transform.SetParent(m_stage, false);
		root.transform.localPosition = new Vector3(0f, 0f, centerZ);
		GameObject track = new GameObject("Track");
		track.layer = m_layer;
		track.transform.SetParent(root.transform, false);
		track.AddComponent<MeshFilter>().sharedMesh = StripMesh(centerZ, trackWidth * 0.5f, 2f, 1, 0f, true);
		MeshRenderer tr = track.AddComponent<MeshRenderer>();
		tr.sharedMaterial = m_terrainTrack ?? m_trackMaterial ?? b.Ground;
		tr.receiveShadows = true;
		if (b.Water && m_waterMaterial != null)
		{
			GameObject water = new GameObject("Water");
			water.layer = m_layer;
			water.transform.SetParent(root.transform, false);
			water.AddComponent<MeshFilter>().sharedMesh = StripMesh(centerZ, TrackHalf + LandWidth, 4f, 8, WaterDepth, false);
			MeshRenderer wr = water.AddComponent<MeshRenderer>();
			wr.sharedMaterial = m_waterMaterial;
			wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		}
		foreach (int side in new[] { -1, 1 })
		{
			GameObject land = new GameObject("Land");
			land.layer = m_layer;
			land.transform.SetParent(root.transform, false);
			land.AddComponent<MeshFilter>().sharedMesh = HillMesh(centerZ, side, out LandGrid grid);
			MeshRenderer r = land.AddComponent<MeshRenderer>();
			r.sharedMaterial = m_terrainLand ?? b.Ground;
			ApplyLandMask(land, r, grid);
			SpawnGrass(root, grid);
			r.receiveShadows = true;
		}
		m_things.Add(new Thing { Go = root, Kind = Kind.Ground, Z = centerZ, HalfLength = GroundTile * 0.5f });
	}

	// Blends from the previous stretch's biome over the first 40 m of a stretch, so nothing
	// jumps at a border (a stretch's end isn't known ahead while its boss items are missing).
	private void BiomeBlend(float z, out int a, out int b, out float blend)
	{
		Segment s = SegAt(z);
		a = s.PrevBiome;
		b = s.Biome;
		blend = Mathf.Clamp01((z - s.Start) / 40f);
	}

	private float LevelAt(float z)
	{
		Segment s = SegAt(z);
		return Mathf.Lerp(s.PrevLevel, s.Level, Mathf.Clamp01((z - s.Start) / 40f));
	}

	private float HillAmplitude(float z)
	{
		BiomeBlend(z, out int a, out int b, out float blend);
		return Mathf.Lerp(s_hills[Mathf.Min(a, s_hills.Length - 1)], s_hills[Mathf.Min(b, s_hills.Length - 1)], blend) * Plugin.HillHeight.Value;
	}

	private float WaterWeight(float z)
	{
		if (m_biomes.Count == 0)
		{
			return 0f;
		}
		BiomeBlend(z, out int a, out int b, out float blend);
		return Mathf.Lerp(m_biomes[a].Water ? 1f : 0f, m_biomes[b].Water ? 1f : 0f, blend);
	}

	// The road rolls up and down, gently in the Meadows and more with every biome (calmer in the
	// Swamp so the water lies flat-ish).
	private float RoadY(float z)
	{
		float biomes = LevelAt(z);
		float amplitude = (0.4f + 0.55f * biomes) * Mathf.Lerp(1f, 0.35f, WaterWeight(z)) * Plugin.RoadRoll.Value * ArenaFlatten(z);
		float n = (Mathf.PerlinNoise(z * 0.011f + 3.1f, 7.3f) - 0.5f) * 2f + (Mathf.PerlinNoise(z * 0.037f + 11f, 1.9f) - 0.5f) * 0.5f;
		return amplitude * n;
	}

	// Ground height beside the track (SurfTerrain.cs): this world's real ground for the biome, or
	// the minigame's own rolling hills where no spot was found.
	private float HillHeight(float x, float z) => LandAt(x, z, out _, out _);

	// A flat strip that follows the road: the track itself, or the swamp water's surface.
	// The game's terrain shader picks each biome's ground textures from the vertex colour
	// (Heightmap.GetBiomeColor), so our meshes carry it too, blended at biome borders.
	private Color32[] BiomeColors(Vector3[] verts, float centerZ)
	{
		Color32[] colors = new Color32[verts.Length];
		for (int i = 0; i < verts.Length; i++)
		{
			BiomeBlend(centerZ + verts[i].z, out int a, out int b, out float blend);
			Color32 ca = Heightmap.GetBiomeColor(SpawnTables.s_order[Mathf.Min(a, SpawnTables.s_order.Length - 1)]);
			Color32 cb = Heightmap.GetBiomeColor(SpawnTables.s_order[Mathf.Min(b, SpawnTables.s_order.Length - 1)]);
			colors[i] = Color32.Lerp(ca, cb, blend);
		}
		return colors;
	}

	private Mesh StripMesh(float centerZ, float halfWidth, float step, int across, float lift, bool trackUv)
	{
		int nz = Mathf.CeilToInt(GroundTile / step) + 1;
		int nx = across + 1;
		Vector3[] verts = new Vector3[nx * nz];
		Vector2[] uvs = new Vector2[verts.Length];
		for (int iz = 0; iz < nz; iz++)
		{
			float lz = -GroundTile * 0.5f + GroundTile * iz / (nz - 1);
			float y = RoadY(centerZ + lz) + lift;
			for (int ix = 0; ix < nx; ix++)
			{
				float x = -halfWidth + 2f * halfWidth * ix / across;
				verts[iz * nx + ix] = new Vector3(x, y, lz);
				uvs[iz * nx + ix] = trackUv ? new Vector2((x / halfWidth + 1f) * 0.5f, (centerZ + lz) / 4f) : new Vector2(x / 10f, (centerZ + lz) / 10f);
			}
		}
		// One face only: back faces sharing these vertices cancel out the normals (black track
		// in 1.4.10/11).
		int[] tris = new int[(nx - 1) * (nz - 1) * 6];
		int k = 0;
		for (int iz = 0; iz < nz - 1; iz++)
		{
			for (int ix = 0; ix < nx - 1; ix++)
			{
				int a = iz * nx + ix;
				int b = a + 1;
				int c = a + nx;
				int d = c + 1;
				tris[k++] = a; tris[k++] = c; tris[k++] = d;
				tris[k++] = a; tris[k++] = d; tris[k++] = b;
			}
		}
		Mesh mesh = new Mesh { name = "SurfStrip" };
		mesh.vertices = verts;
		mesh.uv = uvs;
		mesh.colors32 = BiomeColors(verts, centerZ);
		mesh.triangles = tris;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return mesh;
	}

	private const float LandWidth = 90f;
	private const float LandStep = 3f;

	private Mesh HillMesh(float centerZ, int side, out LandGrid grid)
	{
		int nx = Mathf.CeilToInt(LandWidth / LandStep) + 1;
		int nz = Mathf.CeilToInt(GroundTile / LandStep) + 1;
		grid = new LandGrid { Nx = nx, Nz = nz, Side = side, CenterZ = centerZ, Y = new float[nx * nz], H = new float[nx * nz], Veg = new float[nx * nz] };
		bool maskUv = m_terrainLand != null && System.Array.Exists(m_sources, src => src != null);
		Vector3[] verts = new Vector3[nx * nz];
		Vector2[] uvs = new Vector2[verts.Length];
		for (int iz = 0; iz < nz; iz++)
		{
			float lz = -GroundTile * 0.5f + GroundTile * iz / (nz - 1);
			for (int ix = 0; ix < nx; ix++)
			{
				int i = iz * nx + ix;
				float x = GridX(side, ix);
				float y = LandAt(x, centerZ + lz, out float h, out float veg);
				grid.Y[i] = y;
				grid.H[i] = h;
				grid.Veg[i] = veg;
				verts[i] = new Vector3(x, y, lz);
				// With the real paint mask, uv0 addresses its texels, as on a Heightmap.
				uvs[i] = maskUv ? new Vector2((ix + 0.5f) / nx, (iz + 0.5f) / nz) : new Vector2(x / 10f, (centerZ + lz) / 10f);
			}
		}
		int[] tris = new int[(nx - 1) * (nz - 1) * 6];
		int k = 0;
		for (int iz = 0; iz < nz - 1; iz++)
		{
			for (int ix = 0; ix < nx - 1; ix++)
			{
				int a = iz * nx + ix;
				int b = a + 1;
				int c = a + nx;
				int d = c + 1;
				// Wind so the faces point up on both sides of the track.
				if (side > 0)
				{
					tris[k++] = a; tris[k++] = c; tris[k++] = b;
					tris[k++] = b; tris[k++] = c; tris[k++] = d;
				}
				else
				{
					tris[k++] = a; tris[k++] = b; tris[k++] = c;
					tris[k++] = b; tris[k++] = d; tris[k++] = c;
				}
			}
		}
		Mesh mesh = new Mesh { name = "SurfHills" };
		mesh.vertices = verts;
		mesh.uv = uvs;
		mesh.colors32 = BiomeColors(verts, centerZ);
		mesh.triangles = tris;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return mesh;
	}

	private void DestroyThing(Thing t)
	{
		ClearMarkers(t);
		if (t.Go == null)
		{
			return;
		}
		if (t.Kind == Kind.Ground)
		{
			foreach (MeshFilter f in t.Go.GetComponentsInChildren<MeshFilter>())
			{
				if (f.sharedMesh != null && f.sharedMesh != m_cube)
				{
					Destroy(f.sharedMesh);
				}
			}
		}
		Destroy(t.Go);
	}

	private void SpawnScenery(float z, int side)
	{
		BiomeRuntime b = m_biomes[BiomeAt(z)];
		if (b.Scenery.Count == 0)
		{
			return;
		}
		GameObject prefab = b.Scenery[Random.Range(0, b.Scenery.Count)];
		GameObject go = Instantiate(Picture(prefab), m_stage, false);
		float x = side * (LaneWidth * 1.5f + Random.Range(3f, 26f));
		go.transform.localPosition = new Vector3(x, HillHeight(x, z) - 0.3f, z);
		go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
		go.transform.localScale = Vector3.one * Random.Range(0.85f, 1.15f);
		// Big rocks can be 20 m across: push anything that reaches over the track back out,
		// with a margin so it never blocks the view of the lanes.
		if (Pictures.MeshBounds(go, out Bounds bounds))
		{
			float inner = (side < 0 ? bounds.max.x : bounds.min.x) - m_stage.position.x;
			float limit = side * (TrackHalf + 1.5f + ExtraFlat(z));
			float push = side < 0 ? Mathf.Min(0f, limit - inner) : Mathf.Max(0f, limit - inner);
			if (push != 0f)
			{
				Vector3 p = go.transform.localPosition;
				p.x += push;
				p.y = HillHeight(p.x, z) - 0.3f;
				go.transform.localPosition = p;
			}
		}
		m_things.Add(new Thing { Go = go, Kind = Kind.Scenery, Z = z, HalfLength = 2f });
	}

	// Puts a picture in a world-aligned wrapper and scales the wrapper to fit `size`
	// (per axis, or uniformly within it), standing on y = 0 of the wrapper.
	// Creatures stand on their root (a character's pivot is at its feet); the render bounds of an
	// unanimated copy are the bind pose, which floats or sinks them.
	private GameObject Creature(GameObject prefab, Quaternion rotation, float scale)
	{
		GameObject wrapper = new GameObject(prefab.name + "_Fit");
		wrapper.layer = m_layer;
		wrapper.transform.SetParent(m_stage, false);
		GameObject go = Instantiate(Picture(prefab), wrapper.transform, false);
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = rotation;
		wrapper.transform.localScale = Vector3.one * scale;
		// The game keeps this set for anything standing; some attack transitions need it.
		Animator anim = go.GetComponentInChildren<Animator>();
		if (anim != null && !CreatureInfo.Of(prefab).Flying)
		{
			SetParam(anim, "onGround", true);
		}
		return wrapper;
	}

	// Real size from the collider, only the very biggest shrunk to Monster Height.
	private static float RealScale(CreatureInfo info) => info.Height > 0.3f ? Mathf.Min(1f, Plugin.MonsterHeight.Value / info.Height) : 1f;

	private GameObject Fitted(GameObject template, Quaternion rotation, Vector3 size, bool uniform, float maxScale = float.MaxValue)
	{
		GameObject wrapper = new GameObject(template.name + "_Fit");
		wrapper.layer = m_layer;
		wrapper.transform.SetParent(m_stage, false);
		GameObject go = Instantiate(template, wrapper.transform, false);
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = rotation;
		if (Pictures.MeshBounds(go, out Bounds bounds) && bounds.size.x > 0.01f && bounds.size.y > 0.01f && bounds.size.z > 0.01f)
		{
			Vector3 scale = new Vector3(size.x / bounds.size.x, size.y / bounds.size.y, size.z / bounds.size.z);
			if (uniform)
			{
				float s = Mathf.Min(maxScale, Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z)));
				scale = Vector3.one * s;
			}
			Vector3 offset = wrapper.transform.position - bounds.center;
			offset.y = wrapper.transform.position.y - bounds.min.y;
			go.transform.position += offset;
			wrapper.transform.localScale = scale;
		}
		return wrapper;
	}

	private void SpawnRow(float z)
	{
		BiomeRuntime giants = m_biomes[BiomeAt(z)];
		(GameObject prefab, string label) giant = giants.Giants.Count > 0 ? giants.Giants[Random.Range(0, giants.Giants.Count)] : (null!, "");
		// Ambushers (abominations) burst out of the ground over the track; trolls either swing
		// beside it or leap onto it. Ambushers are rarer (1.14, Sam: a swamp of abominations).
		bool rise = giant.prefab != null && SpawnTables.Contains(Plugin.Ambushers.Value, giant.prefab);
		if (giant.prefab != null && z > 60f && Random.value < Plugin.GiantChance.Value * (rise ? 0.4f : 1f))
		{
			if (!rise && Random.value < 0.5f)
			{
				SpawnSwingTroll(z, giants, giant);
			}
			else
			{
				SpawnStraddleTroll(z, giants, giant, rise);
			}
			m_nextRowZ += 8f;
			return;
		}
		BiomeRuntime b = m_biomes[BiomeAt(z)];
		int biomeIndex = BiomeAt(z);
		float difficulty = Mathf.Clamp01(ProgressAt(z) / m_biomes.Count);
		int blocked = Random.value < 0.3f + 0.4f * difficulty ? 2 : 1;
		List<int> lanes = new() { 0, 1, 2 };
		for (int i = 0; i < lanes.Count; i++)
		{
			int j = Random.Range(i, lanes.Count);
			(lanes[i], lanes[j]) = (lanes[j], lanes[i]);
		}

		bool formation = false;
		for (int i = 0; i < 3 && !formation; i++)
		{
			int lane = lanes[i];
			if (i >= blocked)
			{
				if (MaybeSpawnItem(lane, z))
				{
					continue;
				}
				if (m_coinTemplate != null && Random.value < 0.45f)
				{
					for (int k = -2; k <= 2; k++)
					{
						SpawnCoin(lane, z + k * 2.2f, 0.9f);
					}
				}
				continue;
			}

			float roll = Random.value;
			Kind kind = roll < 0.5f ? Kind.Block : roll < 0.78f ? Kind.Low : Kind.High;
			if (kind == Kind.Block && b.Monsters.Count == 0)
			{
				kind = Kind.Low;
			}
			if (kind != Kind.Block && b.Logs.Count == 0 && (kind == Kind.Low || b.Flyers.Count == 0))
			{
				kind = b.Monsters.Count > 0 ? Kind.Block : Kind.Coin;
			}
			switch (kind)
			{
				case Kind.Block:
				{
					SpawnEntry e = SpawnTables.Pick(b.Monsters)!;
					MaybeLandmark(e.Prefab, z);
					// Swarms, herds, lines, crossers, chasers and helpers lay out the whole row.
					if (SpawnFormation(e, lane, z, biomeIndex, i))
					{
						formation = i == 0;
						break;
					}
					SpawnCreature(e, lane, z, Kind.Block, 0f, biomeIndex);
					// Pack animals (greydwarfs, wolves...) come in their groups. 1.14 (Sam): not a
					// conga line in one lane but spread out, each a few strides on and, when it's the
					// only creature lane in this row, weaving into the next lane, so you have to weave
					// too. With two creature lanes they keep to their own lane, spaced out, so a way
					// through always stays open.
					if (e.GroupMax > 1 && Random.value < 0.75f)
					{
						int extra = Mathf.Min(Random.Range(e.GroupMin, e.GroupMax + 1), 3) - 1;
						int at = lane;
						float zk = z;
						for (int k = 1; k <= extra; k++)
						{
							zk += Random.Range(6.5f, 10f);
							if (blocked == 1 && Random.value < 0.7f)
							{
								at = at == 1 ? (Random.value < 0.5f ? 0 : 2) : 1;
							}
							SpawnCreature(e, at, zk, Kind.Block, 0f, biomeIndex);
						}
						m_nextRowZ = Mathf.Max(m_nextRowZ, zk - 10f);
					}
					break;
				}
				case Kind.Low:
				{
					GameObject log = b.Logs[Random.Range(0, b.Logs.Count)];
					GameObject go = Fitted(Picture(log), Quaternion.Euler(0f, 0f, 90f), new Vector3(LaneWidth * 0.9f, 0.7f, 0.7f), false);
					go.transform.localPosition = new Vector3(LaneX(lane), RoadY(z), z);
					m_things.Add(new Thing { Go = go, Kind = Kind.Low, Lane = lane, Z = z, HalfLength = 0.35f });
					if (m_coinTemplate != null && Random.value < 0.35f)
					{
						for (int k = -2; k <= 2; k++)
						{
							SpawnCoin(lane, z + k * 1.6f, 1.0f + 1.5f * (1f - k * k / 4f));
						}
					}
					break;
				}
				case Kind.High:
				{
					if (b.Flyers.Count > 0 && (b.Logs.Count == 0 || Random.value < 0.5f))
					{
						SpawnEntry fe = SpawnTables.Pick(b.Flyers)!;
						if (IsBat(fe.Prefab))
						{
							SpawnBatSwarm(fe, lane, z, biomeIndex);
						}
						else
						{
							FlyerSchema(SpawnCreature(fe, lane, z, Kind.High, 1.35f, biomeIndex));
						}
					}
					else
					{
						GameObject log = b.Logs[Random.Range(0, b.Logs.Count)];
						GameObject go = Fitted(Picture(log), Quaternion.Euler(0f, 0f, 90f), new Vector3(LaneWidth, 0.6f, 0.6f), false);
						go.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + 1.3f, z);
						// Two posts so the log doesn't just float.
						foreach (float side in new[] { -1f, 1f })
						{
							GameObject post = Fitted(Picture(log), Quaternion.identity, new Vector3(0.3f, 1.9f, 0.3f), false);
							post.transform.SetParent(go.transform, true);
							post.transform.position = go.transform.position + new Vector3(side * LaneWidth * 0.48f, -1.3f, 0f);
						}
						m_things.Add(new Thing { Go = go, Kind = Kind.High, Lane = lane, Z = z, HalfLength = 0.35f });
					}
					break;
				}
			}
		}
	}

	// A troll beside the track holds a log up; when you get close it slams it down across the
	// outer lane on its side, which then stays blocked.
	private void SpawnSwingTroll(float z, BiomeRuntime b, (GameObject prefab, string label) giant)
	{
		(GameObject prefab, string label) = giant;
		int side = Random.value < 0.5f ? -1 : 1;
		GameObject root = new GameObject("SwingTroll");
		root.layer = m_layer;
		root.transform.SetParent(m_stage, false);
		root.transform.localPosition = new Vector3(0f, RoadY(z), z);

		float trollX = side * (LaneWidth * 1.5f + 2.4f);
		// Mostly the troll's own attack with its own weapon; now and then the classic stiff log
		// swing from 1.2, which Sam asked to keep, done by a young (small) troll.
		CreatureInfo info = CreatureInfo.Of(prefab);
		CreatureAttack? attack = Random.value < 0.3f ? null : info.PickMelee("swing");
		const float young = 0.6f;
		// Facing the track and a little toward the runner.
		GameObject troll = Creature(prefab, Quaternion.Euler(0f, side < 0 ? 120f : 240f, 0f), RealScale(info) * (attack == null ? young : 1f));
		troll.transform.SetParent(root.transform, false);
		troll.transform.localPosition = new Vector3(trollX, 0f, 0f);
		Animator anim = troll.GetComponentInChildren<Animator>();
		bool armed = attack != null && Arm(troll, info, attack, anim);
		Transform? moving = null;
		if (!armed)
		{
			if (attack != null)
			{
				// Its own weapon didn't fit after all: the young troll's club instead.
				troll.transform.localScale *= young;
			}
			attack = null;
			moving = ClassicClub(root.transform, trollX, side, b, young);
		}

		int lane = side < 0 ? 0 : 2;
		m_things.Add(new Thing
		{
			Go = root,
			Kind = Kind.Block,
			Lane = lane,
			Z = z,
			HalfLength = 0.7f,
			Label = label,
			CrashText = $"A {label} clubbed you flat in the {b.Def.Name}!",
			Collidable = false,
			Special = Special.Swing,
			Moving = moving,
			Side = side,
			Anim = anim,
			Attack = attack,
		});
	}

	private Transform ClassicClub(Transform root, float trollX, int side, BiomeRuntime b, float size)
	{
		GameObject pivot = new GameObject("ClubPivot");
		pivot.layer = m_layer;
		pivot.transform.SetParent(root, false);
		// Held at the (young) troll's shoulder; the log keeps its length so it still reaches.
		pivot.transform.localPosition = new Vector3(trollX - side * 0.7f * size, Mathf.Max(1.6f, 2.7f * size), 0f);
		GameObject log = b.Logs.Count > 0 ? b.Logs[Random.Range(0, b.Logs.Count)] : m_biomes[1].Logs.Count > 0 ? m_biomes[1].Logs[0] : null!;
		if (log != null)
		{
			GameObject club = Fitted(Picture(log), Quaternion.identity, new Vector3(0.55f, 4.4f, 0.55f), false);
			club.transform.SetParent(pivot.transform, false);
			club.transform.localPosition = Vector3.zero;
		}
		pivot.transform.localRotation = Quaternion.Euler(0f, 0f, side * -25f);
		return pivot.transform;
	}

	// A troll jumps down over the track, legs planted in both outer lanes: the only way through
	// is sliding between its legs in the middle lane.
	private void SpawnStraddleTroll(float z, BiomeRuntime b, (GameObject prefab, string label) giant, bool rise)
	{
		(GameObject prefab, string label) = giant;
		GameObject root = new GameObject("StraddleTroll");
		root.layer = m_layer;
		root.transform.SetParent(m_stage, false);
		root.transform.localPosition = new Vector3(0f, RoadY(z), z);

		GameObject body = new GameObject("Body");
		body.layer = m_layer;
		body.transform.SetParent(root.transform, false);
		int side = Random.value < 0.5f ? -1 : 1;
		body.transform.localPosition = rise ? new Vector3(0f, -5f, 0f) : new Vector3(side * StraddleStartX, 0f, 0f);
		CreatureInfo info = CreatureInfo.Of(prefab);
		// A troll is sized so it spans the track. An ambusher (abomination) comes at its real
		// size, feet on the road: its unanimated picture's bounds (lying in a heap) put it too
		// small and floating.
		GameObject troll = rise
			? Creature(prefab, Quaternion.Euler(0f, 180f, 0f), RealScale(info))
			: Fitted(Picture(prefab), Quaternion.Euler(0f, 180f, 0f), new Vector3(LaneWidth * 3f + 0.4f, 1000f, 1000f), true);
		troll.transform.SetParent(body.transform, false);
		troll.transform.localPosition = Vector3.zero;
		Animator anim = troll.GetComponentInChildren<Animator>();
		if (anim != null)
		{
			SetParam(anim, "onGround", true);
		}
		CreatureAttack? slam = info.PickMelee("slam");

		string legs = $"The {label} stomped you in the {b.Def.Name}!";
		m_things.Add(new Thing
		{
			Go = root,
			Kind = Kind.Block,
			Lane = 0,
			Z = z,
			HalfLength = 0.9f,
			Label = label,
			CrashText = legs,
			Special = Special.Drop,
			Moving = body.transform,
			Side = side,
			Anim = anim,
			Attack = slam,
			Info = info,
			Rise = rise,
		});
		foreach ((int lane, Kind kind, string text) in new[]
		{
			(2, Kind.Block, legs),
			(1, Kind.High, $"You didn't slide under the {label}'s legs!"),
		})
		{
			GameObject marker = new GameObject("TrollLane");
			marker.transform.SetParent(m_stage, false);
			marker.transform.localPosition = new Vector3(LaneX(lane), 0f, z);
			m_things.Add(new Thing { Go = marker, Kind = kind, Lane = lane, Z = z, HalfLength = 0.9f, Label = label, CrashText = text });
		}
	}

	private void UpdateSetPiece(Thing t, float dt)
	{
		float distance = t.Z - m_z;
		bool swing = t.Special == Special.Swing;
		if (distance < -3f && t.Markers.Count > 0)
		{
			ClearMarkers(t);
		}
		// The real attack animation winds up slower than the classic stiff swing.
		bool real = swing && t.Moving == null;
		// The real swing lands when its clip's blow does.
		float windup = real ? AnimTiming.Delay(t.Anim, t.Attack?.Animation ?? "", 0.8f) : 0f;
		if (!t.Triggered)
		{
			// 1.14 (Sam): the real log swing landed too late, and the leap onto the track (and its
			// red lanes) came too early. The log now lands a moment before you get there (it stays
			// down in its lane), the leap lands under a second ahead of you, and its lanes light
			// up about a second and a half before that.
			float trigger = !swing ? m_speed * 0.95f + 3f : real ? m_speed * (windup + 0.3f) + 2.5f : m_speed * 0.5f + 2.5f;
			if (t.Markers.Count == 0 && distance < trigger + (swing ? m_speed * 1.3f + 4f : m_speed * 0.6f + 2f))
			{
				if (swing)
				{
					t.Markers.Add(MakeMarker(t.Lane, t.Z, 2.6f, m_markerMaterial));
				}
				else
				{
					t.Markers.Add(MakeMarker(0, t.Z, 2.6f, m_markerMaterial));
					t.Markers.Add(MakeMarker(2, t.Z, 2.6f, m_markerMaterial));
					// The way through: slide under it in the middle.
					t.Markers.Add(MakeMarker(1, t.Z, 2.6f, SlideMaterial));
				}
			}
			if (distance > trigger)
			{
				return;
			}
			t.Triggered = true;
			if (real && t.Anim != null && t.Attack != null)
			{
				t.FireIn = windup;
				TriggerAttack(t);
			}
			else if (t.Rise)
			{
				string wake = t.Info != null && t.Info.WakeSound.Length > 0 ? t.Info.WakeSound : t.Info?.AlertSound ?? "";
				m_audio.Play(wake.Length > 0 ? wake : Sfx.TrollSlam);
				m_shake = Mathf.Max(m_shake, 0.3f);
			}
			else
			{
				m_audio.Play(swing ? Sfx.ClubSwing : Sfx.TrollStep);
			}
		}
		if (t.Progress >= 1f)
		{
			return;
		}
		float duration = !swing ? 0.5f : real ? 2f * Mathf.Max(0.2f, windup) : 0.22f;
		t.Progress = Mathf.Min(1f, t.Progress + dt / duration);
		float e = t.Progress * t.Progress;
		if (swing)
		{
			if (t.Moving != null)
			{
				t.Moving.localRotation = Quaternion.Euler(0f, 0f, t.Side * Mathf.Lerp(-25f, 128f, e));
			}
			if (real)
			{
				if (t.FireIn >= 0f)
				{
					t.FireIn -= dt;
					if (t.FireIn < 0f)
					{
						t.Collidable = true;
						m_audio.Play(Sfx.TrollSlam, Mathf.Clamp01(1.1f - Mathf.Abs(distance) / 30f));
					}
				}
			}
			else if (t.Progress >= 0.5f)
			{
				t.Collidable = true;
			}
		}
		else if (t.Moving != null && t.Rise)
		{
			// Bursting up out of the ground, overshooting a little.
			float p = t.Progress;
			t.Moving.localPosition = new Vector3(0f, Mathf.Lerp(-5f, 0f, p) + 0.6f * Mathf.Sin(p * Mathf.PI), 0f);
		}
		else if (t.Moving != null)
		{
			// A leap from beside the track onto it.
			float p = t.Progress;
			t.Moving.localPosition = new Vector3(t.Side * StraddleStartX * (1f - p), 5f * Mathf.Sin(p * Mathf.PI), 0f);
		}
		if (t.Progress >= 1f)
		{
			m_audio.Play(Sfx.TrollSlam);
			if (!swing && t.Anim != null && t.Attack != null)
			{
				TriggerAttack(t, t.Rise);
			}
		}
	}

	// A lane monster (or flyer) with one of its own attacks: melee ones swing as you come close,
	// ranged ones shoot down the track toward the lane you're in. They idle until they notice
	// you; then fast creatures charge, slow ones walk, big ones stomp in.
	private Thing SpawnCreature(SpawnEntry entry, int lane, float z, Kind kind, float baseY, int biomeIndex, string role = "")
	{
		GameObject prefab = entry.Prefab;
		string label = entry.Label;
		CreatureInfo info = CreatureInfo.Of(prefab);
		Schema? schema = Schemas.For(prefab);
		// The attack it does this time, out of its schema's alternatives.
		string move = schema?.PickOf("sweep", "gas2", "gas3", "smash", "slams", "lob", "fire", "tar", "bomb", "tickdrop", "shooter", "flyshooter") ?? "";
		int smashes = kind == Kind.Block ? (schema != null ? (move == "smash" ? 1 : move == "slams" ? 3 : 0) : SmashCount(prefab)) : 0;
		bool wantRanged = schema != null
			? ((move == "shooter" && Random.value < 0.6f) || move == "flyshooter") && Plugin.RangedAttacks.Value
			: info.Ranged.Count > 0 && Plugin.RangedAttacks.Value && (info.Melee.Count == 0 || Random.value < 0.6f);
		CreatureAttack? attack = smashes > 0 && info.Melee.Count > 0
			? PickSmash(info)
			: wantRanged && info.Ranged.Count > 0
				? info.PickRanged()
				: move == "lob" || move == "fire" || move == "tar" || move == "bomb"
					? info.PickRanged() ?? info.PickMelee()
					: info.PickMelee() ?? (schema != null && info.Melee.Count == 0 ? info.PickRanged() : null);
		if (attack == null || attack.Ranged)
		{
			smashes = 0;
		}
		bool swoop = kind == Kind.High && SpawnTables.Contains(Plugin.Swoopers.Value, prefab);
		bool ambush = kind == Kind.Block && SpawnTables.Contains(Plugin.Ambushers.Value, prefab);

		// Real size from the creature's collider; only the very biggest are shrunk.
		float scale = RealScale(info);
		GameObject go = Creature(prefab, Quaternion.Euler(0f, 180f, 0f), scale);
		float x = LaneX(lane);
		float y = baseY;
		int side = Random.value < 0.5f ? -1 : 1;
		if (swoop)
		{
			// Waits off to the side and above, then dives across the lanes.
			x = side * (TrackHalf + 7f);
			y = 4.5f;
		}
		go.transform.localPosition = new Vector3(x, RoadY(z) + y, z);
		Animator anim = go.GetComponentInChildren<Animator>();
		if (attack != null)
		{
			Arm(go, info, attack, anim);
		}
		float height = info.Height > 0.3f ? info.Height * scale : (Pictures.MeshBounds(go, out Bounds body) ? body.size.y : 2f);
		// Anything shorter than about three quarters of a jump can be jumped over.
		float clear = kind == Kind.Block && height < Plugin.JumpHeight.Value * 0.75f ? height * 0.85f : 0f;

		bool ranged = attack != null && attack.Ranged;
		bool stomper = schema != null ? schema.Has("stomper") : !ranged && height > 3f;
		if (schema != null && schema.Has("hurdle") && kind == Kind.Block)
		{
			// Short enough to jump, whatever its collider says.
			clear = Mathf.Min(height * 0.85f, Plugin.JumpHeight.Value * 0.6f);
		}
		string gait = role.Length > 0 ? role : schema?.PickOf("charger", "waiter", "switcher") ?? "";
		float speed = 0f;
		float pace = 0f;
		if (gait == "charger" || gait == "switcher")
		{
			pace = info.Fast ? info.RunSpeed : Mathf.Max(info.RunSpeed * 0.8f, info.WalkSpeed);
			speed = pace * (info.Fast ? 0.6f : 0.75f);
		}
		else if (gait == "waiter" || gait == "still")
		{
		}
		else if (stomper || (!ranged && Random.value < (info.Fast ? 0.7f : 0.45f)))
		{
			pace = info.Fast ? info.RunSpeed : stomper ? Mathf.Max(info.RunSpeed, info.WalkSpeed) : info.WalkSpeed;
			speed = pace * (info.Fast ? 0.6f : 0.8f);
		}
		else if (ranged && Random.value < 0.3f)
		{
			pace = info.WalkSpeed;
			speed = pace * 0.5f;
		}
		speed = Mathf.Min(speed * Mathf.Clamp(0.5f + 0.12f * biomeIndex, 0.5f, 1.2f) * Plugin.MonsterMove.Value, 9f);
		if (anim != null)
		{
			SetParam(anim, "forward_speed", 0f);
			if (!info.Flying)
			{
				SetParam(anim, "onGround", true);
			}
			if (ambush)
			{
				SetParam(anim, "sleeping", true);
			}
			if (Plugin.DebugLog.Value && m_loggedTriggers.Add(prefab))
			{
				List<string> triggers = new();
				foreach (AnimatorControllerParameter p in anim.parameters)
				{
					triggers.Add($"{p.name}:{p.type}");
				}
				Plugin.Log.LogInfo($"{prefab.name} animator parameters: {string.Join(", ", triggers)}");
			}
		}
		Thing thing = new Thing
		{
			Go = go,
			Kind = kind,
			Lane = lane,
			Z = z,
			BaseY = y,
			HalfLength = kind == Kind.Block ? 0.9f : 0.8f,
			MoveSpeed = speed,
			Pace = speed > 0.1f ? pace : 0f,
			Label = label,
			Anim = anim,
			Attack = attack,
			Info = info,
			ClearHeight = clear,
			Cooldown = Random.Range(0f, 0.8f),
			Height = height,
			Swoop = swoop,
			Sleeping = ambush,
			Stomper = stomper,
			Smashes = smashes,
			FreeX = swoop,
			X = x,
			TargetX = x,
			Side = side,
			// A swooper isn't in any lane until it dives.
			Collidable = !swoop,
			Schema = schema,
			Move = schema != null && smashes == 0 && (move == "sweep" || move == "gas2" || move == "gas3") ? move : move == "slams" ? "slams" : "",
			Ranged = move == "lob" || move == "fire" || move == "tar" || move == "bomb" || move == "tickdrop" ? move : "",
			Switcher = gait == "switcher",
		};
		if (thing.Switcher)
		{
			thing.FreeX = true;
			thing.SwitchTimer = Random.Range(0.4f, 1.2f);
		}
		m_things.Add(thing);
		ApplyStars(thing, StarsAt(z));
		return thing;
	}

	private readonly HashSet<GameObject> m_loggedTriggers = new();

	// Puts the attack's weapon in the creature's hand, as VisEquipment.AttachItem does (the item's
	// "attach" child on the hand joint), and sets the animator's weapon stance.
	private bool Arm(GameObject wrapper, CreatureInfo info, CreatureAttack attack, Animator? anim)
	{
		if (anim != null)
		{
			SetParam(anim, "statef", (float)attack.AnimationState);
			SetParam(anim, "statei", attack.AnimationState);
		}
		string? path = attack.LeftHand ? info.LeftHand : info.RightHand;
		if (attack.Attach == null || string.IsNullOrEmpty(path) || wrapper.transform.childCount == 0)
		{
			return false;
		}
		Transform hand = wrapper.transform.GetChild(0).Find(path);
		if (hand == null)
		{
			return false;
		}
		GameObject weapon = Instantiate(Picture(attack.Attach), hand, false);
		weapon.transform.localPosition = Vector3.zero;
		weapon.transform.localRotation = Quaternion.identity;
		// VisEquipment parents with the world size kept, so bone scale (rigs are often scaled
		// 100x) doesn't apply. Keep the item's own size times our fit scale instead.
		Vector3 want = attack.AttachScale * wrapper.transform.localScale.x;
		Vector3 bone = hand.lossyScale;
		weapon.transform.localScale = new Vector3(
			want.x / Mathf.Max(1e-4f, Mathf.Abs(bone.x)),
			want.y / Mathf.Max(1e-4f, Mathf.Abs(bone.y)),
			want.z / Mathf.Max(1e-4f, Mathf.Abs(bone.z)));
		return true;
	}

	// Plays the creature's attack. Once its clip shows when the blow really falls, a pending
	// hit (FireIn) is moved to that moment, so the damage, shockwave or shot comes with it.
	private void TriggerAttack(Thing t, bool sound = true)
	{
		if (t.Anim != null && t.Attack != null)
		{
			TriggerWatched(t.Anim, t.Attack.Animation, left =>
			{
				if (t.FireIn > 0f)
				{
					t.FireIn = left;
				}
			});
		}
		if (sound)
		{
			float distance = Mathf.Abs(t.Z - m_z);
			string sfx = t.Attack != null && t.Attack.Sound.Length > 0 ? t.Attack.Sound : Sfx.ClubSwing;
			m_audio.Play(sfx, Mathf.Clamp01(1.2f - distance / 40f));
		}
	}

	private void UpdateCreature(Thing t, float dt)
	{
		float distance = t.Z - m_z;
		if (distance < -1f)
		{
			return;
		}
		if (t.Info == null)
		{
			return;
		}
		// Sleepers (abominations) lie as a heap until you're close, then burst up.
		float notice = t.Sleeping ? m_speed * 1.3f + 7f : t.Swoop ? m_speed * 1.15f + 5f : m_speed * 2.5f + 12f;
		if (!t.Alerted && distance < notice)
		{
			t.Alerted = true;
			string sound = t.Sleeping && t.Info.WakeSound.Length > 0 ? t.Info.WakeSound : t.Info.AlertSound;
			if (sound.Length > 0)
			{
				m_audio.Play(sound, Mathf.Clamp01(1.1f - distance / 50f));
			}
			if (t.Sleeping)
			{
				t.Sleeping = false;
				if (t.Anim != null)
				{
					SetParam(t.Anim, "sleeping", false);
				}
				t.Cooldown = Mathf.Max(t.Cooldown, 0.6f);
			}
			else if (t.Swoop)
			{
				// Dive across toward the lane you're in now.
				t.TargetX = LaneX(m_lane) + t.Side * -LaneWidth * 0.6f;
				t.XSpeed = Mathf.Abs(t.TargetX - t.X) / 1.0f;
				t.MoveSpeed = Mathf.Max(t.MoveSpeed, 3f);
				t.Collidable = true;
			}
			else if (t.MoveSpeed <= 0f && t.Anim != null && Random.value < 0.6f)
			{
				// Creatures that wait for you may taunt first.
				foreach (AnimatorControllerParameter p in t.Anim.parameters)
				{
					if (p.type == AnimatorControllerParameterType.Trigger && p.name.ToLowerInvariant().Contains("taunt"))
					{
						t.Anim.SetTrigger(p.name);
						break;
					}
				}
			}
			if (t.MoveSpeed > 0f)
			{
				t.Speed = t.MoveSpeed;
				if (t.Anim != null)
				{
					// The game sets forward_speed to the real ground speed (m/s), so the legs match.
					SetParam(t.Anim, "forward_speed", t.MoveSpeed);
				}
			}
		}
		if (t.Swoop && t.Alerted)
		{
			// Glide down to head height while crossing.
			t.BaseY = Mathf.MoveTowards(t.BaseY, 1.35f, 3.5f * dt);
		}
		if (t.Speed > 0f && t.Info.StepSound.Length > 0 && distance < 35f && !t.Swoop)
		{
			t.StepTimer -= dt;
			if (t.StepTimer <= 0f)
			{
				t.StepTimer = Mathf.Clamp(2.4f / Mathf.Max(1f, t.Pace), 0.18f, 0.6f) * (t.Stomper ? 1.4f : 1f);
				m_audio.Play(t.Info.StepSound, Mathf.Clamp01((t.Stomper ? 1.1f : 0.8f) - distance / 45f));
				if (t.Stomper)
				{
					m_shake = Mathf.Max(m_shake, Mathf.Clamp01(1f - distance / 30f) * 0.25f);
				}
			}
		}
		if (t.Attack == null || !t.Alerted)
		{
			return;
		}
		t.Cooldown -= dt;
		if (t.FireIn >= 0f)
		{
			t.FireIn -= dt;
			if (t.FireIn < 0f)
			{
				if (t.Attack.Ranged)
				{
					FireShot(t);
				}
				else
				{
					AttackLands(t);
					if (t.Move == "slams" && t.Smashes > 1)
					{
						// The berserk smash: marked lanes first, then a shockwave.
						LaneSlam(t);
						t.Smashes--;
						TriggerAttack(t);
						t.FireIn = 0.55f;
					}
					else if (t.Smashes > 0)
					{
						Shockwave(t);
						t.Smashes--;
						if (t.Smashes > 0)
						{
							// The next blow of a berserk smash.
							TriggerAttack(t);
							t.FireIn = 0.55f;
						}
					}
					else if (t.Move.Length > 0)
					{
						MoveLands(t);
					}
				}
			}
		}
		else if (t.Attack.Ranged && t.Ranged.Length == 0)
		{
			// The shot leaves while you're still well off: the wind-up is counted in (1.14, Sam:
			// shamans attacked way too late).
			float windup = AnimTiming.Delay(t.Anim, t.Attack.Animation, 0.35f);
			float lead = (m_speed + t.Speed) * windup;
			if (t.Cooldown <= 0f && distance < lead + m_speed * 1.6f + 12f && distance > lead + m_speed * 0.6f + 5f)
			{
				TriggerAttack(t);
				t.FireIn = windup;
				t.Cooldown = 2.4f;
			}
		}
		else if (!t.Attacked && t.Cooldown <= 0f)
		{
			// The blow lands as you arrive: the wind-up is the attack clip's own (learned the first
			// time it plays), else a guess from the creature's size.
			float windup = AnimTiming.Delay(t.Anim, t.Attack.Animation, Mathf.Clamp(0.2f + 0.12f * t.Height, 0.3f, 0.8f));
			// A smasher strikes from further off so its shockwaves travel to you.
			// 1.14 (Sam: golems and shamans struck too late): measured from its front, since a big
			// creature's body reaches you before its middle does, and a beat early so the blow is
			// seen landing rather than after you've run into it.
			float reach = t.Smashes > 0 ? (m_speed + t.Speed) * (windup + 1.2f) + 6f : (m_speed + t.Speed) * (windup + 0.15f) + 1.5f + t.HalfLength;
			if (t.Smashes > 0 && t.Markers.Count == 0 && t.Move.Length == 0 && distance < reach + (m_speed + t.Speed) * 1.2f)
			{
				// All three lanes glow before a smash: its shockwave will need jumping.
				for (int l = 0; l < 3; l++)
				{
					t.Markers.Add(MakeMarker(l, t.Z - 2f, 1.4f, JumpMaterial));
				}
			}
			if (t.Smashes > 0 && t.Move.Length == 0)
			{
				for (int l = 0; l < t.Markers.Count && l < 3; l++)
				{
					PlaceMarker(t.Markers[l], l, t.Z - 2f);
				}
			}
			if (distance < reach)
			{
				t.Attacked = true;
				TriggerAttack(t);
				t.FireIn = windup;
			}
		}
	}

	private static int SmashCount(GameObject prefab)
	{
		foreach (string item in Pictures.SplitList(Plugin.Smashers.Value))
		{
			string[] parts = item.Split(':');
			GameObject? p = Pictures.Monster(parts[0].Trim());
			if (p == prefab || string.Equals(parts[0].Trim(), prefab.name, System.StringComparison.OrdinalIgnoreCase))
			{
				return parts.Length > 1 && int.TryParse(parts[1].Trim(), out int n) ? Mathf.Clamp(n, 1, 6) : 1;
			}
		}
		return 0;
	}

	// A brute's heaviest blow: its berserk or ground-slam attack if it has one.
	private static CreatureAttack? PickSmash(CreatureInfo info)
	{
		foreach (string want in new[] { "rage", "berserk", "slam", "smash", "groundslam" })
		{
			foreach (CreatureAttack a in info.Melee)
			{
				if (a.Item.ToLowerInvariant().Contains(want) || a.Animation.ToLowerInvariant().Contains(want))
				{
					return a;
				}
			}
		}
		CreatureAttack? best = null;
		foreach (CreatureAttack a in info.Melee)
		{
			if (best == null || a.Area)
			{
				best = a;
			}
		}
		return best;
	}

	// A ripple of earth rolling down all three lanes from a smash; jump over it.
	private void Shockwave(Thing t)
	{
		ClearMarkers(t);
		float z = t.Z - 1.5f;
		if (z - m_z < 2f)
		{
			return;
		}
		m_shake = Mathf.Max(m_shake, Mathf.Clamp01(1f - Mathf.Abs(t.Z - m_z) / 30f) * 0.35f);
		BiomeRuntime b = m_biomes[BiomeAt(z)];
		for (int lane = 0; lane < 3; lane++)
		{
			GameObject wave = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
			Destroy(wave.GetComponent<Collider>());
			wave.name = "Shockwave";
			wave.layer = m_layer;
			wave.transform.SetParent(m_stage, false);
			wave.transform.localScale = new Vector3(LaneWidth * 0.95f, 0.2f, 0.7f);
			wave.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + 0.15f, z);
			Renderer r = wave.GetComponent<Renderer>();
			r.sharedMaterial = m_terrainTrack ?? b.Ground;
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			// A red band rides with the wave: jump it.
			GameObject band = MakeMarker(lane, z, 1.6f, JumpMaterial);
			band.transform.SetParent(wave.transform, true);
			if (t.Attack != null)
			{
				foreach (GameObject fx in t.Attack.Effects)
				{
					GameObject dust = Instantiate(Picture(fx), wave.transform, false);
					dust.transform.localPosition = Vector3.zero;
					foreach (ParticleSystem ps in dust.GetComponentsInChildren<ParticleSystem>(true))
					{
						ps.Play(true);
					}
					break;
				}
			}
			m_things.Add(new Thing
			{
				Go = wave,
				Kind = Kind.Low,
				Lane = lane,
				Z = z,
				BaseY = 0.15f,
				HalfLength = 0.4f,
				Speed = 7f + 0.5f * BiomeAt(z),
				Label = t.Label,
				CrashText = $"A {t.Label}'s smash knocked you flat in the {b.Def.Name}!",
				Life = 4f,
			});
		}
	}

	// When a melee or area attack strikes: show its effects (a blob's gas, dust), and leave an
	// area attack's cloud hanging in the lane for a moment.
	private void AttackLands(Thing t)
	{
		CreatureAttack? attack = t.Attack;
		if (attack == null || t.Go == null)
		{
			return;
		}
		Vector3 at = t.Go.transform.localPosition + new Vector3(0f, 0.5f, -1f);
		foreach (GameObject fx in attack.Effects)
		{
			PlayFx(fx, at, Quaternion.Euler(0f, 180f, 0f));
		}
		int shown = 0;
		foreach (GameObject fx in attack.Impact)
		{
			if (shown++ < 2)
			{
				PlayFx(fx, at + new Vector3(0f, -0.5f, -1f), Quaternion.Euler(0f, 180f, 0f));
			}
		}
		AttackSound(attack, true, Mathf.Clamp01(1.2f - Mathf.Abs(t.Z - m_z) / 40f));
		if (attack.Area && t.Move.Length == 0)
		{
			GameObject cloud = new GameObject("AttackCloud");
			cloud.transform.SetParent(m_stage, false);
			cloud.transform.localPosition = at;
			m_things.Add(new Thing
			{
				Go = cloud,
				Kind = Kind.Block,
				Lane = t.Lane,
				Z = t.Z - 1.5f,
				HalfLength = 1.6f,
				Label = t.Label,
				CrashText = $"You ran into a {t.Label}'s attack in the {m_biomes[BiomeAt(m_z)].Def.Name}!",
				Life = 2.5f,
			});
		}
	}

	// The creature's own projectile, flying down the track and across toward the lane you were in
	// when it fired. Slide under it or change lane.
	private void FireShot(Thing shooter)
	{
		CreatureAttack attack = shooter.Attack!;
		float startZ = shooter.Z - 1.2f;
		if (startZ - m_z < 3f)
		{
			return;
		}
		float speed = 9f + 0.6f * BiomeAt(shooter.Z);
		float startX = LaneX(shooter.Lane);
		float targetX = LaneX(m_lane);
		float time = Mathf.Max(0.3f, (startZ - m_z) / (speed + m_speed));
		float xSpeed = Mathf.Abs(targetX - startX) / time;
		const float y = 1.1f;
		GameObject wrapper = MakeShotVisual(attack);
		wrapper.transform.localPosition = new Vector3(startX, RoadY(startZ) + y, startZ);
		wrapper.transform.localRotation = Quaternion.LookRotation(new Vector3((targetX - startX) / time, 0f, -speed));
		// A blue patch rides along under it (1.14, Sam: blobs' shots had no telegraph): slide.
		GameObject shadow = MakeMarker(m_lane, startZ, 1.4f, SlideMaterial);
		shadow.transform.position = wrapper.transform.position - m_stage.up * (y - 0.08f);
		shadow.transform.SetParent(wrapper.transform, true);
		string biome = m_biomes[BiomeAt(m_z)].Def.Name;
		m_things.Add(new Thing
		{
			Go = wrapper,
			Kind = Kind.High,
			Lane = m_lane,
			Z = startZ,
			BaseY = y,
			HalfLength = 0.45f,
			Speed = speed,
			Label = shooter.Label,
			CrashText = $"A {shooter.Label}'s shot hit you in the {biome}! (Slide under them.)",
			FreeX = true,
			X = startX,
			TargetX = targetX,
			XSpeed = xSpeed,
		});
	}

	// A creature's projectile (its own model, else a stone), about a metre across, glowing.
	private GameObject MakeShotVisual(CreatureAttack attack)
	{
		GameObject wrapper = new GameObject("Shot");
		wrapper.layer = m_layer;
		wrapper.transform.SetParent(m_stage, false);
		GameObject? visual = null;
		if (attack.Projectile != null)
		{
			visual = Instantiate(Picture(attack.Projectile), wrapper.transform, false);
			visual.transform.localPosition = Vector3.zero;
			visual.transform.localRotation = Quaternion.identity;
			if (visual.GetComponentsInChildren<Renderer>().Length == 0)
			{
				Destroy(visual);
				visual = null;
			}
		}
		if (visual == null && Pictures.Prefab("Stone") is GameObject stone)
		{
			visual = Instantiate(Picture(stone), wrapper.transform, false);
			visual.transform.localPosition = Vector3.zero;
		}
		if (visual != null && Pictures.MeshBounds(visual, out Bounds bounds) && bounds.size.sqrMagnitude > 1e-4f)
		{
			visual.transform.position += wrapper.transform.position - bounds.center;
			float biggest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
			wrapper.transform.localScale = Vector3.one * Mathf.Clamp(0.9f / biggest, 0.05f, 2f);
		}
		// A small glow so it shows through the fog.
		Light glow = new GameObject("Glow").AddComponent<Light>();
		glow.gameObject.layer = m_layer;
		glow.transform.SetParent(wrapper.transform, false);
		glow.type = LightType.Point;
		glow.range = 4f;
		glow.intensity = 1.3f;
		glow.color = new Color(1f, 0.75f, 0.45f);
		glow.cullingMask = 1 << m_layer;
		return wrapper;
	}

	private void SpawnCoin(int lane, float z, float y)
	{
		if (m_coinTemplate == null)
		{
			return;
		}
		GameObject go = Fitted(m_coinTemplate, Quaternion.identity, new Vector3(0.5f, 0.5f, 0.5f), true);
		go.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + y, z);
		m_things.Add(new Thing { Go = go, Kind = Kind.Coin, Lane = lane, Z = z, Y = y, HalfLength = 0.3f });
	}

	// ---------------------------------------------------------------- avatar and camera

	private void SetParam(Animator anim, string name, float value)
	{
		foreach (AnimatorControllerParameter p in anim.parameters)
		{
			if (p.name == name && p.type == AnimatorControllerParameterType.Float)
			{
				anim.SetFloat(name, value);
				return;
			}
		}
	}

	private void SetParam(Animator anim, string name, bool value)
	{
		foreach (AnimatorControllerParameter p in anim.parameters)
		{
			if (p.name == name && p.type == AnimatorControllerParameterType.Bool)
			{
				anim.SetBool(name, value);
				return;
			}
		}
	}

	private void SetParam(Animator anim, string name, int value)
	{
		foreach (AnimatorControllerParameter p in anim.parameters)
		{
			if (p.name == name && p.type == AnimatorControllerParameterType.Int)
			{
				anim.SetInteger(name, value);
				return;
			}
		}
	}

	// Sets a trigger; returns the one used, or null. An attack with random variants (the game's
	// Attack.m_attackRandomAnimations, e.g. the goblin brute's "attack") has no trigger of its
	// own name, only "attack0", "attack1"...: one of those is picked.
	private static string? SetTrigger(Animator anim, string name)
	{
		List<string>? variants = null;
		foreach (AnimatorControllerParameter p in anim.parameters)
		{
			if (p.type != AnimatorControllerParameterType.Trigger)
			{
				continue;
			}
			if (p.name == name)
			{
				anim.SetTrigger(name);
				return name;
			}
			if (p.name.Length > name.Length && p.name.StartsWith(name) && char.IsDigit(p.name[name.Length]))
			{
				(variants ??= new List<string>()).Add(p.name);
			}
		}
		if (variants != null)
		{
			string pick = variants[Random.Range(0, variants.Count)];
			anim.SetTrigger(pick);
			return pick;
		}
		return null;
	}

	private void SetTrigger(string name)
	{
		if (m_avatarAnim != null && m_avatarParams.Contains(Animator.StringToHash(name)))
		{
			m_avatarAnim.SetTrigger(name);
		}
	}

	private void AvatarFloat(string name, float value)
	{
		if (m_avatarAnim != null && m_avatarParams.Contains(Animator.StringToHash(name)))
		{
			m_avatarAnim.SetFloat(name, value);
		}
	}

	private void AvatarBool(string name, bool value)
	{
		if (m_avatarAnim != null && m_avatarParams.Contains(Animator.StringToHash(name)))
		{
			m_avatarAnim.SetBool(name, value);
		}
	}

	private void PlaceAvatar(float dt)
	{
		if (m_avatar == null)
		{
			return;
		}
		m_avatar.transform.localPosition = new Vector3(m_x, RoadY(m_z) + m_y, m_z);
		// Blink while recovering from a hit.
		bool show = m_hurtTime <= 0f || Mathf.FloorToInt(m_hurtTime * 12f) % 2 == 0;
		foreach (Renderer r in m_avatarRenderers)
		{
			if (r != null)
			{
				r.enabled = show;
			}
		}
		float lean = (LaneX(m_lane) - m_x) * -6f;
		m_avatar.transform.localRotation = Quaternion.Euler(0f, 0f, m_state == State.Running ? lean : 0f);
		bool running = m_state == State.Running && m_aimTime <= 0f;
		AvatarFloat("forward_speed", running ? m_runAnimSpeed : 0f);
		AvatarFloat("sideway_speed", 0f);
		AvatarBool("onGround", m_y <= 0.05f);
		AvatarBool("crouching", running && m_slide > 0f);
		AvatarBool("inWater", false);
		if (m_avatarAnim != null)
		{
			m_avatarAnim.speed = running ? Mathf.Clamp(m_speed / 9f, 1f, 1.7f) : 1f;
		}
	}

	private void PlaceCamera(float dt)
	{
		// A chasing boss: the camera swings round you (over the side, a little higher on the
		// way) to look back at it, and back again when the fight is over.
		bool back = m_chase && m_boss != null && m_bossSink < 0f && m_state != State.Countdown;
		m_camBack = Mathf.MoveTowards(m_camBack, back ? 1f : 0f, dt / 1.4f);
		float swing = Mathf.SmoothStep(0f, 1f, m_camBack) * Mathf.PI;
		float dist = Plugin.CameraDistance.Value * (1f + 0.25f * Mathf.Sin(swing));
		float camX = m_x * 0.6f + Mathf.Sin(swing) * dist;
		float camZ = m_z - Mathf.Cos(swing) * dist;
		float lookX = m_x * 0.8f - Mathf.Sin(swing) * 7f;
		float lookZ = m_z + Mathf.Cos(swing) * 7f;
		// Follow the rolling road, but never dip into a rise behind you.
		float road = Mathf.Max(RoadY(m_z), RoadY(camZ) + 0.8f);
		Vector3 target = m_stage.TransformPoint(new Vector3(camX, road + Plugin.CameraHeight.Value * (1f + 0.15f * m_camBack) + 1.5f * Mathf.Sin(swing) + m_y * 0.4f, camZ));
		Vector3 look = m_stage.TransformPoint(new Vector3(lookX, RoadY(lookZ) + 1.0f, lookZ));
		if (!m_camPlaced)
		{
			m_camPos = target;
			m_camPlaced = true;
		}
		else
		{
			Vector3 smooth = Vector3.Lerp(m_camPos, target, 1f - Mathf.Exp(-10f * dt));
			// Never lag behind along the track, only ease sideways and up/down.
			smooth.z = target.z;
			m_camPos = smooth;
		}
		m_shake = Mathf.MoveTowards(m_shake, 0f, dt * 0.8f);
		m_camera.transform.position = m_camPos + (m_shake > 0.001f ? Random.insideUnitSphere * m_shake : Vector3.zero);
		m_camera.transform.rotation = Quaternion.LookRotation(look - m_camPos);
		m_camera.fieldOfView = Fov;
		m_camFinal = m_camera.transform.position;
		m_camFinalRot = m_camera.transform.rotation;

		if (m_biomes.Count == 0)
		{
			return;
		}
		BiomeRuntime b = m_biomes[BiomeAt(m_z + 30f)];
		float k = dt <= 0f ? 0f : 1f - Mathf.Exp(-2f * dt);
		if (!m_envPlaced)
		{
			k = 1f;
			m_envPlaced = true;
		}
		// A boss arena darkens and takes on the boss's colour.
		Color tint = Color.white;
		float arena = 0f;
		BossDef? bd0 = null;
		if (SegAt(m_z + 30f).Boss && Progress.BossOf(BiomeAt(m_z + 30f)) is BossDef bd)
		{
			tint = bd.Tint;
			arena = m_bossSink >= 0f ? 0.3f : 1f;
			bd0 = bd;
		}
		// Thicker without landmarks; with them the air clears a little so the boss's place
		// shows on the horizon.
		float arenaFog = Plugin.BossLandmarks.Value ? 1f - 0.25f * arena : 1f + 0.5f * arena;
		Color sky = Color.Lerp(b.Sky, b.Sky * 0.55f + tint * 0.2f, arena);
		float fog = b.Fog * Plugin.FogStrength.Value * arenaFog;
		Color fogColor = Color.Lerp(b.FogColor, b.FogColor * 0.6f + tint * 0.25f, arena);
		Color ambient = b.Ambient * (1f - 0.25f * arena);
		Color sunColor = Color.Lerp(b.SunColor, Color.Lerp(b.SunColor, tint, 0.3f), arena);
		float sunIntensity = b.SunIntensity * (1f - 0.3f * arena);
		// The boss's own weather over its arena (and its lightning).
		float weather = UpdateBossWeather(dt, arena > 0f ? bd0 : null, arena);
		ApplyBossWeather(weather, ref fog, ref fogColor, ref ambient, ref sunColor, ref sunIntensity, ref sky);
		m_skyBase = Color.Lerp(m_skyBase, sky, k);
		m_fogNow = Mathf.Lerp(m_fogNow, fog, k);
		m_fogColorNow = Color.Lerp(m_fogColorNow, fogColor, k);
		m_ambientNow = Color.Lerp(m_ambientNow, ambient, k);
		m_light.color = Color.Lerp(m_light.color, sunColor, k);
		m_sunBase = Mathf.Lerp(m_sunBase, sunIntensity, k);
		// A lightning flash brightens everything for a moment, on top of the weather.
		float flash = m_flash * m_flash;
		m_camera.backgroundColor = Color.Lerp(m_skyBase, new Color(0.85f, 0.9f, 1f), flash * 0.6f);
		m_light.intensity = m_sunBase + 2.5f * flash;
		UpdateBackdrop(dt);
	}

	public Color SkyColor => m_camera != null ? m_camera.backgroundColor : s_sky[0];

	private float m_fogNow = 0.011f;
	private Color m_fogColorNow = Color.gray;
	private Color m_ambientNow = Color.gray;
	private bool m_envPlaced;
	private float m_shake;

	public Color FogColor => m_fogColorNow;

	public Color AmbientColor => m_flash > 0f ? Color.Lerp(m_ambientNow, new Color(0.85f, 0.9f, 1f), m_flash * m_flash * 0.7f) : m_ambientNow;

	private Color m_skyBase = s_sky[0];
	private float m_sunBase = 1f;

	public float FogDensity => m_fogNow;

	// ---------------------------------------------------------------- UI

	private GUIStyle? m_big;
	private GUIStyle? m_mid;
	private GUIStyle? m_small;

	private void OnGUI()
	{
		float k = Screen.height / 1080f;
		if (m_big == null)
		{
			m_big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
			m_mid = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
			m_small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Bold };
		}
		m_big.fontSize = Mathf.RoundToInt(72 * k);
		m_mid!.fontSize = Mathf.RoundToInt(34 * k);
		m_small!.fontSize = Mathf.RoundToInt(26 * k);

		string biome = m_biomes.Count > 0 ? Progress.Describe(BiomeAt(m_z), StarsAt(m_z)) : "";
		string stretch = (m_biomes.Count > 0 ? StretchHud() : "") + (Plugin.Lives.Value > 1 ? $"   ·   lives {m_lives}" : "");
		Shadowed(new Rect(24 * k, 18 * k, 1400 * k, 200 * k),
			$"{biome}\n{m_z:0} m   ·   {m_coins} coins   ·   {m_speed:0.0} m/s{stretch}", m_small);

		float w = Screen.width;
		float h = Screen.height;
		if (m_state != State.Crashed && Event.current.type == EventType.Repaint)
		{
			HudGui(k, w, h);
		}
		switch (m_state)
		{
			case State.Countdown:
			{
				int n = Mathf.CeilToInt(Plugin.Countdown.Value - m_stateTime);
				Shadowed(new Rect(0, h * 0.25f, w, 120 * k), n > 0 ? n.ToString() : "Run!", m_big);
				Shadowed(new Rect(w * 0.15f, h * 0.62f, w * 0.7f, 200 * k),
					$"A / D or ←/→: change lane     W / Space: jump     S / Ctrl: slide\n{Plugin.SprintKey.Value}: sprint     {KeyName(Plugin.BlockKey.Value)}: block (tap just before a hit to parry)\n{Plugin.ExitKey.Value}: stop running", m_mid);
				break;
			}
			case State.Running:
				if (m_aimTime > 0f)
				{
					AimGui(k, w, h);
				}
				else if (m_bannerTime > 0f && (m_paused || m_bannerKeep || Plugin.PopupText.Value))
				{
					Shadowed(new Rect(0, h * 0.2f, w, 120 * k), m_banner, m_big);
				}
				if (m_aimTime <= 0f && m_hint.Length > 0 && Plugin.PopupText.Value)
				{
					Shadowed(new Rect(0, h * 0.52f, w, 120 * k), m_hint, m_big);
				}
				break;
			case State.Crashed:
				Shadowed(new Rect(w * 0.1f, h * 0.2f, w * 0.8f, 120 * k), m_crashText, m_mid);
				Shadowed(new Rect(w * 0.1f, h * 0.32f, w * 0.8f, 300 * k),
					$"{m_z:0} m, {m_coins} coins, reached {m_biomes[m_highestBiome].Def.Name}\n\n{m_rewardText}", m_mid);
				Shadowed(new Rect(w * 0.1f, h * 0.7f, w * 0.8f, 100 * k), $"Space: run again     {Plugin.ExitKey.Value}: back to the longhouse", m_mid);
				break;
		}
	}

	private static void Shadowed(Rect r, string text, GUIStyle style)
	{
		Color old = style.normal.textColor;
		style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
		GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
		style.normal.textColor = Color.white;
		GUI.Label(r, text, style);
		style.normal.textColor = old;
	}
}

// Fog and ambient light belong to the world's weather; while our camera renders, use a fog that
// fades into our own sky instead, then put the world's settings back.
internal sealed class SurfCameraFog : MonoBehaviour
{
	public SurfGame? Game;

	private bool m_fog;
	private Color m_fogColor;
	private FogMode m_fogMode;
	private float m_fogStart;
	private float m_fogEnd;
	private float m_fogDensity;
	private UnityEngine.Rendering.AmbientMode m_ambientMode;
	private Color m_ambient;

	private Light? m_worldSun;
	private bool m_worldSunOn;

	// The world's sun would also light the minigame (and at night not at all); switch it off
	// while our camera draws.
	private void OnPreCull()
	{
		m_worldSun = EnvMan.instance != null ? EnvMan.instance.m_dirLight : null;
		if (m_worldSun != null)
		{
			m_worldSunOn = m_worldSun.enabled;
			m_worldSun.enabled = false;
		}
	}

	private void OnPreRender()
	{
		m_fog = RenderSettings.fog;
		m_fogColor = RenderSettings.fogColor;
		m_fogMode = RenderSettings.fogMode;
		m_fogStart = RenderSettings.fogStartDistance;
		m_fogEnd = RenderSettings.fogEndDistance;
		m_fogDensity = RenderSettings.fogDensity;
		m_ambientMode = RenderSettings.ambientMode;
		m_ambient = RenderSettings.ambientLight;

		Color sky = Game != null ? Game.SkyColor : Color.gray;
		float density = Game != null ? Game.FogDensity : 0.012f;
		RenderSettings.fog = density > 0f;
		RenderSettings.fogColor = Game != null ? Game.FogColor : sky;
		// Keep the world's fog mode: the game's shaders are built for it, and a mode it never
		// uses (1.2 set exponential-squared) can have no shader variant, so there was no fog.
		if (RenderSettings.fogMode == FogMode.Linear)
		{
			RenderSettings.fogStartDistance = 0f;
			RenderSettings.fogEndDistance = 2.5f / Mathf.Max(0.001f, density);
		}
		RenderSettings.fogDensity = density;
		RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
		RenderSettings.ambientLight = Game != null ? Game.AmbientColor : sky * Plugin.AmbientLight.Value;
	}

	private void OnPostRender()
	{
		if (m_worldSun != null)
		{
			m_worldSun.enabled = m_worldSunOn;
		}
		RenderSettings.fog = m_fog;
		RenderSettings.fogColor = m_fogColor;
		RenderSettings.fogMode = m_fogMode;
		RenderSettings.fogStartDistance = m_fogStart;
		RenderSettings.fogEndDistance = m_fogEnd;
		RenderSettings.fogDensity = m_fogDensity;
		RenderSettings.ambientMode = m_ambientMode;
		RenderSettings.ambientLight = m_ambient;
	}
}

// Vanilla sound prefab names (found by asset search, 2026-11-04).
internal static class Sfx
{
	public const string Tick = "sfx_gui_button";
	public const string Go = "sfx_perfect_dodge";
	public const string Jump = "sfx_jump";
	public const string Land = "sfx_land";
	public const string Slide = "sfx_dodge";
	public const string Lane = "sfx_unarmed_swing";
	public const string Coin = "sfx_pickup|sfx_pickable_pick|sfx_gui_button";
	public const string Biome = "sfx_levelup";
	public const string HitMonster = "sfx_hit";
	public const string HitLog = "sfx_wood_hit";
	public const string Tar = "sfx_land_tar|sfx_tar_bubbles";
	public const string Miss = "sfx_arrow_hit|sfx_wood_hit";
	public const string ClubSwing = "sfx_club_swing";
	public const string TrollSlam = "sfx_troll_attack_hit";
	public const string TrollStep = "sfx_troll_footstep";
	public const string Thunder = "sfx_thunder|sfx_distant thunder";
	// A lightning bolt effect (asset search, 2026-11-05).
	public const string LightningFx = "fx_Lightning|lightningAOE";

	public static string Step(string biome) => biome switch
	{
		"Swamp" => "sfx_footstep_mud_run",
		"Mountains" or "Deep North" => "sfx_footstep_snow_run",
		_ => "sfx_footstep_run",
	};
}
