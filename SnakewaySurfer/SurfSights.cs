using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.11 (gap doc, points of interest): each biome's own places beside the track, the game's
// locations as they stand in the world: abandoned houses, stone circles and ruined towers in the
// Meadows; tower ruins, stone houses and greydwarf camps in the Black Forest; draugr huts and
// sunken ruins in the Swamp (with its crypt entrances and fire spouts); graves, drake nests and
// towers in the Mountains; standing stones, tar pits and fuling camps in the Plains; giant
// skeletons, dvergr towers and swords in stone in the Mistlands; charred ruins and morgen holes in
// the Ashlands. Some have one of their own waiting in the lane by them.
internal sealed partial class SurfGame
{
	// Per biome: groups of locations (the first one this game has), and who lives there.
	private static readonly Dictionary<string, (string places, string who)[]> s_sights = new()
	{
		["Meadows"] = new[]
		{
			("WoodHouse1|WoodHouse2|WoodHouse3|WoodHouse4|WoodHouse5|WoodHouse6|WoodHouse7|WoodHouse8|WoodHouse9|WoodHouse10|WoodHouse11|WoodHouse12|WoodHouse13", "Greyling"),
			("StoneTowerRuins04|StoneTowerRuins05", "Greyling"),
			("StoneCircle|Dolmen01|Dolmen02|Dolmen03|ShipSetting01", ""),
			("WoodFarm1|Runestone_Meadows|Runestone_Boars", "Boar"),
		},
		["Black Forest"] = new[]
		{
			("StoneTowerRuins03|StoneTowerRuins07|StoneTowerRuins08|StoneTowerRuins09|StoneTowerRuins10", "Skeleton"),
			("StoneHouse3|StoneHouse4|Ruin1|Ruin2", "Greydwarf"),
			("Greydwarf_camp1", "Greydwarf"),
			("Runestone_Greydwarfs|Runestone_BlackForest|Crypt2|Crypt3|Crypt4", "Skeleton"),
		},
		["Swamp"] = new[]
		{
			("SwampHut1|SwampHut2|SwampHut3|SwampHut4|SwampHut5", "Draugr"),
			("SwampRuin1|SwampRuin2|SwampWell1", "Draugr"),
			("StoneTowerRuins07_sunk|StoneTowerRuins08_sunk|StoneTowerRuins09_sunk|StoneTowerRuins10_sunk|Runestone_Swamps|Runestone_Draugr", "Blob"),
		},
		["Mountains"] = new[]
		{
			("MountainGrave01|MountainWell1", "Draugr"),
			("DrakeNest01", ""),
			("StoneTower1|StoneTower3|Waymarker01|Waymarker02|Runestone_Mountains", "Wolf"),
		},
		["Plains"] = new[]
		{
			("StoneHenge1|StoneHenge2|StoneHenge3|StoneHenge4|StoneHenge5|StoneHenge6", ""),
			("GoblinCamp2", "Goblin"),
			("TarPit1|TarPit2|TarPit3", "BlobTar"),
			("Ruin3|Runestone_Plains", "Goblin"),
		},
		["Mistlands"] = new[]
		{
			("Mistlands_Giant1|Mistlands_Giant2", "Seeker"),
			("Mistlands_GuardTower1_new|Mistlands_GuardTower2_new|Mistlands_GuardTower3_new|Mistlands_GuardTower1_ruined_new|Mistlands_GuardTower3_ruined_new", "Seeker"),
			("Mistlands_Swords1|Mistlands_Swords2|Mistlands_Swords3|Mistlands_Statue1|Mistlands_Statue2|Mistlands_StatueGroup1", ""),
			("Mistlands_Excavation1|Mistlands_Excavation2|Mistlands_Excavation3|Mistlands_RoadPost1|Runestone_Mistlands", "Tick"),
		},
		["Ashlands"] = new[]
		{
			("CharredRuins1|CharredRuins2|CharredRuins3|CharredRuins4|CharredRuins9|CharredRuins12|CharredRuins13", "Charred_Melee"),
			("CharredTowerRuins1|CharredTowerRuins2|CharredTowerRuins3", "Charred_Archer"),
			("MorgenHole1|MorgenHole2|MorgenHole3", "Morgen"),
			("Runestone_Ashlands", ""),
		},
	};

	// The group shown last in each biome, so the next one is something else.
	private readonly Dictionary<string, int> m_lastSight = new();
	// Where places stand (in stage coordinates), kept clear of the trees still to come.
	private readonly List<Bounds> m_sightAreas = new();

	private bool InSight(float x, float z)
	{
		for (int i = m_sightAreas.Count - 1; i >= 0; i--)
		{
			Bounds b = m_sightAreas[i];
			if (b.max.z < m_z - 60f)
			{
				m_sightAreas.RemoveAt(i);
				continue;
			}
			if (x > b.min.x - 2f && x < b.max.x + 2f && z > b.min.z - 2f && z < b.max.z + 2f)
			{
				return true;
			}
		}
		return false;
	}

	private bool HasSights(string biome) => Plugin.Places.Value && s_sights.ContainsKey(biome);

	// One of the biome's places, back from the track's edge, with the trees it stands among
	// cleared away.
	private bool SpawnSight(float z, int side, int biomeIndex)
	{
		string biome = m_biomes[biomeIndex].Def.Name;
		if (!s_sights.TryGetValue(biome, out (string places, string who)[] groups) || groups.Length == 0)
		{
			return false;
		}
		int last = m_lastSight.TryGetValue(biome, out int l) ? l : -1;
		int first = Random.Range(0, groups.Length);
		if (first == last && groups.Length > 1)
		{
			first = (first + 1) % groups.Length;
		}
		for (int g = 0; g < groups.Length; g++)
		{
			int gi = (first + g) % groups.Length;
			string[] names = groups[gi].places.Split('|');
			int start = Random.Range(0, names.Length);
			for (int k = 0; k < names.Length; k++)
			{
				GameObject? template = LocationPicture(names[(start + k) % names.Length]);
				if (template == null || !PlaceSight(template, z, side))
				{
					continue;
				}
				m_lastSight[biome] = gi;
				SightKeeper(groups[gi].who, z, side, biomeIndex);
				return true;
			}
		}
		return false;
	}

	private bool PlaceSight(GameObject template, float z, int side)
	{
		float edge = TrackHalf + ExtraFlat(z);
		float x = side * (edge + 12f);
		if (UnderSea(x, z))
		{
			return false;
		}
		GameObject go = Instantiate(template, m_stage, false);
		go.SetActive(true);
		go.transform.localRotation = Quaternion.Euler(0f, (side < 0 ? 90f : -90f) + Random.Range(-25f, 25f), 0f);
		go.transform.localScale = Vector3.one;
		go.transform.localPosition = new Vector3(x, HillHeight(x, z) - 0.3f, z);
		foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
		{
			// Anything huge (a water plane, a ground decal, a mist volume) would cover the track.
			if (r.bounds.size.x > 45f || r.bounds.size.z > 45f)
			{
				r.enabled = false;
			}
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		}
		float half = 6f;
		if (Pictures.MeshBounds(go, out Bounds bounds))
		{
			// Its near side a few metres back from the track's edge.
			float inner = (side < 0 ? bounds.max.x : bounds.min.x) - m_stage.position.x;
			float limit = side * (edge + 3.5f);
			float push = side < 0 ? Mathf.Min(0f, limit - inner) : Mathf.Max(0f, limit - inner);
			Vector3 p = go.transform.localPosition;
			p.x += push;
			p.y = HillHeight(p.x, z) - 0.3f;
			go.transform.localPosition = p;
			bounds.center += new Vector3(push, 0f, 0f);
			half = Mathf.Clamp(bounds.size.z * 0.5f, 4f, 30f);
			ClearTrees(bounds);
			m_sightAreas.Add(new Bounds(bounds.center - m_stage.position, bounds.size));
		}
		LightUp(go);
		m_things.Add(new Thing { Go = go, Kind = Kind.Scenery, Z = z, HalfLength = half });
		return true;
	}

	// The trees already standing where a place now is.
	private void ClearTrees(Bounds world)
	{
		world.Expand(new Vector3(2f, 100f, 2f));
		for (int i = m_things.Count - 1; i >= 0; i--)
		{
			Thing t = m_things[i];
			if (!t.Tree || t.Go == null || !world.Contains(t.Go.transform.position))
			{
				continue;
			}
			DestroyThing(t);
			m_things.RemoveAt(i);
		}
	}

	// One of the place's own, waiting in the lane by it (only where the lanes there are clear,
	// and the rows there are already laid out).
	private void SightKeeper(string who, float z, int side, int biomeIndex)
	{
		if (who.Length == 0 || Random.value > 0.7f)
		{
			return;
		}
		List<SpawnEntry> found = m_biomes[biomeIndex].Monsters.FindAll(e => e.Prefab != null && e.Prefab.name.StartsWith(who, System.StringComparison.OrdinalIgnoreCase));
		if (found.Count == 0)
		{
			// 1.16.14 (gap doc, missing creatures): ones that only live at their places, not in the
			// biome's spawn tables (growths at the tar pits, draugr at the mountain graves).
			GameObject? prefab = Pictures.Monster(who);
			if (prefab == null)
			{
				return;
			}
			found.Add(new SpawnEntry { Prefab = prefab, Label = SpawnTables.Label(prefab) });
		}
		int lane = side < 0 ? 0 : 2;
		float cz = Mathf.Min(z - Random.Range(3f, 8f), m_nextRowZ - 3f);
		if (cz < z - 14f || cz < m_z + 30f || InCrypt(cz) || NearTrader(cz, TraderClear) || !LaneClear(lane, cz, null!))
		{
			return;
		}
		SpawnCreature(found[Random.Range(0, found.Count)], lane, cz, Kind.Block, 0f, biomeIndex, "waiter");
	}
}
