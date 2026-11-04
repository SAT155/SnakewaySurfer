using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// The minigame itself. Everything here is local to the player who typed "surfsup": the track is
// built from lifeless copies of vanilla prefabs far above the player, on a render layer only our
// own camera draws, so nothing is networked and nothing touches the world. Other players only
// see what PlayerHooks does to the real character (running in place, Dverger circlet).
internal sealed class SurfGame : MonoBehaviour
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
		public readonly List<(GameObject prefab, string label)> Monsters = new();
		public readonly List<(GameObject prefab, string label)> Flyers = new();
		public readonly List<GameObject> Scenery = new();
		public readonly List<GameObject> Logs = new();
		public readonly List<(GameObject prefab, string label)> Giants = new();
		public float Fog;
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
	private string m_banner = "";
	private float m_bannerTime;
	private string m_crashText = "";
	private string m_rewardText = "";
	private float m_lastHealth;
	private bool m_consoleWasVisible;
	private Vector3 m_camPos;
	private bool m_camPlaced;
	private SurfAudio m_audio = null!;
	private int m_lastTick;
	private float m_stepTimer;

	public static void Begin(Player player, Terminal? terminal)
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

		GameObject go = new GameObject("SnakewaySurfer_Game");
		SurfGame game = go.AddComponent<SurfGame>();
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
			// The world isn't on screen anyway; this saves drawing it twice.
			m_mainCamera.cullingMask = 0;
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
		if (m_mainCamera != null)
		{
			m_mainCamera.cullingMask = m_mainMask;
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

		m_trackMaterial = BorrowMaterial(Plugin.TrackPiece.Value) ?? BorrowMaterial("stone_floor_2x2") ?? BorrowMaterial("wood_floor");
		if (m_trackMaterial != null && m_trackMaterial.HasProperty("_MainTex"))
		{
			m_trackMaterial.mainTextureScale = new Vector2(2f, GroundTile / 4f);
		}
	}

	private Material MakeGroundMaterial(Color color)
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
			mat.color = color;
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
			BiomeRuntime rt = new BiomeRuntime { Def = def, Sky = s_sky[Mathf.Min(i, s_sky.Length - 1)], Fog = s_fog[Mathf.Min(i, s_fog.Length - 1)], Ground = MakeGroundMaterial(def.Ground) };
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
			foreach (string key in Pictures.SplitList(def.Monsters))
			{
				GameObject? prefab = Pictures.Monster(key);
				if (prefab != null)
				{
					rt.Monsters.Add((prefab, MonsterLabel(prefab)));
				}
				else
				{
					missing.Add(key);
				}
			}
			foreach (string key in Pictures.SplitList(def.Flyers))
			{
				GameObject? prefab = Pictures.Monster(key);
				if (prefab != null)
				{
					rt.Flyers.Add((prefab, MonsterLabel(prefab)));
				}
				else
				{
					missing.Add(key);
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
			if (Plugin.DebugLog.Value)
			{
				Plugin.Log.LogInfo($"{def.Name}: monsters [{string.Join(", ", rt.Monsters.ConvertAll(m => m.prefab.name))}], flyers [{string.Join(", ", rt.Flyers.ConvertAll(m => m.prefab.name))}], {rt.Scenery.Count} scenery, {rt.Logs.Count} logs");
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
			if (t.Go != null)
			{
				Destroy(t.Go);
			}
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
		m_lastTick = 0;
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
		m_rewardText = Rewards.Grant(m_player, m_z, m_highestBiome, m_runTime, m_biomes.ConvertAll(b => b.Def));
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

		float dt = Mathf.Min(Time.deltaTime, 0.05f);
		m_stateTime += dt;
		m_bannerTime -= dt;

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
				UpdateRun(dt, canInput);
				break;
			case State.Crashed:
				if (canInput && m_stateTime > 0.8f && KeyDown(KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter))
				{
					ResetRun();
				}
				break;
		}

		UpdateThings(dt);
		PlaceAvatar(dt);
		PlaceCamera(dt);
	}

	private void LateUpdate()
	{
		// The game may switch its HUD back on (Hud.Update); keep it hidden while we're up.
		if (m_hudRoot != null && m_hudRoot.activeSelf)
		{
			m_hudRoot.SetActive(false);
		}
	}

	private int BiomeAt(float z) => Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f, z) / Plugin.BiomeLength.Value), 0, m_biomes.Count - 1);

	private static float LaneX(int lane) => (lane - 1) * LaneWidth;

	private void UpdateRun(float dt, bool canInput)
	{
		m_runTime += dt;
		float biomeProgress = m_z / Plugin.BiomeLength.Value;
		m_speed = Mathf.Min(Plugin.StartSpeed.Value + Plugin.SpeedPerBiome.Value * biomeProgress, Plugin.MaxSpeed.Value);

		bool grounded = m_y <= 0.001f;
		if (canInput)
		{
			if (KeyDown(KeyCode.A, KeyCode.LeftArrow))
			{
				if (m_lane > 0)
				{
					m_audio.Play(Sfx.Lane, 0.5f, 1.15f);
				}
				m_lane = Mathf.Max(0, m_lane - 1);
			}
			if (KeyDown(KeyCode.D, KeyCode.RightArrow))
			{
				if (m_lane < 2)
				{
					m_audio.Play(Sfx.Lane, 0.5f, 1.15f);
				}
				m_lane = Mathf.Min(2, m_lane + 1);
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
				m_audio.Play(Sfx.Step(m_biomes[BiomeAt(m_z)].Def.Name), 0.35f);
			}
		}
		if (m_y <= 0.001f)
		{
			m_slide = Mathf.Max(0f, m_slide - dt);
		}
		m_x = Mathf.MoveTowards(m_x, LaneX(m_lane), Plugin.LaneSwitchSpeed.Value * dt);
		m_z += m_speed * dt;

		int biome = BiomeAt(m_z);
		m_highestBiome = Mathf.Max(m_highestBiome, biome);
		if (biome != m_shownBiome)
		{
			m_shownBiome = biome;
			m_banner = biome == 0 ? m_biomes[0].Def.Name : "Entering " + m_biomes[biome].Def.Name + "!";
			m_bannerTime = 2.5f;
			if (biome > 0)
			{
				m_audio.Play(Sfx.Biome);
			}
		}

		CheckCollisions();
		SpawnAhead();
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
			if (Mathf.Abs(t.Z - m_z) > t.HalfLength + 0.35f || Mathf.Abs(m_x - LaneX(t.Lane)) > LaneWidth * 0.45f)
			{
				continue;
			}
			switch (t.Kind)
			{
				case Kind.Coin:
					if (m_y < 2.6f)
					{
						m_coins++;
						m_audio.Play(Sfx.Coin, 0.6f, 1f + 0.03f * (m_coins % 5));
						Destroy(t.Go);
						m_things.RemoveAt(i);
					}
					break;
				case Kind.Block:
					m_audio.Play(Sfx.HitMonster);
					Crash(t.CrashText.Length > 0 ? t.CrashText : $"A {t.Label} got you in the {m_biomes[BiomeAt(m_z)].Def.Name}!");
					return;
				case Kind.Low:
					if (m_y < 0.75f)
					{
						m_audio.Play(Sfx.HitLog);
						Crash($"You tripped over a log in the {m_biomes[BiomeAt(m_z)].Def.Name}!");
						return;
					}
					break;
				case Kind.High:
					if (!sliding)
					{
						m_audio.Play(string.IsNullOrEmpty(t.Label) ? Sfx.HitLog : Sfx.HitMonster);
						Crash(t.CrashText.Length > 0 ? t.CrashText : string.IsNullOrEmpty(t.Label)
							? $"You ran into a hanging log in the {m_biomes[BiomeAt(m_z)].Def.Name}!"
							: $"A {t.Label} swooped into you in the {m_biomes[BiomeAt(m_z)].Def.Name}!");
						return;
					}
					break;
			}
		}
	}

	private void UpdateThings(float dt)
	{
		float behind = m_z - 25f;
		for (int i = m_things.Count - 1; i >= 0; i--)
		{
			Thing t = m_things[i];
			if (t.Go == null)
			{
				m_things.RemoveAt(i);
				continue;
			}
			if (m_state == State.Running && t.Special != Special.None)
			{
				UpdateSetPiece(t, dt);
			}
			if (m_state == State.Running && t.Speed > 0f)
			{
				t.Z -= t.Speed * dt;
				Vector3 p = t.Go.transform.localPosition;
				t.Go.transform.localPosition = new Vector3(p.x, p.y, t.Z);
			}
			if (t.Kind == Kind.Coin)
			{
				t.Go.transform.Rotate(0f, 180f * dt, 0f, Space.World);
			}
			float end = t.Kind == Kind.Ground ? t.Z + GroundTile * 0.5f : t.Z + t.HalfLength;
			if (end < behind)
			{
				Destroy(t.Go);
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
			m_nextSceneryZ += Random.Range(3.5f, 8f) / Mathf.Max(0.2f, Plugin.SceneryDensity.Value);
		}
		while (m_nextRowZ < horizon)
		{
			SpawnRow(m_nextRowZ);
			float difficulty = Mathf.Clamp01(m_nextRowZ / (Plugin.BiomeLength.Value * m_biomes.Count));
			m_nextRowZ += Mathf.Lerp(30f, 16f, difficulty) + Random.Range(0f, 9f);
		}
	}

	private GameObject Box(string name, Material? material, Vector3 localPos, Vector3 size)
	{
		GameObject go = new GameObject(name);
		go.layer = m_layer;
		go.transform.SetParent(m_stage, false);
		go.transform.localPosition = localPos;
		go.transform.localScale = size;
		go.AddComponent<MeshFilter>().sharedMesh = m_cube;
		MeshRenderer r = go.AddComponent<MeshRenderer>();
		r.sharedMaterial = material;
		r.receiveShadows = true;
		return go;
	}

	private void SpawnGround(float centerZ)
	{
		BiomeRuntime b = m_biomes[BiomeAt(centerZ)];
		float trackWidth = LaneWidth * 3f + 0.8f;
		GameObject root = new GameObject("Ground");
		root.layer = m_layer;
		root.transform.SetParent(m_stage, false);
		root.transform.localPosition = new Vector3(0f, 0f, centerZ);
		GameObject track = Box("Track", m_trackMaterial ?? b.Ground, Vector3.zero, Vector3.one);
		track.transform.SetParent(root.transform, false);
		track.transform.localPosition = new Vector3(0f, -0.1f, 0f);
		track.transform.localScale = new Vector3(trackWidth, 0.2f, GroundTile + 0.02f);
		foreach (float side in new[] { -1f, 1f })
		{
			GameObject land = Box("Land", b.Ground, Vector3.zero, Vector3.one);
			land.transform.SetParent(root.transform, false);
			land.transform.localPosition = new Vector3(side * (trackWidth * 0.5f + 40f), -0.15f, 0f);
			land.transform.localScale = new Vector3(80f, 0.2f, GroundTile + 0.02f);
		}
		m_things.Add(new Thing { Go = root, Kind = Kind.Ground, Z = centerZ, HalfLength = GroundTile * 0.5f });
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
		go.transform.localPosition = new Vector3(x, 0f, z);
		go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
		go.transform.localScale = Vector3.one * Random.Range(0.85f, 1.15f);
		m_things.Add(new Thing { Go = go, Kind = Kind.Scenery, Z = z, HalfLength = 2f });
	}

	// Puts a picture in a world-aligned wrapper and scales the wrapper to fit `size`
	// (per axis, or uniformly within it), standing on y = 0 of the wrapper.
	private GameObject Fitted(GameObject template, Quaternion rotation, Vector3 size, bool uniform)
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
				float s = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
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
		if (giants.Giants.Count > 0 && z > 60f && Random.value < Plugin.GiantChance.Value)
		{
			if (Random.value < 0.5f)
			{
				SpawnSwingTroll(z, giants);
			}
			else
			{
				SpawnStraddleTroll(z, giants);
			}
			m_nextRowZ += 8f;
			return;
		}
		BiomeRuntime b = m_biomes[BiomeAt(z)];
		int biomeIndex = BiomeAt(z);
		float difficulty = Mathf.Clamp01(z / (Plugin.BiomeLength.Value * m_biomes.Count));
		int blocked = Random.value < 0.3f + 0.4f * difficulty ? 2 : 1;
		List<int> lanes = new() { 0, 1, 2 };
		for (int i = 0; i < lanes.Count; i++)
		{
			int j = Random.Range(i, lanes.Count);
			(lanes[i], lanes[j]) = (lanes[j], lanes[i]);
		}

		for (int i = 0; i < 3; i++)
		{
			int lane = lanes[i];
			if (i >= blocked)
			{
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
					(GameObject prefab, string label) = b.Monsters[Random.Range(0, b.Monsters.Count)];
					GameObject go = Fitted(Picture(prefab), Quaternion.Euler(0f, 180f, 0f), new Vector3(2.2f, Plugin.MonsterHeight.Value, 3f), true);
					go.transform.localPosition = new Vector3(LaneX(lane), 0f, z);
					float speed = Random.value < 0.55f ? Plugin.MonsterApproach.Value * biomeIndex * Random.Range(0.5f, 1f) : 0f;
					Animator anim = go.GetComponentInChildren<Animator>();
					if (anim != null)
					{
						SetParam(anim, "forward_speed", speed > 0.1f ? Mathf.Max(speed, 2f) : 0f);
						SetParam(anim, "onGround", true);
					}
					m_things.Add(new Thing { Go = go, Kind = Kind.Block, Lane = lane, Z = z, HalfLength = 0.9f, Speed = speed, Label = label });
					break;
				}
				case Kind.Low:
				{
					GameObject log = b.Logs[Random.Range(0, b.Logs.Count)];
					GameObject go = Fitted(Picture(log), Quaternion.Euler(0f, 0f, 90f), new Vector3(LaneWidth * 0.9f, 0.7f, 0.7f), false);
					go.transform.localPosition = new Vector3(LaneX(lane), 0f, z);
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
						(GameObject prefab, string label) = b.Flyers[Random.Range(0, b.Flyers.Count)];
						GameObject go = Fitted(Picture(prefab), Quaternion.Euler(0f, 180f, 0f), new Vector3(2.0f, 1.0f, 2.0f), true);
						go.transform.localPosition = new Vector3(LaneX(lane), 1.35f, z);
						m_things.Add(new Thing { Go = go, Kind = Kind.High, Lane = lane, Z = z, HalfLength = 0.8f, Label = label });
					}
					else
					{
						GameObject log = b.Logs[Random.Range(0, b.Logs.Count)];
						GameObject go = Fitted(Picture(log), Quaternion.Euler(0f, 0f, 90f), new Vector3(LaneWidth, 0.6f, 0.6f), false);
						go.transform.localPosition = new Vector3(LaneX(lane), 1.3f, z);
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
	private void SpawnSwingTroll(float z, BiomeRuntime b)
	{
		(GameObject prefab, string label) = b.Giants[Random.Range(0, b.Giants.Count)];
		int side = Random.value < 0.5f ? -1 : 1;
		GameObject root = new GameObject("SwingTroll");
		root.layer = m_layer;
		root.transform.SetParent(m_stage, false);
		root.transform.localPosition = new Vector3(0f, 0f, z);

		float trollX = side * (LaneWidth * 1.5f + 2.4f);
		// Facing the track and a little toward the runner.
		GameObject troll = Fitted(Picture(prefab), Quaternion.Euler(0f, side < 0 ? 120f : 240f, 0f), new Vector3(3.2f, 3.8f, 3.2f), true);
		troll.transform.SetParent(root.transform, false);
		troll.transform.localPosition = new Vector3(trollX, 0f, 0f);

		GameObject pivot = new GameObject("ClubPivot");
		pivot.layer = m_layer;
		pivot.transform.SetParent(root.transform, false);
		pivot.transform.localPosition = new Vector3(trollX - side * 0.7f, 2.7f, 0f);
		GameObject log = b.Logs.Count > 0 ? b.Logs[Random.Range(0, b.Logs.Count)] : m_biomes[1].Logs.Count > 0 ? m_biomes[1].Logs[0] : null!;
		if (log != null)
		{
			GameObject club = Fitted(Picture(log), Quaternion.identity, new Vector3(0.55f, 4.4f, 0.55f), false);
			club.transform.SetParent(pivot.transform, false);
			club.transform.localPosition = Vector3.zero;
		}
		pivot.transform.localRotation = Quaternion.Euler(0f, 0f, side * -25f);

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
			Moving = pivot.transform,
			Side = side,
		});
	}

	// A troll jumps down over the track, legs planted in both outer lanes: the only way through
	// is sliding between its legs in the middle lane.
	private void SpawnStraddleTroll(float z, BiomeRuntime b)
	{
		(GameObject prefab, string label) = b.Giants[Random.Range(0, b.Giants.Count)];
		GameObject root = new GameObject("StraddleTroll");
		root.layer = m_layer;
		root.transform.SetParent(m_stage, false);
		root.transform.localPosition = new Vector3(0f, 0f, z);

		GameObject body = new GameObject("Body");
		body.layer = m_layer;
		body.transform.SetParent(root.transform, false);
		body.transform.localPosition = new Vector3(0f, 9f, 0f);
		// Stretched sideways so its feet land in the outer lanes.
		GameObject troll = Fitted(Picture(prefab), Quaternion.Euler(0f, 180f, 0f), new Vector3(LaneWidth * 3f + 0.4f, 4.2f, 3f), false);
		troll.transform.SetParent(body.transform, false);
		troll.transform.localPosition = Vector3.zero;

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
		if (t.Moving == null)
		{
			return;
		}
		float distance = t.Z - m_z;
		if (!t.Triggered)
		{
			float trigger = t.Special == Special.Swing ? m_speed * 0.5f + 2.5f : m_speed * 1.3f + 8f;
			if (distance > trigger)
			{
				return;
			}
			t.Triggered = true;
			m_audio.Play(t.Special == Special.Swing ? Sfx.ClubSwing : Sfx.TrollStep);
		}
		if (t.Progress >= 1f)
		{
			return;
		}
		float duration = t.Special == Special.Swing ? 0.22f : 0.35f;
		t.Progress = Mathf.Min(1f, t.Progress + dt / duration);
		float e = t.Progress * t.Progress;
		if (t.Special == Special.Swing)
		{
			t.Moving.localRotation = Quaternion.Euler(0f, 0f, t.Side * Mathf.Lerp(-25f, 128f, e));
			if (t.Progress >= 0.5f)
			{
				t.Collidable = true;
			}
		}
		else
		{
			t.Moving.localPosition = new Vector3(0f, Mathf.Lerp(9f, 0f, e), 0f);
		}
		if (t.Progress >= 1f)
		{
			m_audio.Play(Sfx.TrollSlam);
		}
	}

	private void SpawnCoin(int lane, float z, float y)
	{
		if (m_coinTemplate == null)
		{
			return;
		}
		GameObject go = Fitted(m_coinTemplate, Quaternion.identity, new Vector3(0.5f, 0.5f, 0.5f), true);
		go.transform.localPosition = new Vector3(LaneX(lane), y, z);
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
		m_avatar.transform.localPosition = new Vector3(m_x, m_y, m_z);
		float lean = (LaneX(m_lane) - m_x) * -6f;
		m_avatar.transform.localRotation = Quaternion.Euler(0f, 0f, m_state == State.Running ? lean : 0f);
		bool running = m_state == State.Running;
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
		Vector3 target = m_stage.TransformPoint(new Vector3(m_x * 0.6f, Plugin.CameraHeight.Value + m_y * 0.4f, m_z - Plugin.CameraDistance.Value));
		Vector3 look = m_stage.TransformPoint(new Vector3(m_x * 0.8f, 1.0f, m_z + 7f));
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
		m_camera.transform.position = m_camPos;
		m_camera.transform.rotation = Quaternion.LookRotation(look - m_camPos);

		Color sky = m_biomes.Count > 0 ? m_biomes[BiomeAt(m_z + 30f)].Sky : s_sky[0];
		m_camera.backgroundColor = Color.Lerp(m_camera.backgroundColor, sky, 1f - Mathf.Exp(-2f * dt));
		float fog = (m_biomes.Count > 0 ? m_biomes[BiomeAt(m_z + 30f)].Fog : 0.011f) * Plugin.FogStrength.Value;
		m_fogNow = Mathf.Lerp(m_fogNow, fog, 1f - Mathf.Exp(-2f * dt));
	}

	public Color SkyColor => m_camera != null ? m_camera.backgroundColor : s_sky[0];

	private float m_fogNow = 0.011f;

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

		string biome = m_biomes.Count > 0 ? m_biomes[BiomeAt(m_z)].Def.Name : "";
		Shadowed(new Rect(24 * k, 18 * k, 900 * k, 200 * k),
			$"{biome}\n{m_z:0} m   ·   {m_coins} coins   ·   {m_speed:0.0} m/s", m_small);

		float w = Screen.width;
		float h = Screen.height;
		switch (m_state)
		{
			case State.Countdown:
			{
				int n = Mathf.CeilToInt(Plugin.Countdown.Value - m_stateTime);
				Shadowed(new Rect(0, h * 0.25f, w, 120 * k), n > 0 ? n.ToString() : "Run!", m_big);
				Shadowed(new Rect(w * 0.15f, h * 0.62f, w * 0.7f, 200 * k),
					$"A / D or ←/→: change lane     W / Space: jump     S / Ctrl: slide\n{Plugin.ExitKey.Value}: stop running", m_mid);
				break;
			}
			case State.Running:
				if (m_bannerTime > 0f)
				{
					Shadowed(new Rect(0, h * 0.2f, w, 120 * k), m_banner, m_big);
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
		RenderSettings.fog = true;
		RenderSettings.fogColor = sky;
		RenderSettings.fogMode = FogMode.ExponentialSquared;
		RenderSettings.fogDensity = Game != null ? Game.FogDensity : 0.012f;
		RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
		RenderSettings.ambientLight = sky * Plugin.AmbientLight.Value;
	}

	private void OnPostRender()
	{
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
	public const string Coin = "sfx_pickup";
	public const string Biome = "sfx_levelup";
	public const string HitMonster = "sfx_hit";
	public const string HitLog = "sfx_wood_hit";
	public const string ClubSwing = "sfx_club_swing";
	public const string TrollSlam = "sfx_troll_attack_hit";
	public const string TrollStep = "sfx_troll_footstep";

	public static string Step(string biome) => biome switch
	{
		"Swamp" => "sfx_footstep_mud_run",
		"Mountains" or "Deep North" => "sfx_footstep_snow_run",
		_ => "sfx_footstep_run",
	};
}
