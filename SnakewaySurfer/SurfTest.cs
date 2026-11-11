using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SnakewaySurfer;

// 1.16.164: the self-test ("surftest") and the autopilot.
//
// The self-test tours the run by itself: the start menu, then for each biome a stretch of open
// track, its dungeon and its boss, and the ocean crossing. An autopilot plays the running parts
// (lanes, jumps, rolls, dungeon corners and digging); bosses and the crossing are toured without
// taking damage. Along the way it saves screenshots and a report to one folder, so a whole build
// can be looked over from one upload:
//   - every hit the autopilot took (what, where, how long it was in view),
//   - things that appeared too close to react to,
//   - which creatures turned up in which biome,
//   - whether the ravens' pictures were drawn.
internal sealed partial class SurfGame
{
	// Set by the surftest command just before the run begins.
	public static string[]? TourRequest;

	private sealed class TourStage
	{
		public string What = "";
		public int Biome;
		public float Seconds;
		public string Name = "";
	}

	private List<TourStage>? m_tour;
	private TourStage? m_tourPending;
	private int m_tourAt = -1;
	private float m_tourTime;
	private float m_tourNextShot;
	private int m_tourShots;
	private int m_tourShotNo;
	private int m_tourHitShots;
	private bool m_tourSaid;
	private bool m_tourSawBoss;
	private bool m_tourSawCrypt;
	private bool m_tourSawShop;
	private float m_tourCrashWait;
	private string m_tourDir = "";
	private bool m_tourOldInvincible;
	private bool m_tourOldGate;
	private readonly StringBuilder m_tourHits = new();
	private readonly StringBuilder m_tourLate = new();
	private readonly StringBuilder m_tourStages = new();
	private int m_tourHitCount;
	private int m_tourLateCount;
	private readonly Dictionary<string, Dictionary<string, int>> m_tourCensus = new();
	private float m_clock;
	// This run was (partly) a self-test: no rewards or banked coins from it.
	private bool m_tourRan;

	private bool Touring => m_tour != null;

	private bool BotOn => Touring || Plugin.Autopilot.Value;

	// ---------------------------------------------------------------- the tour

	private void BeginTour(string[] args)
	{
		int lap = Plugin.LoopBiomes.Value <= 0 ? m_biomes.Count : Mathf.Clamp(Plugin.LoopBiomes.Value, 1, m_biomes.Count);
		int only = -1;
		bool quick = false;
		for (int i = 1; i < args.Length; i++)
		{
			string a = args[i].ToLowerInvariant();
			if (a == "quick")
			{
				quick = true;
				continue;
			}
			for (int b = 0; b < lap; b++)
			{
				if (m_biomes[b].Def.Name.ToLowerInvariant().Replace(" ", "").StartsWith(a) || a == b.ToString())
				{
					only = b;
				}
			}
		}
		List<TourStage> stages = new();
		if (only < 0)
		{
			stages.Add(new TourStage { What = "menu", Seconds = 4f, Name = "Start menu" });
		}
		for (int b = 0; b < lap; b++)
		{
			if (only >= 0 && b != only)
			{
				continue;
			}
			string name = m_biomes[b].Def.Name;
			if (Crossing && b == SeaTo && !quick)
			{
				stages.Add(new TourStage { What = "sea", Biome = b, Seconds = 70f, Name = "Ocean crossing" });
			}
			stages.Add(new TourStage { What = "run", Biome = b, Seconds = quick ? 25f : 45f, Name = name });
			if (!quick && Plugin.Dungeons.Value && ThemeFor(name) != null)
			{
				stages.Add(new TourStage { What = "dungeon", Biome = b, Seconds = 80f, Name = name + " dungeon" });
			}
			if (!quick && Progress.BossOf(b) != null)
			{
				stages.Add(new TourStage { What = "boss", Biome = b, Seconds = 60f, Name = name + " boss" });
			}
		}
		m_tourDir = Path.Combine(BepInEx.Paths.BepInExRootPath, "SnakewaySurferTest", System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
		Directory.CreateDirectory(m_tourDir);
		m_tourOldInvincible = Plugin.Invincible.Value;
		m_tourOldGate = Plugin.IgnoreBossGate.Value;
		Plugin.Invincible.Value = true;
		Plugin.IgnoreBossGate.Value = true;
		m_tour = stages;
		m_tourRan = true;
		m_tourAt = -1;
		m_tourShotNo = 0;
		Plugin.Log.LogInfo($"Self-test: {stages.Count} stops, saving to {m_tourDir}");
		TourLine($"SnakewaySurfer {Plugin.PluginVersion} self-test, {System.DateTime.Now:yyyy-MM-dd HH:mm}, screen {Screen.width}x{Screen.height}, {stages.Count} stops");
	}

	private void TourLine(string text)
	{
		try
		{
			File.AppendAllText(Path.Combine(m_tourDir, "log.txt"), text + "\n");
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Self-test: couldn't write its log: {e.Message}");
		}
	}

	// From Update, once any loading screen is done.
	private void UpdateTour()
	{
		if (m_tour == null)
		{
			if (TourRequest != null && m_biomes.Count > 0)
			{
				string[] args = TourRequest;
				TourRequest = null;
				BeginTour(args);
			}
			if (m_tour == null)
			{
				return;
			}
		}
		if (m_tourAt < 0)
		{
			NextTourStage();
			return;
		}
		TourStage s = m_tour[m_tourAt];
		float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
		if (m_state == State.Crashed)
		{
			// (The ship can still sink, and a few things get past Invincible.)
			m_tourCrashWait += dt;
			if (m_tourCrashWait > 2f)
			{
				TourShot("crashed");
				m_tourStages.AppendLine($"  {s.Name}: ended early after {m_tourTime:0} s: {m_crashReason}");
				NextTourStage();
			}
			return;
		}
		if (s.What == "menu" ? m_state != State.Menu : m_state != State.Running)
		{
			return;
		}
		if (m_paused)
		{
			return;
		}
		m_tourTime += dt;
		if (!m_tourSaid && s.What != "menu" && m_tourTime > 1f)
		{
			// Hugin shows up at every stop, so his picture is in the first screenshot.
			m_tourSaid = true;
			RavenSay($"Self-test, stop {m_tourAt + 1} of {m_tour.Count}: {s.Name}.", 6f);
			if (m_tourAt <= 1)
			{
				SavePortrait();
			}
		}
		if (m_bossSeg != null && !m_tourSawBoss)
		{
			m_tourSawBoss = true;
			m_tourNextShot = Mathf.Min(m_tourNextShot, m_tourTime + 4f);
			TourLine($"  boss fight began {m_tourTime:0.0} s into the stop");
		}
		if (InCrypt(m_z) && !m_tourSawCrypt)
		{
			m_tourSawCrypt = true;
			m_tourNextShot = Mathf.Min(m_tourNextShot, m_tourTime + 2f);
			TourLine($"  entered the dungeon {m_tourTime:0.0} s into the stop");
		}
		if (m_shopOpen)
		{
			if (!m_tourSawShop && m_shopTime > 1.5f)
			{
				m_tourSawShop = true;
				TourShot("shop");
			}
			if (m_shopTime > 3f)
			{
				CloseShop();
			}
		}
		else
		{
			m_tourSawShop = false;
		}
		if (m_tourTime >= m_tourNextShot && m_tourShots < 8)
		{
			m_tourShots++;
			m_tourNextShot = m_tourTime + Mathf.Max(6f, s.Seconds / 5f);
			TourShot(s.What);
		}
		if (m_tourTime >= s.Seconds)
		{
			string note = s.What == "boss" && !m_tourSawBoss ? " (the boss never appeared)"
				: s.What == "dungeon" && !m_tourSawCrypt ? " (never reached the dungeon)" : "";
			m_tourStages.AppendLine($"  {s.Name}: {s.Seconds:0} s, reached {m_z:0} m{note}");
			NextTourStage();
		}
	}

	private void NextTourStage()
	{
		m_tourAt++;
		m_tourTime = 0f;
		m_tourNextShot = 3.5f;
		m_tourShots = 0;
		m_tourHitShots = 0;
		m_tourSaid = false;
		m_tourSawBoss = false;
		m_tourSawCrypt = false;
		m_tourSawShop = false;
		m_tourCrashWait = 0f;
		if (m_tour == null || m_tourAt >= m_tour.Count)
		{
			EndTour(true);
			return;
		}
		TourStage s = m_tour[m_tourAt];
		TourLine($"STOP {m_tourAt + 1}/{m_tour.Count}: {s.Name}");
		if (s.What == "menu")
		{
			if (m_state != State.Menu)
			{
				EnterMenu();
			}
			return;
		}
		m_tourPending = s;
		if (m_shopOpen)
		{
			CloseShop();
		}
		StartFrom(s.Biome, 0, s.What == "sea");
	}

	// In ResetRun, after the stretches are laid out: a dungeon or boss stop starts close to it.
	private void ApplyTourStage()
	{
		TourStage? s = m_tourPending;
		m_tourPending = null;
		if (s == null || m_segments.Count == 0)
		{
			return;
		}
		Segment first = m_segments[0];
		if (first.Sea || first.Shore || first.Boss || first.Rush)
		{
			return;
		}
		float len = Plugin.BiomeLength.Value;
		float along = 0f;
		if (s.What == "dungeon")
		{
			along = Mathf.Max(0f, Mathf.Max(120f, len * 0.25f) - 45f);
		}
		else if (s.What == "boss" && BossFor(first) is BossDef boss)
		{
			// As a retry from the boss's checkpoint: all its summoning items in hand.
			along = len;
			m_itemSeg = first;
			m_items = boss.Amount;
			m_nextItemZ = 0f;
			m_itemsPlanned = first;
			m_quietToBoss = true;
		}
		if (along <= 0f)
		{
			return;
		}
		first.Start = -along;
		if (first.End != float.MaxValue)
		{
			first.End -= along;
		}
		m_halfPlanned.Add(first);
	}

	private void EndTour(bool finished)
	{
		if (m_tour == null)
		{
			return;
		}
		m_tour = null;
		m_tourPending = null;
		try
		{
			StringBuilder r = new();
			r.AppendLine($"SnakewaySurfer {Plugin.PluginVersion} self-test report ({(finished ? "finished" : "stopped early")})");
			r.AppendLine();
			r.AppendLine($"Ravens' pictures: Hugin = {RavenUi.PortraitState(m_ravenPortrait, false)}");
			r.AppendLine();
			r.AppendLine("Stops:");
			r.Append(m_tourStages);
			r.AppendLine();
			r.AppendLine($"Hits the autopilot took: {m_tourHitCount}");
			r.Append(m_tourHits);
			r.AppendLine();
			r.AppendLine($"Things that appeared too close to react to: {m_tourLateCount}");
			r.Append(m_tourLate);
			r.AppendLine();
			r.AppendLine("Creatures met, by biome:");
			foreach (KeyValuePair<string, Dictionary<string, int>> biome in m_tourCensus)
			{
				List<string> parts = new();
				foreach (KeyValuePair<string, int> c in biome.Value)
				{
					parts.Add($"{c.Key} x{c.Value}");
				}
				parts.Sort();
				r.AppendLine($"  {biome.Key}: {string.Join(", ", parts)}");
			}
			File.WriteAllText(Path.Combine(m_tourDir, "report.txt"), r.ToString());
			Plugin.Log.LogInfo($"Self-test {(finished ? "finished" : "stopped")}: {m_tourHitCount} hit(s), {m_tourLateCount} late appearance(s). Folder: {m_tourDir}");
			if (finished)
			{
				Application.OpenURL("file:///" + m_tourDir.Replace('\\', '/'));
			}
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Self-test: couldn't write its report: {e.Message}");
		}
		if (finished)
		{
			// (Still invincible here, so the tour itself earns nothing.)
			Quit();
			if (m_player != null)
			{
				m_player.Message(MessageHud.MessageType.Center, "Self-test finished. Its folder is open: zip it and send it.");
			}
		}
		Plugin.Invincible.Value = m_tourOldInvincible;
		Plugin.IgnoreBossGate.Value = m_tourOldGate;
	}

	private string m_crashReason = "";

	private void TourShot(string what)
	{
		if (m_tour == null || m_tourAt < 0 || m_tourAt >= m_tour.Count)
		{
			return;
		}
		m_tourShotNo++;
		TourStage s = m_tour[m_tourAt];
		string file = $"{m_tourShotNo:000}_{Safe(s.Name)}_{what}.jpg";
		Dictionary<string, int> ahead = new();
		foreach (Thing t in m_things)
		{
			if (t.Go != null && t.Collidable && t.Label.Length > 0 && t.Z > m_z && t.Z < m_z + 70f
				&& (t.Kind == Kind.Block || t.Kind == Kind.Low || t.Kind == Kind.High))
			{
				ahead.TryGetValue(t.Label, out int n);
				ahead[t.Label] = n + 1;
			}
		}
		List<string> names = new();
		foreach (KeyValuePair<string, int> a in ahead)
		{
			names.Add($"{a.Key} x{a.Value}");
		}
		string biome = m_biomes.Count > 0 ? m_biomes[Mathf.Clamp(BiomeAt(m_z), 0, m_biomes.Count - 1)].Def.Name : "?";
		string flags = (m_atSea ? " at-sea" : "") + (m_camping ? " camping" : "") + (m_shopOpen ? " shop" : "") + (InCrypt(m_z) ? " dungeon" : "")
			+ (m_bossSeg != null ? " boss" : "") + (InMaze ? " maze" : "") + (m_ravenOn ? " raven" : "");
		TourLine($"  {file}: {m_tourTime:0.0} s, state {m_state}, {biome}, {m_z:0} m, lane {m_lane}, lives {m_lives}, speed {m_speed:0.0},{flags}; ahead: {(names.Count > 0 ? string.Join(", ", names) : "nothing")}");
		StartCoroutine(Capture(Path.Combine(m_tourDir, file)));
	}

	private static string Safe(string s)
	{
		StringBuilder b = new();
		foreach (char c in s)
		{
			b.Append(char.IsLetterOrDigit(c) ? c : '-');
		}
		return b.ToString();
	}

	// A screenshot of the whole screen (HUD included), shrunk so a tour's worth stays small.
	private IEnumerator Capture(string file)
	{
		yield return new WaitForEndOfFrame();
		try
		{
			int w = Screen.width;
			int h = Screen.height;
			RenderTexture prev = RenderTexture.active;
			RenderTexture.active = null;
			Texture2D full = new Texture2D(w, h, TextureFormat.RGB24, false);
			full.ReadPixels(new Rect(0, 0, w, h), 0, 0);
			full.Apply();
			int tw = Mathf.Min(w, 1280);
			int th = Mathf.Max(1, Mathf.RoundToInt(h * (tw / (float)w)));
			RenderTexture rt = RenderTexture.GetTemporary(tw, th, 0);
			Graphics.Blit(full, rt);
			RenderTexture.active = rt;
			Texture2D small = new Texture2D(tw, th, TextureFormat.RGB24, false);
			small.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
			small.Apply();
			RenderTexture.active = prev;
			RenderTexture.ReleaseTemporary(rt);
			File.WriteAllBytes(file, ImageConversion.EncodeToJPG(small, 80));
			Destroy(full);
			Destroy(small);
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Self-test: screenshot failed: {e.Message}");
		}
	}

	private void SavePortrait()
	{
		try
		{
			TourLine($"  Hugin's circle shows: {RavenUi.PortraitState(m_ravenPortrait, false)}");
			if (m_ravenPortrait != null)
			{
				File.WriteAllBytes(Path.Combine(m_tourDir, "hugin_from_model.png"), ImageConversion.EncodeToPNG(m_ravenPortrait));
			}
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Self-test: couldn't save Hugin's picture: {e.Message}");
		}
	}

	// ---------------------------------------------------------------- hit log and checks

	// From Hit, for every hit that wasn't blocked (Invincible or not). Always in the game's log;
	// the self-test also keeps it for its report, with a screenshot of the first few per stop.
	private void LogHit(Thing t, string why)
	{
		string biome = m_biomes.Count > 0 ? m_biomes[Mathf.Clamp(BiomeAt(m_z), 0, m_biomes.Count - 1)].Def.Name : "?";
		float seen = t.Born >= 0f ? m_clock - t.Born : -1f;
		string line = $"{(t.Label.Length > 0 ? t.Label : "(obstacle)")} [{t.Kind}{(t.Wall ? ", wall" : "")}{(t.Heavy ? ", heavy" : "")}{(t.Move.Length > 0 ? ", " + t.Move : "")}] in {biome}"
			+ $"{(InCrypt(m_z) ? " dungeon" : "")}{(m_bossSeg != null ? " boss fight" : "")}{(m_atSea ? " at sea" : "")}, {m_z:0} m, lane {m_lane} (its lane {t.Lane}), "
			+ $"you were {(m_y > 0.3f ? $"in the air ({m_y:0.0} m)" : m_slide > 0f ? "rolling" : "running")}, in view for {(seen >= 0f ? seen.ToString("0.0") + " s" : "?")}"
			+ $"{(BotOn ? "; autopilot: " + m_botNote : "")}; \"{why}\"";
		Plugin.Log.LogInfo("Hit: " + line);
		if (!Touring)
		{
			return;
		}
		m_tourHitCount++;
		string stop = m_tourAt >= 0 && m_tourAt < m_tour!.Count ? m_tour[m_tourAt].Name : "?";
		m_tourHits.AppendLine($"  [{stop}, {m_tourTime:0.0} s] {line}");
		if (m_tourHitShots < 4)
		{
			m_tourHitShots++;
			TourShot("hit-" + Safe(t.Label.Length > 0 ? t.Label : "obstacle"));
		}
	}

	// From UpdateThings, the first frame a thing is seen.
	private void NoteNewThing(Thing t)
	{
		t.Born = m_clock;
		if (m_state != State.Running || m_atSea)
		{
			return;
		}
		bool threat = t.Collidable && (t.Kind == Kind.Block || t.Kind == Kind.Low || t.Kind == Kind.High);
		if (Touring && t.Info != null && t.Label.Length > 0)
		{
			string biome = m_biomes[Mathf.Clamp(BiomeAt(t.Z), 0, m_biomes.Count - 1)].Def.Name + (InCrypt(t.Z) ? " dungeon" : "") + (SegAt(t.Z).Boss ? " boss" : "");
			if (!m_tourCensus.TryGetValue(biome, out Dictionary<string, int>? list))
			{
				list = new Dictionary<string, int>();
				m_tourCensus[biome] = list;
			}
			list.TryGetValue(t.Label, out int n);
			list[t.Label] = n + 1;
		}
		// Anything dangerous that first shows up under 0.6 s away, in or beside your lane.
		float gap = t.Z - m_z;
		float closing = Mathf.Max(1f, m_speed + Mathf.Max(0f, t.Speed));
		if (threat && m_runTime > 2f && gap > 0f && gap / closing < 0.6f && Mathf.Abs((t.FreeX ? t.X : LaneX(t.Lane)) - m_x) < LaneWidth * 1.5f)
		{
			string biome = m_biomes[Mathf.Clamp(BiomeAt(m_z), 0, m_biomes.Count - 1)].Def.Name;
			string line = $"{(t.Label.Length > 0 ? t.Label : "(obstacle)")} [{t.Kind}{(t.Move.Length > 0 ? ", " + t.Move : "")}] appeared {gap:0.0} m ahead ({gap / closing:0.00} s away) in lane {t.Lane}, {biome}{(InCrypt(m_z) ? " dungeon" : "")}{(m_bossSeg != null ? " boss fight" : "")}";
			if (Plugin.DebugLog.Value || Touring)
			{
				Plugin.Log.LogInfo("Late appearance: " + line);
			}
			if (Touring && m_tourLateCount < 200)
			{
				m_tourLateCount++;
				string stop = m_tourAt >= 0 && m_tourAt < m_tour!.Count ? m_tour[m_tourAt].Name : "?";
				m_tourLate.AppendLine($"  [{stop}, {m_tourTime:0.0} s] {line}");
			}
		}
	}

	// ---------------------------------------------------------------- the autopilot

	private readonly HashSet<KeyCode> m_botKeys = new();
	private float m_botLaneWait;
	private string m_botNote = "";

	private bool BotKey(KeyCode k) => m_botKeys.Count > 0 && m_botKeys.Contains(k);

	private void BotPress(int step)
	{
		// Keys are as seen on the screen, which is mirrored while the camera looks back.
		m_botKeys.Add(step > 0 != Mirrored ? KeyCode.D : KeyCode.A);
	}

	// Decides this frame's key presses for the running parts: what a careful player would do with
	// what's in view. It doesn't block, parry, sprint, fight bosses or sail.
	private void BotThink(float dt)
	{
		m_botKeys.Clear();
		if (!BotOn || m_state != State.Running || m_atSea || m_camping || m_shopOpen || m_aimTime > 0f)
		{
			return;
		}
		m_botLaneWait -= dt;
		// Digging through a mud bank.
		foreach (DigWall d in m_digs)
		{
			if (d.Hits < DigHits && d.Z - m_z < 2.4f && d.Z - m_z > -1f)
			{
				m_botKeys.Add(KeyCode.E);
				m_botNote = "digging";
				return;
			}
		}
		if (InMaze)
		{
			m_botNote = "in the Elder's maze (not played)";
			return;
		}
		int mustLane = -1;
		if (m_crypt != null)
		{
			// A dungeon corner: press toward the opening once inside its window.
			int k = NextCorner();
			if (k >= 0 && !m_crypt.Armed[k] && m_z >= m_crypt.Corners[k] - TurnWindow + 1f && m_cryptBonk < 0f)
			{
				BotPress(m_crypt.Dirs[k]);
				m_botNote = $"turning {(m_crypt.Dirs[k] > 0 ? "right" : "left")} at a corner";
				return;
			}
			// A junction in mid-hall: the way on is straight ahead.
			foreach (CryptJunction j in m_junctions)
			{
				if (j.Corner < 0 && j.Z > m_z && j.Z - m_z < 30f)
				{
					mustLane = 1;
				}
			}
		}

		float look = Mathf.Max(12f, m_speed * 1.3f);
		float jumpHeight = Plugin.JumpHeight.Value;
		float jumpLead = Mathf.Sqrt(2f * jumpHeight / Gravity) * 0.8f;
		float rollLead = Mathf.Min(0.22f, Plugin.SlideTime.Value * 0.4f);
		float[] cost = new float[3];
		int[] act = new int[3];
		float[] when = { 99f, 99f, 99f };
		float[] reach = new float[3];
		foreach (Thing t in m_things)
		{
			if (t.Go == null || !t.Collidable || (t.Kind != Kind.Block && t.Kind != Kind.Low && t.Kind != Kind.High && t.Kind != Kind.Slow))
			{
				continue;
			}
			float gap = t.Z - m_z;
			if (gap < -t.HalfLength - 0.6f || gap > look + t.HalfLength)
			{
				continue;
			}
			int lane = t.FreeX ? Mathf.Clamp(Mathf.RoundToInt(t.X / LaneWidth) + 1, 0, 2) : t.Lane;
			if (lane < 0 || lane > 2)
			{
				continue;
			}
			if (t.Kind == Kind.Slow)
			{
				cost[lane] = Mathf.Max(cost[lane], 0.3f);
				continue;
			}
			// 1 = jump it, 2 = roll under or past it, 3 = no way through.
			int need;
			if (t.Kind == Kind.Low)
			{
				need = (t.ClearHeight > 0f ? t.ClearHeight : 0.75f) < jumpHeight * 0.9f ? 1 : 3;
			}
			else if (t.Kind == Kind.High)
			{
				need = 2;
			}
			else if (t.JumpOrSlide || (t.ClearHeight > 0f && t.ClearHeight < jumpHeight * 0.85f))
			{
				need = 1;
			}
			else if (!t.Wall && t.Label.Length > 0)
			{
				need = 2;
			}
			else
			{
				need = 3;
			}
			float closing = Mathf.Max(1f, m_speed + Mathf.Max(0f, t.Speed));
			float eta = Mathf.Max(0f, gap - t.HalfLength) / closing;
			float past = (gap + t.HalfLength) / closing;
			if (need == 3)
			{
				cost[lane] = 9f;
			}
			else if (act[lane] != 0 && act[lane] != need && Mathf.Abs(eta - when[lane]) < 0.7f)
			{
				// A jump and a roll too close together.
				cost[lane] = Mathf.Max(cost[lane], 6f);
			}
			else
			{
				cost[lane] = Mathf.Max(cost[lane], need == 2 ? 1.2f : 1f);
			}
			if (eta < when[lane])
			{
				when[lane] = eta;
				act[lane] = need;
				reach[lane] = past;
			}
		}

		int best = m_lane;
		float bestCost = cost[m_lane] + (mustLane >= 0 && m_lane != mustLane ? 20f : 0f);
		for (int l = 0; l < 3; l++)
		{
			if (l < m_minLane || l > m_maxLane)
			{
				continue;
			}
			float c = cost[l] + 0.25f * Mathf.Abs(l - m_lane) + (mustLane >= 0 && l != mustLane ? 20f : 0f);
			if (c < bestCost - 0.3f)
			{
				bestCost = c;
				best = l;
			}
		}
		bool grounded = m_y <= 0.001f;
		if (best != m_lane && m_botLaneWait <= 0f)
		{
			int step = best > m_lane ? 1 : -1;
			int next = m_lane + step;
			// Not sideways into something that's already beside you.
			if (!(cost[next] >= 6f && when[next] < 0.25f) && CanSwitch(m_lane, next))
			{
				BotPress(step);
				m_botLaneWait = 0.16f;
				m_botNote = $"moving to lane {next} (lane costs {cost[0]:0.#}/{cost[1]:0.#}/{cost[2]:0.#})";
				return;
			}
		}
		if (act[m_lane] == 1 && when[m_lane] <= jumpLead && grounded)
		{
			m_botKeys.Add(KeyCode.W);
			m_botNote = $"jumped {when[m_lane]:0.00} s ahead";
		}
		else if (act[m_lane] == 2 && when[m_lane] <= rollLead && m_slide <= 0f && m_rollCooldown <= 0f)
		{
			m_botKeys.Add(KeyCode.S);
			m_botNote = $"rolled {when[m_lane]:0.00} s ahead{(m_stamina < Plugin.RollStamina.Value ? " (out of stamina)" : "")}";
		}
		else if (act[m_lane] == 3 || cost[m_lane] >= 6f)
		{
			m_botNote = $"no way out (lane costs {cost[0]:0.#}/{cost[1]:0.#}/{cost[2]:0.#}, next in {when[m_lane]:0.00} s)";
		}
		else if (act[m_lane] == 0)
		{
			m_botNote = "lane clear";
		}
	}
}
