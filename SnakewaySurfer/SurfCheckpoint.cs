using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// 1.16.79 (Sam: "Island's are kind of like sub-checkpoints if you think about it. Maybe we can
// introduce checkpoints now to all of the biomes. Ofcourse only one checkpoint should ever be
// saved since it is just an ad hoc save to reload to in the middle of a biome ... When you die
// you have the option to restart from last checkpoint in a biome"):
// - halfway through each biome a ward stone stands beside the track and lights up as you pass;
// - once you hold all of a biome's summoning items another one waits on the way to the boss;
// - at sea, mooring at an island counts.
// Each saves the one resume point (Progress.SaveResume), replacing the last. After a fall
// in that biome (or crossing), C on the crash screen starts again from it: the biome starts
// part way in (its first stretch begins behind you) with the items you held; a crossing starts
// at the beach as ever but the voyage picks up at that island (m_seaSkip).
internal sealed partial class SurfGame
{
	private sealed class CheckStone
	{
		public float Z;
		public int Items;
		public Segment Seg = null!;
		public GameObject? Go;
		public Light? Glow;
		public bool Lit;
		public float LitT;
	}

	private readonly List<CheckStone> m_checkStones = new();
	private readonly HashSet<Segment> m_halfPlanned = new();
	private Segment? m_itemsPlanned;
	private bool m_resumeNext;
	// How far into the crossing the voyage starts (an island's checkpoint).
	private float m_seaSkip;

	private static bool CheckpointsOn => Plugin.MidCheckpoints.Value;

	private bool CanResume(int biome, int stars, bool sea)
	{
		if (!CheckpointsOn || m_player == null || !Progress.Resume(m_player, out int b, out int s, out bool sh, out _, out _))
		{
			return false;
		}
		return b == biome && s == stars && sh == sea;
	}

	private void ResumeFromCheckpoint()
	{
		(int b, int s, bool sea) = CrashPlace();
		m_resumeNext = true;
		StartFrom(b, s, sea);
	}

	// In ResetRun, right after the stretches are laid out.
	private void ApplyResume()
	{
		m_seaSkip = 0f;
		m_checkStones.Clear();
		m_halfPlanned.Clear();
		m_itemsPlanned = null;
		bool want = m_resumeNext;
		m_resumeNext = false;
		if (!want || m_player == null || m_segments.Count == 0 || !Progress.Resume(m_player, out int biome, out int stars, out bool sea, out int items, out float along))
		{
			return;
		}
		Segment first = m_segments[0];
		if (sea)
		{
			if (!first.Shore || first.Stars != stars)
			{
				return;
			}
			m_seaSkip = Mathf.Max(0f, along);
			if (m_seaSkip >= Mathf.Max(200f, Plugin.OceanLength.Value))
			{
				m_seaBossBeaten = true;
			}
			Plugin.Log.LogInfo($"From the checkpoint: the crossing picks up {m_seaSkip:0} m out.");
			return;
		}
		if (first.Biome != biome || first.Stars != stars || first.Sea || first.Shore || first.Boss || first.Rush)
		{
			return;
		}
		first.Start = -along;
		if (first.End != float.MaxValue)
		{
			first.End -= along;
		}
		m_itemSeg = first;
		m_items = items;
		m_nextItemZ = 0f;
		// Its halfway stone is behind you already.
		m_halfPlanned.Add(first);
		if (items > 0 && BossFor(first) is BossDef boss && items >= boss.Amount)
		{
			m_itemsPlanned = first;
		}
		Plugin.Log.LogInfo($"From the checkpoint: {Progress.Describe(biome, stars)}, {along:0} m in, {items} item(s).");
	}

	private bool StoneStretch(Segment s) => !s.Boss && !s.Sea && !s.Shore && !s.Rush && !s.Repeat;

	// Each frame while running on land.
	private void UpdateCheckpoints(float dt)
	{
		if (!CheckpointsOn || m_segments.Count == 0)
		{
			return;
		}
		Segment here = SegAt(m_z);
		if (StoneStretch(here) && !m_halfPlanned.Contains(here))
		{
			// 1.16.109 (Sam): a biome's dungeon exit is its checkpoint, when it has one.
			float half = !float.IsNaN(CryptExitFor(here)) ? CryptExitFor(here) + 12f : here.Start + Plugin.BiomeLength.Value * 0.5f;
			if (half < m_z + 15f || here.End < half + 20f)
			{
				m_halfPlanned.Add(here);
			}
			else if (half < m_z + AheadDistance * 0.8f)
			{
				m_halfPlanned.Add(here);
				PlantStone(here, half, here == m_itemSeg ? m_items : 0);
			}
		}
		// All the items in hand: one more on the way to the boss.
		if (StoneStretch(here) && m_itemsPlanned != here && here == m_itemSeg && BossFor(here) is BossDef boss && m_items >= boss.Amount)
		{
			m_itemsPlanned = here;
			PlantStone(here, m_z + 28f, m_items);
		}
		for (int i = m_checkStones.Count - 1; i >= 0; i--)
		{
			CheckStone c = m_checkStones[i];
			if (c.Go == null)
			{
				m_checkStones.RemoveAt(i);
				continue;
			}
			if (!c.Lit && m_z >= c.Z && m_state == State.Running)
			{
				c.Lit = true;
				LightStone(c);
			}
			if (c.Lit && c.Glow != null)
			{
				c.LitT += dt;
				float burst = Mathf.Exp(-c.LitT * 2.5f);
				c.Glow.intensity = 1.4f + 4f * burst + 0.2f * Mathf.Sin(Time.time * 3f);
				c.Glow.range = 6f + 6f * burst;
			}
		}
	}

	private void PlantStone(Segment seg, float z, int items)
	{
		if (CryptCovers(z) || (m_crypt != null && z > m_crypt.Start - 10f && z < m_crypt.End + 10f))
		{
			z = (m_crypt?.End ?? z) + 12f;
		}
		GameObject? prefab = Pictures.Prefab("guard_stone") ?? Pictures.Prefab("piece_groundtorch_blue") ?? Pictures.Prefab("piece_groundtorch_wood");
		CheckStone c = new CheckStone { Z = z, Items = items, Seg = seg };
		int side = Random.value < 0.5f ? -1 : 1;
		float x = side * (TrackHalf + 1.6f + ExtraFlat(z));
		GameObject wrapper = new GameObject("Checkpoint") { layer = m_layer };
		wrapper.transform.SetParent(m_stage, false);
		wrapper.transform.localPosition = new Vector3(x, HillHeight(x, z) - 0.05f, z);
		wrapper.transform.localRotation = Quaternion.Euler(0f, side < 0 ? 90f : -90f, 0f);
		if (prefab != null)
		{
			GameObject stone = Instantiate(Picture(prefab), wrapper.transform, false);
			stone.transform.localPosition = Vector3.zero;
			stone.transform.localRotation = Quaternion.identity;
			stone.transform.localScale = Vector3.one * 1.25f;
		}
		GameObject glowGo = new GameObject("Glow") { layer = m_layer };
		glowGo.transform.SetParent(wrapper.transform, false);
		glowGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
		c.Glow = glowGo.AddComponent<Light>();
		c.Glow.type = LightType.Point;
		c.Glow.color = new Color(0.55f, 0.8f, 1f);
		c.Glow.range = 5f;
		c.Glow.intensity = 0.15f;
		c.Glow.shadows = LightShadows.None;
		c.Go = wrapper;
		m_checkStones.Add(c);
		m_things.Add(new Thing { Go = wrapper, Kind = Kind.Scenery, Z = z, HalfLength = 1f, Collidable = false });
	}

	// Passing it: it wakes with the rune stone's chime and a burst of light, and the place is saved.
	private void LightStone(CheckStone c)
	{
		if (m_player == null)
		{
			return;
		}
		int items = c.Seg == m_itemSeg ? m_items : c.Items;
		Progress.SaveResume(m_player, c.Seg.Biome, c.Seg.Stars, false, items, Mathf.Max(0f, c.Z - c.Seg.Start));
		m_audio.Play("sfx_runestone_activate|sfx_gui_select", 0.9f, 1f);
		Flash(new Color(0.55f, 0.8f, 1f), 0.3f);
		if (c.Go != null && FindFx("vfx_guardstone_activate|vfx_guardstone_permitted_add|vfx_Place_stone") is GameObject fx)
		{
			PlayFx(fx, c.Go.transform.localPosition + Vector3.up * 1.2f, Quaternion.identity, 3f);
		}
		Plugin.Log.LogInfo($"Checkpoint at {c.Z:0} m ({Progress.Describe(c.Seg.Biome, c.Seg.Stars)}, {items} item(s)).");
	}

	// At sea: mooring at an island (SurfHull.cs).
	private void IslandCheckpoint(Island island)
	{
		if (island.Saved || !CheckpointsOn || m_player == null || m_shipSeg == null || m_state != State.Running)
		{
			return;
		}
		island.Saved = true;
		if (Progress.Resume(m_player, out int b, out int s, out bool sea, out _, out float along)
			&& sea && b == SeaTo && s == m_shipSeg.Stars && Mathf.Abs(along - island.Along) < 1f)
		{
			return;
		}
		Progress.SaveResume(m_player, SeaTo, m_shipSeg.Stars, true, 0, island.Along);
		m_audio.Play("sfx_runestone_activate|sfx_gui_select", 0.8f, 1.05f);
		Flash(new Color(1f, 0.85f, 0.45f), 0.3f);
		Plugin.Log.LogInfo($"Checkpoint at the island {island.Along:0} m into the crossing.");
	}

	private void ClearResumePoint()
	{
		if (m_player != null)
		{
			Progress.ClearResume(m_player);
		}
	}
}
