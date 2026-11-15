using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// 1.17 (run structure step 4, plan "Teach the roll's invincibility by layout, not text"): the
// first Meadows Journey teaches the roll with two set pieces, once per character:
// 1. a boar standing in the middle lane, the side lanes shut by rocks a roll can't pass: rolling
//    through the boar is the only way on;
// 2. a greyling whose swing covers all three lanes (each marked red), too wide to step out of and
//    too high to jump: rolling through it works.
// A hit on either costs no life: a short fade, and the set piece is laid again ahead until it's
// passed. Then the usual rows come back. Both go through the placement service like any row.
// Saved in player custom data "SnakewaySurfer.RollTaught" (surfprogress wipe erases it).
internal sealed partial class SurfGame
{
	private const string RollTaughtKey = "SnakewaySurfer.RollTaught";
	// 0: no lesson this run; 1, 2: the set piece to pass next; 3: both passed.
	private int m_lesson;
	private float m_lessonZ;
	private bool m_lessonPlaced;
	private int m_lessonTries;
	private readonly List<Thing> m_lessonThings = new();
	private bool m_lessonSwung;
	private string m_lessonSwing = "";

	// The rows stay away until the lesson is over (up to just past its last set piece).
	private bool LessonHoldsRows(float z) => m_lesson is 1 or 2 && z < m_lessonZ + (m_lesson == 1 ? 90f : 30f);

	// From ResetJourney: only the Meadows Journey at no stars, from its start, once per character.
	private void ResetLesson(int biome, int stars, bool fromStart)
	{
		m_lessonThings.Clear();
		m_lessonPlaced = false;
		m_lessonTries = 0;
		m_lessonSwung = false;
		bool want = m_journey && biome == 0 && stars == 0 && fromStart && m_player != null && !m_player.m_customData.ContainsKey(RollTaughtKey);
		m_lesson = want ? 1 : 0;
		// A short clear run-in first.
		m_lessonZ = 75f;
		if (want)
		{
			Plugin.Log.LogInfo("Roll lesson: this character hasn't passed it yet; set piece 1 ahead.");
		}
	}

	// Each frame while running (from UpdateJourney).
	private void UpdateLesson()
	{
		if (m_lesson is not (1 or 2))
		{
			return;
		}
		if (!m_lessonPlaced)
		{
			if (m_lessonZ < m_z + AheadDistance)
			{
				PlaceLesson();
			}
			return;
		}
		Thing? main = m_lessonThings.Count > 0 ? m_lessonThings[0] : null;
		if (m_lesson == 2 && main != null && !m_lessonSwung && main.Anim != null && m_lessonSwing.Length > 0 && m_lessonZ - m_z < Mathf.Max(8f, m_speed) * 0.5f)
		{
			// The greyling swings as you come in (its own animation; the run's creature attacks
			// are left off it, so they don't mark or strike on their own).
			m_lessonSwung = true;
			SetTrigger(main.Anim, m_lessonSwing);
		}
		if (m_z > m_lessonZ + 2.5f)
		{
			Plugin.Log.LogInfo($"Roll lesson: set piece {m_lesson} passed (after {m_lessonTries} miss(es)).");
			m_lesson++;
			m_lessonPlaced = false;
			m_lessonTries = 0;
			m_lessonSwung = false;
			m_lessonThings.Clear();
			if (m_lesson == 2)
			{
				m_lessonZ = m_z + Mathf.Max(55f, m_speed * 5f);
			}
			else if (m_player != null)
			{
				m_player.m_customData[RollTaughtKey] = "1";
				Plugin.Log.LogInfo("Roll lesson: both set pieces passed; saved for this character.");
			}
		}
	}

	private void PlaceLesson()
	{
		// Clear anything already laid out where it goes.
		for (int i = m_things.Count - 1; i >= 0; i--)
		{
			Thing t = m_things[i];
			if (t.Kind != Kind.Ground && t.Kind != Kind.Scenery && t.Kind != Kind.Coin && Mathf.Abs(t.Z - m_lessonZ) < 25f)
			{
				DestroyThing(t);
				m_things.RemoveAt(i);
			}
		}
		int before = m_things.Count;
		bool ok = m_lesson == 1 ? LessonBoar(m_lessonZ) : LessonGreyling(m_lessonZ);
		if (!ok)
		{
			Plugin.Log.LogWarning($"Roll lesson: set piece {m_lesson} couldn't be built here (a prefab is missing); skipping it.");
			m_lesson = m_lesson == 1 ? 2 : 3;
			m_lessonZ += 50f;
			return;
		}
		for (int i = before; i < m_things.Count; i++)
		{
			m_lessonThings.Add(m_things[i]);
		}
		GateNew(before, "roll lesson");
		if (m_lessonThings.Exists(t => !m_things.Contains(t)))
		{
			// The placement service took a piece out (too close): lay it again further on.
			ClearLesson();
			m_lessonZ = m_z + Mathf.Max(45f, m_speed * 4f);
			return;
		}
		m_lessonPlaced = true;
	}

	private void ClearLesson()
	{
		foreach (Thing t in m_lessonThings)
		{
			if (m_things.Remove(t))
			{
				DestroyThing(t);
			}
		}
		m_lessonThings.Clear();
		m_lessonPlaced = false;
		m_lessonSwung = false;
	}

	// Hit by a set piece: no life lost, a short fade, and it's laid again ahead.
	private void LessonHit(Thing t)
	{
		m_lessonTries++;
		Plugin.Log.LogInfo($"Roll lesson: set piece {t.Lesson} failed (try {m_lessonTries}).");
		m_audio.Play(Sfx.HitMonster, 0.7f, 1.1f);
		Flash(new Color(0f, 0f, 0f), 0.8f);
		m_hurtTime = Mathf.Max(m_hurtTime, 0.6f);
		ClearLesson();
		m_lessonZ = m_z + Mathf.Max(45f, m_speed * 4f);
	}

	private bool LessonBoar(float z)
	{
		BiomeRuntime b = m_biomes[0];
		GameObject? boar = Pictures.Prefab("Boar");
		GameObject? rock = Pictures.Prefab("Rock_3") ?? Pictures.Prefab("rock4_coast") ?? Pictures.Prefab("rock2_heath");
		if (boar == null || rock == null)
		{
			return false;
		}
		CreatureInfo info = CreatureInfo.Of(boar);
		GameObject go = Creature(boar, Quaternion.Euler(0f, 180f, 0f), RealScale(info));
		go.transform.localPosition = new Vector3(LaneX(1), RoadY(z), z);
		string label = MonsterLabel(boar);
		m_things.Add(new Thing
		{
			Go = go, Kind = Kind.Block, Lane = 1, Z = z, HalfLength = 0.6f, Label = label, Info = info,
			Anim = go.GetComponentInChildren<Animator>(), Lesson = 1,
			CrashText = $"The {label} knocked you over! (Roll through it.)",
		});
		foreach (int lane in new[] { 0, 2 })
		{
			GameObject wall = Fitted(Picture(rock), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), new Vector3(LaneWidth * 0.95f, 2.2f, 1.6f), false);
			wall.transform.localPosition = new Vector3(LaneX(lane), RoadY(z), z);
			m_things.Add(new Thing
			{
				Go = wall, Kind = Kind.Block, Lane = lane, Z = z, HalfLength = 0.8f, Wall = true, Solid = true, Lesson = 1,
				CrashText = $"You ran into the rocks in the {b.Def.Name}!",
			});
		}
		return true;
	}

	private bool LessonGreyling(float z)
	{
		GameObject? prefab = Pictures.Prefab("Greyling") ?? Pictures.Prefab("Greydwarf");
		if (prefab == null)
		{
			return false;
		}
		CreatureInfo info = CreatureInfo.Of(prefab);
		m_lessonSwing = info.PickMelee()?.Animation ?? "";
		// A little bigger than life, so its swing reads as reaching across the track.
		GameObject go = Creature(prefab, Quaternion.Euler(0f, 180f, 0f), RealScale(info) * 1.3f);
		go.transform.localPosition = new Vector3(LaneX(1), RoadY(z), z);
		string label = MonsterLabel(prefab);
		string crash = $"The {label}'s swing caught you! (Roll through it.)";
		Thing main = new Thing
		{
			Go = go, Kind = Kind.Block, Lane = 1, Z = z, HalfLength = 0.7f, Label = label, Info = info,
			Anim = go.GetComponentInChildren<Animator>(), Lesson = 2, CrashText = crash,
			HitLanes = new[] { 0, 1, 2 },
		};
		m_things.Add(main);
		// The swing's reach over the outer lanes, and the red marks for all three.
		foreach (int lane in new[] { 0, 2 })
		{
			GameObject reach = new GameObject("LessonReach") { layer = m_layer };
			reach.transform.SetParent(m_stage, false);
			reach.transform.localPosition = new Vector3(LaneX(lane), RoadY(z), z);
			m_things.Add(new Thing { Go = reach, Kind = Kind.Block, Lane = lane, Z = z, HalfLength = 0.7f, Label = label, Lesson = 2, CrashText = crash });
		}
		for (int lane = 0; lane < 3; lane++)
		{
			main.Markers.Add(MakeMarker(lane, z - 2f, 3.6f, m_markerMaterial));
		}
		return true;
	}

	// ---------------------------------------------------------------- the roll-through cue

	private float m_rollCueAt = -9f;

	// Every roll through an attack or a creature: the game's dodge sound and a pale flash.
	private void RollThroughCue()
	{
		if (m_runTime - m_rollCueAt < 0.25f)
		{
			return;
		}
		m_rollCueAt = m_runTime;
		m_audio.Play("sfx_perfect_dodge|sfx_dodge", 0.8f, Random.Range(0.97f, 1.05f));
		Flash(new Color(0.9f, 0.95f, 1f), 0.25f);
	}
}
