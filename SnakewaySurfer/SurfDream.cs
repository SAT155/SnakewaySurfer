using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SnakewaySurfer;

// 1.16.16 (Sam): runs load behind the game's own portal loading screen (the black screen with the
// swirling portal you see when you teleport), and the very first run starts with a dream instead
// of the start menu: lines of vague dream text fading in and out on the black, in the game's own
// dream-text look (the one you see when you sleep), before the run fades in.
internal sealed partial class SurfGame
{
	// Seconds into the load (negative: none). The screen covers the game by m_loadAlpha.
	private float m_loadTime = -1f;
	private float m_loadAlpha;
	private bool m_loadRan;
	private int m_loadFrames;
	private float m_loadHeld;
	private System.Action? m_loadAction;
	private bool m_loadShown;
	private bool m_loadChecked;
	private bool m_loadNative;
	private const float LoadFadeIn = 0.4f;
	private const float LoadHold = 1.2f;
	private const float LoadFadeOut = 0.8f;

	private string[]? m_dreamLines;
	private int m_dreamLine;
	private float m_dreamTime;
	// Skipping: the line on screen fades out from here, then the dream ends.
	private float m_dreamSkip = -1f;
	private TMP_Text? m_dreamText;
	// 1.16.18: the dream's own dark, on its own canvas over everything (the loading screen's
	// own black flickered and let the world show through).
	private GameObject? m_dreamCanvas;
	private GUIStyle? m_dreamStyle;
	private const float DreamIn = 1.5f;
	private const float DreamStay = 3.2f;
	private const float DreamOut = 1.5f;
	private const float DreamGap = 0.5f;
	// 1.16.69 (Sam): the portal's swirl first, then the dream (seconds of swirl left).
	private float m_dreamPortal;
	private const float DreamPortalTime = 2.2f;

	// Vague, like the game's own dreams.
	private static readonly string[] s_firstDream =
	{
		"You dream of a road that runs on without end, and you upon it.",
		"The trees rush past like water. Something follows, close behind.",
		"A voice that is not your own whispers: run, and do not stop.",
	};

	private bool Loading => m_loadTime >= 0f;

	// Black, then `action` once nothing shows, then the run fades in.
	private void BeginLoad(System.Action action)
	{
		if (!Plugin.LoadingScreen.Value)
		{
			action();
			return;
		}
		m_loadTime = 0f;
		m_loadRan = false;
		m_loadFrames = 0;
		m_loadHeld = 0f;
		m_loadAction = action;
		m_audio.Play("sfx_OpenPortal|sfx_raven_teleport", 0.45f);
	}

	// Already covered (starting up: the run is set up behind it).
	private void CoverNow()
	{
		if (!Plugin.LoadingScreen.Value && m_dreamLines == null)
		{
			return;
		}
		m_loadTime = LoadFadeIn;
		m_loadAlpha = 1f;
		m_loadRan = true;
		m_loadHeld = 0f;
		m_loadAction = null;
	}

	// Each frame: true while the game waits behind the screen (it runs on as the screen fades).
	private bool UpdateLoad(bool canInput)
	{
		if (m_loadTime < 0f)
		{
			return false;
		}
		float udt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
		m_loadTime += udt;
		if (!m_loadRan)
		{
			m_loadAlpha = Mathf.Clamp01(m_loadTime / LoadFadeIn);
			// One frame fully covered before the work that may stall.
			if (m_loadAlpha < 1f || m_loadFrames++ < 1)
			{
				return true;
			}
			m_loadRan = true;
			System.Action? action = m_loadAction;
			m_loadAction = null;
			action?.Invoke();
			return true;
		}
		if (m_dreamLines != null)
		{
			UpdateDream(udt, canInput);
			return true;
		}
		m_loadHeld += udt;
		if (m_loadHeld < LoadHold)
		{
			return true;
		}
		m_loadAlpha -= udt / LoadFadeOut;
		if (m_loadAlpha <= 0f)
		{
			m_loadAlpha = 0f;
			m_loadTime = -1f;
		}
		return false;
	}

	// The game's own loading screen lives on its HUD canvas, outside the HUD we hide.
	private bool LoadNative()
	{
		if (!m_loadChecked && Hud.instance != null)
		{
			m_loadChecked = true;
			Hud hud = Hud.instance;
			m_loadNative = hud.m_loadingScreen != null && hud.m_teleportingProgress != null
				&& (m_hudRoot == null || !hud.m_loadingScreen.transform.IsChildOf(m_hudRoot.transform));
			if (!m_loadNative)
			{
				Plugin.Log.LogInfo("No game loading screen to use: a plain fade instead.");
			}
		}
		return m_loadNative;
	}

	// After the game's HUD has had its say this frame (it fades the screen out when you're not
	// teleporting).
	private void ApplyLoadScreen()
	{
		Hud? hud = Hud.instance;
		if (hud == null || !LoadNative())
		{
			return;
		}
		if (!Loading)
		{
			if (m_loadShown)
			{
				EndLoadScreen();
			}
			return;
		}
		m_loadShown = true;
		hud.m_loadingScreen.gameObject.SetActive(true);
		hud.m_loadingScreen.alpha = m_loadAlpha;
		SetActive(hud.m_loadingProgress, false);
		SetActive(hud.m_sleepingProgress, false);
		// Dreaming: only the dark.
		SetActive(hud.m_teleportingProgress, m_dreamLines == null || m_dreamPortal > 0f);
	}

	private static void SetActive(GameObject? go, bool on)
	{
		if (go != null && go.activeSelf != on)
		{
			go.SetActive(on);
		}
	}

	private void EndLoadScreen()
	{
		RemoveDreamText();
		if (!m_loadShown)
		{
			return;
		}
		m_loadShown = false;
		Hud? hud = Hud.instance;
		if (hud != null && hud.m_loadingScreen != null)
		{
			hud.m_loadingScreen.alpha = 0f;
			SetActive(hud.m_teleportingProgress, false);
			hud.m_loadingScreen.gameObject.SetActive(false);
		}
	}

	// ---------------------------------------------------------------- the first run's dream

	private bool FirstRun()
	{
		if (m_player == null || m_player.m_customData.ContainsKey(Progress.FirstRunKey))
		{
			return false;
		}
		m_player.m_customData[Progress.FirstRunKey] = "1";
		return true;
	}

	private void StartDream()
	{
		if (!Plugin.DreamIntro.Value)
		{
			return;
		}
		m_dreamLines = s_firstDream;
		m_dreamLine = 0;
		// A moment of dark first.
		m_dreamTime = -1f;
		m_dreamSkip = -1f;
		// Through the portal first (its swirl), then the dark of the dream.
		if (Plugin.LoadingScreen.Value && LoadNative())
		{
			m_dreamPortal = DreamPortalTime;
			m_audio.Play("sfx_OpenPortal|sfx_raven_teleport", 0.45f);
		}
		else
		{
			m_dreamPortal = 0f;
			MakeDreamText();
		}
	}

	private float DreamAlpha()
	{
		float t = m_dreamTime;
		float a = t < 0f ? 0f : t < DreamIn ? t / DreamIn : t < DreamIn + DreamStay ? 1f : 1f - (t - DreamIn - DreamStay) / DreamOut;
		a = Mathf.Clamp01(a);
		if (m_dreamSkip >= 0f)
		{
			a = Mathf.Min(a, m_dreamSkip);
		}
		return a;
	}

	private void UpdateDream(float udt, bool canInput)
	{
		if (m_dreamPortal > 0f)
		{
			if (canInput && KeyDown(KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter))
			{
				// Straight into the run.
				m_dreamPortal = 0f;
				EndDream();
				return;
			}
			m_dreamPortal -= udt;
			if (m_dreamPortal <= 0f)
			{
				m_dreamPortal = 0f;
				MakeDreamText();
			}
			return;
		}
		m_dreamTime += udt;
		if (m_dreamSkip < 0f && canInput && (m_dreamLine > 0 || m_dreamTime > 0.5f)
			&& KeyDown(KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter))
		{
			// Waking early.
			m_dreamSkip = DreamAlpha();
		}
		if (m_dreamSkip >= 0f)
		{
			m_dreamSkip -= udt * 1.5f;
			if (m_dreamSkip <= 0f)
			{
				EndDream();
				return;
			}
		}
		else if (m_dreamTime >= DreamIn + DreamStay + DreamOut + DreamGap)
		{
			m_dreamLine++;
			m_dreamTime = 0f;
			if (m_dreamLine >= m_dreamLines!.Length)
			{
				EndDream();
				return;
			}
		}
		if (m_dreamText != null)
		{
			m_dreamText.text = m_dreamLines![m_dreamLine];
			m_dreamText.alpha = DreamAlpha();
		}
	}

	private void EndDream()
	{
		m_dreamLines = null;
		m_dreamPortal = 0f;
		RemoveDreamText();
		// A short look at the portal, then into the run.
		m_loadHeld = LoadHold * 0.5f;
	}

	// Black, with a copy of the game's dream text (from its sleep screen) on it.
	private void MakeDreamText()
	{
		if (m_dreamCanvas != null)
		{
			return;
		}
		Hud? hud = Hud.instance;
		try
		{
			m_dreamCanvas = new GameObject("SurfDream");
			Canvas canvas = m_dreamCanvas.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 30000;
			CanvasScaler scaler = m_dreamCanvas.AddComponent<CanvasScaler>();
			CanvasScaler? hudScaler = hud != null && hud.m_loadingScreen != null ? hud.m_loadingScreen.GetComponentInParent<CanvasScaler>() : null;
			if (hudScaler != null)
			{
				scaler.uiScaleMode = hudScaler.uiScaleMode;
				scaler.referenceResolution = hudScaler.referenceResolution;
				scaler.screenMatchMode = hudScaler.screenMatchMode;
				scaler.matchWidthOrHeight = hudScaler.matchWidthOrHeight;
				scaler.scaleFactor = hudScaler.scaleFactor;
			}
			else
			{
				scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
				scaler.referenceResolution = new Vector2(1920f, 1080f);
				scaler.matchWidthOrHeight = 0.5f;
			}
			GameObject dark = new GameObject("Dark", typeof(RectTransform));
			dark.transform.SetParent(m_dreamCanvas.transform, false);
			Image image = dark.AddComponent<Image>();
			image.color = Color.black;
			image.raycastTarget = false;
			Stretch((RectTransform)dark.transform, Vector2.zero, Vector2.one);

			SleepText? sleep = hud == null ? null
				: (hud.m_sleepingProgress != null ? hud.m_sleepingProgress.GetComponentInChildren<SleepText>(true) : null) ?? hud.GetComponentInChildren<SleepText>(true);
			if (sleep == null || sleep.m_dreamField == null)
			{
				return;
			}
			GameObject go = Instantiate(sleep.m_dreamField.gameObject, m_dreamCanvas.transform, false);
			go.name = "SurfDreamText";
			foreach (SleepText extra in go.GetComponentsInChildren<SleepText>(true))
			{
				DestroyImmediate(extra);
			}
			Stretch((RectTransform)go.transform, new Vector2(0.12f, 0.38f), new Vector2(0.88f, 0.62f));
			go.SetActive(true);
			m_dreamText = go.GetComponent<TMP_Text>();
			m_dreamText.enabled = true;
			m_dreamText.canvasRenderer.SetAlpha(1f);
			m_dreamText.text = "";
			m_dreamText.alpha = 0f;
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Couldn't copy the game's dream text: {e.Message}");
			m_dreamText = null;
		}
	}

	private static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
	{
		rt.anchorMin = min;
		rt.anchorMax = max;
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
		rt.localScale = Vector3.one;
		rt.localRotation = Quaternion.identity;
	}

	private void RemoveDreamText()
	{
		if (m_dreamCanvas != null)
		{
			Destroy(m_dreamCanvas);
		}
		m_dreamCanvas = null;
		m_dreamText = null;
	}

	// ---------------------------------------------------------------- drawing

	private void OnGUI()
	{
		float cover = Loading ? m_loadAlpha : 0f;
		if (cover < 0.98f)
		{
			s_guiAlpha = 1f - cover;
			DrawGui();
			s_guiAlpha = 1f;
		}
		if (!Loading)
		{
			return;
		}
		if (!LoadNative() || (m_dreamLines != null && m_dreamCanvas == null && m_dreamPortal <= 0f))
		{
			Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, cover));
		}
		if (m_dreamLines != null && m_dreamPortal <= 0f && m_dreamText == null && Event.current.type == EventType.Repaint)
		{
			// No dream text of the game's to copy: soft grey italics, the same way.
			float k = Screen.height / 1080f;
			m_dreamStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic, wordWrap = true };
			m_dreamStyle.fontSize = Mathf.RoundToInt(36 * k);
			m_dreamStyle.normal.textColor = new Color(0.85f, 0.83f, 0.78f, DreamAlpha());
			GUI.Label(new Rect(Screen.width * 0.12f, Screen.height * 0.38f, Screen.width * 0.76f, Screen.height * 0.24f), m_dreamLines[m_dreamLine], m_dreamStyle);
		}
	}
}
