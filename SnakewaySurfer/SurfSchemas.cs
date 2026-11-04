using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// The creature schemas Sam picked (Schemas.cs): formations, lane markers, sweeps, gas lanes,
// leaps, crossings, chasers from behind, lobbed rocks and patches, helpers, and star looks.
internal sealed partial class SurfGame
{
	private sealed class Lob
	{
		public GameObject Go = null!;
		public Vector3 From;
		public Vector3 To;
		public float Time;
		public float Duration = 0.9f;
		public string Kind = "";
		public int[] Lanes = new int[0];
		public float Z;
		public string Label = "";
		public CreatureAttack? Attack;
		public readonly List<GameObject> Markers = new();
	}

	private readonly List<Lob> m_lobs = new();
	private Material? m_markerMaterial;
	private Material? m_gasMaterial;
	private Material? m_tarMaterial;
	private float m_slowTime;

	// ---------------------------------------------------------------- formations

	// Lays out a whole row for creatures that come in a formation; false = spawn it as usual.
	private bool SpawnFormation(SpawnEntry e, int lane, float z, int biomeIndex, int slot)
	{
		Schema? s = Schemas.For(e.Prefab);
		if (s == null)
		{
			return false;
		}
		if (s.Has("helper"))
		{
			Thing h = SpawnCreature(e, lane, z, Kind.Block, 0f, biomeIndex, "still");
			int side = Random.value < 0.5f ? -1 : 1;
			h.X = side * (TrackHalf + 2.5f);
			h.FreeX = true;
			h.Collidable = false;
			h.Helper = true;
			h.Attacked = true;
			h.Go.transform.localPosition = new Vector3(h.X, RoadY(z), z);
			// Facing the track.
			h.Go.transform.localRotation = Quaternion.Euler(0f, side > 0 ? 60f : -60f, 0f);
			return true;
		}
		if (s.Has("chaser"))
		{
			Thing c = SpawnCreature(e, lane, z, Kind.Block, 0f, biomeIndex, "still");
			MakeChaser(c, false);
			return true;
		}
		if (slot > 0)
		{
			// Wide formations only as the first thing in a row, so a way through stays open.
			return false;
		}
		bool herd = s.Has("herd") && Random.value < 0.7f;
		if (herd || s.Has("crosser"))
		{
			int count = herd ? Random.Range(3, 6) : 1;
			int side = Random.value < 0.5f ? -1 : 1;
			for (int k = 0; k < count; k++)
			{
				Thing c = SpawnCreature(e, 1, z + k * 3.4f, Kind.Block, 0f, biomeIndex, "still");
				c.Crosser = true;
				c.Side = side;
				c.FreeX = true;
				c.X = side * (TrackHalf + 4f + k * 0.7f);
				c.Go.transform.localPosition = new Vector3(c.X, RoadY(c.Z), c.Z);
				// The picture faces -z; turn it to face across the track.
				c.Go.transform.localRotation = Quaternion.Euler(0f, side > 0 ? 90f : -90f, 0f);
			}
			return true;
		}
		if (s.Has("line") && Random.value < 0.25f)
		{
			int other = lane == 0 ? 1 : lane == 2 ? 1 : (Random.value < 0.5f ? 0 : 2);
			SpawnCreature(e, lane, z, Kind.Block, 0f, biomeIndex, "charger");
			SpawnCreature(e, other, z, Kind.Block, 0f, biomeIndex, "charger");
			return true;
		}
		if (s.Has("swarm"))
		{
			int count = Mathf.Clamp(Random.Range(Mathf.Max(2, e.GroupMin), Mathf.Max(2, e.GroupMax) + 1), 2, 4);
			int at = lane;
			for (int k = 0; k < count; k++)
			{
				if (k > 0 && Random.value < 0.6f)
				{
					at = Mathf.Clamp(at + (Random.value < 0.5f ? -1 : 1), 0, 2);
				}
				float zk = z + k * 4.5f;
				if (s.Has("headleap") && Random.value < 0.35f)
				{
					// Leaps at you at head height: slide under.
					Thing hi = SpawnCreature(e, at, zk, Kind.High, 1.35f, biomeIndex, "charger");
					hi.ClearHeight = 0f;
					continue;
				}
				Thing t = SpawnCreature(e, at, zk, Kind.Block, 0f, biomeIndex);
				if (s.Has("leaper") && Random.value < 0.35f)
				{
					MakeLeaper(t);
				}
			}
			return true;
		}
		if (s.Has("leaper"))
		{
			MakeLeaper(SpawnCreature(e, lane, z, Kind.Block, 0f, biomeIndex, "still"));
			return true;
		}
		return false;
	}

	private void MakeLeaper(Thing t)
	{
		t.Leaper = true;
		t.MoveSpeed = 0f;
		t.Speed = 0f;
		t.Switcher = false;
		t.FreeX = true;
		t.X = LaneX(t.Lane);
	}

	private void MakeChaser(Thing t, bool high)
	{
		t.Chaser = true;
		t.ChaseHigh = high;
		t.Collidable = false;
		t.Swoop = false;
		t.FreeX = true;
		t.MoveSpeed = 0f;
		t.Speed = 0f;
		t.Phase = 0;
		t.BaseY = high ? 1.35f : 0f;
		t.Go.SetActive(false);
	}

	// Flyers: deathsquitos sometimes dive in from behind; drakes and gjalls hover and attack from
	// above instead of being something to slide under.
	private void FlyerSchema(Thing t)
	{
		Schema? s = t.Schema;
		if (s == null)
		{
			return;
		}
		if (s.Has("behind") && Random.value < 0.4f)
		{
			MakeChaser(t, true);
			return;
		}
		if (s.Has("flyshooter") || s.Has("bomb") || s.Has("tickdrop"))
		{
			t.Collidable = false;
			t.Swoop = false;
			t.BaseY = s.Has("bomb") || s.Has("tickdrop") ? 8f : 3.6f;
			t.Go.transform.localPosition = new Vector3(LaneX(t.Lane), RoadY(t.Z) + t.BaseY, t.Z);
		}
	}

	// ---------------------------------------------------------------- per frame

	// Returns true when it fully handled this creature this frame (UpdateCreature is skipped).
	private bool UpdateSchema(Thing t, float dt)
	{
		if (t.Info == null)
		{
			return false;
		}
		float distance = t.Z - m_z;
		if (t.Chaser)
		{
			UpdateChaser(t, dt);
			return true;
		}
		if (t.Helper)
		{
			UpdateHelper(t, distance);
			return true;
		}
		if (t.Crosser)
		{
			UpdateCrosser(t, dt, distance);
			return true;
		}
		if (t.Leaper && t.Phase < 2)
		{
			if (UpdateLeaper(t, dt, distance))
			{
				return true;
			}
		}
		if (t.Switcher)
		{
			UpdateSwitcher(t, dt, distance);
		}
		if (distance < -2f)
		{
			ClearMarkers(t);
		}
		// Lanes about to be hit are marked well before the blow.
		if (t.Move.Length > 0 && !t.Attacked && t.HitLanes.Length == 0 && t.Attack != null)
		{
			float windup = Mathf.Clamp(0.2f + 0.12f * t.Height, 0.3f, 0.8f);
			float mark = t.Smashes > 0 ? (m_speed + t.Speed) * (windup + 2.0f) + 6f : (m_speed + t.Speed) * (windup + 1.0f) + 4f;
			if (distance < mark && distance > 0f)
			{
				t.HitLanes = ChooseLanes(t);
				foreach (int lane in t.HitLanes)
				{
					t.Markers.Add(MakeMarker(lane, t.Z - 2f, 3.6f, m_markerMaterial));
				}
			}
		}
		if (t.Markers.Count > 0 && t.Move != "slams")
		{
			for (int i = 0; i < t.Markers.Count && i < t.HitLanes.Length; i++)
			{
				PlaceMarker(t.Markers[i], t.HitLanes[i], t.Z - 2f);
			}
		}
		if (t.Ranged.Length > 0 && !t.Attacked && t.Alerted && distance < m_speed * 2.2f + 16f && distance > m_speed * 1.2f + 8f)
		{
			t.Attacked = true;
			TriggerAttack(t);
			AreaShot(t);
		}
		return false;
	}

	private int[] ChooseLanes(Thing t)
	{
		if (t.Move == "gas3")
		{
			return new[] { 0, 1, 2 };
		}
		if (t.Move == "slams")
		{
			// Two lanes slammed, one left safe, then a shockwave over all of them.
			int safe = Random.Range(0, 3);
			List<int> hit = new();
			for (int l = 0; l < 3; l++)
			{
				if (l != safe)
				{
					hit.Add(l);
				}
			}
			if (Random.value < 0.5f)
			{
				hit.Reverse();
			}
			return hit.ToArray();
		}
		int own = Mathf.Clamp(t.Lane, 0, 2);
		int other;
		if (m_lane != own && Mathf.Abs(m_lane - own) == 1)
		{
			other = m_lane;
		}
		else
		{
			other = own == 0 ? 1 : own == 2 ? 1 : (Random.value < 0.5f ? 0 : 2);
		}
		return new[] { own, other };
	}

	// A sweep or gas cloud lands on its marked lanes.
	private void MoveLands(Thing t)
	{
		ClearMarkers(t);
		if (t.HitLanes.Length == 0)
		{
			t.HitLanes = ChooseLanes(t);
		}
		bool gas = t.Move.StartsWith("gas") || (t.Move == "sweep" && t.Schema != null && t.Schema.Has("gas2"));
		string biome = m_biomes[BiomeAt(t.Z)].Def.Name;
		foreach (int lane in t.HitLanes)
		{
			GameObject zone = gas ? MakeCloud(lane, t.Z - 1.5f) : new GameObject("Sweep");
			zone.transform.SetParent(m_stage, false);
			if (!gas)
			{
				zone.transform.localPosition = new Vector3(LaneX(lane), RoadY(t.Z - 1.5f), t.Z - 1.5f);
			}
			m_things.Add(new Thing
			{
				Go = zone,
				Kind = Kind.Block,
				Lane = lane,
				Z = t.Z - 1.5f,
				HalfLength = 1.6f,
				Label = t.Label,
				JumpOrSlide = t.Move == "gas3",
				CrashText = gas ? $"You choked in a {t.Label}'s cloud in the {biome}!" : $"A {t.Label}'s sweep caught you in the {biome}!",
				Life = gas ? 2.5f : 0.6f,
			});
		}
		if (gas && t.Move == "sweep")
		{
			// The vile bear's swipe leaves its poison behind.
			return;
		}
	}

	// One blow of the berserk smash: its marked lane is struck and stays broken for a moment.
	private void LaneSlam(Thing t)
	{
		if (t.HitLanes.Length == 0)
		{
			t.HitLanes = ChooseLanes(t);
		}
		int index = Mathf.Clamp(3 - t.Smashes, 0, t.HitLanes.Length - 1);
		int lane = t.HitLanes[index];
		if (index == 0)
		{
			// The moving markers become fixed ones that last as long as the broken ground.
			ClearMarkers(t);
			foreach (int l in t.HitLanes)
			{
				GameObject m = MakeMarker(l, t.Z - 2f, 3.6f, m_markerMaterial);
				m_things.Add(new Thing { Go = m, Kind = Kind.Scenery, Z = t.Z - 2f, HalfLength = 1.8f, Life = 2.6f });
			}
		}
		m_shake = Mathf.Max(m_shake, Mathf.Clamp01(1f - Mathf.Abs(t.Z - m_z) / 30f) * 0.3f);
		GameObject zone = new GameObject("Slam");
		zone.transform.SetParent(m_stage, false);
		zone.transform.localPosition = new Vector3(LaneX(lane), RoadY(t.Z - 2f), t.Z - 2f);
		m_things.Add(new Thing
		{
			Go = zone,
			Kind = Kind.Block,
			Lane = lane,
			Z = t.Z - 2f,
			HalfLength = 1.8f,
			Label = t.Label,
			CrashText = $"A {t.Label}'s smash flattened you in the {m_biomes[BiomeAt(t.Z)].Def.Name}!",
			Life = 2.4f,
		});
	}

	// Lobs, fire, tar and bombs: thrown to land just ahead of you on marked lanes.
	private void AreaShot(Thing t)
	{
		int[] lanes;
		int you = m_lane;
		int near = you == 0 ? 1 : you == 2 ? 1 : (Random.value < 0.5f ? 0 : 2);
		switch (t.Ranged)
		{
			case "fire":
				lanes = Random.value < 0.5f ? new[] { 0, 1, 2 } : new[] { you, near };
				break;
			case "bomb":
				lanes = Random.value < 0.5f ? new[] { you } : new[] { you, near };
				break;
			default:
				lanes = new[] { you, near };
				break;
		}
		float z = m_z + m_speed * 1.3f + 4f;
		if (z > t.Z - 2f)
		{
			z = t.Z - 2f;
		}
		Lob lob = new Lob
		{
			Kind = t.Ranged,
			Lanes = lanes,
			Z = z,
			Label = t.Label,
			Attack = t.Attack,
			From = t.Go.transform.localPosition + Vector3.up * Mathf.Max(1.5f, t.Height * 0.8f),
			To = new Vector3((LaneX(lanes[0]) + LaneX(lanes[lanes.Length - 1])) * 0.5f, RoadY(z) + 0.3f, z),
		};
		foreach (int lane in lanes)
		{
			lob.Markers.Add(MakeMarker(lane, z, 2.4f, t.Ranged == "tar" ? m_tarMaterial : m_markerMaterial));
		}
		GameObject? visual = null;
		if (t.Attack?.Projectile != null)
		{
			visual = Instantiate(Picture(t.Attack.Projectile), m_stage, false);
		}
		if ((visual == null || visual.GetComponentsInChildren<Renderer>().Length == 0) && Pictures.Prefab("Stone") is GameObject stone)
		{
			if (visual != null)
			{
				Destroy(visual);
			}
			visual = Instantiate(Picture(stone), m_stage, false);
			visual.transform.localScale = Vector3.one * (t.Ranged == "lob" ? 3f : 1.5f);
		}
		lob.Go = visual ?? new GameObject("Lob");
		lob.Go.transform.SetParent(m_stage, false);
		lob.Go.transform.localPosition = lob.From;
		m_lobs.Add(lob);
	}

	private void UpdateSchemaWorld(float dt)
	{
		if (m_markerMaterial == null)
		{
			MakeMarkerMaterials();
		}
		if (m_markerMaterial != null)
		{
			float pulse = 0.45f + 0.3f * Mathf.Sin(Time.time * 12f);
			m_markerMaterial.color = new Color(1f, 0.25f, 0.05f, pulse);
		}
		for (int i = m_lobs.Count - 1; i >= 0; i--)
		{
			Lob lob = m_lobs[i];
			lob.Time += dt;
			float p = Mathf.Clamp01(lob.Time / lob.Duration);
			if (lob.Go != null)
			{
				lob.Go.transform.localPosition = Vector3.Lerp(lob.From, lob.To, p) + Vector3.up * (4f * p * (1f - p) * 5f);
				lob.Go.transform.Rotate(200f * dt, 90f * dt, 0f, Space.Self);
			}
			if (p >= 1f)
			{
				LobLands(lob);
				m_lobs.RemoveAt(i);
			}
		}
	}

	private void LobLands(Lob lob)
	{
		foreach (GameObject m in lob.Markers)
		{
			if (m != null)
			{
				Destroy(m);
			}
		}
		m_shake = Mathf.Max(m_shake, Mathf.Clamp01(1f - Mathf.Abs(lob.Z - m_z) / 30f) * 0.25f);
		string biome = m_biomes[BiomeAt(lob.Z)].Def.Name;
		if (lob.Kind == "tickdrop")
		{
			if (lob.Go != null)
			{
				Destroy(lob.Go);
			}
			GameObject? tick = Pictures.Monster("tick");
			if (tick != null)
			{
				SpawnEntry e = new SpawnEntry { Prefab = tick, Label = SpawnTables.Label(tick) };
				foreach (int lane in lob.Lanes)
				{
					SpawnCreature(e, lane, lob.Z, Kind.Block, 0f, BiomeAt(lob.Z), "charger");
				}
			}
			return;
		}
		bool first = true;
		foreach (int lane in lob.Lanes)
		{
			GameObject visual;
			if (lob.Kind == "tar")
			{
				visual = MakePatch(lane, lob.Z, m_tarMaterial);
			}
			else if (lob.Kind == "lob" && first && lob.Go != null)
			{
				// The rock itself sits across the lanes it landed on.
				visual = lob.Go;
				visual.transform.localRotation = Quaternion.identity;
			}
			else
			{
				visual = MakePatch(lane, lob.Z, m_markerMaterial);
				if (lob.Attack != null)
				{
					foreach (GameObject fx in lob.Attack.Effects)
					{
						GameObject f = Instantiate(Picture(fx), visual.transform, false);
						foreach (ParticleSystem ps in f.GetComponentsInChildren<ParticleSystem>(true))
						{
							ps.Play(true);
						}
						break;
					}
				}
			}
			m_things.Add(new Thing
			{
				Go = visual,
				Kind = lob.Kind == "tar" ? Kind.Slow : Kind.Low,
				Lane = lane,
				Z = lob.Z,
				HalfLength = 1.0f,
				Label = lob.Label,
				CrashText = lob.Kind == "lob"
					? $"You tripped over a {lob.Label}'s rock in the {biome}!"
					: $"You ran into a {lob.Label}'s fire in the {biome}!",
				Life = lob.Kind == "lob" ? 0f : 8f,
			});
			first = false;
		}
		if (lob.Go != null && !(lob.Kind == "lob"))
		{
			Destroy(lob.Go);
		}
	}

	private void UpdateSwitcher(Thing t, float dt, float distance)
	{
		int nearest = Mathf.Clamp(Mathf.RoundToInt(t.X / LaneWidth) + 1, 0, 2);
		t.Lane = nearest;
		if (distance > m_speed * 0.7f + 5f)
		{
			t.SwitchTimer -= dt;
			if (t.SwitchTimer <= 0f)
			{
				int to = Mathf.Clamp(nearest + (Random.value < 0.5f ? -1 : 1), 0, 2);
				if (to == nearest)
				{
					to = nearest == 0 ? 1 : 1;
				}
				t.TargetX = LaneX(to);
				t.XSpeed = 4.5f;
				t.SwitchTimer = Random.Range(0.8f, 1.4f);
			}
		}
		else
		{
			t.TargetX = LaneX(nearest);
			t.XSpeed = 5f;
		}
		if (t.Speed <= 0f)
		{
			t.X = Mathf.MoveTowards(t.X, t.TargetX, t.XSpeed * dt);
			t.Go.transform.localPosition = new Vector3(t.X, RoadY(t.Z) + t.BaseY, t.Z);
		}
	}

	private void UpdateCrosser(Thing t, float dt, float distance)
	{
		if (t.Phase == 0)
		{
			if (distance < m_speed * 1.25f + 6f)
			{
				t.Phase = 1;
				t.TargetX = -t.Side * (TrackHalf + 7f);
				t.XSpeed = (2f * TrackHalf + 11f) / 1.3f;
				if (t.Anim != null)
				{
					SetParam(t.Anim, "forward_speed", t.Info!.RunSpeed);
				}
				if (t.Info!.AlertSound.Length > 0)
				{
					m_audio.Play(t.Info.AlertSound, Mathf.Clamp01(1.1f - distance / 50f));
				}
			}
		}
		else if (t.Phase == 1)
		{
			t.X = Mathf.MoveTowards(t.X, t.TargetX, t.XSpeed * dt);
			if (Mathf.Abs(t.X - t.TargetX) < 0.05f)
			{
				t.Phase = 2;
				t.Life = 0.5f;
			}
		}
		t.Go.transform.localPosition = new Vector3(t.X, RoadY(t.Z) + t.BaseY, t.Z);
	}

	private bool UpdateLeaper(Thing t, float dt, float distance)
	{
		if (t.Phase == 0)
		{
			if (distance < m_speed * 1.8f + 10f && distance > 4f)
			{
				t.Phase = 1;
				t.PhaseTime = 0f;
				t.FromX = t.X;
				t.FromZ = t.Z;
				t.ToZ = Mathf.Max(m_z + m_speed * 0.9f + 4f, t.Z - 7f);
				t.Lane = m_lane;
				t.TargetX = LaneX(m_lane);
				t.Collidable = false;
				t.Markers.Add(MakeMarker(m_lane, t.ToZ, 2.4f, m_markerMaterial));
				if (t.Anim != null)
				{
					SetTrigger(t.Anim, "jump");
				}
				if (t.Info!.AlertSound.Length > 0)
				{
					m_audio.Play(t.Info.AlertSound, Mathf.Clamp01(1.1f - distance / 50f));
				}
				t.Alerted = true;
			}
			return false;
		}
		t.PhaseTime += dt;
		float p = Mathf.Clamp01(t.PhaseTime / 0.75f);
		t.X = Mathf.Lerp(t.FromX, t.TargetX, p);
		t.Z = Mathf.Lerp(t.FromZ, t.ToZ, p);
		t.BaseY = 3f * Mathf.Sin(Mathf.PI * p);
		t.Go.transform.localPosition = new Vector3(t.X, RoadY(t.Z) + t.BaseY, t.Z);
		if (p >= 1f)
		{
			t.Phase = 2;
			t.BaseY = 0f;
			t.Collidable = true;
			ClearMarkers(t);
			m_shake = Mathf.Max(m_shake, 0.12f);
		}
		return true;
	}

	private void UpdateChaser(Thing t, float dt)
	{
		bool sliding = m_slide > 0f && m_y < 0.4f;
		switch (t.Phase)
		{
			case 0:
				if (m_z >= t.Z - 12f)
				{
					t.Phase = 1;
					t.Z = m_z - 16f;
					t.X = LaneX(m_lane);
					t.Go.SetActive(true);
					// Facing the way you run.
					t.Go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
					if (t.Anim != null)
					{
						SetParam(t.Anim, "forward_speed", t.Info!.RunSpeed);
						if (t.ChaseHigh)
						{
							SetParam(t.Anim, "flying", true);
						}
					}
					string sound = t.Info!.AlertSound.Length > 0 ? t.Info.AlertSound : t.Info.StepSound;
					if (sound.Length > 0)
					{
						m_audio.Play(sound, 0.9f);
					}
					m_banner = $"A {t.Label} is coming up behind you!";
					m_bannerTime = 1.2f;
				}
				return;
			case 1:
				t.Z += (m_speed + 5f) * dt;
				t.X = Mathf.MoveTowards(t.X, LaneX(m_lane), 5f * dt);
				if (t.Z >= m_z - 3f)
				{
					t.Phase = 2;
					t.PhaseTime = 0.75f;
					t.Lane = m_lane;
					t.Markers.Add(MakeMarker(t.Lane, m_z + 1f, 3f, m_markerMaterial));
					TriggerAttack(t);
				}
				break;
			case 2:
				t.Z = m_z - 3f;
				t.X = Mathf.MoveTowards(t.X, LaneX(t.Lane), 5f * dt);
				if (t.Markers.Count > 0)
				{
					PlaceMarker(t.Markers[0], t.Lane, m_z + 1f);
				}
				t.PhaseTime -= dt;
				if (t.PhaseTime <= 0f)
				{
					ClearMarkers(t);
					t.Phase = 3;
					t.Life = 2.5f;
					bool caught = m_lane == t.Lane && (t.ChaseHigh ? !sliding : m_y < 1.2f);
					if (caught)
					{
						m_audio.Play(Sfx.HitMonster);
						Hit(t, $"A {t.Label} caught you from behind in the {m_biomes[BiomeAt(m_z)].Def.Name}!");
					}
				}
				break;
			default:
				t.Z += (m_speed - 7f) * dt;
				break;
		}
		t.Go.transform.localPosition = new Vector3(t.X, RoadY(t.Z) + t.BaseY, t.Z);
	}

	private void UpdateHelper(Thing t, float distance)
	{
		if (t.Phase == 0 && distance < 14f)
		{
			t.Phase = 1;
			string line = "";
			if (t.Info!.Talk.Count > 0)
			{
				string key = t.Info.Talk[Random.Range(0, t.Info.Talk.Count)];
				line = Localization.instance != null ? Localization.instance.Localize(key) : key;
			}
			// Now and then it takes out a threat just ahead of you.
			Thing? target = null;
			if (Random.value < 0.5f)
			{
				foreach (Thing other in m_things)
				{
					if (other != t && other.Collidable && other.Info != null && !other.Helper && other.Z > m_z + 10f && other.Z < m_z + 40f
						&& (target == null || other.Z < target.Z))
					{
						target = other;
					}
				}
			}
			if (target != null)
			{
				TriggerAttack(t);
				target.Collidable = false;
				target.Life = 0.05f;
				if (t.Attack != null)
				{
					foreach (GameObject fx in t.Attack.Effects)
					{
						GameObject f = Instantiate(Picture(fx), m_stage, false);
						f.transform.localPosition = target.Go.transform.localPosition + Vector3.up;
						foreach (ParticleSystem ps in f.GetComponentsInChildren<ParticleSystem>(true))
						{
							ps.Play(true);
						}
						Destroy(f, 3f);
						break;
					}
				}
			}
			string who = t.Label.Length > 0 ? t.Label : "Dvergr";
			m_banner = line.Length > 0 ? $"{who}: \"{line}\"" : who + " waves.";
			if (target != null)
			{
				m_banner += $" (and blasts the {target.Label} ahead!)";
			}
			m_bannerTime = 2.5f;
		}
	}

	// ---------------------------------------------------------------- markers

	private void MakeMarkerMaterials()
	{
		Shader shader = Shader.Find("Sprites/Default");
		if (shader == null)
		{
			return;
		}
		m_markerMaterial = new Material(shader) { name = "SurfMarker", color = new Color(1f, 0.25f, 0.05f, 0.6f), renderQueue = 3100 };
		m_gasMaterial = new Material(shader) { name = "SurfGas", color = new Color(0.45f, 0.75f, 0.2f, 0.4f), renderQueue = 3100 };
		m_tarMaterial = new Material(shader) { name = "SurfTar", color = new Color(0.05f, 0.04f, 0.03f, 0.85f), renderQueue = 3100 };
	}

	private GameObject MakeMarker(int lane, float z, float length, Material? material)
	{
		if (m_markerMaterial == null)
		{
			MakeMarkerMaterials();
		}
		GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
		Destroy(quad.GetComponent<Collider>());
		quad.name = "LaneMarker";
		quad.layer = m_layer;
		quad.transform.SetParent(m_stage, false);
		quad.transform.localScale = new Vector3(LaneWidth * 0.92f, length, 1f);
		Renderer r = quad.GetComponent<Renderer>();
		r.sharedMaterial = material ?? m_markerMaterial;
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		r.receiveShadows = false;
		PlaceMarker(quad, lane, z);
		return quad;
	}

	private void PlaceMarker(GameObject marker, int lane, float z)
	{
		if (marker == null)
		{
			return;
		}
		float slope = Mathf.Atan2(RoadY(z + 1f) - RoadY(z - 1f), 2f) * Mathf.Rad2Deg;
		marker.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + 0.08f, z);
		marker.transform.localRotation = Quaternion.Euler(90f - slope, 0f, 0f);
	}

	private GameObject MakePatch(int lane, float z, Material? material)
	{
		GameObject patch = MakeMarker(lane, z, 2f, material);
		patch.name = "Patch";
		return patch;
	}

	private GameObject MakeCloud(int lane, float z)
	{
		if (m_markerMaterial == null)
		{
			MakeMarkerMaterials();
		}
		GameObject cloud = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		Destroy(cloud.GetComponent<Collider>());
		cloud.name = "GasCloud";
		cloud.layer = m_layer;
		cloud.transform.SetParent(m_stage, false);
		cloud.transform.localScale = new Vector3(LaneWidth * 0.95f, 1.6f, 3.2f);
		cloud.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + 0.9f, z);
		Renderer r = cloud.GetComponent<Renderer>();
		r.sharedMaterial = m_gasMaterial;
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		return cloud;
	}

	private void ClearMarkers(Thing t)
	{
		foreach (GameObject m in t.Markers)
		{
			if (m != null)
			{
				Destroy(m);
			}
		}
		t.Markers.Clear();
	}

	private void DestroySchemaObjects()
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
		foreach (Material sm in m_starMaterials.Values)
		{
			Destroy(sm);
		}
		m_starMaterials.Clear();
		foreach (Material? m in new[] { m_markerMaterial, m_gasMaterial, m_tarMaterial })
		{
			if (m != null)
			{
				Destroy(m);
			}
		}
	}

	// ---------------------------------------------------------------- stars

	// Laps after the first: the game's own star looks (LevelEffects: size, colour shift, extra
	// parts) on every creature.
	private void ApplyStars(Thing t, int stars)
	{
		t.Level = stars + 1;
		CreatureInfo? info = t.Info;
		if (stars <= 0 || info == null || info.Levels.Count == 0 || t.Go == null || t.Go.transform.childCount == 0)
		{
			return;
		}
		var setup = info.Levels[Mathf.Clamp(stars - 1, 0, info.Levels.Count - 1)];
		Transform root = t.Go.transform.GetChild(0);
		Transform? target = string.IsNullOrEmpty(info.LevelPath) ? root : root.Find(info.LevelPath);
		// Beyond the game's own levels (3 stars), a little bigger still.
		float extra = stars > info.Levels.Count ? 1.1f : 1f;
		if (target != null)
		{
			target.localScale = Vector3.one * setup.scale * extra;
		}
		Transform? render = null;
		if (info.MainRenderPath != null)
		{
			render = info.MainRenderPath.Length == 0 ? root : root.Find(info.MainRenderPath);
		}
		Renderer? r = render != null ? render.GetComponent<Renderer>() : null;
		if (r != null && r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != null)
		{
			string key = t.Go.name + stars;
			if (!m_starMaterials.TryGetValue(key, out Material mat))
			{
				mat = new Material(r.sharedMaterials[0]);
				mat.SetFloat("_Hue", setup.hue);
				mat.SetFloat("_Saturation", setup.saturation);
				mat.SetFloat("_Value", setup.value);
				if (setup.emissive)
				{
					mat.SetColor("_EmissionColor", setup.emission);
				}
				m_starMaterials[key] = mat;
			}
			Material[] mats = r.sharedMaterials;
			mats[0] = mat;
			r.sharedMaterials = mats;
		}
		if (!string.IsNullOrEmpty(info.BaseEnablePath))
		{
			root.Find(info.BaseEnablePath)?.gameObject.SetActive(false);
		}
		if (!string.IsNullOrEmpty(setup.enablePath))
		{
			root.Find(setup.enablePath)?.gameObject.SetActive(true);
		}
	}

	private readonly Dictionary<string, Material> m_starMaterials = new();
}
