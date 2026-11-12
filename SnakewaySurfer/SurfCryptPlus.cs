using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.109 (SAT's dungeon notes):
// - Forks: in some long halls the way splits into three walled passages, one per lane, and only
//   one goes on. The other two end in a wall in the dark. Pick your lane before the split, by
//   the warm light at the far end of the open passage.
// - Sunken crypts: up to five chambers, each behind a mud bank you dig through (press attack:
//   Left mouse, Space or E). A burst of draugr rises behind some of the banks.
// - The boss's summoning items only start turning up once the biome's dungeon is behind you,
//   and its exit is the biome's checkpoint.
internal sealed partial class SurfGame
{
	// 1.16.150 (SAT's sketch: "a 3 branch type maze with only one of the branches having the
	// correct way out"): at a junction the hall opens three ways, left, ahead and right. Your
	// lane picks the branch (left lane left, middle ahead, right lane right; at a corner a press
	// toward the opening still turns). Two of them are dark dead ends: you come back out at the
	// junction's mouth, and the time it took is the cost. The real way on is lit further in.
	private sealed class CryptJunction
	{
		public float Z;
		// The corner it's at (its turn is the way on), or -1 in the middle of a hall (ahead is).
		public int Corner = -1;
		public bool Built;
	}

	private sealed class DigWall
	{
		public float Z;
		public int Hits;
		public bool Built;
		public bool Burst;
		public readonly List<GameObject> Pieces = new();
	}

	private readonly List<CryptJunction> m_junctions = new();
	private float m_junctionLastZ;
	private const float PocketDepth = 14f;
	private const float SideGap = 8f;
	private readonly List<DigWall> m_digs = new();
	private Segment? m_cryptSeg;
	private float m_cryptExitZ = float.NaN;
	private float m_digCooldown;
	private bool m_digWielded;
	private const int DigHits = 3;

	// The stretch's dungeon exit, if it has one still ahead or just behind: items wait for it.
	private float CryptExitFor(Segment s) => m_cryptSeg == s && !float.IsNaN(m_cryptExitZ) ? m_cryptExitZ : float.NaN;

	private void PlanCryptExtras(CryptRun c, Segment s)
	{
		m_junctions.Clear();
		m_digs.Clear();
		m_cryptSeg = s;
		m_cryptExitZ = c.End;
		int last = c.Legs.Count - 1;
		bool sunken = c.Theme == s_sunkenCrypt;
		int digs = 0;
		foreach (CryptLeg leg in c.Legs)
		{
			float from = leg.A + (leg.Index == 0 ? 20f : CryptHalf + 10f);
			float to = leg.B - (leg.Index == last ? 20f : CryptHalf + 14f);
			if (sunken && digs < 5 && leg.Index > 0 && to - from > 14f)
			{
				// A chamber behind a mud bank; draugr rise behind most of them, well past it
				// (1.16.150, SAT: they rose right behind the bank, where you couldn't get by).
				DigWall d = new DigWall { Z = from + 2f, Burst = Random.value < 0.65f };
				m_digs.Add(d);
				digs++;
				if (d.Burst && to - d.Z > 30f)
				{
					c.Piles.Add(new CryptPile { Z = d.Z + 22f, Side = -1 });
					c.Piles.Add(new CryptPile { Z = d.Z + 24f, Side = 1 });
				}
			}
		}
		// Junctions at the first turn of some out-and-back pairs (with room round them for the
		// dead ends), and in the middle of some long halls.
		for (int k = 0; k + 1 < c.Corners.Count && m_junctions.Count < 3; k += 2)
		{
			float hall = c.Corners[k] - (k == 0 ? c.Start : c.Corners[k - 1]);
			float side = c.Corners[k + 1] - c.Corners[k];
			float next = (k + 2 < c.Corners.Count ? c.Corners[k + 2] : c.End) - c.Corners[k + 1];
			if (hall >= 34f && side >= 26f && next >= 40f && !DigNear(c.Corners[k], 40f) && Random.value < 0.6f)
			{
				m_junctions.Add(new CryptJunction { Z = c.Corners[k], Corner = k });
			}
		}
		foreach (CryptLeg leg in c.Legs)
		{
			if (m_junctions.Count >= 3)
			{
				break;
			}
			float mid = Mathf.Round((leg.A + leg.B) * 0.5f);
			if (leg.Index == 0 || leg.Index == last || leg.B - leg.A < 64f || DigNear(mid, 40f) || Random.value > 0.45f)
			{
				continue;
			}
			m_junctions.Add(new CryptJunction { Z = mid, Corner = -1 });
		}
		m_junctionLastZ = m_z;
		Plugin.Log.LogInfo($"Dungeon extras: {m_junctions.Count} junctions, {m_digs.Count} mud banks.");
	}

	private void ResetCryptExtras()
	{
		m_junctions.Clear();
		m_digs.Clear();
		m_cryptSeg = null;
		m_cryptExitZ = float.NaN;
		m_digWielded = false;
		m_wetTime = 0f;
	}

	private bool InForkOrDig(float z)
	{
		foreach (CryptJunction j in m_junctions)
		{
			// The lanes clear before it (where a dead end sends you back to) and in its mouth.
			if (z > j.Z - Mathf.Max(16f, TurnWindow) - 30f && z < j.Z + 14f)
			{
				return true;
			}
		}
		foreach (DigWall d in m_digs)
		{
			if (z > d.Z - 8f && z < d.Z + 6f)
			{
				return true;
			}
		}
		return false;
	}

	private bool DigNear(float z, float within) => m_digs.Exists(d => Mathf.Abs(d.Z - z) < within);

	// A mud bank not yet dug through between two points along the track.
	private bool DigBetween(float a, float b) => m_digs.Exists(d => d.Hits < DigHits && d.Z > a && d.Z < b);

	private CryptJunction? JunctionAtCorner(int k) => k < 0 ? null : m_junctions.Find(j => j.Corner == k);

	// The middle-of-the-hall junction openings in a hall's walls, if any, as (from, to) along it.
	private List<(float a, float b)> JunctionGaps(CryptLeg leg)
	{
		List<(float a, float b)> gaps = new();
		foreach (CryptJunction j in m_junctions)
		{
			if (j.Corner < 0 && j.Z > leg.A && j.Z < leg.B)
			{
				gaps.Add((j.Z - SideGap * 0.5f, j.Z + SideGap * 0.5f));
			}
		}
		return gaps;
	}

	private bool InJunctionGap(CryptLeg leg, float z, float pad)
	{
		foreach ((float a, float b) in JunctionGaps(leg))
		{
			if (z > a - pad && z < b + pad)
			{
				return true;
			}
		}
		return false;
	}

	// A hall wall with the junction openings left out of it.
	private void WallAlongGaps(CryptLeg leg, Transform root, float x, float z0, float z1, float y)
	{
		float from = z0;
		foreach ((float a, float b) in JunctionGaps(leg))
		{
			if (a > from && a < z1)
			{
				WallAlong(root, x, from, a, y);
			}
			from = Mathf.Max(from, b);
		}
		if (z1 > from)
		{
			WallAlong(root, x, from, z1, y);
		}
	}

	// The roof over a stretch of hall or a dead end, x0 < x1, z0 < z1: slabs laid on the wall tops.
	private void CryptRoof(Transform root, float x0, float x1, float z0, float z1, float y)
	{
		CryptTheme theme = m_crypt!.Theme;
		if (!theme.Roofed || !Plugin.DungeonRoof.Value || x1 - x0 < 0.5f || z1 - z0 < 0.5f)
		{
			return;
		}
		int nx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / 4.2f));
		int nz = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / 4.2f));
		float tx = (x1 - x0) / nx;
		float tz = (z1 - z0) / nz;
		float overlap = theme.Rocky ? 0.8f : 0.05f;
		string names = theme.Roofs.Length > 0 ? theme.Roofs + "|" + theme.Floors : theme.Floors;
		for (int i = 0; i < nx; i++)
		{
			for (int j = 0; j < nz; j++)
			{
				Quaternion turned = theme.Rocky ? Quaternion.Euler(0f, 90f * Random.Range(0, 4), 0f) : Quaternion.identity;
				CryptPiece(root, names, turned, new Vector3(tx + overlap, theme.Rocky ? 1.2f : 0.5f, tz + overlap),
					new Vector3(x0 + tx * (i + 0.5f), y + WallH + theme.RoofLift, z0 + tz * (j + 0.5f)));
			}
		}
	}

	// The floor of a dead end, x0 < x1, z0 < z1.
	private void PocketFloor(Transform root, float x0, float x1, float z0, float z1, float y)
	{
		CryptTheme theme = m_crypt!.Theme;
		CryptRoof(root, x0, x1, z0, z1, y);
		int nx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / 3.7f));
		int nz = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / 3.7f));
		float tx = (x1 - x0) / nx;
		float tz = (z1 - z0) / nz;
		float overlap = theme.Rocky ? 0.6f : 0.03f;
		bool any = false;
		for (int i = 0; i < nx; i++)
		{
			for (int j = 0; j < nz; j++)
			{
				Quaternion turned = theme.Rocky ? Quaternion.Euler(0f, 90f * Random.Range(0, 4), 0f) : Quaternion.identity;
				any |= CryptPiece(root, theme.Floors, turned, new Vector3(tx + overlap, 0.25f, tz + overlap),
					new Vector3(x0 + tx * (i + 0.5f), y - 0.25f, z0 + tz * (j + 0.5f))) != null;
			}
		}
		if (!any)
		{
			GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Destroy(slab.GetComponent<Collider>());
			slab.layer = m_layer;
			slab.transform.SetParent(root, false);
			slab.transform.localPosition = new Vector3((x0 + x1) * 0.5f, y - 0.1f, (z0 + z1) * 0.5f);
			slab.transform.localScale = new Vector3(x1 - x0, 0.2f, z1 - z0);
			slab.GetComponent<Renderer>().sharedMaterial = BorrowMaterial("stone_floor_2x2") ?? m_biomes[Mathf.Min(1, m_biomes.Count - 1)].Ground;
		}
	}

	// A dead end out to one side (s = -1 left, +1 right) of the hall, `width` wide round zc.
	private void SidePocket(Transform root, int s, float zc, float width, float y)
	{
		float far = CryptHalf + PocketDepth;
		float za = zc - width * 0.5f;
		float zb = zc + width * 0.5f;
		PocketFloor(root, s < 0 ? -far : CryptHalf, s < 0 ? -CryptHalf : far, za, zb, y);
		float x0 = s < 0 ? -(far + CryptWall) : CryptHalf;
		float x1 = s < 0 ? -CryptHalf : far + CryptWall;
		WallAcross(root, za - CryptWall * 0.5f, x0, x1, y, 1, 0f, -1);
		WallAcross(root, zb + CryptWall * 0.5f, x0, x1, y, 1, 0f, 1);
		WallAlong(root, s * (far + CryptWall * 0.5f), za - CryptWall, zb + CryptWall, y);
		DeadEndDoor(root, new Vector3(s * (far - 0.15f), y, zc), 90f);
	}

	// A dead end straight on past a corner.
	private void AheadPocket(Transform root, float from, float y)
	{
		float wallX = CryptHalf + CryptWall * 0.5f;
		float to = from + PocketDepth;
		PocketFloor(root, -CryptHalf, CryptHalf, from, to, y);
		WallAlong(root, -wallX, from, to + CryptWall, y);
		WallAlong(root, wallX, from, to + CryptWall, y);
		WallAcross(root, to + CryptWall * 0.5f, -wallX - CryptWall * 0.5f, wallX + CryptWall * 0.5f, y);
		DeadEndDoor(root, new Vector3(0f, y, to - 0.15f), 0f);
	}

	// A burial chamber's dead ends end at a shut wooden door (a sunken crypt's at a grate).
	private void DeadEndDoor(Transform root, Vector3 at, float yaw)
	{
		string door = m_crypt!.Theme.Door;
		if (door.Length == 0)
		{
			return;
		}
		CryptPiece(root, door, Quaternion.Euler(0f, yaw, 0f), new Vector3(yaw == 0f ? 2.4f : 0.4f, 2.8f, yaw == 0f ? 0.4f : 2.4f), at, true, 2f);
	}

	// While a hall is built: the dead ends of its junctions, and its mud banks.
	private void BuildCryptExtras(CryptLeg leg, Transform root, float y)
	{
		CryptRun c = m_crypt!;
		foreach (CryptJunction j in m_junctions)
		{
			if (j.Built)
			{
				continue;
			}
			if (j.Corner >= 0 && j.Corner == leg.Index)
			{
				j.Built = true;
				int turn = c.Dirs[j.Corner];
				SidePocket(root, -turn, leg.B, CryptHalf * 2f, y);
				AheadPocket(root, leg.B + CryptHalf + CryptWall, y);
			}
			else if (j.Corner < 0 && j.Z > leg.A && j.Z < leg.B)
			{
				j.Built = true;
				SidePocket(root, -1, j.Z, SideGap, y);
				SidePocket(root, 1, j.Z, SideGap, y);
			}
		}
		foreach (DigWall d in m_digs)
		{
			if (d.Built || d.Z < leg.A || d.Z > leg.B)
			{
				continue;
			}
			d.Built = true;
			for (int lane = 0; lane < 3; lane++)
			{
				GameObject? pile = CryptPiece(root, "mudpile2|mudpile", Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
					new Vector3(LaneWidth * 1.5f, 2.8f, 2.4f), new Vector3(LaneX(lane), y, d.Z + Random.Range(-0.3f, 0.3f)));
				if (pile != null)
				{
					d.Pieces.Add(pile);
				}
			}
		}
	}

	private float m_wetTime;
	private float m_forkTurnFade;

	private float WetStaminaRegen => m_wetTime > 0f ? 0.5f : 1f;
	private float WetStaminaCost => m_wetTime > 0f ? 1.5f : 1f;

	// Each frame on the run: passing a junction in the middle of a hall in an outer lane takes
	// that side's dead end. Returns the speed factor (always 1 now; wet stays for later use).
	private float UpdateForks(float dt)
	{
		m_wetTime = Mathf.Max(0f, m_wetTime - dt);
		m_forkTurnFade = Mathf.Max(0f, m_forkTurnFade - dt);
		foreach (CryptJunction j in m_junctions)
		{
			if (j.Corner < 0 && m_junctionLastZ < j.Z && m_z >= j.Z && m_lane != 1 && m_state == State.Running)
			{
				WrongWay(j);
				break;
			}
		}
		m_junctionLastZ = m_z;
		return 1f;
	}

	// Down a dead end and back: a dark moment, the sound of you turning round, and you're back
	// before the junction (no life lost).
	private void WrongWay(CryptJunction j)
	{
		m_z = j.Z - Mathf.Max(16f, TurnWindow) - 8f;
		m_junctionLastZ = m_z;
		m_lane = 1;
		m_cryptOver = 0f;
		m_cryptBonk = -1f;
		m_forkTurnFade = 0.6f;
		Flash(new Color(0.02f, 0.01f, 0.05f), 0.95f);
		m_audio.Play("sfx_unarmed_blocked|sfx_wood_blocked", 0.6f, 0.8f);
		m_audio.Play("sfx_raven_flap|sfx_dodge", 0.5f);
		m_hurtTime = Mathf.Max(m_hurtTime, 0.8f);
		Plugin.Log.LogInfo($"Dead end at the junction at {j.Z:0} m; back to {m_z:0} m.");
	}

	private void WetGui(float x, float y, float k)
	{
		if (m_wetTime <= 0f)
		{
			return;
		}
		StatusEffect? wet = ObjectDB.instance != null ? ObjectDB.instance.GetStatusEffect("Wet".GetStableHashCode()) : null;
		if (wet != null && wet.m_icon != null)
		{
			DrawSprite(new Rect(x, y, 30f * k, 30f * k), wet.m_icon, (0.6f + 0.4f * Mathf.Clamp01(m_wetTime / 5f)) * s_guiAlpha);
		}
	}

	// Each frame on the run: stops you at a mud bank until you've dug through it.
	// Returns the speed factor (0 while digging).
	private float UpdateDig(float dt, bool canInput)
	{
		m_digCooldown -= dt;
		DigWall? at = null;
		foreach (DigWall d in m_digs)
		{
			if (d.Hits < DigHits && d.Z - m_z < 2.2f && d.Z - m_z > -1f)
			{
				at = d;
				break;
			}
		}
		if (at == null)
		{
			if (m_digWielded)
			{
				m_digWielded = false;
				Unwield();
			}
			return 1f;
		}
		if (!m_digWielded)
		{
			m_digWielded = Wield("PickaxeAntler|PickaxeBronze|PickaxeIron|PickaxeBlackMetal");
		}
		m_z = Mathf.Min(m_z, at.Z - 2.2f);
		bool press = canInput && (Input.GetKeyDown(KeyCode.Mouse0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E) || BotKey(KeyCode.E) || Input.GetKeyDown(Plugin.ThrowKey.Value));
		if (press && m_digCooldown <= 0f)
		{
			m_digCooldown = 0.35f;
			at.Hits++;
			SetTrigger("swing_pickaxe");
			m_audio.Play("sfx_dig|sfx_mud_hit|sfx_rock_hit", 0.9f, Random.Range(0.9f, 1.1f));
			m_shake = Mathf.Max(m_shake, 0.12f);
			if (FindFx("vfx_dig|vfx_Place_mud_road|vfx_RockHit") is GameObject fx)
			{
				PlayFx(fx, new Vector3(m_x, RoadY(at.Z) + 0.6f, at.Z - 1f), Quaternion.identity, 2f);
			}
			float left = 1f - at.Hits / (float)DigHits;
			foreach (GameObject p in at.Pieces)
			{
				if (p != null)
				{
					p.transform.localScale *= Mathf.Lerp(0.55f, 0.85f, left);
					if (at.Hits >= DigHits)
					{
						Destroy(p);
					}
				}
			}
			if (at.Hits >= DigHits)
			{
				m_audio.Play("sfx_rock_destroyed|sfx_mud_destroyed|sfx_dig", 1f, 0.85f);
			}
		}
		return at.Hits >= DigHits ? 1f : 0f;
	}
}
