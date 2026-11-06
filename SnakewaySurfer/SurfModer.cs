using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// 1.16.2 (Sam: Moder didn't fly, and her logs and slide bars were placeholders). As in the game,
// Moder flies (the animator's "flying" with its take-off and landing), and from the air she fires
// her ice volley: a shotgun spread of shards that land on marked spots ahead, grow into ice
// crystals and burst a moment later. Every row of a volley leaves a lane free, next to the free
// lane of the row before, so there is always a way through by weaving. Now and then she lands on
// the track ahead, facing you, and breathes frost down two lanes, then takes off again.
internal sealed partial class SurfGame
{
	private sealed class IceShard
	{
		public GameObject? Go;
		public Vector3 From;
		public Vector3 To;
		public float Wait;
		public float Time;
		public float Duration;
		public int Lane;
		public float X;
		public float Z;
		public GameObject? Marker;
	}

	private sealed class IceCrystal
	{
		public Thing Thing = null!;
		public float Fuse;
		public float Full;
		public GameObject? Marker;
		public Vector3 Scale;
	}

	private readonly List<IceShard> m_shards = new();
	private readonly List<IceCrystal> m_crystals = new();
	// Her height over the track now, whether she's down on it, and the breaths left before she
	// takes off again.
	private float m_bossFlyNow;
	private bool m_bossLanded;
	private int m_landBreaths;
	private float m_flightClock;
	private float m_wingSfx;

	private static bool Flies(BossDef def) => def.Mode == "fly";

	// Sam's favourite weapon for her landed phase. The game has no silver atgeir, so the iron
	// one is given a silver sheen (a mod's silver atgeir is used if there is one).
	private const string ModerWeapon = "AtgeirSilver|AtgeirIron|AtgeirBronze";
	private static readonly Color SilverSheen = new Color(0.86f, 0.92f, 1.08f);

	// How far ahead she keeps: well off while flying (her volleys land between you and her),
	// closer once she's down and running on the track.
	private float ModerDistance(BossDef def) => m_bossLanded
		? Mathf.Max(34f, m_speed * 1.2f + 16f)
		: Mathf.Max(def.Distance, m_speed * 2.2f + 16f);

	// Takes the boss weapons lying ahead off the track (arrows when she lands, the atgeir
	// when she flies off).
	private void ClearHitPickups()
	{
		for (int i = m_things.Count - 1; i >= 0; i--)
		{
			Thing t = m_things[i];
			if (t.Kind == Kind.BossHit)
			{
				DestroyThing(t);
				m_things.RemoveAt(i);
			}
		}
	}

	// A silver sheen on a picture (all its renderers).
	private void Silver(GameObject? go)
	{
		if (go == null)
		{
			return;
		}
		MaterialPropertyBlock block = new MaterialPropertyBlock();
		foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
		{
			if (r is ParticleSystemRenderer)
			{
				continue;
			}
			r.GetPropertyBlock(block);
			block.SetColor("_Color", SilverSheen);
			r.SetPropertyBlock(block);
		}
	}

	private void StartFlight(BossDef def)
	{
		ClearIce();
		m_bossFlyNow = def.Fly;
		m_bossLanded = false;
		m_landBreaths = 0;
		m_flightClock = Random.value * 10f;
		if (m_bossAnim != null)
		{
			SetParam(m_bossAnim, "onGround", false);
			SetParam(m_bossAnim, "flying", true);
			SetTrigger(m_bossAnim, "fly_takeoff");
		}
	}

	// Her height over the track: high and rising and falling with her wingbeats, or down on it.
	private float FlightHeight(BossDef def, float dt)
	{
		m_flightClock += dt;
		float want = m_bossLanded ? 0f : def.Fly + Mathf.Sin(m_flightClock * 1.3f) * 0.9f;
		m_bossFlyNow = Mathf.MoveTowards(m_bossFlyNow, want, dt * (m_bossLanded ? 5f : 3.5f));
		if (!m_bossLanded && m_bossInfo != null)
		{
			// The beat of her wings, now and then.
			m_wingSfx -= dt;
			if (m_wingSfx <= 0f)
			{
				m_wingSfx = Random.Range(2.5f, 4.5f);
				m_audio.Play("sfx_dragon_wingflap|sfx_dragon_flap|sfx_bird_flap", 0.45f, 0.8f);
			}
		}
		return m_bossFlyNow;
	}

	// Drifting from side to side over the track as she flies.
	private float FlightSway() => m_bossLanded ? 0f : Mathf.Sin(m_flightClock * 0.45f) * 3.2f;

	private void LandOnTrack(BossDef def)
	{
		if (m_bossLanded)
		{
			return;
		}
		m_bossLanded = true;
		// Frost, claws, frost (and claws again later in the fight); she stays down while the
		// atgeir she brings is still on the track ahead.
		m_landBreaths = m_bossHits >= 2 ? 4 : 3;
		ClearHitPickups();
		m_bossHitTimer = 0.6f;
		if (m_bossAnim != null)
		{
			SetTrigger(m_bossAnim, "fly_land");
			SetParam(m_bossAnim, "flying", false);
			SetParam(m_bossAnim, "onGround", true);
		}
		if (m_bossInfo != null && m_bossInfo.AlertSound.Length > 0)
		{
			m_audio.Play(m_bossInfo.AlertSound, 0.9f);
		}
		// Down on the track before the first breath.
		m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, 2.4f);
	}

	private void TakeWing()
	{
		if (!m_bossLanded)
		{
			return;
		}
		m_bossLanded = false;
		ClearHitPickups();
		m_bossHitTimer = Mathf.Max(m_bossHitTimer, 3f);
		if (m_bossAnim != null)
		{
			SetParam(m_bossAnim, "onGround", false);
			SetParam(m_bossAnim, "flying", true);
			SetTrigger(m_bossAnim, "fly_takeoff");
		}
		m_shake = Mathf.Max(m_shake, 0.15f);
		m_audio.Play("sfx_dragon_wingflap|sfx_dragon_flap|sfx_bird_flap", 0.8f, 0.7f);
		m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, 2f);
	}

	// The move after this one while she's down: frost and claws in turn, then up again
	// (not while her atgeir still lies ahead: you get your chance at her).
	private string LandedMove()
	{
		if (m_landBreaths > 0)
		{
			m_landBreaths--;
			return m_landBreaths % 2 == 0 ? "icebreath" : "claws";
		}
		foreach (Thing t in m_things)
		{
			if (t.Kind == Kind.BossHit && t.Z > m_z)
			{
				return "claws";
			}
		}
		return "takeoff";
	}

	// One of her own attacks by a word in its animation ("iceball", "breath"), for the move
	// that uses it, so the right clip plays.
	private string? BossAnimationFor(string move)
	{
		string want = move switch
		{
			"icevolley" or "icewide" => "iceball",
			"icebreath" or "sweep" => "breath",
			"claws" => Random.value < 0.5f ? "claw" : "bite",
			_ => "",
		};
		if (want.Length == 0 || m_bossInfo == null)
		{
			return null;
		}
		foreach (CreatureAttack a in m_bossInfo.Ranged)
		{
			if (a.Animation.ToLowerInvariant().Contains(want))
			{
				return a.Animation;
			}
		}
		foreach (CreatureAttack a in m_bossInfo.Melee)
		{
			if (a.Animation.ToLowerInvariant().Contains(want))
			{
				return a.Animation;
			}
		}
		return null;
	}

	// Her attack for one of these moves (null when she has none for it).
	private CreatureAttack? MoveAttack(string move)
	{
		string? anim = BossAnimationFor(move);
		return anim != null ? BossAttackFor(anim) : null;
	}

	// The wind-up of her clip for this move: the shards leave her mouth, or the frost comes,
	// when the blow of the clip does.
	private float BossWindup(string move, float fallback)
	{
		string? anim = BossAnimationFor(move);
		return anim != null ? AnimTiming.Delay(m_bossAnim, anim, fallback) : fallback;
	}

	// ---------------------------------------------------------------- the ice volley

	private void IceVolley(BossDef def, bool wide)
	{
		if (m_bossGo == null)
		{
			return;
		}
		int rows = wide ? 3 : 2;
		float windup = Mathf.Clamp(BossWindup("icevolley", 0.6f), 0.2f, 1.4f);
		// The rows land from the lead (AheadZ) on towards her, never past her if there's room,
		// and never so close that a crystal lands as you get there.
		float near = AheadZ(1.2f);
		float far = m_bossZ - 5f;
		float gap = Mathf.Max(6.5f, m_speed * 0.5f);
		gap = Mathf.Clamp((far - near) / (rows - 1), 6f, gap);
		float z0 = Mathf.Max(NearZ(windup + 1.4f), Mathf.Min(near, far - (rows - 1) * gap));
		// The first free lane isn't yours: the volley makes you move.
		int free = m_lane == 1 ? (Random.value < 0.5f ? 0 : 2) : 1;
		Vector3 mouth = m_bossGo.transform.localPosition + new Vector3(0f, def.MaxHeight * 0.55f, -2.5f);
		for (int r = 0; r < rows; r++)
		{
			float z = z0 + r * gap;
			for (int lane = 0; lane < 3; lane++)
			{
				if (lane == free)
				{
					continue;
				}
				GameObject marker = MakeMarker(lane, z + 0.6f, 3.6f, m_markerMaterial);
				int shards = wide ? 2 : Random.value < 0.45f ? 2 : 1;
				for (int k = 0; k < shards; k++)
				{
					float x = LaneX(lane) + Random.Range(-0.45f, 0.45f);
					float zz = z + k * 1.7f + Random.Range(-0.4f, 0.4f);
					m_shards.Add(new IceShard
					{
						From = mouth,
						To = new Vector3(x, RoadY(zz), zz),
						Wait = windup + r * 0.12f + k * 0.07f + Random.Range(0f, 0.08f),
						Duration = Random.Range(0.6f, 0.8f),
						Lane = lane,
						X = x,
						Z = zz,
						// One mark per lane of a row, carried by its first shard.
						Marker = k == 0 ? marker : null,
					});
				}
			}
			// The next row frees the same lane or one beside it.
			int step = Random.value < 0.4f ? 0 : Random.value < 0.5f ? -1 : 1;
			free = Mathf.Clamp(free + step, 0, 2);
		}
	}

	private GameObject? ShardVisual()
	{
		CreatureAttack? spit = MoveAttack("icevolley");
		GameObject? go = null;
		if (spit?.Projectile != null)
		{
			go = Instantiate(Picture(spit.Projectile), m_stage, false);
			go.SetActive(true);
			foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
			{
				ps.Play(true);
			}
		}
		if (go == null || go.GetComponentsInChildren<Renderer>().Length == 0)
		{
			if (go != null)
			{
				Destroy(go);
			}
			go = CrystalPicture(0.5f);
		}
		return go;
	}

	// An ice crystal from the frost caves (the game's own), about this tall.
	private GameObject CrystalPicture(float height)
	{
		GameObject? prefab = null;
		foreach (string name in new[] { "Pickable_MountainCaveCrystal", "crystal_wall_1x1", "Crystal" })
		{
			prefab = Pictures.Prefab(name);
			if (prefab != null)
			{
				break;
			}
		}
		GameObject go;
		if (prefab != null)
		{
			go = Fitted(Picture(prefab), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), new Vector3(height * 0.8f, height, height * 0.8f), true);
		}
		else
		{
			go = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Destroy(go.GetComponent<Collider>());
			go.transform.SetParent(m_stage, false);
			go.transform.localScale = new Vector3(height * 0.35f, height, height * 0.35f);
			go.transform.localRotation = Quaternion.Euler(0f, 45f, 12f);
			go.GetComponent<Renderer>().sharedMaterial = TintMaterial(new Color(0.6f, 0.85f, 1f));
		}
		Pictures.SetLayer(go, m_layer);
		return go;
	}

	// Shards in the air, and the crystals they grow into, burning down to their burst. Paused
	// while you aim (the whole run is).
	private void UpdateIce(float dt)
	{
		for (int i = m_shards.Count - 1; i >= 0; i--)
		{
			IceShard s = m_shards[i];
			if (s.Wait > 0f)
			{
				s.Wait -= dt;
				if (s.Wait <= 0f)
				{
					s.Go = ShardVisual();
					if (s.Go != null)
					{
						s.Go.transform.localPosition = s.From;
					}
				}
				continue;
			}
			s.Time += dt;
			float p = Mathf.Clamp01(s.Time / s.Duration);
			if (s.Go != null)
			{
				Vector3 at = Vector3.Lerp(s.From, s.To, p) + Vector3.up * (2.5f * p * (1f - p));
				Vector3 ahead = Vector3.Lerp(s.From, s.To, Mathf.Min(1f, p + 0.05f)) + Vector3.up * (2.5f * (p + 0.05f) * (0.95f - p));
				s.Go.transform.localPosition = at;
				if ((ahead - at).sqrMagnitude > 1e-4f)
				{
					s.Go.transform.localRotation = Quaternion.LookRotation(ahead - at);
				}
			}
			if (p >= 1f)
			{
				if (s.Go != null)
				{
					Destroy(s.Go);
				}
				ShardLands(s);
				m_shards.RemoveAt(i);
			}
		}
		for (int i = m_crystals.Count - 1; i >= 0; i--)
		{
			IceCrystal c = m_crystals[i];
			c.Fuse -= dt;
			if (c.Thing.Go != null)
			{
				// It grows out of the ground, then shivers as it's about to burst.
				float grow = Mathf.Clamp01((c.Full - c.Fuse) / 0.25f);
				float shiver = c.Fuse < 0.45f ? 1f + 0.08f * Mathf.Sin(Time.time * 60f) : 1f;
				c.Thing.Go.transform.localScale = c.Scale * Mathf.Max(0.05f, grow) * shiver;
			}
			if (c.Fuse <= 0f)
			{
				CrystalBursts(c);
				m_crystals.RemoveAt(i);
			}
		}
	}

	private void ShardLands(IceShard s)
	{
		string biome = m_biomes[BiomeAt(s.Z)].Def.Name;
		CreatureAttack? spit = MoveAttack("icevolley");
		Vector3 at = new Vector3(s.X, RoadY(s.Z), s.Z);
		if (FindFx("vfx_dragon_ice_hit") is GameObject hit)
		{
			PlayFx(hit, at + Vector3.up * 0.3f, Quaternion.identity, 2f);
		}
		else if (spit != null && spit.Impact.Count > 0)
		{
			PlayFx(spit.Impact[0], at + Vector3.up * 0.3f, Quaternion.identity, 2f);
		}
		AttackSound(spit, true, Mathf.Clamp01(1.1f - Mathf.Abs(s.Z - m_z) / 40f));
		m_shake = Mathf.Max(m_shake, Mathf.Clamp01(1f - Mathf.Abs(s.Z - m_z) / 30f) * 0.12f);
		GameObject crystal = CrystalPicture(Random.Range(1.5f, 1.9f));
		crystal.transform.localPosition = at;
		Vector3 scale = crystal.transform.localScale;
		crystal.transform.localScale = scale * 0.05f;
		Thing t = new Thing
		{
			Go = crystal,
			Kind = Kind.Block,
			Lane = s.Lane,
			Z = s.Z,
			HalfLength = 0.6f,
			FreeX = true,
			X = s.X,
			TargetX = s.X,
			Label = m_boss?.Name ?? "Moder",
			CrashText = $"You ran into {m_boss?.Name ?? "Moder"}'s ice in the {biome}!",
			Threat = true,
		};
		m_things.Add(t);
		float fuse = Random.Range(1.3f, 1.9f);
		// The mark stays with its crystal until the burst.
		m_crystals.Add(new IceCrystal { Thing = t, Fuse = fuse, Full = fuse, Marker = s.Marker, Scale = scale });
	}

	private void CrystalBursts(IceCrystal c)
	{
		Thing t = c.Thing;
		string biome = m_biomes[BiomeAt(t.Z)].Def.Name;
		Vector3 at = new Vector3(t.X, RoadY(t.Z) + 0.6f, t.Z);
		if (FindFx("fx_crystal_destruction") is GameObject burst)
		{
			PlayFx(burst, at, Quaternion.identity, 3f, null, 1.3f);
		}
		if (FindFx("vfx_dragon_ice_hit") is GameObject ice)
		{
			PlayFx(ice, at, Quaternion.identity, 2f, null, 1.6f);
		}
		float near = Mathf.Clamp01(1.1f - Mathf.Abs(t.Z - m_z) / 40f);
		m_audio.Play("sfx_frozenking_death_crystal_break|sfx_ice_destroyed|sfx_rock_destroyed", 0.8f * near, Random.Range(1.1f, 1.3f));
		m_shake = Mathf.Max(m_shake, Mathf.Clamp01(1f - Mathf.Abs(t.Z - m_z) / 25f) * 0.18f);
		if (t.Go != null)
		{
			Destroy(t.Go);
		}
		m_things.Remove(t);
		if (c.Marker != null)
		{
			Destroy(c.Marker);
		}
		// The burst throws ice about its lane for a moment.
		GameObject zone = new GameObject("IceBurst");
		zone.transform.SetParent(m_stage, false);
		zone.transform.localPosition = new Vector3(t.X, RoadY(t.Z), t.Z);
		m_things.Add(new Thing
		{
			Go = zone,
			Kind = Kind.Block,
			Lane = t.Lane,
			Z = t.Z,
			HalfLength = 1.4f,
			FreeX = true,
			X = t.X,
			TargetX = t.X,
			Label = t.Label,
			CrashText = $"{t.Label}'s ice burst over you in the {biome}!",
			Life = 0.3f,
			Threat = true,
		});
	}

	// ---------------------------------------------------------------- the frost breath

	// Down on the track, facing you: frost from her mouth down two lanes, all the way to her,
	// which lies frozen for a moment. Leave the marked lanes.
	private void IceBreath(BossDef def)
	{
		int other = m_lane == 1 ? (Random.value < 0.5f ? 0 : 2) : 1;
		float windup = Mathf.Clamp(BossWindup("icebreath", 1f), 0.3f, 1.8f);
		float near = NearZ(1.3f);
		float far = Mathf.Max(near + 6f, m_bossZ - 3f);
		float length = far - near;
		float mid = (near + far) * 0.5f;
		string biome = m_biomes[BiomeAt(mid)].Def.Name;
		float arrive = Mathf.Max(0.3f, (near - m_z) / Mathf.Max(1f, m_speed));
		CreatureAttack? breath = MoveAttack("icebreath");
		foreach (int lane in new[] { m_lane, other })
		{
			GameObject root = new GameObject("IceBreath");
			root.layer = m_layer;
			root.transform.SetParent(m_stage, false);
			root.transform.localPosition = new Vector3(LaneX(lane), RoadY(mid), mid);
			GameObject marker = MakeMarker(lane, mid, length, m_markerMaterial);
			marker.transform.SetParent(root.transform, true);
			// The frost lying down the lane: pale and see-through, with the game's breath in it.
			GameObject frost = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Destroy(frost.GetComponent<Collider>());
			frost.name = "Frost";
			frost.layer = m_layer;
			frost.transform.SetParent(root.transform, false);
			frost.transform.localScale = new Vector3(LaneWidth * 0.8f, 1.2f, length);
			frost.transform.localPosition = new Vector3(0f, 0.6f, 0f);
			Renderer r = frost.GetComponent<Renderer>();
			r.sharedMaterial = TintMaterial(def.Tint);
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			GameObject? fx = FindFx("vfx_dragon_coldbreath");
			if (fx == null && breath != null && breath.Effects.Count > 0)
			{
				fx = breath.Effects[0];
			}
			if (fx != null)
			{
				for (float d = -length * 0.5f + 1.5f; d < length * 0.5f; d += 4f)
				{
					// Blowing back toward you, as it comes from her.
					PlayFx(fx, new Vector3(0f, 0.8f, d), Quaternion.Euler(0f, 180f, 0f), 6f, frost.transform, 1f);
				}
			}
			frost.SetActive(false);
			m_things.Add(new Thing
			{
				Go = root,
				Kind = Kind.Block,
				Lane = lane,
				Z = mid,
				HalfLength = length * 0.5f,
				Label = def.Name,
				CrashText = $"{def.Name}'s frost breath froze you in the {biome}! (Leave the marked lanes.)",
				Life = arrive + length / Mathf.Max(1f, m_speed) + 1f,
				FireIn = Mathf.Max(0.05f, Mathf.Min(windup, arrive - 0.4f)),
				Moving = frost.transform,
				Threat = true,
			});
		}
		m_audio.Play("sfx_dragon_coldbreath_start", 1f);
		AttackSound(breath, false, 1f);
	}

	private void ClearIce()
	{
		foreach (IceShard s in m_shards)
		{
			if (s.Go != null)
			{
				Destroy(s.Go);
			}
			if (s.Marker != null)
			{
				Destroy(s.Marker);
			}
		}
		m_shards.Clear();
		foreach (IceCrystal c in m_crystals)
		{
			if (c.Thing.Go != null)
			{
				Destroy(c.Thing.Go);
			}
			m_things.Remove(c.Thing);
			if (c.Marker != null)
			{
				Destroy(c.Marker);
			}
		}
		m_crystals.Clear();
		m_bossLanded = false;
	}
}
