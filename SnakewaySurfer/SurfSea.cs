using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16 (Sam): before the Plains you cross the ocean. The track runs down a beach to a longship;
// you leap aboard and the camera rises above it. Steer with the rudder (A/D), set the sail
// (W/S: oars, half or full, as in the game) and fire the bow ballista (Throw Key) at what comes
// out of the sea. The wind never stops and turns now and then; the ship's speed follows the
// game's own sail rule (wind from the side is best, a headwind stops a sail), and the storm in
// the middle blows harder. Rolling waves must be met bow or stern first, never side on. A sea
// serpent waits in the storm (SurfSeaFoes.cs). On the far side the ship runs up the Plains
// beach and you run on.
internal sealed partial class SurfGame
{
	private enum Sail
	{
		Oars,
		Half,
		Full,
	}

	// The biome the crossing leads to.
	private const int SeaTo = 4;
	private const float SeaHalfWidth = 24f;
	private const float ShoreRamp = 70f;
	private const float MaxHeading = 75f;

	// 0: running; 1: leaping aboard; 2: sailing; 3: leaping ashore.
	private int m_seaPhase;
	private float m_seaPhaseT;
	private Vector3 m_leapFrom;
	private float m_leapToZ;
	private bool m_atSea;
	private bool m_startSea;
	private Segment? m_shipSeg;
	private float m_seaBlend;
	private float m_heading;
	private float m_turnRate;
	private Sail m_sail = Sail.Half;
	private float m_sailPos = 0.5f;
	private float m_boatSpeed;
	private float m_windAngle;
	private float m_windTarget;
	private float m_windStrength = 0.7f;
	private float m_windStrengthTarget = 0.7f;
	private float m_windTimer = 18f;
	private float m_windShift;
	private float m_surf;
	private float m_seaTime;
	private float m_storm;
	private float m_scrape;
	private float m_boatY;
	private float m_pitch;
	private float m_roll;
	private float m_lurch;
	private float m_lurchV;
	private float m_reload;
	private float m_shipSternZ;
	private float m_groundZ = float.MaxValue;
	private Vector2 m_shipHelm;
	private Vector3 m_bowLocal;
	private float m_deckRun = 1f;
	private const float LeapAshore = 0.7f;

	private GameObject? m_ship;
	private Transform? m_mast;
	private Transform? m_sailBottom;
	private Transform? m_sailFurled;
	private Transform? m_sailMid;
	private Transform? m_sailUnfurled;
	private Transform? m_rudderObj;
	private Vector3 m_helmLocal;
	private float m_centerLocalZ;
	private float m_keelLocalY;
	private float m_shipLength = 16f;
	private float m_shipBeam = 2.2f;
	private string m_attachAnim = "";
	private EffectList? m_sailEffect;
	private LineRenderer? m_safeLine;
	private AudioSource? m_oceanLoop;
	private AudioSource? m_windLoop;

	// The storm's weather (SurfFx.cs plays it like a boss's).
	private static readonly BossDef s_seaStorm = new BossDef
	{
		Name = "Storm",
		Env = "ThunderStorm|Rain",
		Storm = true,
		StormSound = "sfx_thunder",
		Tint = new Color(0.55f, 0.7f, 0.95f),
	};

	private static bool Crossing => Plugin.OceanCrossing.Value;

	// ---------------------------------------------------------------- stretches

	private Segment SeaSegment(float start, int stars) =>
		new Segment { Start = start, Biome = SeaTo - 1, Stars = stars, Sea = true, End = float.MaxValue };

	// A short stretch of Mountains coast to start a crossing from.
	private Segment ShoreSegment(int stars) =>
		new Segment { Start = 0f, Biome = SeaTo - 1, Stars = stars, Shore = true, End = 2f * GroundTile };

	// How far the land has sunk under the sea here: 1 at the water's edge of a crossing, fading
	// to 0 over ShoreRamp metres inland on both shores (times `reach`: a shorter slope is a
	// steeper one).
	private float ShoreT(float z, float reach)
	{
		if (m_segments.Count == 0 || !Crossing)
		{
			return 0f;
		}
		Segment s = SegAt(z);
		if (s.Sea)
		{
			return 1f;
		}
		float ramp = ShoreRamp * reach;
		float w = 0f;
		if (s.PrevSea)
		{
			w = 1f - Mathf.Clamp01((z - s.Start) / ramp);
		}
		if (s.End != float.MaxValue && s.End - z < ShoreRamp && SegAt(s.End + 0.01f).Sea)
		{
			w = Mathf.Max(w, 1f - Mathf.Clamp01((s.End - z) / ramp));
		}
		return Mathf.SmoothStep(0f, 1f, w);
	}

	private float ShoreWeight(float z) => ShoreT(z, 1f);

	// The land near a shore sinks toward the sea floor: a sandy bay where the track runs down,
	// the land beside it holding up longer and dropping steeply, like headlands either side.
	private float ShoreLand(float y, float x, float z, ref float veg)
	{
		if (ShoreWeight(z) <= 0f)
		{
			return y;
		}
		float side = Mathf.Clamp01((Mathf.Abs(x) - TrackHalf - 6f) / 50f);
		float reach = Mathf.Lerp(1f, 0.45f, side) * (0.9f + 0.2f * Mathf.PerlinNoise(x * 0.04f + 3.7f, 0.5f));
		float w = ShoreT(z, Mathf.Min(1f, reach));
		if (w <= 0f)
		{
			return y;
		}
		float floor = SeaY - 2.2f - 0.05f * Mathf.Max(0f, Mathf.Abs(x) - TrackHalf) - 2.5f * w * w;
		veg *= Mathf.Clamp01(1f - w * 1.6f);
		return Mathf.Lerp(y, Mathf.Min(y, floor), w);
	}

	// The track down the beach: kept above the water until the slope starts, so the shore is
	// one clean waterline, then down under the sea.
	private float ShoreRoad(float road, float z)
	{
		float w = ShoreWeight(z);
		if (w <= 0f)
		{
			return road;
		}
		float dry = road + Mathf.Max(0f, SeaY + 0.8f - road) * Mathf.Clamp01(w * 4f);
		return Mathf.Lerp(dry, SeaY - 3f, w);
	}

	// Is this spot under the sea (or its shallows)? Nothing grows or stands there.
	private bool UnderSea(float x, float z) => ShoreWeight(z) > 0f && HillHeight(x, z) < SeaY + 0.4f;

	// The arena before a crossing ends where the sea begins: the ground already laid out on its
	// last slope is built again sinking into the sea, and what stood there goes.
	private void ReshapeShore(float end)
	{
		float from = Mathf.Floor((end - ShoreRamp) / GroundTile) * GroundTile;
		for (int i = m_things.Count - 1; i >= 0; i--)
		{
			Thing t = m_things[i];
			bool redo = t.Kind == Kind.Ground ? t.Z + GroundTile * 0.5f > from + 0.01f
				: (t.Kind == Kind.Scenery || t.Kind == Kind.Coin) && t.Z > end - ShoreRamp;
			if (redo)
			{
				DestroyThing(t);
				m_things.RemoveAt(i);
			}
		}
		m_nextGroundZ = Mathf.Min(m_nextGroundZ, from);
		m_nextSceneryZ = Mathf.Min(m_nextSceneryZ, end - ShoreRamp);
	}

	// The start of a crossing coming up within `ahead` metres, if any.
	private Segment? SeaAhead(float ahead)
	{
		foreach (Segment s in m_segments)
		{
			if (s.Sea && s.Start > m_z - 40f && s.Start - m_z < ahead)
			{
				return s;
			}
		}
		return null;
	}

	// ---------------------------------------------------------------- the ship

	private static string PathFrom(Transform root, Transform t)
	{
		List<string> parts = new();
		for (Transform? c = t; c != null && c != root; c = c.parent)
		{
			parts.Insert(0, c.name);
		}
		return string.Join("/", parts);
	}

	private void MakeShip(Segment sea)
	{
		DestroyShip();
		GameObject? prefab = Pictures.Prefab("VikingShip") ?? Pictures.Prefab("Karve");
		GameObject go;
		Ship? ship = prefab != null ? prefab.GetComponent<Ship>() : null;
		if (prefab != null)
		{
			go = Instantiate(Picture(prefab), m_stage, false);
			go.name = "SurfShip";
		}
		else
		{
			// No ship in this game version: a plain hull.
			go = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Destroy(go.GetComponent<Collider>());
			go.transform.SetParent(m_stage, false);
			go.transform.localScale = new Vector3(4f, 1.2f, 14f);
		}
		go.layer = m_layer;
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		// The game hides the sea inside the hull with a mask; our sea doesn't need it.
		foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
		{
			string n = r.gameObject.name.ToLowerInvariant();
			string shader = r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name.ToLowerInvariant() : "";
			if (n.Contains("watermask") || n.Contains("water_mask") || shader.Contains("watermask"))
			{
				r.enabled = false;
			}
		}
		Transform? Find(Component? c)
		{
			if (c == null || prefab == null)
			{
				return null;
			}
			string path = PathFrom(prefab.transform, c.transform);
			return path.Length == 0 ? go.transform : go.transform.Find(path);
		}
		m_mast = ship != null ? Find(ship.m_mastObject != null ? ship.m_mastObject.transform : null) : null;
		m_sailBottom = ship != null ? Find(ship.m_sailBottomTransform) : null;
		m_sailFurled = ship != null ? Find(ship.m_sailFurledPosition) : null;
		m_sailMid = ship != null ? Find(ship.m_sailMidfurledPosition) : null;
		m_sailUnfurled = ship != null ? Find(ship.m_sailUnfurledPosition) : null;
		m_rudderObj = ship != null ? Find(ship.m_rudderObject != null ? ship.m_rudderObject.transform : null) : null;
		m_sailEffect = ship != null ? ship.m_changeSailPosEffect : null;
		m_attachAnim = ship != null && ship.m_shipControlls != null ? ship.m_shipControlls.m_attachAnimation ?? "" : "";

		Vector3 root = go.transform.position;
		if (Pictures.MeshBounds(go, out Bounds b))
		{
			m_shipLength = Mathf.Max(6f, b.size.z);
			m_shipBeam = Mathf.Max(1.2f, b.size.x * 0.4f);
			m_centerLocalZ = b.center.z - root.z;
			m_keelLocalY = b.min.y - root.y;
		}
		Transform? helm = ship != null && ship.m_shipControlls != null ? Find(ship.m_shipControlls.m_attachPoint) : null;
		m_helmLocal = helm != null ? go.transform.InverseTransformPoint(helm.position) : new Vector3(0f, m_keelLocalY + 1.6f, m_centerLocalZ - m_shipLength * 0.36f);
		if (Plugin.DebugLog.Value)
		{
			Plugin.Log.LogInfo($"Ship: {prefab?.name} length {m_shipLength:0.0}, beam {m_shipBeam:0.0}, keel {m_keelLocalY:0.0}, helm {m_helmLocal}, mast {m_mast != null}, sail {m_sailBottom != null}, attach '{m_attachAnim}'.");
		}
		m_ship = go;
		m_shipSeg = sea;
		// It waits where the beach has gone deep enough to float it.
		m_shipSternZ = sea.Start - 4f;
		for (float z = sea.Start - ShoreRamp; z < sea.Start; z += 1f)
		{
			if (RoadY(z) <= SeaY - 0.7f)
			{
				m_shipSternZ = z + 1.5f;
				break;
			}
		}
		m_groundZ = float.MaxValue;
		m_heading = 0f;
		m_sail = Sail.Half;
		m_sailPos = 0f;
		PlaceShip(0f, 0f, BeachedHelmZ(), true);
	}

	// Where the helm is while the ship waits on the beach, stern to the shore.
	private float BeachedHelmZ() => m_shipSternZ + (m_helmLocal.z - (m_centerLocalZ - m_shipLength * 0.5f));

	private void DestroyShip()
	{
		if (m_ship != null)
		{
			Destroy(m_ship);
		}
		m_ship = null;
		m_shipSeg = null;
		m_mast = m_sailBottom = m_sailFurled = m_sailMid = m_sailUnfurled = m_rudderObj = null;
		if (m_safeLine != null)
		{
			Destroy(m_safeLine.gameObject);
			m_safeLine = null;
		}
	}

	private Vector2 HeadingVector() => new Vector2(Mathf.Sin(m_heading * Mathf.Deg2Rad), Mathf.Cos(m_heading * Mathf.Deg2Rad));

	private Vector2 WindVector() => new Vector2(Mathf.Sin(m_windAngle * Mathf.Deg2Rad), Mathf.Cos(m_windAngle * Mathf.Deg2Rad));

	// The ship's middle (flat), from the helm point we steer by.
	private Vector2 ShipCenter() => new Vector2(m_x, m_z) + HeadingVector() * (m_centerLocalZ - m_helmLocal.z);

	// Floats the ship on the waves: height, pitch and roll from the water under bow, stern and
	// both sides, smoothed like a heavy hull; leans into turns and lurches when hit.
	private void PlaceShip(float dt, float helmX, float helmZ, bool snap = false)
	{
		if (m_ship == null)
		{
			return;
		}
		Vector2 f = HeadingVector();
		Vector2 r = new Vector2(f.y, -f.x);
		Vector2 c = new Vector2(helmX, helmZ) + f * (m_centerLocalZ - m_helmLocal.z);
		float half = m_shipLength * 0.4f;
		float hb = SeaHeight(c.x + f.x * half, c.y + f.y * half);
		float hs = SeaHeight(c.x - f.x * half, c.y - f.y * half);
		float hp = SeaHeight(c.x - r.x * m_shipBeam, c.y - r.y * m_shipBeam);
		float hr = SeaHeight(c.x + r.x * m_shipBeam, c.y + r.y * m_shipBeam);
		float y = (hb + hs + hp + hr) * 0.25f;
		// Beached: the keel rests on the sand.
		y = Mathf.Max(y, RoadY(c.y) + 0.9f);
		float pitch = -Mathf.Atan2(hb - hs, half * 2f) * Mathf.Rad2Deg;
		float roll = Mathf.Atan2(hr - hp, m_shipBeam * 2f) * Mathf.Rad2Deg;
		float k = snap ? 1f : 1f - Mathf.Exp(-5f * dt);
		m_boatY = Mathf.Lerp(m_boatY, y, k);
		m_pitch = Mathf.Lerp(m_pitch, pitch, k);
		m_roll = Mathf.Lerp(m_roll, roll, k);
		// A sprung lurch, for wave hits and rams.
		m_lurchV += (-m_lurch * 30f - m_lurchV * 4f) * dt;
		m_lurch += m_lurchV * dt;
		float lean = -m_turnRate * 0.06f;
		Quaternion rot = Quaternion.Euler(m_pitch, m_heading, m_roll + lean + m_lurch);
		Vector3 centre = new Vector3(c.x, m_boatY - 0.9f - m_keelLocalY, c.y);
		m_ship.transform.localRotation = rot;
		m_ship.transform.localPosition = centre - rot * new Vector3(0f, 0f, m_centerLocalZ);
		ApplySail(dt, snap);
	}

	// The sail furls and unfurls between the game's own marks, and the yard turns to the wind.
	private void ApplySail(float dt, bool snap)
	{
		float want = m_seaPhase == 2 ? (m_sail == Sail.Full ? 1f : m_sail == Sail.Half ? 0.5f : 0f) : 0f;
		m_sailPos = snap ? want : Mathf.MoveTowards(m_sailPos, want, dt);
		if (m_sailBottom != null && m_sailFurled != null && m_sailMid != null && m_sailUnfurled != null)
		{
			Vector3 a = m_sailFurled.position;
			Vector3 b = m_sailMid.position;
			if (m_sailPos >= 0.5f)
			{
				a = m_sailMid.position;
				b = m_sailUnfurled.position;
			}
			float t = Mathf.Repeat(Mathf.Clamp(m_sailPos, 0f, 0.999f) * 2f, 1f);
			m_sailBottom.position = Vector3.Lerp(a, b, t);
		}
		if (m_mast != null && m_ship != null && m_sailPos > 0.05f)
		{
			Vector3 up = m_ship.transform.up;
			Vector3 fwd = m_ship.transform.forward;
			Vector2 w = WindVector();
			Vector3 wind = Vector3.Cross(Vector3.Cross(m_stage.TransformDirection(new Vector3(w.x, 0f, w.y)), up), up);
			float t = 0.5f + Vector3.Dot(fwd, wind) * 0.5f;
			Vector3 look = -Vector3.Lerp(wind, Vector3.Normalize(wind - fwd), t);
			if (look.sqrMagnitude > 0.001f)
			{
				Quaternion to = Quaternion.LookRotation(look, up);
				m_mast.rotation = Quaternion.RotateTowards(m_mast.rotation, to, (snap ? 999f : 60f) * Mathf.Max(dt, 0.0001f));
			}
		}
		if (m_rudderObj != null)
		{
			Quaternion want2 = Quaternion.Euler(0f, 30f * Mathf.Clamp(-m_turnRate / 70f, -1f, 1f), 0f);
			if (m_sail == Sail.Oars && m_seaPhase == 2)
			{
				want2 *= Quaternion.Euler(0f, Mathf.Sin(m_seaClock * 6f) * 20f, 0f);
			}
			m_rudderObj.localRotation = Quaternion.Slerp(m_rudderObj.localRotation, want2, 0.5f);
		}
	}

	// The game's own rule for a sail (Ship.GetWindAngleFactor): best with the wind from the
	// side, 70% with it behind, nothing heading into it.
	private float WindAngleFactor()
	{
		Vector2 f = HeadingVector();
		float d = Vector2.Dot(WindVector(), -f);
		float side = Mathf.Lerp(0.7f, 1f, 1f - Mathf.Abs(d));
		float into = 1f - Mathf.Clamp01((d - 0.75f) / 0.05f);
		return side * into;
	}

	private float TurnRate => m_sail switch
	{
		Sail.Oars => 95f,
		Sail.Half => 70f,
		_ => 52f,
	};

	// ---------------------------------------------------------------- boarding and landing

	// While running: the ship waits on the beach; leap aboard when you reach it.
	private void UpdateShore(float dt)
	{
		if (!Crossing)
		{
			return;
		}
		Segment? sea = SeaAhead(260f);
		if (sea != null && (m_ship == null || m_shipSeg != sea))
		{
			MakeShip(sea);
		}
		if (m_ship != null && m_shipSeg != null && m_seaPhase == 0 && !m_atSea)
		{
			if (m_shipSeg.Start > m_z)
			{
				// Waiting for you on the near beach, rocking a little.
				PlaceShip(dt, 0f, BeachedHelmZ());
				if (m_z >= m_shipSternZ - 1.5f && m_state == State.Running)
				{
					Board();
				}
			}
			else if (m_shipSeg.End != float.MaxValue && m_z > m_shipSeg.End + 140f)
			{
				// Left on the far beach, now out of sight.
				DestroyShip();
			}
		}
	}

	private void Board()
	{
		m_seaPhase = 1;
		m_seaPhaseT = 0f;
		m_atSea = true;
		m_leapFrom = m_avatar != null ? m_avatar.transform.localPosition : new Vector3(m_x, RoadY(m_z) + m_y, m_z);
		m_boatSpeed = Mathf.Min(m_speed * 0.4f, 5f);
		m_heading = 0f;
		m_turnRate = 0f;
		m_sail = Sail.Half;
		m_slide = 0f;
		m_y = 0f;
		m_vy = 0f;
		m_windAngle = m_windTarget = Random.Range(-140f, 140f);
		m_windStrength = m_windStrengthTarget = Random.Range(0.6f, 0.85f);
		m_windTimer = Random.Range(14f, 20f);
		SetTrigger("jump");
		m_audio.Play(Sfx.Jump);
		Plugin.Log.LogInfo($"Boarding the longship at {m_z:0} m; the crossing starts at {m_shipSeg?.Start ?? 0f:0} m.");
	}

	private void Land()
	{
		m_seaPhase = 3;
		m_seaPhaseT = 0f;
		m_shipHelm = new Vector2(m_x, m_z);
		m_sail = Sail.Oars;
		// Up the deck from the helm to the bow, then over it onto the sand.
		m_bowLocal = new Vector3(0f, m_helmLocal.y, m_centerLocalZ + m_shipLength * 0.4f);
		m_deckRun = Mathf.Clamp((m_bowLocal.z - m_helmLocal.z) / 11f, 0.5f, 1.8f);
		m_leapToZ = m_groundZ + 6f;
		m_audio.Play("sfx_ship_impact|sfx_wood_hit", 0.8f, 0.8f);
		m_shake = Mathf.Max(m_shake, 0.3f);
		m_lurchV += Random.Range(-20f, 20f);
		SpawnSplash(ShipCenter() + HeadingVector() * (m_shipLength * 0.45f), 1.2f);
		if (m_shipSeg != null)
		{
			Progress.SaveLanded(m_player, m_shipSeg.Stars);
		}
	}

	// ---------------------------------------------------------------- sailing

	private void UpdateSea(float dt, bool canInput)
	{
		m_runTime += dt;
		m_seaTime += dt;
		m_seaPhaseT += dt;
		m_hurtTime = Mathf.Max(0f, m_hurtTime - dt);
		m_reload -= dt;
		m_surf = Mathf.Max(0f, m_surf - dt);
		m_scrape = Mathf.Max(0f, m_scrape - dt);
		m_windShift = Mathf.Max(0f, m_windShift - dt * 0.5f);
		Segment? sea = m_shipSeg;
		bool steering = m_seaPhase == 2;
		float moves = UpdateMoves(dt, canInput && steering);

		if (m_seaPhase == 1)
		{
			// The leap aboard: an arc onto the helm while the ship pushes off.
			float p = Mathf.Clamp01(m_seaPhaseT / 0.75f);
			float helmZ = BeachedHelmZ() + m_seaPhaseT * 1.5f;
			m_x = Mathf.Lerp(m_leapFrom.x, 0f, p);
			m_z = Mathf.Lerp(m_leapFrom.z, helmZ, p);
			if (p >= 1f)
			{
				m_seaPhase = 2;
				m_seaPhaseT = 0f;
				m_audio.Play("sfx_ship_impact|sfx_wood_hit", 0.5f, 1.1f);
				m_audio.PlayEffects(m_sailEffect, 0.8f);
				StartSeaLoops();
			}
		}

		// The wind turns now and then; in the storm it blows harder and more often.
		m_windTimer -= dt;
		if (m_windTimer <= 0f && m_seaPhase == 2)
		{
			ShiftWind();
		}
		m_windAngle = Mathf.MoveTowardsAngle(m_windAngle, m_windTarget, 28f * dt);
		m_windStrength = Mathf.MoveTowards(m_windStrength, m_windStrengthTarget, 0.15f * dt);

		if (steering && canInput)
		{
			if (KeyDown(KeyCode.W, KeyCode.UpArrow) && m_sail != Sail.Full)
			{
				m_sail++;
				SailSound();
			}
			else if (KeyDown(KeyCode.S, KeyCode.DownArrow, KeyCode.LeftControl, KeyCode.C) && m_sail != Sail.Oars)
			{
				m_sail--;
				SailSound();
			}
			if (Input.GetKey(Plugin.ThrowKey.Value) && m_reload <= 0f)
			{
				FireBallista();
			}
		}

		// The rudder: A/D as seen on screen (ahead is always up).
		float steer = 0f;
		if (steering && canInput)
		{
			steer = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
		}
		bool landing = sea != null && sea.End != float.MaxValue && m_z > sea.End - 75f;
		if (landing && steering)
		{
			// Bring her in straight up the beach.
			float wantX = Mathf.Clamp(-m_x * 2.5f, -40f, 40f);
			steer = Mathf.Clamp((wantX - m_heading) / 25f, -1f, 1f);
			if (m_sail == Sail.Full)
			{
				m_sail = Sail.Half;
			}
		}
		m_turnRate = Mathf.MoveTowards(m_turnRate, steer * TurnRate, 360f * dt);
		m_heading = Mathf.Clamp(m_heading + m_turnRate * dt, -MaxHeading, MaxHeading);

		// Speed: the sail by the wind, or the oars; rowing hard (sprint) and riding a wave add to it.
		float target;
		if (m_seaPhase == 2)
		{
			float gust = Mathf.Lerp(0.25f, 1f, m_windStrength) * (1f + 0.4f * m_storm);
			float sail = Plugin.SailSpeed.Value * WindAngleFactor() * gust * (m_sail == Sail.Full ? 1f : 0.62f);
			target = m_sail == Sail.Oars ? 6f : Mathf.Max(3.5f, sail);
			target *= moves;
			if (m_surf > 0f)
			{
				target *= 1.35f;
			}
			if (m_scrape > 0f)
			{
				target *= 0.6f;
			}
		}
		else
		{
			target = m_seaPhase == 1 ? 4f : 0f;
		}
		float accel = target > m_boatSpeed ? 3.2f : 5f;
		m_boatSpeed = Mathf.MoveTowards(m_boatSpeed, target, accel * dt);
		m_speed = m_boatSpeed;

		if (m_seaPhase == 2)
		{
			Vector2 f = HeadingVector();
			m_x += f.x * m_boatSpeed * dt;
			m_z += f.y * m_boatSpeed * dt;
			// The channel's edges: reefs that scrape and turn you back, no harm done.
			if (Mathf.Abs(m_x) > SeaHalfWidth)
			{
				m_x = Mathf.Sign(m_x) * SeaHalfWidth;
				m_heading = Mathf.MoveTowards(m_heading, 0f, 90f * dt);
				if (m_scrape <= 0f)
				{
					m_audio.Play("sfx_ship_impact|sfx_wood_hit", 0.5f, 0.7f);
					SpawnSplash(new Vector2(m_x + Mathf.Sign(m_x) * 2f, m_z), 0.8f);
					m_lurchV += -Mathf.Sign(m_x) * 25f;
				}
				m_scrape = 0.6f;
			}
			// Wake: foam off both quarters, more the faster she goes.
			if (Random.value < dt * (6f + m_boatSpeed * 1.2f))
			{
				Vector2 c = ShipCenter();
				Vector2 r = new Vector2(f.y, -f.x);
				float side = Random.value < 0.5f ? -1f : 1f;
				Vector2 at = c - f * (m_shipLength * 0.42f) + r * (side * m_shipBeam * 0.8f);
				AddFoam(at, Random.Range(0.9f, 1.5f), Random.Range(1.4f, 2.2f), 0.55f, 2.2f, r * side * 1.2f);
			}
			if (m_boatSpeed > 7f && Random.value < dt * m_boatSpeed * 0.6f)
			{
				// Spray at the bow.
				Vector2 bow = ShipCenter() + f * (m_shipLength * 0.47f);
				AddFoam(bow, Random.Range(0.6f, 1.1f), 0.8f, 0.6f, 1.2f);
			}
			// The far beach: run aground and leap off.
			if (sea != null && sea.End != float.MaxValue)
			{
				if (m_groundZ == float.MaxValue)
				{
					m_groundZ = sea.End + ShoreRamp * 0.5f;
					for (float z = sea.End; z < sea.End + ShoreRamp; z += 1f)
					{
						if (RoadY(z) >= SeaY - 0.6f)
						{
							m_groundZ = z;
							break;
						}
					}
				}
				Vector2 bowNow = ShipCenter() + f * (m_shipLength * 0.5f);
				if (bowNow.y >= m_groundZ)
				{
					Land();
				}
			}
		}
		else if (m_seaPhase == 3)
		{
			// She grinds a few metres up the sand and stops; you run up the deck and leap off.
			m_boatSpeed *= Mathf.Exp(-5f * dt);
			m_speed = Mathf.Max(m_speed, 9f);
			m_shipHelm += HeadingVector() * (m_boatSpeed * dt);
			m_heading = Mathf.MoveTowards(m_heading, 0f, 20f * dt);
			m_turnRate = 0f;
			PlaceShip(dt, m_shipHelm.x, m_shipHelm.y);
			if (m_seaPhaseT >= m_deckRun && m_seaPhaseT - dt < m_deckRun)
			{
				SetTrigger("jump");
				m_audio.Play(Sfx.Jump);
			}
			else if (m_seaPhaseT < m_deckRun)
			{
				// Footsteps on the planks.
				m_stepTimer -= dt;
				if (m_stepTimer <= 0f)
				{
					m_stepTimer = 0.27f;
					m_audio.Play("sfx_footstep_wood|sfx_footstep_run", 0.4f, 1f, true);
				}
			}
			Vector3 at = SeaAvatarSpot();
			m_x = at.x;
			m_z = at.z;
			m_y = Mathf.Max(0f, at.y - RoadY(m_z));
			if (m_seaPhaseT >= m_deckRun + LeapAshore)
			{
				EndSailing();
				return;
			}
		}

		if (m_seaPhase == 2)
		{
			PlaceShip(dt, m_x, m_z);
		}
		else if (m_seaPhase == 1)
		{
			// Pushing off from the beach.
			PlaceShip(dt, 0f, BeachedHelmZ() + m_seaPhaseT * 1.5f);
		}

		UpdateSeaLoops(dt);
		UpdateSafeLine();
		if (m_seaPhase == 2)
		{
			UpdateRollersHit();
			UpdateSeaStory(dt);
		}
		UpdateSeaFoes(dt);
		if (m_state != State.Running)
		{
			return;
		}
		// The land on the far side.
		SpawnAhead();
	}

	private void EndSailing()
	{
		m_seaPhase = 0;
		m_atSea = false;
		m_lane = 1;
		m_x = 0f;
		m_z = m_leapToZ;
		m_y = 0f;
		m_vy = 0f;
		m_slide = 0f;
		AvatarBool(m_attachAnim, false);
		m_audio.Play(Sfx.Land, 0.8f);
		StopSeaLoops();
		m_storm = 0f;
		// A clear stretch of beach before anything comes at you.
		m_nextRowZ = Mathf.Max(m_nextRowZ, m_z + 45f);
		ClearSeaFoes();
		Plugin.Log.LogInfo($"Landed on the {m_biomes[Mathf.Min(SeaTo, m_biomes.Count - 1)].Def.Name} at {m_z:0} m after {m_seaTime:0} s at sea.");
	}

	private void ShiftWind()
	{
		bool storm = m_storm > 0.3f;
		m_windTimer = storm ? Random.Range(9f, 14f) : Random.Range(14f, 24f);
		float turn = Random.Range(60f, 150f) * (Random.value < 0.5f ? -1f : 1f);
		float to = Mathf.Repeat(m_windAngle + turn + 180f, 360f) - 180f;
		if (storm)
		{
			// Storm winds blow up or down the crossing, so its waves can always be met head on.
			to = (Random.value < 0.5f ? 180f : 0f) + Random.Range(-40f, 40f);
		}
		m_windTarget = to;
		m_windStrengthTarget = storm ? Random.Range(0.85f, 1f) : Random.Range(0.45f, 1f);
		m_windShift = 1f;
		// A gust as it turns.
		m_audio.Play("sfx_frozenking_frozentwirl_wind|sfx_wind", 0.35f, 1.2f);
	}

	private void SailSound()
	{
		if (m_sailEffect?.m_effectPrefabs != null && m_sailEffect.m_effectPrefabs.Length > 0)
		{
			m_audio.PlayEffects(m_sailEffect, 0.9f);
		}
		m_audio.Play("sfx_cloth|sfx_unarmed_swing", 0.5f, 0.8f);
	}

	// A burst of foam and a splash where something hits the water.
	private void SpawnSplash(Vector2 at, float size)
	{
		for (int i = 0; i < 6; i++)
		{
			AddFoam(at + Random.insideUnitCircle * size, Random.Range(0.8f, 1.6f) * size, Random.Range(0.8f, 1.4f), 0.85f, 2.5f);
		}
		GameObject? fx = FindFx("vfx_watersplash_longship|vfx_water_surface|fx_float_hitwater|fx_land_water");
		if (fx != null)
		{
			PlayFx(fx, new Vector3(at.x, SeaHeight(at.x, at.y), at.y), Quaternion.identity, 3f, null, Mathf.Clamp(size, 0.6f, 2f));
		}
		m_audio.Play("sfx_ship_waterimpact|sfx_land_water", 0.5f + 0.3f * Mathf.Min(1f, size), Random.Range(0.85f, 1.1f));
	}

	// Who is at the helm: the avatar stands at the steering oar, holding on as in the game.
	private bool PlaceSeaAvatar()
	{
		if (m_avatar == null || (m_seaPhase == 0 && !m_atSea) || m_ship == null)
		{
			return false;
		}
		Vector3 helm = m_ship.transform.localPosition + m_ship.transform.localRotation * m_helmLocal;
		Quaternion rot = m_ship.transform.localRotation;
		if (m_seaPhase == 1)
		{
			float p = Mathf.Clamp01(m_seaPhaseT / 0.75f);
			Vector3 pos = Vector3.Lerp(m_leapFrom, helm, p) + Vector3.up * Mathf.Sin(p * Mathf.PI) * 1.6f;
			m_avatar.transform.localPosition = pos;
			m_avatar.transform.localRotation = Quaternion.Slerp(Quaternion.identity, rot, p);
		}
		else if (m_seaPhase == 3)
		{
			m_avatar.transform.localPosition = SeaAvatarSpot();
			float p = Mathf.Clamp01((m_seaPhaseT - m_deckRun) / LeapAshore);
			m_avatar.transform.localRotation = Quaternion.Slerp(rot, Quaternion.identity, p);
		}
		else
		{
			m_avatar.transform.localPosition = helm;
			m_avatar.transform.localRotation = rot;
		}
		bool show = m_hurtTime <= 0f || Mathf.FloorToInt(m_hurtTime * 12f) % 2 == 0;
		foreach (Renderer r in m_avatarRenderers)
		{
			if (r != null)
			{
				r.enabled = show;
			}
		}
		bool deckRun = m_seaPhase == 3 && m_seaPhaseT < m_deckRun;
		AvatarFloat("forward_speed", deckRun ? m_runAnimSpeed : 0f);
		AvatarFloat("sideway_speed", 0f);
		AvatarBool("onGround", m_seaPhase == 2 || deckRun);
		AvatarBool("crouching", false);
		AvatarBool("inWater", false);
		AvatarBool(m_attachAnim, m_seaPhase == 2);
		if (m_avatarAnim != null)
		{
			m_avatarAnim.speed = 1f;
		}
		return true;
	}

	// Where the avatar is while landing: running up the deck (it rides the ship), then in the
	// air over the bow, down onto the sand.
	private Vector3 SeaAvatarSpot()
	{
		if (m_ship == null)
		{
			return new Vector3(m_x, RoadY(m_z) + m_y, m_z);
		}
		Transform t = m_ship.transform;
		if (m_seaPhaseT < m_deckRun)
		{
			float p = Mathf.SmoothStep(0f, 1f, m_seaPhaseT / m_deckRun);
			return t.localPosition + t.localRotation * Vector3.Lerp(m_helmLocal, m_bowLocal, p);
		}
		Vector3 bow = t.localPosition + t.localRotation * m_bowLocal;
		Vector3 to = new Vector3(0f, RoadY(m_leapToZ), m_leapToZ);
		float q = Mathf.Clamp01((m_seaPhaseT - m_deckRun) / LeapAshore);
		return Vector3.Lerp(bow, to, q) + Vector3.up * (Mathf.Sin(q * Mathf.PI) * 2.4f);
	}

	// The view from high over the ship, ahead up the screen, blended with the runner's.
	private void PlaceSeaCamera(float dt)
	{
		bool up = m_atSea && m_seaPhase != 3 && m_state != State.Menu;
		m_seaBlend = Mathf.MoveTowards(m_seaBlend, up ? 1f : 0f, dt / 1.8f);
		if (m_seaBlend <= 0f || m_ship == null)
		{
			return;
		}
		Vector2 c = ShipCenter();
		float height = 20f + 1.15f * m_shipLength;
		Vector3 focus = new Vector3(m_x * 0.8f, SeaY, c.y + 9f);
		Vector3 cam = focus + new Vector3(0f, height, -height * 0.45f);
		Vector3 seaPos = m_stage.TransformPoint(cam);
		Quaternion seaRot = Quaternion.LookRotation(m_stage.TransformPoint(focus) - seaPos);
		float b = Mathf.SmoothStep(0f, 1f, m_seaBlend);
		Vector3 pos = Vector3.Lerp(m_camPos, seaPos, b);
		Quaternion rot = Quaternion.Slerp(m_camera.transform.rotation, seaRot, b);
		m_camera.transform.position = pos + (m_shake > 0.001f ? Random.insideUnitSphere * m_shake * 1.5f : Vector3.zero);
		m_camera.transform.rotation = rot;
		m_camFinal = m_camera.transform.position;
		m_camFinalRot = m_camera.transform.rotation;
	}

	// At sea the air is clear enough to see the ship from above, even in the storm.
	private void ApplySeaWeather(ref float fog, ref Color fogColor, ref Color sky, ref Color ambient)
	{
		float w = m_seaBlend;
		if (w <= 0f)
		{
			return;
		}
		Color seaSky = Color.Lerp(new Color(0.62f, 0.76f, 0.9f), new Color(0.3f, 0.34f, 0.4f), m_storm);
		sky = Color.Lerp(sky, seaSky, w * (1f - 0.5f * m_storm));
		fogColor = Color.Lerp(fogColor, seaSky, w * 0.7f);
		fog = Mathf.Lerp(fog, Mathf.Min(fog, 0.006f + 0.004f * m_storm), w);
		ambient = Color.Lerp(ambient, ambient * (1f - 0.35f * m_storm), w);
	}

	// While a rolling wave nears, a band on the water shows which way to meet it: along it is
	// safe. It turns red while you're side on.
	private void UpdateSafeLine()
	{
		Roller? next = null;
		float best = float.MaxValue;
		Vector2 boat = ShipCenter();
		foreach (Roller r in m_rollers)
		{
			float gap = Vector2.Dot(boat, r.Dir) - r.Crest;
			if (!r.Passed && gap > 0f && gap < 36f && gap < best)
			{
				best = gap;
				next = r;
			}
		}
		if (next == null || m_seaPhase != 2)
		{
			if (m_safeLine != null)
			{
				m_safeLine.enabled = false;
			}
			return;
		}
		if (m_safeLine == null)
		{
			GameObject go = new GameObject("SafeLine") { layer = m_layer };
			go.transform.SetParent(m_stage, false);
			m_safeLine = go.AddComponent<LineRenderer>();
			m_safeLine.useWorldSpace = true;
			m_safeLine.sharedMaterial = BoltMaterial(false);
			// Facing up off the water (the line's face follows the object's z axis).
			m_safeLine.alignment = LineAlignment.TransformZ;
			go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
			m_safeLine.positionCount = 2;
			m_safeLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			m_safeLine.numCapVertices = 4;
		}
		bool side = SideOn(next.Dir);
		float alpha = Mathf.Clamp01((36f - best) / 12f) * 0.45f;
		Color c = side ? new Color(1f, 0.25f, 0.05f, alpha + 0.15f) : new Color(1f, 1f, 1f, alpha);
		m_safeLine.enabled = true;
		m_safeLine.startColor = c;
		m_safeLine.endColor = c;
		m_safeLine.startWidth = m_shipBeam * 1.6f;
		m_safeLine.endWidth = m_shipBeam * 1.6f;
		float y = SeaHeight(boat.x, boat.y) + 0.3f;
		m_safeLine.SetPosition(0, m_stage.TransformPoint(new Vector3(boat.x - next.Dir.x * 18f, y, boat.y - next.Dir.y * 18f)));
		m_safeLine.SetPosition(1, m_stage.TransformPoint(new Vector3(boat.x + next.Dir.x * 18f, y, boat.y + next.Dir.y * 18f)));
	}

	private bool SideOn(Vector2 waveDir) => Mathf.Abs(Vector2.Dot(HeadingVector(), waveDir)) < 0.6f;

	// A rolling wave reaching the ship: met square it lifts her and she surfs down its back;
	// side on it throws her over and costs a life.
	private void UpdateRollersHit()
	{
		Vector2 boat = ShipCenter();
		foreach (Roller r in m_rollers)
		{
			if (r.Passed || r.Age < 0.5f)
			{
				continue;
			}
			float gap = Vector2.Dot(boat, r.Dir) - r.Crest;
			if (gap > 0.5f)
			{
				continue;
			}
			r.Passed = true;
			if (SideOn(r.Dir) && r.Height > 1.2f)
			{
				m_lurchV += 140f * (Vector2.Dot(new Vector2(HeadingVector().y, -HeadingVector().x), r.Dir) > 0f ? -1f : 1f);
				SpawnSplash(boat, 2f);
				m_audio.Play("sfx_ship_impact|sfx_wood_hit", 1f, 0.75f);
				m_shake = Mathf.Max(m_shake, 0.5f);
				Hit(new Thing { Label = "", JumpOrSlide = true, Heavy = true }, "A wave caught you side on and rolled the ship! (Meet waves bow or stern first.)");
			}
			else
			{
				// Down its back like a sled: speed for a moment.
				m_surf = 2.2f;
				m_audio.Play("sfx_ship_waterimpact|sfx_land_water", 0.7f, 1f);
				SpawnSplash(boat + HeadingVector() * (m_shipLength * 0.4f), 0.9f);
				m_lurchV += Random.Range(-15f, 15f);
				m_flash2 = Mathf.Max(m_flash2, 0.15f);
				m_flashColor = new Color(0.8f, 0.95f, 1f);
			}
		}
	}

	// ---------------------------------------------------------------- sound

	private AudioSource? MakeLoop(AudioClip? clip)
	{
		if (clip == null)
		{
			return null;
		}
		AudioSource s = gameObject.AddComponent<AudioSource>();
		s.clip = clip;
		s.loop = true;
		s.spatialBlend = 0f;
		s.volume = 0f;
		s.outputAudioMixerGroup = AudioMan.instance != null ? AudioMan.instance.m_ambientMixer : null;
		s.Play();
		return s;
	}

	private void StartSeaLoops()
	{
		if (!Plugin.Sounds.Value || AudioMan.instance == null)
		{
			return;
		}
		m_oceanLoop ??= MakeLoop(AudioMan.instance.m_oceanAudio);
		m_windLoop ??= MakeLoop(AudioMan.instance.m_windAudio);
	}

	// The sea and the wind, as the game plays them: louder and higher as the wind rises.
	private void UpdateSeaLoops(float dt)
	{
		float master = Plugin.SoundVolume.Value * (m_paused ? 0f : 1f);
		if (m_oceanLoop != null)
		{
			m_oceanLoop.volume = Mathf.MoveTowards(m_oceanLoop.volume, master * (0.45f + 0.35f * m_windStrength + 0.2f * m_storm), dt * 0.5f);
		}
		if (m_windLoop != null)
		{
			float w = Mathf.Pow(Mathf.Clamp01(m_windStrength * (0.75f + 0.5f * m_storm)), 1.5f);
			m_windLoop.volume = Mathf.MoveTowards(m_windLoop.volume, master * Mathf.Lerp(0.1f, 0.7f, w), dt * 0.5f);
			m_windLoop.pitch = Mathf.Lerp(0.6f, 1.4f, w);
		}
	}

	private void StopSeaLoops()
	{
		foreach (AudioSource? s in new[] { m_oceanLoop, m_windLoop })
		{
			if (s != null)
			{
				Destroy(s);
			}
		}
		m_oceanLoop = null;
		m_windLoop = null;
	}

	// ---------------------------------------------------------------- reset

	private void ResetSea()
	{
		m_seaPhase = 0;
		m_atSea = false;
		m_seaBlend = 0f;
		m_heading = 0f;
		m_turnRate = 0f;
		m_boatSpeed = 0f;
		m_seaTime = 0f;
		m_storm = 0f;
		m_surf = 0f;
		m_lurch = m_lurchV = 0f;
		m_groundZ = float.MaxValue;
		AvatarBool(m_attachAnim, false);
		StopSeaLoops();
		DestroyShip();
		ClearSeaFoes();
		ClearOcean();
	}

	// ---------------------------------------------------------------- HUD

	private Sprite? m_hudWind;
	private Sprite? m_hudCompass;
	private Sprite? m_hudShip;
	private Sprite? m_hudFull;
	private Sprite? m_hudHalf;
	private Sprite? m_hudOars;
	private bool m_hudSprites;

	private void FindShipHud()
	{
		m_hudSprites = true;
		Hud? hud = Hud.instance;
		if (hud == null)
		{
			return;
		}
		static Sprite? Of(GameObject? go)
		{
			Image? img = go != null ? go.GetComponentInChildren<Image>(true) : null;
			return img != null ? img.sprite : null;
		}
		m_hudWind = hud.m_shipWindIcon != null ? hud.m_shipWindIcon.sprite : null;
		m_hudCompass = hud.m_shipWindIndicatorRoot != null ? Of(hud.m_shipWindIndicatorRoot.gameObject) : null;
		if (m_hudCompass == m_hudWind)
		{
			m_hudCompass = null;
		}
		m_hudShip = hud.m_shipRudderIcon != null ? hud.m_shipRudderIcon.sprite : null;
		m_hudFull = Of(hud.m_fullSail);
		m_hudHalf = Of(hud.m_halfSail);
		m_hudOars = Of(hud.m_rudderSlow);
	}

	private static void DrawRotated(Rect r, Sprite? sprite, float degrees, Color tint)
	{
		if (sprite == null)
		{
			return;
		}
		Matrix4x4 m = GUI.matrix;
		GUIUtility.RotateAroundPivot(degrees, r.center);
		DrawSprite(r, sprite, tint);
		GUI.matrix = m;
	}

	// A wind rose by the stamina bar, like the game's ship HUD: the wind's arrow (brighter the
	// better it fills the sail), the ship's heading, and the sail setting.
	private void SeaHud(float k, float w, float h)
	{
		if (!m_hudSprites)
		{
			FindShipHud();
		}
		float size = 150f * k;
		Rect rose = new Rect(w - size - 40f * k, h - size - 40f * k, size, size);
		if (m_hudCompass != null)
		{
			DrawSprite(rose, m_hudCompass, new Color(1f, 1f, 1f, 0.85f));
		}
		else
		{
			Fill(new Rect(rose.x + size * 0.15f, rose.y + size * 0.15f, size * 0.7f, size * 0.7f), new Color(0f, 0f, 0f, 0.35f));
		}
		float factor = WindAngleFactor();
		float pulse = 1f + 0.25f * m_windShift * Mathf.Abs(Mathf.Sin(Time.time * 10f));
		Color windTint = Color.Lerp(new Color(0.25f, 0.25f, 0.25f), Color.white, factor);
		float ws = size * 0.6f * pulse;
		Rect windRect = new Rect(rose.center.x - ws * 0.5f, rose.center.y - ws * 0.5f, ws, ws);
		if (m_hudWind != null)
		{
			// The game's icon points the way the wind blows when unturned.
			DrawRotated(windRect, m_hudWind, m_windAngle, windTint);
		}
		else
		{
			Matrix4x4 m = GUI.matrix;
			GUIUtility.RotateAroundPivot(m_windAngle, rose.center);
			Fill(new Rect(rose.center.x - 4f * k, rose.center.y - ws * 0.45f, 8f * k, ws * 0.9f), windTint);
			Star(new Vector2(rose.center.x, rose.center.y - ws * 0.45f), 18f * k, windTint);
			GUI.matrix = m;
		}
		if (m_hudShip != null)
		{
			float ss = size * 0.34f;
			DrawRotated(new Rect(rose.center.x - ss * 0.5f, rose.center.y - ss * 0.5f, ss, ss), m_hudShip, m_heading, Color.white);
		}
		else
		{
			Matrix4x4 m = GUI.matrix;
			GUIUtility.RotateAroundPivot(m_heading, rose.center);
			Fill(new Rect(rose.center.x - 5f * k, rose.center.y - size * 0.2f, 10f * k, size * 0.4f), new Color(0.9f, 0.75f, 0.45f));
			GUI.matrix = m;
		}
		Sprite? sail = m_sail == Sail.Full ? m_hudFull : m_sail == Sail.Half ? m_hudHalf : m_hudOars;
		Rect sailRect = new Rect(rose.x - 70f * k, rose.yMax - 64f * k, 56f * k, 56f * k);
		if (sail != null)
		{
			DrawSprite(sailRect, sail, Color.white);
		}
		else
		{
			for (int i = 0; i <= (int)m_sail; i++)
			{
				Fill(new Rect(sailRect.x + i * 18f * k, sailRect.yMax - (i + 1) * 16f * k, 14f * k, (i + 1) * 16f * k), new Color(0.95f, 0.9f, 0.8f, 0.85f));
			}
		}
		// The ballista's reload.
		if (m_reload > 0f)
		{
			Bar(new Rect(rose.x - 70f * k, rose.yMax - 4f * k, 56f * k, 6f * k), 1f - m_reload / BallistaReload, new Color(0.9f, 0.85f, 0.7f));
		}
		// The keys, small above the rose like the game's key hints, for the first moments aboard.
		float hint = Mathf.Clamp01((16f - m_seaTime) / 3f);
		if (hint > 0f && m_small != null)
		{
			TextAnchor anchor = m_small.alignment;
			Color text = m_small.normal.textColor;
			m_small.alignment = TextAnchor.LowerRight;
			Color old = GUI.color;
			GUI.color = new Color(1f, 1f, 1f, hint);
			Shadowed(new Rect(w - 760f * k, rose.y - 110f * k, 720f * k, 100f * k),
				$"A / D: steer     W / S: sail\n{KeyName(Plugin.ThrowKey.Value)}: ballista     {KeyName(Plugin.BlockKey.Value)}: block", m_small);
			GUI.color = old;
			m_small.alignment = anchor;
			m_small.normal.textColor = text;
		}
	}
}
