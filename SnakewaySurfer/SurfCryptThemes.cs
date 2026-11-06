using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.5: each biome's dungeon is the same run of halls and corners (SurfCrypt.cs) in its own
// stone, with its own dead. The Black Forest's burial chambers and the Swamp's sunken crypts,
// whose halls are built from the game's own sunken crypt kit (its walls and floors are parts of
// the dungeon's rooms, loaded from the game's dungeon list), half flooded, with leeches in the
// water, mud piles to jump, draugr piles that raise draugr, and iron gates that drop as you come
// (roll under them).
internal sealed partial class SurfGame
{
	private sealed class CryptTheme
	{
		public string Name = "";
		public string Biome = "";
		public Room.Theme Rooms;
		// '|' lists: the first that exists, as a game prefab or a part of the dungeon's rooms.
		public string Walls = "";
		public string Floors = "";
		public string Pillars = "";
		public float WallHeight = 2.4f;
		public string Monsters = "";
		public string Flyers = "";
		// Only in flooded halls.
		public string Swimmers = "";
		// What a pile raises, its model and its sound.
		public string Raised = "";
		public string Pile = "";
		public string RiseSound = "";
		public string RiseFx = "";
		public string Chest = "";
		// Fallen pillars (lying) or mud piles (standing) to jump.
		public string Low = "";
		public bool LowUpright;
		public string[] Decor = new string[0];
		public string GateTorch = "";
		public Color GateLight;
		public Color Ambient;
		public Color Fog;
		public string Idle = "";
		public string IdleRare = "";
		public float SconceGap = 13f;
		public float Flooded;
		public bool IronGates;
		// 1.16.6, a frost cave (SurfCave.cs): walls of rough rock, crystals glowing where the
		// crypts have torches, icicles falling from the roof, only flyers to slide under.
		public bool Rocky;
		public bool Crystals;
		public string Sconce = "piece_walltorch";
		public Color SconceColor = new Color(1f, 0.6f, 0.28f);
		public bool Icicles;
		public string Icicle = "";
		public string CrackSound = "";
		public string ShatterSound = "";
		public bool HighFlyersOnly;
		// 1.16.7, an infested mine (SurfMine.cs): what lies across the lanes to slide under, what
		// drops from the roof, egg sacs that burst as their brood comes out, and how far down the
		// raised come up from.
		public string High = "stone_pillar";
		public string Droppers = "";
		public float DropChance;
		public string DropSound = "";
		public string DropLandSound = "";
		public bool PileBurst;
		public string BurstFx = "";
		public string BurstSound = "";
		public float RiseDepth = 1.9f;
		// The tall pillar on the inside of a corner and at the gates, and the size of a light
		// standing at the foot of a wall.
		public string TallPillar = "stone_pillar_high|stone_pillar";
		public Vector3 SconceSize = new Vector3(0.9f, 1.15f, 0.9f);
	}

	private static readonly CryptTheme s_burialChamber = new CryptTheme
	{
		Name = "burial chamber",
		Biome = "Black Forest",
		// (Its walls are the game's own stone building pieces; no rooms to load.)
		Rooms = 0,
		Walls = "stone_wall_4x2|stone_wall_2x1",
		Floors = "stone_floor_2x2|stone_floor",
		Pillars = "stone_pillar",
		WallHeight = 2.4f,
		Monsters = "Skeleton:3:3, Skeleton_NoArcher:2:2, Skeleton_Poison:0.6",
		Flyers = "Ghost:1",
		Raised = "Skeleton:2, Skeleton_NoArcher:1",
		Pile = "BonePileSpawner|crypt_skeleton_pile01",
		RiseSound = "sfx_skeleton_rise|sfx_skeleton_alerted",
		RiseFx = "vfx_skeleton_rise|vfx_RockHit|vfx_Place_stone_floor",
		Chest = "TreasureChest_forestcrypt",
		Low = "stone_pillar",
		Decor = new[]
		{
			"crypt_skeleton_pile01", "crypt_skeleton_pile02", "crypt_skeleton_pile03", "crypt_skeleton_laying",
			"Pickable_ForestCryptRemains01", "Pickable_ForestCryptRemains02", "Pickable_ForestCryptRemains03", "Pickable_ForestCryptRemains04",
			"crypt_rock", "skull_pile", "crypt_skeleton_chest",
		},
		GateTorch = "piece_groundtorch_wood|piece_groundtorch",
		GateLight = new Color(1f, 0.62f, 0.3f),
		Ambient = new Color(0.1f, 0.1f, 0.12f),
		Fog = new Color(0.01f, 0.01f, 0.012f),
		Idle = "sfx_skeleton_idle|sfx_skeleton_basic_verse_idle",
		IdleRare = "sfx_ghost_idle|sfx_ghosts_idle",
	};

	private static readonly CryptTheme s_sunkenCrypt = new CryptTheme
	{
		Name = "sunken crypt",
		Biome = "Swamp",
		Rooms = Room.Theme.SunkenCrypt,
		Walls = "SunkenKit_int_wall_4x4|SunkenKit_int_wall_2x4|stone_wall_4x2",
		Floors = "SunkenKit_int_floor_4x4|SunkenKit_int_floor_2x2|stone_floor_2x2",
		Pillars = "stone_pillar",
		WallHeight = 3.2f,
		Monsters = "Draugr:3:2, Draugr_Ranged:1.6, Draugr_Elite:0.5, Blob:1.4:2, BlobElite:0.3",
		Swimmers = "Leech:1:2",
		Raised = "Draugr:2, Draugr_Ranged:1",
		Pile = "Spawner_DraugrPile|draugr_pile|mudpile2",
		RiseSound = "sfx_DraugrSpawn|sfx_draugr_alerted",
		RiseFx = "vfx_DraugrSpawn|vfx_spawn_draugr|vfx_Place_mud_road|vfx_RockHit",
		Chest = "TreasureChest_sunkencrypt",
		Low = "mudpile2|mudpile",
		LowUpright = true,
		Decor = new[]
		{
			"Pickable_SunkenCryptRandom", "mudpile2", "crypt_skeleton_pile01", "crypt_skeleton_laying", "crypt_rock",
			"skull_pile", "crypt_skeleton_pile02",
		},
		GateTorch = "piece_groundtorch_green|piece_groundtorch_wood",
		GateLight = new Color(0.45f, 1f, 0.5f),
		Ambient = new Color(0.08f, 0.11f, 0.09f),
		Fog = new Color(0.01f, 0.02f, 0.012f),
		Idle = "sfx_draugr_idle",
		IdleRare = "sfx_blob_idle",
		SconceGap = 19f,
		Flooded = 0.5f,
		IronGates = true,
	};

	// 1.16.6: the Mountains' frost cave (SurfCave.cs). Its walls and floors are the frost cave
	// rooms' own rock (caverock_*), with mountain rocks to fall back on.
	private static readonly CryptTheme s_frostCave = new CryptTheme
	{
		Name = "frost cave",
		Biome = "Mountains",
		Rooms = Room.Theme.Cave,
		Rocky = true,
		Walls = "caverock_rockbun|caverock_curvedrock|caverock_cornerwall|caverock_flowstone|rock1_mountain|rock2_mountain|rock3_mountain",
		Floors = "caverock_floorbig|caverock_floorsmall|Ice_floor|rock1_mountain",
		Pillars = "caverock_ice_stalagmite|caverock_stalagmite|Stalagmite",
		TallPillar = "caverock_ice_stalagmite|caverock_stalagmite|Stalagmite",
		WallHeight = 3f,
		Monsters = "Ulv:2.5:3, Fenring:1.2, Fenring_Cultist:1.2",
		Flyers = "Bat:2",
		Raised = "",
		Pile = "",
		Chest = "TreasureChest_mountaincave|TreasureChest_mountains",
		Low = "caverock_rockbun|ice_rock1|rock1_mountain",
		LowUpright = true,
		Decor = new[]
		{
			"Pickable_MountainCaveObsidian", "Pickable_MountainCaveRandom", "caverock_ice_stalagmite|Stalagmite", "caverock_stalagmite|Stalagmite",
			"ice_rock1", "crystal1|crystal_wall_1x1", "crystal3|crystal_wall_1x1",
		},
		GateTorch = "piece_groundtorch_blue|piece_groundtorch_wood",
		GateLight = new Color(0.55f, 0.75f, 1f),
		Ambient = new Color(0.07f, 0.09f, 0.13f),
		Fog = new Color(0.012f, 0.02f, 0.035f),
		Idle = "sfx_fenring_idle|sfx_bat_idle",
		IdleRare = "sfx_fenring_howl|sfx_bat_alerted",
		SconceGap = 10f,
		Crystals = true,
		Sconce = "Pickable_MountainCaveCrystal|crystal_wall_1x1|crystal1",
		SconceColor = new Color(0.45f, 0.75f, 1f),
		Icicles = true,
		Icicle = "caverock_ice_stalagtite_falling|caverock_ice_stalagtite|Stalagtite_falling|Stalagtite",
		CrackSound = "sfx_ice_hit|sfx_rock_destroyed",
		ShatterSound = "sfx_ice_destroyed|sfx_rock_hit",
		HighFlyersOnly = true,
	};

	// 1.16.7: the Mistlands' infested mine (SurfMine.cs): black marble and dvergr timber, lit by
	// their lanterns, overrun by seekers.
	private static readonly CryptTheme s_infestedMine = new CryptTheme
	{
		Name = "infested mine",
		Biome = "Mistlands",
		Rooms = Room.Theme.DvergerTown,
		Walls = "blackmarble_tile_wall_2x2|blackmarble_2x2x2|dvergrtown_wall_large|stone_wall_4x2",
		Floors = "blackmarble_tile_floor_2x2|blackmarble_floor|blackmarble_floor_large|stone_floor_2x2",
		Pillars = "blackmarble_column_1|blackmarble_column_2|stone_pillar",
		TallPillar = "blackmarble_column_3|blackmarble_column_2|stone_pillar_high",
		WallHeight = 3.2f,
		Monsters = "Seeker:3:2, SeekerBrute:0.7, Tick:1.5:3",
		Raised = "Seeker:2, Tick:1",
		Pile = "SeekerEgg|SeekerEgg_alwayshatch|SeekerAspic",
		PileBurst = true,
		BurstFx = "vfx_seekeregg_destroyed|vfx_egg_destroyed|vfx_RockHit",
		BurstSound = "sfx_seekeregg_destroyed|sfx_tick_hurt|sfx_seeker_hurt",
		RiseDepth = 0.6f,
		RiseSound = "sfx_seeker_alerted|sfx_tick_alerted",
		RiseFx = "vfx_seekerbrute_groundslam|vfx_RockHit",
		Chest = "TreasureChest_dvergrtown|TreasureChest_dvergr_loose_stone",
		Low = "dvergrprops_crate|dvergrcrate|dvergrprops_barrel",
		LowUpright = true,
		High = "dvergrprops_wood_beam|dvergrtown_wood_beam|stone_pillar",
		Decor = new[]
		{
			"dvergrprops_barrel", "dvergrprops_crate", "dvergrprops_pickaxe", "dvergrprops_hooknchain", "Pickable_DvergrMineTreasure",
			"Pickable_RoyalJelly", "blackmarble_pile", "dvergrprops_stool", "dvergrprops_crate_long",
		},
		GateTorch = "dvergrprops_lantern_standing|piece_groundtorch_wood",
		GateLight = new Color(1f, 0.82f, 0.55f),
		Ambient = new Color(0.08f, 0.07f, 0.1f),
		Fog = new Color(0.02f, 0.012f, 0.03f),
		Idle = "sfx_seeker_idle|sfx_tick_idle",
		IdleRare = "sfx_seeker_brute_idle|sfx_seeker_brute_mandible",
		SconceGap = 12f,
		Crystals = true,
		Sconce = "dvergrprops_lantern_standing|Pickable_DvergrLantern|dvergrprops_lantern",
		SconceColor = new Color(1f, 0.82f, 0.55f),
		SconceSize = new Vector3(0.8f, 1.9f, 0.8f),
		Droppers = "Tick:1",
		DropChance = 0.25f,
		DropSound = "sfx_gjall_tick_drop|sfx_tick_alerted",
		DropLandSound = "sfx_tick_attack_land|sfx_seeker_land",
	};

	private static CryptTheme? ThemeFor(string biome) =>
		biome == s_burialChamber.Biome ? s_burialChamber
		: biome == s_sunkenCrypt.Biome ? s_sunkenCrypt
		: biome == s_frostCave.Biome ? s_frostCave
		: biome == s_infestedMine.Biome ? s_infestedMine
		: null;

	private float WallH => m_crypt != null ? m_crypt.Theme.WallHeight : 2.4f;

	// ---------------------------------------------------------------- the dungeon's own pieces

	private readonly Dictionary<Room.Theme, bool> m_roomsLoaded = new();
	private readonly List<DungeonDB.RoomData> m_heldRooms = new();
	private readonly Dictionary<string, GameObject> m_kit = new();
	private readonly Dictionary<GameObject, float> m_longYaw = new();

	// The game keeps each dungeon's rooms as soft references; their walls and floors are parts of
	// them, not prefabs of their own. Load a theme's rooms once and index their parts by name.
	private void LoadRooms(Room.Theme theme)
	{
		if (m_roomsLoaded.ContainsKey(theme))
		{
			return;
		}
		m_roomsLoaded[theme] = true;
		int rooms = 0;
		StringBuilder names = new StringBuilder();
		HashSet<string> seen = new();
		try
		{
			foreach (DungeonDB.RoomData room in DungeonDB.GetRooms())
			{
				if (room == null || (room.m_theme & theme) == 0 || !room.m_prefab.IsValid)
				{
					continue;
				}
				room.m_prefab.Load();
				m_heldRooms.Add(room);
				GameObject? asset = room.m_prefab.Asset;
				if (asset == null)
				{
					continue;
				}
				rooms++;
				foreach (Transform t in asset.GetComponentsInChildren<Transform>(true))
				{
					string n = BaseName(t.name);
					if (m_kit.ContainsKey(n) || t.GetComponentInChildren<Renderer>(true) == null || t.GetComponent<Room>() != null)
					{
						continue;
					}
					m_kit[n] = t.gameObject;
					if (seen.Add(n) && seen.Count <= 80)
					{
						names.Append(n).Append(", ");
					}
				}
			}
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"Couldn't load the {theme} rooms: {e.Message}");
		}
		Plugin.Log.LogInfo($"{theme} rooms: {rooms} loaded; their parts: {names}");
	}

	// "SunkenKit_int_wall_2x4 (12)" -> "SunkenKit_int_wall_2x4".
	private static string BaseName(string name)
	{
		int i = name.IndexOf(" (", StringComparison.Ordinal);
		return i > 0 && name.EndsWith(")") ? name.Substring(0, i) : name;
	}

	private void ReleaseRooms()
	{
		foreach (DungeonDB.RoomData room in m_heldRooms)
		{
			try
			{
				if (room.m_prefab.IsValid)
				{
					room.m_prefab.Release();
				}
			}
			catch (Exception)
			{
			}
		}
		m_heldRooms.Clear();
		m_roomsLoaded.Clear();
		m_kit.Clear();
	}

	// A game prefab, else a part of the current dungeon's rooms.
	private GameObject? CryptTemplate(string name)
	{
		GameObject? prefab = Pictures.Prefab(name);
		if (prefab != null)
		{
			return prefab;
		}
		return m_kit.TryGetValue(name, out GameObject part) && part != null ? part : null;
	}

	// The turn that lays a wall piece's long side along x (some kits build theirs along z).
	private float LongYaw(GameObject template)
	{
		if (m_longYaw.TryGetValue(template, out float yaw))
		{
			return yaw;
		}
		yaw = 0f;
		GameObject probe = Instantiate(template, m_stage, false);
		probe.SetActive(true);
		probe.transform.localPosition = Vector3.zero;
		probe.transform.localRotation = Quaternion.identity;
		if (Pictures.MeshBounds(probe, out Bounds b) && b.size.z > b.size.x * 1.2f)
		{
			yaw = 90f;
		}
		Destroy(probe);
		m_longYaw[template] = yaw;
		return yaw;
	}

	// ---------------------------------------------------------------- flooded halls

	// Water over the floor of a flooded hall, as the swamp's own.
	private void FloodLeg(CryptLeg leg, Transform root, float z0, float z1, float y)
	{
		if (m_waterMaterial == null)
		{
			return;
		}
		int n = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / 4f));
		int nx = 5;
		Vector3[] verts = new Vector3[(n + 1) * nx];
		Vector2[] uvs = new Vector2[verts.Length];
		for (int iz = 0; iz <= n; iz++)
		{
			float z = Mathf.Lerp(z0, z1, iz / (float)n);
			for (int ix = 0; ix < nx; ix++)
			{
				float x = Mathf.Lerp(-CryptHalf, CryptHalf, ix / (float)(nx - 1));
				verts[iz * nx + ix] = new Vector3(x, y + WaterDepth, z);
				uvs[iz * nx + ix] = new Vector2(x / 10f, z / 10f);
			}
		}
		int[] tris = new int[n * (nx - 1) * 6];
		int k = 0;
		for (int iz = 0; iz < n; iz++)
		{
			for (int ix = 0; ix < nx - 1; ix++)
			{
				int a = iz * nx + ix;
				int b = a + 1;
				int c = a + nx;
				int d = c + 1;
				tris[k++] = a; tris[k++] = c; tris[k++] = d;
				tris[k++] = a; tris[k++] = d; tris[k++] = b;
			}
		}
		Mesh mesh = new Mesh { name = "CryptWater" };
		mesh.vertices = verts;
		mesh.uv = uvs;
		Color32[] colors = new Color32[verts.Length];
		Color32 swamp = Heightmap.GetBiomeColor(Heightmap.Biome.Swamp);
		for (int i = 0; i < colors.Length; i++)
		{
			colors[i] = swamp;
		}
		mesh.colors32 = colors;
		mesh.triangles = tris;
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();
		leg.Meshes.Add(mesh);
		GameObject water = new GameObject("CryptWater");
		water.layer = m_layer;
		water.transform.SetParent(root, false);
		water.AddComponent<MeshFilter>().sharedMesh = mesh;
		MeshRenderer r = water.AddComponent<MeshRenderer>();
		r.sharedMaterial = m_waterMaterial;
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
	}

	private bool CryptFlooded(float z)
	{
		if (m_crypt == null)
		{
			return false;
		}
		int k = 0;
		while (k < m_crypt.Corners.Count && z >= m_crypt.Corners[k])
		{
			k++;
		}
		// Just round a corner you're still on its square of floor, which belongs to the hall
		// before (as the water does).
		if (k > 0 && z < m_crypt.Corners[k - 1] + CryptHalf)
		{
			k--;
		}
		return m_crypt.Legs[k].Flooded;
	}

	// Wading through a flooded hall slows you down (easing in and out of the water).
	private float m_wade;

	private float CryptSpeed()
	{
		bool crypt = InCrypt(m_z);
		m_wade = Mathf.MoveTowards(m_wade, crypt && CryptFlooded(m_z) ? 1f : 0f, Time.deltaTime / 0.6f);
		return crypt ? 0.88f * Mathf.Lerp(1f, 0.82f, m_wade) : 1f;
	}

	// ---------------------------------------------------------------- iron gates

	private sealed class CryptIronGate
	{
		public float Z;
		public bool Built;
		public bool Dropped;
		public float Fall;
		public Transform? Grate;
		public readonly List<Thing> Lanes = new();
	}

	// An iron gate hanging up in the hall's roof, over all three lanes. As you come it crashes
	// down to a roll's height: roll under it. Blue marks on the floor show it, as for any slide.
	private void BuildIronGate(CryptIronGate gate, Transform root, float y)
	{
		gate.Built = true;
		GameObject holder = new GameObject("IronGate");
		holder.layer = m_layer;
		holder.transform.SetParent(root, false);
		// Up in the roof, its spikes hanging just over your head.
		holder.transform.localPosition = new Vector3(0f, y + GateUp, gate.Z);
		gate.Grate = holder.transform;
		float width = CryptHalf * 2f;
		float height = WallH - GateDown + 0.4f;
		int n = Mathf.Max(1, Mathf.RoundToInt(width / 2.2f));
		float piece = width / n;
		for (int i = 0; i < n; i++)
		{
			bool made = false;
			foreach (string name in "iron_grate|dungeon_sunkencrypt_irongate|iron_wall_2x2".Split('|'))
			{
				GameObject? template = CryptTemplate(name);
				if (template == null)
				{
					continue;
				}
				GameObject picture = Picture(template);
				GameObject bar = Fitted(picture, Quaternion.Euler(0f, LongYaw(picture), 0f), new Vector3(piece + 0.02f, height, 0.22f), false);
				bar.transform.SetParent(holder.transform, false);
				bar.transform.localPosition = new Vector3(-CryptHalf + piece * (i + 0.5f), 0f, 0f);
				made = true;
				break;
			}
			if (!made)
			{
				// (No iron pieces in this game version: plain dark bars.)
				for (float bx = 0.15f; bx < piece; bx += 0.3f)
				{
					GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cube);
					Destroy(rod.GetComponent<Collider>());
					rod.layer = m_layer;
					rod.transform.SetParent(holder.transform, false);
					rod.transform.localScale = new Vector3(0.07f, height, 0.07f);
					rod.transform.localPosition = new Vector3(-CryptHalf + piece * i + bx, height * 0.5f, 0f);
					rod.GetComponent<Renderer>().sharedMaterial = SolidTint(new Color(0.16f, 0.16f, 0.18f));
				}
			}
		}
		for (int lane = 0; lane < 3; lane++)
		{
			GameObject spot = new GameObject("IronGateLane");
			spot.layer = m_layer;
			spot.transform.SetParent(m_stage, false);
			spot.transform.localPosition = new Vector3(LaneX(lane), y, gate.Z);
			Thing t = new Thing
			{
				Go = spot,
				Kind = Kind.High,
				Lane = lane,
				Z = gate.Z,
				HalfLength = 0.3f,
				Collidable = false,
				CrashText = "An iron gate came down on you in a sunken crypt! (Roll under them as they drop.)",
			};
			m_things.Add(t);
			gate.Lanes.Add(t);
		}
	}

	private float GateUp => WallH - 0.4f;
	private const float GateDown = 1.15f;

	private void UpdateIronGates(float dt)
	{
		CryptRun c = m_crypt!;
		foreach (CryptIronGate gate in c.Gates)
		{
			if (!gate.Built || gate.Grate == null)
			{
				continue;
			}
			if (!gate.Dropped)
			{
				if (gate.Z - m_z < m_speed * 1.5f + 6f && gate.Z > m_z && !CornerBetween(m_z + 1f, gate.Z))
				{
					gate.Dropped = true;
					gate.Fall = 0f;
					m_audio.Play("sfx_gate_close|sfx_door_close|sfx_metal_blocked", 1f, 0.7f);
					foreach (Thing t in gate.Lanes)
					{
						t.Collidable = true;
						GameObject marker = MakeMarker(t.Lane, gate.Z, 2.2f, SlideMaterial);
						t.Markers.Add(marker);
					}
				}
				continue;
			}
			if (gate.Fall < 1f)
			{
				gate.Fall += dt / 0.45f;
				float k = Mathf.Clamp01(gate.Fall);
				// Falls faster and faster, and lands with a clang.
				Vector3 p = gate.Grate.localPosition;
				p.y = c.Floor + Mathf.Lerp(GateUp, GateDown, k * k);
				gate.Grate.localPosition = p;
				if (k >= 1f)
				{
					m_audio.Play("sfx_metal_blocked|sfx_HitMetal|sfx_wood_blocked", 0.9f, 0.6f);
					m_shake = Mathf.Max(m_shake, 0.12f);
				}
			}
		}
	}

	// Leeches in the flooded halls; the rest everywhere.
	private SpawnEntry CryptPick(float z, BiomeRuntime b)
	{
		if (m_crypt != null && CryptFlooded(z) && m_cryptSwimmers.TryGetValue(m_crypt.Theme.Name, out List<SpawnEntry> swim) && swim.Count > 0 && Random.value < 0.35f)
		{
			return SpawnTables.Pick(swim)!;
		}
		return SpawnTables.Pick(b.Monsters)!;
	}

	private readonly Dictionary<string, List<SpawnEntry>> m_cryptSwimmers = new();
	private readonly Dictionary<string, List<SpawnEntry>> m_cryptRaised = new();
	private readonly Dictionary<string, BiomeRuntime> m_cryptBiomes = new();
}
