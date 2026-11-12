using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.4 (SAT: "The burial chamber dungeon could include like 90 degree turns or something like
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
	// 1.16.79 (SAT: "Dungeon hallways can be much much larger"): 5 m of floor beside the lanes
	// (was 1.2), taller walls (WallH) and the camera higher to take it in.
	// 1.16.150 (SAT: the high camera in dungeons was disorienting, and the floor's three tiles
	// didn't match the three lanes): one flagstone per lane and a strip beside them, and the
	// camera stays behind you as on the open track.
	private const float CryptHalf = TrackHalf + 1.2f;
	private const float CryptWall = 0.6f;
	// How far before a corner the turn can be pressed, and where you wait if you haven't.
	// 1.16.20 (SAT pressed D at a corner and hit the wall): a press a little early was taken as a
	// lane change, and the window was under a second at speed. Now it's about 1.6 s, a press
	// toward the opening just before it still counts, and being in the lane on the opening's side
	// when you reach the corner takes the turn too.
	private float TurnWindow => Mathf.Max(12f, m_speed * 1.6f);
	private float m_cryptPressTime = -10f;
	private int m_cryptPressDir;
	private const float TurnPoint = 0.25f;
	private const float BonkTime = 0.7f;

	private sealed class CryptLeg
	{
		public float A;
		public float B;
		public int Index;
		public GameObject? Root;
		public bool Built;
		// 1.16.5: a sunken crypt's halls are half under water.
		public bool Flooded;
		public readonly List<Mesh> Meshes = new();
	}

	private sealed class CryptPile
	{
		public float Z;
		public int Side;
		public bool Woken;
		public GameObject? Go;
	}

	private sealed class CryptRun
	{
		public CryptTheme Theme = s_burialChamber;
		public float Start;
		public float End;
		public float Floor;
		public readonly List<float> Corners = new();
		// +1 turns right, -1 left.
		public readonly List<int> Dirs = new();
		public readonly List<bool> Armed = new();
		// 1.16.22 (SAT): the camera turns once you've pressed for the corner (0 to 1 each), and a
		// gust of wind ahead of you beckons the way until you do (SurfGust.cs).
		public readonly List<float> CamTurn = new();
		public float NextWhoosh;
		// Frames[0] is the stage; Frames[k] holds the hallway after corner k - 1.
		public readonly List<Transform> Frames = new();
		public readonly List<CryptLeg> Legs = new();
		public readonly List<CryptPile> Piles = new();
		public readonly List<(Light light, float z)> Glows = new();
		public readonly List<CryptIronGate> Gates = new();
		// 1.16.9: where lava runs across a charred fortress's halls (SurfFortress.cs).
		public readonly List<float> Lava = new();
		public float NextSound = 3f;
		// 1.16.79: its material given once you come out the far end.
		public bool Looted;
		// 1.16.8: the one big creature waiting in a troll cave (SurfTrollCave.cs).
		public float LurkerZ = float.NaN;
		public bool LurkerOut;
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
	// (1.16.150: behind a mud bank you haven't dug through counts too, so nothing shoots through it.)
	private bool CryptHidden(Thing t) => m_crypt != null && t.Z > m_z && (CornerBetween(m_z + 1f, t.Z) || DigBetween(m_z, t.Z));

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
			// 1.16.150 (SAT: monsters right round a corner aren't fair): a second or so of clear
			// hall after each turn.
			if (z > c - Mathf.Max(16f, TurnWindow + 4f) && z < c + Mathf.Max(12f, m_speed * 1.4f + 6f))
			{
				return true;
			}
		}
		// The lanes clear around a troll cave's troll.
		if (!float.IsNaN(m_crypt.LurkerZ) && Mathf.Abs(z - m_crypt.LurkerZ) < 9f)
		{
			return true;
		}
		foreach (float lz in m_crypt.Lava)
		{
			if (Mathf.Abs(z - lz) < 6f)
			{
				return true;
			}
		}
		if (InForkOrDig(z))
		{
			return true;
		}
		// The lanes clear by an iron gate.
		foreach (CryptIronGate g in m_crypt.Gates)
		{
			if (Mathf.Abs(z - g.Z) < 7f)
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

	// Once per stretch of Black Forest or Swamp (not its boss arena), well into it: start, length
	// and the corners, before the ground there is laid.
	private void PlanCrypt()
	{
		if (m_crypt != null || !Plugin.Dungeons.Value || m_biomes.Count < 2)
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
			CryptTheme? theme = s.Biome < m_biomes.Count ? ThemeFor(m_biomes[s.Biome].Def.Name) : null;
			if (s.Boss || s.Sea || s.Shore || s.Rush || theme == null)
			{
				continue;
			}
			float len = Plugin.BiomeLength.Value;
			// 1.16.109 (SAT: dungeons too short): about twice as long; a biome with a boss waits for
			// its dungeon (its items only come after it), so it may run past the biome's usual end.
			float length = Mathf.Clamp(Mathf.Round(len * 0.9f / GroundTile) * GroundTile, theme.MinLength * 1.8f, theme.MaxLength * 1.8f);
			float start = Mathf.Ceil((s.Start + Mathf.Max(120f, len * 0.25f)) / GroundTile) * GroundTile;
			// Ground already laid stays as it is, so it starts past it (and the tile before eases down).
			start = Mathf.Max(start, Mathf.Ceil((m_nextGroundZ + GroundTile) / GroundTile) * GroundTile);
			// Well before the stretch ends (with its boss ahead, before the summoning items can end it).
			float limit = (s.End < float.MaxValue ? s.End : s.Start + len * (BossFor(s) != null ? 2.2f : 1f)) - 80f;
			if (start + length > limit)
			{
				length = Mathf.Floor((limit - start) / GroundTile) * GroundTile;
				if (length < 120f)
				{
					continue;
				}
			}
			MakeCrypt(start, start + length, theme);
			PlanCryptExtras(m_crypt!, s);
			if (theme.Biome == s_burialChamber.Biome)
			{
				// (The Black Forest's burial chambers and troll caves take turns.)
				m_forestDungeons++;
			}
			return;
		}
	}

	private void MakeCrypt(float start, float end, CryptTheme theme)
	{
		if (theme.Rooms != 0)
		{
			LoadRooms(theme.Rooms);
		}
		CryptRun c = new CryptRun { Theme = theme, Start = start, End = end, Floor = RoadRaw(start) };
		// Out to one side and back, a few times: the way on always ends up heading on down the
		// track, and the hallways never cross. A long first and last hallway keep the forest
		// outside well away from the sideways ones.
		float z = start + Random.Range(32f, 38f);
		int net = 0;
		while (true)
		{
			float side = Mathf.Lerp(12f, 44f, Random.value * Random.value);
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
			// 1.16.109 (SAT: halls too alike, could be much longer): short and long ones mixed.
			float north = Random.value < 0.3f ? Random.Range(14f, 24f) : Random.Range(30f, 80f);
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
			c.CamTurn.Add(0f);
			m_cryptPinned.Add(frame);
		}
		for (int k = 0; k <= c.Corners.Count; k++)
		{
			float a = k == 0 ? start : c.Corners[k - 1];
			float b = k == c.Corners.Count ? end : c.Corners[k];
			c.Legs.Add(new CryptLeg { A = a, B = b, Index = k });
		}
		m_crypt = c;
		int last = c.Legs.Count - 1;
		// A sunken crypt's halls between its corners are often flooded (never the way in or out).
		if (theme.Flooded > 0f && m_waterMaterial != null)
		{
			foreach (CryptLeg leg in c.Legs)
			{
				leg.Flooded = leg.Index > 0 && leg.Index < last && leg.B - leg.A >= 14f && Random.value < theme.Flooded;
			}
		}
		// Iron gates hang over some of its longer halls, well clear of the corners.
		if (theme.IronGates)
		{
			foreach (CryptLeg leg in c.Legs)
			{
				float from = leg.A + (leg.Index == 0 ? 16f : 13f);
				float to = leg.B - (leg.Index == last ? 16f : 13f);
				if (to - from < 2f || c.Gates.Count >= 3 || Random.value > 0.45f)
				{
					continue;
				}
				c.Gates.Add(new CryptIronGate { Z = Random.Range(from, to) });
			}
		}
		// Lava first, so nothing is raised standing in it.
		PlanLava(c);
		// A pile of bones (or a draugr's grave mound) in some of the longer hallways, which raises
		// the dead as you come.
		foreach (CryptLeg leg in c.Legs)
		{
			if (theme.Pile.Length == 0 || leg.B - leg.A < 28f || Random.value > 0.65f)
			{
				continue;
			}
			for (int tries = 0; tries < 6; tries++)
			{
				float pz = Random.Range(leg.A + (leg.Index == 0 ? 8f : CryptHalf + 16f), leg.B - 18f);
				if (!CryptNoRow(pz))
				{
					c.Piles.Add(new CryptPile { Z = pz, Side = Random.value < 0.5f ? -1 : 1 });
					break;
				}
			}
		}
		PlanLurker(c);
		CryptBiome();
		int flooded = c.Legs.FindAll(l => l.Flooded).Count;
		Plugin.Log.LogInfo($"A {theme.Name} from {start:0} to {end:0} m: corners at {string.Join(", ", c.Corners.ConvertAll(x => x.ToString("0")))} ({string.Join("", c.Dirs.ConvertAll(x => x > 0 ? "R" : "L"))}), {c.Piles.Count} piles, {flooded} flooded halls, {c.Gates.Count} iron gates.");
	}

	// Who's in the crypt (its theme's dead, a few flyers, swimmers for the flooded halls), what
	// to jump (fallen pillars, mud piles) and what to slide under (a fallen pillar across).
	private BiomeRuntime CryptBiome()
	{
		CryptTheme theme = m_crypt != null ? m_crypt.Theme : s_burialChamber;
		if (m_cryptBiomes.TryGetValue(theme.Name, out BiomeRuntime cached))
		{
			return cached;
		}
		int home = m_biomes.FindIndex(x => x.Def.Name == theme.Biome);
		BiomeRuntime outside = m_biomes[home >= 0 ? home : Mathf.Min(1, m_biomes.Count - 1)];
		BiomeRuntime rt = new BiomeRuntime { Def = outside.Def, Sky = Color.black, Ground = outside.Ground, Fog = 0.028f, FogColor = Color.black };
		List<string> missing = new();
		foreach (SpawnEntry e in SpawnTables.Parse(theme.Monsters, missing))
		{
			e.Flying = false;
			rt.Monsters.Add(e);
		}
		if (theme.Flyers.Length > 0)
		{
			foreach (SpawnEntry e in SpawnTables.Parse(theme.Flyers, missing))
			{
				e.Flying = true;
				rt.Flyers.Add(e);
			}
		}
		if (rt.Monsters.Count == 0)
		{
			rt.Monsters.AddRange(outside.Monsters);
		}
		List<SpawnEntry> swim = new();
		if (theme.Swimmers.Length > 0)
		{
			foreach (SpawnEntry e in SpawnTables.Parse(theme.Swimmers, missing))
			{
				e.Flying = false;
				swim.Add(e);
			}
		}
		m_cryptSwimmers[theme.Name] = swim;
		List<SpawnEntry> raised = new();
		foreach (SpawnEntry e in SpawnTables.Parse(theme.Raised, missing))
		{
			e.Flying = false;
			raised.Add(e);
		}
		m_cryptRaised[theme.Name] = raised.Count > 0 ? raised : rt.Monsters;
		foreach (string name in theme.Low.Split('|'))
		{
			GameObject? low = CryptTemplate(name);
			if (low != null)
			{
				rt.Logs.Add(low);
				break;
			}
		}
		if (rt.Logs.Count == 0)
		{
			rt.Logs.AddRange(outside.Logs);
		}
		GameObject? beam = null;
		foreach (string name in theme.High.Split('|'))
		{
			beam = CryptTemplate(name);
			if (beam != null)
			{
				break;
			}
		}
		m_cryptHigh[theme.Name] = beam != null ? new List<GameObject> { beam } : rt.Logs;
		List<SpawnEntry> droppers = new();
		if (theme.Droppers.Length > 0)
		{
			foreach (SpawnEntry e in SpawnTables.Parse(theme.Droppers, missing))
			{
				e.Flying = false;
				droppers.Add(e);
			}
		}
		m_cryptDroppers[theme.Name] = droppers;
		if (missing.Count > 0)
		{
			Plugin.Log.LogInfo($"The {theme.Name}: not in this game version: " + string.Join(", ", missing));
		}
		m_cryptBiomes[theme.Name] = rt;
		return rt;
	}

	// What to slide under in this crypt (a stone pillar fallen across, held up at its ends).
	private List<GameObject> CryptHighs(BiomeRuntime b) =>
		m_crypt != null && m_cryptHigh.TryGetValue(m_crypt.Theme.Name, out List<GameObject> highs) && highs.Count > 0 ? highs : b.Logs;

	private readonly Dictionary<string, List<GameObject>> m_cryptHigh = new();

	// ---------------------------------------------------------------- building the halls

	// The first of `names` there is (a game prefab, or a part of the dungeon's rooms), fitted to
	// `size`. A wall is first turned so its long side runs along x.
	private GameObject? CryptPiece(Transform root, string names, Quaternion rotation, Vector3 size, Vector3 at, bool uniform = false, float maxScale = float.MaxValue, bool wall = false)
	{
		foreach (string name in names.Split('|'))
		{
			GameObject? prefab = CryptTemplate(name);
			if (prefab == null)
			{
				continue;
			}
			GameObject picture = Picture(prefab);
			if (wall)
			{
				rotation *= Quaternion.Euler(0f, LongYaw(picture), 0f);
			}
			GameObject go = Fitted(picture, rotation, size, uniform, maxScale);
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
		if (m_crypt!.Theme.Rocky)
		{
			// A cave's walls are rough rock (SurfCave.cs).
			RockLine(root, new Vector3(x, y, z0), new Vector3(x, y, z1), new Vector3(Mathf.Sign(x), 0f, 0f), rows);
			return;
		}
		int n = Mathf.Max(1, Mathf.RoundToInt(length / 4f));
		float piece = length / n;
		for (int r = 0; r < rows; r++)
		{
			for (int i = 0; i < n; i++)
			{
				CryptPiece(root, m_crypt!.Theme.Walls, Quaternion.Euler(0f, 90f, 0f), new Vector3(CryptWall, WallH, piece + 0.03f),
					new Vector3(x, y + r * WallH, z0 + piece * (i + 0.5f)), wall: true);
			}
		}
	}

	// A stone wall across the hall (z fixed) from x0 to x1.
	private void WallAcross(Transform root, float z, float x0, float x1, float y, int rows = 1, float lift = 0f, int away = 1)
	{
		float length = x1 - x0;
		if (length < 0.2f)
		{
			return;
		}
		if (m_crypt!.Theme.Rocky)
		{
			RockLine(root, new Vector3(x0, y + lift, z), new Vector3(x1, y + lift, z), new Vector3(0f, 0f, away), rows, lift > 0f);
			return;
		}
		int n = Mathf.Max(1, Mathf.RoundToInt(length / 4f));
		float piece = length / n;
		for (int r = 0; r < rows; r++)
		{
			for (int i = 0; i < n; i++)
			{
				CryptPiece(root, m_crypt!.Theme.Walls, Quaternion.identity, new Vector3(piece + 0.03f, WallH, CryptWall),
					new Vector3(x0 + piece * (i + 0.5f), y + lift + r * WallH, z), wall: true);
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
		if (m_crypt!.Theme.Crystals)
		{
			// A cave has no torches on its walls: crystals glow at their foot (SurfCave.cs).
			GlowCrystal(root, at, yaw, range);
			return;
		}
		if (m_crypt.Theme.StandingSconce)
		{
			StandingTorch(root, at, yaw, Mathf.Max(range, m_crypt.Theme.SconceRange));
			return;
		}
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

	// 1.16.150: a standing torch (the Swamp's green ones) at the foot of the wall, with its light.
	private void StandingTorch(Transform root, Vector3 at, float yaw, float range)
	{
		CryptTheme theme = m_crypt!.Theme;
		Vector3 facing = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
		Vector3 foot = new Vector3(at.x, m_crypt.Floor, at.z) + facing * 0.55f;
		GameObject? go = CryptPiece(root, theme.Sconce, Quaternion.identity, theme.SconceSize, foot, true, 2.2f);
		if (go != null)
		{
			LightUp(go);
			foreach (Light own in go.GetComponentsInChildren<Light>(true))
			{
				own.enabled = false;
			}
		}
		Light glow = new GameObject("CryptGlow").AddComponent<Light>();
		glow.gameObject.layer = m_layer;
		glow.transform.SetParent(root, false);
		glow.transform.localPosition = foot + new Vector3(0f, theme.SconceSize.y + 0.2f, 0f) + facing * 0.3f;
		glow.type = LightType.Point;
		glow.range = range;
		glow.intensity = theme.SconceIntensity;
		glow.color = theme.SconceColor;
		glow.shadows = LightShadows.None;
		glow.cullingMask = 1 << m_layer;
		glow.enabled = false;
		m_crypt.Glows.Add((glow, at.z));
	}

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

		CryptTheme theme = c.Theme;
		// The floor: big flagstones, three across.
		int along = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / LaneWidth));
		float tileZ = (z1 - z0) / along;
		float strip = CryptHalf - LaneWidth * 1.5f;
		bool floored = false;
		// (A cave's rock overlaps, each piece turned any way.)
		float overlap = theme.Rocky ? 0.6f : 0.03f;
		for (int i = 0; i < along; i++)
		{
			for (int j = -1; j < 4; j++)
			{
				// A flagstone under each lane, and a narrow strip along each wall.
				bool edge = j < 0 || j > 2;
				float tileX = edge ? strip : LaneWidth;
				float x = edge ? (j < 0 ? -1f : 1f) * (CryptHalf - strip * 0.5f) : LaneX(j);
				Quaternion turned = theme.Rocky ? Quaternion.Euler(0f, 90f * Random.Range(0, 4), 0f) : Quaternion.identity;
				floored |= CryptPiece(root, theme.Floors, turned, new Vector3(tileX + overlap, 0.25f, tileZ + overlap),
					new Vector3(x, y - 0.25f, z0 + tileZ * (i + 0.5f))) != null;
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

		// The roof, over the walls too; open for the first and last few metres, where the camera
		// comes down from outside and goes back up.
		CryptRoof(root, -wallX - CryptWall * 0.5f, wallX + CryptWall * 0.5f, first ? z0 + 6f : z0, exit ? z1 - 6f : z1 + CryptWall, y);

		// The walls: the one on the outside of the corner ahead runs on to the end wall, the
		// inside one stops where the way on opens.
		int turn = exit ? 0 : c.Dirs[leg.Index];
		// 1.16.150: at a junction the outside wall opens too, and the end wall, onto dead ends.
		CryptJunction? junction = exit ? null : JunctionAtCorner(leg.Index);
		foreach (int side in new[] { -1, 1 })
		{
			float b = exit ? c.End : side == -turn && junction == null ? leg.B + CryptHalf + CryptWall : leg.B - CryptHalf;
			WallAlongGaps(leg, root, side * wallX, z0, b, y);
			// Pillars along it, every 8 m.
			for (float pz = z0 + 4f; pz < b - 2f; pz += 8f)
			{
				if (!InJunctionGap(leg, pz, 1.5f))
				{
					CryptPiece(root, theme.Pillars, Quaternion.identity, new Vector3(0.75f, WallH + 0.35f, 0.75f), new Vector3(side * wallX, y, pz));
				}
			}
		}
		if (!exit)
		{
			// A taller pillar on the inside of the corner, and a torch on the wall ahead to say
			// "turn here" (at a junction, a pillar on each side and no torch: the way on is lit
			// further in, the dead ends aren't).
			CryptPiece(root, theme.TallPillar, Quaternion.identity, new Vector3(0.95f, WallH + 1.1f, 0.95f), new Vector3(turn * wallX, y, leg.B - wallX));
			if (junction == null)
			{
				WallAcross(root, leg.B + CryptHalf + CryptWall * 0.5f, -wallX - CryptWall * 0.5f, wallX + CryptWall * 0.5f, y);
				Sconce(root, new Vector3(turn * 1.6f, y + 1.7f, leg.B + CryptHalf), 180f, 9f);
			}
			else
			{
				CryptPiece(root, theme.TallPillar, Quaternion.identity, new Vector3(0.95f, WallH + 1.1f, 0.95f), new Vector3(-turn * wallX, y, leg.B - wallX));
			}
		}

		// Sconces along the walls, every 13 m or so on alternating sides (fewer in a sunken crypt).
		int sconceSide = Random.value < 0.5f ? -1 : 1;
		for (float sz = z0 + 6f; sz < (exit ? c.End - 6f : leg.B - CryptHalf - 3f); sz += Random.Range(theme.SconceGap - 2f, theme.SconceGap + 2f))
		{
			if (InJunctionGap(leg, sz, 2f))
			{
				continue;
			}
			Sconce(root, new Vector3(sconceSide * CryptHalf, y + 1.7f, sz), sconceSide < 0 ? 90f : -90f);
			sconceSide = -sconceSide;
		}
		BuildCryptExtras(leg, root, y);

		// Bones, remains and rubble along the walls.
		foreach (int side in new[] { -1, 1 })
		{
			for (float dz = z0 + 3f; dz < z1 - 3f; dz += Random.Range(4f, 8f))
			{
				if (Random.value > 0.55f || CornerNear(dz, 6f))
				{
					continue;
				}
				string name = theme.Decor[Random.Range(0, theme.Decor.Length)];
				CryptPiece(root, name, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), new Vector3(0.9f, 1.1f, 1.3f),
					new Vector3(side * (CryptHalf - 0.5f), y, dz), true, 1.3f);
			}
		}

		// Bone piles in this hall.
		foreach (CryptPile pile in c.Piles)
		{
			if (pile.Z >= leg.A && pile.Z < leg.B)
			{
				pile.Go = CryptPiece(root, theme.Pile, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), new Vector3(1.4f, 1.2f, 1.6f),
					new Vector3(pile.Side * (CryptHalf - 0.75f), y, pile.Z), true, 2f);
			}
		}

		// Water over a flooded hall's floor, and the iron gates hanging over it.
		if (leg.Flooded)
		{
			FloodLeg(leg, root, z0, z1, y);
		}
		foreach (CryptIronGate gate in c.Gates)
		{
			if (!gate.Built && gate.Z >= leg.A && gate.Z < leg.B)
			{
				BuildIronGate(gate, root, y);
			}
		}
		foreach (float lz in c.Lava)
		{
			if (lz >= leg.A && lz < leg.B)
			{
				BuildLava(root, lz, y);
			}
		}

		if (!c.LurkerOut && !float.IsNaN(c.LurkerZ) && c.LurkerZ >= leg.A && c.LurkerZ < leg.B)
		{
			SpawnLurker(c);
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
			WallAcross(root, fz, x0, x1, y, 2, 0f, outward);
			CryptPiece(root, m_crypt!.Theme.TallPillar, Quaternion.identity, new Vector3(1f, WallH * 2f + 0.6f, 1f), new Vector3(side * (wallX + 0.2f), y, fz + outward * 0.3f));
			GameObject? torch = CryptPiece(root, m_crypt!.Theme.GateTorch, Quaternion.identity, new Vector3(0.6f, 1.8f, 0.6f),
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
			glow.color = m_crypt.Theme.GateLight;
			glow.shadows = LightShadows.None;
			glow.cullingMask = 1 << m_layer;
		}
		// The beam (or rock) over the way through.
		WallAcross(root, fz, -wallX, wallX, y, 1, WallH, outward);
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
				DestroyLegMeshes(leg);
			}
		}
		if (!c.Looted && m_z > c.End + 2f && m_state == State.Running)
		{
			c.Looted = true;
			DungeonLoot(c);
		}
		UpdateIronGates(dt);
		UpdateIcicles(dt);
		UpdateDrops(dt);
		UpdateLurker();
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
			if (pile.Woken || pile.Z - m_z > m_speed * 2.2f + 8f || pile.Z - m_z < Mathf.Max(9f, m_speed * 1.1f + 4f) || CornerBetween(m_z + 1f, pile.Z) || DigBetween(m_z, pile.Z))
			{
				continue;
			}
			pile.Woken = true;
			RaiseDead(pile);
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
			float depth = c.Theme.RiseDepth;
			float k = Mathf.Clamp01(time / (0.4f + 0.26f * depth));
			t.BaseY = Mathf.Lerp(-depth, 0f, Mathf.SmoothStep(0f, 1f, k));
			Vector3 p = t.Go.transform.localPosition;
			t.Go.transform.localPosition = new Vector3(p.x, RoadY(t.Z) + t.BaseY, t.Z);
			if (k > 0.6f)
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

		// Bones rattling somewhere down the halls and now and then a ghost; in a sunken crypt the
		// draugr muttering, and the blobs.
		if (m_cryptDark > 0.5f)
		{
			c.NextSound -= dt;
			if (c.NextSound <= 0f)
			{
				c.NextSound = Random.Range(3.5f, 8f);
				string sound = Random.value < 0.22f ? c.Theme.IdleRare : c.Theme.Idle;
				m_audio.Play(sound, Random.Range(0.18f, 0.35f), Random.Range(0.85f, 1.05f));
			}
		}
	}

	private void DestroyLegMeshes(CryptLeg leg)
	{
		foreach (Mesh mesh in leg.Meshes)
		{
			if (mesh != null)
			{
				Destroy(mesh);
			}
		}
		leg.Meshes.Clear();
	}

	// A bone pile (or a draugr's grave mound) raises its dead out of the floor in front of you.
	private void RaiseDead(CryptPile pile)
	{
		BiomeRuntime b = CryptBiome();
		CryptTheme theme = m_crypt!.Theme;
		List<SpawnEntry> raised = m_cryptRaised.TryGetValue(theme.Name, out List<SpawnEntry> r) && r.Count > 0 ? r : b.Monsters;
		if (raised.Count == 0)
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
			SpawnEntry e = SpawnTables.Pick(raised)!;
			Thing t = SpawnCreature(e, lane, z, Kind.Block, 0f, BiomeAt(z), "waiter");
			t.BaseY = -theme.RiseDepth;
			t.Collidable = false;
			t.Cooldown = Mathf.Max(t.Cooldown, 1.1f);
			Vector3 p = t.Go.transform.localPosition;
			t.Go.transform.localPosition = new Vector3(p.x, RoadY(z) + t.BaseY, z);
			m_rising.Add((t, 0f));
			GameObject? dust = FindFx(theme.RiseFx);
			if (dust != null)
			{
				PlayFx(dust, new Vector3(LaneX(lane), RoadY(z) + 0.1f, z), Quaternion.identity, 3f);
			}
		}
		m_audio.Play(theme.RiseSound, 0.9f);
		m_shake = Mathf.Max(m_shake, 0.06f);
		if (theme.PileBurst && pile.Go != null)
		{
			// An egg sac splits open as its brood comes out.
			GameObject? burst = FindFx(theme.BurstFx);
			if (burst != null)
			{
				PlayFx(burst, new Vector3(pile.Side * (CryptHalf - 0.75f), RoadY(pile.Z) + 0.6f, pile.Z), Quaternion.identity, 3f);
			}
			m_audio.Play(theme.BurstSound, 0.9f);
			Destroy(pile.Go);
			pile.Go = null;
		}
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
		m_cryptPressTime = Time.time;
		m_cryptPressDir = step;
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
		if (m_cryptBonk < 0f && m_z >= zc - TurnWindow)
		{
			// Pressed toward the opening just before the window, or already in the lane on its side.
			bool early = m_cryptPressDir == m_crypt.Dirs[k] && Time.time - m_cryptPressTime < 0.6f;
			if (early || m_lane == 1 + m_crypt.Dirs[k])
			{
				m_crypt.Armed[k] = true;
				m_lane = 1;
				m_audio.Play(Sfx.Lane, 0.55f, 0.95f);
				return;
			}
		}
		if (m_cryptBonk < 0f && m_z > zc - TurnPoint && JunctionAtCorner(k) is CryptJunction junction)
		{
			// Not turned at a junction: off down one of its dead ends (whichever lane you're in).
			WrongWay(junction);
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
			Hit(new Thing { Label = "", JumpOrSlide = true, Heavy = true }, $"You ran into the wall of a {m_crypt.Theme.Name}. (Press toward the opening at a corner.)");
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
		// Each corner's turn starts when you press for it (or after bouncing off its wall), and
		// is done by the time you're round it.
		float yaw = 0f;
		for (int i = 0; i < c.Corners.Count; i++)
		{
			bool go = c.Armed[i] || m_z > c.Corners[i] + 4f;
			c.CamTurn[i] = Mathf.MoveTowards(c.CamTurn[i], go ? 1f : 0f, dt / 0.55f);
			if (m_z > c.Corners[i] + 3f)
			{
				c.CamTurn[i] = Mathf.Max(c.CamTurn[i], Mathf.InverseLerp(c.Corners[i] + 3f, c.Corners[i] + 6f, m_z));
			}
			yaw += c.Dirs[i] * 90f * Mathf.SmoothStep(0f, 1f, c.CamTurn[i]);
		}
		// 1.16.112 (SAT: the smoke cue isn't working out): the corner gust is gone.
		RemoveGust();
		Vector3 focus = StagePoint(new Vector3(m_x * 0.6f, c.Floor + m_y * 0.4f, m_z));
		if (!m_cryptFocusSet)
		{
			m_cryptFocus = focus;
			m_cryptFocusSet = true;
		}
		else
		{
			m_cryptFocus = Vector3.Lerp(m_cryptFocus, focus, 1f - Mathf.Exp(-14f * dt));
		}
		// Behind you and a little closer than outside, turning with you at each corner.
		Quaternion turn = m_stage.rotation * Quaternion.Euler(0f, yaw, 0f);
		// Under the roof, where there is one.
		float camUp = Plugin.CameraHeight.Value + 0.6f;
		if (c.Theme.Roofed && Plugin.DungeonRoof.Value)
		{
			camUp = Mathf.Min(camUp, WallH + c.Theme.RoofLift - 0.8f);
		}
		Vector3 pos = m_cryptFocus + turn * new Vector3(0f, camUp, -Plugin.CameraDistance.Value * 0.85f);
		Vector3 look = m_cryptFocus + turn * new Vector3(0f, 1f, 7f);
		Quaternion rot = Quaternion.LookRotation(look - pos);
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
		CryptTheme theme = m_crypt != null ? m_crypt.Theme : s_burialChamber;
		fogColor = Color.Lerp(fogColor, theme.Fog, w);
		fog = Mathf.Lerp(fog, 0.028f, w);
		ambient = Color.Lerp(ambient, theme.Ambient, w);
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
		string name = c.Theme.Name;
		ReleaseCrypt();
		Plugin.Log.LogInfo($"Out of the {name}; stage now at {m_stage.position}.");
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
			DestroyLegMeshes(leg);
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
		m_icicles.Clear();
		ClearDrops();
		m_lurker = null;
		m_cryptOver = 0f;
		m_cryptBonk = -1f;
		m_cryptLagPos = Vector3.zero;
		m_cryptLagYaw = 0f;
	}

	// A new run: no crypt, no torch, the sky back.
	private void ClearCrypt()
	{
		if (m_gust != null)
		{
			m_gust.SetActive(false);
		}
		m_gustCorner = -1;
		m_gustGone = -1f;
		if (m_avatar != null && m_avatar.transform.parent != m_stage)
		{
			m_avatar.transform.SetParent(m_stage, false);
		}
		ReleaseCrypt();
		m_forestDungeons = 0;
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
		GameObject? prefab = Pictures.Prefab(m_crypt != null ? m_crypt.Theme.Chest : "TreasureChest_forestcrypt") ?? Pictures.Prefab("TreasureChest_forestcrypt");
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

	// Each dungeon's own spoils (1.16.79): the first that exists of each list.
	private static string DungeonMaterial(CryptTheme theme) => theme.Name switch
	{
		"burial chamber" => "SurtlingCore",
		"sunken crypt" => "IronScrap|Iron",
		"frost cave" => "Crystal|Obsidian",
		"infested mine" => "BlackCore|Softtissue",
		"troll cave" => "TrollHide",
		"charred fortress" => "FlametalOreNew|FlametalOre|Flametal",
		_ => "",
	};

	private string m_lootText = "";

	private void DungeonLoot(CryptRun c)
	{
		if (m_player == null)
		{
			return;
		}
		int amount = 1 + SegAt(c.Start).Stars;
		string? got = Rewards.GiveDungeonLoot(m_player, DungeonMaterial(c.Theme), amount);
		if (got == null)
		{
			return;
		}
		m_lootText = (m_lootText.Length > 0 ? m_lootText + ", " : "") + got;
		m_audio.Play("sfx_coins_pile_destroyed|sfx_pickup", 0.8f, 1f);
		m_iconPulse = 1f;
		Flash(new Color(1f, 0.85f, 0.4f), 0.35f);
		m_banner = $"The {c.Theme.Name}'s spoils: {got}";
		m_bannerTime = 2.5f;
		Plugin.Log.LogInfo($"Dungeon spoils: {got}.");
	}
}
