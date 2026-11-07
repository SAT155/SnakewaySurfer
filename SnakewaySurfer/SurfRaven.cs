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
	private const float RavenDepth = 9f;
	private const float RavenScale = 1.3f;
	// 1.16.18 (Sam saw only a foot, huge, at the top right): he is sized and placed by his
	// actual shape, its middle kept at the spot, measured again once he has landed.
	private Vector3 m_ravenCentre;
	private Vector3 m_ravenCentreGoal;
	private float m_ravenHeight = 1f;
	private float m_ravenSize = RavenScale;
	// His height on screen, as a share of half the view's height.
	private const float RavenScreenHeight = 0.34f;

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
				MeasureRaven(false);
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
			m_ravenSize = RavenScale;
			m_ravenAnim = m_raven.GetComponentInChildren<Animator>();
			MeasureRaven(true);
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
		// Long enough to read: at least five seconds, more for a longer line.
		seconds = Mathf.Max(seconds, 5f, 2.5f + text.Length * 0.07f);
		m_ravenText = text;
		m_ravenTime = seconds;
		m_ravenNative = m_usingGameCamera && Chat.instance != null;
		if (m_ravenNative)
		{
			Vector3 above = m_raven.transform.rotation * (m_ravenCentre * m_ravenSize) + Vector3.up * (m_ravenHeight * m_ravenSize * 0.65f);
			Chat.instance!.SetNpcText(m_raven, above, 30f, seconds + 0.5f, "", text, true);
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
		Vector3 local = new Vector3(0.72f * half * aspect, 0.5f * half, RavenDepth);
		Vector3 at = m_camFinal + m_camFinalRot * local;
		Vector3 back = m_camFinal - at;
		back.y = 0f;
		if (back.sqrMagnitude > 0.01f)
		{
			m_raven.transform.rotation = Quaternion.LookRotation(back) * Quaternion.Euler(0f, 25f, 0f);
		}
		// His middle at the spot, wherever his model keeps it (settling gently once he's landed).
		m_ravenCentre = Vector3.Lerp(m_ravenCentre, m_ravenCentreGoal, 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime));
		m_raven.transform.position = at - m_raven.transform.rotation * (m_ravenCentre * m_ravenSize);
	}

	// His shape as it stands (unscaled, in his own frame): sized to fit the corner when he
	// arrives, and his middle settled again once he has landed.
	private void MeasureRaven(bool arriving)
	{
		if (m_raven == null || !Pictures.MeshBounds(m_raven, out Bounds b) || b.size.y < 0.01f)
		{
			return;
		}
		Transform t = m_raven.transform;
		float scale = Mathf.Max(0.001f, t.lossyScale.y);
		m_ravenCentreGoal = Quaternion.Inverse(t.rotation) * (b.center - t.position) / scale;
		m_ravenHeight = b.size.y / scale;
		if (arriving)
		{
			m_ravenCentre = m_ravenCentreGoal;
			float half = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) * RavenDepth;
			// Never bigger than a raven set back in the corner (a measure taken mid-flap could say
			// he is tiny and blow him up to fill the screen).
			float most = RavenScale * RavenDepth / 5f * 0.8f;
			m_ravenSize = Mathf.Clamp(RavenScreenHeight * half / m_ravenHeight, most * 0.4f, most);
			t.localScale = Vector3.one * m_ravenSize;
		}
		Plugin.Log.LogInfo($"Hugin: height {m_ravenHeight:0.00}, middle {m_ravenCentreGoal}, scale {m_ravenSize:0.00}");
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
