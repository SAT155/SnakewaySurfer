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

	private bool JourneyWanted => Plugin.Mode.Value == RunMode.Journey && !Touring;

	// At the start of a run, before the stretches are laid out.
	private void ResetJourney(int biome, int stars)
	{
		m_journey = JourneyWanted;
		m_journeyBiome = biome;
		m_journeyStars = stars;
		m_journeyStage = -1;
		m_journeyWinIn = -1f;
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
		return biome < LapBiomesNow();
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
		Rect label = new Rect(box.x, box.y, box.width - pipsW - 16f * k, box.height);
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
	}

	private GUIStyle? m_stageStyle;

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
			int secs = Mathf.RoundToInt(m_wonTime);
			FitShadowed(Region.WinSummary, $"{secs / 60}:{secs % 60:00}     {m_coins} coins     {m_lives} {(m_lives == 1 ? "life" : "lives")} left\n\n{m_rewardText}", m_mid!);
		}
		catch (System.Exception e)
		{
			LogGuiError("win summary", e);
		}
	}
}
