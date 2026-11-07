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
	// 1.16.26 (Sam: the big monument clipped into the track): from its middle to its near side,
	// so its nearest part keeps the distance, not its middle.
	private float m_backdropNear;
	// 1.16.27 (Sam: "flattened and warped, then reprojected ... to be a good skybox"): the place
	// is drawn once into a picture, and that picture stands far off on the skyline, upright and
	// facing you, free of the fog like the sky.
	private bool m_backdropSky;
	private Texture2D? m_backdropTexture;
	private Material? m_backdropMaterial;
	private float SkyDistance => Mathf.Min(m_camera.farClipPlane * 0.85f, 420f);
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
		// 1.16.79 (Sam: a much bigger battle area): the walls start 14 m out, not 5.
		return walls * Plugin.HillHeight.Value * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((out_ - 14f) / 25f));
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
		float wanted = Mathf.Clamp(0.85f / Mathf.Max(0.0005f, m_fogNow), 32f, 95f * ViewScale) * MonumentFar;
		wanted = Mathf.Max(wanted, m_backdropDef.Mode == "stand" ? m_backdropDef.Distance + 22f : m_backdropDef.Distance + 14f);
		if (m_backdropSky)
		{
			wanted = SkyDistance;
		}
		m_backdropDist = Mathf.Lerp(m_backdropDist, wanted, dt <= 0f ? 1f : 1f - Mathf.Exp(-1f * dt));
		float z = m_z + m_backdropDist + m_backdropNear;
		float k = Mathf.SmoothStep(0f, 1f, m_backdropShow);
		float y = RoadY(z) + m_backdropLift - (1f - k) * (m_backdropHeight + 2f);
		m_backdrop.transform.localPosition = new Vector3(0f, y, z);
		m_backdrop.SetActive(k > 0.001f);
		if (m_backdropSky)
		{
			// Upright, turned to face you.
			Vector3 look = m_backdrop.transform.position - m_camFinal;
			look.y = 0f;
			if (look.sqrMagnitude > 1f)
			{
				m_backdrop.transform.rotation = Quaternion.LookRotation(look);
			}
		}
	}

	// Boss Landmark Size: further off (a little), and taller against the sky (more).
	private static float MonumentSize => Mathf.Clamp(Plugin.MonumentSize.Value, 1f, 3f);
	private static float MonumentFar => 1f + (MonumentSize - 1f) * 0.5f;
	private static float MonumentTall => 1f + (MonumentSize - 1f) * 0.6f;

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
		// Sized for where it will stand (not where the last one stood).
		float dist = Mathf.Clamp(0.85f / Mathf.Max(0.0005f, m_fogNow), 32f, 95f * ViewScale) * MonumentFar;
		m_backdropDist = dist;
		float scale = 1f;
		float bottom = 0f;
		m_backdropNear = 0f;
		float height = 10f;
		wrapper.transform.position = m_stage.position;
		if (Pictures.MeshBounds(go, out Bounds bounds) && bounds.size.y > 0.1f)
		{
			float wide = Mathf.Max(bounds.size.x, bounds.size.z, 0.1f);
			// 1.16.21 (Sam): extra large, towering over the skyline (Boss Landmark Size).
			float tall = Mathf.Min(0.75f, (location ? 0.34f : 0.4f) * MonumentTall);
			scale = Mathf.Clamp(Mathf.Min(dist * tall / bounds.size.y, dist * 1.3f * MonumentTall / wide), 0.3f, (location ? 8f : 12f) * MonumentFar * MonumentTall) * def.BackdropSize;
			bottom = bounds.min.y - m_stage.position.y;
			m_backdropNear = Mathf.Max(0f, m_stage.position.z - bounds.min.z) * scale;
			height = bounds.size.y * scale;
		}
		wrapper.transform.localScale = Vector3.one * scale;
		m_backdropSky = false;
		if (Plugin.MonumentSky.Value && SkyBackdrop(wrapper, go, def, location))
		{
			return;
		}
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
		m_backdropSky = false;
		if (m_backdropTexture != null)
		{
			Destroy(m_backdropTexture);
			m_backdropTexture = null;
		}
		if (m_backdropMaterial != null)
		{
			Destroy(m_backdropMaterial);
			m_backdropMaterial = null;
		}
	}

	private static Color32[] Bake(Camera cam, RenderTexture rt, Color background)
	{
		cam.backgroundColor = new Color(background.r, background.g, background.b, 1f);
		cam.Render();
		RenderTexture old = RenderTexture.active;
		RenderTexture.active = rt;
		Texture2D read = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
		read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
		read.Apply(false);
		RenderTexture.active = old;
		Color32[] px = read.GetPixels32();
		Destroy(read);
		return px;
	}

	// Draws the place (already in `wrapper`, scaled) into a picture from in front, then swaps it
	// for a flat, upright picture sized for the skyline. False: keep it as it is.
	private bool SkyBackdrop(GameObject wrapper, GameObject go, BossDef def, bool location)
	{
		Shader? shader = Shader.Find("Sprites/Default");
		if (shader == null)
		{
			return false;
		}
		Camera? cam = null;
		bool fog = RenderSettings.fog;
		try
		{
			// Far below everything else, alone in view.
			wrapper.transform.position = m_stage.position + Vector3.down * 3000f;
			if (!Pictures.MeshBounds(go, out Bounds b) || b.size.y < 0.1f)
			{
				return false;
			}
			float aspect = Mathf.Clamp(b.size.x / b.size.y, 0.4f, 3f);
			int hpx = 1024;
			int wpx = Mathf.Clamp(Mathf.RoundToInt(hpx * aspect), 256, 2048);
			RenderTexture rt = new RenderTexture(wpx, hpx, 24, RenderTextureFormat.ARGB32) { name = "SurfSkyLandmark" };
			rt.Create();
			cam = new GameObject("SurfSkyBake").AddComponent<Camera>();
			cam.enabled = false;
			cam.cullingMask = 1 << m_layer;
			cam.clearFlags = CameraClearFlags.SolidColor;
			cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
			cam.orthographic = true;
			cam.orthographicSize = b.extents.y * 1.04f;
			cam.aspect = wpx / (float)hpx;
			cam.nearClipPlane = 1f;
			cam.farClipPlane = b.size.z + 200f;
			cam.allowHDR = false;
			cam.allowMSAA = true;
			cam.transform.position = b.center + Vector3.back * (b.extents.z + 60f);
			cam.transform.rotation = Quaternion.identity;
			cam.renderingPath = RenderingPath.Forward;
			cam.targetTexture = rt;
			RenderSettings.fog = false;
			// Drawn on black and on white: what differs is see-through (the game's shaders don't
			// all write a usable alpha).
			Color32[] onBlack = Bake(cam, rt, Color.black);
			Color32[] onWhite = Bake(cam, rt, Color.white);
			RenderSettings.fog = fog;
			cam.targetTexture = null;
			rt.Release();
			Destroy(rt);
			Color32[] px = new Color32[onBlack.Length];
			for (int i = 0; i < px.Length; i++)
			{
				Color32 k0 = onBlack[i];
				Color32 k1 = onWhite[i];
				int seen = 255 - ((k1.r - k0.r) + (k1.g - k0.g) + (k1.b - k0.b)) / 3;
				seen = Mathf.Clamp(seen, 0, 255);
				if (seen < 3)
				{
					px[i] = new Color32(0, 0, 0, 0);
					continue;
				}
				float un = 255f / seen;
				px[i] = new Color32((byte)Mathf.Min(255, k0.r * un), (byte)Mathf.Min(255, k0.g * un), (byte)Mathf.Min(255, k0.b * un), (byte)seen);
			}
			Texture2D tex = new Texture2D(wpx, hpx, TextureFormat.RGBA32, true) { name = "SurfSkyLandmark", wrapMode = TextureWrapMode.Clamp };
			tex.SetPixels32(px);
			tex.Apply(true, false);

			// The picture, as tall against the sky as the place would have stood.
			float tall = Mathf.Min(0.75f, (location ? 0.34f : 0.4f) * MonumentTall) * def.BackdropSize;
			float d = SkyDistance;
			float height = d * tall;
			float width = height * (wpx / (float)hpx);
			Destroy(go);
			wrapper.transform.localScale = Vector3.one;
			wrapper.transform.localPosition = Vector3.zero;
			GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
			Destroy(quad.GetComponent<Collider>());
			quad.name = "SkyLandmark";
			quad.layer = m_layer;
			quad.transform.SetParent(wrapper.transform, false);
			quad.transform.localScale = new Vector3(width, height, 1f);
			m_backdropMaterial = new Material(shader) { name = "SurfSkyLandmark", mainTexture = tex, color = Color.Lerp(Color.white, def.Tint, 0.12f) };
			// Behind everything else that's see-through.
			m_backdropMaterial.renderQueue = 2900;
			Renderer r = quad.GetComponent<Renderer>();
			r.sharedMaterial = m_backdropMaterial;
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			r.receiveShadows = false;
			m_backdropTexture = tex;
			m_backdropSky = true;
			m_backdropNear = 0f;
			m_backdropDist = d;
			// Its foot a little below the land.
			m_backdropLift = height * 0.5f - height * 0.08f;
			m_backdropHeight = height;
			m_backdrop = wrapper;
			m_backdrop.SetActive(false);
			Plugin.Log.LogInfo($"Arena backdrop for {def.Name} on the sky: {(location ? def.Location : def.Prefab + " (shadow)")}, {wpx}x{hpx}, {height:0} m high at {d:0} m.");
			return true;
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"Couldn't draw the boss's place onto the sky: {e.Message}");
			RenderSettings.fog = fog;
			wrapper.transform.position = m_stage.position;
			return false;
		}
		finally
		{
			if (cam != null)
			{
				Destroy(cam.gameObject);
			}
		}
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
