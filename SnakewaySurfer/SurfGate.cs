using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// The gate between the spawners and the track (the placement service, SurfPlacement.cs): every
// row the game lays out is written into the placement record, and if a runner could no longer
// get through fairly, the new thing nearest the trouble is taken out again until one can.
// What it took out is logged ("Placement: ..."), so a self-test run shows how often the
// spawners needed correcting and where.
//
// What it knows so far: where things stand when they appear. Creatures that run at you or
// change lane are recorded where they wait; their attacks get their own records next.
internal sealed partial class SurfGame
{
	private readonly Placement m_place = new();
	private float m_placeFrom;
	private int m_placeChecked;

	private Pace GatePace() =>
		// A little faster than now: the run speeds up before the runner gets there.
		Pace.At(Mathf.Max(8f, m_speed) * 1.1f, Plugin.JumpHeight.Value, Plugin.SlideTime.Value, Gravity);

	private bool GateNeed(Thing t, out Need need)
	{
		need = Need.Wall;
		if (!t.Collidable || t.Go == null || t.Lane < 0 || t.Lane > 2 || t.FreeX || t.Swoop || t.Nuisance || t.Life > 0f)
		{
			return false;
		}
		switch (t.Kind)
		{
			case Kind.Low:
				need = t.ClearHeight >= Plugin.JumpHeight.Value * 0.9f ? Need.Wall : Need.Jump;
				return true;
			case Kind.High:
				need = Need.Roll;
				return true;
			case Kind.Block:
			{
				bool jump = t.ClearHeight > 0f && t.ClearHeight < Plugin.JumpHeight.Value * 0.85f;
				// A roll dodges a creature, as in the game; walls and bare obstacles it doesn't.
				bool roll = !t.Wall && t.Label.Length > 0;
				need = t.JumpOrSlide || (jump && roll) ? Need.Either : jump ? Need.Jump : roll ? Need.Roll : Need.Wall;
				return true;
			}
			default:
				return false;
		}
	}

	// Called after a row has been laid out: `before` is how many things there were until then.
	private void GateRow(int before)
	{
		if (!Plugin.FairPlacement.Value || before >= m_things.Count)
		{
			return;
		}
		// The record is rebuilt from what is really on the track, so anything removed elsewhere
		// (a dungeon's cleared corners, a boss's arena) is never counted.
		if (m_z < m_placeFrom - 1f || m_placeChecked == 0)
		{
			m_place.Reset(m_z);
		}
		m_placeFrom = m_z;
		m_placeChecked++;
		Pace pace = GatePace();
		m_place.Clear();
		for (int i = 0; i < before; i++)
		{
			Thing t = m_things[i];
			if (GateNeed(t, out Need need))
			{
				m_place.Add(t.Lane, t.Z - t.HalfLength - 0.4f, t.Z + t.HalfLength + 0.4f, need, t.Label, t);
			}
		}
		m_place.Passed(m_z, pace);
		if (!m_place.Passable(pace, out _))
		{
			// Something older than this row already closes the track (a set piece with its own
			// rules); nothing here to judge the new row by.
			return;
		}
		List<Thing> mine = new();
		for (int i = before; i < m_things.Count; i++)
		{
			Thing t = m_things[i];
			if (GateNeed(t, out Need need))
			{
				m_place.Add(t.Lane, t.Z - t.HalfLength - 0.4f, t.Z + t.HalfLength + 0.4f, need, t.Label, t);
				mine.Add(t);
			}
		}
		while (mine.Count > 0 && !m_place.Passable(pace, out float stuck))
		{
			Thing worst = mine[0];
			foreach (Thing t in mine)
			{
				if (Mathf.Abs(t.Z - stuck) < Mathf.Abs(worst.Z - stuck))
				{
					worst = t;
				}
			}
			mine.Remove(worst);
			m_place.RemoveOwner(worst);
			m_place.Removed++;
			string biome = m_biomes.Count > 0 ? m_biomes[BiomeAt(worst.Z)].Def.Name : "?";
			Plugin.Log.LogInfo($"Placement: took out {(worst.Label.Length > 0 ? worst.Label : worst.Kind.ToString())} [{worst.Kind}] on lane {worst.Lane} at {worst.Z:0} m in {biome}{(InCrypt(worst.Z) ? " dungeon" : "")}: no fair way through at {stuck:0} m ({m_place.Removed} so far this run).");
			m_things.Remove(worst);
			DestroyThing(worst);
		}
	}
}
