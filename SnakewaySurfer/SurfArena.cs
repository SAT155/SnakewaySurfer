using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// How a boss arena looks: the boss's own place from the game (its ZoneSystem location, e.g.
// Eikthyrnir or GDKing) looms far ahead like a skybox, rising out of the ground as you arrive;
// the path flattens while the land beside it rises into walls; torches in the boss's colour
// line the path. Everything fades in over an arena's first 40 m and out over the next
// stretch's first 40 m, like the biome blends.
internal sealed partial class SurfGame
{
	private GameObject? m_backdrop;
	private BossDef? m_backdropDef;
	private float m_backdropShow;
	private float m_backdropDist = 70f;
	private float m_backdropLift;
	private float m_backdropHeight;
	private float m_nextTorchZ;
	private int m_torchCount;
	// Location prefabs are soft references the game loads on demand: ours stay loaded (our
	// copies use their meshes) until the minigame closes.
	private readonly Dictionary<string, GameObject?> m_locationPictures = new();
	private readonly List<ZoneSystem.ZoneLocation> m_heldLocations = new();

	// 0 outside arenas, 1 inside, blended at the edges. `def` is the arena's boss (the one
	// before, while its arena fades out).
	private float ArenaWeight(float z, out BossDef? def) => ArenaWeight(z, out def, out _);

	private float ArenaWeight(float z, out BossDef? def, out float walls)
	{
		def = null;
		walls = 0f;
		if (!Plugin.BossLandmarks.Value || m_segments.Count == 0)
		{
			return 0f;
		}
		Segment s = SegAt(z);
		float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((z - s.Start) / 40f));
		BossDef? prev = s.PrevBoss ? Progress.BossOf(s.PrevBiome) : null;
		if (s.Boss && Progress.BossOf(s.Biome) is BossDef cur)
		{
			def = cur;
			// Arena after arena (the boss rush): the walls change height, the look stays.
			walls = Mathf.Lerp(prev != null ? prev.Walls : 0f, cur.Walls, ramp);
			return prev != null ? 1f : ramp;
		}
		if (prev != null)
		{
			def = prev;
			walls = prev.Walls * (1f - ramp);
			return 1f - ramp;
		}
		return 0f;
	}

	// Road roll is multiplied by this: a flatter battleground.
	private float ArenaFlatten(float z) => 1f - 0.75f * ArenaWeight(z, out _);

	// Extra height of the land beside the track, `out_` metres past its edge.
	private float ArenaWalls(float z, float out_)
	{
		ArenaWeight(z, out _, out float walls);
		if (walls <= 0f)
		{
			return 0f;
		}
		return walls * Plugin.HillHeight.Value * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((out_ - 5f) / 25f));
	}

	// ---------------------------------------------------------------- torches

	private void SpawnArenaTorches(float horizon)
	{
		while (m_nextTorchZ < horizon)
		{
			float z = m_nextTorchZ;
			m_nextTorchZ += 12f;
			if (ArenaWeight(z, out BossDef? def) < 0.5f || def == null || ShoreWeight(z) > 0.15f)
			{
				// (Not down the beach to a crossing, 1.16.)
				continue;
			}
			GameObject? prefab = Pictures.Prefab(def.Torch) ?? Pictures.Prefab("piece_groundtorch_wood");
			if (prefab == null)
			{
				continue;
			}
			m_torchCount++;
			foreach (int side in new[] { -1, 1 })
			{
				float x = side * (TrackHalf + 1.2f + ExtraFlat(z));
				GameObject go = Instantiate(Picture(prefab), m_stage, false);
				go.transform.localPosition = new Vector3(x, HillHeight(x, z), z);
				go.transform.localRotation = Quaternion.identity;
				LightUp(go);
				// One light per pair, on alternating sides: enough to light the path, without
				// dozens of lights.
				if ((m_torchCount & 1) == (side < 0 ? 0 : 1))
				{
					Light glow = new GameObject("ArenaGlow").AddComponent<Light>();
					glow.gameObject.layer = m_layer;
					glow.transform.SetParent(go.transform, false);
					glow.transform.localPosition = new Vector3(0f, 1.6f, 0f);
					glow.type = LightType.Point;
					glow.range = 9f;
					glow.intensity = 1.5f;
					glow.color = Color.Lerp(new Color(1f, 0.65f, 0.3f), def.Tint, 0.6f);
					glow.cullingMask = 1 << m_layer;
					glow.shadows = LightShadows.None;
				}
				m_things.Add(new Thing { Go = go, Kind = Kind.Scenery, Z = z, HalfLength = 0.5f });
			}
		}
	}

	// ---------------------------------------------------------------- the backdrop

	private void UpdateBackdrop(float dt)
	{
		if (m_segments.Count == 0)
		{
			return;
		}
		// The arena you're in, or the one coming up.
		BossDef? want = null;
		if (Plugin.BossLandmarks.Value)
		{
			Segment here = SegAt(m_z);
			Segment ahead = SegAt(m_z + 60f);
			Segment? arena = here.Boss ? here : ahead.Boss ? ahead : null;
			if (arena != null && !arena.Beaten)
			{
				want = Progress.BossOf(arena.Biome);
			}
		}
		if (want != m_backdropDef && m_backdropShow <= 0.001f)
		{
			DestroyBackdrop();
			if (want != null)
			{
				BuildBackdrop(want);
			}
			m_backdropDef = want;
		}
		float target = want != null && want == m_backdropDef ? 1f : 0f;
		// Rises over 3 s, sinks over 2.5 s.
		m_backdropShow = Mathf.MoveTowards(m_backdropShow, target, dt / (target > 0f ? 3f : 2.5f));
		if (m_backdrop == null || m_backdropDef == null)
		{
			return;
		}
		// Far enough to read as scenery and stay behind the boss, near enough to show through
		// the fog.
		float wanted = Mathf.Clamp(0.85f / Mathf.Max(0.0005f, m_fogNow), 32f, 95f);
		wanted = Mathf.Max(wanted, m_backdropDef.Mode == "stand" ? m_backdropDef.Distance + 22f : m_backdropDef.Distance + 14f);
		m_backdropDist = Mathf.Lerp(m_backdropDist, wanted, dt <= 0f ? 1f : 1f - Mathf.Exp(-1f * dt));
		float z = m_z + m_backdropDist;
		float k = Mathf.SmoothStep(0f, 1f, m_backdropShow);
		float y = RoadY(z) + m_backdropLift - (1f - k) * (m_backdropHeight + 2f);
		m_backdrop.transform.localPosition = new Vector3(0f, y, z);
		m_backdrop.SetActive(k > 0.001f);
	}

	private void BuildBackdrop(BossDef def)
	{
		GameObject? template = LocationPicture(def.Location);
		bool location = template != null;
		if (template == null)
		{
			// Not found: a giant shadow of the boss itself.
			GameObject? boss = Pictures.Prefab(def.Prefab);
			if (boss == null)
			{
				return;
			}
			template = Picture(boss);
		}
		GameObject wrapper = new GameObject("Backdrop_" + def.Name);
		wrapper.layer = m_layer;
		wrapper.transform.SetParent(m_stage, false);
		GameObject go = Instantiate(template, wrapper.transform, false);
		go.transform.localPosition = Vector3.zero;
		// Face the runner (the track runs along +z).
		go.transform.localRotation = Quaternion.Euler(0f, 180f + def.BackdropTurn, 0f);
		go.transform.localScale = Vector3.one;
		go.SetActive(true);
		foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
		{
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			if (!location && r.sharedMaterials.Length > 0)
			{
				// The boss as a dark shape on the horizon.
				Material[] mats = r.materials;
				foreach (Material m in mats)
				{
					if (m.HasProperty("_Color"))
					{
						m.color = Color.Lerp(m.color * 0.25f, def.Tint * 0.3f, 0.5f);
					}
				}
				r.materials = mats;
			}
		}
		foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
		{
			ps.Play(true);
		}
		// Fit it to about a third of its distance high (a big shape, but the road and the boss
		// stay clear), standing a little sunk into the ground.
		float dist = Mathf.Max(32f, m_backdropDist);
		float scale = 1f;
		float bottom = 0f;
		float height = 10f;
		wrapper.transform.position = m_stage.position;
		if (Pictures.MeshBounds(go, out Bounds bounds) && bounds.size.y > 0.1f)
		{
			float wide = Mathf.Max(bounds.size.x, bounds.size.z, 0.1f);
			scale = Mathf.Clamp(Mathf.Min(dist * (location ? 0.34f : 0.4f) / bounds.size.y, dist * 1.3f / wide), 0.3f, location ? 8f : 12f) * def.BackdropSize;
			bottom = bounds.min.y - m_stage.position.y;
			height = bounds.size.y * scale;
		}
		wrapper.transform.localScale = Vector3.one * scale;
		// Sunk well into the ground (1.16.1): with its foot out of sight it reads as something on
		// the skyline, not a thing sliding along the path ahead.
		m_backdropLift = -bottom * scale - Mathf.Max(1.5f, height * 0.14f);
		m_backdropHeight = height;
		m_backdrop = wrapper;
		m_backdrop.SetActive(false);
		Plugin.Log.LogInfo($"Arena backdrop for {def.Name}: {(location ? def.Location : def.Prefab + " (shadow)")}, x{scale:0.00}, {height:0} m high.");
	}

	private void DestroyBackdrop()
	{
		if (m_backdrop != null)
		{
			Destroy(m_backdrop);
		}
		m_backdrop = null;
		m_backdropDef = null;
	}

	// A lifeless copy of a ZoneSystem location prefab (null if this game has no such location
	// or it can't load).
	private GameObject? LocationPicture(string name)
	{
		if (name.Length == 0)
		{
			return null;
		}
		if (m_locationPictures.TryGetValue(name, out GameObject? cached))
		{
			return cached;
		}
		GameObject? picture = null;
		try
		{
			GameObject? prefab = LocationAsset(name);
			if (prefab != null)
			{
				picture = Pictures.MakePicture(prefab, m_templates, m_layer);
			}
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"Couldn't picture the {name} location: {e.Message}");
			picture = null;
		}
		if (picture == null)
		{
			Plugin.Log.LogInfo($"No {name} location to show (using the boss's shadow instead).");
		}
		m_locationPictures[name] = picture;
		return picture;
	}

	private readonly Dictionary<string, GameObject?> m_locationAssets = new();
	// The altar's summoning effects in a boss location (OfferingBowl): the sparkles as the
	// items are given, and the boss forming, after its delay.
	private readonly Dictionary<string, (EffectList? start, EffectList? done, float delay)> m_summonFx = new();

	// A boss location's own prefab (loaded on demand; released when the minigame closes), and
	// what we read from it before it's pictured: its weather zone and its altar's effects.
	private GameObject? LocationAsset(string name)
	{
		if (name.Length == 0)
		{
			return null;
		}
		if (m_locationAssets.TryGetValue(name, out GameObject? cached))
		{
			return cached;
		}
		GameObject? prefab = null;
		try
		{
			if (ZoneSystem.instance != null)
			{
				foreach (ZoneSystem.ZoneLocation loc in ZoneSystem.instance.m_locations)
				{
					if (loc == null || !loc.m_prefab.IsValid || loc.m_prefab.Name != name)
					{
						continue;
					}
					loc.m_prefab.Load();
					m_heldLocations.Add(loc);
					prefab = loc.m_prefab.Asset;
					break;
				}
			}
			if (prefab != null)
			{
				EnvZone zone = prefab.GetComponentInChildren<EnvZone>(true);
				if (zone != null && !string.IsNullOrEmpty(zone.m_environment))
				{
					m_locationEnv[name] = zone.m_environment;
				}
				OfferingBowl bowl = prefab.GetComponentInChildren<OfferingBowl>(true);
				if (bowl != null)
				{
					m_summonFx[name] = (
						AccessTools.Field(typeof(OfferingBowl), "m_spawnBossStartEffects")?.GetValue(bowl) as EffectList,
						AccessTools.Field(typeof(OfferingBowl), "m_spawnBossDoneffects")?.GetValue(bowl) as EffectList,
						AccessTools.Field(typeof(OfferingBowl), "m_spawnBossDelay")?.GetValue(bowl) is float d ? d : 3f);
				}
			}
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"Couldn't load the {name} location: {e.Message}");
			prefab = null;
		}
		m_locationAssets[name] = prefab;
		return prefab;
	}

	private void ReleaseLocations()
	{
		DestroyBackdrop();
		foreach (GameObject? picture in m_locationPictures.Values)
		{
			if (picture != null)
			{
				Destroy(picture);
			}
		}
		m_locationPictures.Clear();
		foreach (ZoneSystem.ZoneLocation loc in m_heldLocations)
		{
			try
			{
				if (loc.m_prefab.IsValid)
				{
					loc.m_prefab.Release();
				}
			}
			catch (Exception)
			{
			}
		}
		m_heldLocations.Clear();
		m_locationAssets.Clear();
	}
}
