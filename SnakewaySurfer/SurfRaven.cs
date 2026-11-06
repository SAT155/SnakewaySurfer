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
	private GameObject? m_raven;
	private Animator? m_ravenAnim;
	private string m_ravenText = "";
	private float m_ravenTime;
	// Seconds since he started to leave (negative while he's staying).
	private float m_ravenLeave = -1f;
	// Seconds since he arrived (he talks once he's landed).
	private float m_ravenAge;
	private bool m_ravenTalked;
	// The game's popup draws through the main camera; with our own camera, our own text instead.
	private bool m_ravenNative;
	private float m_prevBannerTime;
	private string m_prevBanner = "";
	private const float RavenDepth = 5f;
	private const float RavenScale = 1.3f;

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
		if (m_raven == null)
		{
			return;
		}
		m_ravenAge += dt;
		if (m_ravenLeave < 0f)
		{
			if (!m_ravenTalked && m_ravenAge > 0.7f)
			{
				m_ravenTalked = true;
				if (m_ravenAnim != null)
				{
					SetTrigger(m_ravenAnim, "talk");
				}
			}
			m_ravenTime -= dt;
			if (m_ravenTime <= 0f || m_state != State.Running)
			{
				RavenLeave();
			}
		}
		else
		{
			m_ravenLeave += dt;
			if (m_ravenLeave > 2f)
			{
				RemoveRaven();
			}
		}
	}

	private void RavenSay(string text, float seconds)
	{
		if (m_raven != null && m_ravenLeave >= 0f)
		{
			RemoveRaven();
		}
		if (m_raven == null)
		{
			GameObject? template = RavenTemplate();
			if (template == null)
			{
				return;
			}
			m_raven = Instantiate(template, transform, false);
			m_raven.name = "SurfHugin";
			m_raven.SetActive(true);
			m_raven.transform.localScale = Vector3.one * RavenScale;
			m_ravenAnim = m_raven.GetComponentInChildren<Animator>();
			m_ravenAge = 0f;
			m_ravenTalked = false;
			m_ravenLeave = -1f;
			PlaceRaven();
			// In as he comes to you in the world (a puff of feathers if this version has no
			// fly-in).
			if (m_ravenAnim != null && SetTrigger(m_ravenAnim, "flyin") == null)
			{
				SetTrigger(m_ravenAnim, "teleportin");
			}
			m_audio.Play("sfx_raven_flap|sfx_raven_teleport", 0.7f);
			m_audio.Play("sfx_raven_kaw", 0.6f, Random.Range(0.95f, 1.08f));
		}
		else
		{
			m_ravenTalked = false;
			m_ravenAge = Mathf.Min(m_ravenAge, 0.6f);
		}
		m_ravenText = text;
		m_ravenTime = seconds;
		m_ravenNative = m_usingGameCamera && Chat.instance != null;
		if (m_ravenNative)
		{
			Chat.instance!.SetNpcText(m_raven, Vector3.up * (0.75f * RavenScale), 30f, seconds + 0.5f, "", text, true);
		}
	}

	private void RavenLeave()
	{
		if (m_raven == null || m_ravenLeave >= 0f)
		{
			return;
		}
		m_ravenLeave = 0f;
		if (Chat.instance != null)
		{
			Chat.instance.ClearNpcText(m_raven);
		}
		if (m_ravenAnim != null && SetTrigger(m_ravenAnim, "flyaway") == null)
		{
			SetTrigger(m_ravenAnim, "poff");
		}
		m_audio.Play("sfx_raven_flap|sfx_raven_poof", 0.6f);
	}

	private void RemoveRaven()
	{
		if (m_raven != null)
		{
			if (Chat.instance != null)
			{
				Chat.instance.ClearNpcText(m_raven);
			}
			Destroy(m_raven);
		}
		m_raven = null;
		m_ravenAnim = null;
		m_ravenLeave = -1f;
	}

	// Perched at the top right of the view, turned a little towards you (after the camera moves).
	private void PlaceRaven()
	{
		if (m_raven == null)
		{
			return;
		}
		float half = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) * RavenDepth;
		float aspect = m_camera != null && m_camera.aspect > 0.1f ? m_camera.aspect : 16f / 9f;
		Vector3 local = new Vector3(0.74f * half * aspect, 0.3f * half, RavenDepth);
		Vector3 at = m_camFinal + m_camFinalRot * local;
		m_raven.transform.position = at;
		Vector3 back = m_camFinal - at;
		back.y = 0f;
		if (back.sqrMagnitude > 0.01f)
		{
			m_raven.transform.rotation = Quaternion.LookRotation(back) * Quaternion.Euler(0f, 25f, 0f);
		}
	}

	// With our own camera the game's popup can't find him: the words beside him instead.
	private void RavenGui(float k, float w, float h)
	{
		if (m_raven == null || m_ravenNative || m_ravenLeave >= 0f || m_ravenText.Length == 0)
		{
			return;
		}
		GUIStyle style = m_mid!;
		TextAnchor anchor = style.alignment;
		style.alignment = TextAnchor.UpperRight;
		Shadowed(new Rect(w * 0.45f, h * 0.06f, w * 0.52f, 200 * k), m_ravenText, style);
		style.alignment = anchor;
	}
}
