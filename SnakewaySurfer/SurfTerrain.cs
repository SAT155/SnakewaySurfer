using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SnakewaySurfer;

// The land beside the track, shaped by this world's own terrain generator: each biome borrows a
// ring of real ground (WorldGenerator.GetBiomeHeight, the biome's own height formula) somewhere in
// this world where that biome is, and the track runs round it. Heights are relative to the
// ring's centre line, so the road keeps its own gentle roll. The generator's paint mask (its
// alpha: Mistlands cliffs, vegetation) goes to the terrain shader the way Heightmap does it, and
// the game's own grass (ClutterSystem's clutter list, drawn instanced) grows by the game's rules.
internal sealed partial class SurfGame
{
	private sealed class TerrainSource
	{
		public Heightmap.Biome Biome;
		public Vector2 Center;
		public float Radius = 170f;
	}

	private TerrainSource?[] m_sources = new TerrainSource?[0];
	private readonly Dictionary<long, float> m_centerHeights = new();
	private const float SeaLevel = 30f;

	// ---------------------------------------------------------------- finding the places

	private void FindTerrainSources()
	{
		m_sources = new TerrainSource?[m_biomes.Count];
		m_centerHeights.Clear();
		if (!Plugin.GameTerrainShape.Value || WorldGenerator.instance == null)
		{
			return;
		}
		System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
		for (int i = 0; i < m_biomes.Count && i < SpawnTables.s_order.Length; i++)
		{
			try
			{
				m_sources[i] = FindSource(SpawnTables.s_order[i]);
			}
			catch (Exception e)
			{
				Plugin.Log.LogWarning($"Terrain for {m_biomes[i].Def.Name}: {e.Message}");
			}
			Plugin.Log.LogInfo(m_sources[i] is TerrainSource s
				? $"Terrain for {m_biomes[i].Def.Name}: this world's ground around ({s.Center.x:0}, {s.Center.y:0})."
				: $"Terrain for {m_biomes[i].Def.Name}: no fitting spot found, using the minigame's own hills.");
		}
		Plugin.Log.LogInfo($"Terrain search took {watch.ElapsedMilliseconds} ms.");
	}

	// A spot where a ring (radius 170 m, 90 m either side) is all this biome, and dry unless it's
	// the Swamp. Searched outward from the world centre, so it's the nearest such spot.
	// For the `surfkit` biome tour: the same spot the terrain is borrowed from.
	internal static bool BiomeSpot(Heightmap.Biome biome, out Vector2 spot)
	{
		spot = default;
		if (WorldGenerator.instance == null)
		{
			return false;
		}
		TerrainSource? s = FindSource(biome);
		if (s == null)
		{
			return false;
		}
		spot = s.Center;
		return true;
	}

	private static TerrainSource? FindSource(Heightmap.Biome biome)
	{
		WorldGenerator wg = WorldGenerator.instance;
		const float radius = 170f;
		TerrainSource? best = null;
		float bestScore = 0f;
		int tried = 0;
		for (float r = 300f; r < 9600f && tried < 260; r += 220f)
		{
			int steps = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / 300f));
			float turn = r * 0.37f;
			for (int k = 0; k < steps && tried < 260; k++)
			{
				float a = turn + k * 2f * Mathf.PI / steps;
				Vector2 c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
				if (wg.GetBiome(c.x, c.y) != biome)
				{
					continue;
				}
				tried++;
				float score = RingScore(wg, biome, c, radius);
				if (score > bestScore)
				{
					bestScore = score;
					best = new TerrainSource { Biome = biome, Center = c, Radius = radius };
					if (score >= 0.97f)
					{
						return best;
					}
				}
			}
		}
		return bestScore >= 0.8f ? best : null;
	}

	private static float RingScore(WorldGenerator wg, Heightmap.Biome biome, Vector2 c, float radius)
	{
		int good = 0;
		int total = 0;
		foreach (float dr in new[] { -80f, 0f, 80f })
		{
			for (int k = 0; k < 12; k++)
			{
				float a = k * Mathf.PI / 6f;
				Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius + dr);
				total++;
				if (wg.GetBiome(p.x, p.y) != biome)
				{
					continue;
				}
				if (biome != Heightmap.Biome.Swamp && dr == 0f && wg.GetBiomeHeight(biome, p.x, p.y, out _) < SeaLevel + 1f)
				{
					continue;
				}
				good++;
			}
		}
		return (float)good / total;
	}

	// Track (x across, z along) to the world spot it borrows.
	private static Vector2 SourcePoint(TerrainSource s, float x, float z)
	{
		float a = z / s.Radius;
		return s.Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (s.Radius + x);
	}

	private float SourceHeight(TerrainSource s, float x, float z, out float veg)
	{
		Vector2 p = SourcePoint(s, x, z);
		float h = WorldGenerator.instance.GetBiomeHeight(s.Biome, p.x, p.y, out Color mask);
		veg = Mathf.Clamp01(mask.a);
		return h;
	}

	private float CenterHeight(int biome, TerrainSource s, float z)
	{
		long key = (long)biome * 100000000L + Mathf.RoundToInt(z * 4f);
		if (!m_centerHeights.TryGetValue(key, out float h))
		{
			if (m_centerHeights.Count > 40000)
			{
				m_centerHeights.Clear();
			}
			h = SourceHeight(s, 0f, z, out _);
			m_centerHeights[key] = h;
		}
		return h;
	}

	// Real ground height beside the track relative to the road, for one biome.
	private float RealRelative(int biome, TerrainSource s, float x, float z, out float h, out float veg)
	{
		h = SourceHeight(s, x, z, out veg);
		if (s.Biome == Heightmap.Biome.Swamp)
		{
			// The swamp's water is the sea level: our water lies WaterDepth above the road.
			return WaterDepth + (h - SeaLevel);
		}
		// No water is drawn outside the Swamp, so dips stop at the shore.
		float c = Mathf.Max(CenterHeight(biome, s, z), SeaLevel + 0.3f);
		return Mathf.Max(h, SeaLevel + 0.3f) - c;
	}

	// The land's height at (x, z), plus the real height above the sea and the paint mask's
	// vegetation alpha there (for the shader and the grass).
	private float LandAt(float x, float z, out float realH, out float veg)
	{
		float y = LandBase(x, z, out realH, out veg);
		// 1.16: sinking under the sea at a crossing's shores (SurfSea.cs).
		return ShoreLand(y, x, z, ref veg);
	}

	private float LandBase(float x, float z, out float realH, out float veg)
	{
		float road = RoadY(z);
		float water = WaterWeight(z);
		// Where bosses split the path into extra lanes (the Elder's roots), the ground beside
		// the track is flat for them, and the land starts past them.
		float out_ = Mathf.Abs(x) - TrackHalf - ExtraFlat(z);
		realH = SeaLevel + 2f;
		veg = 1f;
		float edge = road - 0.05f - 0.6f * water;
		if (out_ <= 0f)
		{
			return edge;
		}
		// The minigame's own hills (and the fallback for a biome with no real spot).
		float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((out_ - 2.5f) / 22f));
		float n = Mathf.PerlinNoise((x + 500f) * 0.035f, z * 0.035f) * 0.75f + Mathf.PerlinNoise(x * 0.11f + 91f, z * 0.11f) * 0.25f;
		float amp = HillAmplitude(z);
		float dry = -0.05f + ramp * amp * (0.25f + n);
		float wet = -0.65f + Mathf.Lerp(0.3f, 1f, ramp) * amp * (n * 1.6f - 0.45f);
		float own = road + Mathf.Lerp(dry, wet, water);
		float walls = ArenaWalls(z, out_) * (0.8f + 0.4f * n);

		BiomeBlend(z, out int a, out int b, out float blend);
		TerrainSource? sa = a < m_sources.Length ? m_sources[a] : null;
		TerrainSource? sb = b < m_sources.Length ? m_sources[b] : null;
		if (sa == null && sb == null)
		{
			return own + walls;
		}
		// Real ground meets the path within a few metres, as a dug path does.
		float near = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((out_ - 0.8f) / 9f));
		float scale = Plugin.HillHeight.Value;
		float ya, yb, ha = realH, hb = realH, va = 1f, vb = 1f;
		ya = sa != null ? Mathf.Lerp(edge, road + RealRelative(a, sa, x, z, out ha, out va) * scale, near) : own;
		if (blend <= 0.001f || b == a)
		{
			yb = ya;
			hb = ha;
			vb = va;
		}
		else
		{
			yb = sb != null ? Mathf.Lerp(edge, road + RealRelative(b, sb, x, z, out hb, out vb) * scale, near) : own;
		}
		realH = Mathf.Lerp(sa != null ? ha : hb, sb != null ? hb : ha, blend);
		veg = Mathf.Lerp(sa != null ? va : vb, sb != null ? vb : va, blend);
		return Mathf.Lerp(ya, yb, blend) + walls;
	}

	// Where the real sea level (30 m) lies in the stage's local height at z, or null with no
	// real ground here.
	private float? SeaLocalY(float z)
	{
		BiomeBlend(z, out int a, out int b, out float blend);
		float? ya = SeaFor(a, z);
		float? yb = b == a ? ya : SeaFor(b, z);
		if (ya == null && yb == null)
		{
			return null;
		}
		return Mathf.Lerp(ya ?? yb!.Value, yb ?? ya!.Value, blend);
	}

	private float? SeaFor(int biome, float z)
	{
		TerrainSource? s = biome < m_sources.Length ? m_sources[biome] : null;
		if (s == null)
		{
			return null;
		}
		float road = RoadY(z);
		if (s.Biome == Heightmap.Biome.Swamp)
		{
			return road + WaterDepth;
		}
		return road + (SeaLevel - Mathf.Max(CenterHeight(biome, s, z), SeaLevel + 0.3f)) * Plugin.HillHeight.Value;
	}

	// ---------------------------------------------------------------- land tiles

	private sealed class LandGrid
	{
		public int Nx;
		public int Nz;
		public int Side;
		public float CenterZ;
		public float[] Y = new float[0];
		public float[] H = new float[0];
		public float[] Veg = new float[0];
	}

	private float GridX(int side, int ix) => side * (TrackHalf - 0.2f + ix * LandStep);

	// Height at a local x/z inside a tile's grid (bilinear), and its normal.
	private static float SampleGrid(LandGrid g, float[] values, float ax, float lz)
	{
		float fx = Mathf.Clamp((ax - (TrackHalf - 0.2f)) / LandStep, 0f, g.Nx - 1.001f);
		float fz = Mathf.Clamp((lz + GroundTile * 0.5f) / GroundTile * (g.Nz - 1), 0f, g.Nz - 1.001f);
		int ix = (int)fx;
		int iz = (int)fz;
		float tx = fx - ix;
		float tz = fz - iz;
		float a = Mathf.Lerp(values[iz * g.Nx + ix], values[iz * g.Nx + ix + 1], tx);
		float b = Mathf.Lerp(values[(iz + 1) * g.Nx + ix], values[(iz + 1) * g.Nx + ix + 1], tx);
		return Mathf.Lerp(a, b, tz);
	}

	// A soft per-tile texture holding the generator's paint mask (alpha), addressed by uv0 as
	// on a Heightmap; destroyed with the tile.
	private Texture2D MaskFor(LandGrid g)
	{
		Texture2D tex = new Texture2D(g.Nx, g.Nz, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SurfLandMask" };
		Color32[] px = new Color32[g.Nx * g.Nz];
		for (int i = 0; i < px.Length; i++)
		{
			px[i] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(g.Veg[i]) * 255f));
		}
		tex.SetPixels32(px);
		tex.Apply();
		return tex;
	}

	private void ApplyLandMask(GameObject land, MeshRenderer r, LandGrid g)
	{
		if (m_terrainLand == null || m_sources.Length == 0 || Array.TrueForAll(m_sources, s => s == null))
		{
			return;
		}
		Texture2D tex = MaskFor(g);
		MaterialPropertyBlock block = new MaterialPropertyBlock();
		r.GetPropertyBlock(block);
		block.SetTexture("_ClearedMaskTex", tex);
		block.SetFloat(LodHide, 0f);
		// The shader shades by height above the sea (shores, wet ground): put this tile's sea
		// where the real sea level falls on it.
		if (m_terrainLand.HasProperty("_WaterLevel") && Mathf.Max(ShoreWeight(g.CenterZ - GroundTile * 0.5f), ShoreWeight(g.CenterZ + GroundTile * 0.5f)) > 0f)
		{
			// A crossing's shore: wet sand and shallows down to our own sea.
			block.SetFloat("_WaterLevel", m_stage.position.y + SeaY);
		}
		else if (m_terrainLand.HasProperty("_WaterLevel") && SeaLocalY(g.CenterZ) is float sea)
		{
			block.SetFloat("_WaterLevel", m_stage.position.y + sea);
		}
		r.SetPropertyBlock(block);
		land.AddComponent<SurfOwned>().Owned = tex;
	}

	// ---------------------------------------------------------------- the game's grass

	private sealed class GrassBatch
	{
		public GameObject Owner = null!;
		public Mesh Mesh = null!;
		public Material Material = null!;
		public UnityEngine.Rendering.ShadowCastingMode Shadows;
		public Matrix4x4[] Matrices = new Matrix4x4[1023];
		public int Count;
		public float Z;
	}

	private readonly List<GrassBatch> m_grass = new();
	private List<ClutterSystem.Clutter>? m_clutter;
	private float m_clutterWater = 27f;
	private float m_clutterAmount = 1f;
	private bool m_grassBroken;

	private void LoadClutter()
	{
		m_clutter = new List<ClutterSystem.Clutter>();
		if (!Plugin.GameGrass.Value)
		{
			return;
		}
		ClutterSystem cs = Object.FindObjectOfType<ClutterSystem>();
		if (cs == null)
		{
			Plugin.Log.LogInfo("No ClutterSystem: no grass.");
			return;
		}
		m_clutterWater = cs.m_waterLevel;
		m_clutterAmount = cs.m_amountScale;
		try
		{
			// Low/Med/High quality: a quarter, half or all of each clutter's amount, as the game.
			object q = AccessTools.Field(typeof(ClutterSystem), "m_quality").GetValue(cs);
			int quality = Convert.ToInt32(q);
			m_clutterAmount *= quality <= 0 ? 0f : quality == 1 ? 0.25f : quality == 2 ? 0.5f : 1f;
		}
		catch (Exception)
		{
			m_clutterAmount *= 0.5f;
		}
		foreach (ClutterSystem.Clutter c in cs.m_clutter)
		{
			if (c != null && c.m_enabled && c.m_prefab != null)
			{
				m_clutter.Add(c);
			}
		}
		Plugin.Log.LogInfo($"Grass: {m_clutter.Count} clutter types from the game (amount x{m_clutterAmount:0.00}).");
	}

	// Grows the game's clutter on one side of a land tile, by ClutterSystem's own rules
	// (biome, amount per 8 m patch, forest and fractal masks, altitude, tilt, vegetation mask).
	private void SpawnGrass(GameObject root, LandGrid g)
	{
		if (m_clutter == null)
		{
			LoadClutter();
		}
		if (m_grassBroken || m_clutter == null || m_clutter.Count == 0 || m_clutterAmount <= 0f)
		{
			return;
		}
		const float patch = 8f;
		const float reach = 26f;
		System.Random rng = new System.Random(unchecked((int)(g.CenterZ * 7.31f) * 31 + g.Side));
		// (The frame of a burial chamber's hall, past one: SurfCrypt.cs.)
		Matrix4x4 toWorld = StageMatrix(g.CenterZ);
		Dictionary<int, GrassBatch> batches = new();
		int loose = 0;
		for (float pz = -GroundTile * 0.5f; pz < GroundTile * 0.5f - 0.01f; pz += patch)
		{
			float z = g.CenterZ + pz + patch * 0.5f;
			BiomeBlend(z, out int ba, out int bb, out float blend);
			for (float px = TrackHalf + 0.6f; px < TrackHalf + reach; px += patch)
			{
				for (int ci = 0; ci < m_clutter.Count; ci++)
				{
					ClutterSystem.Clutter c = m_clutter[ci];
					if (!c.m_onUncleared || (c.m_minOceanDepth != c.m_maxOceanDepth && c.m_minOceanDepth > 0f))
					{
						continue;
					}
					int amount = (int)(c.m_amount * m_clutterAmount);
					float cosMax = Mathf.Cos(Mathf.Deg2Rad * c.m_maxTilt);
					float cosMin = Mathf.Cos(Mathf.Deg2Rad * c.m_minTilt);
					for (int j = 0; j < amount; j++)
					{
						float ax = px + (float)rng.NextDouble() * patch;
						float lz = pz + (float)rng.NextDouble() * patch;
						float rot = (float)rng.NextDouble() * 360f;
						int bi = rng.NextDouble() < blend ? bb : ba;
						Heightmap.Biome biome = SpawnTables.s_order[Mathf.Min(bi, SpawnTables.s_order.Length - 1)];
						if ((c.m_biome & biome) == 0)
						{
							continue;
						}
						float x = g.Side * ax;
						float wz = g.CenterZ + lz;
						TerrainSource? src = bi < m_sources.Length ? m_sources[bi] : null;
						Vector2 wp = src != null ? SourcePoint(src, x, wz) : new Vector2(x + 5000f * bi, wz);
						Vector3 world = new Vector3(wp.x, 0f, wp.y);
						if (c.m_inForest)
						{
							float forest = WorldGenerator.GetForestFactor(world);
							if (forest < c.m_forestTresholdMin || forest > c.m_forestTresholdMax)
							{
								continue;
							}
						}
						if (c.m_fractalScale > 0f)
						{
							float f = Utils.Fbm(world * 0.01f * c.m_fractalScale + new Vector3(c.m_fractalOffset, 0f, 0f), 3, 1.6f, 0.7f);
							if (f < c.m_fractalTresholdMin || f > c.m_fractalTresholdMax)
							{
								continue;
							}
						}
						float y = SampleGrid(g, g.Y, ax, lz);
						float h = SampleGrid(g, g.H, ax, lz);
						float alt = h - m_clutterWater;
						if (alt < c.m_minAlt || alt > c.m_maxAlt)
						{
							continue;
						}
						float dx = (SampleGrid(g, g.Y, ax + 0.75f, lz) - SampleGrid(g, g.Y, ax - 0.75f, lz)) / 1.5f * g.Side;
						float dz = (SampleGrid(g, g.Y, ax, lz + 0.75f) - SampleGrid(g, g.Y, ax, lz - 0.75f)) / 1.5f;
						Vector3 normal = new Vector3(-dx, 1f, -dz).normalized;
						if (normal.y < cosMax || normal.y > cosMin)
						{
							continue;
						}
						if (c.m_minVegetation != c.m_maxVegetation)
						{
							float veg = SampleGrid(g, g.Veg, ax, lz);
							if (veg > c.m_maxVegetation || veg < c.m_minVegetation)
							{
								continue;
							}
						}
						if (c.m_snapToWater)
						{
							if (WaterWeight(wz) < 0.5f)
							{
								continue;
							}
							y = RoadY(wz) + WaterDepth;
						}
						if (c.m_randomOffset != 0f)
						{
							y += ((float)rng.NextDouble() * 2f - 1f) * c.m_randomOffset;
						}
						Quaternion q = c.m_terrainTilt ? Quaternion.AngleAxis(rot, normal) : Quaternion.Euler(0f, rot, 0f);
						float s = Mathf.Lerp(c.m_scaleMin, c.m_scaleMax, (float)rng.NextDouble());
						Vector3 local = new Vector3(x, y, wz);
						if (c.m_instanced)
						{
							InstanceRenderer ir = c.m_prefab.GetComponent<InstanceRenderer>();
							if (ir == null || ir.m_mesh == null || ir.m_material == null)
							{
								continue;
							}
							if (!batches.TryGetValue(ci, out GrassBatch batch) || batch.Count >= batch.Matrices.Length)
							{
								batch = new GrassBatch { Owner = root, Mesh = ir.m_mesh, Material = ir.m_material, Shadows = ir.m_shadowCasting, Z = g.CenterZ };
								batches[ci] = batch;
								m_grass.Add(batch);
							}
							batch.Matrices[batch.Count++] = toWorld * Matrix4x4.TRS(local, q, ir.m_scale * s);
						}
						else if (loose < 40)
						{
							loose++;
							GameObject go = Instantiate(Picture(c.m_prefab), root.transform, false);
							go.transform.position = m_stage.TransformPoint(local);
							go.transform.rotation = m_stage.rotation * q;
							go.transform.localScale = Vector3.one * s;
						}
					}
				}
			}
		}
	}

	// Each frame: draw the grass near the runner into the minigame's layer.
	private void DrawGrass()
	{
		if (m_grassBroken)
		{
			return;
		}
		for (int i = m_grass.Count - 1; i >= 0; i--)
		{
			GrassBatch b = m_grass[i];
			if (b.Owner == null)
			{
				m_grass.RemoveAt(i);
				continue;
			}
			// The game draws grass to 40 m; tiles are 40 m long.
			if (b.Count == 0 || b.Z < m_z - 45f || b.Z > m_z + 70f)
			{
				continue;
			}
			try
			{
				Graphics.DrawMeshInstanced(b.Mesh, 0, b.Material, b.Matrices, b.Count, null, b.Shadows, true, m_layer);
			}
			catch (Exception e)
			{
				Plugin.Log.LogWarning($"Grass can't be drawn here ({e.Message}); turning it off.");
				m_grassBroken = true;
				return;
			}
		}
	}
}

// Destroys an object (a generated texture) along with the GameObject that uses it.
internal sealed class SurfOwned : MonoBehaviour
{
	public Object? Owned;

	private void OnDestroy()
	{
		if (Owned != null)
		{
			Destroy(Owned);
		}
	}
}
