using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.109 (Sam's dungeon notes):
// - Forks: in some long halls the way splits into three walled passages, one per lane, and only
//   one goes on. The other two end in a wall in the dark. Pick your lane before the split, by
//   the warm light at the far end of the open passage.
// - Sunken crypts: up to five chambers, each behind a mud bank you dig through (press attack:
//   Left mouse, Space or E). A burst of draugr rises behind some of the banks.
// - The boss's summoning items only start turning up once the biome's dungeon is behind you,
//   and its exit is the biome's checkpoint.
internal sealed partial class SurfGame
{
	private sealed class CryptFork
	{
		public float Z0;
		public float Z1;
		public int Lane;
		// A wrong passage that's flooded instead of walled off: you swim through, and come out wet.
		public int Flood = -1;
		public bool Built;
		public bool Turned;
	}

	private sealed class DigWall
	{
		public float Z;
		public int Hits;
		public bool Built;
		public bool Burst;
		public readonly List<GameObject> Pieces = new();
	}

	private readonly List<CryptFork> m_forks = new();
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
		m_forks.Clear();
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
				// A chamber behind a mud bank; draugr rise behind most of them.
				DigWall d = new DigWall { Z = from + 2f, Burst = Random.value < 0.65f };
				m_digs.Add(d);
				digs++;
				if (d.Burst)
				{
					c.Piles.Add(new CryptPile { Z = d.Z + 9f, Side = -1 });
					c.Piles.Add(new CryptPile { Z = d.Z + 11f, Side = 1 });
				}
				from = d.Z + 16f;
			}
			float len = Random.Range(18f, 30f);
			if (m_forks.Count < 3 && to - from > len + 4f && Random.value < 0.6f)
			{
				float z0 = Random.Range(from, to - len);
				CryptFork f = new CryptFork { Z0 = z0, Z1 = z0 + len, Lane = Random.Range(0, 3) };
				if (m_waterMaterial != null && Random.value < 0.5f)
				{
					f.Flood = (f.Lane + Random.Range(1, 3)) % 3;
				}
				m_forks.Add(f);
				m_dividers.Add((f.Z0, f.Z1, 0));
				m_dividers.Add((f.Z0, f.Z1, 1));
			}
		}
		Plugin.Log.LogInfo($"Dungeon extras: {m_forks.Count} forks, {m_digs.Count} mud banks.");
	}

	private void ResetCryptExtras()
	{
		m_forks.Clear();
		m_digs.Clear();
		m_cryptSeg = null;
		m_cryptExitZ = float.NaN;
		m_digWielded = false;
		m_wetTime = 0f;
	}

	private bool InForkOrDig(float z)
	{
		foreach (CryptFork f in m_forks)
		{
			if (z > f.Z0 - 6f && z < f.Z1 + 4f)
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

	// While a hall is built: its forks' walls and lights, and its mud banks.
	private void BuildCryptExtras(CryptLeg leg, Transform root, float y)
	{
		foreach (CryptFork f in m_forks)
		{
			if (f.Built || f.Z0 < leg.A || f.Z0 > leg.B)
			{
				continue;
			}
			f.Built = true;
			foreach (float x in new[] { -LaneWidth * 0.5f, LaneWidth * 0.5f })
			{
				WallAlong(root, x, f.Z0, f.Z1, y);
			}
			for (int lane = 0; lane < 3; lane++)
			{
				if (lane == f.Lane)
				{
					// The way on: warm light where the passage opens out again.
					Light l = new GameObject("ForkLight").AddComponent<Light>();
					l.gameObject.layer = m_layer;
					l.transform.SetParent(root, false);
					l.transform.localPosition = new Vector3(LaneX(lane), y + 2f, f.Z1 + 1.5f);
					l.type = LightType.Point;
					l.range = 10f;
					l.intensity = 2.2f;
					l.color = new Color(1f, 0.75f, 0.4f);
					l.shadows = LightShadows.None;
					l.cullingMask = 1 << m_layer;
					continue;
				}
				if (lane == f.Flood)
				{
					// Dark water filling the passage, as in the game's flooded crypts.
					GameObject water = GameObject.CreatePrimitive(PrimitiveType.Quad);
					Destroy(water.GetComponent<Collider>());
					water.layer = m_layer;
					water.transform.SetParent(root, false);
					water.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
					water.transform.localScale = new Vector3(LaneWidth - 0.6f, f.Z1 - f.Z0, 1f);
					water.transform.localPosition = new Vector3(LaneX(lane), y + 0.55f, (f.Z0 + f.Z1) * 0.5f);
					water.GetComponent<Renderer>().sharedMaterial = m_waterMaterial;
					continue;
				}
				// 1.16.112 (Sam: no damage for a wrong pick): a dead end turns you round instead.
				WallAcross(root, f.Z1, LaneX(lane) - LaneWidth * 0.5f, LaneX(lane) + LaneWidth * 0.5f, y);
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

	// While a hall is built: its forks' walls and lights, and its mud banks.
	private void BuildCryptExtras(CryptLeg leg, Transform root, float y)
	{
		foreach (CryptFork f in m_forks)
		{
			if (f.Built || f.Z0 < leg.A || f.Z0 > leg.B)
			{
				continue;
			}
			f.Built = true;
			foreach (float x in new[] { -LaneWidth * 0.5f, LaneWidth * 0.5f })
			{
				WallAlong(root, x, f.Z0, f.Z1, y);
			}
			for (int lane = 0; lane < 3; lane++)
			{
				if (lane == f.Lane)
				{
					// The way on: warm light where the passage opens out again.
					Light l = new GameObject("ForkLight").AddComponent<Light>();
					l.gameObject.layer = m_layer;
					l.transform.SetParent(root, false);
					l.transform.localPosition = new Vector3(LaneX(lane), y + 2f, f.Z1 + 1.5f);
					l.type = LightType.Point;
					l.range = 10f;
					l.intensity = 2.2f;
					l.color = new Color(1f, 0.75f, 0.4f);
					l.shadows = LightShadows.None;
					l.cullingMask = 1 << m_layer;
					continue;
				}
				WallAcross(root, f.Z1, LaneX(lane) - LaneWidth * 0.5f, LaneX(lane) + LaneWidth * 0.5f, y);
				GameObject dead = new GameObject("DeadEnd");
				dead.transform.SetParent(m_stage, false);
				m_things.Add(new Thing
				{
					Go = dead,
					Kind = Kind.Block,
					Lane = lane,
					Z = f.Z1 - 0.4f,
					HalfLength = 0.4f,
					CrashText = "A dead end! (Before the split, look for the passage with light at its end.)",
				});
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

	// Each frame on the run: a dead end turns you back to the fork's mouth (the time it took is
	// the cost); a flooded passage slows you to a swim and leaves you wet for 30 seconds.
	private float UpdateForks(float dt)
	{
		m_wetTime = Mathf.Max(0f, m_wetTime - dt);
		m_forkTurnFade = Mathf.Max(0f, m_forkTurnFade - dt);
		float factor = 1f;
		foreach (CryptFork f in m_forks)
		{
			if (m_z < f.Z0 || m_z > f.Z1 + 0.5f)
			{
				continue;
			}
			if (m_lane == f.Flood)
			{
				factor = 0.55f;
				m_wetTime = 30f;
				if (Random.value < dt * 3f)
				{
					Splash(0.4f);
				}
				continue;
			}
			if (m_lane != f.Lane && m_z > f.Z1 - 1.4f)
			{
				// Bump, turn round, and you're back where the passages split.
				m_z = f.Z0 - 14f;
				m_forkTurnFade = 0.6f;
				Flash(new Color(0.02f, 0.01f, 0.05f), 0.95f);
				m_audio.Play("sfx_unarmed_blocked|sfx_wood_blocked", 0.6f, 0.8f);
				m_audio.Play("sfx_raven_flap|sfx_dodge", 0.5f);
				m_hurtTime = Mathf.Max(m_hurtTime, 0.8f);
			}
		}
		return factor;
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
		bool press = canInput && (Input.GetKeyDown(KeyCode.Mouse0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(Plugin.ThrowKey.Value));
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
