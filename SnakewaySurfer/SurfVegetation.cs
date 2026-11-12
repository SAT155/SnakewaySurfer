using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SnakewaySurfer;

// 1.16.189 (SAT: the Black Forest was missing its dense trees, the Mistlands its cliffs, rocks,
// Yggdrasil shoots and giant roots): what lines the track is read from the game's own vegetation
// list (ZoneSystem.m_vegetation), the table the world generator plants each zone from. Every
// biome gets its real trees, bushes, rocks and cliffs in the game's own proportions, in the
// game's own clumps, and as thickly as the game plants them (up to what the minigame can carry).
internal sealed partial class SurfGame
{
	private sealed class VegEntry
	{
		public GameObject Prefab = null!;
		public float Weight;
		public float ScaleMin = 1f;
		public float ScaleMax = 1f;
		public int GroupMin = 1;
		public int GroupMax = 1;
		public float GroupRadius;
		public float GroundOffset;
		public float Tilt;
		// Its reach to the side, measured on its first copy (0: not measured yet).
		public float Reach;
	}

	// A zone is 64 m square; the track's verges are two bands this wide.
	private const float VergeNear = 3f;
	private const float VergeFar = 30f;
	private const float VegMaxPerMetre = 2.5f;

	private void LoadVegetation(BiomeRuntime rt, int index)
	{
		if (!Plugin.GameVegetation.Value || ZoneSystem.instance == null || index >= SpawnTables.s_order.Length)
		{
			return;
		}
		Heightmap.Biome biome = SpawnTables.s_order[index];
		float perZone = 0f;
		StringBuilder names = new StringBuilder();
		try
		{
			foreach (ZoneSystem.ZoneVegetation v in ZoneSystem.instance.m_vegetation)
			{
				if (v == null || !v.m_enable || v.m_prefab == null || (v.m_biome & biome) == 0)
				{
					continue;
				}
				// Not what grows under water or on the shoreline, and not what the track's own
				// pickables, spawners and ore already cover.
				if (v.m_maxAltitude < 2f || v.m_snapToWater)
				{
					continue;
				}
				GameObject prefab = v.m_prefab;
				string n = prefab.name;
				if (prefab.GetComponentInChildren<Pickable>(true) != null || prefab.GetComponentInChildren<SpawnArea>(true) != null
					|| prefab.GetComponentInChildren<CreatureSpawner>(true) != null || prefab.GetComponentInChildren<Character>(true) != null
					|| n.StartsWith("Leviathan") || n.Contains("Spawner"))
				{
					continue;
				}
				// m_max under 1 is a chance per zone; otherwise a count between min and max.
				float count = v.m_max < 1f ? v.m_max : (v.m_min + v.m_max) * 0.5f;
				float group = (Mathf.Max(1, v.m_groupSizeMin) + Mathf.Max(1, v.m_groupSizeMax)) * 0.5f;
				float weight = count * group;
				if (weight <= 0f)
				{
					continue;
				}
				VegEntry? have = rt.Veg.Find(e => e.Prefab == prefab);
				if (have != null)
				{
					have.Weight += weight;
				}
				else
				{
					rt.Veg.Add(new VegEntry
					{
						Prefab = prefab,
						Weight = weight,
						ScaleMin = Mathf.Max(0.1f, v.m_scaleMin),
						ScaleMax = Mathf.Max(0.1f, v.m_scaleMax),
						GroupMin = Mathf.Max(1, v.m_groupSizeMin),
						GroupMax = Mathf.Max(1, v.m_groupSizeMax),
						GroupRadius = v.m_groupRadius,
						GroundOffset = v.m_groundOffset,
						Tilt = v.m_randTilt,
					});
				}
				perZone += weight;
			}
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Couldn't read the game's vegetation for {rt.Def.Name}: {e.Message}");
			rt.Veg.Clear();
			return;
		}
		if (rt.Veg.Count < 2)
		{
			rt.Veg.Clear();
			return;
		}
		// What the picture tour counted in a real world sets the mix and the crowding, where it
		// counted enough to go by (SurfCensus.cs).
		float counted = 0f;
		if (Census.ByBiome.TryGetValue(biome.ToString(), out (string name, int count)[]? census) && ZNetScene.instance != null)
		{
			float all = 0f;
			foreach ((string _, int count) in census)
			{
				all += count;
			}
			if (all >= 60f)
			{
				HashSet<VegEntry> seen = new();
				foreach ((string name, int count) in census)
				{
					VegEntry? have = rt.Veg.Find(e => e.Prefab.name == name);
					if (have == null)
					{
						GameObject? prefab = ZNetScene.instance.GetPrefab(name);
						if (prefab == null || prefab.GetComponentInChildren<Pickable>(true) != null || prefab.GetComponentInChildren<Character>(true) != null
							|| prefab.GetComponentInChildren<SpawnArea>(true) != null || prefab.GetComponentInChildren<CreatureSpawner>(true) != null
							|| prefab.GetComponentInChildren<Renderer>(true) == null)
						{
							continue;
						}
						have = new VegEntry { Prefab = prefab };
						rt.Veg.Add(have);
					}
					have.Weight = count;
					seen.Add(have);
					counted += count;
				}
				// What the table lists but the count never met stays, as a rarity.
				perZone = counted;
				foreach (VegEntry e in rt.Veg)
				{
					if (!seen.Contains(e))
					{
						e.Weight = Mathf.Min(e.Weight, counted * 0.01f);
						perZone += e.Weight;
					}
				}
			}
		}
		rt.VegWeight = perZone;
		// The game's count per zone, over the two verges' share of a zone, per metre of track.
		// Not every spot the generator tries takes (slope, crowding), so two thirds of it.
		float real = perZone / (64f * 64f) * 2f * (VergeFar - VergeNear) * 0.66f;
		if (counted > 0f)
		{
			real = counted / Census.Area * 2f * (VergeFar - VergeNear);
		}
		rt.VegPerMetre = Mathf.Clamp(real, 0.3f, VegMaxPerMetre);
		rt.Veg.Sort((a, b) => b.Weight.CompareTo(a.Weight));
		foreach (VegEntry e in rt.Veg)
		{
			names.Append($"{e.Prefab.name} {e.Weight:0.#}, ");
		}
		Plugin.Log.LogInfo($"{rt.Def.Name} vegetation from the game{(counted > 0f ? " (mix and crowding as counted in a real world)" : "")}: {rt.Veg.Count} kinds, {perZone:0} {(counted > 0f ? "counted" : "a zone")}, {real:0.00} a metre of track (using {rt.VegPerMetre:0.00}): {names}");
	}

	private void SpawnVegetation(BiomeRuntime b, float z, int side)
	{
		float roll = Random.value * b.VegWeight;
		VegEntry pick = b.Veg[b.Veg.Count - 1];
		foreach (VegEntry e in b.Veg)
		{
			roll -= e.Weight;
			if (roll <= 0f)
			{
				pick = e;
				break;
			}
		}
		// One of its clump: the clump's weight is already in how often it is picked, so each
		// pick plants one, near the last of its kind when it grows in clumps.
		float x = side * (LaneWidth * 1.5f + Random.Range(VergeNear, VergeFar));
		// Thicker close to the track, where it is seen.
		if (Random.value < 0.35f)
		{
			x = side * (LaneWidth * 1.5f + Random.Range(VergeNear, VergeNear + 9f));
		}
		if (UnderSea(x, z) || InSight(x, z))
		{
			return;
		}
		GameObject go = Instantiate(Picture(pick.Prefab), m_stage, false);
		float scale = Random.Range(pick.ScaleMin, pick.ScaleMax);
		go.transform.localScale = go.transform.localScale * scale;
		go.transform.localRotation = Quaternion.Euler(Random.Range(-pick.Tilt, pick.Tilt), Random.Range(0f, 360f), Random.Range(-pick.Tilt, pick.Tilt));
		go.transform.localPosition = new Vector3(x, HillHeight(x, z) + pick.GroundOffset, z);
		// Cliffs and big rocks reach a long way: nothing leans over the lanes.
		if (Pictures.MeshBounds(go, out Bounds bounds))
		{
			float inner = (side < 0 ? bounds.max.x : bounds.min.x) - m_stage.position.x;
			float limit = side * (TrackHalf + 1.5f + ExtraFlat(z));
			float push = side < 0 ? Mathf.Min(0f, limit - inner) : Mathf.Max(0f, limit - inner);
			if (push != 0f)
			{
				Vector3 p = go.transform.localPosition;
				p.x += push;
				p.y = HillHeight(p.x, z) + pick.GroundOffset;
				go.transform.localPosition = p;
			}
			pick.Reach = Mathf.Max(pick.Reach, bounds.extents.z);
		}
		m_things.Add(new Thing { Go = go, Kind = Kind.Scenery, Z = z, HalfLength = Mathf.Max(2f, pick.Reach), Tree = true });
	}
}
