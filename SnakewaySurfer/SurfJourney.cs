using UnityEngine;

namespace SnakewaySurfer;

// 1.17 (run structure step 2, plan "Revision, Nov 15"): a run is a Journey, one biome at one
// star level in three stages, ending after its boss with a win screen:
// - Travel: from the start of the biome to the halfway ward stone (SurfCheckpoint.cs), or its
//   dungeon's exit. For the Plains the ocean crossing is the travel stage (landfall ends it).
// - Hunt: the summoning items turn up from the stone on, all within the hunt's length.
// - Boss: the fight (SurfBoss.cs). Once it falls, a short breather on the same ground, then the
//   win screen (State.Won).
// Each stage lasts about Journey Stage Seconds at the speed you run in that biome, so a Journey
// takes about five minutes with its boss. Endless (Run Mode) is the old run, untouched; the
// self-test tour always runs Endless.
internal sealed partial class SurfGame
{
	// Set at the start of each run (a config change mid-run waits for the next one).
	private bool m_journey;
	private int m_journeyBiome;
	private int m_journeyStars;
	// -1 before the run starts; 0 travel, 1 hunt, 2 boss, 3 done (the breather).
	private int m_journeyStage = -1;
	// Counting down the breather after the boss; the win screen when it runs out.
	private float m_journeyWinIn = -1f;

	private static readonly string[] s_stageNames = { "Travel", "Hunt", "Boss" };
	private static readonly string[] s_markNames = { "finish", "challenge", "a life to spare" };

	// 1.17 (step 3): the three marks (JourneyGoals.cs). Held: on this Journey from any run;
	// run: earned in this run; new: earned for the first time in this run.
	private JourneyTally m_tally;
	private Challenge? m_challenge;
	private int m_marksHeld;
	private int m_marksRun;
	private int m_marksNew;
	private readonly float[] m_markFlash = new float[3];
	private bool m_challengeSaid;

	private bool JourneyWanted => Plugin.Mode.Value == RunMode.Journey && !Touring;

	// At the start of a run, before the stretches are laid out.
	private void ResetJourney(int biome, int stars)
	{
		m_journey = JourneyWanted;
		m_journeyBiome = biome;
		m_journeyStars = stars;
		m_journeyStage = -1;
		m_journeyWinIn = -1f;
		// (Still set here: ApplyResume, right after the stretches are laid out, clears it.)
		m_tally = new JourneyTally { FromStart = !m_resumeNext };
		m_challenge = JourneyGoals.For(biome);
		m_marksHeld = m_player != null ? JourneyGoals.Of(Progress.Marks(m_player), biome, stars) : 0;
		m_marksRun = 0;
		m_marksNew = 0;
		System.Array.Clear(m_markFlash, 0, m_markFlash.Length);
		m_challengeSaid = false;
		ResetLesson(biome, stars, m_tally.FromStart);
		m_coinFraction = 0f;
	}

	// 1.17 (step 4): sprinting gives a job in a Journey: coins picked up count Sprint Coin
	// Multiplier times (the fractions carry over).
	private float m_coinFraction;

	private bool SprintCoinBonus => m_journey && m_sprinting && m_state == State.Running && Plugin.SprintCoinMultiplier.Value > 1.001f;

	private int SprintCoins(int gain)
	{
		if (!SprintCoinBonus)
		{
			return gain;
		}
		m_coinFraction += gain * (Plugin.SprintCoinMultiplier.Value - 1f);
		int extra = Mathf.FloorToInt(m_coinFraction);
		m_coinFraction -= extra;
		return gain + extra;
	}

	// Metres one stage takes at the speed you'd run there (StartSpeed + SpeedPerBiome per biome
	// and star of progress, as SpeedProgressAt, taken halfway through the biome).
	private static float JourneyStageLength(int biome, int stars)
	{
		float speed = Mathf.Min(Plugin.StartSpeed.Value + Plugin.SpeedPerBiome.Value * (biome + stars + 0.5f), Plugin.MaxSpeed.Value) * Plugin.SpeedScale;
		return Mathf.Max(100f, Plugin.JourneyStageSeconds.Value * speed);
	}

	// Where the travel stage ends and the hunt begins: the biome's dungeon exit, or its halfway
	// stone. (Endless: half the biome, as before.)
	private float HalfZ(Segment s) => !float.IsNaN(CryptExitFor(s)) ? CryptExitFor(s) + 12f : s.Start + s.Travel;

	// How far on the summoning items keep coming before Hugin starts nagging.
	private float HuntEnd(Segment s) => m_journey ? HalfZ(s) + s.Hunt : s.Start + Plugin.BiomeLength.Value;

	private int JourneyStageAt(float z)
	{
		Segment s = SegAt(z);
		if (m_sea.AtSea || s.Sea || s.Shore)
		{
			return 0;
		}
		if (s.Boss)
		{
			return 2;
		}
		if (s.Repeat)
		{
			return 3;
		}
		return z < HalfZ(s) ? 0 : 1;
	}

	// Each frame while running.
	private void UpdateJourney(float dt)
	{
		if (!m_journey || m_segments.Count == 0)
		{
			return;
		}
		int stage = JourneyStageAt(m_z);
		if (stage != m_journeyStage)
		{
			m_journeyStage = stage;
			if (stage < 3)
			{
				Plugin.Log.LogInfo($"Journey: {JourneyName()} {m_journeyStars}* stage {s_stageNames[stage]} at z={m_z:0} ({m_runTime:0} s).");
			}
		}
		UpdateLesson();
		if (!m_challengeSaid && m_challenge != null)
		{
			// Hugin names the trial as the Journey starts.
			m_challengeSaid = true;
			RavenSay(m_challenge.Hugin, 6f);
		}
		if (InCrypt(m_z))
		{
			m_tally.SawDungeon = true;
		}
		for (int i = 0; i < 3; i++)
		{
			m_markFlash[i] = Mathf.MoveTowards(m_markFlash[i], 0f, dt * 1.2f);
		}
		CheckChallenge(stage == 3 ? 2 : stage);
		// With no boss to fight (Boss Fights off), the Journey ends where the hunt would.
		if (stage == 3 && m_journeyWinIn < 0f && !m_segments.Exists(x => x.Boss))
		{
			m_journeyWinIn = 1.5f;
		}
		if (m_journeyWinIn >= 0f)
		{
			m_journeyWinIn -= dt;
			if (m_journeyWinIn < 0f)
			{
				WinJourney();
			}
		}
	}

	private string JourneyName() => m_journeyBiome >= 0 && m_journeyBiome < m_biomes.Count ? m_biomes[m_journeyBiome].Def.Name : "the dream";

	// The boss fell (SurfBoss.BossHit): a breather, then the win screen.
	private void JourneyBossBeaten()
	{
		m_journeyWinIn = 4f;
	}

	private float m_wonTime;

	private void WinJourney()
	{
		if (m_state != State.Running)
		{
			return;
		}
		m_state = State.Won;
		m_stateTime = 0f;
		m_wonTime = m_runTime;
		m_banner = "";
		m_bannerTime = 0f;
		m_hint = "";
		// 1.17 (step 5): the haul, given on waking; half for a Journey already won.
		bool repeat = (m_marksHeld & JourneyGoals.Finish) != 0;
		m_haul = Rewards.Haul(m_journeyBiome, m_journeyStars, repeat);
		if (m_player != null && m_haul.Count > 0 && !m_tourRan)
		{
			Rewards.AddPending(m_player, m_haul);
		}
		Plugin.Log.LogInfo($"Journey reward: {Rewards.Describe(m_haul)}{(repeat ? " (half: won before)" : "")} (given on waking).");
		int earned = JourneyGoals.Finish;
		if (m_challenge != null && JourneyGoals.Met(m_challenge, m_tally, 3))
		{
			earned |= JourneyGoals.ChallengeMark;
		}
		if (m_lives >= JourneyGoals.LivesNeeded(Plugin.Lives.Value, Plugin.StartLives))
		{
			earned |= JourneyGoals.Life;
		}
		EarnMarks(earned);
		Plugin.Log.LogInfo($"Journey marks held on {JourneyName()} {m_journeyStars}*: {MarkList(m_marksHeld)} (new this run: {MarkList(m_marksNew)}).");
		GrantRunRewards();
		m_audio.Play(Sfx.Biome, 1f, 1.2f);
		Flash(new Color(1f, 0.85f, 0.45f), 0.35f);
		Plugin.Log.LogInfo($"Journey won: {JourneyName()} {m_journeyStars}* in {m_wonTime:0} s ({m_z:0} m, {m_coins} coins, {m_lives} lives left).");
	}

	// The next Journey: the next biome along at the same stars, if it's open.
	private bool NextJourney(out int biome, out bool sea)
	{
		biome = m_journeyBiome + 1;
		sea = Crossing && biome == SeaTo;
		int allowed = LapBiomesNow();
		return biome < allowed && (m_player == null || Progress.JourneyOpen(m_player, biome, m_journeyStars, allowed));
	}

	// ---------------------------------------------------------------- marks

	private void CheckChallenge(int stagesDone)
	{
		if (m_challenge != null && (m_marksRun & JourneyGoals.ChallengeMark) == 0 && JourneyGoals.Met(m_challenge, m_tally, stagesDone))
		{
			EarnMarks(JourneyGoals.ChallengeMark);
		}
	}

	// Earned in this run: saved at once, the rune fills with a chime and a flash.
	private void EarnMarks(int bits)
	{
		bits &= ~m_marksRun;
		if (bits == 0 || m_player == null)
		{
			return;
		}
		int fresh = Progress.AddMarks(m_player, m_journeyBiome, m_journeyStars, bits);
		bool opened = (m_marksHeld | bits) == JourneyGoals.AllMarks && m_marksHeld != JourneyGoals.AllMarks && m_journeyStars < JourneyGoals.MaxStars;
		m_marksRun |= bits;
		m_marksHeld |= bits;
		m_marksNew |= fresh;
		for (int i = 0; i < 3; i++)
		{
			if ((bits & (1 << i)) != 0)
			{
				m_markFlash[i] = 1f;
				string what = i == 1 && m_challenge != null ? $"challenge ({m_challenge.Short})" : s_markNames[i];
				Plugin.Log.LogInfo($"Journey mark: {JourneyName()} {m_journeyStars}* {what}{((fresh & (1 << i)) == 0 ? " (already held)" : "")}.");
			}
		}
		m_audio.Play("sfx_runestone_activate|sfx_gui_select", 0.7f, 1.35f);
		Flash(new Color(1f, 0.85f, 0.4f), 0.2f);
		if (opened)
		{
			Plugin.Log.LogInfo($"Journey: all three marks on {JourneyName()} {m_journeyStars}*: {Progress.Describe(m_journeyBiome, m_journeyStars + 1)} is open.");
		}
	}

	private string MarkList(int mask)
	{
		System.Collections.Generic.List<string> names = new();
		for (int i = 0; i < 3; i++)
		{
			if ((mask & (1 << i)) != 0)
			{
				names.Add(i == 1 && m_challenge != null ? m_challenge.Short : s_markNames[i]);
			}
		}
		return names.Count == 0 ? "none" : string.Join(", ", names);
	}

	// Counting for the challenges (called from the places the run already handles these).
	private void NoteJourneyRoll() => m_tally.Rolls++;

	private void NoteJourneyBlock() => m_tally.Blocks++;

	private void NoteJourneyParry()
	{
		m_tally.Parries++;
		if (m_journeyStage == 2 && m_boss != null)
		{
			m_tally.BossParries++;
		}
	}

	private void NoteJourneyHit()
	{
		switch (m_journeyStage)
		{
			case 0:
				m_tally.HitsTravel++;
				if (InCrypt(m_z))
				{
					m_tally.HitsDungeon++;
				}
				break;
			case 1:
				m_tally.HitsHunt++;
				break;
			case 2:
				m_tally.HitsBoss++;
				break;
		}
	}

	// A rune: a diamond, filled gold when held (bright when earned this run), a dark outline when
	// not, with a ring of light while it's just been earned.
	private static void Rune(Vector2 c, float size, bool held, bool bright, float flash, float k)
	{
		Matrix4x4 m = GUI.matrix;
		GUIUtility.RotateAroundPivot(45f, c);
		float h = size * 0.5f;
		if (flash > 0f)
		{
			float g = h + (4f + 10f * (1f - flash)) * k;
			Fill(new Rect(c.x - g, c.y - g, g * 2f, g * 2f), new Color(1f, 0.9f, 0.5f, 0.45f * flash));
		}
		Fill(new Rect(c.x - h - 2f * k, c.y - h - 2f * k, size + 4f * k, size + 4f * k), new Color(0f, 0f, 0f, 0.6f));
		Fill(new Rect(c.x - h, c.y - h, size, size), held ? (bright ? new Color(1f, 0.82f, 0.25f, 1f) : new Color(0.75f, 0.62f, 0.3f, 0.75f)) : new Color(0.5f, 0.5f, 0.55f, 0.9f));
		if (!held)
		{
			float i = h - 2.5f * k;
			Fill(new Rect(c.x - i, c.y - i, i * 2f, i * 2f), new Color(0.08f, 0.08f, 0.1f, 0.85f));
		}
		GUI.matrix = m;
	}

	private void StartJourney(int biome, int stars) => StartFrom(biome, stars, Crossing && biome == SeaTo);

	private void UpdateWon(bool canInput)
	{
		if (!canInput || m_stateTime < 1f)
		{
			return;
		}
		if (KeyDown(KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter) && NextJourney(out int next, out _))
		{
			StartJourney(next, m_journeyStars);
		}
		else if (KeyDown(KeyCode.R))
		{
			StartJourney(m_journeyBiome, m_journeyStars);
		}
		else if (KeyDown(KeyCode.M))
		{
			EnterMenu();
		}
	}

	// ---------------------------------------------------------------- drawing

	// "Meadows - Travel" and three pips: done (gold), now (bright, breathing), to come (dim).
	private void StageGui(float k)
	{
		if (!m_journey || m_journeyStage < 0 || m_journeyStage > 2 || Event.current.type != EventType.Repaint)
		{
			return;
		}
		Rect box = RectOf(Region.Stage);
		float pip = 14f * k;
		float gap = 10f * k;
		float pipsW = 3f * pip + 2f * gap;
		float rune = 16f * k;
		float runeGap = 12f * k;
		float runesW = 3f * rune + 2f * runeGap + 22f * k;
		Rect label = new Rect(box.x, box.y, box.width - pipsW - runesW - 16f * k, box.height);
		m_stageStyle ??= new GUIStyle(m_small!) { alignment = TextAnchor.MiddleRight, wordWrap = false };
		m_stageStyle.fontSize = m_small!.fontSize;
		string stars = m_journeyStars > 0 ? $" {m_journeyStars}*" : "";
		Shadowed(label, $"{JourneyName()}{stars} - {s_stageNames[m_journeyStage]}", m_stageStyle);
		float x = label.xMax + 16f * k;
		float y = box.y + (box.height - pip) * 0.5f;
		for (int i = 0; i < 3; i++)
		{
			Rect r = new Rect(x + i * (pip + gap), y, pip, pip);
			Fill(new Rect(r.x - 2f * k, r.y - 2f * k, r.width + 4f * k, r.height + 4f * k), new Color(0f, 0f, 0f, 0.5f));
			if (i < m_journeyStage)
			{
				Fill(r, new Color(1f, 0.8f, 0.25f, 0.95f));
			}
			else if (i == m_journeyStage)
			{
				float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
				Fill(r, new Color(1f, 1f, 1f, pulse));
			}
			else
			{
				Fill(r, new Color(0.45f, 0.45f, 0.5f, 0.5f));
			}
		}
		// The three marks after the pips.
		float rx = x + pipsW + 22f * k + rune * 0.5f;
		for (int i = 0; i < 3; i++)
		{
			int bit = 1 << i;
			Rune(new Vector2(rx + i * (rune + runeGap), box.y + box.height * 0.5f), rune, (m_marksHeld & bit) != 0, (m_marksRun & bit) != 0, m_markFlash[i], k);
		}
	}

	private GUIStyle? m_stageStyle;
	private System.Collections.Generic.List<(GameObject prefab, int count)> m_haul = new();
	private GUIStyle? m_haulStyle;

	// The haul as a row of the game's item icons, each with its count.
	private void WinHaulGui()
	{
		if (m_haul.Count == 0)
		{
			return;
		}
		Rect box = RectOf(Region.WinHaul);
		float k = Screen.height / 1080f;
		float size = Mathf.Min(48f * k, box.height - 4f * k);
		float step = size + 70f * k;
		float x = box.center.x - step * m_haul.Count * 0.5f;
		m_haulStyle ??= new GUIStyle(m_small!) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
		m_haulStyle.fontSize = m_small!.fontSize;
		foreach ((GameObject prefab, int count) in m_haul)
		{
			Rect icon = new Rect(x, box.y + (box.height - size) * 0.5f, size, size);
			if (Event.current.type == EventType.Repaint && ItemIcon(prefab.name) is Sprite sprite)
			{
				DrawSprite(icon, sprite, new Color(1f, 1f, 1f, s_guiAlpha));
			}
			Shadowed(new Rect(icon.xMax + 6f * k, box.y, step - size - 6f * k, box.height), $"x{count}", m_haulStyle);
			x += step;
		}
	}
	private GUIStyle? m_markStyle;

	// The three marks in a row, each with its name; new ones glow and breathe.
	private void WinMarksGui()
	{
		Rect box = RectOf(Region.WinMarks);
		float k = Screen.height / 1080f;
		float slot = box.width / 3f;
		float size = Mathf.Min(26f * k, box.height * 0.6f);
		m_markStyle ??= new GUIStyle(m_small!) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
		m_markStyle.fontSize = m_small!.fontSize;
		for (int i = 0; i < 3; i++)
		{
			int bit = 1 << i;
			bool held = (m_marksHeld & bit) != 0;
			bool fresh = (m_marksNew & bit) != 0;
			float glow = fresh ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 3f + i) : 0f;
			float x = box.x + slot * i + size;
			if (Event.current.type == EventType.Repaint)
			{
				Rune(new Vector2(x, box.y + box.height * 0.5f), size, held, held, glow, k);
			}
			string name = i == 1 && m_challenge != null ? m_challenge.Short : s_markNames[i];
			Color old = m_markStyle.normal.textColor;
			m_markStyle.normal.textColor = held ? (fresh ? new Color(1f, 0.85f, 0.35f) : Color.white) : new Color(0.6f, 0.6f, 0.6f);
			Shadowed(new Rect(x + size, box.y, slot - size * 2f, box.height), name, m_markStyle);
			m_markStyle.normal.textColor = old;
		}
	}

	private void WonGui()
	{
		if (Event.current.type == EventType.Repaint)
		{
			Fill(RectOf(Region.WinPanel), new Color(0f, 0f, 0f, 0.55f * s_guiAlpha));
		}
		string next = NextJourney(out int nb, out bool nsea) ? $"Space: next Journey ({Progress.Describe(nb, m_journeyStars, nsea)})     " : "";
		FitShadowed(Region.WinKeys, $"{next}R: this Journey again\nM: pick a Journey     {Plugin.ExitKey.Value}: back to the longhouse", m_mid!);
		try
		{
			FitShadowed(Region.WinTitle, $"{Progress.Describe(m_journeyBiome, m_journeyStars)}: Journey complete!", m_mid!);
			WinMarksGui();
			WinHaulGui();
			int secs = Mathf.RoundToInt(m_wonTime);
			FitShadowed(Region.WinSummary, $"{secs / 60}:{secs % 60:00}     {m_coins} coins     {m_lives} {(m_lives == 1 ? "life" : "lives")} left\n\n{m_rewardText}", m_mid!);
		}
		catch (System.Exception e)
		{
			LogGuiError("win summary", e);
		}
	}
}
