using UnityEngine;

namespace SnakewaySurfer;

// 1.14.2 (SAT): fighting back by parrying. Grabbing the boss's weapon arms you; the boss closes
// in and swings at your lane, up to three times, each swing marked red before it lands. Parry
// one (block just before it lands) and it staggers. Blocking or dodging keeps you safe but does
// no harm; after three swings the weapon is spent. Eikthyr's spear and Moder's bow work their
// own way (SurfThrow.cs, SurfBow.cs).
// 1.16.2 (SAT): the parry staggers it (its own reeling clip, as a parried creature in the game),
// you get a chain of blows in with the weapon in your hands (the weapon's own clips: the
// atgeir's three thrusts, the mace's swings), then dodge roll back away and it backs off to
// its distance. Moder down on the track is fought this way with a silver atgeir.
internal sealed partial class SurfGame
{
	private const int DuelTries = 3;
	private int m_duelTries;
	private float m_duelTimer;
	private float m_duelLand = -1f;
	private int m_duelLane;
	private GameObject? m_duelMarker;
	private float m_duelPull;
	private float m_duelSlide;

	// The counter: blows left to land, the wait for the next and for the one swinging now.
	private const int ComboBlows = 3;
	private int m_comboStep = -1;
	private float m_comboTimer;
	private float m_comboBlowIn = -1f;
	private ItemDrop.ItemData.SharedData? m_comboWeapon;
	// After it all, it backs off to its distance (no jump back).
	private float m_recede;

	private bool Dueling => m_duelTries > 0;

	private bool InCombo => m_comboStep >= 0;

	private bool DuelBusy => Dueling || InCombo || m_recede > 0f;

	private int m_duelHints;

	private void ArmDuel()
	{
		m_duelTries = DuelTries;
		m_duelTimer = 1.1f;
		m_duelLand = -1f;
		m_iconPulse = 1f;
		m_bossOff = m_bossZ - m_z;
		m_audio.Play("sfx_equip_start|sfx_pickable_pick", 1f, 0.9f);
		if (m_boss != null)
		{
			ArmForMelee(m_boss);
			// 1.16.x (playtest: nothing said how to use the surtling core on the Elder): Hugin
			// says it the first two times a run.
			if (m_duelHints < 2)
			{
				m_duelHints++;
				m_banner = $"{m_boss.Name} comes at you! Block just before each swing lands to parry it, and your blow lands.";
				m_bannerTime = 5f;
			}
		}
		// It turns on you: no other attacks meanwhile.
		m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, 99f);
		m_bossHitTimer = Mathf.Max(m_bossHitTimer, 99f);
	}

	// What you fight it with up close: Moder's silver atgeir, the boss's own weapon when it's
	// one you swing (Bonemass's mace), else what you carry yourself.
	private void ArmForMelee(BossDef def)
	{
		if (Flies(def))
		{
			if (Wield(ModerWeapon, true))
			{
				Silver(m_wielded);
			}
			return;
		}
		Wield(def.HitItem, true);
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
			m_recede = 1.5f;
			Unwield();
		}
	}

	// Each frame while dueling, instead of its usual moves: it comes up close ahead, facing you.
	private void UpdateDuel(float dt, BossDef def)
	{
		if (m_bossGo == null)
		{
			EndDuel(false);
			EndCombo();
			m_recede = 0f;
			return;
		}
		m_duelPull = Mathf.MoveTowards(m_duelPull, 1f, dt * 1.2f);
		float close = Mathf.Max(5f, (m_bossInfo != null ? m_bossInfo.Height : 4f) * 0.9f);
		if (Dueling || InCombo)
		{
			m_bossOff = Mathf.Lerp(m_bossOff, close, dt * 2.5f);
		}
		else
		{
			// Backing off after the fight up close.
			m_recede -= dt;
			float home = BossDistance(def);
			m_bossOff = Mathf.MoveTowards(m_bossOff, home, dt * Mathf.Max(8f, (home - close) / 1.2f));
			if (Mathf.Abs(m_bossOff - home) < 0.3f)
			{
				m_recede = 0f;
			}
		}
		m_bossZ = m_z + m_bossOff;
		if (m_bossThing != null)
		{
			m_bossThing.Z = m_bossZ;
		}
		Vector3 p = m_bossGo.transform.localPosition;
		// 1.16.x (SAT: the Queen spazzed all over the road): it holds still while a swing plays
		// and only eases across between swings (no sliding back and forth after your lane).
		float aimX = LaneX(m_duelLand >= 0f ? m_duelLane : m_lane) * 0.6f;
		// (Still coming in from the side of the track, it keeps coming.)
		bool hold = (m_duelLand >= 0f || InCombo) && Mathf.Abs(p.x - aimX) < 2.5f;
		if (hold)
		{
			m_duelSlide = 0f;
		}
		float x = hold ? p.x : Mathf.SmoothDamp(p.x, aimX, ref m_duelSlide, 0.45f, 5f, dt);
		// Moder fights you on the ground; others keep a little of their height.
		float lift = Flies(def) ? 0f : def.Fly * 0.4f;
		m_bossGo.transform.localPosition = new Vector3(x, RoadY(m_bossZ) + lift, m_bossZ);
		m_bossGo.transform.localRotation = Quaternion.RotateTowards(m_bossGo.transform.localRotation, Quaternion.Euler(0f, 180f, 0f), 360f * dt);
		if (m_bossAnim != null)
		{
			SetParam(m_bossAnim, "forward_speed", InCombo ? 0f : m_speed);
		}
		if (InCombo)
		{
			UpdateCombo(dt, def);
			return;
		}
		if (!Dueling)
		{
			return;
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
		if (inLane && m_blocking && m_runTime - m_blockPressed <= ParryWindowNow)
		{
			// Parried: it reels, and you get your blows in.
			NoteParry();
			PlayBlock(true);
			Flash(new Color(1f, 0.95f, 0.75f), 0.6f);
			m_blockXp += 2f;
			Plugin.Log.LogInfo($"Parried {def.Name}: counter.");
			EndDuel(false);
			StartCombo(def);
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
		// Moder bites or rakes with a claw (her taunt isn't a blow).
		string keys = Flies(def) ? (Random.value < 0.5f ? "claw|bite" : "bite|claw") : "attack|punch|swing|slam|melee|claw|bite";
		CreatureAttack? attack = BossAttackFor(keys);
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

	// ---------------------------------------------------------------- the counter

	private void StartCombo(BossDef def)
	{
		if (m_bossGo == null || m_bossSink >= 0f)
		{
			return;
		}
		if (m_wielded == null)
		{
			ArmForMelee(def);
		}
		m_comboWeapon = SwingWeapon();
		m_comboStep = 0;
		m_comboTimer = 0.2f;
		m_comboBlowIn = -1f;
		m_bossOff = m_bossZ - m_z;
		// It reels from the parry, as a parried creature does in the game.
		if (m_bossAnim != null)
		{
			SetTrigger(m_bossAnim, "stagger");
		}
		m_audio.Play("sfx_creature_stagger|sfx_unarmed_blocked", 0.8f, 0.9f);
		// Nothing else comes at you meanwhile.
		m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, 99f);
		m_bossHitTimer = Mathf.Max(m_bossHitTimer, 99f);
	}

	private void UpdateCombo(float dt, BossDef def)
	{
		if (m_comboBlowIn >= 0f)
		{
			m_comboBlowIn -= dt;
			if (m_comboBlowIn < 0f)
			{
				ComboBlow(def, m_comboStep >= ComboBlows);
				if (m_bossSink >= 0f || m_boss == null)
				{
					// That was the last of it: it falls.
					EndCombo();
					RollAway();
					return;
				}
			}
		}
		m_comboTimer -= dt;
		if (m_comboTimer > 0f || m_comboBlowIn >= 0f)
		{
			return;
		}
		if (m_comboStep < ComboBlows)
		{
			string? swing = SwingTrigger(m_comboWeapon, m_comboStep);
			float blow = swing != null && m_avatarAnim != null ? Mathf.Clamp(AnimTiming.Delay(m_avatarAnim, swing, 0.32f), 0.15f, 0.6f) : 0.3f;
			if (swing != null && m_avatarAnim != null)
			{
				TriggerWatched(m_avatarAnim, swing, left =>
				{
					if (m_comboBlowIn > 0f)
					{
						m_comboBlowIn = Mathf.Clamp(left, 0.1f, 0.7f);
					}
				});
			}
			m_comboBlowIn = blow;
			m_comboStep++;
			// The last blow of a chain is the heavy one: a beat longer.
			m_comboTimer = blow + (m_comboStep == ComboBlows ? 0.5f : 0.22f);
			// The weapon's own swing sound.
			EffectList? swingFx = m_comboWeapon?.m_attack?.m_startEffect;
			if (swingFx?.m_effectPrefabs == null || swingFx.m_effectPrefabs.Length == 0)
			{
				swingFx = m_comboWeapon?.m_startEffect;
			}
			if (swingFx?.m_effectPrefabs != null && swingFx.m_effectPrefabs.Length > 0)
			{
				m_audio.PlayEffects(swingFx, 0.9f);
			}
			else
			{
				m_audio.Play(Sfx.ClubSwing, 0.7f, Random.Range(0.95f, 1.08f));
			}
			return;
		}
		// Then away: a roll back while it backs off.
		EndCombo();
		RollAway();
		m_recede = 1.6f;
		m_bossAttackTimer = 2.2f;
		m_bossHitTimer = 6f;
	}

	// One blow lands on it: the weapon's own hit effect and sound where it strikes; the last
	// of the chain counts as the hit on it.
	private void ComboBlow(BossDef def, bool last)
	{
		if (m_bossGo == null)
		{
			return;
		}
		Vector3 at = m_bossGo.transform.localPosition + new Vector3(Random.Range(-0.6f, 0.6f), def.MaxHeight * Random.Range(0.25f, 0.45f), -Mathf.Max(1f, m_bossOff * 0.25f));
		bool shown = false;
		if (m_comboWeapon?.m_hitEffect?.m_effectPrefabs != null)
		{
			foreach (EffectList.EffectData e in m_comboWeapon.m_hitEffect.m_effectPrefabs)
			{
				if (e?.m_prefab != null && e.m_enabled && e.m_prefab.GetComponentInChildren<ParticleSystem>(true) != null)
				{
					PlayFx(e.m_prefab, at, Quaternion.identity, 2f);
					shown = true;
				}
			}
			m_audio.PlayEffects(m_comboWeapon.m_hitEffect, 1f);
		}
		if (!shown && FindFx("vfx_HitSparks|vfx_hit_metal|vfx_blocked") is GameObject sparks)
		{
			PlayFx(sparks, at, Quaternion.identity, 2f);
		}
		m_audio.Play(Sfx.HitMonster, last ? 1f : 0.75f, last ? 0.8f : Random.Range(0.95f, 1.1f));
		m_shake = Mathf.Max(m_shake, last ? 0.3f : 0.15f);
		if (last)
		{
			BossHit();
		}
		else if (m_bossAnim != null)
		{
			// Each blow rocks it.
			SetTrigger(m_bossAnim, "stagger");
		}
	}

	private void EndCombo()
	{
		m_comboStep = -1;
		m_comboBlowIn = -1f;
		m_comboWeapon = null;
		Unwield();
		// A short blink of grace afterwards, so a hazard that was already on you can't land the moment it ends.
		if (m_state == State.Running)
		{
			m_hurtTime = Mathf.Max(m_hurtTime, 0.8f);
		}
	}
}
