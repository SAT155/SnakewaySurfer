using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.8: the Black Forest's other dungeon, a troll cave, taking turns with its burial
// chambers. Shorter than the others: rough mossy rock lit only by blue cave mushrooms and the torch
// in your hand, greydwarfs, and one troll waiting in the dark of its longest hall. As you come
// round into that hall it roars, the cave shakes, and rocks come down from the roof over your
// lane (move, or jump what lands). Then it comes for you.
internal sealed partial class SurfGame
{
	private int m_forestDungeons;

	private static readonly CryptTheme s_trollCave = new CryptTheme
	{
		Name = "troll cave",
		Biome = "Black Forest",
		Rocky = true,
		Walls = "rock4_forest|Rock_3|rock1_mountain",
		Floors = "rock4_forest|Rock_3|rock1_mountain",
		Pillars = "",
		TallPillar = "",
		WallHeight = 2.8f,
		MinLength = 120f,
		MaxLength = 200f,
		Monsters = "Greydwarf:2:3, Greydwarf_Elite:0.7, Greydwarf_Shaman:0.5",
		Raised = "",
		Pile = "",
		Lurker = "Troll",
		Chest = "TreasureChest_trollcave|TreasureChest_forestcrypt",
		Low = "rock4_forest|Rock_3|rock1_mountain",
		LowUpright = true,
		High = "FirTree_log_half|PineTree_log_half|stone_pillar",
		Decor = new[]
		{
			"Pickable_Mushroom_yellow", "Pickable_Mushroom", "Pickable_Stone", "crypt_skeleton_laying", "skull_pile", "Pickable_Mushroom_yellow",
		},
		GateTorch = "Pickable_Mushroom_blue|Pickable_Mushroom_yellow",
		GateLight = new Color(0.45f, 0.65f, 1f),
		Ambient = new Color(0.07f, 0.08f, 0.06f),
		Fog = new Color(0.012f, 0.016f, 0.01f),
		Idle = "sfx_troll_idle|sfx_greydwarf_idle",
		IdleRare = "sfx_greydwarf_idle|sfx_troll_idle",
		SconceGap = 11f,
		Crystals = true,
		Sconce = "Pickable_Mushroom_blue|Pickable_Mushroom_yellow",
		SconceColor = new Color(0.45f, 0.65f, 1f),
		SconceSize = new Vector3(0.6f, 0.5f, 0.6f),
		SconceIntensity = 0.95f,
		Icicles = true,
		FallChance = 0.55f,
		Icicle = "rock4_forest|Rock_3|rock1_mountain",
		FallText = "A rock came down from the roof of a troll cave! (Get out from under it, or jump it.)",
		CrackSound = "sfx_rock_wall_crumble|sfx_rocks",
		ShatterSound = "sfx_rock_destroyed|sfx_rock_hit",
		CrackFx = "vfx_RockHit",
		ShatterFx = "vfx_RockDestroyed|vfx_RockHit",
	};

	// The troll waits in the longest hall (not the way in or out when there's a choice), well clear
	// of its corners.
	private void PlanLurker(CryptRun c)
	{
		if (c.Theme.Lurker.Length == 0)
		{
			return;
		}
		CryptLeg? best = null;
		int last = c.Legs.Count - 1;
		foreach (CryptLeg leg in c.Legs)
		{
			bool inner = leg.Index > 0 && leg.Index < last;
			if (best == null || (inner && leg.B - leg.A >= 24f && (best.Index == 0 || best.Index == last || leg.B - leg.A > best.B - best.A)))
			{
				best = leg;
			}
		}
		if (best == null)
		{
			return;
		}
		float from = Mathf.Max(best.A + 12f, c.Start + 20f);
		float to = Mathf.Min(best.B - 14f, c.End - 20f);
		c.LurkerZ = to > from ? Mathf.Lerp(from, to, 0.5f) : (best.A + best.B) * 0.5f;
	}

	private bool m_lurkerRoared;
	private Thing? m_lurker;

	private void SpawnLurker(CryptRun c)
	{
		c.LurkerOut = true;
		List<string> missing = new();
		List<SpawnEntry> list = SpawnTables.Parse(c.Theme.Lurker + ":1", missing);
		if (list.Count == 0)
		{
			return;
		}
		m_lurker = SpawnCreature(list[0], 1, c.LurkerZ, Kind.Block, 0f, BiomeAt(c.LurkerZ));
		m_lurkerRoared = false;
		Plugin.Log.LogInfo($"A {list[0].Label} waits in the {c.Theme.Name} at {c.LurkerZ:0} m.");
	}

	// As you come round into its hall: a roar, the cave shaking, rocks coming down.
	private void UpdateLurker()
	{
		CryptRun c = m_crypt!;
		if (m_lurkerRoared || m_lurker == null || m_lurker.Go == null || float.IsNaN(c.LurkerZ))
		{
			return;
		}
		float ahead = m_lurker.Z - m_z;
		if (ahead <= 0f || ahead > 36f || CornerBetween(m_z + 1f, m_lurker.Z))
		{
			return;
		}
		m_lurkerRoared = true;
		m_audio.Play("sfx_troll_alerted|sfx_troll_idle", 1f, 0.9f);
		m_audio.Play("sfx_rock_wall_crumble|sfx_rocks", 0.7f, 0.8f);
		m_shake = Mathf.Max(m_shake, 0.35f);
		if (m_lurker.Anim != null)
		{
			SetTrigger(m_lurker.Anim, "taunt");
		}
		// Two rocks shaken loose between you and it, the first over your lane.
		float z = m_z + m_speed * 1.3f + 7f;
		for (int i = 0; i < 2 && z < m_lurker.Z - 6f; i++, z += Random.Range(6f, 9f))
		{
			int lane = i == 0 ? m_lane : Random.Range(0, 3);
			if (LaneClear(lane, z, null!))
			{
				SpawnIcicle(lane, z);
			}
		}
	}
}
