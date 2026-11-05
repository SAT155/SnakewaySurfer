using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16: what the crossing throws at you (SurfSea.cs). Serpents surface beside the ship and
// lunge along a red streak on the water; drakes off the Mountains coast and deathsquitos off the
// Plains spit or dive at you; leviathans drift across your way until a bolt sends them under;
// sea stacks stand in the channel; barrels, chests and driftwood float by. In the storm,
// lightning comes down on red rings and the serpent of the deep rises: it circles, lunges,
// slams the sea into a wave at you and comes up from under the keel. The bow ballista hurts
// whatever is above the water.
internal sealed partial class SurfGame
{
	private const float BallistaReload = 0.5f;
	private const float BoltSpeed = 62f;

	private sealed class SeaFoe
	{
		public string Kind = "";
		public GameObject Go = null!;
		public Animator? Anim;
		public CreatureInfo? Info;
		public CreatureAttack? Attack;
		public string Label = "";
		public Vector2 P;
		// Height over the water (flyers), or under it (< 0, swimmers).
		public float Y;
		public float Facing;
		public float Radius = 1.5f;
		public float Length = 2f;
		public int Hp = 1;
		public int MaxHp = 1;
		public int Phase;
		public float T;
		public float Wait;
		public int Attacks;
		public Vector2 Aim;
		public Vector2 Dir;
		public float Side = 1f;
		public bool Struck;
		public bool Dead;
		public bool Boss;
		public float Bob;
		public readonly List<GameObject> Markers = new();
		public string Move = "";
	}

	private sealed class SeaShot
	{
		public GameObject Go = null!;
		public Vector2 From;
		public Vector2 To;
		public float FromY;
		public float T;
		public float Flight;
		public float Radius = 2.4f;
		public GameObject? Marker;
		public SeaFoe? Owner;
		public string Why = "";
	}

	private sealed class BallistaBolt
	{
		public GameObject Go = null!;
		public Vector2 P;
		public Vector2 V;
		public float Y;
		public float Life;
	}

	private sealed class SeaStrike
	{
		public Vector2 At;
		public float Warn;
		public GameObject? Marker;
	}

	private readonly List<SeaFoe> m_seaFoes = new();
	private readonly List<SeaShot> m_seaShots = new();
	private readonly List<BallistaBolt> m_ballistaBolts = new();
	private readonly List<SeaStrike> m_seaStrikes = new();
	private float m_nextFoeZ;
	private float m_strikeTimer = 3f;
	private float m_rollerTimer = 4f;
	private SeaFoe? m_seaBoss;
	private bool m_seaBossBeaten;
	private float m_seaBossFlash;
	private Material? m_ringMaterial;
	private GameObject? m_boltTemplate;
	private Quaternion m_boltTurn = Quaternion.identity;
	private readonly Dictionary<GameObject, float> m_lengths = new();

	private int SeaStars => m_shipSeg?.Stars ?? 0;

	// ---------------------------------------------------------------- the story of a crossing

	private void UpdateSeaStory(float dt)
	{
		Segment? sea = m_shipSeg;
		if (sea == null)
		{
			return;
		}
		float along = m_z - sea.Start;
		float len = Mathf.Max(200f, Plugin.OceanLength.Value);
		bool fight = m_seaBoss != null && !m_seaBoss.Dead;
		// The storm gathers from a third of the way to the serpent and clears once it's beaten.
		float goal = m_seaBossBeaten ? 0f : Mathf.Clamp01((along - len * 0.3f) / (len * 0.15f));
		m_storm = Mathf.MoveTowards(m_storm, goal, dt / (m_seaBossBeaten ? 9f : 7f));
		if (!m_seaBossBeaten && m_seaBoss == null && along >= len)
		{
			if (Plugin.SeaSerpent.Value && Pictures.Prefab("Serpent") != null)
			{
				SpawnSeaBoss();
			}
			else
			{
				SeaBossDone();
			}
		}
		if (m_storm > 0.5f)
		{
			m_rollerTimer -= dt;
			if (m_rollerTimer <= 0f)
			{
				m_rollerTimer = fight ? Random.Range(8f, 11f) : Random.Range(5f, 7.5f) / (1f + 0.12f * SeaStars);
				SpawnRoller(WindVector(), Random.Range(2.2f, 3.4f) * m_storm, Random.Range(4.5f, 5.5f));
			}
		}
		if (m_storm > 0.6f)
		{
			m_strikeTimer -= dt;
			if (m_strikeTimer <= 0f)
			{
				m_strikeTimer = Random.Range(2.4f, 4.2f) / (1f + 0.1f * SeaStars);
				StartSeaStrike();
			}
		}
		if (!fight && m_z >= m_nextFoeZ && (sea.End == float.MaxValue || m_z < sea.End - 90f))
		{
			SpawnSeaFoes(along, len, sea);
			float difficulty = Mathf.Clamp01(along / len);
			m_nextFoeZ = m_z + Mathf.Lerp(34f, 24f, difficulty) / (1f + 0.15f * SeaStars) * Random.Range(0.8f, 1.2f);
		}
	}

	private void SpawnSeaFoes(float along, float len, Segment sea)
	{
		if (along < 60f)
		{
			if (Random.value < 0.6f)
			{
				SpawnLoot(false);
			}
			else
			{
				SpawnGulls();
			}
			return;
		}
		int serpents = m_seaFoes.FindAll(f => f.Kind == "serpent" && !f.Dead).Count;
		int flyers = m_seaFoes.FindAll(f => (f.Kind == "drake" || f.Kind == "squito") && !f.Dead).Count;
		float r = Random.value;
		if (m_seaBossBeaten)
		{
			// The home stretch: calmer water, then the Plains' deathsquitos over the shallows.
			bool nearShore = sea.End != float.MaxValue && sea.End - m_z < 260f;
			if (nearShore && r < 0.45f && flyers < 2)
			{
				SpawnFlyer("squito");
			}
			else if (r < 0.65f)
			{
				SpawnLoot(Random.value < 0.25f);
			}
			else if (r < 0.82f)
			{
				SpawnLeviathan();
			}
			else
			{
				SpawnRocks();
			}
			if (Random.value < 0.25f)
			{
				SpawnGulls();
			}
			return;
		}
		float p = along / len;
		if (p < 0.4f)
		{
			// Off the Mountains coast: drakes come down from the peaks.
			if (r < 0.28f && flyers < 2)
			{
				SpawnFlyer("drake");
			}
			else if (r < 0.5f && serpents < 1)
			{
				SpawnSerpent();
			}
			else if (r < 0.72f)
			{
				SpawnRocks();
			}
			else if (r < 0.82f)
			{
				SpawnLeviathan();
			}
			else
			{
				SpawnLoot(Random.value < 0.2f);
			}
		}
		else
		{
			// The storm: serpents rise and the sea is wild.
			if (r < 0.4f && serpents < 1 + SeaStars / 2)
			{
				SpawnSerpent();
			}
			else if (r < 0.62f)
			{
				SpawnRocks();
			}
			else if (r < 0.75f)
			{
				SpawnLeviathan();
			}
			else
			{
				SpawnLoot(Random.value < 0.35f);
			}
		}
	}

	// ---------------------------------------------------------------- making them

	// A creature (or prop) picture stretched to `length` along its longest side, in a wrapper.
	private GameObject SeaPicture(GameObject prefab, float length, out Animator? anim)
	{
		GameObject wrapper = new GameObject(prefab.name + "_Sea") { layer = m_layer };
		wrapper.transform.SetParent(m_stage, false);
		GameObject go = Instantiate(Picture(prefab), wrapper.transform, false);
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		if (!m_lengths.TryGetValue(prefab, out float size))
		{
			size = Pictures.MeshBounds(go, out Bounds b) ? Mathf.Max(b.size.x, b.size.z) : 1f;
			m_lengths[prefab] = Mathf.Max(0.1f, size);
			size = m_lengths[prefab];
		}
		wrapper.transform.localScale = Vector3.one * Mathf.Clamp(length / size, 0.05f, 20f);
		anim = go.GetComponentInChildren<Animator>();
		return wrapper;
	}

	private SeaFoe? SpawnCreature(string kind, string prefabs, float length, Vector2 at)
	{
		GameObject? prefab = null;
		foreach (string name in prefabs.Split('|'))
		{
			prefab = Pictures.Prefab(name);
			if (prefab != null)
			{
				break;
			}
		}
		if (prefab == null)
		{
			return null;
		}
		GameObject go = SeaPicture(prefab, length, out Animator? anim);
		CreatureInfo info = CreatureInfo.Of(prefab);
		Character? c = prefab.GetComponent<Character>();
		SeaFoe f = new SeaFoe
		{
			Kind = kind,
			Go = go,
			Anim = anim,
			Info = info,
			Label = Localize(c != null ? c.m_name : prefab.name),
			P = at,
			Length = length,
			Radius = length * 0.25f,
			Bob = Random.Range(0f, 10f),
		};
		if (anim != null)
		{
			SetParam(anim, "inWater", kind == "serpent" || kind == "boss");
			SetParam(anim, "onGround", false);
			SetParam(anim, "flying", info.Flying);
		}
		m_seaFoes.Add(f);
		return f;
	}

	private Vector2 AheadSpot(float ahead, float spread = 1f)
	{
		return new Vector2(Mathf.Clamp(m_x + Random.Range(-20f, 20f) * spread, -SeaHalfWidth, SeaHalfWidth), m_z + ahead);
	}

	private void SpawnSerpent()
	{
		float side = Random.value < 0.5f ? -1f : 1f;
		Vector2 at = new Vector2(m_x + side * Random.Range(14f, 24f), m_z + Random.Range(50f, 70f));
		SeaFoe? f = SpawnCreature("serpent", "Serpent", 9f + SeaStars * 0.5f, at);
		if (f == null)
		{
			return;
		}
		f.Y = -2.4f;
		f.Side = side;
		f.Hp = f.MaxHp = 2 + SeaStars / 2;
		f.Radius = 1.3f;
		f.Facing = 180f;
		f.Attack = f.Info?.PickMelee();
		f.Wait = Random.Range(0.5f, 1.5f);
	}

	private void SpawnFlyer(string kind)
	{
		Vector2 at = AheadSpot(Random.Range(55f, 65f));
		SeaFoe? f = kind == "drake"
			? SpawnCreature("drake", "Hatchling", 2.6f, at)
			: SpawnCreature("squito", "Deathsquito", 1.6f, at);
		if (f == null)
		{
			return;
		}
		f.Y = kind == "drake" ? 7f : 3.5f;
		f.Facing = 180f;
		f.Radius = kind == "drake" ? 1.6f : 1.2f;
		f.Side = Random.value < 0.5f ? -1f : 1f;
		f.Attack = kind == "drake" ? f.Info?.PickRanged() ?? f.Info?.PickMelee() : f.Info?.PickMelee();
		f.Wait = Random.Range(0.8f, 1.6f);
	}

	private void SpawnLeviathan()
	{
		if (m_seaFoes.Exists(f => f.Kind == "leviathan" && !f.Dead))
		{
			return;
		}
		float side = Random.value < 0.5f ? -1f : 1f;
		Vector2 at = new Vector2(side * Random.Range(4f, 16f), m_z + Random.Range(80f, 95f));
		SeaFoe? f = SpawnCreature("leviathan", "Leviathan", Random.Range(16f, 20f), at);
		if (f == null)
		{
			return;
		}
		f.Y = -0.6f;
		f.Facing = Random.Range(0f, 360f);
		f.Radius = f.Length * 0.32f;
		f.Hp = f.MaxHp = 2;
		// It drifts slowly across your way.
		f.Dir = new Vector2(-side * Random.Range(0.6f, 1.4f), 0f);
	}

	private void SpawnRocks()
	{
		GameObject? prefab = null;
		string[] names = { "rock4_coast", "RockFinger", "RockThumb", "rock3_mountain", "rock4_heath", "Rock_7" };
		for (int tries = 0; tries < 6 && prefab == null; tries++)
		{
			prefab = Pictures.Prefab(names[Random.Range(0, names.Length)]);
		}
		if (prefab == null)
		{
			return;
		}
		// A few stacks, always leaving a way through.
		int count = Random.Range(1, 3 + Mathf.Min(1, SeaStars));
		float gapX = Random.Range(-14f, 14f);
		for (int i = 0; i < count; i++)
		{
			float x = Random.Range(-SeaHalfWidth + 2f, SeaHalfWidth - 2f);
			if (Mathf.Abs(x - gapX) < 7f)
			{
				x = gapX + Mathf.Sign(x - gapX + 0.01f) * 7f;
			}
			float size = Random.Range(4.5f, 8f);
			GameObject go = SeaPicture(prefab, size, out _);
			go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
			SeaFoe f = new SeaFoe
			{
				Kind = "rock",
				Go = go,
				P = new Vector2(x, m_z + Random.Range(72f, 90f)),
				Y = -1.2f,
				Radius = size * 0.42f,
				Length = size,
				Hp = 9999,
			};
			m_seaFoes.Add(f);
		}
	}

	private void SpawnLoot(bool driftwood)
	{
		GameObject? prefab = driftwood
			? Pictures.Item("Wood") ?? Pictures.Item("FineWood")
			: Pictures.Prefab("shipwreck_karve_chest") ?? FindFx("barrell|BeerBarrel|barrel");
		GameObject go;
		if (prefab != null)
		{
			go = SeaPicture(prefab, driftwood ? 1.8f : 1.4f, out _);
		}
		else if (m_coinTemplate != null)
		{
			go = Instantiate(m_coinTemplate, m_stage, false);
		}
		else
		{
			return;
		}
		Vector2 at = AheadSpot(Random.Range(60f, 75f), 0.9f);
		m_seaFoes.Add(new SeaFoe
		{
			Kind = driftwood ? "wood" : "loot",
			Go = go,
			P = at,
			Y = -0.15f,
			Radius = 2f,
			Facing = Random.Range(0f, 360f),
			Bob = Random.Range(0f, 10f),
			Hp = 9999,
		});
	}

	private void SpawnGulls()
	{
		GameObject? prefab = Pictures.Prefab("Seagal");
		if (prefab == null)
		{
			return;
		}
		float side = Random.value < 0.5f ? -1f : 1f;
		int n = Random.Range(2, 5);
		Vector2 start = new Vector2(-side * 45f, m_z + Random.Range(10f, 40f));
		for (int i = 0; i < n; i++)
		{
			SeaFoe? f = SpawnCreature("gull", "Seagal", 1.1f, start + Random.insideUnitCircle * 4f);
			if (f == null)
			{
				return;
			}
			f.Y = Random.Range(7f, 11f);
			f.Dir = new Vector2(side * Random.Range(7f, 9f), Random.Range(1f, 4f));
			f.Facing = Mathf.Atan2(f.Dir.x, f.Dir.y) * Mathf.Rad2Deg;
			f.Hp = 9999;
		}
		m_audio.Play("sfx_seagull_idle", 0.5f, Random.Range(0.9f, 1.1f));
	}

	// ---------------------------------------------------------------- the serpent of the deep

	private void SpawnSeaBoss()
	{
		Vector2 at = new Vector2(m_x + 18f, m_z + 40f);
		SeaFoe? f = SpawnCreature("boss", "Serpent", 17f + SeaStars, at);
		if (f == null)
		{
			SeaBossDone();
			return;
		}
		f.Boss = true;
		f.Y = -3f;
		f.Hp = f.MaxHp = 6 + 2 * SeaStars;
		f.Radius = 2.2f;
		f.Side = 1f;
		f.Attack = f.Info?.PickMelee();
		f.Wait = 3.5f;
		f.Phase = 0;
		m_seaBoss = f;
		ApplyStarsTint(f);
		m_audio.Play("sfx_serpent_taunt|sfx_serpent_alerted", 1f, 0.75f);
		m_shake = Mathf.Max(m_shake, 0.4f);
		Plugin.Log.LogInfo($"The serpent rises: {f.Hp} hits to beat.");
	}

	// Bigger, darker: an old one.
	private static void ApplyStarsTint(SeaFoe f)
	{
		foreach (Renderer r in f.Go.GetComponentsInChildren<Renderer>())
		{
			foreach (Material m in r.materials)
			{
				if (m.HasProperty("_Color"))
				{
					m.color = m.color * new Color(0.75f, 0.85f, 0.95f, 1f);
				}
			}
		}
	}

	private void SeaBossDone()
	{
		m_seaBossBeaten = true;
		if (m_shipSeg != null && m_shipSeg.End == float.MaxValue)
		{
			CloseSegment(m_shipSeg, m_z + Mathf.Max(200f, Plugin.OceanLength.Value * 0.35f));
			m_groundZ = float.MaxValue;
		}
	}

	// ---------------------------------------------------------------- each frame

	private void UpdateSeaFoes(float dt)
	{
		m_seaBossFlash = Mathf.MoveTowards(m_seaBossFlash, 0f, dt * 3f);
		if (m_things.Count == 0)
		{
			// Out of sight of land nothing else watches the creatures' attack clips.
			UpdateAnimWatches(dt);
		}
		UpdateBallistaBolts(dt);
		for (int i = m_seaFoes.Count - 1; i >= 0; i--)
		{
			SeaFoe f = m_seaFoes[i];
			if (f.Go == null)
			{
				m_seaFoes.RemoveAt(i);
				continue;
			}
			f.T += dt;
			if (m_state == State.Running)
			{
				switch (f.Kind)
				{
					case "serpent":
						UpdateSerpent(f, dt);
						break;
					case "boss":
						UpdateSeaBoss(f, dt);
						break;
					case "drake":
					case "squito":
						UpdateFlyer(f, dt);
						break;
					case "leviathan":
						UpdateLeviathan(f, dt);
						break;
					case "rock":
						if (!f.Struck && ShipDistance(f.P) < f.Radius)
						{
							f.Struck = true;
							Ram(f.P, "You ran onto the rocks!");
						}
						break;
					case "loot":
					case "wood":
						f.P += WindVector() * (0.6f * dt);
						f.Facing += 8f * dt;
						if (!f.Dead && ShipDistance(f.P) < f.Radius)
						{
							PickLoot(f);
						}
						break;
					case "gull":
						f.P += f.Dir * dt;
						break;
				}
			}
			if (f.Dead && f.Kind != "boss" && f.Kind != "serpent" && f.Kind != "drake" && f.Kind != "squito" && f.Kind != "leviathan")
			{
				DestroyFoe(f);
				m_seaFoes.RemoveAt(i);
				continue;
			}
			PlaceFoe(f, dt);
			// Gone behind, or far out to the side.
			if (f.P.y < m_z - 40f || Mathf.Abs(f.P.x - m_x) > 90f || (f.Dead && f.T > 4f))
			{
				if (f == m_seaBoss && !f.Dead)
				{
					// The serpent never falls behind for good: it comes round again.
					f.P = new Vector2(m_x + f.Side * 20f, m_z + 30f);
					continue;
				}
				DestroyFoe(f);
				m_seaFoes.RemoveAt(i);
			}
		}
		UpdateSeaShots(dt);
		UpdateSeaStrikes(dt);
	}

	private void PlaceFoe(SeaFoe f, float dt)
	{
		float water = SeaHeight(f.P.x, f.P.y);
		float bob = f.Kind == "rock" ? 0f : Mathf.Sin(m_seaClock * 1.7f + f.Bob) * 0.15f;
		f.Go.transform.localPosition = new Vector3(f.P.x, (f.Kind == "rock" ? SeaY : water) + f.Y + bob, f.P.y);
		Quaternion turn = Quaternion.Euler(0f, f.Facing, 0f);
		if (f.Dead && (f.Kind == "serpent" || f.Kind == "boss" || f.Kind == "drake" || f.Kind == "squito"))
		{
			// Rolled over, going under.
			turn *= Quaternion.Euler(0f, 0f, Mathf.Min(150f, f.T * 120f));
		}
		if (f.Kind != "rock")
		{
			f.Go.transform.localRotation = Quaternion.Slerp(f.Go.transform.localRotation, turn, 1f - Mathf.Exp(-8f * dt));
		}
		if (f.Anim != null)
		{
			SetParam(f.Anim, "forward_speed", f.Dir.magnitude);
		}
	}

	private void DestroyFoe(SeaFoe f)
	{
		foreach (GameObject m in f.Markers)
		{
			if (m != null)
			{
				Destroy(m);
			}
		}
		f.Markers.Clear();
		if (f.Go != null)
		{
			Destroy(f.Go);
		}
	}

	private void ClearSeaFoes()
	{
		foreach (SeaFoe f in m_seaFoes)
		{
			DestroyFoe(f);
		}
		m_seaFoes.Clear();
		foreach (SeaShot s in m_seaShots)
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
		m_seaShots.Clear();
		foreach (BallistaBolt b in m_ballistaBolts)
		{
			if (b.Go != null)
			{
				Destroy(b.Go);
			}
		}
		m_ballistaBolts.Clear();
		foreach (SeaStrike s in m_seaStrikes)
		{
			if (s.Marker != null)
			{
				Destroy(s.Marker);
			}
		}
		m_seaStrikes.Clear();
		m_seaBoss = null;
		m_seaBossBeaten = false;
		m_nextFoeZ = 0f;
		m_rollerTimer = 4f;
		m_strikeTimer = 3f;
	}

	// How far a point is from the ship's hull (negative: inside it).
	private float ShipDistance(Vector2 p)
	{
		Vector2 c = ShipCenter();
		Vector2 f = HeadingVector();
		float half = Mathf.Max(0f, m_shipLength * 0.5f - m_shipBeam);
		float t = Mathf.Clamp(Vector2.Dot(p - c, f), -half, half);
		return Vector2.Distance(p, c + f * t) - m_shipBeam;
	}

	private static Vector2 Dir2(float degrees) => new Vector2(Mathf.Sin(degrees * Mathf.Deg2Rad), Mathf.Cos(degrees * Mathf.Deg2Rad));

	private static float Angle2(Vector2 v) => Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;

	// Runs something hard: a heavy hit nothing blocks, and the ship is thrown back.
	private void Ram(Vector2 at, string why)
	{
		Vector2 away = ShipCenter() - at;
		m_boatSpeed *= 0.3f;
		m_lurchV += (Vector2.Dot(away, new Vector2(HeadingVector().y, -HeadingVector().x)) > 0f ? 1f : -1f) * 90f;
		m_heading = Mathf.Clamp(m_heading + Mathf.Sign(Vector2.Dot(away, new Vector2(HeadingVector().y, -HeadingVector().x)) + 0.01f) * 25f, -MaxHeading, MaxHeading);
		SpawnSplash(at, 1.4f);
		m_audio.Play("sfx_ship_impact|sfx_wood_hit", 1f, 0.8f);
		m_shake = Mathf.Max(m_shake, 0.5f);
		Hit(new Thing { Label = "", JumpOrSlide = true, Heavy = true }, why);
	}

	// A creature's blow on the ship: blocks and parries as on land. 0 hurt, 1 blocked, 2 parried.
	private int SeaHit(SeaFoe? foe, bool heavy, string why)
	{
		bool parry = m_hurtTime <= 0f && m_blocking && m_runTime - m_blockPressed <= Plugin.ParryWindow.Value;
		int lives = m_lives;
		float hurt = m_hurtTime;
		Hit(new Thing { Label = foe?.Label ?? "it", Heavy = heavy }, why);
		if (parry)
		{
			if (foe?.Anim != null)
			{
				SetTrigger(foe.Anim, "stagger");
			}
			return 2;
		}
		bool unharmed = m_lives == lives && m_state == State.Running && Mathf.Approximately(m_hurtTime, hurt) && !Plugin.Invincible.Value;
		return unharmed ? 1 : 0;
	}

	private void PickLoot(SeaFoe f)
	{
		f.Dead = true;
		if (f.Kind == "wood" && m_lives < Plugin.Lives.Value)
		{
			// Driftwood to mend the hull: a life back.
			m_lives++;
			Flash(new Color(0.4f, 1f, 0.5f), 0.45f);
			m_audio.Play("sfx_build_hammer_wood|sfx_wood_hit", 0.9f, 1.1f);
		}
		else
		{
			int coins = f.Kind == "wood" ? 5 : Random.Range(8, 16);
			m_coins += coins;
			m_audio.Play(Sfx.Coin, 1f, 0.9f);
			m_audio.Play("sfx_coins_placed|sfx_pickable_pick", 0.6f, 1f);
		}
		AddFoam(f.P, 1.5f, 0.9f, 0.7f, 2f);
	}

	// ---------------------------------------------------------------- serpents

	private void SerpentMarker(SeaFoe f, Vector2 from, Vector2 to, float width)
	{
		GameObject m = f.Markers.Count > 0 && f.Markers[0] != null ? f.Markers[0] : WaterMarker(false);
		if (f.Markers.Count == 0)
		{
			f.Markers.Add(m);
		}
		Vector2 mid = (from + to) * 0.5f;
		PlaceWaterMarker(m, mid, width, Vector2.Distance(from, to), Angle2(to - from));
		m.SetActive(true);
	}

	private void HideMarkers(SeaFoe f)
	{
		foreach (GameObject m in f.Markers)
		{
			if (m != null)
			{
				m.SetActive(false);
			}
		}
	}

	// Under the water toward the ship (a wake shows it), up beside it, a lunge along a red
	// streak, a moment on the surface to be shot, then under again. Two lunges and it's gone.
	private void UpdateSerpent(SeaFoe f, float dt)
	{
		if (f.Dead)
		{
			f.Y -= dt * 1.2f;
			return;
		}
		Vector2 ship = ShipCenter();
		Vector2 shipVel = HeadingVector() * m_boatSpeed;
		float windup = Mathf.Max(0.7f, 1.15f - 0.1f * SeaStars);
		switch (f.Phase)
		{
			case 0:
			{
				// Swimming under, to a spot beside the ship.
				Vector2 spot = ship + new Vector2(f.Side * 12f, 6f) + shipVel * 0.8f;
				Vector2 to = spot - f.P;
				float speed = m_boatSpeed + 8f;
				f.Dir = to.normalized * speed;
				f.Facing = Mathf.MoveTowardsAngle(f.Facing, Angle2(to), 160f * dt);
				f.P += Dir2(f.Facing) * (speed * dt);
				f.Y = Mathf.MoveTowards(f.Y, -2.4f, dt * 2f);
				if (Random.value < dt * 10f)
				{
					AddFoam(f.P - Dir2(f.Facing) * 2f, Random.Range(0.6f, 1.1f), 1.2f, 0.45f, 1.8f);
				}
				f.Wait -= dt;
				if (to.magnitude < 3f && f.Wait <= 0f)
				{
					f.Phase = 1;
					f.T = 0f;
					f.Aim = ship + shipVel * (windup + 0.25f);
					f.Facing = Angle2(f.Aim - f.P);
					SpawnSplash(f.P, 1f);
					m_audio.Play("sfx_serpent_alerted|sfx_serpent_idle", 0.9f, 1f);
					TriggerWatched(f.Anim, f.Attack?.Animation ?? "attack");
				}
				break;
			}
			case 1:
			{
				// Up and rearing: the streak shows where it will strike.
				f.Y = Mathf.MoveTowards(f.Y, -0.3f, dt * 5f);
				// It follows the ship with its eyes until just before the lunge.
				if (f.T < windup * 0.7f)
				{
					f.Aim = ship + shipVel * (windup - f.T + 0.25f);
					f.Facing = Mathf.MoveTowardsAngle(f.Facing, Angle2(f.Aim - f.P), 220f * dt);
				}
				Vector2 dir = Dir2(f.Facing);
				f.P += shipVel * dt;
				float reach = Vector2.Distance(f.P, f.Aim) + 5f;
				bool blink = f.T < windup - 0.35f || Mathf.FloorToInt(f.T * 12f) % 2 == 0;
				SerpentMarker(f, f.P, f.P + dir * reach, 3.2f);
				f.Markers[0].SetActive(blink);
				if (f.T >= windup)
				{
					f.Phase = 2;
					f.T = 0f;
					f.Dir = dir * (reach / 0.4f);
					f.Struck = false;
					AttackSound(f.Attack, false, 1f);
					m_audio.Play("sfx_serpent_attack|sfx_serpent_attack_trigger", 1f, 1f);
				}
				break;
			}
			case 2:
			{
				// The lunge.
				f.P += f.Dir * dt;
				Vector2 head = f.P + Dir2(f.Facing) * (f.Length * 0.35f);
				if (!f.Struck && ShipDistance(head) < 0.9f)
				{
					f.Struck = true;
					int result = SeaHit(f, true, $"The {f.Label} struck the ship!");
					m_audio.Play("sfx_serpent_attack_hit", 1f, 1f);
					if (result == 2)
					{
						// Parried: it reels, wide open.
						f.Phase = 3;
						f.T = -1.5f;
						f.Dir = Vector2.zero;
						HideMarkers(f);
						break;
					}
				}
				if (f.T >= 0.4f)
				{
					f.Phase = 3;
					f.T = 0f;
					f.Dir = Vector2.zero;
					HideMarkers(f);
				}
				break;
			}
			case 3:
				// On the surface: shoot it now.
				f.P += shipVel * dt * 0.6f;
				if (f.T > 1.6f)
				{
					f.Attacks++;
					f.Phase = 0;
					f.T = 0f;
					f.Side = -f.Side;
					f.Wait = 1.2f;
					SpawnSplash(f.P, 0.8f);
					if (f.Attacks >= 2)
					{
						// Off it goes.
						f.Phase = 9;
					}
				}
				break;
			case 9:
				f.Y = Mathf.MoveTowards(f.Y, -4f, dt * 2f);
				f.P += Dir2(f.Facing) * (6f * dt);
				if (f.Y <= -3.9f)
				{
					f.Dead = true;
					f.T = 99f;
				}
				break;
		}
	}

	private bool Surfaced(SeaFoe f) => f.Y > -1.2f;

	// ---------------------------------------------------------------- the serpent of the deep

	private void UpdateSeaBoss(SeaFoe f, float dt)
	{
		if (f.Dead)
		{
			f.Y -= dt * 0.8f;
			return;
		}
		Vector2 ship = ShipCenter();
		Vector2 shipVel = HeadingVector() * m_boatSpeed;
		float windup = Mathf.Max(0.8f, 1.25f - 0.1f * SeaStars);
		switch (f.Phase)
		{
			case 0:
			{
				// Circling the ship, a dark shape and a wake.
				f.Y = Mathf.MoveTowards(f.Y, -2.8f, dt * 2f);
				float angle = Angle2(f.P - ship) + f.Side * 40f * dt;
				Vector2 want = ship + Dir2(angle) * 20f + shipVel * 0.5f;
				Vector2 to = want - f.P;
				float speed = m_boatSpeed + 10f;
				f.Facing = Mathf.MoveTowardsAngle(f.Facing, Angle2(to), 180f * dt);
				f.P += to.normalized * Mathf.Min(speed * dt, to.magnitude);
				if (Random.value < dt * 14f)
				{
					AddFoam(f.P - Dir2(f.Facing) * 3f, Random.Range(1f, 1.8f), 1.5f, 0.5f, 2f);
				}
				f.Wait -= dt;
				if (f.Wait <= 0f)
				{
					float hurt = 1f - (float)f.Hp / f.MaxHp;
					float r = Random.value;
					f.Move = hurt > 0.4f && r < 0.25f ? "under" : r < 0.6f ? "lunge" : "slam";
					f.T = 0f;
					if (f.Move == "under")
					{
						f.Phase = 5;
						m_audio.Play("sfx_serpent_idle|sfx_serpent_alerted", 0.8f, 0.7f);
					}
					else
					{
						// Up at a spot ahead of the ship, where it can be seen.
						f.Phase = 1;
						Vector2 up = ship + new Vector2(f.Side * Random.Range(10f, 16f), Random.Range(10f, 18f));
						f.P = Vector2.Lerp(f.P, up, 0.7f);
						f.Aim = ship + shipVel * (windup + 0.3f);
						f.Facing = Angle2(f.Aim - f.P);
						SpawnSplash(f.P, 2f);
						m_audio.Play("sfx_serpent_taunt|sfx_serpent_alerted", 1f, 0.8f);
						TriggerWatched(f.Anim, f.Move == "slam" ? "taunt" : f.Attack?.Animation ?? "attack");
					}
				}
				break;
			}
			case 1:
			{
				// Risen, rearing up: a lunge streak, or a slam that will raise a wave.
				f.Y = Mathf.MoveTowards(f.Y, -0.2f, dt * 6f);
				f.P += shipVel * dt;
				if (f.Move == "lunge")
				{
					if (f.T < windup * 0.7f)
					{
						f.Aim = ship + shipVel * (windup - f.T + 0.3f);
						f.Facing = Mathf.MoveTowardsAngle(f.Facing, Angle2(f.Aim - f.P), 200f * dt);
					}
					Vector2 dir = Dir2(f.Facing);
					float reach = Vector2.Distance(f.P, f.Aim) + 8f;
					SerpentMarker(f, f.P, f.P + dir * reach, 4.4f);
					f.Markers[0].SetActive(f.T < windup - 0.35f || Mathf.FloorToInt(f.T * 12f) % 2 == 0);
					if (f.T >= windup)
					{
						f.Phase = 2;
						f.T = 0f;
						f.Dir = dir * (reach / 0.45f);
						f.Struck = false;
						AttackSound(f.Attack, false, 1f);
						m_audio.Play("sfx_serpent_attack|sfx_serpent_attack_trigger", 1f, 0.8f);
					}
				}
				else if (f.T >= windup * 1.2f)
				{
					// The slam: a wave rolls out from it toward the ship.
					Vector2 dir = (ship - f.P).normalized;
					SpawnRoller(dir, 2.6f + 0.3f * SeaStars, 0f, true, f.P + dir * 3f);
					SpawnSplash(f.P + dir * 4f, 2.4f);
					m_audio.Play("sfx_serpent_attack_hit|sfx_ship_waterimpact", 1f, 0.7f);
					m_shake = Mathf.Max(m_shake, 0.35f);
					f.Phase = 3;
					f.T = 0f;
				}
				break;
			}
			case 2:
			{
				f.P += f.Dir * dt;
				Vector2 head = f.P + Dir2(f.Facing) * (f.Length * 0.35f);
				if (!f.Struck && ShipDistance(head) < 1.2f)
				{
					f.Struck = true;
					int result = SeaHit(f, true, $"The {f.Label} of the deep struck the ship!");
					m_audio.Play("sfx_serpent_attack_hit", 1f, 0.8f);
					if (result == 2)
					{
						f.Phase = 3;
						f.T = -2f;
						f.Dir = Vector2.zero;
						HideMarkers(f);
						break;
					}
				}
				if (f.T >= 0.45f)
				{
					f.Phase = 3;
					f.T = 0f;
					f.Dir = Vector2.zero;
					HideMarkers(f);
				}
				break;
			}
			case 3:
				// Up on the surface, open to the ballista.
				f.P += shipVel * dt * 0.7f;
				if (f.T > 2.3f)
				{
					f.Phase = 0;
					f.T = 0f;
					f.Side = Random.value < 0.5f ? -1f : 1f;
					f.Wait = Random.Range(2.5f, 4f) / (1f + 0.1f * SeaStars);
					SpawnSplash(f.P, 1.6f);
				}
				break;
			case 5:
			{
				// Under the keel: a red ring follows the ship, stops, and it bursts up there.
				f.Y = Mathf.MoveTowards(f.Y, -4f, dt * 3f);
				float follow = 2.2f;
				if (f.T < follow)
				{
					f.Aim = ship + shipVel * 0.3f;
				}
				f.P = Vector2.MoveTowards(f.P, f.Aim, (m_boatSpeed + 14f) * dt);
				GameObject ring = f.Markers.Count > 1 && f.Markers[1] != null ? f.Markers[1] : WaterMarker(true);
				if (f.Markers.Count < 2)
				{
					if (f.Markers.Count == 0)
					{
						f.Markers.Add(WaterMarker(false));
						f.Markers[0].SetActive(false);
					}
					f.Markers.Add(ring);
				}
				PlaceWaterMarker(ring, f.Aim, 9f, 9f, 0f);
				ring.SetActive(f.T < follow || Mathf.FloorToInt(f.T * 12f) % 2 == 0);
				if (f.T >= follow + 0.7f)
				{
					ring.SetActive(false);
					f.P = f.Aim;
					f.Y = -0.2f;
					f.Facing = Random.Range(0f, 360f);
					SpawnSplash(f.Aim, 3f);
					m_audio.Play("sfx_serpent_attack_hit|sfx_ship_impact", 1f, 0.7f);
					m_shake = Mathf.Max(m_shake, 0.5f);
					if (ShipDistance(f.Aim) < 3.5f)
					{
						int result = SeaHit(f, true, $"The {f.Label} of the deep burst up under the ship!");
						m_lurchV += 160f * (Random.value < 0.5f ? -1f : 1f);
						if (result == 2)
						{
							f.T = -2f;
						}
					}
					f.Phase = 3;
					f.T = Mathf.Min(f.T, 0f);
				}
				break;
			}
		}
	}

	// ---------------------------------------------------------------- flyers

	// Drakes hold station ahead and spit at where the ship will be (a red ring); deathsquitos
	// hover, then dart along a red streak. Either can be blocked or parried.
	private void UpdateFlyer(SeaFoe f, float dt)
	{
		Vector2 ship = ShipCenter();
		Vector2 shipVel = HeadingVector() * m_boatSpeed;
		if (f.Dead)
		{
			f.Y -= dt * 9f;
			if (f.Y < -1f && !f.Struck)
			{
				f.Struck = true;
				SpawnSplash(f.P, 0.7f);
			}
			return;
		}
		bool drake = f.Kind == "drake";
		switch (f.Phase)
		{
			case 0:
			{
				// In to its station, keeping pace.
				Vector2 station = ship + new Vector2(f.Side * (drake ? 9f : 6f), drake ? 20f : 16f);
				Vector2 to = station - f.P;
				f.P += shipVel * dt + to * Mathf.Min(1f, dt * 1.6f);
				f.Facing = Mathf.MoveTowardsAngle(f.Facing, Angle2(ship - f.P), 200f * dt);
				f.Wait -= dt;
				if (f.Wait <= 0f && to.magnitude < 4f)
				{
					f.Phase = 1;
					f.T = 0f;
					TriggerWatched(f.Anim, f.Attack?.Animation ?? "attack");
					AttackSound(f.Attack, false, 0.8f);
				}
				break;
			}
			case 1:
			{
				f.P += shipVel * dt;
				f.Facing = Mathf.MoveTowardsAngle(f.Facing, Angle2(ship - f.P), 200f * dt);
				if (drake)
				{
					float wind = 0.55f;
					if (f.T >= wind)
					{
						// The spit flies at where the ship is going.
						Vector2 to = ship + shipVel * 1.05f;
						SeaShot s = new SeaShot
						{
							From = f.P,
							To = to,
							FromY = f.Y,
							Flight = 1.05f,
							Owner = f,
							Radius = 2.6f,
							Why = $"The {f.Label}'s spit hit the ship!",
						};
						s.Go = f.Attack != null ? MakeShotVisual(f.Attack) : new GameObject("Spit");
						s.Go.transform.SetParent(m_stage, false);
						s.Marker = WaterMarker(true);
						PlaceWaterMarker(s.Marker, to, s.Radius * 2f, s.Radius * 2f, 0f);
						m_seaShots.Add(s);
						f.Attacks++;
						f.Phase = f.Attacks >= 2 ? 9 : 0;
						f.Wait = 2.2f;
						f.T = 0f;
					}
				}
				else
				{
					// A squito lines up, then darts through where the ship is.
					if (f.T < 0.8f)
					{
						f.Aim = ship + shipVel * 0.45f;
					}
					Vector2 dir = (f.Aim - f.P).normalized;
					GameObject m = f.Markers.Count > 0 ? f.Markers[0] : WaterMarker(false);
					if (f.Markers.Count == 0)
					{
						f.Markers.Add(m);
					}
					float reach = Vector2.Distance(f.P, f.Aim) + 6f;
					PlaceWaterMarker(m, f.P + dir * (reach * 0.5f), 1.8f, reach, Angle2(dir));
					m.SetActive(f.T < 0.8f || Mathf.FloorToInt(f.T * 12f) % 2 == 0);
					if (f.T >= 1.1f)
					{
						f.Phase = 2;
						f.T = 0f;
						f.Dir = dir * (reach / 0.35f);
						f.Struck = false;
						m_audio.Play("sfx_deathsquito_attack|sfx_deathsquito_idle", 0.9f, 1f);
					}
				}
				break;
			}
			case 2:
				f.P += f.Dir * dt;
				f.Y = Mathf.MoveTowards(f.Y, 1.6f, dt * 8f);
				if (!f.Struck && ShipDistance(f.P) < 0.6f)
				{
					f.Struck = true;
					SeaHit(f, false, $"A {f.Label} stung you!");
				}
				if (f.T >= 0.35f)
				{
					HideMarkers(f);
					f.Phase = 9;
					f.T = 0f;
				}
				break;
			case 9:
				// Away it goes.
				f.P += new Vector2(f.Side * 14f, 6f) * dt + shipVel * dt;
				f.Y += dt * 2f;
				if (f.T > 4f)
				{
					f.Dead = true;
					f.Struck = true;
					f.Y = -10f;
				}
				break;
		}
	}

	private void UpdateSeaShots(float dt)
	{
		for (int i = m_seaShots.Count - 1; i >= 0; i--)
		{
			SeaShot s = m_seaShots[i];
			s.T += dt;
			float p = Mathf.Clamp01(s.T / s.Flight);
			Vector2 at = Vector2.Lerp(s.From, s.To, p);
			float y = Mathf.Lerp(SeaY + s.FromY, SeaHeight(s.To.x, s.To.y), p) + Mathf.Sin(p * Mathf.PI) * 3f;
			if (s.Go != null)
			{
				s.Go.transform.localPosition = new Vector3(at.x, y, at.y);
			}
			if (s.Marker != null)
			{
				s.Marker.SetActive(p < 0.7f || Mathf.FloorToInt(s.T * 12f) % 2 == 0);
				PlaceWaterMarker(s.Marker, s.To, s.Radius * 2f, s.Radius * 2f, 0f);
			}
			if (p < 1f)
			{
				continue;
			}
			if (s.Owner?.Attack != null)
			{
				foreach (GameObject fx in s.Owner.Attack.Impact)
				{
					PlayFx(fx, new Vector3(s.To.x, SeaHeight(s.To.x, s.To.y), s.To.y), Quaternion.identity, 3f);
					break;
				}
				AttackSound(s.Owner.Attack, true, 0.9f);
			}
			AddFoam(s.To, 2f, 1f, 0.7f, 1.5f);
			if (m_state == State.Running && ShipDistance(s.To) < s.Radius)
			{
				SeaHit(s.Owner, false, s.Why);
			}
			if (s.Go != null)
			{
				Destroy(s.Go);
			}
			if (s.Marker != null)
			{
				Destroy(s.Marker);
			}
			m_seaShots.RemoveAt(i);
		}
	}

	// ---------------------------------------------------------------- leviathans

	// A living island drifting across the way. Ram it and it's like a reef; shoot it and it
	// shakes, then dives (as in the game) and the way is clear.
	private void UpdateLeviathan(SeaFoe f, float dt)
	{
		f.P += f.Dir * dt;
		if (f.Dead)
		{
			f.Y -= dt * 1.4f;
			return;
		}
		if (!f.Struck && ShipDistance(f.P) < f.Radius)
		{
			f.Struck = true;
			Ram(f.P, "You rammed a leviathan!");
		}
	}

	// ---------------------------------------------------------------- lightning

	private void StartSeaStrike()
	{
		// Where the ship will be, give or take.
		Vector2 at = ShipCenter() + HeadingVector() * (m_boatSpeed * 1.3f) + Random.insideUnitCircle * 5f;
		SeaStrike s = new SeaStrike { At = at, Warn = Mathf.Max(1f, 1.4f - 0.1f * SeaStars), Marker = WaterMarker(true) };
		PlaceWaterMarker(s.Marker, at, 7f, 7f, 0f);
		m_seaStrikes.Add(s);
		m_audio.Play("sfx_thunder|sfx_distant thunder", 0.25f, 1.4f);
	}

	private void UpdateSeaStrikes(float dt)
	{
		for (int i = m_seaStrikes.Count - 1; i >= 0; i--)
		{
			SeaStrike s = m_seaStrikes[i];
			s.Warn -= dt;
			if (s.Marker != null)
			{
				PlaceWaterMarker(s.Marker, s.At, 7f, 7f, 0f);
				s.Marker.SetActive(s.Warn > 0.5f || Mathf.FloorToInt(s.Warn * 12f) % 2 == 0);
			}
			if (s.Warn > 0f)
			{
				continue;
			}
			Vector3 at = new Vector3(s.At.x, SeaHeight(s.At.x, s.At.y), s.At.y);
			StormStrike(s_seaStorm, at);
			AddFoam(s.At, 3f, 1f, 0.8f, 1.5f);
			m_audio.Play(Sfx.Thunder, 1f, 1f);
			if (m_state == State.Running && ShipDistance(s.At) < 3.2f)
			{
				Hit(new Thing { Label = "", JumpOrSlide = true }, "Lightning struck the ship! (Steer clear of the red rings.)");
			}
			if (s.Marker != null)
			{
				Destroy(s.Marker);
			}
			m_seaStrikes.RemoveAt(i);
		}
	}

	// ---------------------------------------------------------------- the ballista

	private GameObject MakeBallistaBolt()
	{
		if (m_boltTemplate == null)
		{
			GameObject? prefab = Pictures.Item("TurretBoltWood") ?? Pictures.Item("TurretBolt") ?? Pictures.Item("BoltIron") ?? Pictures.Item("ArrowWood");
			if (prefab != null)
			{
				m_boltTemplate = Picture(prefab);
				// Find its long side once, to fly it point first.
				GameObject probe = Instantiate(m_boltTemplate, m_stage, false);
				if (Pictures.MeshBounds(probe, out Bounds b))
				{
					if (b.size.x >= b.size.y && b.size.x >= b.size.z)
					{
						m_boltTurn = Quaternion.Euler(0f, 90f, 0f);
					}
					else if (b.size.y >= b.size.x && b.size.y >= b.size.z)
					{
						m_boltTurn = Quaternion.Euler(90f, 0f, 0f);
					}
				}
				Destroy(probe);
			}
		}
		GameObject wrapper = new GameObject("Bolt") { layer = m_layer };
		wrapper.transform.SetParent(m_stage, false);
		if (m_boltTemplate != null)
		{
			GameObject bolt = Instantiate(m_boltTemplate, wrapper.transform, false);
			bolt.transform.localRotation = m_boltTurn;
			bolt.transform.localPosition = Vector3.zero;
			if (Pictures.MeshBounds(bolt, out Bounds b))
			{
				float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
				bolt.transform.localScale = Vector3.one * Mathf.Clamp(1.8f / Mathf.Max(0.05f, longest), 0.2f, 6f);
			}
		}
		else
		{
			GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
			Destroy(rod.GetComponent<Collider>());
			rod.transform.SetParent(wrapper.transform, false);
			rod.transform.localScale = new Vector3(0.1f, 0.9f, 0.1f);
			rod.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
		}
		Pictures.SetLayer(wrapper, m_layer);
		// A streak behind it, to follow it by.
		TrailRenderer trail = wrapper.AddComponent<TrailRenderer>();
		trail.time = 0.16f;
		trail.startWidth = 0.22f;
		trail.endWidth = 0f;
		trail.sharedMaterial = BoltMaterial(false);
		trail.startColor = new Color(1f, 0.96f, 0.85f, 0.6f);
		trail.endColor = new Color(1f, 1f, 1f, 0f);
		trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		return wrapper;
	}

	private void FireBallista()
	{
		m_reload = BallistaReload;
		Vector2 f = HeadingVector();
		Vector2 bow = ShipCenter() + f * (m_shipLength * 0.5f);
		GameObject go = MakeBallistaBolt();
		float y = m_boatY + 1.6f;
		go.transform.localPosition = new Vector3(bow.x, y, bow.y);
		go.transform.localRotation = Quaternion.LookRotation(new Vector3(f.x, 0f, f.y));
		m_ballistaBolts.Add(new BallistaBolt { Go = go, P = bow, V = f * (BoltSpeed + m_boatSpeed), Y = y, Life = 1.1f });
		GameObject? fx = FindFx("fx_turret_fire");
		if (fx != null)
		{
			PlayFx(fx, new Vector3(bow.x, y, bow.y), Quaternion.LookRotation(new Vector3(f.x, 0f, f.y)), 2f);
		}
		m_audio.Play("fx_turret_fire|sfx_bow_fire|sfx_crossbow_fire", 0.8f, Random.Range(0.95f, 1.05f));
		m_lurchV += Random.Range(-4f, 4f);
	}

	private void UpdateBallistaBolts(float dt)
	{
		for (int i = m_ballistaBolts.Count - 1; i >= 0; i--)
		{
			BallistaBolt b = m_ballistaBolts[i];
			Vector2 from = b.P;
			b.P += b.V * dt;
			b.Life -= dt;
			b.Y -= dt * 1.2f;
			if (b.Go != null)
			{
				b.Go.transform.localPosition = new Vector3(b.P.x, b.Y, b.P.y);
			}
			SeaFoe? hit = null;
			foreach (SeaFoe f in m_seaFoes)
			{
				if (f.Dead || f.Kind == "gull" || f.Kind == "loot" || f.Kind == "wood")
				{
					continue;
				}
				bool open = f.Kind == "rock" || f.Kind == "leviathan" || f.Kind == "drake" || f.Kind == "squito" || Surfaced(f);
				if (!open)
				{
					continue;
				}
				// Against its body's length for the long ones.
				Vector2 axis = Dir2(f.Facing) * ((f.Kind == "serpent" || f.Kind == "boss") ? f.Length * 0.4f : 0f);
				if (SegmentGap(from, b.P, f.P - axis, f.P + axis) < f.Radius + 0.4f)
				{
					hit = f;
					break;
				}
			}
			if (hit != null)
			{
				BoltHits(hit, b.P);
			}
			if (hit != null || b.Life <= 0f)
			{
				if (hit == null)
				{
					AddFoam(b.P, 0.7f, 0.7f, 0.6f, 1.2f);
				}
				if (b.Go != null)
				{
					Destroy(b.Go);
				}
				m_ballistaBolts.RemoveAt(i);
			}
		}
	}

	// The shortest distance between two flat segments.
	private static float SegmentGap(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
	{
		float best = float.MaxValue;
		for (int k = 0; k <= 6; k++)
		{
			Vector2 p = Vector2.Lerp(a0, a1, k / 6f);
			Vector2 d = b1 - b0;
			float t = d.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector2.Dot(p - b0, d) / d.sqrMagnitude) : 0f;
			best = Mathf.Min(best, Vector2.Distance(p, b0 + d * t));
		}
		return best;
	}

	private void BoltHits(SeaFoe f, Vector2 at)
	{
		Vector3 at3 = new Vector3(at.x, SeaHeight(at.x, at.y) + 1f, at.y);
		if (f.Kind == "rock")
		{
			m_audio.Play(Sfx.Miss, 0.7f, 0.9f);
			GameObject? chips = FindFx("vfx_RockHit|vfx_rock_hit|vfx_Place_stone");
			if (chips != null)
			{
				PlayFx(chips, at3, Quaternion.identity, 2f);
			}
			return;
		}
		f.Hp--;
		m_audio.Play(Sfx.HitMonster, 1f, f.Boss ? 0.75f : 1f);
		if (f.Kind == "leviathan")
		{
			// It shakes, and dives once it's had enough (as in the game).
			Leviathan? lev = Pictures.Prefab("Leviathan")?.GetComponent<Leviathan>();
			if (f.Anim != null)
			{
				SetTrigger(f.Anim, f.Hp <= 0 ? "dive" : "shake");
			}
			EffectList? fx = f.Hp <= 0 ? lev?.m_leaveEffects : lev?.m_reactionEffects;
			m_audio.PlayEffects(fx, 1f);
			PlayEffectParticles(fx, new Vector3(f.P.x, SeaY, f.P.y));
			if (f.Hp <= 0)
			{
				f.Dead = true;
				f.T = 0f;
				f.Struck = true;
				m_coins += 5;
			}
			return;
		}
		GameObject? prefab = f.Kind == "boss" || f.Kind == "serpent" ? Pictures.Prefab("Serpent") : f.Kind == "drake" ? Pictures.Prefab("Hatchling") : Pictures.Prefab("Deathsquito");
		Character? c = prefab != null ? prefab.GetComponent<Character>() : null;
		if (f.Hp > 0)
		{
			if (c != null)
			{
				m_audio.PlayEffects(c.m_hitEffects, 0.9f);
				PlayEffectParticles(c.m_hitEffects, at3);
			}
			if (f.Anim != null && !f.Boss)
			{
				SetTrigger(f.Anim, "stagger");
			}
			if (f.Boss)
			{
				m_seaBossFlash = 1f;
				m_iconPulse = 1f;
			}
			return;
		}
		// Down it goes.
		f.Dead = true;
		f.T = 0f;
		f.Struck = false;
		HideMarkers(f);
		if (c != null)
		{
			m_audio.PlayEffects(c.m_deathEffects, 1f);
			PlayEffectParticles(c.m_deathEffects, at3);
		}
		m_coins += f.Boss ? 40 : f.Kind == "serpent" ? 10 : 4;
		SpawnSplash(f.P, f.Boss ? 3f : 1.2f);
		if (f.Boss)
		{
			m_bonusDistance += Plugin.BiomeLength.Value * 0.3f;
			m_audio.Play(Sfx.Biome, 1f, 1.1f);
			m_shake = Mathf.Max(m_shake, 0.5f);
			Flash(new Color(1f, 0.95f, 0.75f), 0.5f);
			Plugin.Log.LogInfo($"The serpent of the deep is beaten at {m_z:0} m.");
			SeaBossDone();
		}
	}

	// The particles of an effect list (sounds come separately), minus ragdolls.
	private void PlayEffectParticles(EffectList? effects, Vector3 at)
	{
		if (effects?.m_effectPrefabs == null)
		{
			return;
		}
		foreach (EffectList.EffectData e in effects.m_effectPrefabs)
		{
			if (e?.m_prefab == null || !e.m_enabled || e.m_prefab.GetComponentInChildren<Ragdoll>(true) != null
				|| e.m_prefab.GetComponentInChildren<ParticleSystem>(true) == null)
			{
				continue;
			}
			PlayFx(e.m_prefab, at, Quaternion.identity, 4f);
		}
	}

	// ---------------------------------------------------------------- marks on the water

	// A red streak (or ring) lying on the water: leave it, as on land.
	private GameObject WaterMarker(bool ring)
	{
		if (m_markerMaterial == null)
		{
			MakeMarkerMaterials();
		}
		if (ring && m_ringMaterial == null)
		{
			m_ringMaterial = new Material(Shader.Find("Sprites/Default")) { name = "SurfRing", mainTexture = Ring(), color = new Color(1f, 0.2f, 0.05f, 0.9f), renderQueue = 3110 };
		}
		GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
		Destroy(quad.GetComponent<Collider>());
		quad.name = ring ? "SeaRing" : "SeaStreak";
		quad.layer = m_layer;
		quad.transform.SetParent(m_stage, false);
		Renderer r = quad.GetComponent<Renderer>();
		r.sharedMaterial = ring ? m_ringMaterial : m_markerMaterial;
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		r.receiveShadows = false;
		return quad;
	}

	private void PlaceWaterMarker(GameObject? m, Vector2 centre, float width, float length, float angle)
	{
		if (m == null)
		{
			return;
		}
		m.transform.localPosition = new Vector3(centre.x, SeaHeight(centre.x, centre.y) + 0.25f, centre.y);
		m.transform.localRotation = Quaternion.Euler(90f, angle, 0f);
		m.transform.localScale = new Vector3(width, Mathf.Max(0.5f, length), 1f);
	}
}
