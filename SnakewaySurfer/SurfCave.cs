using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.6: the Mountains' dungeon, a frost cave. The same halls and corners as the crypts
// (SurfCrypt.cs), but hewn out of the mountain: rough rock walls, crystals glowing cold blue at
// their foot instead of torches, ulvs and fenrings in the dark, cultists breathing fire down the
// lanes, bats, and icicles that crack loose from the roof over your lane as you come (jump the
// spike, or get out from under it).
internal sealed partial class SurfGame
{
	// ---------------------------------------------------------------- rock walls

	// Rough rock along a line, as a cave's wall: overlapping boulders, each pushed out by about
	// half its size so the hall keeps its width. `over` is a lintel over an opening.
	private void RockLine(Transform root, Vector3 from, Vector3 to, Vector3 away, int rows, bool over = false)
	{
		string[] names = m_crypt!.Theme.Walls.Split('|');
		float length = Vector3.Distance(from, to);
		int n = Mathf.Max(1, Mathf.CeilToInt(length / 1.7f));
		for (int i = 0; i <= n; i++)
		{
			Vector3 at = Vector3.Lerp(from, to, i / (float)n);
			float size = Random.Range(2f, 2.8f);
			float height = over ? Random.Range(1.4f, 2f) : WallH * rows + Random.Range(-0.3f, 0.9f);
			if (!over)
			{
				at += away * (size * 0.5f - 0.35f);
			}
			// Any of the rocks there are, at random.
			int start = Random.Range(0, names.Length);
			for (int k = 0; k < names.Length; k++)
			{
				GameObject? template = CryptTemplate(names[(start + k) % names.Length]);
				if (template == null)
				{
					continue;
				}
				GameObject rock = Fitted(Picture(template), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), new Vector3(size, height, over ? size * 0.8f : size), false);
				rock.transform.SetParent(root, false);
				rock.transform.localPosition = at + Vector3.down * (over ? 0f : Random.Range(0f, 0.25f));
				break;
			}
		}
	}

	// ---------------------------------------------------------------- crystals

	// A cluster of crystals glowing at the foot of a wall (or the end wall of a corner, to say
	// "turn here"), with a cold light that's only on near you.
	private void GlowCrystal(Transform root, Vector3 at, float yaw, float range)
	{
		CryptTheme theme = m_crypt!.Theme;
		Vector3 facing = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
		Vector3 foot = new Vector3(at.x, m_crypt.Floor, at.z) + facing * 0.45f;
		GameObject? go = CryptPiece(root, theme.Sconce, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), theme.SconceSize, foot, true, 2.2f);
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
		glow.transform.localPosition = foot + facing * 0.4f + Vector3.up * (theme.SconceSize.y * 0.7f);
		glow.type = LightType.Point;
		glow.range = range;
		glow.intensity = theme.SconceIntensity;
		glow.color = theme.SconceColor;
		glow.shadows = LightShadows.None;
		glow.cullingMask = 1 << m_layer;
		glow.enabled = false;
		m_crypt.Glows.Add((glow, at.z));
	}

	// ---------------------------------------------------------------- icicles

	private sealed class Icicle
	{
		public Thing T = null!;
		public GameObject? Ice;
		public bool Dropped;
		public float Fall;
	}

	private readonly List<Icicle> m_icicles = new();
	private const float IcicleTop = 1.2f;

	// A spike of ice up in the roof over a lane, out of sight. As you come it cracks loose over
	// the lane you're in (if that lane is clear there) and falls, and stays standing in the floor
	// to be jumped.
	private bool SpawnIcicle(int lane, float z)
	{
		GameObject? template = null;
		foreach (string name in m_crypt!.Theme.Icicle.Split('|'))
		{
			template = CryptTemplate(name);
			if (template != null)
			{
				break;
			}
		}
		GameObject holder = new GameObject("Icicle");
		holder.layer = m_layer;
		holder.transform.SetParent(m_stage, false);
		holder.transform.localPosition = new Vector3(LaneX(lane), RoadY(z), z);
		GameObject ice;
		if (template != null)
		{
			// A stalactite as it hangs, point down.
			ice = Fitted(Picture(template), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), new Vector3(LaneWidth * 0.55f, 1.25f, 0.8f), false);
		}
		else
		{
			ice = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Destroy(ice.GetComponent<Collider>());
			ice.layer = m_layer;
			ice.transform.localScale = new Vector3(0.35f, 1.25f, 0.35f);
			ice.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
			ice.GetComponent<Renderer>().sharedMaterial = SolidTint(new Color(0.7f, 0.88f, 1f));
		}
		ice.transform.SetParent(holder.transform, false);
		ice.transform.localPosition = new Vector3(0f, IcicleTop + WallH, 0f);
		ice.SetActive(false);
		Thing t = new Thing
		{
			Go = holder,
			Kind = Kind.Low,
			Lane = lane,
			Z = z,
			HalfLength = 0.35f,
			Collidable = false,
			CrashText = m_crypt.Theme.FallText,
		};
		m_things.Add(t);
		m_icicles.Add(new Icicle { T = t, Ice = ice });
		return true;
	}

	// A lane is clear at z if nothing that blocks stands within a few metres of it there.
	private bool LaneClear(int lane, float z, Thing except)
	{
		foreach (Thing o in m_things)
		{
			if (o == except || o.Lane != lane || !o.Collidable || o.Kind == Kind.Ground || o.Kind == Kind.Scenery || o.Kind == Kind.Coin || o.Kind == Kind.Item)
			{
				continue;
			}
			if (Mathf.Abs(o.Z - z) < 5f)
			{
				return false;
			}
		}
		return true;
	}

	private void UpdateIcicles(float dt)
	{
		for (int i = m_icicles.Count - 1; i >= 0; i--)
		{
			Icicle ic = m_icicles[i];
			Thing t = ic.T;
			if (t.Go == null || ic.Ice == null)
			{
				m_icicles.RemoveAt(i);
				continue;
			}
			if (!ic.Dropped)
			{
				if (t.Z > m_z && t.Z - m_z < m_speed * 1.25f + 5f && !CornerBetween(m_z + 1f, t.Z))
				{
					ic.Dropped = true;
					if (m_lane != t.Lane && LaneClear(m_lane, t.Z, t))
					{
						t.Lane = m_lane;
						Vector3 p = t.Go.transform.localPosition;
						t.Go.transform.localPosition = new Vector3(LaneX(t.Lane), p.y, p.z);
					}
					ic.Ice.SetActive(true);
					t.Markers.Add(MakeMarker(t.Lane, t.Z, 1.6f, JumpMaterial));
					m_audio.Play(m_crypt != null ? m_crypt.Theme.CrackSound : "sfx_rock_destroyed", 0.85f, Random.Range(1.05f, 1.25f));
					GameObject? dust = FindFx(m_crypt != null ? m_crypt.Theme.CrackFx : "vfx_RockHit");
					if (dust != null)
					{
						PlayFx(dust, t.Go.transform.localPosition + Vector3.up * (WallH + 0.5f), Quaternion.identity, 2f);
					}
				}
				continue;
			}
			if (ic.Fall < 1f)
			{
				ic.Fall += dt / 0.5f;
				float k = Mathf.Clamp01(ic.Fall);
				// Down from the roof, faster and faster, its point sinking into the floor.
				float y = Mathf.Lerp(IcicleTop + WallH, -0.5f, k * k);
				ic.Ice.transform.localPosition = new Vector3(0f, y, 0f);
				if (k >= 1f)
				{
					t.Collidable = true;
					m_audio.Play(m_crypt != null ? m_crypt.Theme.ShatterSound : "sfx_rock_hit", 1f, Random.Range(0.9f, 1.1f));
					GameObject? shards = FindFx(m_crypt != null ? m_crypt.Theme.ShatterFx : "vfx_RockHit");
					if (shards != null)
					{
						PlayFx(shards, t.Go.transform.localPosition + Vector3.up * 0.3f, Quaternion.identity, 2f);
					}
					m_shake = Mathf.Max(m_shake, 0.1f);
				}
			}
		}
	}
}
