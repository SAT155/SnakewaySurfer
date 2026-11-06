using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.12 (gap doc, the Ashlands): lava in the open Ashlands. Now and then, in place of a row,
// a glowing channel of lava runs across the track (jump it), or a pool of lava lies along one or
// two lanes (keep out of it: jumping in only lands you in it). Glowing cracks run through the
// ash beside the track around them. The lava pulses, and lights the ground around it.
internal sealed partial class SurfGame
{
	private Material? m_lavaMaterial;
	private const float LavaRowChance = 0.22f;
	private static readonly Color LavaColor = new Color(1f, 0.38f, 0.06f);

	// Bright, glowing orange (its glow pulses, see UpdateLava).
	private Material LavaMaterial()
	{
		if (m_lavaMaterial == null)
		{
			Shader shader = FindPlainShader() ?? Shader.Find("Sprites/Default");
			m_lavaMaterial = new Material(shader) { name = "SurfLava", color = LavaColor };
			if (m_lavaMaterial.HasProperty("_EmissionColor"))
			{
				m_lavaMaterial.EnableKeyword("_EMISSION");
				m_lavaMaterial.SetColor("_EmissionColor", LavaColor * 2f);
			}
		}
		return m_lavaMaterial;
	}

	private void UpdateLava()
	{
		if (m_lavaMaterial != null && m_lavaMaterial.HasProperty("_EmissionColor"))
		{
			float pulse = 1.7f + 0.5f * Mathf.Sin(Time.time * 2.3f) + 0.2f * Mathf.Sin(Time.time * 5.1f);
			m_lavaMaterial.SetColor("_EmissionColor", LavaColor * pulse);
		}
	}

	// In the open Ashlands, some rows are lava instead.
	private bool MaybeLavaRow(float z, bool crypt)
	{
		if (crypt || !Plugin.Lava.Value || m_biomes.Count == 0 || m_biomes[BiomeAt(z)].Def.Name != "Ashlands"
			|| SegAt(z).Boss || ArenaWeight(z, out _) > 0.05f || Random.value > LavaRowChance)
		{
			return false;
		}
		if (Random.value < 0.45f)
		{
			LavaChannel(z);
		}
		else
		{
			LavaPool(z);
		}
		LavaCracks(z);
		return true;
	}

	// A strip of lava on the road, lying along its slope.
	private GameObject LavaStrip(float x, float z, float width, float length)
	{
		GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
		Destroy(strip.GetComponent<Collider>());
		strip.name = "Lava";
		strip.layer = m_layer;
		strip.transform.SetParent(m_stage, false);
		float slope = Mathf.Atan2(RoadY(z + 1f) - RoadY(z - 1f), 2f) * Mathf.Rad2Deg;
		strip.transform.localPosition = new Vector3(x, RoadY(z) + 0.03f, z);
		strip.transform.localRotation = Quaternion.Euler(-slope, 0f, 0f);
		strip.transform.localScale = new Vector3(width, 0.06f, length);
		Renderer r = strip.GetComponent<Renderer>();
		r.sharedMaterial = LavaMaterial();
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		r.receiveShadows = false;
		return strip;
	}

	private void LavaGlow(float x, float z, float range, float intensity)
	{
		Light glow = new GameObject("LavaGlow").AddComponent<Light>();
		glow.gameObject.layer = m_layer;
		glow.transform.SetParent(m_stage, false);
		glow.transform.localPosition = new Vector3(x, RoadY(z) + 0.8f, z);
		glow.type = LightType.Point;
		glow.range = range;
		glow.intensity = intensity;
		glow.color = new Color(1f, 0.45f, 0.12f);
		glow.shadows = LightShadows.None;
		glow.cullingMask = 1 << m_layer;
		m_things.Add(new Thing { Go = glow.gameObject, Kind = Kind.Scenery, Z = z, HalfLength = 1f });
	}

	// Across the whole track: jump it.
	private void LavaChannel(float z)
	{
		const float width = 1.5f;
		GameObject strip = LavaStrip(0f, z, TrackHalf * 2f + 0.8f, width);
		m_things.Add(new Thing { Go = strip, Kind = Kind.Scenery, Z = z, HalfLength = 1f });
		LavaGlow(0f, z, 8f, 1.6f);
		for (int lane = 0; lane < 3; lane++)
		{
			GameObject spot = new GameObject("LavaLane");
			spot.layer = m_layer;
			spot.transform.SetParent(m_stage, false);
			spot.transform.localPosition = new Vector3(LaneX(lane), RoadY(z), z);
			m_things.Add(new Thing
			{
				Go = spot,
				Kind = Kind.Low,
				Lane = lane,
				Z = z,
				HalfLength = width * 0.5f,
				CrashText = "You ran into the lava in the Ashlands! (Jump the glowing channels.)",
			});
		}
	}

	// Along one lane, or two side by side: keep out of it.
	private void LavaPool(float z)
	{
		float length = Random.Range(7f, 12f);
		int first = Random.Range(0, 3);
		int count = Random.value < 0.35f ? 2 : 1;
		if (count == 2 && first == 2)
		{
			first = 1;
		}
		for (int lane = first; lane < first + count; lane++)
		{
			// In pieces of about 2 m, so it lies on the road's rises and dips.
			int pieces = Mathf.CeilToInt(length / 2f);
			float piece = length / pieces;
			GameObject root = new GameObject("LavaPool");
			root.layer = m_layer;
			root.transform.SetParent(m_stage, false);
			root.transform.localPosition = new Vector3(LaneX(lane), RoadY(z), z);
			for (int k = 0; k < pieces; k++)
			{
				float pz = z - length * 0.5f + piece * (k + 0.5f);
				GameObject strip = LavaStrip(LaneX(lane), pz, LaneWidth * 0.9f, piece + 0.05f);
				strip.transform.SetParent(root.transform, true);
			}
			m_things.Add(new Thing
			{
				Go = root,
				Kind = Kind.Low,
				Lane = lane,
				Z = z,
				HalfLength = length * 0.5f,
				CrashText = "You ran into the lava in the Ashlands! (Keep out of the glowing pools.)",
			});
		}
		LavaGlow(LaneX(first) + (count - 1) * LaneWidth * 0.5f, z, 9f, 1.8f);
	}

	// Glowing cracks through the ash beside the track.
	private void LavaCracks(float z)
	{
		int n = Random.Range(2, 5);
		for (int i = 0; i < n; i++)
		{
			int side = Random.value < 0.5f ? -1 : 1;
			float x = side * (TrackHalf + ExtraFlat(z) + Random.Range(1f, 9f));
			float cz = z + Random.Range(-8f, 8f);
			if (UnderSea(x, cz))
			{
				continue;
			}
			GameObject crack = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Destroy(crack.GetComponent<Collider>());
			crack.name = "LavaCrack";
			crack.layer = m_layer;
			crack.transform.SetParent(m_stage, false);
			crack.transform.localPosition = new Vector3(x, HillHeight(x, cz) + 0.02f, cz);
			crack.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 180f), 0f);
			crack.transform.localScale = new Vector3(Random.Range(0.15f, 0.35f), 0.05f, Random.Range(2f, 5f));
			Renderer r = crack.GetComponent<Renderer>();
			r.sharedMaterial = LavaMaterial();
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			m_things.Add(new Thing { Go = crack, Kind = Kind.Scenery, Z = cz, HalfLength = 2.5f });
		}
	}
}
