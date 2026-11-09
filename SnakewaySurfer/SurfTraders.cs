using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.10 (Sam's notes in the gap doc): the game's traders as rare stops along the track. Haldor
// in the Black Forest, the Bog Witch in the Swamp, and Hildir in the Meadows once you're on the
// 1-star lap or later. One stands at the edge of the track in front of their camp, lit by a
// lantern, with a line of coins and a gold mark along the lane on their side. They greet you as
// you come; run past them in that lane and they trade: your lives back to full, or one extra if
// you already have them all. The lanes around them stay clear, so it's a breather too.
internal sealed partial class SurfGame
{
	private sealed class TraderDef
	{
		public string Biome = "";
		public string Prefab = "";
		public string Camp = "";
		// The game's own lines for them (used if their effect lists come up empty).
		public string Greet = "";
		public string Trade = "";
		public string Bye = "";
		public int MinStars;
		public Color Lamp = new Color(1f, 0.75f, 0.4f);
	}

	private static readonly TraderDef[] s_traders =
	{
		new TraderDef
		{
			Biome = "Meadows", Prefab = "Hildir", Camp = "Hildir_camp", MinStars = 1,
			Greet = "sfx_hildir_hello", Trade = "sfx_hildir_trade|sfx_hildir_talk", Bye = "sfx_hildir_bye",
			Lamp = new Color(1f, 0.72f, 0.5f),
		},
		new TraderDef
		{
			Biome = "Black Forest", Prefab = "Haldor", Camp = "Vendor_BlackForest",
			Greet = "sfx_haldor_greet", Trade = "sfx_haldor_yea|sfx_haldor_laugh", Bye = "sfx_haldor_laugh",
			Lamp = new Color(1f, 0.7f, 0.35f),
		},
		new TraderDef
		{
			Biome = "Swamp", Prefab = "BogWitch", Camp = "BogWitch_Camp",
			Greet = "sfx_bogwitch_greet", Trade = "sfx_bogwitch_trade", Bye = "sfx_bogwitch_random",
			Lamp = new Color(0.65f, 1f, 0.55f),
		},
	};

	private sealed class TraderStop
	{
		public TraderDef Def = null!;
		public float Z;
		public int Side;
		public int Lane;
		public bool Spawned;
		public bool Greeted;
		public bool Done;
		public GameObject? Figure;
		public Animator? Anim;
		public Light? Lamp;
	}

	private readonly List<TraderStop> m_traderStops = new();
	private readonly Dictionary<string, (EffectList? greet, EffectList? trade, EffectList? bye)> m_traderFx = new();
	private Material? m_traderMarker;
	// The lives on the HUD glow gold for a moment after a trade.
	private float m_livesFlash;
	// The odds that a stretch with a trader has one standing by the track.
	private const float TraderChance = 0.4f;
	// The lanes kept clear around one, either way.
	private const float TraderClear = 14f;

	private void ResetTraders()
	{
		m_traderStops.Clear();
	}

	private bool NearTrader(float z, float within)
	{
		foreach (TraderStop t in m_traderStops)
		{
			if (Mathf.Abs(t.Z - z) < within)
			{
				return true;
			}
		}
		return false;
	}

	// Their side of the track is kept clear of trees around them and their camp.
	private bool TraderSide(float z, int side)
	{
		foreach (TraderStop t in m_traderStops)
		{
			if (t.Side == side && Mathf.Abs(t.Z - z) < 20f)
			{
				return true;
			}
		}
		return false;
	}

	// Each stretch gets its chance of a trader once, when it comes up (after its dungeon, if it
	// has one, is laid out, so the trader keeps well away from it).
	private void PlanTraders(float horizon)
	{
		if (!Plugin.Traders.Value || m_biomes.Count == 0)
		{
			return;
		}
		foreach (Segment s in m_segments)
		{
			if (s.TraderChecked)
			{
				continue;
			}
			string biome = s.Biome < m_biomes.Count ? m_biomes[s.Biome].Def.Name : "";
			bool open = !(s.Boss || s.Sea || s.Shore || s.Rush);
			if (open && !s.CryptChecked && Plugin.Dungeons.Value && m_biomes.Count >= 2 && ThemeFor(biome) != null)
			{
				// (Its dungeon isn't planned yet.)
				return;
			}
			s.TraderChecked = true;
			TraderDef? def = System.Array.Find(s_traders, d => d.Biome == biome);
			if (def == null || !open || s.Stars < def.MinStars || Random.value > TraderChance)
			{
				continue;
			}
			float end = s.End < float.MaxValue ? s.End : s.Start + Plugin.BiomeLength.Value;
			float lo = Mathf.Max(s.Start + 40f, horizon + 50f);
			float hi = end - 60f;
			// Not in or near its dungeon.
			List<(float a, float b)> spans = new() { (lo, hi) };
			if (m_crypt != null && m_crypt.End > lo && m_crypt.Start < hi)
			{
				spans = new() { (lo, Mathf.Min(hi, m_crypt.Start - 35f)), (Mathf.Max(lo, m_crypt.End + 35f), hi) };
			}
			spans.RemoveAll(sp => sp.b - sp.a < 1f);
			if (spans.Count == 0)
			{
				continue;
			}
			float total = 0f;
			foreach ((float a, float b) in spans)
			{
				total += b - a;
			}
			float pick = Random.Range(0f, total);
			float z = spans[0].a;
			foreach ((float a, float b) in spans)
			{
				if (pick <= b - a)
				{
					z = a + pick;
					break;
				}
				pick -= b - a;
			}
			int side = Random.value < 0.5f ? -1 : 1;
			m_traderStops.Add(new TraderStop { Def = def, Z = z, Side = side, Lane = side < 0 ? 0 : 2 });
			Plugin.Log.LogInfo($"{def.Prefab} trades by the track at {z:0} m ({(side < 0 ? "left" : "right")}).");
		}
	}

	private void SpawnTraders(float horizon)
	{
		foreach (TraderStop t in m_traderStops)
		{
			if (t.Spawned || t.Z > horizon)
			{
				continue;
			}
			t.Spawned = true;
			if (ArenaWeight(t.Z, out _) > 0.05f || InCrypt(t.Z) || !SpawnTrader(t))
			{
				// (Nobody there after all: nothing to trade.)
				t.Done = true;
			}
		}
	}

	private bool SpawnTrader(TraderStop t)
	{
		GameObject? prefab = Pictures.Prefab(t.Def.Prefab);
		if (prefab == null)
		{
			return false;
		}
		float edge = TrackHalf + ExtraFlat(t.Z);
		float x = t.Side * (edge + 1.4f);
		if (UnderSea(x, t.Z))
		{
			t.Side = -t.Side;
			t.Lane = 2 - t.Lane;
			x = -x;
			if (UnderSea(x, t.Z))
			{
				return false;
			}
		}
		if (!m_traderFx.ContainsKey(t.Def.Prefab))
		{
			Trader trader = prefab.GetComponent<Trader>();
			m_traderFx[t.Def.Prefab] = trader == null ? (null, null, null) : (trader.m_randomGreetFX, trader.m_randomStartTradeFX, trader.m_randomGoodbyeFX);
		}

		// The trader at the edge of the track, watching you come.
		GameObject figure = Instantiate(Picture(prefab), m_stage, false);
		figure.SetActive(true);
		figure.transform.localPosition = new Vector3(x, HillHeight(x, t.Z), t.Z);
		figure.transform.localRotation = Quaternion.Euler(0f, t.Side < 0 ? 90f : -90f, 0f);
		figure.transform.localScale = Vector3.one;
		t.Figure = figure;
		t.Anim = figure.GetComponentInChildren<Animator>();
		Thing ft = new Thing { Go = figure, Kind = Kind.Scenery, Z = t.Z, HalfLength = 2f };
		m_things.Add(ft);

		// A lantern's warm light on them (and on the lane they trade from).
		Light lamp = new GameObject("TraderLamp").AddComponent<Light>();
		lamp.gameObject.layer = m_layer;
		lamp.transform.SetParent(figure.transform, false);
		lamp.transform.localPosition = new Vector3(0f, 2.4f, 0.9f);
		lamp.type = LightType.Point;
		lamp.range = 7f;
		lamp.intensity = 1.5f;
		lamp.color = t.Def.Lamp;
		lamp.shadows = LightShadows.None;
		lamp.cullingMask = 1 << m_layer;
		t.Lamp = lamp;

		// Their camp behind them, if this game has it.
		GameObject? camp = LocationPicture(t.Def.Camp);
		if (camp != null)
		{
			PlaceCamp(camp, t, edge);
		}

		// Coins along their lane to say "this way", and a gold mark where they trade.
		for (int k = 0; k < 5; k++)
		{
			SpawnCoin(t.Lane, t.Z - 13f + k * 2.4f, 0.9f);
		}
		if (m_traderMarker == null)
		{
			if (m_markerMaterial == null)
			{
				MakeMarkerMaterials();
			}
			m_traderMarker = new Material(m_markerMaterial!) { name = "SurfTraderMarker", color = new Color(1f, 0.78f, 0.25f, 0.45f) };
		}
		ft.Markers.Add(MakeMarker(t.Lane, t.Z, 3f, m_traderMarker));
		return true;
	}

	private void PlaceCamp(GameObject template, TraderStop t, float edge)
	{
		GameObject camp = Instantiate(template, m_stage, false);
		camp.SetActive(true);
		camp.transform.localRotation = Quaternion.Euler(0f, t.Side < 0 ? 90f : -90f, 0f);
		camp.transform.localScale = Vector3.one;
		float x = t.Side * (edge + 11f);
		camp.transform.localPosition = new Vector3(x, HillHeight(x, t.Z) - 0.2f, t.Z);
		foreach (Transform child in camp.GetComponentsInChildren<Transform>(true))
		{
			// The camp's own trader stands by the track instead; and anything huge (a ward's
			// dome, a ground decal) would cover the track.
			if (child != camp.transform && (child.name == t.Def.Prefab || child.name.StartsWith(t.Def.Prefab + " (")))
			{
				child.gameObject.SetActive(false);
			}
		}
		foreach (Renderer r in camp.GetComponentsInChildren<Renderer>(true))
		{
			if (r.bounds.size.x > 26f || r.bounds.size.z > 26f)
			{
				r.enabled = false;
			}
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		}
		float half = 8f;
		if (Pictures.MeshBounds(camp, out Bounds bounds))
		{
			// Its near side a few metres back from the track.
			float inner = (t.Side < 0 ? bounds.max.x : bounds.min.x) - m_stage.position.x;
			float limit = t.Side * (edge + 3.5f);
			float push = t.Side < 0 ? Mathf.Min(0f, limit - inner) : Mathf.Max(0f, limit - inner);
			Vector3 p = camp.transform.localPosition;
			p.x += push;
			p.y = HillHeight(p.x, t.Z) - 0.2f;
			camp.transform.localPosition = p;
			half = Mathf.Clamp(bounds.size.z * 0.5f, 4f, 16f);
		}
		LightUp(camp);
		m_things.Add(new Thing { Go = camp, Kind = Kind.Scenery, Z = t.Z, HalfLength = half });
	}

	private void UpdateTraders(float dt)
	{
		m_livesFlash = Mathf.MoveTowards(m_livesFlash, 0f, dt);
		for (int i = m_traderStops.Count - 1; i >= 0; i--)
		{
			TraderStop t = m_traderStops[i];
			if (t.Z < m_z - 60f)
			{
				m_traderStops.RemoveAt(i);
				continue;
			}
			if (!t.Spawned || t.Done || t.Figure == null)
			{
				continue;
			}
			float ahead = t.Z - m_z;
			// They watch you come.
			Vector3 at = t.Figure.transform.localPosition;
			Vector3 look = new Vector3(m_x - at.x, 0f, Mathf.Max(m_z - at.z, -4f));
			if (look.sqrMagnitude > 0.01f)
			{
				Quaternion want = Quaternion.LookRotation(look);
				t.Figure.transform.localRotation = Quaternion.Slerp(t.Figure.transform.localRotation, want, dt * 4f);
			}
			if (!t.Greeted && ahead < 40f)
			{
				t.Greeted = true;
				if (t.Anim != null)
				{
					SetParam(t.Anim, "Stand", true);
					SetTrigger(t.Anim, "Greet");
				}
				PlayTraderSound(t, 0, 0.8f);
			}
			if (ahead > 0.3f)
			{
				continue;
			}
			t.Done = true;
			if (m_lane == t.Lane)
			{
				Trade(t);
			}
			else
			{
				// Missed: they call after you.
				if (t.Anim != null)
				{
					SetTrigger(t.Anim, "Greet");
				}
				PlayTraderSound(t, 2, 0.6f);
			}
		}
	}

	// Your lives back to full, or one more if you have them all.
	private void Trade(TraderStop t)
	{
		int full = Plugin.StartLives;
		m_lives = m_lives < full ? full : m_lives + 1;
		m_livesFlash = 1.2f;
		if (t.Anim != null)
		{
			SetTrigger(t.Anim, "Talk");
		}
		PlayTraderSound(t, 1, 1f);
		m_audio.Play(Sfx.Coin, 0.9f, 0.9f);
		m_audio.Play("sfx_coins_placed|sfx_coins_pile_destroyed|sfx_gui_craftitem_end", 0.7f, 1f);
		Flash(new Color(1f, 0.82f, 0.35f), 0.5f);
		GameObject? fx = FindFx("vfx_Potion_health_medium|vfx_HealthUpgrade|vfx_Potion_health_minor");
		if (fx != null)
		{
			PlayFx(fx, new Vector3(m_x, RoadY(m_z) + m_y + 0.2f, m_z), Quaternion.identity, 2.5f);
		}
		if (t.Lamp != null)
		{
			t.Lamp.intensity = 2.6f;
		}
	}

	// 0 greet, 1 trade, 2 goodbye: their own effect list if there is one, else the game's sound
	// by name.
	private void PlayTraderSound(TraderStop t, int which, float volume)
	{
		(EffectList? greet, EffectList? trade, EffectList? bye) = m_traderFx.TryGetValue(t.Def.Prefab, out var fx) ? fx : (null, null, null);
		EffectList? list = which == 0 ? greet : which == 1 ? trade : bye;
		if (list?.m_effectPrefabs != null && list.m_effectPrefabs.Length > 0)
		{
			m_audio.PlayEffects(list, volume);
			return;
		}
		m_audio.Play(which == 0 ? t.Def.Greet : which == 1 ? t.Def.Trade : t.Def.Bye, volume);
	}
}
