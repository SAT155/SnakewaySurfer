using UnityEngine;

namespace SnakewaySurfer;

// 1.16.102 (SAT: "Just a red highlight does not give any timing information ... partial fill to
// full fill or similar"): a cue for the next blow that can be parried.
// 1.16.150 (SAT: the ring over your head distracted more than it helped): the red lane tiles turn
// gold while a parry would work.
// 1.16.x (SAT: parrying feels janky, so players spam block, and some Elder swings are hard to
// read): a clear tell and a fair window.
//  - The tell: the moment a parry would work, a gold glint flashes on the attacker with a high
//    clink, and the tiles turn gold. Press block on the glint.
//  - The window is at least ParryFloor seconds, so reacting to the glint is in time.
//  - Spamming doesn't work: a press within SpamGap of the last one that didn't parry opens no
//    window (it still blocks). One clean press per blow.
internal sealed partial class SurfGame
{
	private const float ParryFloor = 0.4f;
	private const float SpamGap = 0.55f;

	private static Texture2D? s_glint;
	private float m_cueFlash;
	private bool m_tellOpen;
	private float m_glintAge = -1f;
	private GameObject? m_glintFrom;
	private Renderer[] m_glintBody = new Renderer[0];
	private float m_prevPress = -99f;
	private float m_lastParry = -99f;

	// A press of block: opens a parry window unless it follows a missed press too closely.
	private void OnBlockPressed()
	{
		bool spam = m_runTime - m_prevPress < SpamGap && m_lastParry < m_prevPress;
		m_prevPress = m_runTime;
		m_blockPressed = spam ? -99f : m_runTime;
	}

	private void NoteParry() => m_lastParry = m_runTime;

	// A thin ring (the area-fire marker and the totem cue use it).
	private static Texture2D? s_cueRing;

	private static Texture2D CueRing()
	{
		if (s_cueRing != null)
		{
			return s_cueRing;
		}
		const int n = 128;
		s_cueRing = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
		Color[] px = new Color[n * n];
		for (int y = 0; y < n; y++)
		{
			for (int x = 0; x < n; x++)
			{
				float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
				float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.88f) / 0.07f);
				px[y * n + x] = new Color(1f, 1f, 1f, a * a);
			}
		}
		s_cueRing.SetPixels(px);
		s_cueRing.Apply();
		return s_cueRing;
	}

	// A four-pointed star, soft in the middle.
	private static Texture2D Glint()
	{
		if (s_glint != null)
		{
			return s_glint;
		}
		const int n = 128;
		s_glint = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
		Color[] px = new Color[n * n];
		for (int y = 0; y < n; y++)
		{
			for (int x = 0; x < n; x++)
			{
				float dx = Mathf.Abs((x + 0.5f) / (n * 0.5f) - 1f);
				float dy = Mathf.Abs((y + 0.5f) / (n * 0.5f) - 1f);
				float core = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / 0.3f);
				float rays = Mathf.Clamp01(1f - dx / 0.06f * (0.3f + dy)) * (1f - dy) + Mathf.Clamp01(1f - dy / 0.06f * (0.3f + dx)) * (1f - dx);
				float a = Mathf.Clamp01(core * core + rays);
				px[y * n + x] = new Color(1f, 1f, 1f, a);
			}
		}
		s_glint.SetPixels(px);
		s_glint.Apply();
		return s_glint;
	}

	// Seconds until the next parryable blow reaches you, or -1; and who throws it.
	private float NextBlowIn() => NextBlowIn(out _);

	private float NextBlowIn(out GameObject? from)
	{
		from = null;
		// Every fight, not only bosses: any creature's blow in your lane gets the tell.
		if (m_state != State.Running)
		{
			return -1f;
		}
		if (Dueling && m_duelLand >= 0f)
		{
			from = m_bossGo;
			return Mathf.Abs(m_x - LaneX(m_duelLane)) < LaneWidth * 0.6f ? m_duelLand : -1f;
		}
		float best = -1f;
		foreach (Thing t in m_things)
		{
			if (!t.Collidable || t.Label.Length == 0 || t.JumpOrSlide || t.Kind == Kind.Scenery || t.Kind == Kind.Ground
				|| t.Kind == Kind.Coin || t.Kind == Kind.Item || t.Kind == Kind.BossHit || t.Kind == Kind.Slow)
			{
				continue;
			}
			if (Mathf.Abs(LaneX(t.Lane) - m_x) > LaneWidth * 0.6f)
			{
				continue;
			}
			float gap = t.Z - t.HalfLength - m_z;
			if (gap < 0f)
			{
				continue;
			}
			float secs = gap / Mathf.Max(1f, m_speed + Mathf.Max(0f, t.Speed));
			if (secs < 1.6f && (best < 0f || secs < best))
			{
				best = secs;
				from = t.Go;
			}
		}
		return best;
	}

	private static readonly Color s_markerRed = new Color(1f, 0.25f, 0.05f, 0.6f);
	private static readonly Color s_markerParry = new Color(1f, 0.95f, 0.3f, 0.75f);

	private void UpdateTileCue(float dt)
	{
		float left = NextBlowIn(out GameObject? from);
		float window = Mathf.Max(0.05f, ParryWindowNow);
		bool open = left >= 0f && left <= window;
		if (m_markerMaterial != null)
		{
			m_markerMaterial.color = open ? s_markerParry : s_markerRed;
		}
		if (open && !m_tellOpen)
		{
			// The tell: a glint on the attacker and a high clink, once per blow.
			m_glintAge = 0f;
			m_glintFrom = from;
			m_glintBody = from != null ? from.GetComponentsInChildren<Renderer>() : new Renderer[0];
			m_audio.Play("sfx_metal_blocked|sfx_metal_shield_blocked|sfx_gui_button", 0.6f, 1.9f);
		}
		m_tellOpen = open;
		if (m_glintAge >= 0f)
		{
			m_glintAge += dt;
			if (!open && m_glintAge > 0.35f)
			{
				m_glintAge = -1f;
				m_glintFrom = null;
			}
		}
	}

	private void ClearTileCue()
	{
		m_tellOpen = false;
		m_glintAge = -1f;
		m_glintFrom = null;
		m_glintBody = new Renderer[0];
		m_prevPress = -99f;
		m_lastParry = -99f;
		if (m_markerMaterial != null)
		{
			m_markerMaterial.color = s_markerRed;
		}
	}

	// Where the glint sits: the upper part of the attacker, a little to your side of it.
	private bool GlintPoint(out Vector3 world)
	{
		world = Vector3.zero;
		if (m_glintFrom == null)
		{
			return false;
		}
		Bounds b = default;
		bool any = false;
		foreach (Renderer r in m_glintBody)
		{
			if (r == null || !r.enabled || r is ParticleSystemRenderer)
			{
				continue;
			}
			if (any)
			{
				b.Encapsulate(r.bounds);
			}
			else
			{
				b = r.bounds;
				any = true;
			}
		}
		world = any ? b.center + m_stage.up * b.extents.y * 0.45f : m_glintFrom.transform.position + m_stage.up * 1.4f;
		if (m_camera != null)
		{
			world += (m_camera.transform.position - world).normalized * (any ? Mathf.Min(b.extents.magnitude, 3f) * 0.5f : 0.5f);
		}
		return true;
	}

	private void ParryCueGui(float k)
	{
		if (m_glintAge < 0f || m_camera == null || !GlintPoint(out Vector3 world))
		{
			m_cueFlash = 0f;
			return;
		}
		Vector3 sp = m_camera.WorldToScreenPoint(world);
		if (sp.z <= 0f)
		{
			return;
		}
		Vector2 c = new Vector2(sp.x, Screen.height - sp.y);
		// A sharp pop, then a smaller steady twinkle while the window is open.
		float pop = m_glintAge < 0.18f ? Mathf.Sin(m_glintAge / 0.18f * Mathf.PI) : 0f;
		m_cueFlash += Time.deltaTime * 14f;
		float twinkle = 0.75f + 0.25f * Mathf.Sin(m_cueFlash);
		float fade = m_tellOpen ? 1f : Mathf.Clamp01(1f - (m_glintAge - 0.2f) / 0.15f);
		float r = (34f + 70f * pop) * twinkle * k;
		Color old = GUI.color;
		Matrix4x4 m = GUI.matrix;
		GUIUtility.RotateAroundPivot(m_glintAge * 90f, c);
		GUI.color = new Color(1f, 0.85f, 0.3f, 0.9f * fade * s_guiAlpha);
		GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), Glint());
		GUI.matrix = m;
		GUI.color = new Color(1f, 1f, 0.9f, fade * s_guiAlpha);
		GUI.DrawTexture(new Rect(c.x - r * 0.55f, c.y - r * 0.55f, r * 1.1f, r * 1.1f), Glint());
		GUI.color = old;
	}
}
