using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// 1.16.25 (Sam's sketch): at a dungeon corner a gust of wind waits just ahead of you, drawn as
// flat cut-out shapes with a dark inked edge (a hand-drawn, storybook look): a billowy cloud on
// the side away from the turn, spiky gusts leaning the way you should go, and puffs curling off
// toward the opening. The nearer you come to missing the turn, the more the cloud shrinks and
// the gusts grow; once you've turned, the cloud is squeezed against the wall and left behind
// while the gusts whoosh away round the corner. The outlines "boil" a little, like drawn frames.
internal sealed partial class SurfGame
{
	private GameObject? m_gust;
	private Transform? m_gustFlip;
	private Transform? m_gustSoft;
	private Transform? m_gustSharp;
	private Transform? m_gustTrail;
	private readonly List<(Transform t, Vector2 home, float r)> m_gustBlobs = new();
	private readonly List<(Transform t, Vector2 home, float h, float w)> m_gustSpikes = new();
	private readonly List<Transform> m_gustDots = new();
	private int m_gustCorner = -1;
	private float m_gustGone = -1f;
	private float m_gustBoilAt;
	private int m_gustBoil;
	private bool m_gustPlaced;
	private static Mesh? s_gustDisc;
	private static Mesh? s_gustSpike;
	private const float GustGoneTime = 0.9f;

	// A flat shape seen from both sides: a disc (radius 0.5) or a spike (base 1 wide, 1 tall).
	private static Mesh GustMesh(bool spike)
	{
		List<Vector3> v = new();
		List<int> tri = new();
		if (spike)
		{
			v.Add(new Vector3(-0.5f, 0f, 0f));
			v.Add(new Vector3(0f, 1f, 0f));
			v.Add(new Vector3(0.5f, 0f, 0f));
			tri.AddRange(new[] { 0, 1, 2 });
		}
		else
		{
			const int n = 22;
			v.Add(Vector3.zero);
			for (int i = 0; i < n; i++)
			{
				float a = i * Mathf.PI * 2f / n;
				v.Add(new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f));
			}
			for (int i = 0; i < n; i++)
			{
				tri.AddRange(new[] { 0, 1 + (i + 1) % n, 1 + i });
			}
		}
		int one = tri.Count;
		for (int i = 0; i < one; i += 3)
		{
			tri.AddRange(new[] { tri[i], tri[i + 2], tri[i + 1] });
		}
		Mesh mesh = new Mesh { name = spike ? "GustSpike" : "GustDisc" };
		mesh.SetVertices(v);
		mesh.SetTriangles(tri, 0);
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		return mesh;
	}

	private Material GustMaterial(Color c)
	{
		Shader? unlit = Shader.Find("Unlit/Color");
		if (unlit != null && unlit.isSupported)
		{
			return new Material(unlit) { color = c };
		}
		return SolidTint(c);
	}

	// One shape: the pale fill, and behind it the same shape a little bigger in ink.
	private Transform GustPart(Transform parent, bool spike, Material body, Material ink)
	{
		Transform root = new GameObject(spike ? "Spike" : "Puff").transform;
		root.gameObject.layer = m_layer;
		root.SetParent(parent, false);
		Mesh mesh = spike ? (s_gustSpike ??= GustMesh(true)) : (s_gustDisc ??= GustMesh(false));
		foreach ((Material mat, float z, Vector3 scale, Vector3 at) in new[]
		{
			(ink, 0.03f, spike ? new Vector3(1.45f, 1.14f, 1f) : new Vector3(1.16f, 1.16f, 1f), spike ? new Vector3(0f, -0.06f, 0f) : Vector3.zero),
			(body, 0f, Vector3.one, Vector3.zero),
		})
		{
			GameObject go = new GameObject("Shape");
			go.layer = m_layer;
			go.transform.SetParent(root, false);
			go.transform.localPosition = new Vector3(at.x, at.y, z);
			go.transform.localScale = scale;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer r = go.AddComponent<MeshRenderer>();
			r.sharedMaterial = mat;
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			r.receiveShadows = false;
		}
		return root;
	}

	private void BuildGust()
	{
		m_gust = new GameObject("SurfGust");
		m_gust.layer = m_layer;
		m_gust.transform.SetParent(transform, false);
		Material body = GustMaterial(new Color(0.94f, 0.92f, 0.86f));
		Material ink = GustMaterial(new Color(0.07f, 0.06f, 0.07f));
		// Drawn with +x the way to go; mirrored for a left turn.
		m_gustFlip = new GameObject("Flip").transform;
		m_gustFlip.SetParent(m_gust.transform, false);
		m_gustSoft = new GameObject("Cloud").transform;
		m_gustSoft.SetParent(m_gustFlip, false);
		m_gustSharp = new GameObject("Gusts").transform;
		m_gustSharp.SetParent(m_gustFlip, false);
		m_gustTrail = new GameObject("Curl").transform;
		m_gustTrail.SetParent(m_gustFlip, false);
		// The billowy cloud: a mound of rounds with a few loose dots.
		(Vector2 at, float r)[] blobs =
		{
			(new Vector2(0f, 0f), 1f), (new Vector2(0.45f, 0.35f), 0.75f), (new Vector2(-0.42f, 0.12f), 0.62f),
			(new Vector2(0.2f, 0.78f), 0.55f), (new Vector2(-0.15f, -0.32f), 0.72f), (new Vector2(0.58f, -0.26f), 0.6f),
			(new Vector2(0.98f, 0.95f), 0.2f), (new Vector2(1.2f, 0.62f), 0.13f), (new Vector2(-0.75f, -0.5f), 0.15f),
		};
		foreach ((Vector2 at, float r) in blobs)
		{
			m_gustBlobs.Add((GustPart(m_gustSoft, false, body, ink), at, r));
		}
		// The gusts: tall spikes leaning the way on.
		(Vector2 at, float h, float w)[] spikes =
		{
			(new Vector2(-0.55f, 0f), 0.85f, 0.36f), (new Vector2(-0.18f, 0f), 1.3f, 0.4f),
			(new Vector2(0.2f, 0f), 1.05f, 0.38f), (new Vector2(0.55f, 0f), 0.7f, 0.32f),
		};
		foreach ((Vector2 at, float h, float w) in spikes)
		{
			m_gustSpikes.Add((GustPart(m_gustSharp, true, body, ink), at, h, w));
		}
		for (int i = 0; i < 7; i++)
		{
			m_gustDots.Add(GustPart(m_gustTrail, false, body, ink));
		}
		m_gust.SetActive(false);
	}

	private static float GustHash(int a, int b) => Mathf.Repeat(Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.547f, 1f) - 0.5f;

	private void UpdateGust(CryptRun c)
	{
		int k = NextCorner();
		float dt = Time.deltaTime;
		bool armedNow = m_gustCorner >= 0 && m_gustCorner < c.Armed.Count && c.Armed[m_gustCorner];
		if (m_gustGone < 0f && armedNow && m_gust != null && m_gust.activeSelf)
		{
			m_gustGone = 0f;
		}
		if (m_gustGone >= 0f)
		{
			m_gustGone += dt;
			if (m_gustGone > GustGoneTime)
			{
				m_gustGone = -1f;
				m_gustCorner = -1;
			}
		}
		bool cue = m_gustGone < 0f && k >= 0 && !c.Armed[k] && m_z > c.Corners[k] - TurnWindow - 14f && m_cryptBonk < 0f;
		if (cue && m_gustCorner != k)
		{
			m_gustCorner = k;
			m_gustPlaced = false;
		}
		bool show = cue || m_gustGone >= 0f;
		if (!show)
		{
			if (m_gust != null && m_gust.activeSelf)
			{
				m_gust.SetActive(false);
			}
			return;
		}
		if (m_gust == null)
		{
			BuildGust();
		}
		if (!m_gust!.activeSelf)
		{
			m_gust.SetActive(true);
		}
		int ck = m_gustCorner;
		float zc = c.Corners[ck];
		int turn = c.Dirs[ck];
		float t = Time.time;
		// How near missing it you are (0 far, 1 at the corner).
		float u = Mathf.Clamp01(Mathf.InverseLerp(zc - TurnWindow - 14f, zc - TurnPoint, m_z));
		float gone = m_gustGone >= 0f ? Mathf.Clamp01(m_gustGone / GustGoneTime) : 0f;

		// Just ahead of you, in your hall, facing the camera (left behind once you've turned).
		if (m_gustGone < 0f)
		{
			Transform frame = CryptFrameAt(m_z);
			Vector3 want = frame.TransformPoint(new Vector3(LaneX(m_lane) * 0.5f, c.Floor + 1.6f, Mathf.Min(m_z + 4.5f, zc - 1.2f)));
			m_gust.transform.position = m_gustPlaced ? Vector3.Lerp(m_gust.transform.position, want, 1f - Mathf.Exp(-8f * dt)) : want;
			m_gustPlaced = true;
			// The way on, as it shows on the screen.
			Vector3 way = frame.TransformDirection(new Vector3(turn, 0f, 0f));
			float side = Vector3.Dot(way, m_camera.transform.right);
			m_gustFlip!.localScale = new Vector3(side >= 0f ? 1f : -1f, 1f, 1f);
		}
		m_gust.transform.rotation = m_camera.transform.rotation;
		m_gust.transform.localScale = Vector3.one * 0.85f;

		// The drawn lines boil: each shape shifts a touch, a dozen times a second.
		if (t >= m_gustBoilAt)
		{
			m_gustBoilAt = t + 0.11f;
			m_gustBoil++;
		}

		// The cloud: shrinks as you near the corner; squeezed flat and left behind after.
		float soft = Mathf.Lerp(1f, 0.45f, u) * (1f - gone);
		Vector3 squash = gone > 0f ? new Vector3(1f - 0.55f * gone, 1f + 0.35f * gone, 1f) : Vector3.one;
		m_gustSoft!.localPosition = new Vector3(-1.05f + Mathf.Sin(t * 1.6f) * 0.25f - 0.8f * gone, 0.05f, 0f);
		m_gustSoft.localScale = squash;
		for (int i = 0; i < m_gustBlobs.Count; i++)
		{
			(Transform tr, Vector2 home, float r) = m_gustBlobs[i];
			float size = r * soft * (1f + 0.1f * Mathf.Sin(t * 2.4f + i * 1.3f));
			tr.localPosition = new Vector3(home.x + GustHash(m_gustBoil, i) * 0.06f, home.y + GustHash(i, m_gustBoil) * 0.06f, 0f);
			tr.localScale = Vector3.one * Mathf.Max(0.001f, size);
		}

		// The gusts: grow as you near the corner, flickering and leaning the way on; after you
		// turn they whoosh off that way.
		float sharp = Mathf.Lerp(0.55f, 1.35f, u) * (1f - gone * 0.8f);
		m_gustSharp!.localPosition = new Vector3(0.85f + 3.5f * gone * gone, -0.15f, 0f);
		for (int i = 0; i < m_gustSpikes.Count; i++)
		{
			(Transform tr, Vector2 home, float h, float w) = m_gustSpikes[i];
			float height = h * sharp * (0.72f + 0.38f * Mathf.Abs(Mathf.Sin(t * 4.2f + i * 1.9f)));
			tr.localPosition = new Vector3(home.x + GustHash(m_gustBoil, i + 20) * 0.05f, home.y, 0f);
			tr.localRotation = Quaternion.Euler(0f, 0f, -16f - 12f * Mathf.Sin(t * 3.1f + i) - 20f * gone);
			tr.localScale = new Vector3(Mathf.Max(0.001f, w * Mathf.Lerp(0.8f, 1.1f, u)), Mathf.Max(0.001f, height), 1f);
		}

		// Puffs curling off toward the opening.
		float trail = Mathf.Lerp(0.6f, 1.2f, u) * (1f - gone);
		for (int i = 0; i < m_gustDots.Count; i++)
		{
			float p = Mathf.Repeat(t * (0.7f + gone * 2f) + i / (float)m_gustDots.Count, 1f);
			Vector2 a = new Vector2(-0.6f, 0.5f);
			Vector2 b = new Vector2(0.9f, 1.4f);
			Vector2 e = new Vector2(2.9f, 0.35f);
			Vector2 at = (1f - p) * (1f - p) * a + 2f * (1f - p) * p * b + p * p * e;
			float spin = p * 6f * Mathf.PI;
			at += new Vector2(Mathf.Cos(spin), Mathf.Sin(spin)) * (0.22f * p);
			m_gustDots[i].localPosition = new Vector3(at.x, at.y, 0f);
			m_gustDots[i].localScale = Vector3.one * Mathf.Max(0.001f, 0.2f * Mathf.Sin(p * Mathf.PI) * trail);
		}

		if (m_gustGone < 0f && m_z >= zc - TurnWindow && t >= c.NextWhoosh)
		{
			// Whoosh: calling you round.
			c.NextWhoosh = t + 1.5f;
			m_audio.Play(Sfx.Slide, 0.45f, Random.Range(0.55f, 0.65f));
		}
	}

	private void RemoveGust()
	{
		if (m_gust != null)
		{
			Destroy(m_gust);
		}
		m_gust = null;
		m_gustBlobs.Clear();
		m_gustSpikes.Clear();
		m_gustDots.Clear();
		m_gustCorner = -1;
		m_gustGone = -1f;
	}
}
