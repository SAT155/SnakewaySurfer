using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// Boss chases (1.12): the boss runs after you, a little way behind, and the camera swings
// round to look back at it over your shoulder for the whole fight. Its attacks come up the
// lanes from behind or land where you are:
// - lane strikes (Eikthyr's lightning, the Elder's roots, meteors): a red band follows you in
//   the lanes it's aiming at, then the blow lands there (with the boss's own effects and sound);
// - walls, gas, fire and breath: the marked lanes stay deadly for a couple of seconds;
// - stomps, rows and beams: waves and bars racing up behind you to jump or slide;
// - shots, minions and the charge: they run or fly past you down a lane.
// Left and right follow the screen while it faces back. The fight starts with the summoning, as
// in the game: the items rise and circle, the altar's sparkles play, and the boss forms.
internal sealed partial class SurfGame
{
	private bool m_chase;
	// 0: the camera behind you looking ahead; 1: in front of you looking back.
	private float m_camBack;
	// Where the boss runs, relative to you (negative: behind).
	private float m_bossOff;
	private float m_bossScale = 1f;
	private int m_minLane;
	private int m_maxLane = 2;
	private float m_extraLanes;

	// Which way is "ahead on the screen" along the track.
	private float ChaseSign => m_chase ? -1f : 1f;

	// While the camera faces back, screen left is the track's right.
	private bool Mirrored => m_camBack > 0.5f;

	// A chase is on or coming up soon (what's behind you stays, for the camera to show).
	private bool ChaseAhead() => Plugin.BossChase.Value && Plugin.BossFights.Value && m_segments.Count > 0 && (SegAt(m_z).Boss || SegAt(m_z + AheadDistance).Boss);

	private static float ChaseDistance(BossDef def) => def.Chase > 0f ? def.Chase : Mathf.Max(12f, 6f + 1.6f * def.MaxHeight);

	private static bool HasBranches(BossDef def)
	{
		foreach (string[] phase in def.Moves)
		{
			if (System.Array.IndexOf(phase, "branches") >= 0)
			{
				return true;
			}
		}
		return System.Array.IndexOf(def.StarMoves, "branches") >= 0;
	}

	// How much wider the flat ground beside the track is here: room for the two extra lanes
	// in the arena of a boss whose roots split the path.
	private float ExtraFlat(float z)
	{
		if (!Plugin.BossChase.Value || !Plugin.BossFights.Value || m_segments.Count == 0)
		{
			return 0f;
		}
		Segment s = SegAt(z);
		float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((z - s.Start) / 30f));
		bool here = s.Boss && Progress.BossOf(s.Biome) is BossDef cur && HasBranches(cur);
		bool before = s.PrevBoss && Progress.BossOf(s.PrevBiome) is BossDef prev && HasBranches(prev);
		float w = here ? (before ? 1f : ramp) : before ? 1f - ramp : 0f;
		return w * (LaneWidth + 0.6f);
	}

	// ---------------------------------------------------------------- strikes

	private sealed class ChaseStrike
	{
		public int[] Lanes = new int[0];
		public float Warn;
		public float Active;
		public string Look = "strike";
		public CreatureAttack? Attack;
		public float AnimAt = -1f;
		public bool Landed;
		public bool Hurt;
		public float Trail;
		public string CrashText = "";
		public readonly List<GameObject> Markers = new();
		public readonly List<GameObject> Hazards = new();
		public GameObject? Ball;
		public Vector3 From;
		public float Flight;
		public float Age;
	}

	private sealed class Vine
	{
		public GameObject Go = null!;
		public float Z;
		public float Age;
		public float Life;
		public float Depth;
	}

	private readonly List<ChaseStrike> m_chaseStrikes = new();
	private readonly List<Vine> m_vines = new();

	// Seconds of warning for a lane strike: less in later phases and star laps, never under 1.1.
	private float ChaseWarn()
	{
		int phase = Mathf.Clamp(m_bossHits, 0, 2);
		float w = (phase == 0 ? 1.6f : phase == 1 ? 1.45f : 1.3f) / (1f + 0.06f * (m_bossSeg?.Stars ?? 0));
		return Mathf.Max(1.1f, w);
	}

	// One of the boss's own attacks, by a word in its weapon or animation name (the first key
	// that matches), else any.
	private CreatureAttack? BossAttackFor(string keys)
	{
		if (m_bossInfo == null)
		{
			return null;
		}
		List<CreatureAttack> all = new(m_bossInfo.Melee);
		all.AddRange(m_bossInfo.Ranged);
		if (all.Count == 0)
		{
			return null;
		}
		foreach (string key in keys.Split('|'))
		{
			if (key.Length == 0)
			{
				continue;
			}
			foreach (CreatureAttack a in all)
			{
				if (a.Item.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0 || a.Animation.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return a;
				}
			}
		}
		return all[Random.Range(0, all.Count)];
	}

	// The boss plays an attack now (its sound too).
	private void ChaseAnimate(CreatureAttack? attack)
	{
		if (attack == null || m_bossAnim == null || m_bossHidden > 0f)
		{
			return;
		}
		TriggerWatched(m_bossAnim, attack.Animation);
		AttackSound(attack, false, 0.8f);
	}

	// A strike on some lanes, landing where you are in `warn` seconds. `active`: how long the
	// lanes stay deadly after (a blow is short; walls, gas and breath last). `ball`: something
	// is thrown from the boss and lands there (meteors, Bonemass's blobs).
	private ChaseStrike ChaseStrikeAt(BossDef def, int[] lanes, float warn, float active, string look, string attackKeys, bool ball = false)
	{
		ChaseStrike s = new ChaseStrike
		{
			Lanes = lanes,
			Warn = warn,
			Active = active,
			Look = look,
			Attack = BossAttackFor(attackKeys),
			CrashText = look switch
			{
				"gas" => $"{def.Name}'s poison caught you! (Leave the red lanes.)",
				"fire" or "breath" => $"{def.Name}'s fire caught you! (Leave the red lanes.)",
				"wall" or "vines" => $"{def.Name}'s roots caught you! (Leave the red lanes.)",
				_ => $"{def.Name} struck you down! (Leave the red lanes.)",
			},
		};
		HashSet<int> seen = new();
		foreach (int lane in lanes)
		{
			if (seen.Add(lane))
			{
				s.Markers.Add(MakeMarker(lane, m_z, 3.2f, m_markerMaterial));
			}
		}
		if (s.Attack != null && m_bossAnim != null)
		{
			float windup = AnimTiming.Delay(m_bossAnim, s.Attack.Animation, 0.6f);
			s.AnimAt = Mathf.Max(0f, warn - windup);
		}
		if (ball && m_bossGo != null)
		{
			s.Ball = MakeBall(def, s.Attack);
			s.From = m_bossGo.transform.localPosition + Vector3.up * def.MaxHeight * 0.7f;
			s.Flight = Mathf.Min(warn, 1.1f);
			s.Ball.SetActive(false);
		}
		m_chaseStrikes.Add(s);
		return s;
	}

	private GameObject MakeBall(BossDef def, CreatureAttack? attack)
	{
		GameObject wrapper = attack?.Projectile != null ? MakeShotVisual(attack!) : new GameObject("Ball");
		wrapper.layer = m_layer;
		wrapper.transform.SetParent(m_stage, false);
		if (attack?.Projectile == null)
		{
			GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			Destroy(visual.GetComponent<Collider>());
			visual.layer = m_layer;
			visual.transform.SetParent(wrapper.transform, false);
			visual.transform.localScale = Vector3.one * 0.9f;
			visual.GetComponent<Renderer>().sharedMaterial = TintMaterial(def.Tint);
			Light glow = new GameObject("Glow").AddComponent<Light>();
			glow.gameObject.layer = m_layer;
			glow.transform.SetParent(wrapper.transform, false);
			glow.type = LightType.Point;
			glow.range = 5f;
			glow.intensity = 1.6f;
			glow.color = def.Tint;
			glow.cullingMask = 1 << m_layer;
		}
		else
		{
			wrapper.transform.localScale *= 1.5f;
		}
		return wrapper;
	}

	private bool InLane(int lane) => Mathf.Abs(m_x - LaneX(lane)) < LaneWidth * 0.45f;

	private void UpdateChaseStrikes(float dt, BossDef def)
	{
		for (int i = m_chaseStrikes.Count - 1; i >= 0; i--)
		{
			ChaseStrike s = m_chaseStrikes[i];
			s.Age += dt;
			if (!s.Landed)
			{
				float before = s.Warn;
				s.Warn -= dt;
				if (s.AnimAt >= 0f && before > s.AnimAt && s.Warn <= s.AnimAt)
				{
					ChaseAnimate(s.Attack);
				}
				// The marks ride along with you, blinking faster as the blow comes.
				bool show = s.Warn > 0.6f || Mathf.FloorToInt(s.Warn * 10f) % 2 == 0;
				int k = 0;
				foreach (int lane in Distinct(s.Lanes))
				{
					if (k < s.Markers.Count && s.Markers[k] != null)
					{
						PlaceMarker(s.Markers[k], lane, m_z);
						s.Markers[k].SetActive(show);
					}
					k++;
				}
				if (s.Ball != null)
				{
					float p = 1f - Mathf.Clamp01(s.Warn / s.Flight);
					if (p > 0f)
					{
						s.Ball.SetActive(true);
						float x = 0f;
						foreach (int lane in s.Lanes)
						{
							x += LaneX(lane);
						}
						x /= Mathf.Max(1, s.Lanes.Length);
						Vector3 to = new Vector3(x, RoadY(m_z) + 0.4f, m_z);
						Vector3 from = s.From + new Vector3(0f, 0f, m_z - s.From.z - ChaseDistance(def));
						Vector3 pos = Vector3.Lerp(from, to, p) + Vector3.up * Mathf.Sin(p * Mathf.PI) * 6f;
						s.Ball.transform.localPosition = pos;
					}
				}
				if (s.Warn <= 0f)
				{
					LandStrike(s, def);
				}
				continue;
			}
			// Landed: deadly while it lasts.
			s.Active -= dt;
			if (!s.Hurt)
			{
				foreach (int lane in s.Lanes)
				{
					if (InLane(lane))
					{
						s.Hurt = true;
						m_audio.Play(Sfx.HitMonster);
						Hit(new Thing { Label = def.Name }, s.CrashText);
						break;
					}
				}
			}
			FollowHazards(s, def, dt);
			if (s.Active <= 0f)
			{
				ClearStrike(s);
				m_chaseStrikes.RemoveAt(i);
			}
		}
	}

	private static IEnumerable<int> Distinct(int[] lanes)
	{
		HashSet<int> seen = new();
		foreach (int lane in lanes)
		{
			if (seen.Add(lane))
			{
				yield return lane;
			}
		}
	}

	private void LandStrike(ChaseStrike s, BossDef def)
	{
		s.Landed = true;
		if (s.Ball != null)
		{
			Destroy(s.Ball);
			s.Ball = null;
		}
		bool lasting = s.Active > 0.5f;
		if (!lasting)
		{
			foreach (GameObject m in s.Markers)
			{
				if (m != null)
				{
					Destroy(m);
				}
			}
			s.Markers.Clear();
		}
		m_shake = Mathf.Max(m_shake, 0.25f);
		GameObject? strikeFx = def.StrikeFx.Length > 0 ? FindFx(def.StrikeFx) : null;
		foreach (int lane in Distinct(s.Lanes))
		{
			Vector3 at = new Vector3(LaneX(lane), RoadY(m_z), m_z);
			bool shown = false;
			if (strikeFx != null)
			{
				PlayFx(strikeFx, at, Quaternion.identity, 3f);
				shown = true;
			}
			if (s.Attack != null)
			{
				foreach (GameObject fx in s.Attack.Impact)
				{
					PlayFx(fx, at, Quaternion.identity, 3f);
					shown = true;
					break;
				}
				if (!shown)
				{
					foreach (GameObject fx in s.Attack.Effects)
					{
						PlayFx(fx, at + Vector3.up * 0.5f, Quaternion.identity, 3f);
						shown = true;
						break;
					}
				}
			}
			if (def.Vines.Length > 0 && (s.Look == "strike" || s.Look == "vines" || s.Look == "wall"))
			{
				shown |= SpawnVine(def, LaneX(lane), m_z, Mathf.Max(0.9f, s.Active + 0.4f));
			}
			if (!shown || s.Look == "gas")
			{
				// Nothing of its own to show: a flash of the boss's colour.
				GameObject flash = s.Look == "gas" ? MakeCloud(lane, m_z) : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
				if (s.Look != "gas")
				{
					Destroy(flash.GetComponent<Collider>());
					flash.layer = m_layer;
					flash.transform.SetParent(m_stage, false);
					flash.transform.localScale = new Vector3(LaneWidth * 0.8f, 1.6f, LaneWidth * 0.8f);
					flash.transform.localPosition = at + Vector3.up * 1.6f;
					flash.GetComponent<Renderer>().sharedMaterial = TintMaterial(def.Tint);
				}
				Destroy(flash, lasting ? 0.4f : 0.6f);
			}
		}
		if (def.StrikeSfx.Length > 0)
		{
			m_audio.Play(def.StrikeSfx, 1f);
		}
		else
		{
			AttackSound(s.Attack, true, 1f);
		}
		if (lasting)
		{
			// What keeps the lanes deadly, riding along with you.
			foreach (int lane in Distinct(s.Lanes))
			{
				GameObject h;
				if (s.Look == "gas")
				{
					h = MakeCloud(lane, m_z);
					Vector3 cs = h.transform.localScale;
					h.transform.localScale = new Vector3(cs.x, cs.y, 9f);
				}
				else if (def.Vines.Length > 0 && (s.Look == "wall" || s.Look == "vines"))
				{
					h = new GameObject("Roots");
					h.transform.SetParent(m_stage, false);
				}
				else
				{
					h = GameObject.CreatePrimitive(PrimitiveType.Cube);
					Destroy(h.GetComponent<Collider>());
					h.layer = m_layer;
					h.transform.SetParent(m_stage, false);
					h.transform.localScale = new Vector3(LaneWidth * 0.9f, s.Look == "breath" ? 1.1f : 2.2f, 9f);
					Renderer r = h.GetComponent<Renderer>();
					r.sharedMaterial = TintMaterial(def.Tint);
					r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				}
				s.Hazards.Add(h);
			}
		}
	}

	// Lasting strikes follow you, leaving their own effects (fire, roots) behind in the lane.
	private void FollowHazards(ChaseStrike s, BossDef def, float dt)
	{
		int k = 0;
		foreach (int lane in Distinct(s.Lanes))
		{
			if (k < s.Hazards.Count && s.Hazards[k] != null)
			{
				GameObject h = s.Hazards[k];
				float y = h.name == "GasCloud" ? 0.9f : h.transform.localScale.y * 0.5f;
				h.transform.localPosition = new Vector3(LaneX(lane), RoadY(m_z) + y, m_z - 2.5f);
			}
			if (k < s.Markers.Count && s.Markers[k] != null)
			{
				PlaceMarker(s.Markers[k], lane, m_z);
				s.Markers[k].SetActive(true);
			}
			k++;
		}
		s.Trail -= dt;
		if (s.Trail > 0f)
		{
			return;
		}
		s.Trail = def.Vines.Length > 0 ? 0.45f : 0.3f;
		GameObject? fx = null;
		if (s.Attack != null)
		{
			foreach (GameObject e in s.Attack.Effects)
			{
				fx = e;
				break;
			}
			if (fx == null)
			{
				foreach (GameObject e in s.Attack.Impact)
				{
					fx = e;
					break;
				}
			}
		}
		foreach (int lane in Distinct(s.Lanes))
		{
			if (def.Vines.Length > 0 && (s.Look == "wall" || s.Look == "vines"))
			{
				SpawnVine(def, LaneX(lane) + Random.Range(-0.4f, 0.4f), m_z + 1.5f, 1.6f);
			}
			else if (fx != null && s.Look != "gas")
			{
				PlayFx(fx, new Vector3(LaneX(lane), RoadY(m_z) + 0.3f, m_z - 1f), Quaternion.identity, 1.6f);
			}
		}
		if (s.Look == "vines")
		{
			// Roots along the lane edges too, closing the lanes off from each other.
			for (int boundary = m_minLane; boundary < m_maxLane; boundary++)
			{
				SpawnVine(def, (boundary - 0.5f) * LaneWidth, m_z + 1.5f + Random.Range(-1f, 1f), 1.6f, 0.6f);
			}
		}
	}

	private void ClearStrike(ChaseStrike s)
	{
		foreach (GameObject m in s.Markers)
		{
			if (m != null)
			{
				Destroy(m);
			}
		}
		foreach (GameObject h in s.Hazards)
		{
			if (h != null)
			{
				Destroy(h);
			}
		}
		if (s.Ball != null)
		{
			Destroy(s.Ball);
		}
		s.Markers.Clear();
		s.Hazards.Clear();
	}

	private void ClearChase()
	{
		foreach (ChaseStrike s in m_chaseStrikes)
		{
			ClearStrike(s);
		}
		m_chaseStrikes.Clear();
		foreach (Vine v in m_vines)
		{
			if (v.Go != null)
			{
				Destroy(v.Go);
			}
		}
		m_vines.Clear();
		CloseExtraLanes(false);
	}

	// ---------------------------------------------------------------- vines

	// One of the boss's vines (the Elder's TentaRoot) bursting out of the road at x/z, standing
	// `life` seconds, then sinking back. False if it has none.
	private bool SpawnVine(BossDef def, float x, float z, float life, float size = 1f)
	{
		GameObject? prefab = def.Vines.Length > 0 ? Pictures.Prefab(def.Vines) : null;
		if (prefab == null)
		{
			return false;
		}
		CreatureInfo info = CreatureInfo.Of(prefab);
		float scale = info.Height > 0.3f ? Mathf.Clamp(2.6f * size / info.Height, 0.2f, 3f) : size;
		GameObject go = Creature(prefab, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), scale);
		float depth = Mathf.Max(1.5f, (info.Height > 0.3f ? info.Height : 2.6f) * scale);
		go.transform.localPosition = new Vector3(x, RoadY(z) - depth, z);
		Animator anim = go.GetComponentInChildren<Animator>();
		if (anim != null && info.Melee.Count > 0)
		{
			SetTrigger(anim, info.Melee[Random.Range(0, info.Melee.Count)].Animation);
		}
		m_vines.Add(new Vine { Go = go, Z = z, Life = life, Depth = depth });
		return true;
	}

	private void UpdateVines(float dt)
	{
		for (int i = m_vines.Count - 1; i >= 0; i--)
		{
			Vine v = m_vines[i];
			if (v.Go == null)
			{
				m_vines.RemoveAt(i);
				continue;
			}
			v.Age += dt;
			float up = Mathf.Clamp01(v.Age / 0.22f);
			float down = Mathf.Clamp01((v.Age - v.Life) / 0.45f);
			float h = (1f - Mathf.Pow(1f - up, 3f)) * (1f - down);
			Vector3 p = v.Go.transform.localPosition;
			v.Go.transform.localPosition = new Vector3(p.x, RoadY(v.Z) - v.Depth * (1f - h), v.Z);
			if (down >= 1f || v.Z < m_z - 60f)
			{
				Destroy(v.Go);
				m_vines.RemoveAt(i);
			}
		}
	}

	// ---------------------------------------------------------------- the chase itself

	private void UpdateChaseBoss(float dt, BossDef def)
	{
		Thing boss = m_bossThing!;
		m_bossIntro = Mathf.Min(1f, m_bossIntro + dt * 0.8f);
		float dist = ChaseDistance(def);
		m_bossOff = Mathf.MoveTowards(m_bossOff, -dist, dt * 4f);
		m_bossZ = m_z + m_bossOff;
		boss.Z = m_bossZ;
		// It follows you across the lanes a little; the side bosses keep to their side.
		float x = def.Mode == "side" ? m_bossSide * (TrackHalf + 2.5f) : m_x * 0.5f;
		Vector3 p = m_bossGo!.transform.localPosition;
		m_bossGo.transform.localPosition = new Vector3(Mathf.MoveTowards(p.x, x, dt * 5f), RoadY(m_bossZ) + def.Fly, m_bossZ);
		m_bossGo.transform.localRotation = Quaternion.RotateTowards(m_bossGo.transform.localRotation, Quaternion.identity, 360f * dt);
		if (m_bossAnim != null)
		{
			SetParam(m_bossAnim, "forward_speed", m_speed);
		}
		UpdateChaseStrikes(dt, def);
		if (m_extraLanes > 0f)
		{
			m_extraLanes -= dt;
			if (m_extraLanes <= 0f)
			{
				CloseExtraLanes(true);
			}
		}
		if (m_bossIntro < 1f)
		{
			return;
		}
		int phase = Mathf.Clamp(m_bossHits, 0, def.Moves.Length - 1);
		int stars = m_bossSeg?.Stars ?? 0;
		m_bossAttackTimer -= dt;
		if (m_bossAttackTimer <= 0f)
		{
			ChaseMove(def, NextMove(def, phase));
			float interval = (phase == 0 ? 2.4f : phase == 1 ? 1.95f : 1.55f) / (1f + 0.1f * stars);
			m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, interval);
		}
		m_bossHitTimer -= dt;
		if (m_bossHitTimer <= 0f)
		{
			m_bossHitTimer = 7f;
			ChasePickup(def, Random.Range(0, 3), m_bossZ + 1.5f, 4f);
		}
	}

	// A boss-hit item coming up a lane from behind, slower than its attacks: get in its lane.
	private void ChasePickup(BossDef def, int lane, float z, float relative)
	{
		GameObject? template = ItemTemplate(def.HitItem);
		if (template == null)
		{
			return;
		}
		Thing t = SpawnPickup(template, lane, z, Kind.BossHit);
		t.BaseY = t.Y;
		t.Speed = -(m_speed + relative);
		t.Life = 12f;
	}

	private void ChaseMove(BossDef def, string move)
	{
		string arg = "";
		int colon = move.IndexOf(':');
		if (colon > 0)
		{
			arg = move.Substring(colon + 1);
			move = move.Substring(0, colon);
		}
		float w = ChaseWarn();
		const string Blow = "antler|punch|slam|stomp|attack";
		switch (move)
		{
			case "strike1":
				ChaseStrikeAt(def, new[] { m_lane }, w, 0.3f, "strike", Blow);
				break;
			case "strike2":
				ChaseStrikeAt(def, new[] { m_lane, NearLane(m_lane) }, w + 0.1f, 0.3f, "strike", Blow);
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
				ChaseStrikeAt(def, lanes.ToArray(), w + 0.2f, 0.3f, "strike", Blow);
				break;
			}
			case "grab":
			{
				int lane = m_bossSide < 0 ? 0 : 2;
				ChaseStrikeAt(def, new[] { lane, Random.value < 0.4f ? 1 : lane }, w, 0.3f, "strike", "grab|punch|attack");
				break;
			}
			case "wall":
			case "gaswall":
			case "firewall":
			{
				string look = move == "gaswall" ? "gas" : move == "firewall" ? "fire" : "wall";
				ChaseStrikeAt(def, new[] { m_lane, NearLane(m_lane) }, w + 0.3f, 2.2f, look, move == "wall" ? "spawn|root|stomp" : "aoe|breath|nova|throw");
				// Nothing else while two lanes are closed.
				m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, w + 0.3f + 2.2f + 0.4f);
				break;
			}
			case "sweep":
			{
				int other = m_lane == 1 ? (Random.value < 0.5f ? 0 : 2) : 1;
				ChaseStrikeAt(def, new[] { m_lane, other }, w, 1.6f, "breath", "breath|beam|nova|flame");
				m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, w + 1.6f + 0.4f);
				break;
			}
			case "blobs":
			case "meteors":
			{
				// Two or three single-lane hits, half a second apart: always passable.
				int count = Random.Range(2, 4);
				for (int k = 0; k < count; k++)
				{
					ChaseStrikeAt(def, new[] { k == 0 ? m_lane : Random.Range(0, 3) }, w + 0.5f * k, 0.3f, move == "blobs" ? "gas" : "fire",
						move == "blobs" ? "throw|aoe|punch" : "meteor|cast|spawn", true);
				}
				break;
			}
			case "lob2":
				ChaseStrikeAt(def, new[] { m_lane, NearLane(m_lane) }, w, 0.4f, "fire", "throw|meteor|cast|spit", true);
				break;
			case "shot":
			case "shot2":
				ChaseShots(def, move == "shot2" ? 2 : 1, w);
				break;
			case "stomp":
				ChaseRow(def, "wave", "stomp|nova|slam");
				break;
			case "jumprow":
				ChaseRow(def, "low", "stomp|slam|breath");
				break;
			case "slide":
				ChaseRow(def, "high", "breath|beam|swing|attack");
				break;
			case "beam":
				ChaseRow(def, "mixed", "beam|nova|breath");
				break;
			case "minions":
				ChaseMinions(def, arg);
				break;
			case "branches":
				ChaseBranches(def);
				break;
			case "dash":
				ChaseDash(def);
				break;
		}
	}

	// The boss's projectile up a lane from behind, toward the lane you're in: slide under it or
	// change lane.
	private void ChaseShots(BossDef def, int count, float warn)
	{
		CreatureAttack? ranged = m_bossInfo?.PickRanged();
		if (ranged == null || ranged.Projectile == null || m_bossGo == null || m_bossHidden > 0f)
		{
			ChaseStrikeAt(def, count > 1 ? new[] { m_lane, NearLane(m_lane) } : new[] { m_lane }, warn, 0.3f, "strike", "antler|attack");
			return;
		}
		ChaseAnimate(ranged);
		int[] lanes = count > 1 ? new[] { m_lane, NearLane(m_lane) } : new[] { m_lane };
		const float relative = 10f;
		const float y = 1.1f;
		float startZ = m_bossZ + 1.5f;
		float startX = m_bossGo.transform.localPosition.x;
		float time = Mathf.Max(0.5f, (m_z - startZ) / relative);
		foreach (int lane in lanes)
		{
			GameObject wrapper = MakeShotVisual(ranged);
			float targetX = LaneX(lane);
			wrapper.transform.localPosition = new Vector3(startX, RoadY(startZ) + y, startZ);
			wrapper.transform.localRotation = Quaternion.LookRotation(new Vector3((targetX - startX) / time, 0f, m_speed + relative));
			m_things.Add(new Thing
			{
				Go = wrapper,
				Kind = Kind.High,
				Lane = lane,
				Z = startZ,
				BaseY = y,
				HalfLength = 0.45f,
				Speed = -(m_speed + relative),
				Label = def.Name,
				CrashText = $"{def.Name}'s shot hit you! (Slide under them, or change lane.)",
				FreeX = true,
				X = startX,
				TargetX = targetX,
				XSpeed = Mathf.Abs(targetX - startX) / time,
				Life = 6f,
				Hint = "SLIDE!",
			});
		}
	}

	// A row across all three lanes racing up from behind: waves and logs to jump, bars to
	// slide under, or a mix (one per lane).
	private void ChaseRow(BossDef def, string type, string attackKeys)
	{
		CreatureAttack? attack = BossAttackFor(attackKeys);
		ChaseAnimate(attack);
		BiomeRuntime b = m_biomes[BiomeAt(m_z)];
		float relative = 8.5f + 0.5f * Mathf.Clamp(m_bossHits, 0, 2);
		float z = m_bossZ + 1.5f;
		if (type == "wave")
		{
			m_shake = Mathf.Max(m_shake, 0.3f);
		}
		for (int lane = 0; lane < 3; lane++)
		{
			bool high = type == "high" || (type == "mixed" && Random.value < 0.5f);
			Thing t = high ? BossHigh(b, lane, z, def) : type == "wave" ? BossWave(lane, z, def, attack) : BossLow(b, lane, z, def);
			t.Speed = -(m_speed + relative);
			t.Life = 6f;
		}
	}

	// Creatures it calls running up behind you in two of the three lanes.
	private void ChaseMinions(BossDef def, string who)
	{
		who = who.Length > 0 ? who : def.Minion;
		BiomeRuntime b = m_biomes[BiomeAt(m_z)];
		GameObject? prefab = who.Length > 0 ? Pictures.Monster(who) : null;
		if (prefab == null)
		{
			prefab = SpawnTables.Pick(b.Monsters)?.Prefab;
		}
		if (prefab == null)
		{
			return;
		}
		ChaseAnimate(BossAttackFor("spawn|taunt|call|cast"));
		CreatureInfo info = CreatureInfo.Of(prefab);
		string label = SpawnTables.Label(prefab);
		int open = Random.Range(0, 3);
		const float relative = 5.5f;
		for (int lane = 0; lane < 3; lane++)
		{
			if (lane == open)
			{
				continue;
			}
			float z = m_bossZ + 2f + Random.Range(0f, 2f);
			GameObject go = Creature(prefab, Quaternion.identity, RealScale(info));
			float baseY = info.Flying ? 1.2f : 0f;
			go.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + baseY, z);
			Animator anim = go.GetComponentInChildren<Animator>();
			if (anim != null)
			{
				SetParam(anim, "forward_speed", info.RunSpeed);
				if (info.Flying)
				{
					SetBool(anim, "flying", true);
				}
			}
			GameObject marker = MakeMarker(lane, z + 2f, 4f, m_markerMaterial);
			marker.transform.SetParent(go.transform, true);
			m_things.Add(new Thing
			{
				Go = go,
				Kind = Kind.Block,
				Lane = lane,
				Z = z,
				BaseY = baseY,
				HalfLength = 0.8f,
				Speed = -(m_speed + relative),
				Label = label,
				CrashText = $"{def.Name}'s {label} ran you down! (Keep to the free lane.)",
				Life = 9f,
			});
		}
		m_banner = $"{def.Name} calls for help!";
		m_bannerTime = 1.2f;
	}

	// The charge Sam loves: the boss lowers its head behind you, then charges up your lane,
	// past you, and drops back in.
	private void ChaseDash(BossDef def)
	{
		if (m_bossGo == null || m_bossHidden > 0f || m_bossInfo == null)
		{
			return;
		}
		GameObject? prefab = Pictures.Prefab(def.Prefab);
		if (prefab == null)
		{
			return;
		}
		int lane = m_lane;
		float z = m_bossZ;
		float scale = m_bossInfo.Height > 0.3f ? Mathf.Min(1f, Mathf.Min(def.MaxHeight, 4.5f) / m_bossInfo.Height) : 1f;
		GameObject copy = Creature(prefab, Quaternion.identity, scale);
		copy.transform.localPosition = new Vector3(LaneX(lane), RoadY(z), z);
		Animator anim = copy.GetComponentInChildren<Animator>();
		CreatureAttack? charge = BossAttackFor("charge|dash|attack");
		if (anim != null)
		{
			SetParam(anim, "forward_speed", 12f);
			SetParam(anim, "onGround", true);
			if (charge != null)
			{
				SetTrigger(anim, charge.Animation);
			}
		}
		const float windup = 0.7f;
		const float relative = 12f;
		float travel = m_z - z + 8f;
		// The lane it will charge up glows the whole way, ahead of it.
		GameObject marker = MakeMarker(lane, z + travel * 0.5f, travel, m_markerMaterial);
		marker.transform.SetParent(copy.transform, true);
		float seconds = windup + travel / relative;
		m_things.Add(new Thing
		{
			Go = copy,
			Kind = Kind.Block,
			Lane = lane,
			Z = z,
			HalfLength = 1.2f,
			Speed = -m_speed,
			ChargeIn = windup,
			ChargeSpeed = -(m_speed + relative),
			Label = def.Name,
			CrashText = $"{def.Name} ran you down! (Get out of the glowing lane.)",
			Life = seconds + 2f,
		});
		m_bossGo.SetActive(false);
		m_bossHidden = seconds + 0.8f;
		// It comes back from further behind.
		m_bossOff = -(ChaseDistance(def) + 10f);
		m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, m_bossHidden + 1f);
		AttackSound(charge, false, 1f);
		m_banner = $"{def.Name} charges!";
		m_bannerTime = 1.2f;
	}

	// ---------------------------------------------------------------- the roots split the path

	private readonly List<GameObject> m_extraLaneObjects = new();

	// Two more lanes open beside the track; roots erupt in three of the five and along the lane
	// edges for a few seconds. The red lanes show which three, two seconds ahead.
	private void ChaseBranches(BossDef def)
	{
		if (m_extraLanes > 0f)
		{
			return;
		}
		const float warn = 2f;
		const float active = 3.2f;
		OpenExtraLanes(def, warn + active + 0.4f);
		List<int> lanes = new() { -1, 0, 1, 2, 3 };
		List<int> open = new();
		for (int k = 0; k < 2; k++)
		{
			int pick = lanes[Random.Range(0, lanes.Count)];
			lanes.Remove(pick);
			open.Add(pick);
		}
		ChaseStrikeAt(def, lanes.ToArray(), warn, active, "vines", "spawn|root|stomp");
		// Root walls between the lanes while the roots are up: you can't change lane inside.
		float z0 = m_z + m_speed * warn;
		float z1 = z0 + m_speed * active;
		for (int boundary = -1; boundary <= 2; boundary++)
		{
			m_dividers.Add((z0, z1, boundary));
		}
		// Something to throw at it comes up one of the free lanes while the roots are up.
		ChasePickup(def, open[0], m_z - 4f * (warn + 1.4f), 4f);
		m_banner = "The roots split the path!\nFind a lane without red.";
		m_bannerTime = 2f;
		m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, warn + active + 1f);
		m_bossHitTimer = Mathf.Max(m_bossHitTimer, warn + active + 3f);
	}

	private void OpenExtraLanes(BossDef def, float seconds)
	{
		m_minLane = -1;
		m_maxLane = 3;
		m_extraLanes = seconds;
		BiomeRuntime b = m_biomes[BiomeAt(m_z)];
		Material mat = m_terrainTrack ?? b.Ground;
		const float tile = 20f;
		float from = Mathf.Floor((m_z - 60f) / tile) * tile;
		float to = m_z + m_speed * seconds + 20f;
		foreach (int lane in new[] { -1, 3 })
		{
			for (float z = from; z < to; z += tile)
			{
				GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Quad);
				Destroy(strip.GetComponent<Collider>());
				strip.name = "ExtraLane";
				strip.layer = m_layer;
				strip.transform.SetParent(m_stage, false);
				strip.transform.localScale = new Vector3(LaneWidth, tile + 0.2f, 1f);
				float zc = z + tile * 0.5f;
				float slope = Mathf.Atan2(RoadY(zc + 1f) - RoadY(zc - 1f), 2f) * Mathf.Rad2Deg;
				strip.transform.localPosition = new Vector3(LaneX(lane), RoadY(zc) + 0.02f, zc);
				strip.transform.localRotation = Quaternion.Euler(90f - slope, 0f, 0f);
				Renderer r = strip.GetComponent<Renderer>();
				r.sharedMaterial = mat;
				r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				m_extraLaneObjects.Add(strip);
			}
			// The new lanes are cut by the roots: a burst of them along each.
			for (int k = 0; k < 4; k++)
			{
				SpawnVine(def, LaneX(lane) + (lane < 0 ? -1f : 1f) * LaneWidth * 0.62f, m_z - 4f - k * 5f, 1.4f, 0.7f);
			}
		}
	}

	private void CloseExtraLanes(bool push)
	{
		m_extraLanes = 0f;
		m_minLane = 0;
		m_maxLane = 2;
		foreach (GameObject go in m_extraLaneObjects)
		{
			if (go != null)
			{
				Destroy(go);
			}
		}
		m_extraLaneObjects.Clear();
		if (m_lane < 0 || m_lane > 2)
		{
			m_lane = Mathf.Clamp(m_lane, 0, 2);
			if (push)
			{
				m_audio.Play(Sfx.Lane, 0.5f, 1.15f);
			}
		}
	}

	// ---------------------------------------------------------------- the summoning

	private float m_summon = -1f;
	private float m_summonDelay = 3f;
	private bool m_summonLit;
	private bool m_summonFormed;
	private readonly List<GameObject> m_summonItems = new();
	private const float SummonRise = 0.9f;
	private const float SummonForm = 0.9f;

	private float SummonZ => m_z + (m_chase ? -13f : 24f);

	// As at the altar: the items rise and circle, the altar's sparkles play, then the boss forms.
	private void StartSummon(BossDef def)
	{
		ClearSummon();
		m_summon = 0f;
		m_summonLit = false;
		m_summonFormed = false;
		LocationAsset(def.Location);
		m_summonDelay = m_summonFx.TryGetValue(def.Location, out var fx) ? Mathf.Clamp(fx.delay, 1.5f, 3f) : 2.5f;
		GameObject? template = ItemTemplate(def.Item);
		if (template != null)
		{
			int n = Mathf.Clamp(def.Amount, 1, 6);
			for (int i = 0; i < n; i++)
			{
				GameObject item = Fitted(template, Quaternion.identity, new Vector3(0.7f, 0.7f, 0.7f), true, 3f);
				m_summonItems.Add(item);
			}
		}
		if (m_bossGo != null)
		{
			m_bossGo.SetActive(false);
		}
		m_banner = $"Summoning {def.Name}...";
		m_bannerTime = SummonRise + m_summonDelay;
		m_audio.Play(Sfx.Biome, 0.8f, 0.6f);
	}

	private void UpdateSummon(float dt, BossDef def)
	{
		m_summon += dt;
		float z = SummonZ;
		Vector3 centre = new Vector3(0f, RoadY(z) + 1.5f, z);
		float lit = SummonRise + m_summonDelay;
		float close = Mathf.Clamp01((m_summon - SummonRise) / m_summonDelay);
		float rise = Mathf.Clamp01(m_summon / SummonRise);
		for (int i = 0; i < m_summonItems.Count; i++)
		{
			GameObject item = m_summonItems[i];
			if (item == null)
			{
				continue;
			}
			float angle = m_summon * (1.6f + 4f * close * close) + i * Mathf.PI * 2f / m_summonItems.Count;
			float r = Mathf.Lerp(1.7f, 0.15f, close * close);
			item.transform.localPosition = centre + new Vector3(Mathf.Cos(angle) * r, -1.5f * (1f - rise) + Mathf.Sin(m_summon * 2.3f + i) * 0.15f, Mathf.Sin(angle) * r);
			item.transform.localRotation = Quaternion.Euler(0f, m_summon * 200f + i * 40f, 0f);
		}
		if (!m_summonLit && m_summon >= SummonRise)
		{
			m_summonLit = true;
			m_summonFx.TryGetValue(def.Location, out var fx);
			if (!PlayEffectList(fx.start, centre - Vector3.up * 1.5f))
			{
				m_audio.Play("sfx_offering|sfx_build_hammer_metal", 0.8f);
			}
			m_shake = Mathf.Max(m_shake, 0.1f);
		}
		if (!m_summonFormed && m_summon >= lit)
		{
			m_summonFormed = true;
			m_summonFx.TryGetValue(def.Location, out var fx);
			PlayEffectList(fx.done, centre - Vector3.up * 1.5f);
			foreach (GameObject item in m_summonItems)
			{
				if (item != null)
				{
					Destroy(item);
				}
			}
			m_summonItems.Clear();
			if (m_bossGo != null)
			{
				m_bossGo.SetActive(true);
				m_bossGo.transform.localScale = Vector3.one * m_bossScale * 0.05f;
			}
			m_shake = Mathf.Max(m_shake, 0.35f);
			if (m_bossInfo != null && m_bossInfo.AlertSound.Length > 0)
			{
				m_audio.Play(m_bossInfo.AlertSound, 1f);
			}
			m_banner = $"{def.Name}!";
			m_bannerTime = 2f;
		}
		if (m_summonFormed && m_bossGo != null)
		{
			float f = Mathf.Clamp01((m_summon - lit) / SummonForm);
			float ease = 1f - Mathf.Pow(1f - f, 3f);
			m_bossGo.transform.localScale = Vector3.one * m_bossScale * Mathf.Lerp(0.05f, 1f, ease);
			m_bossZ = z;
			m_bossOff = z - m_z;
			m_bossGo.transform.localPosition = new Vector3(0f, RoadY(z) + def.Fly * ease, z);
			m_bossGo.transform.localRotation = Quaternion.Euler(0f, m_chase ? 0f : 180f, 0f);
			if (m_bossThing != null)
			{
				m_bossThing.Z = z;
			}
			if (f >= 1f)
			{
				m_summon = -1f;
				m_bossIntro = m_chase ? 0f : 1f;
				m_bossAttackTimer = 1.8f;
				m_bossHitTimer = 4f;
			}
		}
	}

	// Plays a game EffectList (its particles as silent pictures, its sounds through ours).
	// False if it had nothing to show.
	private bool PlayEffectList(EffectList? list, Vector3 at)
	{
		if (list?.m_effectPrefabs == null)
		{
			return false;
		}
		bool any = false;
		foreach (EffectList.EffectData e in list.m_effectPrefabs)
		{
			if (e == null || e.m_prefab == null || !e.m_enabled)
			{
				continue;
			}
			if (e.m_prefab.GetComponentInChildren<ZSFX>(true) != null)
			{
				m_audio.Play(e.m_prefab.name, 1f);
				any = true;
			}
			if (e.m_prefab.GetComponentInChildren<ParticleSystem>(true) != null || e.m_prefab.GetComponentInChildren<Light>(true) != null)
			{
				PlayFx(e.m_prefab, at, Quaternion.identity, 5f);
				any = true;
			}
		}
		return any;
	}

	private void ClearSummon()
	{
		foreach (GameObject item in m_summonItems)
		{
			if (item != null)
			{
				Destroy(item);
			}
		}
		m_summonItems.Clear();
		m_summon = -1f;
	}

	// ---------------------------------------------------------------- hints

	private string m_hint = "";

	// "JUMP!" or "SLIDE!" when a boss's bar or wave is about to reach you in your lane.
	private void UpdateHints()
	{
		m_hint = "";
		float best = 1.1f;
		foreach (Thing t in m_things)
		{
			if (t.Hint.Length == 0 || !t.Collidable || t.Go == null)
			{
				continue;
			}
			float x = t.FreeX ? t.TargetX : LaneX(t.Lane);
			if (Mathf.Abs(x - m_x) > LaneWidth * 0.6f)
			{
				continue;
			}
			float closing = t.Speed + m_speed;
			if (Mathf.Abs(closing) < 0.1f)
			{
				continue;
			}
			float time = (t.Z - m_z) / closing;
			if (time > 0f && time < best)
			{
				best = time;
				m_hint = t.Hint;
			}
		}
	}
}
