using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.3 (Sam: "There isn't much you can do when an attack is inbound. Maybe the ship should have
// a separate health from the lives of the player"; "there could be little islands with a
// workbench on them you can stop at to repair your boat"). At sea a blow cracks the hull, not
// you: the planks by the wind rose show what's left, the ship sits lower and lists as it fails,
// and only when the hull gives way does it cost a life (the crew patches her up to half). Driftwood
// mends a little. Small islands lie at the channel's edge, one before the storm and one after
// the serpent of the deep, each with a workbench on the shore: steer into the golden ring on the
// water and take the sail in (oars), and the ship moors while the hull is mended plank by plank.
// Raise the sail to go on. Their sand and grass are part of the land, so the waves wash their
// beaches and the game's terrain shader wets the waterline.
internal sealed partial class SurfGame
{
	private const int HullMax = 6;
	private float m_hull = HullMax;
	private float m_hullGrace;
	private float m_hullFlash;
	private float m_repairPulse;

	private sealed class Island
	{
		public Vector2 C;
		public float R;
		public float Top;
		public float Side;
		public bool Built;
		public GameObject? Bench;
		public GameObject? Ring;
	}

	private readonly List<Island> m_islands = new();
	private Island? m_moored;
	private float m_repairTick;
	private Material? m_harbourMaterial;

	// The waterline's distance from an island's centre, as a share of its radius.
	private const float IslandShore = 0.8f;
	// How close to the shore the ship moors (metres beyond the waterline).
	private const float MooringReach = 10f;

	// ---------------------------------------------------------------- the hull

	// Called from Hit: at sea the ship takes the blow. True when it's dealt with.
	private bool HullTakes(Thing t, string why)
	{
		if (!m_atSea || m_seaPhase != 2)
		{
			return false;
		}
		if (m_hullGrace > 0f)
		{
			return true;
		}
		t.Collidable = false;
		m_hullGrace = 1.1f;
		m_hullFlash = 1f;
		m_hull = Mathf.Max(0f, m_hull - (t.Heavy ? 2f : 1f));
		m_shake = Mathf.Max(m_shake, 0.35f);
		m_audio.Play("sfx_ship_damaged|sfx_wood_break|sfx_ship_impact", 1f, Random.Range(0.9f, 1.05f));
		Vector2 c = ShipCenter();
		Vector3 at = new Vector3(c.x, SeaY + 1.2f, c.y);
		if (FindFx("vfx_ship_damaged|vfx_SawDust|vfx_wood_break|vfx_destroyed") is GameObject chips)
		{
			PlayFx(chips, at, Quaternion.identity, 2.5f);
		}
		SpawnSplash(c + Random.insideUnitCircle * m_shipBeam, 1.1f);
		// Splintering wood, not your blood.
		Flash(new Color(0.55f, 0.32f, 0.12f), 0.45f);
		if (m_hull > 0f)
		{
			return true;
		}
		// The hull gives way: a life, while the crew bails and patches her to half.
		Flash(new Color(0.85f, 0.05f, 0.02f), 0.9f);
		PlayHurt();
		if (m_lives > 1)
		{
			m_lives--;
			m_hull = HullMax / 2;
			m_hurtTime = 1.6f;
			SetTrigger("stagger");
			m_audio.Play("sfx_ship_destroyed|sfx_wood_break", 0.9f, 0.8f);
			return true;
		}
		Crash(why);
		return true;
	}

	// How much lower she sits, and her list, as the hull fails.
	private float HullSink => m_atSea ? (1f - m_hull / HullMax) * 0.45f : 0f;

	private float HullList => m_atSea ? (1f - m_hull / HullMax) * 5f : 0f;

	private void ResetHull()
	{
		m_hull = HullMax;
		m_hullGrace = 0f;
		m_hullFlash = 0f;
		m_moored = null;
		m_hammerOut = false;
	}

	// Driftwood: two planks for the hull, else a life, else coins.
	private bool MendWithDriftwood()
	{
		if (m_hull < HullMax)
		{
			m_hull = Mathf.Min(HullMax, m_hull + 2f);
			m_hullFlash = 0.6f;
			Flash(new Color(0.4f, 1f, 0.5f), 0.4f);
			m_audio.Play("sfx_build_hammer_wood|sfx_wood_hit", 0.9f, 1.1f);
			return true;
		}
		if (m_lives < Plugin.Lives.Value)
		{
			m_lives++;
			Flash(new Color(0.4f, 1f, 0.5f), 0.45f);
			m_audio.Play("sfx_build_hammer_wood|sfx_wood_hit", 0.9f, 1.1f);
			return true;
		}
		return false;
	}

	// ---------------------------------------------------------------- islands

	private void MakeIslands(Segment sea)
	{
		ClearIslands();
		float len = Mathf.Max(200f, Plugin.OceanLength.Value);
		// Before the serpents, before the storm, and after the serpent of the deep.
		float[] along = { Mathf.Max(200f, len * 0.3f), len * 0.6f, len + Mathf.Max(90f, len * 0.12f) };
		float side = Random.value < 0.5f ? -1f : 1f;
		foreach (float a in along)
		{
			float r = Random.Range(11f, 14f);
			m_islands.Add(new Island
			{
				C = new Vector2(side * (SeaHalfWidth + r * 0.35f), sea.Start + a),
				R = r,
				Side = side,
				Top = SeaY + Random.Range(1.3f, 1.9f),
			});
			side = -side;
		}
	}

	private void ClearIslands()
	{
		foreach (Island i in m_islands)
		{
			if (i.Bench != null)
			{
				Destroy(i.Bench);
			}
			if (i.Ring != null)
			{
				Destroy(i.Ring);
			}
		}
		m_islands.Clear();
		m_moored = null;
	}

	// The land of the islands, over the sea floor (from ShoreLand, at sea only).
	private float IslandLand(float x, float z, float y, ref float veg)
	{
		foreach (Island i in m_islands)
		{
			float dx = x - i.C.x;
			float dz = z - i.C.y;
			float reach = i.R * 1.35f;
			if (Mathf.Abs(dx) > reach || Mathf.Abs(dz) > reach)
			{
				continue;
			}
			float d = Mathf.Sqrt(dx * dx + dz * dz) / i.R;
			if (d >= 1.35f)
			{
				continue;
			}
			// A low hump: a sandy shelf into the water, a grassy top with a few bumps.
			float t = Mathf.SmoothStep(0f, 1f, (1.35f - d) / 0.85f);
			float bumps = 0.5f * (Mathf.PerlinNoise(x * 0.17f + 11f, z * 0.17f + 5f) - 0.5f);
			float h = Mathf.Lerp(SeaY - 3.2f, i.Top + bumps * t, t);
			if (h > y)
			{
				y = h;
				veg = Mathf.Max(veg, Mathf.Clamp01((h - SeaY - 0.7f) * 1.4f));
			}
		}
		return y;
	}

	// 0..1: how much of the islands' own (Meadows) ground shows here.
	private float IslandWeight(float x, float z)
	{
		float w = 0f;
		foreach (Island i in m_islands)
		{
			float d = Vector2.Distance(new Vector2(x, z), i.C) / i.R;
			w = Mathf.Max(w, Mathf.Clamp01((1.25f - d) / 0.5f));
		}
		return w;
	}

	private bool OnIsland(Vector2 p)
	{
		foreach (Island i in m_islands)
		{
			if (Vector2.Distance(p, i.C) < i.R * IslandShore)
			{
				return true;
			}
		}
		return false;
	}

	// Each frame while sailing: the islands' benches come into view, the ship can't sail
	// through them, and she moors and is mended in the ring.
	private void UpdateHarbours(float dt)
	{
		m_hullGrace = Mathf.Max(0f, m_hullGrace - dt);
		m_hullFlash = Mathf.MoveTowards(m_hullFlash, 0f, dt * 1.5f);
		m_repairPulse += dt;
		Vector2 c = ShipCenter();
		Island? near = null;
		foreach (Island i in m_islands)
		{
			if (!i.Built && i.C.y - m_z < AheadDistance + 20f && i.C.y > m_z - 40f)
			{
				BuildIsland(i);
			}
			if (i.Ring != null)
			{
				float ring = (i.R * IslandShore + MooringReach) * 2f;
				PlaceWaterMarker(i.Ring, i.C, ring, ring, 0f);
				bool inside = Vector2.Distance(c, i.C) < i.R * IslandShore + MooringReach;
				// Golden and steady while you're moored; it pulses to call you in when the hull
				// needs mending, and dims once it's whole.
				float a = m_moored == i ? 0.85f : m_hull < HullMax ? 0.45f + 0.3f * Mathf.Abs(Mathf.Sin(m_repairPulse * (inside ? 6f : 2.5f))) : 0.18f;
				i.Ring.GetComponent<Renderer>().material.color = new Color(1f, 0.82f, 0.35f, a);
			}
			float d = Vector2.Distance(c, i.C);
			float shore = i.R * IslandShore + m_shipBeam;
			if (d < shore && d > 0.01f)
			{
				// Aground: she's pushed back off the sand.
				// 1.16.74 (Sam: "If you get too close to the island you get stuck"): she slides off
				// sideways round the island and her bow swings clear, instead of being held
				// against it (she can't be pushed back down the course).
				Vector2 away = (c - i.C) / d;
				float over = shore - d;
				Vector2 push = away * over;
				float aside = Mathf.Sign(c.x - i.C.x + 0.01f);
				m_x += away.x * over + (away.y < 0f ? aside * -away.y * over : 0f);
				m_z += Mathf.Max(0f, away.y * over);
				m_heading = Mathf.MoveTowards(m_heading, aside * MaxHeading * 0.7f, 140f * dt);
				if (m_scrape <= 0f)
				{
					m_audio.Play("sfx_ship_impact|sfx_wood_hit", 0.6f, 0.75f);
					SpawnSplash(c - push.normalized * m_shipBeam, 0.9f);
					m_lurchV += Mathf.Sign(push.x + 0.01f) * 30f;
					m_boatSpeed *= 0.5f;
				}
				m_scrape = 0.6f;
			}
			if (d < i.R * IslandShore + MooringReach)
			{
				near = i;
			}
		}
		if (near == null || m_sail != Sail.Oars)
		{
			m_moored = null;
			RepairHammer(false);
			return;
		}
		if (m_moored != near)
		{
			m_moored = near;
			m_repairTick = 0.6f;
			m_audio.Play("sfx_ship_impact|sfx_wood_hit", 0.4f, 1.2f);
		}
		if (m_hull >= HullMax || m_boatSpeed > 1.5f)
		{
			RepairHammer(false);
			return;
		}
		// 1.16.75 (Sam: the sounds, animations and effects of a repair): the hammer comes out
		// and you mend her the game's way, a swing for each plank, with the hammer's knock and
		// the sawdust of a placed piece where it lands.
		RepairHammer(true);
		float before = m_repairTick;
		m_repairTick -= dt;
		if (before > 0.3f && m_repairTick <= 0.3f)
		{
			AvatarSwing(m_wieldShared, 0);
		}
		if (m_repairTick > 0f)
		{
			return;
		}
		m_repairTick = 0.75f;
		m_hull = Mathf.Min(HullMax, m_hull + 1f);
		m_hullFlash = 0.5f;
		m_audio.Play("sfx_build_hammer_wood|sfx_wood_hit", 0.9f, Random.Range(0.95f, 1.1f));
		Vector2 fwd = HeadingVector();
		Vector2 side = new Vector2(fwd.y, -fwd.x) * (Random.value < 0.5f ? -1f : 1f);
		Vector2 spotOnHull = c + fwd * (Random.Range(-0.35f, 0.35f) * m_shipLength) + side * (m_shipBeam * 0.5f);
		Vector3 at = new Vector3(spotOnHull.x, SeaY + 1.2f, spotOnHull.y);
		if (FindFx("vfx_Place_wood_wall|vfx_Place_workbench|vfx_SawDust") is GameObject sparkle)
		{
			PlayFx(sparkle, at, Quaternion.identity, 2f);
		}
		if (FindFx("vfx_SawDust|vfx_Place_wood_beam") is GameObject dust)
		{
			PlayFx(dust, at + Vector3.up * 0.3f, Quaternion.identity, 2f);
		}
		m_lurchV += Random.Range(-6f, 6f);
		if (m_hull >= HullMax)
		{
			// Whole again: the game's own "repaired" chime.
			Flash(new Color(0.4f, 1f, 0.5f), 0.4f);
			m_audio.Play("sfx_gui_repairitem_workbench|sfx_build_hammer_metal", 0.8f, 1f);
		}
	}

	private bool m_hammerOut;

	// The hammer in hand while mending, put away after.
	private void RepairHammer(bool on)
	{
		if (on == m_hammerOut)
		{
			return;
		}
		if (on)
		{
			Wield("Hammer");
			m_hammerOut = true;
		}
		else
		{
			m_hammerOut = false;
			if (m_wieldName == "Hammer")
			{
				Unwield();
			}
		}
	}

	// The workbench (and a tree) on the island's shore by the channel, and its ring.
	private void BuildIsland(Island i)
	{
		i.Built = true;
		Vector2 spot = i.C - new Vector2(i.Side * i.R * 0.38f, 0f);
		GameObject? bench = Pictures.Prefab("piece_workbench");
		if (bench != null)
		{
			GameObject go = SeaPicture(bench, 2.4f, out _);
			go.transform.localPosition = new Vector3(spot.x, IslandTop(spot), spot.y);
			go.transform.localRotation = Quaternion.Euler(0f, i.Side > 0f ? -90f : 90f, 0f);
			i.Bench = go;
			GameObject? block = Pictures.Prefab("piece_workbench_ext1");
			if (block != null)
			{
				Vector2 by = spot + new Vector2(0f, 2.6f);
				GameObject b = SeaPicture(block, 1.4f, out _);
				b.transform.SetParent(go.transform, true);
				b.transform.localPosition = go.transform.InverseTransformPoint(m_stage.TransformPoint(new Vector3(by.x, IslandTop(by), by.y)));
			}
			// The bench's warm light, as a lit crafting station.
			Light glow = new GameObject("Glow").AddComponent<Light>();
			glow.gameObject.layer = m_layer;
			glow.transform.SetParent(go.transform, false);
			glow.transform.localPosition = Vector3.up * 1.5f;
			glow.type = LightType.Point;
			glow.range = 9f;
			glow.intensity = 1.2f;
			glow.color = new Color(1f, 0.8f, 0.5f);
			glow.cullingMask = 1 << m_layer;
		}
		foreach (string name in new[] { "Beech1", "Pinetree_01", "Birch1", "FirTree" })
		{
			GameObject? tree = Pictures.Prefab(name);
			if (tree == null)
			{
				continue;
			}
			// 1.16.72 (Sam: the island was hard to see, the trees were so big): a small tree on
			// the far side of the island, away from the bench, so it doesn't hide it from above.
			Vector2 at = i.C + new Vector2(i.Side * i.R * 0.45f, Random.Range(-2f, 2f));
			GameObject t = Instantiate(Picture(tree), m_stage, false);
			t.SetActive(true);
			t.transform.localScale = Vector3.one * 0.4f;
			t.transform.localPosition = new Vector3(at.x, IslandTop(at) - 0.2f, at.y);
			t.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
			if (i.Bench != null)
			{
				// Goes with the bench when the islands are cleared.
				t.transform.SetParent(i.Bench.transform, true);
			}
			else
			{
				i.Bench = t;
			}
			break;
		}
		m_harbourMaterial ??= new Material(Shader.Find("Sprites/Default")) { name = "SurfHarbour", mainTexture = Ring(), color = new Color(1f, 0.82f, 0.35f, 0.5f), renderQueue = 3105 };
		GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
		Destroy(ring.GetComponent<Collider>());
		ring.name = "HarbourRing";
		ring.layer = m_layer;
		ring.transform.SetParent(m_stage, false);
		Renderer r = ring.GetComponent<Renderer>();
		r.material = m_harbourMaterial;
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		r.receiveShadows = false;
		i.Ring = ring;
	}

	private float IslandTop(Vector2 p)
	{
		float veg = 0f;
		return IslandLand(p.x, p.y, SeaY - 4f, ref veg);
	}

	// ---------------------------------------------------------------- HUD

	// The hull's planks in a row above the wind rose: lost ones dark, the next one mended
	// glowing while you're moored.
	private void HullHud(float k, Rect rose)
	{
		if (ItemIcon("Wood") is not Sprite plank)
		{
			return;
		}
		// 1.16.76 (Sam): bigger planks.
		float s = 44f * k * (1f + 0.25f * m_hullFlash);
		float gap = 5f * k;
		float total = HullMax * s + (HullMax - 1) * gap;
		float x0 = rose.center.x - total * 0.5f;
		float y = rose.y - s - 10f * k;
		int whole = Mathf.FloorToInt(m_hull + 0.01f);
		for (int i = 0; i < HullMax; i++)
		{
			Color tint = i < whole ? Color.white : new Color(0.2f, 0.2f, 0.2f, 0.6f);
			if (i == whole && m_moored != null && m_hull < HullMax)
			{
				tint = Color.Lerp(new Color(0.2f, 0.2f, 0.2f, 0.6f), new Color(0.6f, 1f, 0.6f), 0.5f + 0.5f * Mathf.Sin(m_repairPulse * 8f));
			}
			else if (i < whole && m_hull <= 2f)
			{
				// Nearly gone: the last planks flash red.
				tint = Color.Lerp(Color.white, new Color(1f, 0.3f, 0.2f), 0.5f + 0.5f * Mathf.Sin(m_repairPulse * 7f));
			}
			DrawSprite(new Rect(x0 + i * (s + gap), y, s, s), plank, tint);
		}
	}
}
