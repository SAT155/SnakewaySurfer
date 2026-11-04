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

	// ---------------------------------------------------------------- stretches

	private void ResetSegments()
	{
		m_segments.Clear();
		int lap = Plugin.LoopBiomes.Value <= 0 ? m_biomes.Count : Mathf.Clamp(Plugin.LoopBiomes.Value, 1, m_biomes.Count);
		m_lapBiomes = Mathf.Clamp(Progress.AllowedBiomes(lap), 1, m_biomes.Count);
		int biome = Mathf.Clamp(m_startBiome, 0, m_lapBiomes - 1);
		Segment first = NewSegment(0f, biome, Plugin.LoopBiomes.Value <= 0 ? 0 : m_startStars, false, false);
		first.PrevBiome = first.Biome;
		first.PrevLevel = first.Level;
		m_segments.Add(first);
		m_itemSeg = null;
		m_items = 0;
		m_nextItemZ = 0f;
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
		if (s.Boss || s.Repeat || !Plugin.BossFights.Value)
		{
			return null;
		}
		BossDef? boss = Progress.BossOf(s.Biome);
		if (boss == null || (!Plugin.IgnoreBossGate.Value && s.Biome > Progress.FurthestBoss()))
		{
			return null;
		}
		return Pictures.Prefab(boss.Prefab) != null ? boss : null;
	}

	private Segment Following(Segment s)
	{
		if (!s.Boss && BossFor(s) != null)
		{
			return NewSegment(s.End, s.Biome, s.Stars, true, false);
		}
		int biome = s.Biome + 1;
		int stars = s.Stars;
		bool repeat = false;
		if (biome >= m_lapBiomes)
		{
			if (Plugin.LoopBiomes.Value <= 0 || m_lapBiomes <= 0)
			{
				biome = s.Biome;
				repeat = true;
			}
			else
			{
				biome = 0;
				stars = Mathf.Min(3, stars + 1);
			}
		}
		return NewSegment(s.End, biome, stars, false, repeat);
	}

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
		float ordinal = s.Stars * Mathf.Max(1, m_lapBiomes) + s.Biome;
		float frac = s.Boss ? 1f : Mathf.Clamp01((z - s.Start) / Plugin.BiomeLength.Value);
		return ordinal + frac;
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
				m_nextItemZ = Mathf.Max(m_nextItemZ, m_z + 30f);
			}
			if (m_items >= boss.Amount)
			{
				CloseSegment(here, Mathf.Max(here.Start + Plugin.BiomeLength.Value, m_z + 55f));
				m_banner = $"{boss.Name} approaches!";
				m_bannerTime = 2.5f;
				m_audio.Play(Sfx.Biome, 1f, 0.7f);
			}
			else if (m_z > here.Start + Plugin.BiomeLength.Value)
			{
				m_itemNagTime -= dt;
				if (m_itemNagTime <= 0f)
				{
					m_itemNagTime = 9f;
					int left = boss.Amount - m_items;
					m_banner = $"Find {left} more {ItemName(boss.Item)} to face {boss.Name}!";
					m_bannerTime = 2.5f;
				}
			}
		}
		if (here.Boss && !here.Beaten && m_bossSeg != here && m_bossSink < 0f)
		{
			StartBoss(here);
		}
		UpdateBoss(dt);
	}

	// Summoning items come up in place of some coins while you still need them.
	private bool MaybeSpawnItem(int lane, float z)
	{
		Segment s = SegAt(z);
		BossDef? boss = BossFor(s);
		if (boss == null || s.End != float.MaxValue || z < m_nextItemZ || (s == m_itemSeg && m_items >= boss.Amount))
		{
			return false;
		}
		GameObject? template = ItemTemplate(boss.Item);
		if (template == null)
		{
			return false;
		}
		float spacing = Mathf.Max(16f, Plugin.BiomeLength.Value / (boss.Amount * 1.7f));
		if (z > s.Start + Plugin.BiomeLength.Value)
		{
			spacing *= 0.5f;
		}
		m_nextItemZ = z + spacing * Random.Range(0.8f, 1.2f);
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

	private void SpawnPickup(GameObject template, int lane, float z, Kind kind)
	{
		GameObject go = Fitted(template, Quaternion.Euler(0f, 0f, 0f), new Vector3(0.9f, 0.9f, 0.9f), true, 3f);
		const float y = 1.0f;
		go.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + y, z);
		Light glow = new GameObject("Glow").AddComponent<Light>();
		glow.gameObject.layer = m_layer;
		glow.transform.SetParent(go.transform, false);
		glow.type = LightType.Point;
		glow.range = 3.5f;
		glow.intensity = 1.6f;
		glow.color = kind == Kind.BossHit ? new Color(0.5f, 0.8f, 1f) : new Color(1f, 0.85f, 0.4f);
		glow.cullingMask = 1 << m_layer;
		m_things.Add(new Thing { Go = go, Kind = kind, Lane = lane, Z = z, Y = y, HalfLength = 0.45f });
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
		m_audio.Play(Sfx.Coin, 1f, 0.8f);
		if (m_items < boss.Amount)
		{
			m_banner = $"{ItemName(boss.Item)} {m_items}/{boss.Amount}";
			m_bannerTime = 1.2f;
		}
	}

	// ---------------------------------------------------------------- the boss

	private void StartBoss(Segment seg)
	{
		EndBoss();
		BossDef? def = Progress.BossOf(seg.Biome);
		GameObject? prefab = def != null ? Pictures.Prefab(def.Prefab) : null;
		if (def == null || prefab == null)
		{
			CloseSegment(seg, m_z + 20f);
			return;
		}
		m_boss = def;
		m_bossSeg = seg;
		m_bossInfo = CreatureInfo.Of(prefab);
		float scale = m_bossInfo.Height > 0.3f ? Mathf.Min(1f, def.MaxHeight / m_bossInfo.Height) : 1f;
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
		m_bossHits = 0;
		m_bossIntro = 0f;
		m_bossAttackTimer = 2.5f;
		m_bossHitTimer = 3.5f;
		m_bossMove = 0;
		m_bossSide = -1;
		m_bossZ = m_z + def.Distance + 40f;
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
		m_banner = $"{def.Name}!";
		m_bannerTime = 2.5f;
		if (m_bossInfo.AlertSound.Length > 0)
		{
			m_audio.Play(m_bossInfo.AlertSound, 1f);
		}
		Plugin.Log.LogInfo($"Boss fight: {def.Name} ({prefab.name}, scale {scale:0.00}, {seg.Stars} stars).");
	}

	private static string Localize(string text) => Localization.instance != null ? Localization.instance.Localize(text) : text;

	private void EndBoss()
	{
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
	}

	private void UpdateBoss(float dt)
	{
		if (m_boss == null || m_bossGo == null || m_bossThing == null)
		{
			return;
		}
		BossDef def = m_boss;
		if (m_bossSink >= 0f)
		{
			// Beaten: it sinks away while you run on.
			m_bossSink += dt;
			m_bossZ += m_speed * dt;
			Vector3 p = m_bossGo.transform.localPosition;
			m_bossGo.transform.localPosition = new Vector3(p.x, RoadY(m_bossZ) + def.Fly - m_bossSink * 2.5f, m_bossZ);
			if (m_bossSink > 3f)
			{
				EndBoss();
			}
			return;
		}

		m_bossIntro = Mathf.Min(1f, m_bossIntro + dt * 0.6f);
		float want = m_z + def.Distance;
		switch (def.Mode)
		{
			case "stand":
				// It stays put; when you get close it sinks and comes up further on.
				if (m_bossZ - m_z < def.Distance * 0.45f)
				{
					m_bossZ = m_z + def.Distance;
				}
				if (m_bossIntro < 1f)
				{
					m_bossZ = Mathf.Lerp(m_bossZ, want, dt * 2f);
				}
				break;
			default:
				m_bossZ = Mathf.Lerp(m_bossZ, want, m_bossIntro < 1f ? dt * 1.5f : 1f);
				break;
		}
		float x = def.Mode == "side" ? m_bossSide * (TrackHalf + 6f) : 0f;
		Vector3 pos = new Vector3(Mathf.MoveTowards(m_bossGo.transform.localPosition.x, x, dt * 6f), RoadY(m_bossZ) + def.Fly, m_bossZ);
		m_bossGo.transform.localPosition = pos;
		m_bossThing.Z = m_bossZ;

		// Runners face away and turn round to attack; the others face you or the track.
		m_bossTurn = Mathf.Max(0f, m_bossTurn - dt);
		float yaw = def.Mode switch
		{
			"run" or "fly" => m_bossTurn > 0f ? 180f : 0f,
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
			string[] moves = def.Moves[phase];
			string move = moves[m_bossMove++ % moves.Length];
			BossMove(def, move);
			float interval = (phase == 0 ? 2.3f : phase == 1 ? 1.8f : 1.4f) / (1f + 0.12f * (m_bossSeg?.Stars ?? 0));
			m_bossAttackTimer = interval;
		}
		m_bossHitTimer -= dt;
		if (m_bossHitTimer <= 0f)
		{
			m_bossHitTimer = 7f;
			GameObject? template = ItemTemplate(def.HitItem);
			if (template != null)
			{
				SpawnPickup(template, Random.Range(0, 3), m_z + m_speed * 3f + 6f, Kind.BossHit);
			}
		}
	}

	private void BossAnimate()
	{
		m_bossTurn = 1.1f;
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
	private float AheadZ(float seconds) => m_z + m_speed * seconds + 3f;

	private int NearLane(int lane) => lane == 0 ? 1 : lane == 2 ? 1 : (Random.value < 0.5f ? 0 : 2);

	private void BossMove(BossDef def, string move)
	{
		Thing boss = m_bossThing!;
		BiomeRuntime b = m_biomes[BiomeAt(m_z)];
		BossAnimate();
		switch (move)
		{
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
					BossStrike(def, new[] { k == 0 ? m_lane : Random.Range(0, 3) }, AheadZ(1.4f + 0.55f * k), 2.2f, move == "blobs" ? "gas" : "fire");
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
				GameObject? prefab = def.Minion.Length > 0 ? Pictures.Monster(def.Minion) : null;
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
			if (m_bossThing?.Attack != null)
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
				Z = look == "wall" ? z + length * 0.5f - 1f : z,
				HalfLength = look == "wall" ? length * 0.5f : 1.2f,
				Label = def.Name,
				CrashText = $"{def.Name} got you in the {biome}! (Watch the marked lanes.)",
				Life = arrive + 1.5f + (look == "wall" ? length / Mathf.Max(1f, m_speed) : 0f),
				FireIn = Mathf.Max(0.05f, arrive - 0.5f),
				Moving = strike.transform,
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
				m_shake = Mathf.Max(m_shake, 0.15f);
			}
		}
	}

	private void BossLow(BiomeRuntime b, int lane, float z, BossDef def)
	{
		GameObject go;
		if (b.Logs.Count > 0)
		{
			go = Fitted(Picture(b.Logs[Random.Range(0, b.Logs.Count)]), Quaternion.Euler(0f, 0f, 90f), new Vector3(LaneWidth * 0.9f, 0.7f, 0.7f), false);
		}
		else
		{
			go = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Destroy(go.GetComponent<Collider>());
			go.layer = m_layer;
			go.transform.SetParent(m_stage, false);
			go.transform.localScale = new Vector3(LaneWidth * 0.9f, 0.6f, 0.6f);
			go.GetComponent<Renderer>().sharedMaterial = TintMaterial(def.Tint);
		}
		go.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + (b.Logs.Count > 0 ? 0f : 0.3f), z);
		GameObject marker = MakeMarker(lane, z, 1.6f, m_markerMaterial);
		marker.transform.SetParent(go.transform, true);
		m_things.Add(new Thing { Go = go, Kind = Kind.Low, Lane = lane, Z = z, HalfLength = 0.35f, Label = def.Name, CrashText = $"{def.Name} tripped you up! (Jump the low ones.)" });
	}

	private void BossHigh(BiomeRuntime b, int lane, float z, BossDef def)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		Destroy(go.GetComponent<Collider>());
		go.layer = m_layer;
		go.transform.SetParent(m_stage, false);
		go.transform.localScale = new Vector3(LaneWidth * 0.95f, 0.5f, 0.5f);
		go.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + 1.35f, z);
		Renderer r = go.GetComponent<Renderer>();
		r.sharedMaterial = TintMaterial(def.Tint);
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		m_things.Add(new Thing { Go = go, Kind = Kind.High, Lane = lane, Z = z, HalfLength = 0.35f, Label = def.Name, CrashText = $"{def.Name} caught you standing! (Slide under the high ones.)" });
	}

	private void BossHit()
	{
		if (m_boss == null || m_bossGo == null || m_bossSink >= 0f)
		{
			return;
		}
		BossDef def = m_boss;
		m_bossHits++;
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
			return;
		}

		// Beaten: a checkpoint for what comes next, and a breather.
		Segment seg = m_bossSeg!;
		seg.Beaten = true;
		Segment next = Following(seg);
		Progress.SaveCheckpoint(m_player, next.Biome, next.Stars);
		m_bonusDistance += Plugin.BiomeLength.Value * 0.5f;
		m_banner = $"{def.Name} defeated!\nCheckpoint: {Progress.Describe(next.Biome, next.Stars)}";
		m_bannerTime = 3.5f;
		m_audio.Play(Sfx.Biome, 1f, 1.1f);
		Plugin.Log.LogInfo($"Beat {def.Name}; checkpoint {next.Biome}:{next.Stars}.");
		for (int i = m_things.Count - 1; i >= 0; i--)
		{
			Thing t = m_things[i];
			if (t.Kind != Kind.Ground && t.Kind != Kind.Scenery && t.Kind != Kind.Coin && t.Z > m_z + 2f)
			{
				DestroyThing(t);
				m_things.RemoveAt(i);
			}
		}
		DestroySchemaLobs();
		CloseSegment(seg, m_z + 45f);
		m_bossSink = 0f;
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
			return $"   ·   {m_boss.Name}: {m_bossHits}/{BossHitsNeeded} hits";
		}
		Segment here = SegAt(m_z);
		BossDef? boss = BossFor(here);
		if (boss != null && here.End == float.MaxValue)
		{
			return $"   ·   {ItemName(boss.Item)} {Mathf.Min(m_items, boss.Amount)}/{boss.Amount}";
		}
		return "";
	}

	// ---------------------------------------------------------------- landmarks and pickables

	// A creature's home beside the track, now and then, near where it turns up.
	private void MaybeLandmark(GameObject creature, float z)
	{
		if (z - m_lastLandmarkZ < 45f || Random.value > 0.45f)
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
		GameObject go = Instantiate(Picture(prefab), m_stage, false);
		float x = side * (TrackHalf + Random.Range(near, far));
		go.transform.localPosition = new Vector3(x, HillHeight(x, z) - 0.05f, z);
		go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
		go.transform.localScale = Vector3.one * scale;
		if (Pictures.MeshBounds(go, out Bounds bounds))
		{
			float inner = (side < 0 ? bounds.max.x : bounds.min.x) - m_stage.position.x;
			float limit = side * (TrackHalf + 0.6f);
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
}
