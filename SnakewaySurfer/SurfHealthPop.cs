using UnityEngine;

namespace SnakewaySurfer;

// 1.16.x (SAT): when a hit costs a life, the health bar fades in over your character, the lost
// segment blinks and drains away, then the bar fades out. The bar in the corner stays as it was.
internal sealed partial class SurfGame
{
	private const float PopIn = 0.15f;
	private const float PopBlink = 1.0f;
	private const float PopHold = 0.5f;
	private const float PopOut = 0.5f;

	private int m_popLives = -1;
	private int m_popLost = -1;
	private float m_popAge = -1f;

	private void HealthPopGui(float k)
	{
		if (m_popLives < 0 || m_lives > m_popLives || m_state != State.Running)
		{
			// A new run, a life back, or not running: nothing to show.
			m_popLives = m_lives;
			if (m_state != State.Running)
			{
				m_popAge = -1f;
			}
			return;
		}
		if (m_lives < m_popLives)
		{
			m_popLost = m_lives;
			m_popLives = m_lives;
			m_popAge = 0f;
		}
		if (m_popAge < 0f || m_avatar == null || m_camera == null)
		{
			return;
		}
		m_popAge += Time.deltaTime;
		float total = PopIn + PopBlink + PopHold + PopOut;
		if (m_popAge >= total)
		{
			m_popAge = -1f;
			return;
		}
		int max = Mathf.Max(Plugin.StartLives, m_lives + 1);
		if (max <= 1)
		{
			return;
		}
		Vector3 sp = m_camera.WorldToScreenPoint(m_avatar.transform.position + m_stage.up * 2.3f);
		if (sp.z <= 0f)
		{
			return;
		}
		float fade = m_popAge < PopIn ? m_popAge / PopIn
			: m_popAge > total - PopOut ? (total - m_popAge) / PopOut
			: 1f;
		float a = Mathf.Clamp01(fade) * s_guiAlpha;
		float w = 30f * k;
		float h = 8f * k;
		float gap = 4f * k;
		float width = max * w + (max - 1) * gap;
		float x0 = sp.x - width * 0.5f;
		float y0 = Screen.height - sp.y - h * 0.5f;
		// The lost segment: blinks white and red, draining as it does.
		float t = Mathf.Clamp01((m_popAge - PopIn) / PopBlink);
		bool lit = Mathf.Repeat(m_popAge * 8f, 1f) < 0.5f;
		Color old = GUI.color;
		for (int i = 0; i < max; i++)
		{
			Rect r = new Rect(x0 + i * (w + gap), y0, w, h);
			GUI.color = new Color(0f, 0f, 0f, 0.6f * a);
			GUI.DrawTexture(new Rect(r.x - 1.5f * k, r.y - 1.5f * k, r.width + 3f * k, r.height + 3f * k), Texture2D.whiteTexture);
			if (i < m_lives)
			{
				GUI.color = new Color(0.85f, 0.12f, 0.08f, 0.95f * a);
				GUI.DrawTexture(r, Texture2D.whiteTexture);
			}
			else if (i == m_popLost && t < 1f)
			{
				GUI.color = lit ? new Color(1f, 1f, 1f, 0.95f * a) : new Color(0.85f, 0.12f, 0.08f, 0.8f * a);
				GUI.DrawTexture(new Rect(r.x, r.y, r.width * (1f - t), r.height), Texture2D.whiteTexture);
			}
		}
		GUI.color = old;
	}
}
