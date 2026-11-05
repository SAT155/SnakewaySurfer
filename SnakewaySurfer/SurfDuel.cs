using UnityEngine;

namespace SnakewaySurfer;

// 1.14.2 (Sam): fighting back by parrying. Grabbing the boss's weapon arms you; the boss closes
// in and swings at your lane, up to three times, each swing marked red before it lands. Parry
// one (block just before it lands) and it staggers and your blow lands. Blocking or dodging
// keeps you safe but does no harm; after three swings the weapon is spent. Eikthyr's spear and
// Moder's ballista work their own way (SurfThrow.cs, SurfBoss.cs).
internal sealed partial class SurfGame
{
	private const int DuelTries = 3;
	private int m_duelTries;
	private float m_duelTimer;
	private float m_duelLand = -1f;
	private int m_duelLane;
	private GameObject? m_duelMarker;
	private float m_duelPull;

	private bool Dueling => m_duelTries > 0;

	private void ArmDuel()
	{
		m_duelTries = DuelTries;
		m_duelTimer = 1.1f;
		m_duelLand = -1f;
		m_iconPulse = 1f;
		m_audio.Play("sfx_equip_start|sfx_pickable_pick", 1f, 0.9f);
		// It turns on you: no other attacks meanwhile.
		m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, 99f);
		m_bossHitTimer = Mathf.Max(m_bossHitTimer, 99f);
	}

	private void EndDuel(bool spent)
	{
		m_duelTries = 0;
		m_duelLand = -1f;
		if (m_duelMarker != null)
		{
			Destroy(m_duelMarker);
			m_duelMarker = null;
		}
		m_bossAttackTimer = 2f;
		m_bossHitTimer = spent ? 3f : 6f;
		if (spent)
		{
			m_audio.Play("sfx_wood_break|sfx_unarmed_blocked", 1f, 0.8f);
		}
	}

	// Each frame while dueling, instead of its usual moves: it comes up close ahead, facing you.
	private void UpdateDuel(float dt, BossDef def)
	{
		if (m_bossGo == null)
		{
			EndDuel(false);
			return;
		}
		m_duelPull = Mathf.MoveTowards(m_duelPull, 1f, dt * 1.2f);
		float close = Mathf.Max(5f, (m_bossInfo != null ? m_bossInfo.Height : 4f) * 0.9f);
		m_bossOff = Mathf.Lerp(m_bossOff, close, dt * 2.5f);
		m_bossZ = m_z + m_bossOff;
		if (m_bossThing != null)
		{
			m_bossThing.Z = m_bossZ;
		}
		Vector3 p = m_bossGo.transform.localPosition;
		float x = Mathf.MoveTowards(p.x, LaneX(m_duelLand >= 0f ? m_duelLane : m_lane) * 0.6f, dt * 4f);
		m_bossGo.transform.localPosition = new Vector3(x, RoadY(m_bossZ) + def.Fly * 0.4f, m_bossZ);
		m_bossGo.transform.localRotation = Quaternion.RotateTowards(m_bossGo.transform.localRotation, Quaternion.Euler(0f, 180f, 0f), 360f * dt);
		if (m_bossAnim != null)
		{
			SetParam(m_bossAnim, "forward_speed", m_speed);
		}

		if (m_duelLand < 0f)
		{
			m_duelTimer -= dt;
			if (m_duelTimer <= 0f)
			{
				DuelSwing(def);
			}
			return;
		}
		m_duelLand -= dt;
		if (m_duelMarker != null)
		{
			PlaceMarker(m_duelMarker, m_duelLane, m_z + 0.6f);
		}
		if (m_duelLand > 0f)
		{
			return;
		}
		// The blow lands.
		m_duelLand = -1f;
		if (m_duelMarker != null)
		{
			Destroy(m_duelMarker);
			m_duelMarker = null;
		}
		m_shake = Mathf.Max(m_shake, 0.2f);
		if (def.StrikeSfx.Length > 0)
		{
			m_audio.Play(def.StrikeSfx, 0.8f);
		}
		m_duelTries--;
		bool inLane = Mathf.Abs(m_x - LaneX(m_duelLane)) < LaneWidth * 0.5f;
		if (inLane && m_blocking && m_runTime - m_blockPressed <= Plugin.ParryWindow.Value)
		{
			// Parried: it reels, and your blow lands.
			PlayBlock(true);
			Flash(new Color(1f, 0.95f, 0.75f), 0.6f);
			Plugin.Log.LogInfo($"Parried {def.Name}: counter-blow.");
			EndDuel(false);
			BossHit();
			return;
		}
		if (inLane)
		{
			Hit(new Thing { Label = def.Name, CrashText = $"{def.Name} struck you down! (Parry its swing: block just before it lands.)" },
				$"{def.Name} struck you down! (Parry its swing: block just before it lands.)");
		}
		if (m_duelTries <= 0)
		{
			EndDuel(true);
		}
		else
		{
			m_duelTimer = 0.7f;
		}
	}

	private void DuelSwing(BossDef def)
	{
		CreatureAttack? attack = BossAttackFor("attack|punch|swing|slam|melee|claw|bite");
		float warn = attack != null && m_bossAnim != null ? Mathf.Clamp(AnimTiming.Delay(m_bossAnim, attack.Animation, 1.1f), 0.8f, 1.8f) : 1.1f;
		m_duelLane = m_lane;
		m_duelLand = warn;
		if (attack != null && m_bossAnim != null && m_bossHidden <= 0f)
		{
			TriggerWatched(m_bossAnim, attack.Animation, left =>
			{
				if (m_duelLand > 0f)
				{
					m_duelLand = Mathf.Clamp(left, 0.6f, 2f);
				}
			});
			AttackSound(attack, false, 0.9f);
		}
		m_duelMarker = MakeMarker(m_duelLane, m_z + 0.6f, 3f, m_markerMaterial);
	}
}
