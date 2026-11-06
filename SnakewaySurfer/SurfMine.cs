using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.7: the Mistlands' dungeon, an infested mine. The same halls and corners as the crypts
// (SurfCrypt.cs), in the dvergr's black marble, lit by their lanterns and overrun: seekers and
// their soldiers in the halls, egg sacs that burst into seekers as you come, and ticks that drop
// from the roof into your lane (a red mark on the floor as one lets go: get out from under it, or
// jump it when it lands).
internal sealed partial class SurfGame
{
	private sealed class MineDrop
	{
		public int Lane;
		public float Z;
	}

	private readonly List<MineDrop> m_drops = new();
	private readonly List<(Thing t, float time)> m_dropping = new();
	private readonly Dictionary<string, List<SpawnEntry>> m_cryptDroppers = new();
	private const float DropHeight = 4.5f;
	private const float DropTime = 0.45f;

	// Something up in the roof over this lane, waiting for you.
	private bool PlanDrop(int lane, float z)
	{
		if (m_crypt == null || !m_cryptDroppers.TryGetValue(m_crypt.Theme.Name, out List<SpawnEntry> droppers) || droppers.Count == 0)
		{
			return false;
		}
		m_drops.Add(new MineDrop { Lane = lane, Z = z });
		return true;
	}

	private void UpdateDrops(float dt)
	{
		CryptRun c = m_crypt!;
		for (int i = m_drops.Count - 1; i >= 0; i--)
		{
			MineDrop d = m_drops[i];
			if (d.Z < m_z - 5f)
			{
				m_drops.RemoveAt(i);
				continue;
			}
			if (d.Z <= m_z || d.Z - m_z > m_speed * 1.2f + 5f || CornerBetween(m_z + 1f, d.Z))
			{
				continue;
			}
			m_drops.RemoveAt(i);
			if (!m_cryptDroppers.TryGetValue(c.Theme.Name, out List<SpawnEntry> droppers) || droppers.Count == 0)
			{
				continue;
			}
			// It lets go over the lane you're in, if nothing else is there.
			int lane = d.Lane;
			if (lane != m_lane && LaneClear(m_lane, d.Z, null!))
			{
				lane = m_lane;
			}
			SpawnEntry e = SpawnTables.Pick(droppers)!;
			Thing t = SpawnCreature(e, lane, d.Z, Kind.Block, 0f, BiomeAt(d.Z), "waiter");
			t.BaseY = DropHeight;
			t.Collidable = false;
			t.Cooldown = Mathf.Max(t.Cooldown, 0.9f);
			Vector3 p = t.Go.transform.localPosition;
			t.Go.transform.localPosition = new Vector3(p.x, RoadY(d.Z) + t.BaseY, d.Z);
			t.Markers.Add(MakeMarker(lane, d.Z, 1.8f, m_markerMaterial));
			m_dropping.Add((t, 0f));
			m_audio.Play(c.Theme.DropSound, 0.9f, Random.Range(0.95f, 1.15f));
		}
		for (int i = m_dropping.Count - 1; i >= 0; i--)
		{
			(Thing t, float time) = m_dropping[i];
			if (t.Go == null)
			{
				m_dropping.RemoveAt(i);
				continue;
			}
			time += dt;
			float k = Mathf.Clamp01(time / DropTime);
			t.BaseY = Mathf.Lerp(DropHeight, 0f, k * k);
			Vector3 p = t.Go.transform.localPosition;
			t.Go.transform.localPosition = new Vector3(p.x, RoadY(t.Z) + t.BaseY, t.Z);
			if (k >= 1f)
			{
				t.Collidable = true;
				m_audio.Play(c.Theme.DropLandSound, 0.8f, Random.Range(0.9f, 1.1f));
				GameObject? dust = FindFx("vfx_tick_hit|vfx_RockHit|vfx_Place_stone_floor");
				if (dust != null)
				{
					PlayFx(dust, new Vector3(p.x, RoadY(t.Z) + 0.1f, t.Z), Quaternion.identity, 2f);
				}
				m_dropping.RemoveAt(i);
			}
			else
			{
				m_dropping[i] = (t, time);
			}
		}
	}

	private void ClearDrops()
	{
		m_drops.Clear();
		m_dropping.Clear();
	}
}
