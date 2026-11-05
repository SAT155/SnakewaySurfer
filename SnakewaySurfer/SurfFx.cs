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
					foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
					{
						if (go != null && go.name == name && go.transform.parent == null)
						{
							found = go;
							break;
						}
					}
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
	private GameObject PlayFx(GameObject prefab, Vector3 local, Quaternion rotation, float life = 4f, Transform? parent = null)
	{
		GameObject go = Instantiate(Picture(prefab), parent != null ? parent : m_stage, false);
		go.SetActive(true);
		go.transform.localPosition = local;
		go.transform.localRotation = rotation;
		foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
		{
			ps.Play(true);
		}
		foreach (Light l in go.GetComponentsInChildren<Light>(true))
		{
			l.cullingMask = 1 << m_layer;
		}
		Destroy(go, life);
		return go;
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
		// Its rain, snow or ash, following our camera.
		foreach (GameObject ps in m_bossEnv.m_psystems ?? new GameObject[0])
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
		float z = m_z + ChaseSign * Random.Range(25f, 70f);
		float x = side * Random.Range(TrackHalf + 10f, TrackHalf + 40f);
		PlayFx(bolt, new Vector3(x, LandAt(x, z, out _, out _), z), Quaternion.identity, 3f);
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
		}
	}
}
