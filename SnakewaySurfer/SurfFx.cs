using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// Effects and boss weather. A boss arena takes on the weather of the boss's own place in the
// game (the EnvZone in its location, e.g. Eikthyr's thunderstorm): its fog, light and sky,
// its rain or snow, and its thunder and lightning flashes.
internal sealed partial class SurfGame
{
	private static readonly Dictionary<string, GameObject?> s_fx = new();
	private static Dictionary<string, GameObject>? s_roots;

	// 1.16.x (playtest: hitches at checkpoints): every loaded root object by name, from one walk
	// over all objects. Before, each effect name missing from the prefab list walked them all
	// again the first time it played (a checkpoint's activation effect, a first hit...), and
	// that walk is a long stall mid-run. Now it's done once, at the run's start (WarmRun).
	private static Dictionary<string, GameObject> RootIndex()
	{
		if (s_roots == null)
		{
			s_roots = new Dictionary<string, GameObject>();
			foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
			{
				if (go != null && go.transform.parent == null && !s_roots.ContainsKey(go.name))
				{
					s_roots[go.name] = go;
				}
			}
		}
		return s_roots;
	}

	// While the run loads: the lookups and copies the first checkpoint would otherwise do in
	// the middle of the run.
	private void WarmRun()
	{
		float t0 = Time.realtimeSinceStartup;
		RootIndex();
		m_audio.Warm("sfx_runestone_activate|sfx_gui_select");
		m_audio.Warm("sfx_gui_inventory_close|sfx_gui_button");
		if (CheckpointsOn)
		{
			if ((Pictures.Prefab("guard_stone") ?? Pictures.Prefab("piece_groundtorch_blue") ?? Pictures.Prefab("piece_groundtorch_wood")) is GameObject stone)
			{
				Picture(stone);
			}
			if (FindFx(CheckpointFx) is GameObject fx)
			{
				Picture(fx);
			}
		}
		Plugin.Log.LogInfo($"Run warm-up: {(Time.realtimeSinceStartup - t0) * 1000f:0} ms.");
	}

	// An effect prefab by name: from the scene's prefab list, else any loaded object of that
	// name (many effects aren't registered), looked up once.
	private static GameObject? FindFx(string names)
	{
		foreach (string name in names.Split('|'))
		{
			if (name.Length == 0)
			{
				continue;
			}
			if (!s_fx.TryGetValue(name, out GameObject? found))
			{
				found = Pictures.Prefab(name);
				if (found == null)
				{
					RootIndex().TryGetValue(name, out found);
				}
				s_fx[name] = found;
			}
			if (found != null)
			{
				return found;
			}
		}
		return null;
	}

	// A silent copy of an effect prefab, played once at a local spot on the stage.
	private GameObject PlayFx(GameObject prefab, Vector3 local, Quaternion rotation, float life = 4f, Transform? parent = null, float scale = 1f)
	{
		GameObject go = Instantiate(Picture(prefab), parent != null ? parent : m_stage, false);
		go.SetActive(true);
		go.transform.localPosition = local;
		go.transform.localRotation = rotation;
		if (scale != 1f)
		{
			go.transform.localScale *= scale;
		}
		foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
		{
			if (scale != 1f)
			{
				// Particles only grow with the copy when they take its whole scale.
				ParticleSystem.MainModule main = ps.main;
				main.scalingMode = ParticleSystemScalingMode.Hierarchy;
			}
			ps.Play(true);
		}
		foreach (Light l in go.GetComponentsInChildren<Light>(true))
		{
			l.range *= scale;
		}
		foreach (Light l in go.GetComponentsInChildren<Light>(true))
		{
			l.cullingMask = 1 << m_layer;
		}
		Destroy(go, life);
		return go;
	}

	// A torch or fire copy burning: a fireplace keeps its flames on inactive children (its
	// "enabled" objects, switched by fuel), which a lifeless copy never turns on (1.14.2, SAT:
	// the torches weren't lit).
	private void LightUp(GameObject go)
	{
		foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
		{
			string n = child.name.ToLowerInvariant();
			if (!child.gameObject.activeSelf && !n.Contains("disabled") && !n.Contains("low") && !n.Contains("smoke_low")
				&& (n.Contains("enabled") || n.Contains("fire") || n.Contains("flame")))
			{
				child.gameObject.SetActive(true);
			}
		}
		foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
		{
			ps.Play(true);
		}
		foreach (Light l in go.GetComponentsInChildren<Light>(true))
		{
			l.cullingMask = 1 << m_layer;
		}
	}

	private void AttackSound(CreatureAttack? attack, bool impact, float volume)
	{
		if (attack == null)
		{
			return;
		}
		List<string> list = impact && attack.ImpactSounds.Count > 0 ? attack.ImpactSounds : attack.StartSounds;
		if (list.Count > 0)
		{
			m_audio.Play(list[Random.Range(0, list.Count)], volume);
		}
		else if (attack.Sound.Length > 0)
		{
			m_audio.Play(attack.Sound, volume);
		}
	}

	// ---------------------------------------------------------------- boss weather

	private EnvSetup? m_bossEnv;
	private BossDef? m_bossEnvDef;
	private readonly List<GameObject> m_weatherObjects = new();
	private float m_thunderTimer = 3f;
	private float m_thunderDelay = -1f;
	private string m_thunderSound = "";
	private float m_flash;
	private float m_weatherWeight;
	private readonly Dictionary<string, string> m_locationEnv = new();

	private EnvSetup? EnvFor(BossDef def)
	{
		if (EnvMan.instance == null)
		{
			return null;
		}
		string name = m_locationEnv.TryGetValue(def.Location, out string fromZone) ? fromZone : "";
		List<string> options = new() { name };
		options.AddRange(def.Env.Split('|'));
		foreach (string option in options)
		{
			if (option.Length == 0)
			{
				continue;
			}
			EnvSetup env = EnvMan.instance.GetEnv(option);
			if (env != null)
			{
				return env;
			}
		}
		return null;
	}

	// Each frame from PlaceCamera: fades the boss's weather in over its arena (weight 0..1)
	// and returns its strength now.
	private float UpdateBossWeather(float dt, BossDef? def, float weight)
	{
		if (!Plugin.BossWeather.Value)
		{
			def = null;
		}
		if (def != m_bossEnvDef && m_weatherWeight <= 0.01f)
		{
			ClearWeather();
			m_bossEnvDef = def;
			m_bossEnv = def != null ? EnvFor(def) : null;
			if (def != null)
			{
				StartWeather(def);
			}
		}
		float target = def != null && def == m_bossEnvDef ? weight : 0f;
		m_weatherWeight = Mathf.MoveTowards(m_weatherWeight, target, dt / 2.5f);
		bool on = m_weatherWeight > 0.3f;
		foreach (GameObject go in m_weatherObjects)
		{
			if (go != null && go.activeSelf != on)
			{
				go.SetActive(on);
				if (on)
				{
					// Systems that don't play on waking up wouldn't start by themselves.
					foreach (ParticleSystem p in go.GetComponentsInChildren<ParticleSystem>(true))
					{
						p.Play(true);
					}
				}
			}
		}
		m_flash = Mathf.MoveTowards(m_flash, 0f, dt * 5f);
		if (m_bossEnvDef != null && m_bossEnvDef.Storm && m_weatherWeight > 0.5f && dt > 0f)
		{
			m_thunderTimer -= dt;
			if (m_thunderTimer <= 0f)
			{
				m_thunderTimer = Random.Range(2.5f, 7f);
				m_flash = 1f;
				m_thunderDelay = Random.Range(0.3f, 1.6f);
				LightningFlash();
			}
			if (m_thunderDelay > 0f)
			{
				m_thunderDelay -= dt;
				if (m_thunderDelay <= 0f)
				{
					m_audio.Play(m_thunderSound.Length > 0 ? m_thunderSound : Sfx.Thunder, Random.Range(0.6f, 1f));
				}
			}
		}
		return m_weatherWeight;
	}

	private void StartWeather(BossDef def)
	{
		m_thunderSound = def.StormSound;
		if (m_bossEnv == null)
		{
			Plugin.Log.LogInfo($"No weather found for {def.Name}'s arena (tried '{(m_locationEnv.TryGetValue(def.Location, out string z) ? z : "")}', '{def.Env}').");
			return;
		}
		// Its rain, snow or ash, following our camera. 1.14 (SAT saw no rain at Eikthyr): when
		// its own place's weather has none, the first of its fallback weathers that has some.
		GameObject[] systems = m_bossEnv.m_psystems ?? new GameObject[0];
		if (systems.Length == 0 && EnvMan.instance != null)
		{
			foreach (string option in def.Env.Split('|'))
			{
				EnvSetup other = option.Length > 0 ? EnvMan.instance.GetEnv(option) : null!;
				if (other?.m_psystems != null && other.m_psystems.Length > 0)
				{
					systems = other.m_psystems;
					Plugin.Log.LogInfo($"{def.Name}'s weather '{m_bossEnv.m_name}' has no rain or snow; using '{other.m_name}''s.");
					break;
				}
			}
		}
		foreach (GameObject ps in systems)
		{
			if (ps == null || m_camera == null)
			{
				continue;
			}
			GameObject copy = Instantiate(ps, m_camera.transform, false);
			copy.transform.localPosition = Vector3.zero;
			Pictures.SetLayer(copy, m_layer);
			copy.SetActive(false);
			foreach (ParticleSystem p in copy.GetComponentsInChildren<ParticleSystem>(true))
			{
				ParticleSystem.CollisionModule collision = p.collision;
				collision.enabled = false;
				p.Play(true);
			}
			m_weatherObjects.Add(copy);
		}
		// Its thunder (the environment's own Thunder settings, if it has any).
		Thunder? thunder = m_bossEnv.m_envObject != null ? m_bossEnv.m_envObject.GetComponentInChildren<Thunder>(true) : null;
		if (thunder != null && thunder.m_thunderEffect?.m_effectPrefabs != null)
		{
			foreach (EffectList.EffectData e in thunder.m_thunderEffect.m_effectPrefabs)
			{
				if (e?.m_prefab != null && e.m_prefab.GetComponentInChildren<ZSFX>(true) != null)
				{
					m_thunderSound = e.m_prefab.name + (m_thunderSound.Length > 0 ? "|" + m_thunderSound : "");
					break;
				}
			}
		}
		Plugin.Log.LogInfo($"{def.Name}'s weather: '{m_bossEnv.m_name}' (fog {m_bossEnv.m_fogDensityDay:0.000}, {m_weatherObjects.Count} particle systems{(def.Storm ? ", thunder" : "")}).");
	}

	// A bolt somewhere off to the side, far up the track, lighting everything for a moment.
	private void LightningFlash()
	{
		GameObject? bolt = FindFx(Sfx.LightningFx);
		if (bolt == null)
		{
			return;
		}
		float side = Random.value < 0.5f ? -1f : 1f;
		float z = m_z + (m_atSea ? Random.Range(5f, 60f) : ChaseSign * Random.Range(25f, 70f));
		// At sea, out on the water either side of the ship, where the camera above can see it.
		float x = m_atSea ? m_x + side * Random.Range(28f, 60f) : side * Random.Range(TrackHalf + 10f, TrackHalf + 40f);
		Vector3 ground = new Vector3(x, m_atSea ? SeaHeight(x, z) : LandAt(x, z, out _, out _), z);
		PlayFx(bolt, ground, Quaternion.identity, 3f);
		// 1.15.2: a visible streak from the clouds, not just the flash.
		LightningBolt(ground + new Vector3(Random.Range(-10f, 10f), 70f, 0f), ground, 0.9f, 0.4f, 0f, m_bossEnvDef?.Tint ?? new Color(0.6f, 0.8f, 1f));
	}

	private void ClearWeather()
	{
		foreach (GameObject go in m_weatherObjects)
		{
			if (go != null)
			{
				Destroy(go);
			}
		}
		m_weatherObjects.Clear();
		m_bossEnv = null;
		m_bossEnvDef = null;
		m_flash = 0f;
		ClearBolts();
	}

	// Blends the lighting toward the boss's weather (daytime values, like the biomes). The
	// lightning flash goes on top of this, in PlaceCamera.
	private void ApplyBossWeather(float w, ref float fog, ref Color fogColor, ref Color ambient, ref Color sunColor, ref float sunIntensity, ref Color sky)
	{
		if (m_bossEnv != null && w > 0f && Plugin.GameLighting.Value)
		{
			EnvSetup env = m_bossEnv;
			float scale = Plugin.GameLightScale.Value;
			fog = Mathf.Lerp(fog, env.m_fogDensityDay * Plugin.FogStrength.Value, w);
			fogColor = Color.Lerp(fogColor, env.m_fogColorDay, w);
			ambient = Color.Lerp(ambient, env.m_ambColorDay * scale, w);
			sunColor = Color.Lerp(sunColor, env.m_sunColorDay, w);
			sunIntensity = Mathf.Lerp(sunIntensity, env.m_lightIntensityDay * scale, w);
			sky = Color.Lerp(sky, env.m_fogColorDay, w * 0.8f);
			if (m_bossEnvDef != null && m_bossEnvDef.Storm)
			{
				// A storm closes in: thick fog and a dark sky whatever the zone's daytime says
				// (not at sea, where the ship must stay in sight from above).
				if (m_bossEnvDef != s_seaStorm)
				{
					fog = Mathf.Lerp(fog, Mathf.Max(fog, 0.03f * Plugin.FogStrength.Value), w);
				}
				Color storm = new Color(0.32f, 0.35f, 0.4f);
				fogColor = Color.Lerp(fogColor, storm, w * 0.6f);
				sky = Color.Lerp(sky, storm, w * 0.7f);
				sunIntensity *= 1f - 0.45f * w;
			}
		}
	}
}
