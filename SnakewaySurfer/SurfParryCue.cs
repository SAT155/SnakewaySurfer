using UnityEngine;

namespace SnakewaySurfer;

// 1.16.102 (Sam: "Just a red highlight does not give any timing information ... partial fill to
// full fill or similar"): in a boss fight, a ring over your head for the next blow that can be
// parried. A faint outer ring closes in on a fixed inner ring and arrives exactly when the
// parry window opens. Then the inner ring flashes gold until the blow lands. No text.
internal sealed partial class SurfGame
{
	private static Texture2D? s_cueRing;
	private float m_cueFlash;

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

	// Seconds until the next parryable blow reaches you, or -1.
	private float NextBlowIn()
	{
		if (m_state != State.Running || m_boss == null)
		{
			return -1f;
		}
		if (Dueling && m_duelLand >= 0f)
		{
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
			}
		}
		return best;
	}

	private void ParryCueGui(float k)
	{
		float left = NextBlowIn();
		if (left < 0f || m_avatar == null || m_camera == null)
		{
			m_cueFlash = 0f;
			return;
		}
		Vector3 sp = m_camera.WorldToScreenPoint(m_avatar.transform.position + m_stage.up * 2.4f);
		if (sp.z <= 0f)
		{
			return;
		}
		Vector2 c = new Vector2(sp.x, Screen.height - sp.y);
		float window = Mathf.Max(0.05f, ParryWindowNow);
		float inner = 46f * k;
		Color old = GUI.color;
		if (left > window)
		{
			// Closing in: from three times the size, faint, to the inner ring as the window opens.
			float u = Mathf.Clamp01((left - window) / 1.2f);
			float outer = inner * (1f + 2f * u);
			GUI.color = new Color(1f, 0.85f, 0.4f, (1f - u) * 0.85f * s_guiAlpha);
			GUI.DrawTexture(new Rect(c.x - outer, c.y - outer, outer * 2f, outer * 2f), CueRing());
			GUI.color = new Color(1f, 1f, 1f, 0.35f * s_guiAlpha);
			GUI.DrawTexture(new Rect(c.x - inner, c.y - inner, inner * 2f, inner * 2f), CueRing());
		}
		else
		{
			// Now: the ring flashes bright gold and swells a little.
			m_cueFlash += Time.deltaTime * 18f;
			float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(m_cueFlash));
			float r = inner * (1.05f + 0.1f * pulse);
			GUI.color = new Color(1f, 0.9f, 0.3f, pulse * s_guiAlpha);
			GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), CueRing());
			GUI.DrawTexture(new Rect(c.x - r * 0.8f, c.y - r * 0.8f, r * 1.6f, r * 1.6f), CueRing());
		}
		GUI.color = old;
	}
}
