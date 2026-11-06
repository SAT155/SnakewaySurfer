using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.4 (Sam: "The burial chamber dungeon could include like 90 degree turns or something like
// moving around tight corners in the dark hallway. with lots of skeletons in a hallway and such.
// Maybe a ghost or skeleton spawner every now and then. Perhaps the dungeons could all be
// top-down. Similar to the sailing one. A torch would definitely be used in the burial
// chambers"): once in each stretch of the Black Forest the track runs into a burial chamber.
// A stone gate with two torches outside, then dark stone hallways seen from above, lit by the
// torch you carry and a few sconces. The hallways turn 90 degrees: press A or D toward the
// opening as you reach a corner (the camera starts to swing round and a torch burns on the
// wall ahead); miss it and you run into the wall. Skeletons crowd the halls, archers among
// them, bone piles raise more as you come, ghosts drift in through the walls and old chests
// lie about (ten coins each).
//
// The track itself stays straight (z goes on counting along it): each hallway between two
// corners has its own frame, turned about the corner, and everything on the track is put in
// the frame of the hallway its z is in, so the code that places things along the lanes works
// unchanged. After the exit the last frame becomes the stage again.
internal sealed partial class SurfGame
{
	private const float CryptHalf = TrackHalf + 1.2f;
	private const float CryptWall = 0.6f;
	private const float CryptWallHeight = 2.4f;
	// How far before a corner the turn can be pressed, and where you wait if you haven't.
	private const float TurnWindow = 10f;
	private const float TurnPoint = 0.25f;
	private const float BonkTime = 0.7f;

	private sealed class CryptLeg
	{
		public float A;
		public float B;
		public int Index;
		public GameObject? Root;
		public bool Built;
	}

	private sealed class CryptPile
	{
		public float Z;
		public int Side;
		public bool Woken;
	}

	private sealed class CryptRun
	{
		public float Start;
		public float End;
		public float Floor;
		public readonly List<float> Corners = new();
		// +1 turns right, -1 left.
		public readonly List<int> Dirs = new();
		public readonly List<bool> Armed = new();
		// Frames[0] is the stage; Frames[k] holds the hallway after corner k - 1.
		public readonly List<Transform> Frames = new();
		public readonly List<CryptLeg> Legs = new();
		public readonly List<CryptPile> Piles = new();
		public readonly List<(Light light, float z)> Glows = new();
		public float NextSound = 3f;
	}

	private CryptRun? m_crypt;
	private readonly HashSet<Transform> m_cryptPinned = new();
	private readonly List<Transform> m_cryptMove = new();
	private float m_cryptView;
	private float m_cryptDark;
	private Vector3 m_cryptFocus;
	private bool m_cryptFocusSet;
	// Smooths the avatar over a change of hallway frame.
	private Vector3 m_cryptLagPos;
	private float m_cryptLagYaw;
	private Vector3 m_avatarDrawnPos;
	private Quaternion m_avatarDrawnRot = Quaternion.identity;
	private bool m_avatarDrawn;
	// Running on past a missed corner toward its wall, and the bounce back off it.
	private float m_cryptOver;
	private float m_cryptBonk = -1f;
	private Light? m_torchLight;
	private bool m_torchHeld;
	private BiomeRuntime? m_cryptBiome;
	private CameraClearFlags m_mainClear;
	private bool m_cryptClear;
	private float m_sconceYaw = float.NaN;
	private readonly List<(Thing t, float time)> m_rising = new();

	// ---------------------------------------------------------------- where the hallways are

	private bool InCrypt(float z) => m_crypt != null && z >= m_crypt.Start && z <= m_crypt.End;

	// The stretch of track the crypt takes over (no forest ground, trees or pickables).
	private bool CryptCovers(float z) => m_crypt != null && z > m_crypt.Start - 3f && z < m_crypt.End + 3f;

	private Transform CryptFrameAt(float z)
	{
		if (m_crypt == null)
		{
			return m_stage;
		}
		int k = 0;
		while (k < m_crypt.Corners.Count && z >= m_crypt.Corners[k])
		{
			k++;
		}
		return m_crypt.Frames[k];
	}

	// A point in track space (lanes across, z along) in the world.
	private Vector3 StagePoint(Vector3 local) => CryptFrameAt(local.z).TransformPoint(local);

	private Matrix4x4 StageMatrix(float z) => CryptFrameAt(z).localToWorldMatrix;

	private bool CornerBetween(float from, float to)
	{
		if (m_crypt == null)
		{
			return false;
		}
		foreach (float c in m_crypt.Corners)
		{
			if (c > from && c < to)
			{
				return true;
			}
		}
		return false;
	}

	// Round a corner from you: it can't see you yet, and waits.
	private bool CryptHidden(Thing t) => m_crypt != null && t.Z > m_z && CornerBetween(m_z + 1f, t.Z);

	// No rows by the entrance and exit or around a corner (the turn wants the lanes clear).
	private bool CryptNoRow(float z)
	{
		if (m_crypt == null)
		{
			return false;
		}
		if (z < m_crypt.Start + 14f || z > m_crypt.End - 10f)
		{
			return true;
		}
		foreach (float c in m_crypt.Corners)
		{
			if (z > c - 16f && z < c + 9f)
			{
				return true;
			}
		}
		return false;
	}

	// The crypt's floor is flat; the road eases down (or up) to it outside the gates.
	private float CryptRoad(float raw, float z)
	{
		if (m_crypt == null)
		{
			return raw;
		}
		float outside = Mathf.Max(0f, Mathf.Max(m_crypt.Start - z, z - m_crypt.End));
		float w = Mathf.SmoothStep(0f, 1f, 1f - outside / 20f);
		return Mathf.Lerp(raw, m_crypt.Floor, w);
	}

	private int NextCorner()
	{
		if (m_crypt == null)
		{
			return -1;
		}
		for (int k = 0; k < m_crypt.Corners.Count; k++)
		{
			if (m_crypt.Corners[k] > m_z)
			{
				return k;
			}
		}
		return -1;
	}

	// ---------------------------------------------------------------- planning

	// Once per stretch of Black Forest (not its boss arena), well into it: start, length and the
	// corners, before the ground there is laid.
	private void PlanCrypt()
	{
		if (m_crypt != null || !Plugin.BurialChambers.Value || m_biomes.Count < 2)
		{
			return;
		}
		foreach (Segment s in m_segments)
		{
			if (s.CryptChecked)
			{
				continue;
			}
			s.CryptChecked = true;
			if (s.Boss || s.Sea || s.Shore || s.Rush || s.Biome >= m_biomes.Count || m_biomes[s.Biome].Def.Name != "Black Forest")
			{
				continue;
			}
			float len = Plugin.BiomeLength.Value;
			float length = Mathf.Clamp(Mathf.Round(len * 0.45f / GroundTile) * GroundTile, 160f, 320f);
			float start = Mathf.Ceil((s.Start + Mathf.Max(120f, len * 0.25f)) / GroundTile) * GroundTile;
			// Ground already laid stays as it is, so it starts past it (and the tile before eases down).
			start = Mathf.Max(start, Mathf.Ceil((m_nextGroundZ + GroundTile) / GroundTile) * GroundTile);
			// Well before the stretch ends (with its boss ahead, before the summoning items can end it).
			float limit = (s.End < float.MaxValue ? s.End : s.Start + len) - 80f;
			if (start + length > limit)
			{
				length = Mathf.Floor((limit - start) / GroundTile) * GroundTile;
				if (length < 120f)
				{
					continue;
				}
			}
			MakeCrypt(start, start + length);
			return;
		}
	}

	private void MakeCrypt(float start, float end)
	{
		CryptRun c = new CryptRun { Start = start, End = end, Floor = RoadRaw(start) };
		// Out to one side and back, a few times: the way on always ends up heading on down the
		// track, and the hallways never cross. A long first and last hallway keep the forest
		// outside well away from the sideways ones.
		float z = start + Random.Range(32f, 38f);
		int net = 0;
		while (true)
		{
			float side = Random.Range(14f, 26f);
			if (z + side + 30f > end)
			{
				break;
			}
			int d = net != 0 && Random.value < 0.75f ? -System.Math.Sign(net) : (Random.value < 0.5f ? -1 : 1);
			c.Corners.Add(z);
			c.Dirs.Add(d);
			c.Corners.Add(z + side);
			c.Dirs.Add(-d);
			net += d;
			z += side;
			float north = Random.Range(16f, 30f);
			if (z + north + 14f + 30f > end)
			{
				break;
			}
			z += north;
		}

		c.Frames.Add(m_stage);
		Vector3 p = Vector3.zero;
		Quaternion q = Quaternion.identity;
		for (int i = 0; i < c.Corners.Count; i++)
		{
			// Turned about the corner's middle: (0, 0, z) is the same point in both frames.
			Vector3 pivot = new Vector3(0f, 0f, c.Corners[i]);
			Quaternion turn = Quaternion.Euler(0f, c.Dirs[i] * 90f, 0f);
			p += q * (pivot - turn * pivot);
			q *= turn;
			Transform frame = new GameObject("CryptHall" + (i + 1)).transform;
			frame.gameObject.layer = m_layer;
			frame.SetParent(m_stage, false);
			frame.localPosition = p;
			frame.localRotation = q;
			c.Frames.Add(frame);
			c.Armed.Add(false);
			m_cryptPinned.Add(frame);
		}
		for (int k = 0; k <= c.Corners.Count; k++)
		{
			float a = k == 0 ? start : c.Corners[k - 1];
			float b = k == c.Corners.Count ? end : c.Corners[k];
			c.Legs.Add(new CryptLeg { A = a, B = b, Index = k });
		}
		m_crypt = c;
		// A bone pile in some of the longer hallways, which raises skeletons as you come.
		foreach (CryptLeg leg in c.Legs)
		{
			if (leg.B - leg.A < 28f || Random.value > 0.65f)
			{
				continue;
			}
			for (int tries = 0; tries < 6; tries++)
			{
				float pz = Random.Range(leg.A + 8f, leg.B - 18f);
				if (!CryptNoRow(pz))
				{
					c.Piles.Add(new CryptPile { Z = pz, Side = Random.value < 0.5f ? -1 : 1 });
					break;
				}
			}
		}
		CryptBiome();
		Plugin.Log.LogInfo($"Burial chamber from {start:0} to {end:0} m: corners at {string.Join(", ", c.Corners.ConvertAll(x => x.ToString("0")))} ({string.Join("", c.Dirs.ConvertAll(x => x > 0 ? "R" : "L"))}), {c.Piles.Count} bone piles.");
	}

	// Who's in the crypt: skeletons (archers among them), the rotten ones now and then, and
	// ghosts. Fallen pillars to jump and beams to slide under.
	private BiomeRuntime CryptBiome()
	{
		if (m_cryptBiome != null)
		{
			return m_cryptBiome;
		}
		BiomeRuntime forest = m_biomes[Mathf.Min(1, m_biomes.Count - 1)];
		BiomeRuntime rt = new BiomeRuntime { Def = forest.Def, Sky = Color.black, Ground = forest.Ground, Fog = 0.028f, FogColor = Color.black };
		List<string> missing = new();
		foreach (SpawnEntry e in SpawnTables.Parse("Skeleton:3:3, Skeleton_NoArcher:2:2, Skeleton_Poison:0.6", missing))
		{
			e.Flying = false;
			rt.Monsters.Add(e);
		}
		foreach (SpawnEntry e in SpawnTables.Parse("Ghost:1", missing))
		{
			e.Flying = true;
			rt.Flyers.Add(e);
		}
		if (rt.Monsters.Count == 0)
		{
			rt.Monsters.AddRange(forest.Monsters);
		}
		GameObject? pillar = Pictures.Prefab("stone_pillar");
		if (pillar != null)
		{
			rt.Logs.Add(pillar);
		}
		else
		{
			rt.Logs.AddRange(forest.Logs);
		}
		if (missing.Count > 0)
		{
			Plugin.Log.LogInfo("Burial chamber: not in this game version: " + string.Join(", ", missing));
		}
		m_cryptBiome = rt;
		return rt;
	}

	// ---------------------------------------------------------------- building the halls

	private GameObject? CryptPiece(Transform root, string names, Quaternion rotation, Vector3 size, Vector3 at, bool uniform = false, float maxScale = float.MaxValue)
	{
		foreach (string name in names.Split('|'))
		{
			GameObject? prefab = Pictures.Prefab(name);
			if (prefab == null)
			{
				continue;
			}
			GameObject go = Fitted(Picture(prefab), rotation, size, uniform, maxScale);
			go.transform.SetParent(root, false);
			go.transform.localPosition = at;
			return go;
		}
		return null;
	}

	// A stone wall along the hall (x fixed) from z0 to z1, rows high.
	private void WallAlong(Transform root, float x, float z0, float z1, float y, int rows = 1)
	{
		float length = z1 - z0;
		if (length < 0.2f)
		{
			return;
		}
		int n = Mathf.Max(1, Mathf.RoundToInt(length / 4f));
		float piece = length / n;
		for (int r = 0; r < rows; r++)
		{
			for (int i = 0; i < n; i++)
			{
				CryptPiece(root, "stone_wall_4x2|stone_wall_2x1", Quaternion.Euler(0f, 90f, 0f), new Vector3(CryptWall, CryptWallHeight, piece + 0.03f),
					new Vector3(x, y + r * CryptWallHeight, z0 + piece * (i + 0.5f)));
			}
		}
	}

	// A stone wall across the hall (z fixed) from x0 to x1.
	private void WallAcross(Transform root, float z, float x0, float x1, float y, int rows = 1, float lift = 0f)
	{
		float length = x1 - x0;
		if (length < 0.2f)
		{
			return;
		}
		int n = Mathf.Max(1, Mathf.RoundToInt(length / 4f));
		float piece = length / n;
		for (int r = 0; r < rows; r++)
		{
			for (int i = 0; i < n; i++)
			{
				CryptPiece(root, "stone_wall_4x2|stone_wall_2x1", Quaternion.identity, new Vector3(piece + 0.03f, CryptWallHeight, CryptWall),
					new Vector3(x0 + piece * (i + 0.5f), y + lift + r * CryptWallHeight, z));
			}
		}
	}

	// Which way a wall torch's model sticks out from its bracket (its picture's middle is off its
	// pivot that way), as a turn that points it down +z.
	private float SconceYaw(GameObject template)
	{
		if (!float.IsNaN(m_sconceYaw))
		{
			return m_sconceYaw;
		}
		m_sconceYaw = 0f;
		GameObject probe = Instantiate(template, m_stage, false);
		probe.SetActive(true);
		probe.transform.localPosition = Vector3.zero;
		probe.transform.localRotation = Quaternion.identity;
		if (Pictures.MeshBounds(probe, out Bounds b))
		{
			Vector3 o = m_stage.InverseTransformPoint(b.center);
			if (Mathf.Abs(o.x) > Mathf.Abs(o.z))
			{
				m_sconceYaw = o.x > 0f ? -90f : 90f;
			}
			else
			{
				m_sconceYaw = o.z < 0f ? 180f : 0f;
			}
		}
		Destroy(probe);
		return m_sconceYaw;
	}

	// A burning torch on a wall, facing `yaw` (0 = down the hall), with a light that's only on
	// near you.
	private void Sconce(Transform root, Vector3 at, float yaw, float range = 8f)
	{
		GameObject? prefab = Pictures.Prefab("piece_walltorch");
		GameObject? go = null;
		if (prefab != null)
		{
			GameObject template = Picture(prefab);
			go = Instantiate(template, root, false);
			go.transform.localPosition = at;
			go.transform.localRotation = Quaternion.Euler(0f, yaw + SconceYaw(template), 0f);
			LightUp(go);
			foreach (Light own in go.GetComponentsInChildren<Light>(true))
			{
				own.enabled = false;
			}
		}
		Light glow = new GameObject("CryptGlow").AddComponent<Light>();
		glow.gameObject.layer = m_layer;
		glow.transform.SetParent(root, false);
		glow.transform.localPosition = at + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0.35f, 0.45f);
		glow.type = LightType.Point;
		glow.range = range;
		glow.intensity = 1.35f;
		glow.color = new Color(1f, 0.6f, 0.28f);
		glow.shadows = LightShadows.None;
		glow.cullingMask = 1 << m_layer;
		glow.enabled = false;
		m_crypt!.Glows.Add((glow, at.z));
	}

	private static readonly string[] s_cryptDecor =
	{
		"crypt_skeleton_pile01", "crypt_skeleton_pile02", "crypt_skeleton_pile03", "crypt_skeleton_laying",
		"Pickable_ForestCryptRemains01", "Pickable_ForestCryptRemains02", "Pickable_ForestCryptRemains03", "Pickable_ForestCryptRemains04",
		"crypt_rock", "skull_pile", "crypt_skeleton_chest",
	};

	private void BuildLeg(CryptLeg leg)
	{
		CryptRun c = m_crypt!;
		leg.Built = true;
		Transform frame = c.Frames[leg.Index];
		GameObject rootGo = new GameObject("CryptLeg" + leg.Index);
		rootGo.layer = m_layer;
		rootGo.transform.SetParent(frame, false);
		Transform root = rootGo.transform;
		leg.Root = rootGo;
		m_cryptPinned.Add(root);

		int last = c.Corners.Count;
		bool first = leg.Index == 0;
		bool exit = leg.Index == last;
		// (A corner's square of floor belongs to the hall that runs into it.)
		float z0 = first ? c.Start : leg.A + CryptHalf;
		float z1 = exit ? c.End : leg.B + CryptHalf;
		float y = c.Floor;
		float wallX = CryptHalf + CryptWall * 0.5f;

		// The floor: big flagstones, three across.
		int along = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / 3.7f));
		float tileZ = (z1 - z0) / along;
		float tileX = CryptHalf * 2f / 3f;
		bool floored = false;
		for (int i = 0; i < along; i++)
		{
			for (int j = 0; j < 3; j++)
			{
				floored |= CryptPiece(root, "stone_floor_2x2|stone_floor", Quaternion.identity, new Vector3(tileX + 0.03f, 0.25f, tileZ + 0.03f),
					new Vector3(-CryptHalf + tileX * (j + 0.5f), y - 0.25f, z0 + tileZ * (i + 0.5f))) != null;
			}
		}
		if (!floored)
		{
			GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Destroy(slab.GetComponent<Collider>());
			slab.layer = m_layer;
			slab.transform.SetParent(root, false);
			slab.transform.localPosition = new Vector3(0f, y - 0.1f, (z0 + z1) * 0.5f);
			slab.transform.localScale = new Vector3(CryptHalf * 2f, 0.2f, z1 - z0);
			slab.GetComponent<Renderer>().sharedMaterial = BorrowMaterial("stone_floor_2x2") ?? m_biomes[Mathf.Min(1, m_biomes.Count - 1)].Ground;
		}

		// The walls: the one on the outside of the corner ahead runs on to the end wall, the
		// inside one stops where the way on opens.
		int turn = exit ? 0 : c.Dirs[leg.Index];
		foreach (int side in new[] { -1, 1 })
		{
			float b = exit ? c.End : side == -turn ? leg.B + CryptHalf + CryptWall : leg.B - CryptHalf;
			WallAlong(root, side * wallX, z0, b, y);
			// Pillars along it, every 8 m.
			for (float pz = z0 + 4f; pz < b - 2f; pz += 8f)
			{
				CryptPiece(root, "stone_pillar", Quaternion.identity, new Vector3(0.75f, CryptWallHeight + 0.35f, 0.75f), new Vector3(side * wallX, y, pz));
			}
		}
		if (!exit)
		{
			WallAcross(root, leg.B + CryptHalf + CryptWall * 0.5f, -wallX - CryptWall * 0.5f, wallX + CryptWall * 0.5f, y);
			// A taller pillar on the inside of the corner, and a torch on the wall ahead to say
			// "turn here".
			CryptPiece(root, "stone_pillar_high|stone_pillar", Quaternion.identity, new Vector3(0.95f, CryptWallHeight + 1.1f, 0.95f), new Vector3(turn * wallX, y, leg.B - wallX));
			Sconce(root, new Vector3(turn * 1.6f, y + 1.7f, leg.B + CryptHalf), 180f, 9f);
		}

		// Sconces along the walls, every 13 m or so on alternating sides.
		int sconceSide = Random.value < 0.5f ? -1 : 1;
		for (float sz = z0 + 6f; sz < (exit ? c.End - 6f : leg.B - CryptHalf - 3f); sz += Random.Range(11f, 15f))
		{
			Sconce(root, new Vector3(sconceSide * CryptHalf, y + 1.7f, sz), sconceSide < 0 ? 90f : -90f);
			sconceSide = -sconceSide;
		}

		// Bones, remains and rubble along the walls.
		foreach (int side in new[] { -1, 1 })
		{
			for (float dz = z0 + 3f; dz < z1 - 3f; dz += Random.Range(4f, 8f))
			{
				if (Random.value > 0.55f || CornerNear(dz, 6f))
				{
					continue;
				}
				string name = s_cryptDecor[Random.Range(0, s_cryptDecor.Length)];
				CryptPiece(root, name, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), new Vector3(1.1f, 1.2f, 1.6f),
					new Vector3(side * (CryptHalf - 0.6f), y, dz), true, 1.3f);
			}
		}

		// Bone piles in this hall.
		foreach (CryptPile pile in c.Piles)
		{
			if (pile.Z >= leg.A && pile.Z < leg.B)
			{
				CryptPiece(root, "BonePileSpawner|crypt_skeleton_pile01", Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), new Vector3(1.9f, 1.4f, 1.9f),
					new Vector3(pile.Side * (CryptHalf - 0.95f), y, pile.Z), true, 2f);
			}
		}

		if (first)
		{
			CryptGate(root, c.Start, y, -1);
		}
		if (exit)
		{
			CryptGate(root, c.End, y, 1);
		}
	}

	private bool CornerNear(float z, float within)
	{
		if (m_crypt == null)
		{
			return false;
		}
		foreach (float c in m_crypt.Corners)
		{
			if (Mathf.Abs(z - c) < within)
			{
				return true;
			}
		}
		return false;
	}

	// The way in (out = -1, facing the forest) or out (+1): a stone front two walls high either
	// side of the hall, a beam over it, tall pillars, and two torches burning outside.
	private void CryptGate(Transform root, float z, float y, int outward)
	{
		float wallX = CryptHalf + CryptWall * 0.5f;
		float fz = z + outward * CryptWall * 0.5f;
		foreach (int side in new[] { -1, 1 })
		{
			float x0 = side < 0 ? -(CryptHalf + 13f) : wallX - CryptWall * 0.5f;
			float x1 = side < 0 ? -(wallX - CryptWall * 0.5f) : CryptHalf + 13f;
			WallAcross(root, fz, x0, x1, y, 2);
			CryptPiece(root, "stone_pillar_high|stone_pillar", Quaternion.identity, new Vector3(1f, CryptWallHeight * 2f + 0.6f, 1f), new Vector3(side * (wallX + 0.2f), y, fz + outward * 0.3f));
			GameObject? torch = CryptPiece(root, "piece_groundtorch_wood|piece_groundtorch", Quaternion.identity, new Vector3(0.6f, 1.8f, 0.6f),
				new Vector3(side * (CryptHalf - 0.2f), y, z + outward * 1.6f), true, 1.5f);
			if (torch != null)
			{
				LightUp(torch);
			}
			Light glow = new GameObject("CryptGateGlow").AddComponent<Light>();
			glow.gameObject.layer = m_layer;
			glow.transform.SetParent(root, false);
			glow.transform.localPosition = new Vector3(side * (CryptHalf - 0.2f), y + 1.9f, z + outward * 1.6f);
			glow.type = LightType.Point;
			glow.range = 9f;
			glow.intensity = 1.6f;
			glow.color = new Color(1f, 0.62f, 0.3f);
			glow.shadows = LightShadows.None;
			glow.cullingMask = 1 << m_layer;
		}
		// The beam over the way through.
		WallAcross(root, fz, -wallX, wallX, y, 1, CryptWallHeight);
	}

	// ---------------------------------------------------------------- each frame

	private void UpdateCrypt(float dt)
	{
		CryptRun? c = m_crypt;
		if (c == null)
		{
			return;
		}
		foreach (CryptLeg leg in c.Legs)
		{
			if (!leg.Built && leg.A < m_z + AheadDistance + 30f)
			{
				BuildLeg(leg);
			}
			else if (leg.Root != null && leg.B + CryptHalf + 2f < m_z - 40f && leg.Index < c.Legs.Count - 1)
			{
				m_cryptPinned.Remove(leg.Root.transform);
				Destroy(leg.Root);
				leg.Root = null;
			}
		}
		for (int i = c.Glows.Count - 1; i >= 0; i--)
		{
			(Light light, float z) = c.Glows[i];
			if (light == null)
			{
				c.Glows.RemoveAt(i);
				continue;
			}
			light.enabled = z > m_z - 10f && z < m_z + 30f;
		}

		// Bone piles raise their dead as you come down the hall.
		foreach (CryptPile pile in c.Piles)
		{
			if (pile.Woken || pile.Z - m_z > m_speed * 2.2f + 8f || pile.Z - m_z < 9f || CornerBetween(m_z + 1f, pile.Z))
			{
				continue;
			}
			pile.Woken = true;
			RaiseSkeletons(pile);
		}
		for (int i = m_rising.Count - 1; i >= 0; i--)
		{
			(Thing t, float time) = m_rising[i];
			if (t.Go == null)
			{
				m_rising.RemoveAt(i);
				continue;
			}
			time += dt;
			float k = Mathf.Clamp01(time / 0.9f);
			t.BaseY = Mathf.Lerp(-1.9f, 0f, Mathf.SmoothStep(0f, 1f, k));
			Vector3 p = t.Go.transform.localPosition;
			t.Go.transform.localPosition = new Vector3(p.x, RoadY(t.Z) + t.BaseY, t.Z);
			if (time > 0.55f)
			{
				t.Collidable = true;
			}
			if (k >= 1f)
			{
				m_rising.RemoveAt(i);
			}
			else
			{
				m_rising[i] = (t, time);
			}
		}

		// The dark, and the torch in your hand.
		m_cryptDark = Mathf.Clamp01((m_z - (c.Start - 4f)) / 10f) * Mathf.Clamp01((c.End + 8f - m_z) / 10f);
		bool torch = m_z > c.Start - 3f && m_z < c.End + 6f && m_state != State.Menu;
		if (torch != m_torchHeld)
		{
			HoldTorch(torch);
		}
		if (m_torchLight != null)
		{
			float flicker = 0.86f + 0.14f * Mathf.PerlinNoise(Time.time * 7f, 0.3f) + 0.05f * Mathf.Sin(Time.time * 23f);
			m_torchLight.intensity = 1.75f * flicker * Mathf.Clamp01(m_cryptDark * 1.5f + 0.15f);
		}

		// Bones rattling somewhere down the halls, and now and then a ghost.
		if (m_cryptDark > 0.5f)
		{
			c.NextSound -= dt;
			if (c.NextSound <= 0f)
			{
				c.NextSound = Random.Range(3.5f, 8f);
				string sound = Random.value < 0.2f ? "sfx_ghost_idle|sfx_ghosts_idle" : "sfx_skeleton_idle|sfx_skeleton_basic_verse_idle";
				m_audio.Play(sound, Random.Range(0.18f, 0.35f), Random.Range(0.85f, 1.05f));
			}
		}
	}

	private void RaiseSkeletons(CryptPile pile)
	{
		BiomeRuntime b = CryptBiome();
		if (b.Monsters.Count == 0)
		{
			return;
		}
		List<int> lanes = new() { 0, 1, 2 };
		lanes.RemoveAt(Random.Range(0, 3));
		int count = StarsAt(pile.Z) >= 2 ? 3 : 2;
		for (int i = 0; i < count; i++)
		{
			int lane = lanes[i % 2];
			float z = pile.Z + (i >= 2 ? 6f : Random.Range(-1f, 1f));
			SpawnEntry e = SpawnTables.Pick(b.Monsters)!;
			Thing t = SpawnCreature(e, lane, z, Kind.Block, 0f, BiomeAt(z), "waiter");
			t.BaseY = -1.9f;
			t.Collidable = false;
			t.Cooldown = Mathf.Max(t.Cooldown, 1.1f);
			Vector3 p = t.Go.transform.localPosition;
			t.Go.transform.localPosition = new Vector3(p.x, RoadY(z) + t.BaseY, z);
			m_rising.Add((t, 0f));
			GameObject? dust = FindFx("vfx_skeleton_rise|vfx_RockHit|vfx_Place_stone_floor");
			if (dust != null)
			{
				PlayFx(dust, new Vector3(LaneX(lane), RoadY(z) + 0.1f, z), Quaternion.identity, 3f);
			}
		}
		m_audio.Play("sfx_skeleton_rise|sfx_skeleton_alerted", 0.9f);
		m_shake = Mathf.Max(m_shake, 0.06f);
	}

	private void HoldTorch(bool hold)
	{
		m_torchHeld = hold;
		if (hold)
		{
			if (m_wielded == null && Wield("Torch") && m_wielded != null)
			{
				LightUp(m_wielded);
				foreach (Light own in m_wielded.GetComponentsInChildren<Light>(true))
				{
					own.enabled = false;
				}
				foreach (Renderer r in m_wielded.GetComponentsInChildren<Renderer>(true))
				{
					r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				}
			}
			if (m_torchLight == null && m_avatar != null)
			{
				m_torchLight = new GameObject("TorchLight").AddComponent<Light>();
				m_torchLight.gameObject.layer = m_layer;
				bool inHand = m_wielded != null && m_wieldName == "Torch";
				m_torchLight.transform.SetParent(inHand ? m_wielded!.transform : m_avatar.transform, false);
				if (!inHand)
				{
					m_torchLight.transform.localPosition = new Vector3(0.35f, 1.9f, 0.35f);
				}
				else
				{
					// Up at the flame, whichever way the torch's model points.
					m_torchLight.transform.position = m_wielded!.transform.position + Vector3.up * 0.45f;
				}
				m_torchLight.type = LightType.Point;
				m_torchLight.range = 15f;
				m_torchLight.intensity = 0.3f;
				m_torchLight.color = new Color(1f, 0.64f, 0.32f);
				m_torchLight.shadows = LightShadows.Soft;
				m_torchLight.shadowStrength = 0.85f;
				m_torchLight.cullingMask = 1 << m_layer;
			}
			m_audio.Play("sfx_torch_ignite|fx_torch|sfx_fireplace_ignite", 0.6f);
		}
		else
		{
			if (m_torchLight != null)
			{
				Destroy(m_torchLight.gameObject);
			}
			m_torchLight = null;
			if (m_wieldName == "Torch")
			{
				Unwield();
			}
		}
	}

	// Lane input near a corner: the key toward the opening takes the turn instead of a lane.
	private int CryptTurn(int step)
	{
		int k = NextCorner();
		if (step == 0 || k < 0 || m_crypt == null)
		{
			return step;
		}
		float zc = m_crypt.Corners[k];
		if (m_crypt.Armed[k] || m_z < zc - TurnWindow || m_cryptBonk >= 0f || step != m_crypt.Dirs[k])
		{
			return step;
		}
		m_crypt.Armed[k] = true;
		m_lane = 1;
		m_audio.Play(Sfx.Lane, 0.55f, 0.95f);
		return 0;
	}

	// After the run moves you on: you can't pass a corner you haven't turned at. You run on into
	// the corner toward its wall (a late press still makes it), and bounce off it.
	private void CryptCorner(float dt)
	{
		if (m_crypt == null)
		{
			m_cryptOver = 0f;
			return;
		}
		int k = -1;
		float before = m_z - m_speed * dt;
		for (int i = 0; i < m_crypt.Corners.Count; i++)
		{
			if (m_crypt.Corners[i] > before)
			{
				k = i;
				break;
			}
		}
		if (k < 0)
		{
			m_cryptOver = 0f;
			return;
		}
		float zc = m_crypt.Corners[k];
		float wall = CryptHalf + TurnPoint - 0.55f;
		if (m_crypt.Armed[k])
		{
			if (m_z >= zc)
			{
				// Round the corner: the avatar's frame changes and its smoothing takes over.
				m_cryptOver = 0f;
			}
			return;
		}
		if (m_z > zc - TurnPoint)
		{
			float over = m_z - (zc - TurnPoint);
			m_z = zc - TurnPoint;
			if (m_cryptBonk < 0f)
			{
				m_cryptOver = Mathf.Min(wall, m_cryptOver + over);
			}
		}
		if (m_cryptBonk < 0f && m_cryptOver >= wall - 0.001f)
		{
			// Into the wall.
			m_cryptBonk = 0f;
			m_audio.Play(Sfx.HitLog, 1f, 0.6f);
			m_audio.Play("sfx_rock_hit|sfx_stone_hit|sfx_hit", 0.8f, 0.8f);
			m_shake = Mathf.Max(m_shake, 0.3f);
			m_lane = 1;
			Hit(new Thing { Label = "", JumpOrSlide = true, Heavy = true }, "You ran into the wall of a burial chamber. (Press toward the opening at a corner.)");
		}
		if (m_cryptBonk >= 0f)
		{
			m_cryptBonk += dt;
			m_cryptOver = Mathf.Lerp(wall, 0f, Mathf.SmoothStep(0f, 1f, m_cryptBonk / BonkTime));
			if (m_cryptBonk >= BonkTime)
			{
				m_cryptBonk = -1f;
				m_cryptOver = 0f;
				m_crypt.Armed[k] = true;
			}
		}
	}

	// The avatar in its hall's frame, eased over a change of frame.
	private void CryptAvatar(float dt)
	{
		if (m_avatar == null)
		{
			return;
		}
		Transform tr = m_avatar.transform;
		Transform want = CryptFrameAt(m_z);
		if (tr.parent != want)
		{
			tr.SetParent(want, false);
			if (m_avatarDrawn)
			{
				m_cryptLagPos = m_avatarDrawnPos - tr.position;
				m_cryptLagYaw = Mathf.DeltaAngle(tr.eulerAngles.y, m_avatarDrawnRot.eulerAngles.y);
			}
		}
		float ease = 1f - Mathf.Exp(-9f * dt);
		m_cryptLagPos = Vector3.Lerp(m_cryptLagPos, Vector3.zero, ease);
		m_cryptLagYaw = Mathf.Lerp(m_cryptLagYaw, 0f, ease);
		if (m_cryptLagPos.sqrMagnitude > 1e-6f)
		{
			tr.position += m_cryptLagPos;
		}
		if (Mathf.Abs(m_cryptLagYaw) > 0.01f)
		{
			tr.rotation = Quaternion.AngleAxis(m_cryptLagYaw, Vector3.up) * tr.rotation;
		}
		m_avatarDrawnPos = tr.position;
		m_avatarDrawnRot = tr.rotation;
		m_avatarDrawn = true;
	}

	// Top-down over the halls, like the sea: it turns with the way ahead around each corner.
	private void PlaceCryptCamera(float dt)
	{
		CryptRun? c = m_crypt;
		bool want = c != null && m_z > c.Start - 22f && m_z < c.End + 2f && m_state != State.Menu;
		m_cryptView = Mathf.MoveTowards(m_cryptView, want ? 1f : 0f, dt / 1.3f);
		CryptBackground();
		if (m_cryptView <= 0f || c == null)
		{
			m_cryptFocusSet = false;
			return;
		}
		float yaw = 0f;
		for (int i = 0; i < c.Corners.Count; i++)
		{
			yaw += c.Dirs[i] * 90f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(c.Corners[i] - 7f, c.Corners[i] + 4f, m_z));
		}
		Vector3 focus = StagePoint(new Vector3(0f, c.Floor + 1f, m_z + 6f));
		if (!m_cryptFocusSet)
		{
			m_cryptFocus = focus;
			m_cryptFocusSet = true;
		}
		else
		{
			m_cryptFocus = Vector3.Lerp(m_cryptFocus, focus, 1f - Mathf.Exp(-6f * dt));
		}
		const float height = 14f;
		Quaternion turn = m_stage.rotation * Quaternion.Euler(0f, yaw, 0f);
		Vector3 pos = m_cryptFocus + turn * new Vector3(0f, height, -height * 0.6f);
		Quaternion rot = Quaternion.LookRotation(m_cryptFocus - pos);
		float b = Mathf.SmoothStep(0f, 1f, m_cryptView);
		m_camera.transform.position = Vector3.Lerp(m_camera.transform.position, pos, b) + (m_shake > 0.001f ? Random.insideUnitSphere * m_shake * b : Vector3.zero);
		m_camera.transform.rotation = Quaternion.Slerp(m_camera.transform.rotation, rot, b);
		m_camFinal = m_camera.transform.position;
		m_camFinalRot = m_camera.transform.rotation;
	}

	// The game's own camera draws its sky behind ours; in the crypt it's black instead.
	private void CryptBackground()
	{
		bool black = m_usingGameCamera && m_cryptDark > 0.6f;
		if (m_mainCamera == null || black == m_cryptClear)
		{
			return;
		}
		if (black)
		{
			m_mainClear = m_mainCamera.clearFlags;
			m_mainCamera.clearFlags = CameraClearFlags.SolidColor;
		}
		else
		{
			m_mainCamera.clearFlags = m_mainClear;
		}
		m_cryptClear = black;
	}

	private void ApplyCryptWeather(ref float fog, ref Color fogColor, ref Color ambient, ref Color sunColor, ref float sunIntensity, ref Color sky)
	{
		float w = m_cryptDark;
		if (w <= 0f)
		{
			return;
		}
		sky = Color.Lerp(sky, Color.black, w);
		fogColor = Color.Lerp(fogColor, new Color(0.01f, 0.01f, 0.012f), w);
		fog = Mathf.Lerp(fog, 0.028f, w);
		ambient = Color.Lerp(ambient, new Color(0.1f, 0.1f, 0.12f), w);
		sunColor = Color.Lerp(sunColor, new Color(0.55f, 0.6f, 0.8f), w);
		sunIntensity = Mathf.Lerp(sunIntensity, 0.06f, w);
	}

	// Late each frame: everything on the stage goes into the frame of the hall its z is in. Once
	// you're well out, the last hall's frame becomes the stage.
	private void UpdateCryptFrames()
	{
		CryptRun? c = m_crypt;
		if (c == null)
		{
			return;
		}
		m_cryptMove.Clear();
		foreach (Transform frame in c.Frames)
		{
			foreach (Transform child in frame)
			{
				if (!m_cryptPinned.Contains(child) && (m_avatar == null || child != m_avatar.transform))
				{
					m_cryptMove.Add(child);
				}
			}
		}
		foreach (Transform child in m_cryptMove)
		{
			Transform want = CryptFrameAt(child.localPosition.z);
			if (child.parent != want)
			{
				child.SetParent(want, false);
			}
		}
		if (m_z > c.End + 30f && m_cryptView <= 0f && m_cryptDark <= 0f && !m_torchHeld)
		{
			BakeCrypt();
		}
	}

	private void BakeCrypt()
	{
		CryptRun c = m_crypt!;
		Transform last = c.Frames[c.Frames.Count - 1];
		if (last != m_stage)
		{
			Vector3 pos = last.position;
			Quaternion rot = last.rotation;
			m_stage.SetPositionAndRotation(pos, rot);
			last.localPosition = Vector3.zero;
			last.localRotation = Quaternion.identity;
		}
		ReleaseCrypt();
		Plugin.Log.LogInfo($"Out of the burial chamber; stage now at {m_stage.position}.");
	}

	// Everything back on the stage itself, and the halls gone.
	private void ReleaseCrypt()
	{
		CryptRun? c = m_crypt;
		if (c == null)
		{
			return;
		}
		foreach (CryptLeg leg in c.Legs)
		{
			if (leg.Root != null)
			{
				Destroy(leg.Root);
			}
		}
		m_cryptMove.Clear();
		for (int i = 1; i < c.Frames.Count; i++)
		{
			if (c.Frames[i] == null)
			{
				continue;
			}
			foreach (Transform child in c.Frames[i])
			{
				if (!m_cryptPinned.Contains(child))
				{
					m_cryptMove.Add(child);
				}
			}
		}
		foreach (Transform child in m_cryptMove)
		{
			child.SetParent(m_stage, false);
		}
		m_cryptMove.Clear();
		for (int i = 1; i < c.Frames.Count; i++)
		{
			if (c.Frames[i] != null)
			{
				Destroy(c.Frames[i].gameObject);
			}
		}
		m_cryptPinned.Clear();
		m_crypt = null;
		m_rising.Clear();
		m_cryptOver = 0f;
		m_cryptBonk = -1f;
		m_cryptLagPos = Vector3.zero;
		m_cryptLagYaw = 0f;
	}

	// A new run: no crypt, no torch, the sky back.
	private void ClearCrypt()
	{
		if (m_avatar != null && m_avatar.transform.parent != m_stage)
		{
			m_avatar.transform.SetParent(m_stage, false);
		}
		ReleaseCrypt();
		if (m_torchHeld)
		{
			HoldTorch(false);
		}
		m_cryptView = 0f;
		m_cryptDark = 0f;
		m_cryptFocusSet = false;
		m_avatarDrawn = false;
		CryptBackground();
	}

	// An old chest in a free lane: ten coins.
	private bool SpawnChest(int lane, float z)
	{
		GameObject? prefab = Pictures.Prefab("TreasureChest_forestcrypt");
		if (prefab == null)
		{
			return false;
		}
		GameObject go = Fitted(Picture(prefab), Quaternion.Euler(0f, 180f, 0f), new Vector3(1.3f, 1.1f, 1f), true, 1.6f);
		go.transform.localPosition = new Vector3(LaneX(lane), RoadY(z), z);
		m_things.Add(new Thing { Go = go, Kind = Kind.Coin, Lane = lane, Z = z, HalfLength = 0.5f, Label = "Chest" });
		return true;
	}

	// Anything a row put round a corner or by the gates (pack members strung out after it).
	private void CryptClearRows(int from)
	{
		for (int i = m_things.Count - 1; i >= from; i--)
		{
			Thing t = m_things[i];
			if (t.Kind != Kind.Ground && t.Kind != Kind.Scenery && InCrypt(t.Z) && CryptNoRow(t.Z))
			{
				DestroyThing(t);
				m_things.RemoveAt(i);
			}
		}
	}

	// A thing that came past you and round the corner behind (out of sight now).
	private bool CryptGoneBehind(Thing t) => m_crypt != null && t.Speed > 0f && t.Z < m_z - 1f && CornerBetween(t.Z, m_z - 1f);
}
