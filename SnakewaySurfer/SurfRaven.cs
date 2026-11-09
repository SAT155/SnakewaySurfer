using UnityEngine;

namespace SnakewaySurfer;

// 1.16.15 (Sam): the run's messages come from Hugin, the tutorial raven. He flies in at the top
// right of the screen, out of the way, with the game's own animation, says it in the game's own
// speech popup (the one he uses in the world), and flies off the game's way once it's said. A
// new message while he's there just replaces what he's saying.
internal sealed partial class SurfGame
{
	private GameObject? m_ravenTemplate;
	private bool m_ravenLooked;
	private string m_ravenText = "";
	private float m_ravenTime;
	// Seconds since he started to leave (negative while he's staying).
	private float m_ravenLeave = -1f;
	// Seconds since he arrived (he talks once he's landed).
	private float m_ravenAge;
	private float m_prevBannerTime;
	private string m_prevBanner = "";

	private bool m_ravenOn;
	private float m_ravenTextAge;
	private Texture2D? m_ravenPortrait;

	private bool RavenTalks => Plugin.Raven.Value && !Plugin.PopupText.Value;

	// A lifeless copy of Hugin's look (his animator kept), found once.
	private GameObject? RavenTemplate()
	{
		if (m_ravenLooked)
		{
			return m_ravenTemplate;
		}
		m_ravenLooked = true;
		try
		{
			Raven? hugin = null;
			foreach (Raven r in Resources.FindObjectsOfTypeAll<Raven>())
			{
				if (r != null && !r.m_isMunin && r.m_visual != null)
				{
					hugin = r;
					break;
				}
			}
			if (hugin != null)
			{
				m_ravenTemplate = Pictures.MakePicture(hugin.m_visual, m_templates, m_layer);
				// Not the "!" he wears over a tutorial.
				string mark = hugin.m_exclamation != null ? hugin.m_exclamation.name : "";
				foreach (Transform child in m_ravenTemplate.GetComponentsInChildren<Transform>(true))
				{
					if (mark.Length > 0 && child.name == mark)
					{
						child.gameObject.SetActive(false);
					}
				}
			}
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Couldn't find Hugin: {e.Message}");
			m_ravenTemplate = null;
		}
		if (m_ravenTemplate == null)
		{
			Plugin.Log.LogInfo("No raven to carry the run's messages.");
		}
		return m_ravenTemplate;
	}

	// Each frame (running): a message just set goes to Hugin; he stays while it shows, then leaves.
	private void UpdateRaven(float dt)
	{
		bool fresh = m_bannerTime > 0f && m_banner.Length > 0 && (m_bannerTime > m_prevBannerTime + 0.05f || m_banner != m_prevBanner);
		m_prevBannerTime = m_bannerTime;
		m_prevBanner = m_banner;
		if (fresh && RavenTalks && m_state == State.Running)
		{
			RavenSay(m_banner, Mathf.Clamp(m_bannerTime, 2f, 8f) + 1.5f);
		}
		if (!m_ravenOn)
		{
			return;
		}
		m_ravenAge += dt;
		m_ravenTextAge += dt;
		if (m_ravenLeave < 0f)
		{
			m_ravenTime -= dt;
			if (m_ravenTime <= 0f || m_state != State.Running)
			{
				RavenLeave();
			}
		}
		else
		{
			m_ravenLeave += dt;
			if (m_ravenLeave > 0.5f)
			{
				RemoveRaven();
			}
		}
	}

	private void RavenSay(string text, float seconds)
	{
		m_ravenPortrait ??= RavenUi.Portrait(RavenTemplate());
		if (!m_ravenOn || m_ravenLeave >= 0f)
		{
			m_ravenOn = true;
			m_ravenAge = 0f;
			m_ravenLeave = -1f;
			m_audio.Play("sfx_raven_flap|sfx_raven_teleport", 0.6f);
			m_audio.Play("sfx_raven_kaw", 0.55f, Random.Range(0.95f, 1.08f));
		}
		// Long enough to read: at least five seconds, more for a longer line.
		seconds = Mathf.Max(seconds, 5f, 2.5f + text.Length * 0.07f);
		m_ravenText = text;
		m_ravenTextAge = 0f;
		m_ravenTime = seconds;
	}

	private void RavenLeave()
	{
		if (!m_ravenOn || m_ravenLeave >= 0f)
		{
			return;
		}
		m_ravenLeave = 0f;
		m_audio.Play("sfx_raven_flap|sfx_raven_poof", 0.5f);
	}

	private void RemoveRaven()
	{
		m_ravenOn = false;
		m_ravenLeave = -1f;
		m_ravenText = "";
	}

	// 1.16.102: he's flat UI now, so there's nothing to place after the camera moves.
	private void PlaceRaven()
	{
	}

	private void RavenGui(float k, float w, float h)
	{
		if (!m_ravenOn)
		{
			return;
		}
		RavenUi.Draw(m_ravenPortrait, m_ravenLeave >= 0f ? "" : m_ravenText, m_ravenTextAge, m_ravenAge, m_ravenLeave, 0, false, s_guiAlpha);
	}
}
