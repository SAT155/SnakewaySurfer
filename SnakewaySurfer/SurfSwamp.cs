using UnityEngine;

namespace SnakewaySurfer;

// 1.16.1 (Sam's test): the swamp's own sights along the track. Now and then a sunken crypt's
// entrance stands beside it, and a surtling fire spout burns there with a flock of surtlings
// above it, loosing slow fireballs at your lane as you come (roll under them, or change lanes).
internal sealed partial class SurfGame
{
	private float m_nextSightZ;
	private bool m_lastSightCrypt;

	private void ResetSwampSights()
	{
		m_nextSightZ = 0f;
		m_lastSightCrypt = false;
	}

	private void SpawnSwampSights(float horizon)
	{
		if (m_nextSightZ <= 0f)
		{
			m_nextSightZ = m_z + Random.Range(120f, 200f);
		}
		while (m_nextSightZ < horizon)
		{
			float z = m_nextSightZ;
			m_nextSightZ += Random.Range(180f, 300f);
			Segment s = SegAt(z);
			if (m_biomes.Count == 0 || m_biomes[s.Biome].Def.Name != "Swamp" || s.Boss || s.Sea || ArenaWeight(z, out _) > 0.05f
				|| z < s.Start + 50f || (s.End != float.MaxValue && z > s.End - 40f))
			{
				continue;
			}
			int side = Random.value < 0.5f ? -1 : 1;
			// Mostly taking turns, so a stretch has a bit of both.
			bool crypt = Random.value < (m_lastSightCrypt ? 0.3f : 0.7f);
			bool done = crypt ? SpawnCrypt(z, side) : SpawnFireSpout(z, side, s.Biome);
			if (!done)
			{
				done = crypt ? SpawnFireSpout(z, side, s.Biome) : SpawnCrypt(z, side);
				crypt = !crypt;
			}
			if (done)
			{
				m_lastSightCrypt = crypt;
			}
		}
	}

	// A sunken crypt's entrance (the game's own location), well back from the track's edge.
	private bool SpawnCrypt(float z, int side)
	{
		GameObject? template = null;
		foreach (string name in new[] { "SunkenCrypt4", "SunkenCrypt1", "SunkenCrypt2", "SunkenCrypt3" })
		{
			template = LocationPicture(name);
			if (template != null)
			{
				break;
			}
		}
		if (template == null)
		{
			return false;
		}
		GameObject go = Instantiate(template, m_stage, false);
		go.SetActive(true);
		go.transform.localRotation = Quaternion.Euler(0f, side < 0 ? 90f : -90f, 0f);
		go.transform.localScale = Vector3.one;
		float x = side * (TrackHalf + ExtraFlat(z) + 14f);
		go.transform.localPosition = new Vector3(x, HillHeight(x, z) - 0.6f, z);
		float half = 8f;
		if (Pictures.MeshBounds(go, out Bounds bounds))
		{
			// Its near side well clear of the track (it's big), its feet in the mud.
			float inner = (side < 0 ? bounds.max.x : bounds.min.x) - m_stage.position.x;
			float limit = side * (TrackHalf + ExtraFlat(z) + 4f);
			float push = side < 0 ? Mathf.Min(0f, limit - inner) : Mathf.Max(0f, limit - inner);
			Vector3 p = go.transform.localPosition;
			p.x += push;
			p.y = HillHeight(p.x, z) - 0.6f;
			go.transform.localPosition = p;
			half = Mathf.Max(4f, bounds.size.z * 0.5f);
		}
		foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
		{
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		}
		foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
		{
			ps.Play(true);
		}
		m_things.Add(new Thing { Go = go, Kind = Kind.Scenery, Z = z, HalfLength = half });
		return true;
	}

	// A fire spout beside the track with surtlings hovering over it.
	private bool SpawnFireSpout(float z, int side, int biomeIndex)
	{
		GameObject? surtling = Pictures.Monster("Surtling");
		if (surtling == null || !Plugin.RangedAttacks.Value || CreatureInfo.Of(surtling).Ranged.Count == 0)
		{
			return false;
		}
		float x = side * (TrackHalf + ExtraFlat(z) + 4.5f);
		GameObject? hole = LocationPicture("FireHole");
		if (hole != null)
		{
			GameObject go = Instantiate(hole, m_stage, false);
			go.SetActive(true);
			go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
			go.transform.localPosition = new Vector3(x, HillHeight(x, z) - 0.2f, z);
			foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
			{
				ps.Play(true);
			}
			LightUp(go);
			m_things.Add(new Thing { Go = go, Kind = Kind.Scenery, Z = z, HalfLength = 4f });
		}
		Light glow = new GameObject("SpoutGlow").AddComponent<Light>();
		glow.gameObject.layer = m_layer;
		glow.transform.SetParent(m_stage, false);
		glow.transform.localPosition = new Vector3(x, HillHeight(x, z) + 1.2f, z);
		glow.type = LightType.Point;
		glow.range = 10f;
		glow.intensity = 1.8f;
		glow.color = new Color(1f, 0.55f, 0.2f);
		glow.cullingMask = 1 << m_layer;
		glow.shadows = LightShadows.None;
		m_things.Add(new Thing { Go = glow.gameObject, Kind = Kind.Scenery, Z = z, HalfLength = 1f });

		SpawnEntry e = new SpawnEntry { Prefab = surtling, Label = SpawnTables.Label(surtling), Flying = true };
		int count = Random.Range(4, 7);
		for (int k = 0; k < count; k++)
		{
			float az = z + Random.Range(-5f, 7f);
			Thing a = SpawnCreature(e, side < 0 ? 0 : 2, az, Kind.Block, 0f, biomeIndex, "sideline");
			a.X = x + Random.Range(-2.2f, 2.2f);
			a.TargetX = a.X;
			a.FreeX = true;
			a.Collidable = false;
			a.Sideline = true;
			a.Attacked = true;
			a.Side = side;
			a.BaseY = Random.Range(1.4f, 3.2f);
			a.Seed = Random.value * 20f;
			// Staggered, so the fireballs come one after another, not all at once.
			a.Cooldown = k * Random.Range(0.35f, 0.6f);
			// Slow fireballs, easy to read (and roll under).
			a.ShotSpeed = Random.Range(4.5f, 6f);
			a.Go.transform.localPosition = new Vector3(a.X, HillHeight(a.X, a.Z) + a.BaseY, a.Z);
		}
		return true;
	}
}
