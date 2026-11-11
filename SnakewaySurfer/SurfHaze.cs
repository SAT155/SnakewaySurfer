using UnityEngine;

namespace SnakewaySurfer;

// 1.16.69 (SAT: "The fog doesn't work"): through the game's camera its own fog doesn't always
// reach our track (the game's shaders and post effects keep their own idea of it), so the far
// end of the track stopped crisply against the sky. Layers of thin haze now stand across the
// view towards the end of the track, each a sheet of fog colour that fades out upwards, so
// whatever is behind them (the end of the track, far trees, the bottom of a boss's place)
// sinks into the mist whatever the shaders do. Things nearer than a sheet are drawn in front of
// it, so only the distance is hazed.
internal sealed partial class SurfGame
{
	private Transform[]? m_haze;
	private Material[]? m_hazeMats;
	private static Mesh? s_hazeMesh;
	private static Texture2D? s_hazeTex;

	// How far along the view each sheet stands, and how thick it is.
	private static readonly float[] s_hazeAt = { 0.55f, 0.72f, 0.86f, 0.97f };
	private static readonly float[] s_hazeAlpha = { 0.22f, 0.3f, 0.45f, 0.9f };

	private static Mesh HazeMesh()
	{
		Mesh mesh = new Mesh { name = "SurfHaze" };
		mesh.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f) };
		mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
		mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
		mesh.RecalculateBounds();
		mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 2f);
		return mesh;
	}

	// Solid up to a little above the ground, then thinning away to nothing.
	private static Texture2D HazeTexture()
	{
		Texture2D tex = new Texture2D(4, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SurfHaze" };
		Color[] px = new Color[4 * 64];
		for (int y = 0; y < 64; y++)
		{
			float v = y / 63f;
			float a = v < 0.45f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (v - 0.45f) / 0.55f);
			for (int x = 0; x < 4; x++)
			{
				px[y * 4 + x] = new Color(1f, 1f, 1f, a);
			}
		}
		tex.SetPixels(px);
		tex.Apply(false);
		return tex;
	}

	private void BuildHaze()
	{
		Shader? shader = Shader.Find("Sprites/Default");
		if (shader == null)
		{
			return;
		}
		s_hazeMesh ??= HazeMesh();
		s_hazeTex ??= HazeTexture();
		m_haze = new Transform[s_hazeAt.Length];
		m_hazeMats = new Material[s_hazeAt.Length];
		for (int i = 0; i < s_hazeAt.Length; i++)
		{
			GameObject go = new GameObject("SurfHaze" + i);
			go.layer = m_layer;
			go.transform.SetParent(transform, false);
			go.AddComponent<MeshFilter>().sharedMesh = s_hazeMesh;
			MeshRenderer r = go.AddComponent<MeshRenderer>();
			Material mat = new Material(shader) { mainTexture = s_hazeTex };
			// Far sheets first, so the nearer ones lay over them.
			mat.renderQueue = 3010 - i;
			r.sharedMaterial = mat;
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			r.receiveShadows = false;
			m_haze[i] = go.transform;
			m_hazeMats[i] = mat;
		}
	}

	// After the camera has moved (LateUpdate).
	private void UpdateHaze()
	{
		bool want = Plugin.DistanceHaze.Value && m_camera != null && !CryptCovers(m_z) && Plugin.FogStrength.Value > 0f && m_seaBlend < 0.99f;
		if (!want)
		{
			if (m_haze != null)
			{
				foreach (Transform t in m_haze)
				{
					t.gameObject.SetActive(false);
				}
			}
			return;
		}
		if (m_haze == null)
		{
			BuildHaze();
			if (m_haze == null)
			{
				return;
			}
		}
		Vector3 fwd = m_camFinalRot * Vector3.forward;
		fwd.y = 0f;
		if (fwd.sqrMagnitude < 0.001f)
		{
			fwd = Vector3.forward;
		}
		fwd.Normalize();
		float aspect = m_camera!.aspect > 0.1f ? m_camera.aspect : 16f / 9f;
		float tanH = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) * aspect;
		// Thicker in thicker fog (each biome's, a storm's, a boss's weather), as the fog would be.
		float thick = Mathf.Clamp(FogDensity / Mathf.Max(0.001f, 0.011f * ViewFog), 0.5f, 1.6f) * Mathf.Clamp(Plugin.FogStrength.Value, 0f, 2f);
		Color c = FogColor;
		for (int i = 0; i < m_haze.Length; i++)
		{
			float d = AheadDistance * s_hazeAt[i];
			Vector3 at = m_camFinal + fwd * d;
			float ground = StagePoint(new Vector3(0f, RoadY(m_z + d), m_z + d)).y;
			float height = d * 0.35f;
			Transform t = m_haze[i];
			t.gameObject.SetActive(true);
			t.position = new Vector3(at.x, ground + d * 0.06f - height * 0.45f, at.z);
			t.rotation = Quaternion.LookRotation(fwd);
			t.localScale = new Vector3(2f * d * tanH * 1.5f + 40f, height, 1f);
			m_hazeMats![i].color = new Color(c.r, c.g, c.b, Mathf.Clamp01(s_hazeAlpha[i] * thick) * (1f - m_seaBlend));
		}
	}
}
