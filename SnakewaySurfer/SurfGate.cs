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
		// An icicle or loose rock still up in the roof is judged as what it will be once it has
		// fallen (a block in the floor to jump): it isn't collidable until then.
		if (!t.Collidable && !Hanging(t))
		{
			return false;
		}
		if (t.Go == null || t.Lane < 0 || t.Lane > 2 || t.FreeX || t.Swoop || t.Nuisance || t.Life > 0f)
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
	private void GateRow(int before) => GateNew(before, "row");

	// Every spawner that puts something in the runner's way mid-run calls this once it has added
	// its things (`before`: how many there were until then). The placement service decides what
	// may stay (Placement.Admit): nothing inside the reaction distance (speed x MinWarning), and
	// nothing that leaves no fair way through.
	// 1.16.x (architecture step 1): one gate for every spawner. Rows used to be the only callers,
	// and a row was not judged at all when an older set piece already closed the track; now it
	// may not close it any sooner than it was.
	private void GateNew(int before, string source)
	{
		if (!Plugin.FairPlacement.Value || before >= m_things.Count)
		{
			return;
		}
		Pace pace = Record(before, null);
		List<Slot> fresh = new();
		for (int i = before; i < m_things.Count; i++)
		{
			Thing t = m_things[i];
			if (GateNeed(t, out Need need))
			{
				fresh.Add(m_place.Add(t.Lane, t.Z - t.HalfLength - 0.4f, t.Z + t.HalfLength + 0.4f, need, t.Label, t));
			}
		}
		if (fresh.Count == 0)
		{
			return;
		}
		float warn = Mathf.Max(8f, m_speed) * MinWarning;
		foreach (Slot s in m_place.Admit(pace, fresh, m_z, warn))
		{
			if (s.Owner is not Thing worst)
			{
				continue;
			}
			string biome = m_biomes.Count > 0 ? m_biomes[BiomeAt(worst.Z)].Def.Name : "?";
			string why = s.From < m_z + warn ? $"under {MinWarning:0.0} s ahead" : "no fair way through";
			Plugin.Log.LogInfo($"Placement: took out {(worst.Label.Length > 0 ? worst.Label : worst.Kind.ToString())} [{worst.Kind}] ({source}) on lane {worst.Lane} at {worst.Z:0} m in {biome}{(InCrypt(worst.Z) ? " dungeon" : "")}: {why} ({m_place.Removed} so far this run).");
			m_things.Remove(worst);
			DestroyThing(worst);
		}
	}
	// The record rebuilt from the first `count` things really on the track (all but `skip`), so
	// anything removed elsewhere (a dungeon's cleared corners, a boss's arena) is never counted.
	private Pace Record(int count, Thing? skip)
	{
		if (m_z < m_placeFrom - 1f || m_placeChecked == 0)
		{
			m_place.Reset(m_z);
		}
		m_placeFrom = m_z;
		m_placeChecked++;
		Pace pace = GatePace();
		m_place.Clear();
		for (int i = 0; i < count; i++)
		{
			Thing t = m_things[i];
			if (t != skip && GateNeed(t, out Need need))
			{
				m_place.Add(t.Lane, t.Z - t.HalfLength - 0.4f, t.Z + t.HalfLength + 0.4f, need, t.Label, t);
			}
		}
		m_place.Passed(m_z, pace);
		return pace;
	}

	// May a thing already on the track move to `lane` now (an icicle cracking loose over the
	// runner's lane)? Asks the placement service; nothing is taken out.
	private bool MayMoveTo(Thing t, int lane)
	{
		if (!Plugin.FairPlacement.Value)
		{
			return true;
		}
		int was = t.Lane;
		bool judged = GateNeed(t, out Need need);
		if (!judged)
		{
			return true;
		}
		Pace pace = Record(m_things.Count, t);
		Slot s = m_place.Add(lane, t.Z - t.HalfLength - 0.4f, t.Z + t.HalfLength + 0.4f, need, t.Label, t);
		int removed = m_place.Removed;
		bool ok = m_place.Admit(pace, new[] { s }, m_z, 0f).Count == 0;
		m_place.Removed = removed;
		if (!ok)
		{
			Plugin.Log.LogInfo($"Placement: kept {(t.Label.Length > 0 ? t.Label : t.Kind.ToString())} on lane {was} at {t.Z:0} m: lane {lane} would leave no fair way through.");
		}
		return ok;
	}
}
