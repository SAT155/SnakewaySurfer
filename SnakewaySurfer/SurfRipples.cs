using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// 1.14 (Sam): running through the swamp's water leaves ripples, and your steps splash.
internal sealed partial class SurfGame
{
	private sealed class Ripple
	{
		public GameObject Go = null!;
		public float Age;
		public float Life;
		public float Size;
	}

	private readonly List<Ripple> m_ripples = new();
	private Material? m_rippleMaterial;
	private static Texture2D? s_ring;

	private bool InWater => m_biomes.Count > 0 && m_y < 0.2f && (WaterWeight(m_z) > 0.5f || InShallows);

	// 1.16: wading through the shallows of a crossing's beach.
	private bool InShallows => !m_atSea && ShoreWeight(m_z) > 0f && RoadY(m_z) < SeaHeight(m_x, m_z) - 0.05f;

	private static Texture2D Ring()
	{
		if (s_ring != null)
		{
			return s_ring;
		}
		const int n = 64;
		Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
		Color[] px = new Color[n * n];
		for (int y = 0; y < n; y++)
		{
			for (int x = 0; x < n; x++)
			{
				float dx = (x + 0.5f) / n * 2f - 1f;
				float dy = (y + 0.5f) / n * 2f - 1f;
				float r = Mathf.Sqrt(dx * dx + dy * dy);
				// A soft bright ring near the edge.
				float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.12f);
				px[y * n + x] = new Color(1f, 1f, 1f, a * a);
			}
		}
		tex.SetPixels(px);
		tex.Apply();
		s_ring = tex;
		return tex;
	}

	// Called on each footstep (and on landing).
	private void Splash(float strength)
	{
		if (!InWater)
		{
			return;
		}
		m_audio.Play("sfx_footstep_water|sfx_land_water", 0.4f * strength + 0.2f, Random.Range(0.9f, 1.1f));
		float surface = InShallows ? SeaHeight(m_x, m_z) : RoadY(m_z) + WaterDepth;
		AddRipple(m_x + Random.Range(-0.2f, 0.2f), surface, m_z, strength);
	}

	// 1.16.1 (Sam): creatures in the swamp's water ripple it too (leeches swimming, draugr wading).
	private void CreatureRipples(Thing t, float dt)
	{
		if (t.Go == null || !t.Go.activeInHierarchy || m_biomes.Count == 0 || WaterWeight(t.Z) < 0.5f || Mathf.Abs(t.Z - m_z) > 45f)
		{
			return;
		}
		float above = t.Go.transform.localPosition.y - (RoadY(t.Z) + WaterDepth);
		if (above > 0.6f)
		{
			return;
		}
		t.RippleTimer -= dt;
		if (t.RippleTimer > 0f)
		{
			return;
		}
		bool moving = t.Speed != 0f || t.Chaser || Mathf.Abs(t.X - t.TargetX) > 0.05f;
		t.RippleTimer = moving ? Random.Range(0.25f, 0.4f) : Random.Range(0.9f, 1.5f);
		Vector3 p = t.Go.transform.localPosition;
		AddRipple(p.x + Random.Range(-0.3f, 0.3f), RoadY(t.Z) + WaterDepth, p.z + Random.Range(-0.3f, 0.3f), moving ? 0.6f : 0.35f);
	}

	private void AddRipple(float x, float surface, float z, float strength)
	{
		if (m_rippleMaterial == null)
		{
			Shader shader = Shader.Find("Sprites/Default");
			if (shader == null)
			{
				return;
			}
			m_rippleMaterial = new Material(shader) { name = "SurfRipple", mainTexture = Ring(), color = new Color(0.85f, 0.9f, 0.85f, 0.55f), renderQueue = 3100 };
		}
		if (m_ripples.Count > 60)
		{
			return;
		}
		GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
		Destroy(quad.GetComponent<Collider>());
		quad.name = "Ripple";
		quad.layer = m_layer;
		quad.transform.SetParent(m_stage, false);
		quad.transform.localPosition = new Vector3(x, surface + 0.02f, z);
		quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
		Renderer r = quad.GetComponent<Renderer>();
		r.sharedMaterial = new Material(m_rippleMaterial);
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		r.receiveShadows = false;
		m_ripples.Add(new Ripple { Go = quad, Life = 0.9f + 0.4f * strength, Size = 1.2f + 1.6f * strength });
	}

	private void UpdateRipples(float dt)
	{
		for (int i = m_ripples.Count - 1; i >= 0; i--)
		{
			Ripple r = m_ripples[i];
			r.Age += dt;
			if (r.Go == null || r.Age >= r.Life)
			{
				if (r.Go != null)
				{
					Destroy(r.Go.GetComponent<Renderer>().sharedMaterial);
					Destroy(r.Go);
				}
				m_ripples.RemoveAt(i);
				continue;
			}
			float p = r.Age / r.Life;
			float s = Mathf.Lerp(0.3f, r.Size, Mathf.Sqrt(p));
			r.Go.transform.localScale = new Vector3(s, s, 1f);
			Renderer rend = r.Go.GetComponent<Renderer>();
			Color c = rend.sharedMaterial.color;
			c.a = 0.55f * (1f - p);
			rend.sharedMaterial.color = c;
		}
	}
}
