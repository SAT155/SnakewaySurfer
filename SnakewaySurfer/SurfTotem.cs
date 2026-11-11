using UnityEngine;

namespace SnakewaySurfer;

// 1.16.124 (SAT: Yagluth needs a gimmick; "you have to aim it back at him"): when Yagluth
// charges his beam, the Fuling totems you gathered to summon him come up in your hand. Move the
// mouse to hold a totem over him as the beam lands: it bounces back and burns him (a boss hit).
// Aim off him and the beam comes down your lane (get out of it). Five totems, one per bounce.
internal sealed partial class SurfGame
{
	private float m_totemCharge = -1f;
	private int m_totemLane;
	private int m_totems;
	private GameObject? m_totemMarker;
	private float m_totemFlash;
	private Vector2 m_totemBeamFrom;
	private Vector2 m_totemBeamTo;
	private float m_totemBeamTime;

	private bool TotemBoss => m_boss != null && m_boss.Prefab == "GoblinKing";

	private void ResetTotems(BossDef def)
	{
		m_totems = def.Prefab == "GoblinKing" ? def.Amount : 0;
		EndTotemBeam();
	}

	private void StartTotemBeam(BossDef def)
	{
		if (m_totemCharge >= 0f)
		{
			return;
		}
		if (m_totems <= 0)
		{
			// Out of totems: his beam is a plain lane strike again.
			BossStrike(def, new[] { m_lane }, AheadZ(1.6f), 5f, "breath");
			return;
		}
		m_totemLane = m_lane;
		m_totemCharge = 2f;
		CreatureAttack? beam = BossAttackFor("beam");
		if (beam != null && m_bossAnim != null && m_bossHidden <= 0f)
		{
			m_totemCharge = Mathf.Clamp(AnimTiming.Delay(m_bossAnim, beam.Animation, 2f), 1.4f, 3f);
			TriggerWatched(m_bossAnim, beam.Animation, left =>
			{
				if (m_totemCharge > 0f)
				{
					m_totemCharge = Mathf.Clamp(left, 1f, 3f);
				}
			});
			AttackSound(beam, false, 1f);
		}
		m_totemMarker = MakeMarker(m_totemLane, m_z + 3f, 8f, m_markerMaterial);
		// Hold off his other moves until it lands.
		m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, m_totemCharge + 1.2f);
		m_audio.Play("sfx_gui_craftitem|sfx_pickable_pick", 0.6f, 0.8f);
	}

	private void EndTotemBeam()
	{
		m_totemCharge = -1f;
		if (m_totemMarker != null)
		{
			Destroy(m_totemMarker);
			m_totemMarker = null;
		}
	}

	// Where Yagluth is on screen (GUI coordinates), and how big.
	private bool BossOnScreen(out Vector2 at, out float radius)
	{
		at = Vector2.zero;
		radius = 0f;
		if (m_bossGo == null || m_camera == null || !Pictures.MeshBounds(m_bossGo, out Bounds b))
		{
			return false;
		}
		Vector3 c = m_camera.WorldToScreenPoint(b.center);
		Vector3 top = m_camera.WorldToScreenPoint(b.center + m_stage.up * b.extents.y);
		if (c.z <= 0f)
		{
			return false;
		}
		at = new Vector2(c.x, Screen.height - c.y);
		radius = Mathf.Clamp(Mathf.Abs(top.y - c.y) * 0.8f, Screen.height * 0.05f, Screen.height * 0.25f);
		return true;
	}

	private void UpdateTotemBeam(float dt)
	{
		m_totemFlash = Mathf.Max(0f, m_totemFlash - dt);
		m_totemBeamTime = Mathf.Max(0f, m_totemBeamTime - dt);
		if (m_totemCharge < 0f)
		{
			return;
		}
		if (m_totemMarker != null)
		{
			PlaceMarker(m_totemMarker, m_totemLane, m_z + 3f);
		}
		m_totemCharge -= dt;
		if (m_totemCharge > 0f)
		{
			return;
		}
		Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
		bool onHim = BossOnScreen(out Vector2 boss, out float radius) && Vector2.Distance(mouse, boss) <= radius;
		m_totemBeamFrom = boss;
		m_totemBeamTo = onHim ? mouse : new Vector2(Screen.width * 0.5f, Screen.height * 0.75f);
		m_totemBeamTime = 0.5f;
		EndTotemBeam();
		if (FindFx("fx_goblinking_beam_hit") is GameObject hitFx)
		{
			Vector3 where = onHim && m_bossGo != null ? m_bossGo.transform.localPosition + Vector3.up * 3f : new Vector3(LaneX(m_totemLane), RoadY(m_z + 2f), m_z + 2f);
			PlayFx(hitFx, where, Quaternion.identity, 2f);
		}
		m_shake = Mathf.Max(m_shake, 0.35f);
		if (onHim)
		{
			// Bounced back: the totem crumbles, and he burns.
			m_totems--;
			m_totemFlash = 0.8f;
			Flash(new Color(0.5f, 1f, 0.6f), 0.5f);
			m_audio.Play("sfx_goblinking_beam_hit|sfx_fireball_hit", 1f, 1.1f);
			BossHit();
			return;
		}
		m_audio.Play("sfx_goblinking_beam_hit|sfx_fireball_hit", 1f, 0.9f);
		if (Mathf.Abs(m_x - LaneX(m_totemLane)) < LaneWidth * 0.6f)
		{
			Hit(new Thing { Label = "", CrashText = "Yagluth's beam burned you! (Hold a totem over him as it lands.)" },
				"Yagluth's beam burned you! (Hold a totem over him as it lands.)");
		}
	}

	private void TotemGui(float k)
	{
		if (!TotemBoss || m_state != State.Running)
		{
			return;
		}
		Sprite? icon = IconOf("GoblinTotem");
		// How many are left, by the lives.
		for (int i = 0; i < m_totems; i++)
		{
			DrawSprite(new Rect(24f * k + i * 26f * k, 152f * k, 24f * k, 24f * k), icon, 0.85f * s_guiAlpha);
		}
		if (m_totemBeamTime > 0f)
		{
			// The beam, flashing back along the line it went.
			float a = m_totemBeamTime / 0.5f;
			Vector2 d = m_totemBeamTo - m_totemBeamFrom;
			int steps = Mathf.Clamp(Mathf.CeilToInt(d.magnitude / (8f * k)), 2, 200);
			for (int i = 0; i <= steps; i++)
			{
				Vector2 p = m_totemBeamFrom + d * (i / (float)steps);
				Fill(new Rect(p.x - 5f * k, p.y - 5f * k, 10f * k, 10f * k), new Color(0.55f, 1f, 0.6f, a * s_guiAlpha));
			}
		}
		if (m_totemCharge < 0f)
		{
			return;
		}
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = false;
		Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
		bool on = BossOnScreen(out Vector2 boss, out float radius) && Vector2.Distance(mouse, boss) <= radius;
		// The beam gathers on him: a ring closing in, green when your totem is over him.
		float u = Mathf.Clamp01(m_totemCharge / 2f);
		float r = radius * (1f + u * 1.5f);
		Color old = GUI.color;
		GUI.color = on ? new Color(0.5f, 1f, 0.55f, 0.9f * s_guiAlpha) : new Color(1f, 0.45f, 0.2f, 0.7f * s_guiAlpha);
		GUI.DrawTexture(new Rect(boss.x - r, boss.y - r, r * 2f, r * 2f), CueRing());
		GUI.DrawTexture(new Rect(boss.x - radius, boss.y - radius, radius * 2f, radius * 2f), CueRing());
		GUI.color = old;
		float s = 64f * k * (on ? 1.15f : 1f);
		DrawSprite(new Rect(mouse.x - s * 0.5f, mouse.y - s * 0.5f, s, s), icon, s_guiAlpha);
	}
}
