using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16: the sea itself for the ocean crossing (SurfSea.cs). One big grid of water that follows
// the boat, its waves worked out in stage space every frame (so the swell stays put on the water
// while the grid moves along), gentle swell in calm weather and big rolling waves in the storm.
// Foam for the boat's wake, the wind's streaks and the waves' crests is drawn as flat quads in one
// mesh. All on our own layer, like everything else in the minigame.
internal sealed partial class SurfGame
{
	// Still-water height on the stage, and the size of the water grid.
	private const float SeaY = -1.6f;
	private const int OceanN = 73;
	private const float OceanStep = 3.4f;
	// The grid's outer ring reaches this many times further out, to the horizon.
	private const float RimReach = 8f;
	private static readonly Bounds OceanBounds = new Bounds(new Vector3(0f, SeaY, 0f), new Vector3((OceanN - 1) * OceanStep * RimReach + 10f, 40f, (OceanN - 1) * OceanStep * RimReach + 10f));
	// One ripple tile per this many metres (the texture is laid out in stage space).
	private const float RippleTile = 9f;

	private sealed class Roller
	{
		// Which way it travels (flat, unit length) and where its crest is along that line.
		public Vector2 Dir;
		public float Crest;
		public float Speed;
		public float Height;
		public float Age;
		// It has reached the boat (and been judged).
		public bool Passed;
		// A serpent's slam: a smaller wave from one spot, spreading as it goes.
		public bool Small;
		public Vector2 Origin;
		public float StartCrest;
	}

	// How much of a rolling wave reaches this spot across its front: all of a storm wave, but a
	// serpent's slam only near the line it was sent along, widening as it travels.
	private static float RollerReach(Roller r, float x, float z)
	{
		if (!r.Small)
		{
			return 1f;
		}
		float side = Mathf.Abs((x - r.Origin.x) * r.Dir.y - (z - r.Origin.y) * r.Dir.x);
		float span = 7f + 0.55f * Mathf.Max(0f, r.Crest - r.StartCrest);
		return Mathf.Clamp01(1.4f - side / span);
	}

	private sealed class Foam
	{
		public Vector2 P;
		public Vector2 V;
		public Vector2 Axis;
		public float Width;
		public float Length;
		public float Grow;
		public float Age;
		public float Life;
		public float Alpha;
	}

	private GameObject? m_ocean;
	private Mesh? m_oceanMesh;
	private Vector3[] m_oceanVerts = new Vector3[0];
	private Material? m_oceanMaterial;
	private GameObject? m_foamGo;
	private Mesh? m_foamMesh;
	private Material? m_foamMaterial;
	private readonly List<Roller> m_rollers = new();
	private readonly List<Foam> m_foam = new();
	private float m_seaClock;
	private float m_streakTimer;
	private Vector2 m_rippleDrift;
	private float m_surfTimer;
	private float m_reefTimer;
	private Segment? m_beachSea;
	private float m_beachNear = float.NaN;
	private float m_beachFar = float.NaN;
	private static Texture2D? s_seaNormals;
	private static Texture2D? s_foamTex;
	private readonly List<Vector3> m_fv = new();
	private readonly List<Vector2> m_fuv = new();
	private readonly List<Color> m_fc = new();
	private readonly List<int> m_ft = new();

	// The water's height at a stage point: the swell, stronger in the storm, plus every rolling
	// wave. A wave's front is steeper than its back, with a trough ahead of it.
	private float SeaHeight(float x, float z)
	{
		float t = m_seaClock;
		float calm = Mathf.Lerp(0.22f, 0.75f, m_storm) + 0.12f * m_windStrength;
		float h = calm * (0.55f * Mathf.Sin(x * 0.11f + z * 0.07f + t * 1.3f)
			+ 0.3f * Mathf.Sin(-x * 0.05f + z * 0.16f + t * 1.9f)
			+ 0.15f * Mathf.Sin(x * 0.23f - z * 0.19f + t * 2.7f));
		foreach (Roller r in m_rollers)
		{
			float s = x * r.Dir.x + z * r.Dir.y - r.Crest;
			if (s > 30f || s < -40f)
			{
				continue;
			}
			float fade = Mathf.Clamp01(r.Age / 1.5f) * RollerReach(r, x, z);
			if (fade <= 0f)
			{
				continue;
			}
			float w = s > 0f ? 4.5f : 8f;
			float shape = Mathf.Exp(-(s / w) * (s / w)) - 0.28f * Mathf.Exp(-((s - 8f) / 5f) * ((s - 8f) / 5f));
			h += r.Height * fade * shape;
		}
		return SeaY + h;
	}

	// Is any of the crossing in sight (or are we on it)?
	private Segment? SeaInView()
	{
		foreach (Segment s in m_segments)
		{
			if (s.Sea && s.Start - m_z < AheadDistance + 130f && (s.End == float.MaxValue || s.End - m_z > -160f))
			{
				return s;
			}
		}
		return null;
	}

	private void UpdateOcean(float dt)
	{
		Segment? sea = m_biomes.Count > 0 && m_segments.Count > 0 ? SeaInView() : null;
		if (sea == null)
		{
			if (m_ocean != null && m_ocean.activeSelf)
			{
				m_ocean.SetActive(false);
			}
			if (m_foamGo != null && m_foamGo.activeSelf)
			{
				m_foamGo.SetActive(false);
			}
			m_rollers.Clear();
			m_foam.Clear();
			return;
		}
		if (m_ocean == null)
		{
			MakeOcean();
		}
		m_ocean!.SetActive(true);
		m_foamGo!.SetActive(true);
		m_seaClock += dt;

		// Rolling waves move on, and go once well past (or left far behind by a fast ship).
		Vector2 boat = new Vector2(m_x, m_z);
		for (int i = m_rollers.Count - 1; i >= 0; i--)
		{
			Roller r = m_rollers[i];
			r.Age += dt;
			r.Crest += r.Speed * dt;
			float ahead = r.Crest - Vector2.Dot(boat, r.Dir);
			if (ahead > 70f || ahead < -90f || r.Age > 40f)
			{
				m_rollers.RemoveAt(i);
			}
		}

		// The grid snaps to its own spacing, so the waves don't crawl as it moves.
		float end = sea.End == float.MaxValue ? float.MaxValue : sea.End - 40f;
		float cz = Mathf.Clamp(m_z + 22f, sea.Start + 45f, Mathf.Max(sea.Start + 45f, end));
		float cx = Mathf.Round(m_x * 0.6f / OceanStep) * OceanStep;
		cz = Mathf.Round(cz / OceanStep) * OceanStep;
		m_ocean.transform.localPosition = new Vector3(cx, 0f, cz);
		// No sea inland of either coast: there it tucks away under the land.
		// (The land beside the track goes under further inland than the track: SurfSea.cs.)
		float back = sea.Start - ShoreRamp * SideReach - 6f;
		float front = sea.End == float.MaxValue ? float.MaxValue : sea.End + ShoreRamp * SideReach + 6f;
		float half = (OceanN - 1) * OceanStep * 0.5f;
		for (int iz = 0; iz < OceanN; iz++)
		{
			float lz = -half + iz * OceanStep;
			for (int ix = 0; ix < OceanN; ix++)
			{
				float lx = -half + ix * OceanStep;
				bool rim = ix == 0 || iz == 0 || ix == OceanN - 1 || iz == OceanN - 1;
				if (rim)
				{
					lx *= RimReach;
					lz *= RimReach;
				}
				float wz = cz + lz;
				float y = rim ? SeaY : SeaHeight(cx + lx, wz);
				if (wz < back || wz > front)
				{
					y = SeaY - 6f;
				}
				if (float.IsNaN(y) || float.IsInfinity(y))
				{
					y = SeaY;
				}
				m_oceanVerts[iz * OceanN + ix] = new Vector3(lx, y, lz);
			}
		}
		m_oceanMesh!.vertices = m_oceanVerts;
		m_oceanMesh.RecalculateNormals();
		// 1.16.2 (Sam: the water was missing; his log had "abnormal mesh bounds" for this mesh
		// all along the coast, so the sea was culled): fixed bounds that always hold the grid,
		// never worked out from the vertices.
		m_oceanMesh.bounds = OceanBounds;

		if (m_oceanMaterial != null)
		{
			// Darker and greyer under the storm; the ripples drift with the wind.
			Color calm = new Color(0.10f, 0.27f, 0.33f, 0.88f);
			Color storm = new Color(0.07f, 0.13f, 0.15f, 0.92f);
			m_oceanMaterial.color = Color.Lerp(calm, storm, m_storm);
			// The ripples drift with the wind, and stay put on the water as the grid moves.
			m_rippleDrift += WindVector() * (dt * (0.02f + 0.03f * m_windStrength));
			m_rippleDrift = new Vector2(Mathf.Repeat(m_rippleDrift.x, 1f), Mathf.Repeat(m_rippleDrift.y, 1f));
			Vector2 offset = m_rippleDrift + new Vector2(cx, cz) / RippleTile;
			m_oceanMaterial.mainTextureOffset = new Vector2(Mathf.Repeat(offset.x, 1f), Mathf.Repeat(offset.y, 1f));
		}

		UpdateSurf(sea, dt);
		UpdateWindStreaks(dt);
		UpdateFoam(dt);
	}

	// Surf on the beaches: lines of foam that form offshore and wash up the sand.
	private void UpdateSurf(Segment sea, float dt)
	{
		if (m_beachSea != sea || (float.IsNaN(m_beachFar) && sea.End != float.MaxValue))
		{
			m_beachSea = sea;
			m_beachNear = float.NaN;
			m_beachFar = float.NaN;
			for (float z = sea.Start - ShoreRamp; z < sea.Start; z += 0.5f)
			{
				if (RoadY(z) <= SeaY)
				{
					m_beachNear = z;
					break;
				}
			}
			if (sea.End != float.MaxValue)
			{
				for (float z = sea.End + ShoreRamp; z > sea.End; z -= 0.5f)
				{
					if (RoadY(z) <= SeaY)
					{
						m_beachFar = z;
						break;
					}
				}
			}
		}
		m_surfTimer -= dt;
		if (m_surfTimer > 0f)
		{
			return;
		}
		m_surfTimer = Random.Range(2.2f, 3.2f);
		foreach ((float line, float toShore) in new[] { (m_beachNear, -1f), (m_beachFar, 1f) })
		{
			if (float.IsNaN(line) || Mathf.Abs(line - m_z) > 140f)
			{
				continue;
			}
			for (float x = -30f; x <= 30f; x += 3.2f)
			{
				Vector2 at = new Vector2(x + Random.Range(-1f, 1f), line - toShore * Random.Range(4f, 6f));
				AddFoam(at, Random.Range(1.6f, 2.6f), Random.Range(2.2f, 2.8f), 0.5f, 0.8f, new Vector2(0f, toShore * Random.Range(1.6f, 2.2f)), new Vector2(1f, 0f), Random.Range(3f, 4.5f));
			}
		}
	}

	private void MakeOcean()
	{
		m_ocean = new GameObject("Ocean") { layer = m_layer };
		m_ocean.transform.SetParent(m_stage, false);
		m_oceanVerts = new Vector3[OceanN * OceanN];
		Vector2[] uv = new Vector2[m_oceanVerts.Length];
		float half = (OceanN - 1) * OceanStep * 0.5f;
		for (int iz = 0; iz < OceanN; iz++)
		{
			for (int ix = 0; ix < OceanN; ix++)
			{
				float lx = -half + ix * OceanStep;
				float lz = -half + iz * OceanStep;
				if (ix == 0 || iz == 0 || ix == OceanN - 1 || iz == OceanN - 1)
				{
					lx *= RimReach;
					lz *= RimReach;
				}
				m_oceanVerts[iz * OceanN + ix] = new Vector3(lx, SeaY, lz);
				// Laid out in metres; the material's offset adds where the grid is.
				uv[iz * OceanN + ix] = new Vector2(lx / RippleTile, lz / RippleTile);
			}
		}
		int[] tris = new int[(OceanN - 1) * (OceanN - 1) * 6];
		int k = 0;
		for (int iz = 0; iz < OceanN - 1; iz++)
		{
			for (int ix = 0; ix < OceanN - 1; ix++)
			{
				int a = iz * OceanN + ix;
				int b = a + 1;
				int c = a + OceanN;
				int d = c + 1;
				tris[k++] = a; tris[k++] = c; tris[k++] = d;
				tris[k++] = a; tris[k++] = d; tris[k++] = b;
			}
		}
		m_oceanMesh = new Mesh { name = "SurfOcean" };
		m_oceanMesh.MarkDynamic();
		m_oceanMesh.vertices = m_oceanVerts;
		m_oceanMesh.uv = uv;
		m_oceanMesh.triangles = tris;
		m_oceanMesh.RecalculateNormals();
		m_oceanMesh.bounds = OceanBounds;
		m_ocean.AddComponent<MeshFilter>().sharedMesh = m_oceanMesh;
		MeshRenderer r = m_ocean.AddComponent<MeshRenderer>();
		m_oceanMaterial = MakeOceanMaterial();
		r.sharedMaterial = m_oceanMaterial;
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		r.receiveShadows = false;

		m_foamGo = new GameObject("Foam") { layer = m_layer };
		m_foamGo.transform.SetParent(m_stage, false);
		m_foamMesh = new Mesh { name = "SurfFoam" };
		m_foamMesh.MarkDynamic();
		m_foamGo.AddComponent<MeshFilter>().sharedMesh = m_foamMesh;
		MeshRenderer fr = m_foamGo.AddComponent<MeshRenderer>();
		Shader sprite = Shader.Find("Sprites/Default");
		m_foamMaterial = new Material(sprite) { name = "SurfFoam", mainTexture = FoamTexture(), renderQueue = 3105 };
		fr.sharedMaterial = m_foamMaterial;
		fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		fr.receiveShadows = false;
	}

	private static Material? MakeOceanMaterial()
	{
		Shader? shader = FindPlainShader();
		if (shader == null)
		{
			return null;
		}
		Material mat = new Material(shader) { name = "SurfOcean" };
		if (shader.name == "Standard")
		{
			// The Standard shader's "Transparent" mode, which keeps the sun's glints.
			mat.SetFloat("_Mode", 3f);
			mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
			mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
			mat.SetInt("_ZWrite", 0);
			mat.DisableKeyword("_ALPHATEST_ON");
			mat.DisableKeyword("_ALPHABLEND_ON");
			mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
			mat.renderQueue = 3000;
			mat.SetFloat("_Glossiness", 0.86f);
			mat.SetFloat("_Metallic", 0.05f);
			mat.SetTexture("_BumpMap", SeaNormals());
			mat.SetFloat("_BumpScale", 0.55f);
			mat.EnableKeyword("_NORMALMAP");
		}
		mat.color = new Color(0.10f, 0.27f, 0.33f, 0.88f);
		return mat;
	}

	// Small ripples for the water's surface: a few sine waves that tile, made into a normal map.
	private static Texture2D SeaNormals()
	{
		if (s_seaNormals != null)
		{
			return s_seaNormals;
		}
		const int n = 128;
		Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, name = "SurfSeaNormals" };
		(int fx, int fy, float a, float p)[] waves =
		{
			(3, 1, 1f, 0.3f), (-2, 4, 0.8f, 1.7f), (5, -3, 0.5f, 2.9f), (1, 7, 0.35f, 4.1f), (-8, 5, 0.25f, 0.9f), (11, 9, 0.15f, 5.3f),
		};
		Color[] px = new Color[n * n];
		for (int y = 0; y < n; y++)
		{
			for (int x = 0; x < n; x++)
			{
				float u = x / (float)n;
				float v = y / (float)n;
				float du = 0f;
				float dv = 0f;
				foreach ((int fx, int fy, float a, float p) in waves)
				{
					float c = Mathf.Cos(2f * Mathf.PI * (fx * u + fy * v) + p) * a;
					du += c * fx;
					dv += c * fy;
				}
				Vector3 normal = new Vector3(-du * 0.05f, -dv * 0.05f, 1f).normalized;
				px[y * n + x] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f);
			}
		}
		tex.SetPixels(px);
		tex.Apply(true);
		s_seaNormals = tex;
		return tex;
	}

	// A soft, broken blob of foam.
	private static Texture2D FoamTexture()
	{
		if (s_foamTex != null)
		{
			return s_foamTex;
		}
		const int n = 64;
		Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SurfFoam" };
		Color[] px = new Color[n * n];
		for (int y = 0; y < n; y++)
		{
			for (int x = 0; x < n; x++)
			{
				float dx = (x + 0.5f) / n * 2f - 1f;
				float dy = (y + 0.5f) / n * 2f - 1f;
				float r = Mathf.Sqrt(dx * dx + dy * dy);
				float noise = Mathf.PerlinNoise(x * 0.21f, y * 0.21f) * 0.7f + Mathf.PerlinNoise(x * 0.55f + 13f, y * 0.55f) * 0.3f;
				float a = Mathf.Clamp01((1f - r) * 1.6f) * Mathf.Clamp01((noise - 0.25f) * 2.2f);
				px[y * n + x] = new Color(1f, 1f, 1f, a);
			}
		}
		tex.SetPixels(px);
		tex.Apply();
		s_foamTex = tex;
		return tex;
	}

	// A patch of foam on the water: `velocity` drifts it, `axis` stretches it (wind streaks).
	private void AddFoam(Vector2 p, float size, float life, float alpha = 0.8f, float grow = 1.5f, Vector2? velocity = null, Vector2? axis = null, float length = 0f)
	{
		if (m_foam.Count > 420)
		{
			return;
		}
		m_foam.Add(new Foam
		{
			P = p,
			V = velocity ?? Vector2.zero,
			Axis = axis ?? Vector2.zero,
			Width = size,
			Length = length > 0f ? length : size,
			Grow = grow,
			Life = life,
			Alpha = alpha,
		});
	}

	// The wind shows on the water as long faint streaks drifting with it. The reefs either side
	// of the channel show as lines of breaking surf.
	private void UpdateWindStreaks(float dt)
	{
		if (!m_atSea)
		{
			return;
		}
		m_reefTimer -= dt;
		while (m_reefTimer <= 0f)
		{
			m_reefTimer += 0.07f;
			float side = Random.value < 0.5f ? -1f : 1f;
			Vector2 reef = new Vector2(side * (SeaHalfWidth + 2.5f + Random.Range(0f, 3.5f)), m_z + Random.Range(-20f, 55f));
			AddFoam(reef, Random.Range(1.2f, 2.4f), Random.Range(1.2f, 2f), 0.45f + 0.2f * m_storm, 1.4f, new Vector2(0f, 0f), new Vector2(0f, 1f), Random.Range(2f, 4f));
		}
		m_streakTimer -= dt;
		// More streaks the harder it blows, and a rush of them while the wind turns.
		float rate = (0.05f + 0.12f * (1f - m_windStrength) - 0.03f * m_storm) * (1f - 0.6f * m_windShift);
		while (m_streakTimer <= 0f)
		{
			m_streakTimer += Mathf.Max(0.02f, rate);
			Vector2 wind = WindVector();
			Vector2 at = new Vector2(m_x + Random.Range(-38f, 38f), m_z + Random.Range(-18f, 46f));
			float speed = 3f + 7f * m_windStrength + 4f * m_storm;
			AddFoam(at, Random.Range(0.25f, 0.4f), Random.Range(1.6f, 2.6f), 0.22f + 0.12f * m_storm, 0f, wind * speed, wind, Random.Range(2.5f, 5f));
		}
	}

	private void UpdateFoam(float dt)
	{
		m_fv.Clear();
		m_fuv.Clear();
		m_fc.Clear();
		m_ft.Clear();
		for (int i = m_foam.Count - 1; i >= 0; i--)
		{
			Foam f = m_foam[i];
			f.Age += dt;
			if (f.Age >= f.Life)
			{
				m_foam.RemoveAt(i);
				continue;
			}
			f.P += f.V * dt;
			float p = f.Age / f.Life;
			float grow = 1f + f.Grow * p;
			float alpha = f.Alpha * Mathf.Clamp01(p * 6f) * (1f - p);
			Vector2 along = f.Axis.sqrMagnitude > 0.01f ? f.Axis.normalized : new Vector2(0f, 1f);
			Vector2 side = new Vector2(along.y, -along.x);
			FoamQuad(f.P, along * (f.Length * grow * 0.5f), side * (f.Width * grow * 0.5f), alpha);
		}
		// Each rolling wave's crest: a band of broken foam along its front.
		foreach (Roller r in m_rollers)
		{
			float fade = Mathf.Clamp01(r.Age / 1.5f) * Mathf.Clamp01(r.Height / 2f);
			Vector2 side = new Vector2(r.Dir.y, -r.Dir.x);
			// The point of the crest line nearest the boat (a slam's: on its own line), then out
			// to both sides.
			Vector2 from = r.Small ? r.Origin : new Vector2(m_x, m_z);
			Vector2 mid = from + r.Dir * (r.Crest + 1.2f - Vector2.Dot(from, r.Dir));
			float span = r.Small ? (7f + 0.55f * Mathf.Max(0f, r.Crest - r.StartCrest)) * 1.4f : 110f;
			int segments = r.Small ? 16 : 40;
			for (int j = 0; j < segments; j++)
			{
				float a = -span + 2f * span * j / segments;
				float b = a + 2f * span / segments;
				Vector2 pa = mid + side * a;
				float noise = Mathf.PerlinNoise(a * 0.15f + r.Crest * 0.05f, m_seaClock * 0.4f);
				float edge = r.Small ? Mathf.Clamp01(1f - Mathf.Abs(a) / span) : 1f;
				FoamQuad(pa + side * ((b - a) * 0.5f), side * ((b - a) * 0.55f), r.Dir * 1.6f, fade * edge * (0.35f + 0.5f * noise));
			}
		}
		if (m_foamMesh == null)
		{
			return;
		}
		m_foamMesh.Clear();
		m_foamMesh.SetVertices(m_fv);
		m_foamMesh.SetUVs(0, m_fuv);
		m_foamMesh.SetColors(m_fc);
		m_foamMesh.SetTriangles(m_ft, 0);
		m_foamMesh.RecalculateBounds();
	}

	private void FoamQuad(Vector2 centre, Vector2 halfAlong, Vector2 halfSide, float alpha)
	{
		if (alpha <= 0.01f)
		{
			return;
		}
		int i0 = m_fv.Count;
		Color c = new Color(0.95f, 0.97f, 1f, Mathf.Clamp01(alpha));
		// Corner by corner (no arrays: hundreds of quads a frame).
		for (int k = 0; k < 4; k++)
		{
			Vector2 p = centre + (k < 2 ? -halfAlong : halfAlong) + (k == 1 || k == 2 ? halfSide : -halfSide);
			m_fv.Add(new Vector3(p.x, SeaHeight(p.x, p.y) + 0.07f, p.y));
			m_fuv.Add(new Vector2(k == 1 || k == 2 ? 1f : 0f, k < 2 ? 0f : 1f));
			m_fc.Add(c);
		}
		m_ft.Add(i0); m_ft.Add(i0 + 1); m_ft.Add(i0 + 2);
		m_ft.Add(i0); m_ft.Add(i0 + 2); m_ft.Add(i0 + 3);
		// Seen from below too (a pitching boat's camera never is, but a wave can turn it).
		m_ft.Add(i0); m_ft.Add(i0 + 2); m_ft.Add(i0 + 1);
		m_ft.Add(i0); m_ft.Add(i0 + 3); m_ft.Add(i0 + 2);
	}

	// A rolling wave that will reach the boat in about `eta` seconds, travelling `dir`.
	private Roller SpawnRoller(Vector2 dir, float height, float eta, bool small = false, Vector2? from = null)
	{
		dir.Normalize();
		Vector2 boat = new Vector2(m_x, m_z);
		// Measured against the boat's own speed along the wave's way: a following sea always
		// overtakes the ship, as real ones do.
		Vector2 boatVel = HeadingVector() * m_boatSpeed;
		float along = Vector2.Dot(boatVel, dir);
		float speed = Mathf.Max(small ? 9f : 7.5f + 2.5f * m_storm, along + 4f);
		float closing = Mathf.Max(3f, speed - along);
		float crest = from.HasValue ? Vector2.Dot(from.Value, dir) : Vector2.Dot(boat, dir) - closing * eta;
		Roller r = new Roller { Dir = dir, Crest = crest, StartCrest = crest, Speed = speed, Height = height, Small = small, Origin = from ?? boat };
		m_rollers.Add(r);
		return r;
	}

	private void ClearOcean()
	{
		m_rollers.Clear();
		m_foam.Clear();
		m_beachSea = null;
		if (m_ocean != null)
		{
			m_ocean.SetActive(false);
		}
		if (m_foamGo != null)
		{
			m_foamGo.SetActive(false);
		}
	}

	private void DestroyOcean()
	{
		foreach (Object o in new Object?[] { m_ocean, m_foamGo, m_oceanMesh, m_foamMesh, m_oceanMaterial, m_foamMaterial })
		{
			if (o != null)
			{
				Destroy(o);
			}
		}
	}
}
