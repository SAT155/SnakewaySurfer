using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// 1.16.71 (Sam's idea for the way in): a strange portal turns up near you. Step in and you come
// out right where you were... but now Hugin sits at the top right of your view, as in the
// minigame, nudging you to go to sleep. The longer you stay up, the more often he's back and
// the less polite he gets. After a day awake, his brother Munin joins him and the two of them
// talk about you, a burst every few minutes for a very long time, in "episodes" (a podcast, fun
// facts, a suspiciously long break, becoming your best friends). Sleep, and you dream the run.
//
// All of it is local to this player, kept in the character's custom data.
internal sealed class SurfStory : MonoBehaviour
{
	public static SurfStory? Instance;

	private const string StageKey = "SnakewaySurfer.Story";
	private const string AwakeKey = "SnakewaySurfer.StoryAwake";
	private const string EpisodeKey = "SnakewaySurfer.StoryEpisode";
	private const string BurstKey = "SnakewaySurfer.StoryBurst";

	// 0: nothing yet (the portal is coming), 1: Hugin nags, 2: Munin too, 3: you've slept.
	public const int StageNone = 0;
	public const int StageHugin = 1;
	public const int StageMunin = 2;
	public const int StageSlept = 3;

	private SurfAudio m_audio = null!;
	private float m_playTime;
	private bool m_wasSleeping;
	private bool m_bedSaid;
	private float m_nextTalk = 20f;
	private readonly Queue<(bool munin, string text)> m_queue = new();
	private float m_lineLeft;
	private readonly Dictionary<string, int> m_shuffle = new();

	// The portal.
	private GameObject? m_portal;
	private Transform? m_portalCentre;
	private float m_portalTime = -1f;
	private float m_portalLooked;
	private const float PortalBlack = 2.6f;

	// The two ravens, drawn by a camera of their own over the view.
	private sealed class Bird
	{
		public GameObject Go = null!;
		public Animator? Anim;
		public bool Munin;
		public float Age;
		public float Leave = -1f;
		public bool Talked;
		public Vector3 Centre;
		public float Size = 1f;
		public float Height = 1f;
	}

	private Bird? m_hugin;
	private Bird? m_munin;
	private Transform? m_holder;
	private GameObject? m_huginTemplate;
	private GameObject? m_muninTemplate;
	private bool m_looked;
	private Camera? m_birdCam;
	private Light? m_birdLight;
	private const float Depth = 9f;
	// 1.16.83 (Sam: "He is too big still"): about half the size (was 0.34).
	private const float ScreenHeight = 0.16f;

	private void Awake()
	{
		Instance = this;
		m_audio = new SurfAudio(gameObject);
		GameObject holder = new GameObject("Templates");
		holder.SetActive(false);
		holder.transform.SetParent(transform, false);
		m_holder = holder.transform;
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	// ---------------------------------------------------------------- state

	private static int GetInt(Player p, string key, int fallback = 0) =>
		p.m_customData.TryGetValue(key, out string v) && int.TryParse(v, out int i) ? i : fallback;

	private static double GetDouble(Player p, string key) =>
		p.m_customData.TryGetValue(key, out string v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0.0;

	private static void Set(Player p, string key, object value) =>
		p.m_customData[key] = System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

	public static int Stage(Player p) => GetInt(p, StageKey);

	private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;

	private static double DayLength => EnvMan.instance != null && EnvMan.instance.m_dayLengthSec > 0 ? EnvMan.instance.m_dayLengthSec : 1800.0;

	// Days awake since you came through the portal (or last slept).
	private static double DaysAwake(Player p) => (Now - GetDouble(p, AwakeKey)) / DayLength;

	// ---------------------------------------------------------------- each frame

	private Player? m_checked;

	// 1.16.84 (Sam: Hugin came back after logging in although he'd slept and dreamt the run):
	// whoever has already been through a run is past the ravens' story.
	public static void MarkDreamed(Player p)
	{
		if (Stage(p) != StageSlept)
		{
			Set(p, StageKey, StageSlept);
			Set(p, AwakeKey, Now);
			Plugin.Log.LogInfo("Already dreamt the run: the ravens' story is done.");
		}
		if (Instance != null)
		{
			Instance.RemoveBirds();
			Instance.m_queue.Clear();
			Instance.RemovePortal();
		}
	}

	private void Update()
	{
		Player? p = Player.m_localPlayer;
		if (p != m_checked)
		{
			m_checked = p;
			if (p != null && Stage(p) != StageSlept && p.m_customData.ContainsKey(Progress.FirstRunKey))
			{
				MarkDreamed(p);
			}
		}
		bool quiet = p == null || SurfGame.Active != null || !Plugin.RavenStory.Value || p.IsDead();
		if (p != null && SurfGame.Active == null)
		{
			UpdateSleep(p);
		}
		if (quiet)
		{
			if (SurfGame.Active != null || p == null)
			{
				RemoveBirds();
				m_queue.Clear();
			}
			if (p == null)
			{
				RemovePortal();
				m_playTime = 0f;
			}
			UpdatePortalScreen(p);
			return;
		}
		float dt = Time.deltaTime;
		int stage = Stage(p!);
		if (stage == StageNone)
		{
			UpdatePortal(p!, dt);
		}
		else
		{
			RemovePortalIfIdle();
			if (stage == StageHugin || stage == StageMunin)
			{
				UpdateNagging(p!, stage, dt);
			}
		}
		UpdatePortalScreen(p);
		UpdateTalk(dt);
		UpdateBirds(dt);
	}

	private void LateUpdate()
	{
		PlaceBirds();
		ApplyPortalScreen();
	}

	// Lying down, sleeping, waking: when you wake, you've dreamt the run.
	private void UpdateSleep(Player p)
	{
		int stage = Stage(p);
		bool sleeping = p.IsSleeping();
		if (p.InBed() && !m_bedSaid && (stage == StageHugin || stage == StageMunin) && Plugin.RavenStory.Value)
		{
			m_bedSaid = true;
			m_queue.Clear();
			Say(Pick(stage == StageMunin ? StoryLines.BedTogether : StoryLines.Bed, "bed"));
		}
		if (!p.InBed() && !sleeping)
		{
			m_bedSaid = false;
		}
		if (m_wasSleeping && !sleeping)
		{
			OnWoke(p, stage);
		}
		m_wasSleeping = sleeping;
	}

	private void OnWoke(Player p, int stage)
	{
		RemoveBirds();
		m_queue.Clear();
		if (!Plugin.RavenStory.Value)
		{
			return;
		}
		bool first = stage == StageHugin || stage == StageMunin;
		if (first)
		{
			Set(p, StageKey, StageSlept);
		}
		Set(p, AwakeKey, Now);
		if (first || (stage == StageSlept && Plugin.DreamEverySleep.Value))
		{
			Plugin.Log.LogInfo(first ? "Slept at last: dreaming the run." : "Slept: dreaming the run again.");
			SurfGame.Begin(p, null, null, first ? SurfGame.FirstDream : Pick(StoryLines.LaterDreams, "dream"));
		}
	}

	// ---------------------------------------------------------------- the portal

	private void UpdatePortal(Player p, float dt)
	{
		if (m_portalTime >= 0f)
		{
			return;
		}
		m_playTime += dt;
		if (m_portal == null)
		{
			if (m_playTime >= Plugin.PortalMinutes.Value * 60f && Time.time >= m_portalLooked)
			{
				m_portalLooked = Time.time + 20f;
				TrySpawnPortal(p);
			}
			return;
		}
		Vector3 centre = m_portalCentre != null ? m_portalCentre.position : m_portal.transform.position + Vector3.up;
		Vector3 d = p.transform.position - centre;
		float flat = new Vector2(d.x, d.z).magnitude;
		if (flat > 200f)
		{
			// Wandered off: it turns up again near you later.
			RemovePortal();
			m_playTime = Mathf.Max(0f, Plugin.PortalMinutes.Value * 60f - 120f);
			return;
		}
		if (flat < 1.3f && Mathf.Abs(d.y) < 2.5f && !p.IsTeleporting() && !p.InBed())
		{
			EnterPortal(p);
		}
	}

	// The debug way: the usual spot if there is one, else 5 m in front of you.
	private bool SpawnPortalNow(Player p)
	{
		TrySpawnPortal(p);
		if (m_portal != null)
		{
			return true;
		}
		GameObject? prefab = Pictures.Prefab("portal_wood");
		if (prefab == null)
		{
			return false;
		}
		Vector3 fwd = p.transform.forward;
		fwd.y = 0f;
		fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
		Vector3 at = p.transform.position + fwd * 5f;
		if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float height))
		{
			at.y = height;
		}
		MakePortal(prefab, at, Quaternion.LookRotation(-fwd));
		return m_portal != null;
	}

	// 1.16.84: a built Strange Portal left on "the dream" (SurfPortalPiece.cs). It's the strange
	// portal: it starts the ravens' story, or once that's begun, through it and out again and
	// Hugin is soon at you to get some sleep. (Ravens off: straight into the run.)
	public void EnterBuiltPortal(Player p)
	{
		if (SurfGame.Active != null || m_portalTime >= 0f)
		{
			return;
		}
		if (!Plugin.RavenStory.Value)
		{
			SurfGame.Begin(p, null, null, null);
			return;
		}
		RemovePortal();
		EnterPortal(p);
		if (Stage(p) != StageNone)
		{
			m_nextTalk = Mathf.Min(m_nextTalk, 3f);
		}
	}

	private void TrySpawnPortal(Player p)
	{
		if (p.InInterior() || p.InWater() || p.IsTeleporting() || ZoneSystem.instance == null)
		{
			return;
		}
		GameObject? prefab = Pictures.Prefab("portal_wood");
		if (prefab == null)
		{
			Plugin.Log.LogWarning("No portal to borrow for the ravens' story.");
			return;
		}
		Vector3 fwd = p.transform.forward;
		fwd.y = 0f;
		fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
		float water = ZoneSystem.instance.m_waterLevel;
		foreach (float angle in new[] { 0f, 35f, -35f, 70f, -70f, 120f, -120f, 180f })
		{
			Vector3 dir = Quaternion.Euler(0f, angle, 0f) * fwd;
			Vector3 at = p.transform.position + dir * 16f;
			if (!ZoneSystem.instance.GetSolidHeight(at, out float height) || height < water + 0.6f || Mathf.Abs(height - p.transform.position.y) > 6f)
			{
				continue;
			}
			// Not inside something: nothing solid just above the spot.
			at.y = height;
			if (Physics.CheckSphere(at + Vector3.up * 1.6f, 0.9f, ~0, QueryTriggerInteraction.Ignore))
			{
				continue;
			}
			MakePortal(prefab, at, Quaternion.LookRotation(-dir));
			Plugin.Log.LogInfo($"A strange portal stands at {at}.");
			return;
		}
	}

	private void MakePortal(GameObject prefab, Vector3 at, Quaternion rotation)
	{
		GameObject copy = Instantiate(prefab, m_holder, false);
		copy.name = "SurfStrangePortal";
		TeleportWorld? tw = copy.GetComponent<TeleportWorld>();
		GameObject? swirl = tw != null && tw.m_target_found != null ? tw.m_target_found.gameObject : null;
		MeshRenderer? model = tw != null ? tw.m_model : null;
		m_portalCentre = tw != null && tw.m_proximityRoot != null ? tw.m_proximityRoot : null;
		Pictures.Strip(copy);
		copy.transform.SetParent(null, false);
		copy.transform.position = at;
		copy.transform.rotation = rotation;
		if (swirl != null)
		{
			swirl.SetActive(true);
			foreach (ParticleSystem ps in swirl.GetComponentsInChildren<ParticleSystem>(true))
			{
				ps.gameObject.SetActive(true);
				ParticleSystem.MainModule main = ps.main;
				main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.95f, 0.35f), new Color(0.6f, 0.2f, 0.85f));
				ps.Play(true);
			}
			foreach (Light l in swirl.GetComponentsInChildren<Light>(true))
			{
				l.enabled = true;
				l.color = new Color(0.55f, 0.9f, 0.4f);
			}
		}
		if (model != null)
		{
			// A sickly glow, not the friendly orange of a portal that knows where it goes.
			Material mat = model.material;
			mat.EnableKeyword("_EMISSION");
			mat.SetColor("_EmissionColor", new Color(0.35f, 0.9f, 0.25f) * 2.2f);
		}
		m_portal = copy;
		m_audio.Play("sfx_OpenPortal|sfx_portal_activate", 0.25f, 0.8f);
	}

	private void RemovePortal()
	{
		if (m_portal != null)
		{
			Destroy(m_portal);
		}
		m_portal = null;
		m_portalCentre = null;
	}

	private void RemovePortalIfIdle()
	{
		if (m_portal != null && m_portalTime < 0f)
		{
			RemovePortal();
		}
	}

	private void EnterPortal(Player p)
	{
		m_portalTime = 0f;
		m_audio.Play("sfx_OpenPortal|sfx_raven_teleport", 0.5f);
		Plugin.Log.LogInfo("Through the strange portal.");
	}

	// The black of the portal, and out where you went in.
	private void UpdatePortalScreen(Player? p)
	{
		if (m_portalTime < 0f)
		{
			return;
		}
		m_portalTime += Time.unscaledDeltaTime;
		if (m_portal != null && m_portalTime > 0.6f)
		{
			RemovePortal();
		}
		if (m_portalTime >= PortalBlack + 0.6f)
		{
			m_portalTime = -1f;
			EndPortalScreen();
			if (p != null && Stage(p) == StageNone)
			{
				Set(p, StageKey, StageHugin);
				Set(p, AwakeKey, Now);
				m_nextTalk = 2.5f;
				m_arrivalLine = 0;
			}
		}
	}

	private int m_arrivalLine = -1;
	private bool m_screenShown;

	private void ApplyPortalScreen()
	{
		Hud? hud = Hud.instance;
		if (m_portalTime < 0f || hud == null || hud.m_loadingScreen == null)
		{
			return;
		}
		float a = m_portalTime < 0.4f ? m_portalTime / 0.4f : m_portalTime < PortalBlack ? 1f : 1f - (m_portalTime - PortalBlack) / 0.6f;
		m_screenShown = true;
		hud.m_loadingScreen.gameObject.SetActive(true);
		hud.m_loadingScreen.alpha = Mathf.Clamp01(a);
		if (hud.m_teleportingProgress != null)
		{
			hud.m_teleportingProgress.SetActive(true);
		}
		if (hud.m_loadingProgress != null)
		{
			hud.m_loadingProgress.SetActive(false);
		}
		if (hud.m_sleepingProgress != null)
		{
			hud.m_sleepingProgress.SetActive(false);
		}
	}

	private void EndPortalScreen()
	{
		Hud? hud = Hud.instance;
		if (!m_screenShown || hud == null || hud.m_loadingScreen == null)
		{
			return;
		}
		m_screenShown = false;
		hud.m_loadingScreen.alpha = 0f;
		if (hud.m_teleportingProgress != null)
		{
			hud.m_teleportingProgress.SetActive(false);
		}
		hud.m_loadingScreen.gameObject.SetActive(false);
	}

	// ---------------------------------------------------------------- what they say, and when

	private void UpdateNagging(Player p, int stage, float dt)
	{
		if (m_queue.Count > 0 || m_lineLeft > 0f || p.InBed() || p.IsSleeping())
		{
			return;
		}
		m_nextTalk -= dt;
		if (m_nextTalk > 0f)
		{
			return;
		}
		float gap = Mathf.Max(0.5f, Plugin.RavenMinutes.Value) * 60f;
		if (m_arrivalLine >= 0 && m_arrivalLine < StoryLines.Arrival.Length)
		{
			Say(StoryLines.Arrival[m_arrivalLine++]);
			m_nextTalk = m_arrivalLine < StoryLines.Arrival.Length ? 6f : gap * Random.Range(0.8f, 1.2f);
			return;
		}
		double days = DaysAwake(p);
		if (stage == StageHugin)
		{
			if (days >= Plugin.MuninDays.Value)
			{
				Set(p, StageKey, StageMunin);
				Set(p, EpisodeKey, 0);
				Set(p, BurstKey, 0);
				Plugin.Log.LogInfo("Munin joins Hugin.");
				NextBurst(p, gap);
				return;
			}
			// Hugin alone: more often, and ruder, the longer you've been up.
			float share = (float)(days / Mathf.Max(0.05f, Plugin.MuninDays.Value));
			string[][] pool = share < 0.3f ? StoryLines.Polite : share < 0.65f ? StoryLines.Snide : StoryLines.Rude;
			if (EnvMan.IsNight() && Random.value < 0.35f)
			{
				pool = StoryLines.Night;
			}
			Say(Pick(pool, pool == StoryLines.Polite ? "polite" : pool == StoryLines.Snide ? "snide" : pool == StoryLines.Rude ? "rude" : "night"));
			m_nextTalk = gap * Mathf.Lerp(1.1f, 0.5f, Mathf.Clamp01(share)) * Random.Range(0.75f, 1.25f);
			return;
		}
		NextBurst(p, gap);
	}

	// The next bit of the ravens' show.
	private void NextBurst(Player p, float gap)
	{
		int ep = GetInt(p, EpisodeKey);
		int burst = GetInt(p, BurstKey);
		string[][][] eps = StoryLines.Episodes;
		if (ep >= eps.Length)
		{
			Say(Pick(StoryLines.Reruns, "rerun"));
			m_nextTalk = gap * Random.Range(0.9f, 1.8f);
			return;
		}
		string[] lines = eps[ep][burst];
		burst++;
		if (burst >= eps[ep].Length)
		{
			ep++;
			burst = 0;
		}
		Set(p, EpisodeKey, ep);
		Set(p, BurstKey, burst);
		if (lines.Length == 1 && lines[0].StartsWith("~") && float.TryParse(lines[0].Substring(1), out float minutes))
		{
			// A long silence (it's part of the show).
			m_nextTalk = minutes * 60f;
			return;
		}
		Say(lines);
		// Between episodes a longer wait than within one.
		m_nextTalk = gap * (burst == 0 ? Random.Range(1.2f, 1.8f) : Random.Range(0.5f, 0.9f));
	}

	private string[] Pick(string[][] pool, string key)
	{
		m_shuffle.TryGetValue(key, out int i);
		if (i == 0)
		{
			i = Random.Range(0, pool.Length) + 1;
		}
		m_shuffle[key] = i % pool.Length + 1 + (Random.value < 0.3f ? 1 : 0);
		return pool[(i - 1) % pool.Length];
	}

	private void Say(string[] lines)
	{
		foreach (string line in lines)
		{
			bool munin = line.StartsWith("M:");
			string text = line.Length > 2 && line[1] == ':' ? line.Substring(2) : line;
			m_queue.Enqueue((munin, text));
		}
	}

	private static float ReadTime(string text) => Mathf.Max(5f, 2.5f + text.Length * 0.075f);

	private void UpdateTalk(float dt)
	{
		if (m_lineLeft > 0f)
		{
			m_lineLeft -= dt;
			if (m_lineLeft <= 0f && m_queue.Count == 0)
			{
				m_lineLeft = 0f;
				Dismiss(m_hugin);
				Dismiss(m_munin);
			}
			return;
		}
		if (m_queue.Count == 0)
		{
			return;
		}
		// Both arrive for a burst that has both in it; each says his line in turn.
		bool both = false;
		foreach ((bool munin, string _) in m_queue)
		{
			both |= munin;
		}
		Bird? hugin = Arrive(false);
		Bird? munin2 = both ? Arrive(true) : null;
		(bool who, string text) = m_queue.Peek();
		Bird? speaker = who ? munin2 : hugin;
		if (speaker == null)
		{
			speaker = hugin ?? munin2;
		}
		if (speaker == null)
		{
			m_queue.Clear();
			return;
		}
		if (speaker.Age < 0.8f)
		{
			// Landing first.
			return;
		}
		m_queue.Dequeue();
		float seconds = ReadTime(text);
		m_lineLeft = seconds;
		Speak(speaker, text, seconds);
	}

	// ---------------------------------------------------------------- the ravens

	private void FindTemplates()
	{
		if (m_looked)
		{
			return;
		}
		m_looked = true;
		foreach (Raven r in Resources.FindObjectsOfTypeAll<Raven>())
		{
			if (r == null || r.m_visual == null)
			{
				continue;
			}
			if (r.m_isMunin ? m_muninTemplate != null : m_huginTemplate != null)
			{
				continue;
			}
			GameObject t = Pictures.MakePicture(r.m_visual, m_holder!, Plugin.RenderLayer.Value);
			string mark = r.m_exclamation != null ? r.m_exclamation.name : "";
			foreach (Transform child in t.GetComponentsInChildren<Transform>(true))
			{
				if (mark.Length > 0 && child.name == mark)
				{
					child.gameObject.SetActive(false);
				}
			}
			if (r.m_isMunin)
			{
				m_muninTemplate = t;
			}
			else
			{
				m_huginTemplate = t;
			}
		}
		if (m_muninTemplate == null && m_huginTemplate != null)
		{
			// No Munin in this version: Hugin's look, pale.
			m_muninTemplate = Instantiate(m_huginTemplate, m_holder, false);
			foreach (Renderer r in m_muninTemplate.GetComponentsInChildren<Renderer>(true))
			{
				foreach (Material m in r.materials)
				{
					m.color = new Color(0.9f, 0.9f, 0.88f);
				}
			}
		}
	}

	private Bird? Arrive(bool munin)
	{
		Bird? b = munin ? m_munin : m_hugin;
		if (b != null && b.Leave < 0f)
		{
			return b;
		}
		if (b != null)
		{
			RemoveBird(b);
		}
		FindTemplates();
		GameObject? template = munin ? m_muninTemplate : m_huginTemplate;
		Camera? main = Utils.GetMainCamera();
		if (template == null || main == null)
		{
			return null;
		}
		EnsureBirdCamera(main);
		GameObject go = Instantiate(template, transform, false);
		go.name = munin ? "StoryMunin" : "StoryHugin";
		go.SetActive(true);
		b = new Bird { Go = go, Anim = go.GetComponentInChildren<Animator>(), Munin = munin };
		foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
		{
			// Its outline from the bones as they are, so Place can keep him in frame.
			smr.updateWhenOffscreen = true;
		}
		Measure(b, main);
		if (b.Anim != null && SurfGame.SetTrigger(b.Anim, "flyin") == null)
		{
			SurfGame.SetTrigger(b.Anim, "teleportin");
		}
		m_audio.Play("sfx_raven_flap|sfx_raven_teleport", 0.6f);
		m_audio.Play("sfx_raven_kaw", 0.5f, munin ? Random.Range(0.8f, 0.88f) : Random.Range(0.95f, 1.08f));
		if (munin)
		{
			m_munin = b;
		}
		else
		{
			m_hugin = b;
		}
		Place(b, main);
		return b;
	}

	private void Measure(Bird b, Camera cam)
	{
		Transform t = b.Go.transform;
		t.localScale = Vector3.one;
		if (!Pictures.MeshBounds(b.Go, out Bounds bounds) || bounds.size.y < 0.01f)
		{
			b.Size = 1.3f;
			t.localScale = Vector3.one * b.Size;
			return;
		}
		b.Centre = Quaternion.Inverse(t.rotation) * (bounds.center - t.position);
		b.Height = bounds.size.y;
		float half = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Depth;
		float most = 1.3f * Depth / 5f * 0.4f;
		b.Size = Mathf.Clamp(ScreenHeight * half / b.Height, most * 0.2f, most);
		t.localScale = Vector3.one * b.Size;
	}

	private void Speak(Bird b, string text, float seconds)
	{
		if (b.Anim != null)
		{
			SurfGame.SetTrigger(b.Anim, "talk");
		}
		m_audio.Play("sfx_raven_talk|sfx_raven_kaw", 0.35f, b.Munin ? 0.85f : 1f);
		if (Chat.instance != null)
		{
			Vector3 above = b.Go.transform.rotation * (b.Centre * b.Size) + Vector3.up * (b.Height * b.Size * 0.65f);
			Chat.instance.SetNpcText(b.Go, above, 30f, seconds, "", text, true);
		}
	}

	private void Dismiss(Bird? b)
	{
		if (b == null || b.Leave >= 0f)
		{
			return;
		}
		b.Leave = 0f;
		if (Chat.instance != null)
		{
			Chat.instance.ClearNpcText(b.Go);
		}
		if (b.Anim != null && SurfGame.SetTrigger(b.Anim, "flyaway") == null)
		{
			SurfGame.SetTrigger(b.Anim, "poff");
		}
		m_audio.Play("sfx_raven_flap|sfx_raven_poof", 0.5f);
	}

	private void UpdateBirds(float dt)
	{
		foreach (Bird? b in new[] { m_hugin, m_munin })
		{
			if (b == null)
			{
				continue;
			}
			b.Age += dt;
			if (b.Leave >= 0f)
			{
				b.Leave += dt;
				if (b.Leave > 2f)
				{
					RemoveBird(b);
				}
			}
		}
		bool any = m_hugin != null || m_munin != null;
		if (m_birdCam != null && m_birdCam.enabled != any)
		{
			m_birdCam.enabled = any;
			m_birdLight!.enabled = any;
		}
	}

	private void PlaceBirds()
	{
		Camera? main = Utils.GetMainCamera();
		if (main == null)
		{
			return;
		}
		if (m_hugin != null)
		{
			Place(m_hugin, main);
		}
		if (m_munin != null)
		{
			Place(m_munin, main);
		}
		if (m_birdCam != null)
		{
			m_birdCam.fieldOfView = main.fieldOfView;
		}
	}

	// Perched at the top right of the view: Hugin in the corner, Munin a little in from him.
	private static void Place(Bird b, Camera cam)
	{
		float half = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Depth;
		float aspect = cam.aspect > 0.1f ? cam.aspect : 16f / 9f;
		// 1.16.82 (Sam: his words sat over the buff icons at the top right): lower down the
		// right side, so the speech above him clears the HUD.
		Vector3 local = new Vector3((b.Munin ? 0.5f : 0.76f) * half * aspect, (b.Munin ? 0.12f : 0.16f) * half, Depth);
		Transform ct = cam.transform;
		Vector3 at = ct.position + ct.rotation * local;
		Vector3 back = ct.position - at;
		back.y = 0f;
		if (back.sqrMagnitude > 0.01f)
		{
			b.Go.transform.rotation = Quaternion.LookRotation(back) * Quaternion.Euler(0f, b.Munin ? -25f : 25f, 0f);
		}
		b.Go.transform.position = at - b.Go.transform.rotation * (b.Centre * b.Size);
		// 1.16.85 (Sam: only Munin's talons showed, big, in the corner, the rest of him off the
		// top of the view): the size and middle measured when he arrives aren't where his
		// animation actually puts him. Each frame, his real outline is centred on his perch and
		// eased to the size he should be.
		if (Pictures.MeshBounds(b.Go, out Bounds live) && live.size.y > 0.01f)
		{
			// (half: the view's half height at his depth, above)
			float want = ScreenHeight * half;
			float tall = Mathf.Max(live.size.y, live.size.x * 0.6f);
			float k = Mathf.Clamp(want / tall, 0.85f, 1.15f);
			if (Mathf.Abs(k - 1f) > 0.03f)
			{
				b.Size = Mathf.Clamp(b.Size * Mathf.Lerp(1f, k, 0.5f), 0.02f, 3f);
				b.Go.transform.localScale = Vector3.one * b.Size;
			}
			b.Height = live.size.y / Mathf.Max(0.01f, b.Size);
			// Eased, not snapped: the outline can be a frame behind.
			Vector3 off = Vector3.ClampMagnitude(live.center - at, half);
			b.Centre += Quaternion.Inverse(b.Go.transform.rotation) * off / Mathf.Max(0.01f, b.Size) * 0.3f;
		}
	}

	private void EnsureBirdCamera(Camera main)
	{
		if (m_birdCam != null)
		{
			if (m_birdCam.transform.parent != main.transform)
			{
				m_birdCam.transform.SetParent(main.transform, false);
			}
			return;
		}
		GameObject go = new GameObject("StoryRavenCamera");
		go.transform.SetParent(main.transform, false);
		m_birdCam = go.AddComponent<Camera>();
		m_birdCam.clearFlags = CameraClearFlags.Depth;
		m_birdCam.cullingMask = 1 << Plugin.RenderLayer.Value;
		m_birdCam.depth = main.depth + 5f;
		m_birdCam.nearClipPlane = 0.2f;
		m_birdCam.farClipPlane = 40f;
		m_birdCam.fieldOfView = main.fieldOfView;
		m_birdCam.allowHDR = false;
		m_birdCam.allowMSAA = false;
		GameObject lightGo = new GameObject("StoryRavenLight");
		lightGo.transform.SetParent(transform, false);
		lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
		m_birdLight = lightGo.AddComponent<Light>();
		m_birdLight.type = LightType.Directional;
		m_birdLight.intensity = 1.6f;
		m_birdLight.color = new Color(1f, 0.96f, 0.88f);
		m_birdLight.cullingMask = 1 << Plugin.RenderLayer.Value;
		m_birdLight.shadows = LightShadows.None;
		// 1.16.82 (Sam: Hugin couldn't be seen at night): a black bird on a night sky needs his
		// own light: a cool rim from behind, and the world's dark and fog left out of his camera.
		GameObject rimGo = new GameObject("StoryRavenRim");
		rimGo.transform.SetParent(lightGo.transform, false);
		rimGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
		Light rim = rimGo.AddComponent<Light>();
		rim.type = LightType.Directional;
		rim.intensity = 1.5f;
		rim.color = new Color(0.7f, 0.82f, 1f);
		rim.cullingMask = 1 << Plugin.RenderLayer.Value;
		rim.shadows = LightShadows.None;
		// 1.16.84 (Sam: terrain lighting flickered while Hugin was up): no more swapping the
		// world's fog and ambient around his camera; a soft fill from the front instead.
		GameObject fillGo = new GameObject("StoryRavenFill");
		fillGo.transform.SetParent(go.transform, false);
		fillGo.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
		Light fill = fillGo.AddComponent<Light>();
		fill.type = LightType.Directional;
		fill.intensity = 0.9f;
		fill.color = new Color(0.85f, 0.85f, 0.95f);
		fill.cullingMask = 1 << Plugin.RenderLayer.Value;
		fill.shadows = LightShadows.None;
	}

	private void RemoveBird(Bird b)
	{
		if (b.Go != null)
		{
			if (Chat.instance != null)
			{
				Chat.instance.ClearNpcText(b.Go);
			}
			Destroy(b.Go);
		}
		if (b == m_hugin)
		{
			m_hugin = null;
		}
		if (b == m_munin)
		{
			m_munin = null;
		}
	}

	private void RemoveBirds()
	{
		if (m_hugin != null)
		{
			RemoveBird(m_hugin);
		}
		if (m_munin != null)
		{
			RemoveBird(m_munin);
		}
		m_lineLeft = 0f;
	}

	// ---------------------------------------------------------------- testing

	public static string Command(Player p, string[] args)
	{
		string what = args.Length > 1 ? args[1].ToLowerInvariant() : "";
		SurfStory? s = Instance;
		switch (what)
		{
			case "reset":
				Set(p, StageKey, StageNone);
				Set(p, EpisodeKey, 0);
				Set(p, BurstKey, 0);
				if (s != null)
				{
					s.RemoveBirds();
					s.m_queue.Clear();
					s.RemovePortal();
					s.m_playTime = 0f;
				}
				return "Story reset: the strange portal comes again after a while.";
			case "portal":
				Set(p, StageKey, StageNone);
				if (s != null)
				{
					// 1.16.84 (Sam: a debug to spawn the portal now): right away, in front of you.
					s.RemovePortal();
					s.m_playTime = 99999f;
					s.m_portalLooked = Time.time + 20f;
					if (!s.SpawnPortalNow(p))
					{
						return "Couldn't place the strange portal here. Try in the open.";
					}
				}
				return Plugin.RavenStory.Value ? "A strange portal stands in front of you." : "A strange portal stands in front of you (Raven Story is off in the config, so it won't do anything).";
			case "hugin":
				Set(p, StageKey, StageHugin);
				Set(p, AwakeKey, Now - (args.Length > 2 && float.TryParse(args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d) ? d : 0f) * DayLength);
				if (s != null)
				{
					s.m_nextTalk = 1f;
				}
				return "Hugin is on his way.";
			case "munin":
				Set(p, StageKey, StageMunin);
				Set(p, EpisodeKey, args.Length > 2 && int.TryParse(args[2], out int ep) ? Mathf.Max(0, ep) : 0);
				Set(p, BurstKey, 0);
				if (s != null)
				{
					s.m_nextTalk = 1f;
				}
				return "Hugin and Munin are on their way.";
			case "next":
				if (s != null)
				{
					s.m_nextTalk = 0f;
				}
				return "Next line now.";
			default:
				return $"Story stage {Stage(p)}, {DaysAwake(p):0.00} days awake, episode {GetInt(p, EpisodeKey)} burst {GetInt(p, BurstKey)}. "
					+ "surfstory [reset | portal | hugin [days awake] | munin [episode] | next]";
		}
	}
}
