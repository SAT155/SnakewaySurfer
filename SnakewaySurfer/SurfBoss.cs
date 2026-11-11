using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// The run as a list of stretches: a biome, then (when its boss is in reach) a boss arena in the
// same biome, then the next biome. A biome with a boss stays open until you hold its summoning
// items; beating the boss saves a checkpoint for the next biome. Also the biome landmarks
// (creature nests, totems, bone piles) and pickables beside the track.
internal sealed partial class SurfGame
{
	private sealed class Segment
	{
		public float Start;
		public float End = float.MaxValue;
		public int Biome;
		public int Stars;
		public bool Boss;
		// The last biome of a run with no loop: it repeats with no boss.
		public bool Repeat;
		public bool Beaten;
		public int PrevBiome;
		public float PrevLevel;
		// The stretch before was a boss arena (its look fades out over this one's start).
		public bool PrevBoss;
		// One fight of the boss rush (after the 3-star lap): every boss in a row.
		public bool Rush;
		public int RushIndex;
		// 1.16: the ocean crossing before the Plains (SurfSea.cs), the stretch of Mountains coast
		// a crossing starts from, and the land just after a crossing.
		public bool Sea;
		public bool Shore;
		public bool PrevSea;
		// 1.16.4: whether this stretch has been given its burial chamber (SurfCrypt.cs).
		public bool CryptChecked;
		// 1.16.10: whether this stretch has had its chance of a trader (SurfTraders.cs).
		public bool TraderChecked;

		public float Level => Biome + 1.5f * Stars;
	}

	private readonly List<Segment> m_segments = new();
	private int m_lapBiomes = 6;
	private int m_startBiome;
	private int m_startStars;
	private bool m_explicitStart;
	private float m_bonusDistance;

	// Summoning items for the open biome stretch.
	private Segment? m_itemSeg;
	private int m_items;
	private float m_nextItemZ;
	private float m_itemNagTime;
	private readonly Dictionary<string, GameObject?> m_itemTemplates = new();

	// The boss fight.
	private BossDef? m_boss;
	private Segment? m_bossSeg;
	private GameObject? m_bossGo;
	private Animator? m_bossAnim;
	private CreatureInfo? m_bossInfo;
	private Thing? m_bossThing;
	private int m_bossHits;
	private float m_bossIntro;
	private float m_bossAttackTimer;
	private float m_bossHitTimer;
	private float m_bossTurn;
	private float m_bossZ;
	private int m_bossSide = -1;
	private float m_bossSink = -1f;
	private int m_bossMove;
	private const int BossHitsNeeded = 3;

	private float m_lastLandmarkZ = -999f;
	private int m_furthestBoss = -1;
	private readonly Dictionary<string, bool> m_bossFound = new();
	// 1 = just moved on and still under the ground, rising to 0.
	private float m_bossRise;

	// ---------------------------------------------------------------- stretches

	private void ResetSegments()
	{
		m_segments.Clear();
		// Read once per run: the world's keys don't change while you surf.
		m_furthestBoss = Plugin.IgnoreBossGate.Value ? Progress.Bosses.Length - 1 : Progress.FurthestBoss();
		int lap = Plugin.LoopBiomes.Value <= 0 ? m_biomes.Count : Mathf.Clamp(Plugin.LoopBiomes.Value, 1, m_biomes.Count);
		m_lapBiomes = Mathf.Clamp(Progress.AllowedBiomes(lap), 1, m_biomes.Count);
		int biome = Mathf.Clamp(m_startBiome, 0, m_lapBiomes - 1);
		int stars = Plugin.LoopBiomes.Value <= 0 ? 0 : m_startStars;
		// A start past the Mountains begins with the crossing, until you've made it this lap
		// (or picked the Ocean).
		// The self-test picks its stops itself: only its crossing stop starts at sea.
		bool sea = Crossing && biome == SeaTo && (m_startSea || (!Touring && (m_player == null || !Progress.Landed(m_player, stars, SeaTo))));
		Segment first = sea ? ShoreSegment(stars) : NewSegment(0f, biome, stars, false, false);
		first.PrevBiome = first.Biome;
		first.PrevLevel = first.Level;
		m_segments.Add(first);
		m_itemSeg = null;
		m_items = 0;
		m_nextItemZ = 0f;
		m_itemStartSeg = null;
		m_bonusDistance = 0f;
		EndBoss();
	}

	private Segment NewSegment(float start, int biome, int stars, bool boss, bool repeat)
	{
		Segment s = new Segment { Start = start, Biome = biome, Stars = stars, Boss = boss, Repeat = repeat };
		// A biome with a boss ahead stays open until you have the summoning items.
		s.End = boss || BossFor(s) != null ? float.MaxValue : start + Plugin.BiomeLength.Value;
		return s;
	}

	private BossDef? BossFor(Segment s)
	{
		if (s.Boss || s.Repeat || s.Sea || s.Shore || !Plugin.BossFights.Value)
		{
			return null;
		}
		BossDef? boss = Progress.BossOf(s.Biome);
		if (boss == null || s.Biome > m_furthestBoss)
		{
			return null;
		}
		if (!m_bossFound.TryGetValue(boss.Prefab, out bool found))
		{
			found = Pictures.Prefab(boss.Prefab) != null;
			m_bossFound[boss.Prefab] = found;
		}
		return found ? boss : null;
	}

	// Pure: also used to look ahead (the checkpoint after a boss), so it must not change state.
	private Segment Following(Segment s)
	{
		if (s.Rush)
		{
			List<int> rush = RushBosses();
			if (s.RushIndex + 1 < rush.Count)
			{
				return RushSegment(s.End, rush[s.RushIndex + 1], s.RushIndex + 1);
			}
			// Done: back to the 3-star lap.
			return NewSegment(s.End, 0, 3, false, false);
		}
		if (!s.Boss && BossFor(s) != null)
		{
			return NewSegment(s.End, s.Biome, s.Stars, true, false);
		}
		int biome = s.Biome + 1;
		int stars = s.Stars;
		bool repeat = false;
		if (Crossing && !s.Sea && biome == SeaTo && biome < m_lapBiomes)
		{
			// Across the sea to the Plains.
			return SeaSegment(s.End, s.Stars);
		}
		if (biome >= m_lapBiomes)
		{
			if (Plugin.LoopBiomes.Value <= 0 || m_lapBiomes <= 0)
			{
				biome = s.Biome;
				repeat = true;
			}
			else
			{
				// The end of the 3-star lap: the boss rush, once a run.
				if (s.Stars >= 3 && !m_segments.Exists(x => x.Rush) && RushBosses() is { Count: > 0 } rush)
				{
					return RushSegment(s.End, rush[0], 0);
				}
				biome = 0;
				stars = Mathf.Min(3, stars + 1);
			}
		}
		return NewSegment(s.End, biome, stars, false, repeat);
	}

	private Segment RushSegment(float start, int biome, int index) =>
		new Segment { Start = start, Biome = biome, Stars = 3, Boss = true, Rush = true, RushIndex = index };

	// The bosses of the rush, in order: every one in this lap you could fight.
	private List<int> RushBosses()
	{
		List<int> list = new();
		for (int b = 0; b < m_lapBiomes; b++)
		{
			if (BossFor(new Segment { Biome = b }) != null)
			{
				list.Add(b);
			}
		}
		return list;
	}

	// A rush with every boss of the game earns the golden shoes.
	// (The six of the usual lap: 1.16.13's Fader, past it, isn't needed for them.)
	private bool FullRush() => RushBosses().Count >= Mathf.Min(6, Progress.Bosses.Length);

	private Segment SegAt(float z)
	{
		if (m_segments.Count == 0)
		{
			ResetSegments();
		}
		// Add stretches as the track reaches them.
		int guard = 0;
		while (m_segments[m_segments.Count - 1].End <= z && guard++ < 64)
		{
			Segment last = m_segments[m_segments.Count - 1];
			Segment next = Following(last);
			next.PrevBiome = last.Biome;
			next.PrevLevel = last.Level;
			next.PrevBoss = last.Boss;
			next.PrevSea = last.Sea;
			if (last.Sea)
			{
				// A new land over the water: no blend with the one left behind.
				next.PrevBiome = next.Biome;
				next.PrevLevel = next.Level;
			}
			m_segments.Add(next);
		}
		for (int i = m_segments.Count - 1; i >= 0; i--)
		{
			if (z >= m_segments[i].Start)
			{
				return m_segments[i];
			}
		}
		return m_segments[0];
	}

	// Biomes passed so far, with the fraction of this one: speed and difficulty follow it.
	private float ProgressAt(float z)
	{
		Segment s = SegAt(z);
		// The rush counts as past the whole 3-star lap.
		float ordinal = s.Rush ? (s.Stars + 1) * Mathf.Max(1, m_lapBiomes) : s.Stars * Mathf.Max(1, m_lapBiomes) + s.Biome;
		float frac = s.Boss ? 1f : Mathf.Clamp01((z - s.Start) / Plugin.BiomeLength.Value);
		return ordinal + frac;
	}

	// What the run speed follows: each lap starts slow again (a star adds about one biome's
	// worth), so a star lap isn't a sprint from its first metre.
	private float SpeedProgressAt(float z)
	{
		Segment s = SegAt(z);
		float frac = s.Boss ? 1f : Mathf.Clamp01((z - s.Start) / Plugin.BiomeLength.Value);
		return s.Rush ? Mathf.Max(1, m_lapBiomes) + 3f : s.Biome + frac + s.Stars;
	}

	// Ends an open stretch at the first ground tile edge past minZ and clears what was already
	// laid out beyond it, so the next stretch is built fresh.
	private void CloseSegment(Segment s, float minZ)
	{
		float end = Mathf.Ceil(minZ / GroundTile) * GroundTile;
		s.End = end;
		for (int i = m_things.Count - 1; i >= 0; i--)
		{
			Thing t = m_things[i];
			bool beyond = t.Kind == Kind.Ground ? t.Z - GroundTile * 0.5f >= end - 0.01f : t.Z > end;
			if (beyond)
			{
				DestroyThing(t);
				m_things.RemoveAt(i);
			}
		}
		m_nextGroundZ = Mathf.Min(m_nextGroundZ, end);
		m_nextSceneryZ = Mathf.Min(m_nextSceneryZ, end);
		if (m_nextRowZ > end)
		{
			m_nextRowZ = end + 14f;
		}
		m_nextItemZ = Mathf.Min(m_nextItemZ, end);
		m_nextTorchZ = Mathf.Min(m_nextTorchZ, end);
		// Root walls splitting the lanes end where the stretch does.
		for (int i = m_dividers.Count - 1; i >= 0; i--)
		{
			(float z0, float z1, int boundary) = m_dividers[i];
			if (z0 >= end)
			{
				m_dividers.RemoveAt(i);
			}
			else if (z1 > end)
			{
				m_dividers[i] = (z0, end, boundary);
			}
		}
	}

	// Each frame while running: items, the gate out of the biome, and the boss.
	private void UpdateStretch(float dt)
	{
		Segment here = SegAt(m_z);
		BossDef? boss = BossFor(here);
		if (boss != null && here.End == float.MaxValue)
		{
			if (m_itemSeg != here)
			{
				m_itemSeg = here;
				m_items = 0;
				// A good stretch of the biome first, then the items start turning up.
				m_nextItemZ = Mathf.Max(m_nextItemZ, Mathf.Max(m_z + 30f, here.Start + Plugin.BiomeLength.Value * 0.45f));
			}
			if (m_items >= boss.Amount)
			{
				// 1.16.19: far enough on that the track already built doesn't vanish in plain view.
				CloseSegment(here, Mathf.Max(here.Start + Plugin.BiomeLength.Value, m_z + Mathf.Max(55f, AheadDistance * 0.5f)));
				m_banner = $"{boss.Name} approaches!";
				m_bannerTime = 2.5f;
				m_audio.Play(Sfx.Biome, 1f, 0.7f);
			}
			else if (m_z > here.Start + Plugin.BiomeLength.Value && !(m_z < CryptExitFor(here) + 10f))
			{
				m_itemNagTime -= dt;
				if (m_itemNagTime <= 0f)
				{
					m_itemNagTime = 9f;
					int left = boss.Amount - m_items;
					m_banner = $"Find {left} more {ItemName(boss.Item)} to face {boss.Name}!";
					m_bannerTime = 2.5f;
					// A nudge at the item's icon instead of words.
					m_iconPulse = 1f;
				}
			}
		}
		if (here.Boss && !here.Beaten && m_bossSeg != here && m_bossSink < 0f)
		{
			StartBoss(here);
		}
		UpdateBoss(dt);
	}

	// 1.16.1 (SAT): where the first summoning item of a stretch may come up: 30 to 50 seconds
	// into it, never right as the biome begins.
	private Segment? m_itemStartSeg;
	private float m_itemStartZ;

	// Summoning items come up in place of some coins while you still need them.
	private bool MaybeSpawnItem(int lane, float z)
	{
		Segment s = SegAt(z);
		BossDef? boss = BossFor(s);
		if (boss != null && m_itemStartSeg != s)
		{
			m_itemStartSeg = s;
			m_itemStartZ = s.Start + Mathf.Max(Plugin.BiomeLength.Value * 0.45f, Mathf.Max(8f, m_speed) * Random.Range(30f, 50f));
		}
		// 1.16.109 (SAT): with a dungeon in the biome, not before you're out the far end of it.
		if (boss != null && !float.IsNaN(CryptExitFor(s)) && z < CryptExitFor(s) + 10f)
		{
			return false;
		}
		// A good stretch of the biome first (also before the stretch's own bookkeeping starts).
		if (boss == null || s.End != float.MaxValue || z < m_nextItemZ || z < m_itemStartZ || (s == m_itemSeg && m_items >= boss.Amount))
		{
			return false;
		}
		GameObject? template = ItemTemplate(boss.Item);
		if (template == null)
		{
			return false;
		}
		// 1.16.1 (SAT): the fewer a boss needs, the rarer each one is (two deer trophies are far
		// apart; ten withered bones come as before).
		float spacing = Mathf.Max(16f, Plugin.BiomeLength.Value / (boss.Amount * 1.7f)) * Mathf.Clamp(6f / Mathf.Max(1, boss.Amount), 1f, 3f);
		if (z > s.Start + Plugin.BiomeLength.Value)
		{
			spacing *= 0.5f;
		}
		m_nextItemZ = z + spacing * Random.Range(0.8f, 1.2f) * (Has("boar") ? 0.75f : 1f);
		SpawnPickup(template, lane, z, Kind.Item);
		return true;
	}

	private GameObject? ItemTemplate(string names)
	{
		if (m_itemTemplates.TryGetValue(names, out GameObject? cached))
		{
			return cached;
		}
		GameObject? found = null;
		foreach (string name in names.Split('|'))
		{
			GameObject? prefab = Pictures.Prefab(name.Trim()) ?? Pictures.Item(name.Trim());
			if (prefab != null)
			{
				found = Picture(prefab);
				break;
			}
		}
		if (found == null)
		{
			Plugin.Log.LogWarning($"SnakewaySurfer: none of these items were found: {names}");
		}
		m_itemTemplates[names] = found;
		return found;
	}

	private static string ItemName(string names)
	{
		foreach (string name in names.Split('|'))
		{
			GameObject? prefab = Pictures.Prefab(name.Trim());
			ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null!;
			if (drop != null && drop.m_itemData?.m_shared != null)
			{
				string n = drop.m_itemData.m_shared.m_name;
				return Localization.instance != null ? Localization.instance.Localize(n) : n;
			}
		}
		return names.Split('|')[0];
	}

	private Thing SpawnPickup(GameObject template, int lane, float z, Kind kind)
	{
		bool weapon = kind == Kind.BossHit;
		// 1.16.150 (SAT: hard to see in the Eikthyr fight): what you hurt a boss with is bigger,
		// stands in a pillar of light, and glows brighter.
		GameObject go = Fitted(template, weapon ? Quaternion.Euler(0f, 0f, 35f) : Quaternion.identity, Vector3.one * (weapon ? 1.5f : 0.9f), true, 3f);
		const float y = 1.0f;
		go.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + y, z);
		if (weapon)
		{
			PickupBeam(go.transform, y);
		}
		Light glow = new GameObject("Glow").AddComponent<Light>();
		glow.gameObject.layer = m_layer;
		glow.transform.SetParent(go.transform, false);
		glow.type = LightType.Point;
		glow.range = weapon ? 6f : 3.5f;
		glow.intensity = weapon ? 2.6f : 1.6f;
		glow.color = kind == Kind.BossHit ? new Color(0.5f, 0.8f, 1f) : new Color(1f, 0.85f, 0.4f);
		glow.cullingMask = 1 << m_layer;
		Thing t = new Thing { Go = go, Kind = kind, Lane = lane, Z = z, Y = y, HalfLength = 0.45f };
		m_things.Add(t);
		return t;
	}

	private static Material? s_beamMaterial;

	// A soft column of light up from the ground through a pickup, so it reads from far off.
	private void PickupBeam(Transform parent, float y)
	{
		if (s_beamMaterial == null)
		{
			Shader shader = Shader.Find("Sprites/Default");
			if (shader == null)
			{
				return;
			}
			s_beamMaterial = new Material(shader) { name = "SurfPickupBeam", color = new Color(0.55f, 0.85f, 1f, 0.28f), renderQueue = 3110 };
		}
		GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		Destroy(beam.GetComponent<Collider>());
		beam.name = "PickupBeam";
		beam.layer = m_layer;
		// (The fitted wrapper is upright and scaled evenly; undo its scale.)
		float k = Mathf.Max(0.01f, parent.localScale.x);
		beam.transform.SetParent(parent, false);
		beam.transform.localRotation = Quaternion.identity;
		beam.transform.localScale = new Vector3(0.5f / k, 3f / k, 0.5f / k);
		beam.transform.localPosition = new Vector3(0f, (3f - y) / k, 0f);
		Renderer r = beam.GetComponent<Renderer>();
		r.sharedMaterial = s_beamMaterial;
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		r.receiveShadows = false;
		GameObject patch = MakeMarker(1, 0f, 1.6f, s_beamMaterial);
		patch.transform.SetParent(parent, false);
		patch.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
		patch.transform.localScale = new Vector3(LaneWidth * 0.92f / k, 1.6f / k, 1f);
		patch.transform.localPosition = new Vector3(0f, (0.08f - y) / k, 0f);
	}

	private void PickedUpItem()
	{
		Segment here = SegAt(m_z);
		BossDef? boss = BossFor(here);
		if (boss == null)
		{
			return;
		}
		if (m_itemSeg != here)
		{
			m_itemSeg = here;
			m_items = 0;
		}
		m_items++;
		m_iconPulse = 1f;
		m_audio.Play(Sfx.Coin, 1f, 0.8f);
		if (m_items >= boss.Amount)
		{
			m_audio.Play(Sfx.Biome, 0.8f, 0.85f);
		}
		if (m_items < boss.Amount)
		{
			m_banner = $"{ItemName(boss.Item)} {m_items}/{boss.Amount}";
			m_bannerTime = 1.2f;
		}
	}

	// ---------------------------------------------------------------- the boss

	private void StartBoss(Segment seg)
	{
		m_quietToBoss = false;
		EndBoss();
		ResetSpears();
		BossDef? def = Progress.BossOf(seg.Biome);
		GameObject? prefab = def != null ? Pictures.Prefab(def.Prefab) : null;
		if (def == null || prefab == null)
		{
			CloseSegment(seg, m_z + 20f);
			return;
		}
		ResetTotems(def);
		m_boss = def;
		m_bossSeg = seg;
		m_bossInfo = CreatureInfo.Of(prefab);
		float scale = m_bossInfo.Height > 0.3f ? Mathf.Min(1f, def.MaxHeight / m_bossInfo.Height) : 1f;
		m_chase = Chases(def);
		m_bossScale = scale;
		m_bossGo = Creature(prefab, Quaternion.identity, scale);
		m_bossAnim = m_bossGo.GetComponentInChildren<Animator>();
		if (m_bossAnim != null)
		{
			SetParam(m_bossAnim, "forward_speed", 0f);
			if (def.Mode != "fly")
			{
				SetParam(m_bossAnim, "onGround", true);
			}
			SetBool(m_bossAnim, "flying", def.Mode == "fly");
		}
		if (Flies(def))
		{
			StartFlight(def);
		}
		m_bossHits = 0;
		m_bossIntro = 0f;
		m_bossAttackTimer = 2.5f;
		m_bossHitTimer = 3.5f;
		m_bossMove = 0;
		m_bossSide = -1;
		m_bossZ = m_chase ? m_z - ChaseDistance(def) - 20f : m_z + def.Distance + 40f;
		m_bossOff = m_bossZ - m_z;
		m_bossThing = new Thing
		{
			Go = m_bossGo,
			Kind = Kind.Scenery,
			Lane = 1,
			Z = m_bossZ,
			Label = Localize(prefab.GetComponent<Character>()?.m_name ?? def.Name),
			Info = m_bossInfo,
			Anim = m_bossAnim,
			Attack = m_bossInfo.PickRanged() ?? m_bossInfo.PickMelee(),
			Height = def.MaxHeight,
			Level = seg.Stars + 1,
		};
		ApplyStars(m_bossThing, seg.Stars);
		// Its weather and altar effects come from its location.
		LocationAsset(def.Location);
		if (Plugin.BossSummon.Value)
		{
			StartSummon(def);
		}
		if (seg.Rush && seg.RushIndex == 0)
		{
			m_banner = $"BOSS RUSH!\nEvery boss in a row, starting with {def.Name}!";
			m_bannerTime = 2.5f;
		}
		else if (!Plugin.BossSummon.Value)
		{
			m_banner = $"{def.Name}!";
			m_bannerTime = 2.5f;
		}
		if (!Plugin.BossSummon.Value && m_bossInfo.AlertSound.Length > 0)
		{
			m_audio.Play(m_bossInfo.AlertSound, 1f);
		}
		Plugin.Log.LogInfo($"Boss fight: {def.Name} ({prefab.name}, scale {scale:0.00}, {seg.Stars} stars).");
	}

	private static string Localize(string text) => Localization.instance != null ? Localization.instance.Localize(text) : text;

	// 1.16.150 (SAT: little difference between star levels): from 2 stars a boss sometimes
	// follows one move straight up with another (unless the move held the next one back).
	private void StarFollowUp(int stars, float interval)
	{
		if (stars >= 2 && m_bossAttackTimer <= interval + 0.01f && Random.value < 0.22f * (stars - 1))
		{
			m_bossAttackTimer = 0.8f;
		}
	}

	private void EndBoss()
	{
		m_dash = null;
		m_dashBack = 0f;
		EndTotemBeam();
		m_totems = 0;
		foreach (var m in m_meteors)
		{
			if (m.go != null)
			{
				Destroy(m.go);
			}
		}
		m_meteors.Clear();
		if (m_bossGo != null)
		{
			Destroy(m_bossGo);
		}
		m_bossGo = null;
		m_bossAnim = null;
		m_bossThing = null;
		m_boss = null;
		m_bossSeg = null;
		m_bossSink = -1f;
		m_bossHidden = 0f;
		EndDuel(false);
		EndCombo();
		m_recede = 0f;
		ClearChase();
		ClearSummon();
		ClearIce();
		m_chase = false;
	}

	private void UpdateBoss(float dt)
	{
		if (m_boss == null || m_bossGo == null || m_bossThing == null)
		{
			return;
		}
		UpdateBossHidden(dt);
		UpdateVines(dt);
		UpdateIce(dt);
		UpdateMeteors(dt);
		UpdateTotemBeam(dt);
		BossDef def = m_boss;
		if (m_bossSink >= 0f)
		{
			// Beaten: it sinks away while you run on.
			m_bossSink += dt;
			if (!m_chase || m_bossSink < 1.6f)
			{
				m_bossZ += m_speed * dt;
			}
			// It reels, then topples onto its side, lies there a moment, then sinks into the ground
			// (a flyer falls out of the sky first).
			Vector3 p = m_bossGo.transform.localPosition;
			float fall = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((m_bossSink - 0.4f) / 1.1f));
			float fly = (Flies(def) ? m_bossFlyNow : def.Fly) * (1f - fall);
			float sink = Mathf.Max(0f, m_bossSink - 2.8f) * 2f;
			m_bossGo.transform.localPosition = new Vector3(p.x, RoadY(m_bossZ) + fly - sink, m_bossZ);
			m_bossGo.transform.localRotation = m_bossDeathRot * Quaternion.Euler(0f, 0f, m_bossDeathSide * 82f * fall);
			if (m_bossSink > 1.5f && m_bossAnim != null)
			{
				m_bossAnim.speed = Mathf.MoveTowards(m_bossAnim.speed, 0f, dt * 2f);
			}
			if (m_bossSink > 1.45f && m_bossSink - dt <= 1.45f)
			{
				m_shake = Mathf.Max(m_shake, 0.35f);
				m_audio.Play(Sfx.TrollSlam, 1f, 0.7f);
			}
			if (m_bossSink > 5f)
			{
				EndBoss();
			}
			return;
		}

		if (m_summon >= 0f)
		{
			UpdateSummon(dt, def);
			return;
		}
		if (DuelBusy)
		{
			UpdateDuel(dt, def);
			return;
		}
		if (m_chase)
		{
			UpdateChaseBoss(dt, def);
			return;
		}
		m_bossIntro = Mathf.Min(1f, m_bossIntro + dt * 0.6f);
		float want = m_z + BossDistance(def);
		if (m_bossIntro < 1f)
		{
			m_bossZ = Mathf.Lerp(m_bossZ, want, dt * (def.Mode == "stand" ? 2f : 1.5f));
		}
		else
		{
			// It keeps its distance ahead (1.13: a stander no longer sinks and rises on every
			// move); 1.16.2: when that distance changes (Moder landing or taking off) it gets
			// there smoothly instead of jumping.
			m_bossZ = m_z + Mathf.MoveTowards(m_bossZ - m_z, want - m_z, dt * 14f);
		}
		float x = def.Mode == "side" ? m_bossSide * (TrackHalf + 6f) : Flies(def) ? FlightSway() : 0f;
		float lift = Flies(def) ? FlightHeight(def, dt) : def.Fly;
		// After moving on it comes up out of the ground.
		m_bossRise = Mathf.Max(0f, m_bossRise - dt * 1.4f);
		Vector3 pos = new Vector3(Mathf.MoveTowards(m_bossGo.transform.localPosition.x, x, dt * 6f), RoadY(m_bossZ) + lift - def.MaxHeight * 1.1f * m_bossRise, m_bossZ);
		m_bossGo.transform.localPosition = pos;
		m_bossThing.Z = m_bossZ;

		// Runners face away and turn round to attack; the others face you or the track.
		m_bossTurn = Mathf.Max(0f, m_bossTurn - dt);
		float yaw = def.Mode switch
		{
			// 1.16.2: Moder flies on ahead, or runs on the track when she's down, and turns to
			// attack like the runners.
			"fly" or "run" => m_bossTurn > 0f ? 180f : 0f,
			"side" => m_bossTurn > 0f ? (m_bossSide < 0 ? 90f : -90f) : 0f,
			_ => 180f,
		};
		Quaternion target = Quaternion.Euler(0f, yaw, 0f);
		m_bossGo.transform.localRotation = Quaternion.RotateTowards(m_bossGo.transform.localRotation, target, 540f * dt);
		if (m_bossAnim != null)
		{
			bool moving = def.Mode != "stand";
			SetParam(m_bossAnim, "forward_speed", moving && m_bossTurn <= 0f ? m_speed : 0f);
		}

		if (m_bossIntro < 1f)
		{
			return;
		}
		int phase = Mathf.Clamp(m_bossHits, 0, def.Moves.Length - 1);
		m_bossAttackTimer -= dt;
		if (m_bossAttackTimer <= 0f)
		{
			BossMove(def, Flies(def) && m_bossLanded ? LandedMove() : NextMove(def, phase));
			int bossStars = m_bossSeg?.Stars ?? 0;
			float interval = (phase == 0 ? 2.3f : phase == 1 ? 1.8f : 1.4f) / (1f + 0.2f * bossStars);
			// Some moves (branches, the dash) hold the next one back themselves.
			m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, interval);
			StarFollowUp(bossStars, interval);
		}
		m_bossHitTimer -= dt;
		if (m_bossHitTimer <= 0f)
		{
			m_bossHitTimer = 7f;
			// 1.16.2: Moder down on the track brings a silver atgeir to fight her up close; in
			// the air it's obsidian arrows for the bow.
			bool atgeir = Flies(def) && m_bossLanded;
			GameObject? template = ItemTemplate(atgeir ? ModerWeapon : def.HitItem);
			if (template != null)
			{
				Thing pickup = SpawnPickup(template, Random.Range(0, 3), m_z + m_speed * 3f + 6f, Kind.BossHit);
				if (atgeir)
				{
					Silver(pickup.Go);
				}
			}
		}
	}

	// The next of its moves in this phase (each star lap adds one more trick to every phase).
	private string NextMove(BossDef def, int phase)
	{
		List<string> moves = new(def.Moves[phase]);
		int stars = m_bossSeg?.Stars ?? 0;
		for (int i = 0; i < Mathf.Min(stars, def.StarMoves.Length); i++)
		{
			moves.Add(def.StarMoves[i]);
		}
		return moves[m_bossMove++ % moves.Count];
	}

	private void BossAnimate(string? animation = null)
	{
		m_bossTurn = 1.1f;
		// 1.16.2: a move that is one of its own attacks plays that attack's clip.
		if (m_bossAnim != null && animation != null && SetTrigger(m_bossAnim, animation) != null)
		{
			return;
		}
		if (m_bossAnim != null && m_bossInfo != null)
		{
			List<CreatureAttack> all = new(m_bossInfo.Melee);
			all.AddRange(m_bossInfo.Ranged);
			if (all.Count > 0)
			{
				SetTrigger(m_bossAnim, all[Random.Range(0, all.Count)].Animation);
			}
		}
	}

	// Where a move lands: this far ahead of you, so you see the marks for about `seconds`.
	// 1.16.2 (SAT: boss attacks popped into existence too close): BossLead metres further down
	// the track, so they show well before you reach them. A boss standing on the track still
	// lands its blows between you and it.
	private const float BossLead = 10f;

	private float AheadZ(float seconds)
	{
		float z = NearZ(seconds) + BossLead;
		if (m_boss != null && !m_chase && m_boss.Mode == "stand" && m_bossGo != null)
		{
			z = Mathf.Min(z, Mathf.Max(NearZ(seconds), m_bossZ - 4f));
		}
		return z;
	}

	// Without the lead: for what reaches out from the boss towards you (Moder's frost breath).
	private float NearZ(float seconds) => m_z + m_speed * seconds + 3f;

	// How far ahead it keeps (Moder flies further off than she runs once she's down).
	private float BossDistance(BossDef def) => Flies(def) ? ModerDistance(def) : def.Distance;

	private int NearLane(int lane) => lane == 0 ? 1 : lane == 2 ? 1 : (Random.value < 0.5f ? 0 : 2);

	private void BossMove(BossDef def, string move)
	{
		Thing boss = m_bossThing!;
		BiomeRuntime b = m_biomes[BiomeAt(m_z)];
		string arg = "";
		int colon = move.IndexOf(':');
		if (colon > 0)
		{
			arg = move.Substring(colon + 1);
			move = move.Substring(0, colon);
		}
		if (move != "land" && move != "takeoff")
		{
			BossAnimate(BossAnimationFor(move));
		}
		switch (move)
		{
			case "icevolley":
				IceVolley(def, false);
				break;
			case "icewide":
				IceVolley(def, true);
				break;
			case "icebreath":
				IceBreath(def);
				break;
			case "claws":
				// Down on the track: she wheels round and rakes the ground ahead of her with
				// claws or jaws, your lane first.
				BossStrike(def, Random.value < 0.5f ? new[] { m_lane } : new[] { m_lane, NearLane(m_lane) }, AheadZ(1.2f), 2.2f, "strike");
				break;
			case "land":
				LandOnTrack(def);
				break;
			case "takeoff":
				TakeWing();
				break;
			case "shot2":
				// Two shots at once: your lane and the one beside it.
				if (boss.Info != null && boss.Info.PickRanged() is CreatureAttack r2 && r2.Projectile != null)
				{
					CreatureAttack keep = boss.Attack!;
					boss.Attack = r2;
					boss.Z = Mathf.Min(m_bossZ, m_z + 30f);
					int was = m_lane;
					FireShot(boss);
					m_lane = NearLane(was);
					FireShot(boss);
					m_lane = was;
					boss.Attack = keep;
				}
				else
				{
					BossStrike(def, new[] { m_lane, NearLane(m_lane) }, AheadZ(1.4f), 2.2f, "strike");
				}
				break;
			case "gaswall":
				BossStrike(def, new[] { m_lane, NearLane(m_lane) }, AheadZ(1.8f), 8f, "gas");
				break;
			case "firewall":
				BossStrike(def, new[] { m_lane, NearLane(m_lane) }, AheadZ(1.8f), 8f, "wall");
				break;
			case "lava":
				// 1.16.13: Fader's lava, a pool down your lane (and maybe the next) or a channel
				// across the track, glowing well ahead.
				if (Random.value < 0.35f)
				{
					LavaChannel(AheadZ(1.7f));
				}
				else
				{
					LavaPool(AheadZ(1.9f), m_lane);
				}
				break;
			case "dash":
				BossDash(def);
				break;
			case "shot":
				if (boss.Info != null && boss.Info.PickRanged() is CreatureAttack ranged && ranged.Projectile != null)
				{
					CreatureAttack keep = boss.Attack!;
					boss.Attack = ranged;
					boss.Z = Mathf.Min(m_bossZ, m_z + 30f);
					FireShot(boss);
					boss.Attack = keep;
				}
				else
				{
					BossStrike(def, new[] { m_lane }, AheadZ(1.3f), 2.2f, "strike");
				}
				break;
			case "stomp":
				boss.Z = Mathf.Min(m_bossZ, m_z + 28f);
				Shockwave(boss);
				break;
			case "strike1":
				BossStrike(def, new[] { m_lane }, AheadZ(1.4f), 2.2f, "strike");
				break;
			case "strike2":
				BossStrike(def, new[] { m_lane, NearLane(m_lane) }, AheadZ(1.5f), 2.2f, "strike");
				break;
			case "gap":
			{
				int open = Random.Range(0, 3);
				List<int> lanes = new();
				for (int l = 0; l < 3; l++)
				{
					if (l != open)
					{
						lanes.Add(l);
					}
				}
				BossStrike(def, lanes.ToArray(), AheadZ(1.7f), 2.2f, "strike");
				break;
			}
			case "wall":
				// Two lanes closed for a long stretch: read the open one early.
				BossStrike(def, new[] { m_lane, NearLane(m_lane) }, AheadZ(1.8f), 9f, "wall");
				break;
			case "sweep":
			{
				// Breath down two neighbouring lanes, starting on yours.
				int other = m_lane == 1 ? (Random.value < 0.5f ? 0 : 2) : 1;
				BossStrike(def, new[] { m_lane, other }, AheadZ(1.5f), 5f, "breath");
				break;
			}
			case "blobs":
			case "meteors":
			{
				// Two or three single-lane hits at different distances, always passable.
				int count = Random.Range(2, 4);
				for (int k = 0; k < count; k++)
				{
					BossStrike(def, new[] { k == 0 ? m_lane : Random.Range(0, 3) }, AheadZ(1.4f + 0.55f * k), 2.2f, move == "blobs" ? "gas" : "meteor");
				}
				break;
			}
			case "jumprow":
				for (int l = 0; l < 3; l++)
				{
					BossLow(b, l, AheadZ(1.6f), def);
				}
				break;
			case "slide":
				for (int l = 0; l < 3; l++)
				{
					BossHigh(b, l, AheadZ(1.6f), def);
				}
				break;
			case "totembeam":
				StartTotemBeam(def);
				break;
			case "beam":
				for (int l = 0; l < 3; l++)
				{
					if (Random.value < 0.5f)
					{
						BossLow(b, l, AheadZ(1.6f), def);
					}
					else
					{
						BossHigh(b, l, AheadZ(1.6f), def);
					}
				}
				break;
			case "lob2":
			{
				int[] lanes = { m_lane, NearLane(m_lane) };
				float z = AheadZ(1.3f);
				Lob lob = new Lob
				{
					Kind = "fire",
					Lanes = lanes,
					Z = z,
					Label = boss.Label,
					Attack = boss.Attack,
					From = m_bossGo!.transform.localPosition + Vector3.up * def.MaxHeight * 0.7f,
					To = new Vector3((LaneX(lanes[0]) + LaneX(lanes[1])) * 0.5f, RoadY(z) + 0.3f, z),
				};
				foreach (int lane in lanes)
				{
					lob.Markers.Add(MakeMarker(lane, z, 2.4f, m_markerMaterial));
				}
				GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
				Destroy(visual.GetComponent<Collider>());
				visual.layer = m_layer;
				visual.transform.localScale = Vector3.one * 0.9f;
				Renderer r = visual.GetComponent<Renderer>();
				r.sharedMaterial = TintMaterial(def.Tint);
				lob.Go = visual;
				lob.Go.transform.SetParent(m_stage, false);
				lob.Go.transform.localPosition = lob.From;
				m_lobs.Add(lob);
				break;
			}
			case "minions":
			{
				string who = arg.Length > 0 ? arg : def.Minion;
				GameObject? prefab = who.Length > 0 ? Pictures.Monster(who) : null;
				SpawnEntry? e = prefab != null ? new SpawnEntry { Prefab = prefab, Label = SpawnTables.Label(prefab) } : SpawnTables.Pick(b.Monsters);
				if (e != null)
				{
					int open = Random.Range(0, 3);
					for (int l = 0; l < 3; l++)
					{
						if (l != open)
						{
							SpawnCreature(e, l, m_z + m_speed * 2.2f + 8f, Kind.Block, 0f, BiomeAt(m_z), "charger");
						}
					}
				}
				break;
			}
			case "branches":
				// 1.16.1 (SAT): the Elder's maze of six paths, longer as the fight goes on.
				SpawnMaze(def, b, m_bossHits >= 2 ? 8 : 6);
				break;
			case "grab":
			{
				// It reaches out of the wall on its side: the lane nearest it glows first.
				int lane = m_bossSide < 0 ? 0 : 2;
				BossStrike(def, new[] { lane, Random.value < 0.4f ? 1 : lane }, AheadZ(1.3f), 2.2f, "strike");
				break;
			}
		}
	}

	private readonly Dictionary<Color, Material> m_tints = new();
	private float m_gasSfxAt = -99f;

	private Material TintMaterial(Color c)
	{
		if (!m_tints.TryGetValue(c, out Material mat) || mat == null)
		{
			Shader shader = Shader.Find("Sprites/Default");
			mat = new Material(shader) { color = new Color(c.r, c.g, c.b, 0.55f), renderQueue = 3100 };
			m_tints[c] = mat;
		}
		return mat;
	}

	// A marked hit on some lanes at a fixed spot of track: switch lane. The marks show at once;
	// the strike (a pillar in the boss's colour, plus its own attack effect) shows as you arrive.
	private void BossStrike(BossDef def, int[] lanes, float z, float length, string look)
	{
		string biome = m_biomes[BiomeAt(z)].Def.Name;
		HashSet<int> done = new();
		foreach (int lane in lanes)
		{
			if (!done.Add(lane))
			{
				continue;
			}
			GameObject root = new GameObject("BossStrike");
			root.layer = m_layer;
			root.transform.SetParent(m_stage, false);
			root.transform.localPosition = new Vector3(LaneX(lane), RoadY(z), z);
			GameObject marker = MakeMarker(lane, z + length * 0.5f - 1f, length, m_markerMaterial);
			marker.transform.SetParent(root.transform, true);
			GameObject strike;
			if (look == "gas")
			{
				strike = MakeCloud(lane, z);
				if (length > 3f)
				{
					// A long poison cloud lying down the lane.
					Vector3 cs = strike.transform.localScale;
					strike.transform.localScale = new Vector3(cs.x, cs.y, length);
					strike.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + 0.9f, z + length * 0.5f - 1f);
				}
			}
			else if (look == "meteor")
			{
				// 1.16.102 (SAT: meteors didn't look like the game's): the game's own meteor falls
				// out of the sky onto the marked spot and bursts with its own impact.
				strike = new GameObject("MeteorStrike");
				strike.layer = m_layer;
				strike.transform.SetParent(m_stage, false);
				strike.transform.localPosition = new Vector3(LaneX(lane), RoadY(z), z);
				float land = Mathf.Max(0.3f, (z - m_z) / Mathf.Max(1f, m_speed)) - 0.5f;
				DropMeteor(new Vector3(LaneX(lane), RoadY(z), z), Mathf.Max(0.05f, land));
			}
			else
			{
				strike = GameObject.CreatePrimitive(look == "wall" ? PrimitiveType.Cube : PrimitiveType.Cylinder);
				Destroy(strike.GetComponent<Collider>());
				strike.layer = m_layer;
				strike.transform.SetParent(m_stage, false);
				strike.transform.localScale = look == "wall"
					? new Vector3(LaneWidth * 0.9f, 2.6f, length)
					: new Vector3(LaneWidth * 0.8f, look == "breath" ? 0.9f : 2.2f, LaneWidth * 0.8f);
				strike.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + (look == "wall" ? 1.3f : 1.1f), look == "wall" ? z + length * 0.5f - 1f : z);
				Renderer r = strike.GetComponent<Renderer>();
				r.sharedMaterial = TintMaterial(def.Tint);
				r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			}
			strike.transform.SetParent(root.transform, true);
			strike.SetActive(false);
			// The game's attack effect inside the strike: not in gas, which is its own cloud (the
			// effect would be stretched along with it).
			if (look != "gas" && look != "meteor" && m_bossThing?.Attack != null)
			{
				foreach (GameObject fx in m_bossThing.Attack.Effects)
				{
					GameObject f = Instantiate(Picture(fx), strike.transform, false);
					foreach (ParticleSystem ps in f.GetComponentsInChildren<ParticleSystem>(true))
					{
						ps.Play(true);
					}
					break;
				}
			}
			float arrive = Mathf.Max(0.3f, (z - m_z) / Mathf.Max(1f, m_speed));
			m_things.Add(new Thing
			{
				Go = root,
				Kind = Kind.Block,
				Lane = lane,
				Z = length > 3f ? z + length * 0.5f - 1f : z,
				HalfLength = length > 3f ? length * 0.5f : 1.2f,
				Label = def.Name,
				CrashText = $"{def.Name} got you in the {biome}! (Watch the marked lanes.)",
				Life = arrive + 1.5f + (length > 3f ? length / Mathf.Max(1f, m_speed) : 0f),
				// 1.16.1 (SAT: Bonemass's gas clouds appeared much too close): gas billows up
				// where it's marked, well down the track, and hangs there as you come; other
				// strikes still show as you arrive.
				FireIn = look == "gas" ? 0.05f : Mathf.Max(0.05f, arrive - 0.5f),
				Moving = strike.transform,
				Threat = true,
			});
		}
	}

	// Called from UpdateThings for strikes: shows the strike just before you arrive.
	private void UpdateStrike(Thing t, float dt)
	{
		if (t.FireIn > 0f && t.Moving != null && t.Info == null && t.Special == Special.None)
		{
			t.FireIn -= dt;
			if (t.FireIn <= 0f)
			{
				t.Moving.gameObject.SetActive(true);
				if (!IsGas(t))
				{
					m_shake = Mathf.Max(m_shake, 0.15f);
				}
				StrikeEffects(t);
			}
		}
		FadeGasBehind(t);
	}

	private static bool IsGas(Thing t) => t.Moving != null && t.Moving.name == "GasCloud";

	private MaterialPropertyBlock? m_gasBlock;

	// Once you're past a gas cloud it thins away, so the camera behind you never flies into it.
	private void FadeGasBehind(Thing t)
	{
		Transform? cloud = t.Moving;
		if (cloud == null || cloud.name != "GasCloud" || !cloud.gameObject.activeSelf)
		{
			cloud = t.Go != null && t.Go.name == "GasCloud" ? t.Go.transform : null;
		}
		if (cloud == null)
		{
			return;
		}
		float fade = 1f - Mathf.Clamp01((m_z - (t.Z + t.HalfLength) + 0.5f) / 3f);
		m_gasBlock ??= new MaterialPropertyBlock();
		foreach (ParticleSystemRenderer r in cloud.GetComponentsInChildren<ParticleSystemRenderer>())
		{
			r.GetPropertyBlock(m_gasBlock);
			m_gasBlock.SetColor("_Color", new Color(1f, 1f, 1f, fade));
			r.SetPropertyBlock(m_gasBlock);
			r.enabled = fade > 0.01f;
		}
	}

	// The boss's own effects and sound where a strike lands (Eikthyr's lightning, the Elder's
	// roots, Yagluth's fire); the tinted pillar only shows when it has none.
	private void StrikeEffects(Thing t)
	{
		if (m_boss == null || t.Go == null)
		{
			return;
		}
		BossDef def = m_boss;
		Vector3 at = t.Go.transform.localPosition;
		if (t.HalfLength > 1.3f)
		{
			at.z = t.Z - t.HalfLength + 1f;
		}
		bool shown = false;
		// The boss's own burst, cut down to the lane for gas: at full size Bonemass's fills the
		// screen.
		bool gas = IsGas(t);
		if (def.Storm)
		{
			StormStrike(def, at);
			shown = true;
		}
		else if (def.StrikeFx.Length > 0 && FindFx(def.StrikeFx) is GameObject fx)
		{
			PlayFx(fx, at, Quaternion.identity, gas ? 1.6f : 3f, null, gas ? 0.4f : 1f);
			shown = true;
		}
		CreatureAttack? attack = m_bossThing?.Attack;
		if (attack != null && !gas)
		{
			foreach (GameObject impact in attack.Impact)
			{
				PlayFx(impact, at, Quaternion.identity, 3f);
				shown = true;
				break;
			}
		}
		if (def.Vines.Length > 0)
		{
			shown |= SpawnVine(def, at.x, at.z, 1.2f);
		}
		if (def.StrikeSfx.Length > 0)
		{
			// Several clouds at once make one sound, not a pile of them.
			if (!gas || Time.time - m_gasSfxAt > 0.2f)
			{
				m_audio.Play(def.StrikeSfx, gas ? 0.8f : 1f);
				m_gasSfxAt = gas ? Time.time : m_gasSfxAt;
			}
		}
		else
		{
			AttackSound(attack, true, 1f);
		}
		if (shown && t.HalfLength <= 1.3f && t.Moving != null)
		{
			foreach (Renderer r in t.Moving.GetComponents<Renderer>())
			{
				r.enabled = false;
			}
		}
	}

	// Something low across a lane to jump. 1.16.102 (SAT: no unnatural cut logs in boss fights):
	// the boss's own shockwave rolling along the ground, glowing in its colour with its attack's
	// impact effect. The Elder, a tree, still sends a root across.
	private Thing BossLow(BiomeRuntime b, int lane, float z, BossDef def)
	{
		GameObject go;
		float baseY = 0f;
		if (def.Prefab == "gd_king")
		{
			// A vine lying across the path, as his roots do in the maze.
			go = VinePiece(def, Vector3.zero, LaneWidth * 0.9f, Quaternion.Euler(0f, Random.Range(-15f, 15f), 90f), false)
				?? RootPiece(b, def, Vector3.zero, new Vector3(LaneWidth * 0.9f, 0.7f, 0.7f), Quaternion.Euler(0f, 0f, 90f), false);
		}
		else
		{
			go = BossEnergy(def, 0.28f, 0.55f, true);
		}
		go.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + baseY, z);
		GameObject marker = MakeMarker(lane, z, 1.6f, JumpMaterial);
		marker.transform.SetParent(go.transform, true);
		Thing t = new Thing { Go = go, Kind = Kind.Low, Lane = lane, Z = z, BaseY = baseY, HalfLength = 0.35f, Label = def.Name, Hint = "JUMP!", CrashText = $"{def.Name} tripped you up! (Jump the low ones.)" };
		m_things.Add(t);
		return t;
	}

	// A glowing bolt of the boss's power lying across a lane at height y (local to its root),
	// with its attack's effect on it and a light: a shockwave low down, a beam up high.
	private GameObject BossEnergy(BossDef def, float y, float thickness, bool impact)
	{
		GameObject root = new GameObject("BossEnergy");
		root.layer = m_layer;
		root.transform.SetParent(m_stage, false);
		if (def.Prefab == "Eikthyr")
		{
			// 1.16.150 (SAT: the yellow-marked bars looked like odd logs): Eikthyr's is a crackle
			// of his lightning running along the ground across the lane.
			Crackle crackle = root.AddComponent<Crackle>();
			crackle.Glow = BoltLine(root, false, "Glow");
			crackle.Core = BoltLine(root, true, "Core");
			Color tint = def.Tint;
			crackle.Glow.startColor = crackle.Glow.endColor = new Color(tint.r, tint.g, tint.b, 0.5f);
			crackle.Glow.widthMultiplier = thickness * 1.1f;
			Color white = Color.Lerp(Color.white, tint, 0.2f);
			crackle.Core.startColor = crackle.Core.endColor = white;
			crackle.Core.widthMultiplier = thickness * 0.3f;
			crackle.From = new Vector3(-LaneWidth * 0.5f, y, 0f);
			crackle.To = new Vector3(LaneWidth * 0.5f, y, 0f);
			Light spark = new GameObject("Glow").AddComponent<Light>();
			spark.gameObject.layer = m_layer;
			spark.transform.SetParent(root.transform, false);
			spark.transform.localPosition = new Vector3(0f, y + 0.4f, 0f);
			spark.type = LightType.Point;
			spark.range = 5f;
			spark.intensity = 2.2f;
			spark.color = Color.Lerp(Color.white, tint, 0.5f);
			spark.cullingMask = 1 << m_layer;
			crackle.Light = spark;
			return root;
		}
		GameObject bolt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		Destroy(bolt.GetComponent<Collider>());
		bolt.layer = m_layer;
		bolt.transform.SetParent(root.transform, false);
		bolt.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
		bolt.transform.localScale = new Vector3(thickness, LaneWidth * 0.48f, thickness);
		bolt.transform.localPosition = new Vector3(0f, y, 0f);
		Renderer r = bolt.GetComponent<Renderer>();
		r.sharedMaterial = TintMaterial(def.Tint);
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		CreatureAttack? a = m_bossThing?.Attack;
		List<GameObject>? fxs = a == null ? null : impact && a.Impact.Count > 0 ? a.Impact : a.Effects;
		if (fxs != null && fxs.Count > 0)
		{
			GameObject f = Instantiate(Picture(fxs[0]), root.transform, false);
			f.transform.localPosition = new Vector3(0f, y, 0f);
			foreach (ParticleSystem ps in f.GetComponentsInChildren<ParticleSystem>(true))
			{
				ParticleSystem.MainModule main = ps.main;
				main.loop = true;
				ps.Play(true);
			}
		}
		Light glow = new GameObject("Glow").AddComponent<Light>();
		glow.gameObject.layer = m_layer;
		glow.transform.SetParent(root.transform, false);
		glow.transform.localPosition = new Vector3(0f, y + 0.3f, 0f);
		glow.type = LightType.Point;
		glow.range = 4f;
		glow.intensity = 1.6f;
		glow.color = def.Tint;
		glow.cullingMask = 1 << m_layer;
		return root;
	}

	// A bar at head height across a lane to slide under: a glowing beam in the boss's colour on
	// two posts (a gate you can see the gap under), with a red band below.
	private Thing BossHigh(BiomeRuntime b, int lane, float z, BossDef def)
	{
		const float barY = 1.35f;
		// 1.16.102: the boss's beam crackling across at head height (the Elder's a root arching over).
		GameObject root;
		if (def.Prefab == "gd_king")
		{
			// Two vines leaning in from the sides, meeting over the lane.
			root = new GameObject("BossVineArch");
			root.layer = m_layer;
			root.transform.SetParent(m_stage, false);
			root.transform.localPosition = new Vector3(LaneX(lane), RoadY(z), z);
			bool any = false;
			foreach (float side in new[] { -1f, 1f })
			{
				GameObject? v = VinePiece(def, new Vector3(LaneX(lane) + side * LaneWidth * 0.5f, 0f, z + side * 0.15f), 2.3f,
					Quaternion.Euler(0f, Random.Range(0f, 360f), side * 58f), false);
				if (v != null)
				{
					v.transform.SetParent(root.transform, true);
					any = true;
				}
			}
			if (!any)
			{
				GameObject bar = RootPiece(b, def, new Vector3(LaneX(lane), barY + 0.25f, z), new Vector3(LaneWidth, 0.6f, 0.6f), Quaternion.Euler(0f, 0f, 90f), false);
				bar.transform.SetParent(root.transform, true);
			}
		}
		else
		{
			root = BossEnergy(def, barY + 0.25f, 0.45f, false);
		}
		root.transform.localPosition = new Vector3(LaneX(lane), RoadY(z), z);
		GameObject marker = MakeMarker(lane, z, 1.6f, SlideMaterial);
		marker.transform.SetParent(root.transform, true);
		Thing t = new Thing { Go = root, Kind = Kind.High, Lane = lane, Z = z, HalfLength = 0.35f, Label = def.Name, Hint = "SLIDE!", CrashText = $"{def.Name} caught you standing! (Slide under the high ones.)" };
		m_things.Add(t);
		return t;
	}

	private readonly Dictionary<Color, Material> m_solidTints = new();

	// An opaque, slightly glowing material in a colour (easy to read against any ground).
	private Material SolidTint(Color c)
	{
		if (!m_solidTints.TryGetValue(c, out Material mat) || mat == null)
		{
			Shader shader = FindPlainShader() ?? Shader.Find("Sprites/Default");
			mat = new Material(shader) { color = new Color(c.r, c.g, c.b, 1f) };
			if (mat.HasProperty("_EmissionColor"))
			{
				mat.EnableKeyword("_EMISSION");
				mat.SetColor("_EmissionColor", c * 0.6f);
			}
			m_solidTints[c] = mat;
		}
		return mat;
	}

	// A shockwave across a lane (a stomp), with the attack's dust: jump it.
	private Thing BossWave(int lane, float z, BossDef def, CreatureAttack? attack)
	{
		BiomeRuntime b = m_biomes[BiomeAt(z)];
		GameObject wave = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		Destroy(wave.GetComponent<Collider>());
		wave.name = "Shockwave";
		wave.layer = m_layer;
		wave.transform.SetParent(m_stage, false);
		wave.transform.localScale = new Vector3(LaneWidth * 0.95f, 0.2f, 0.7f);
		wave.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + 0.15f, z);
		Renderer r = wave.GetComponent<Renderer>();
		r.sharedMaterial = m_terrainTrack ?? b.Ground;
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		GameObject band = MakeMarker(lane, z, 1.6f, JumpMaterial);
		band.transform.SetParent(wave.transform, true);
		GameObject? fx = def.StrikeFx.Length > 0 ? FindFx(def.StrikeFx) : null;
		if (fx == null && attack != null)
		{
			foreach (GameObject e in attack.Effects)
			{
				fx = e;
				break;
			}
		}
		if (fx != null)
		{
			PlayFx(fx, Vector3.zero, Quaternion.identity, 6f, wave.transform);
		}
		Thing t = new Thing
		{
			Go = wave,
			Kind = Kind.Low,
			Lane = lane,
			Z = z,
			BaseY = 0.15f,
			HalfLength = 0.4f,
			Label = def.Name,
			Hint = "JUMP!",
			CrashText = $"{def.Name}'s stomp knocked you flat! (Jump the waves.)",
		};
		m_things.Add(t);
		return t;
	}

	private void BossHit()
	{
		if (m_boss == null || m_bossGo == null || m_bossSink >= 0f)
		{
			return;
		}
		BossDef def = m_boss;
		m_bossHits++;
		if (Has("meadheal") && m_lives < Plugin.StartLives)
		{
			m_lives++;
			m_livesFlash = 1.2f;
		}
		m_shake = Mathf.Max(m_shake, 0.3f);
		m_audio.Play(Sfx.HitMonster, 1f, 0.8f);
		if (m_bossAnim != null)
		{
			SetTrigger(m_bossAnim, "stagger");
		}
		if (m_boss.Mode == "side")
		{
			m_bossSide = -m_bossSide;
		}
		if (m_bossHits < BossHitsNeeded)
		{
			int left = BossHitsNeeded - m_bossHits;
			m_banner = $"{def.HitVerb} {def.Name}! {left} more";
			m_bannerTime = 1.8f;
			m_iconPulse = 1f;
			return;
		}

		// Beaten: a checkpoint for what comes next, and a breather.
		Segment seg = m_bossSeg!;
		seg.Beaten = true;
		Segment next = Following(seg);
		m_bonusDistance += Plugin.BiomeLength.Value * 0.5f;
		if (seg.Rush)
		{
			// No checkpoints inside the rush: it's one run of fights.
			if (next.Rush)
			{
				m_banner = $"{def.Name} defeated!\nNext: {Progress.BossOf(next.Biome)?.Name}";
			}
			else
			{
				m_bonusDistance += Plugin.BiomeLength.Value * 2f;
				m_banner = "Boss rush complete!" + (FullRush() ? "\n" + SurferShoes.Give(m_player) : "\n(Beat all six in a row for the golden shoes.)");
				// A reward worth words.
				m_bannerKeep = true;
			}
		}
		else
		{
			// Beating Moder saves the crossing: a start there begins on the coast.
			Progress.SaveCheckpoint(m_player, next.Sea ? SeaTo : next.Biome, next.Stars);
			ClearResumePoint();
			CampAfterBoss(seg, next, def);
			m_banner = $"{def.Name} defeated!\nCheckpoint: {Progress.Describe(next.Sea ? SeaTo : next.Biome, next.Stars, next.Sea)}";
			if (next.Rush)
			{
				m_banner += "\nBut first... the boss rush!";
			}
		}
		m_bannerTime = 3.5f;
		m_audio.Play(Sfx.Biome, 1f, 1.1f);
		Plugin.Log.LogInfo($"Beat {def.Name}; checkpoint {(next.Sea ? "the ocean" : next.Biome.ToString())}:{next.Stars}.");
		for (int i = m_things.Count - 1; i >= 0; i--)
		{
			Thing t = m_things[i];
			if (t.Kind != Kind.Ground && t.Kind != Kind.Scenery && t.Kind != Kind.Coin && (t.Z > m_z + 2f || t.Speed < 0f))
			{
				DestroyThing(t);
				m_things.RemoveAt(i);
			}
		}
		DestroySchemaLobs();
		ClearChase();
		if (next.Sea)
		{
			// The coast: far enough on for the beach to slope down to the ship.
			CloseSegment(seg, m_z + Mathf.Max(110f, AheadDistance * 0.6f));
			ReshapeShore(seg.End);
		}
		else
		{
			CloseSegment(seg, m_z + Mathf.Max(45f, AheadDistance * 0.35f));
		}
		m_bossSink = 0f;
		m_bossHidden = 0f;
		m_bossGo.SetActive(true);
		BossDeath(def);
	}

	private Quaternion m_bossDeathRot;
	private float m_bossDeathSide = 1f;

	// 1.14.2 (SAT: bosses just sank away): its own death, from the game: the death effects and
	// sounds (Character.m_deathEffects, minus the ragdoll, which needs physics), a last stagger,
	// and it topples over before the ground takes it.
	private void BossDeath(BossDef def)
	{
		// What you bought in this biome lasts until its boss falls (SurfShop.cs).
		AbilitiesEnd();
		m_bossDeathRot = m_bossGo != null ? m_bossGo.transform.localRotation : Quaternion.identity;
		m_bossDeathSide = Random.value < 0.5f ? -1f : 1f;
		if (m_bossAnim != null)
		{
			SetTrigger(m_bossAnim, "stagger");
		}
		GameObject? prefab = Pictures.Prefab(def.Prefab);
		Character? c = prefab != null ? prefab.GetComponent<Character>() : null;
		if (c?.m_deathEffects?.m_effectPrefabs == null || m_bossGo == null)
		{
			m_audio.Play(Sfx.HitMonster, 1f, 0.6f);
			return;
		}
		m_audio.PlayEffects(c.m_deathEffects, 1f);
		foreach (EffectList.EffectData e in c.m_deathEffects.m_effectPrefabs)
		{
			if (e?.m_prefab == null || !e.m_enabled || e.m_prefab.GetComponentInChildren<Ragdoll>(true) != null
				|| e.m_prefab.GetComponentInChildren<ParticleSystem>(true) == null)
			{
				continue;
			}
			PlayFx(e.m_prefab, m_bossGo.transform.localPosition + Vector3.up * 1f, Quaternion.identity, 6f);
		}
	}

	private void DestroySchemaLobs()
	{
		foreach (Lob lob in m_lobs)
		{
			if (lob.Go != null)
			{
				Destroy(lob.Go);
			}
			foreach (GameObject m in lob.Markers)
			{
				if (m != null)
				{
					Destroy(m);
				}
			}
		}
		m_lobs.Clear();
	}

	private static void SetBool(Animator anim, string name, bool value)
	{
		foreach (AnimatorControllerParameter p in anim.parameters)
		{
			if (p.name == name && p.type == AnimatorControllerParameterType.Bool)
			{
				anim.SetBool(name, value);
				return;
			}
		}
	}

	private string StretchHud()
	{
		if (m_boss != null && m_bossSink < 0f)
		{
			string rush = m_bossSeg != null && m_bossSeg.Rush ? $"Boss rush {m_bossSeg.RushIndex + 1}/{RushBosses().Count}: " : "";
			// The boss's health and the item count are on the HUD (SurfHud.cs).
			return rush.Length > 0 ? "   ·   " + rush.TrimEnd(' ', ':') : "";
		}
		return "";
	}

	// ---------------------------------------------------------------- landmarks and pickables

	// A creature's home beside the track, now and then, near where it turns up.
	private void MaybeLandmark(GameObject creature, float z)
	{
		if (z - m_lastLandmarkZ < 45f || Random.value > 0.45f || NearTrader(z, 30f))
		{
			return;
		}
		BiomeRuntime b = m_biomes[BiomeAt(z)];
		List<GameObject>? homes = null;
		foreach ((string key, bool prefix, List<GameObject> list) in b.Landmarks)
		{
			if (prefix ? creature.name.StartsWith(key, System.StringComparison.OrdinalIgnoreCase) : string.Equals(creature.name, key, System.StringComparison.OrdinalIgnoreCase))
			{
				homes = list;
				break;
			}
		}
		if (homes == null || homes.Count == 0)
		{
			return;
		}
		m_lastLandmarkZ = z;
		GameObject prefab = homes[Random.Range(0, homes.Count)];
		PlaceBeside(prefab, z + Random.Range(-3f, 3f), Random.value < 0.5f ? -1 : 1, 5f, 12f, 1f);
	}

	// Plants, mushrooms, stones and bushes close to the track edge.
	private void MaybePickable(float z, int side)
	{
		BiomeRuntime b = m_biomes[BiomeAt(z)];
		if (b.Pickables.Count == 0 || Random.value > 0.4f)
		{
			return;
		}
		PlaceBeside(b.Pickables[Random.Range(0, b.Pickables.Count)], z, side, 1f, 6f, Random.Range(0.9f, 1.2f));
	}

	private void PlaceBeside(GameObject prefab, float z, int side, float near, float far, float scale)
	{
		float x = side * (TrackHalf + ExtraFlat(z) + Random.Range(near, far));
		if (UnderSea(x, z))
		{
			return;
		}
		GameObject go = Instantiate(Picture(prefab), m_stage, false);
		go.transform.localPosition = new Vector3(x, HillHeight(x, z) - 0.05f, z);
		go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
		go.transform.localScale = Vector3.one * scale;
		if (Pictures.MeshBounds(go, out Bounds bounds))
		{
			float inner = (side < 0 ? bounds.max.x : bounds.min.x) - m_stage.position.x;
			float limit = side * (TrackHalf + 0.6f + ExtraFlat(z));
			float push = side < 0 ? Mathf.Min(0f, limit - inner) : Mathf.Max(0f, limit - inner);
			if (push != 0f)
			{
				Vector3 p = go.transform.localPosition;
				p.x += push;
				p.y = HillHeight(p.x, z) - 0.05f;
				go.transform.localPosition = p;
			}
		}
		m_things.Add(new Thing { Go = go, Kind = Kind.Scenery, Z = z, HalfLength = 2f });
	}

	// ---------------------------------------------------------------- the dash (star trick)

	private float m_bossHidden;

	// The boss itself charges back down a marked lane at you, then returns to its place.
	private void BossDash(BossDef def)
	{
		if (m_bossGo == null || m_bossHidden > 0f)
		{
			return;
		}
		GameObject? prefab = Pictures.Prefab(def.Prefab);
		if (prefab == null || m_bossInfo == null)
		{
			return;
		}
		int lane = m_lane;
		float z = m_z + 42f;
		float speed = 13f;
		float scale = m_bossInfo.Height > 0.3f ? Mathf.Min(1f, Mathf.Min(def.MaxHeight, 4.5f) / m_bossInfo.Height) : 1f;
		GameObject copy = Creature(prefab, Quaternion.Euler(0f, 180f, 0f), scale);
		copy.transform.localPosition = new Vector3(LaneX(lane), RoadY(z), z);
		Animator anim = copy.GetComponentInChildren<Animator>();
		if (anim != null)
		{
			SetParam(anim, "forward_speed", 12f);
			SetParam(anim, "onGround", true);
		}
		// The lane it will charge down glows the whole way.
		float travel = z - m_z;
		GameObject marker = MakeMarker(lane, m_z + travel * 0.5f, travel, m_markerMaterial);
		marker.transform.SetParent(copy.transform, true);
		float seconds = travel / (speed + Mathf.Max(1f, m_speed));
		m_things.Add(new Thing
		{
			Go = copy,
			Kind = Kind.Block,
			Lane = lane,
			Z = z,
			HalfLength = 1.2f,
			Speed = speed,
			Label = def.Name,
			CrashText = $"{def.Name} ran you down! (Get out of the glowing lane.)",
			Life = seconds + 2f,
		});
		m_bossGo.SetActive(false);
		m_bossHidden = seconds + 1.5f;
		// It can't attack from its place while it's charging at you.
		m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, m_bossHidden + 0.5f);
		m_banner = $"{def.Name} charges!";
		m_bannerTime = 1.2f;
	}

	private void UpdateBossHidden(float dt)
	{
		if (m_bossHidden > 0f)
		{
			m_bossHidden -= dt;
			if (m_bossHidden <= 0f && m_bossGo != null)
			{
				m_bossGo.SetActive(true);
			}
		}
	}
}

internal sealed partial class SurfGame
{
	private readonly List<(GameObject go, Vector3 from, Vector3 to, float at, float dur)> m_meteors = new();
	private float m_meteorClock;

	private void DropMeteor(Vector3 to, float landIn)
	{
		GameObject? prefab = Pictures.Prefab("projectile_meteor");
		if (prefab == null)
		{
			return;
		}
		float dur = Mathf.Clamp(landIn, 0.4f, 1.1f);
		// From high up, a little ahead and to one side, as Yagluth's fall in the game.
		Vector3 from = to + new Vector3(Random.Range(-6f, 6f), 28f, 14f);
		GameObject go = Instantiate(Picture(prefab), m_stage, false);
		go.transform.localPosition = from;
		go.transform.localRotation = Quaternion.LookRotation(to - from);
		go.SetActive(false);
		LightUp(go);
		m_meteors.Add((go, from, to, m_meteorClock + landIn - dur, dur));
	}

	private void UpdateMeteors(float dt)
	{
		m_meteorClock += dt;
		for (int i = m_meteors.Count - 1; i >= 0; i--)
		{
			(GameObject go, Vector3 from, Vector3 to, float at, float dur) = m_meteors[i];
			if (go == null)
			{
				m_meteors.RemoveAt(i);
				continue;
			}
			float u = (m_meteorClock - at) / dur;
			if (u < 0f)
			{
				continue;
			}
			if (!go.activeSelf)
			{
				go.SetActive(true);
				LightUp(go);
			}
			go.transform.localPosition = Vector3.Lerp(from, to, Mathf.Min(1f, u * u));
			if (u >= 1f)
			{
				if (FindFx("fx_goblinking_meteor_hit|fx_fader_meteor_hit") is GameObject hit)
				{
					PlayFx(hit, to, Quaternion.identity, 3f);
				}
				m_shake = Mathf.Max(m_shake, 0.3f);
				Destroy(go);
				m_meteors.RemoveAt(i);
			}
		}
	}
}
