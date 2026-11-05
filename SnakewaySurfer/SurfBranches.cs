using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// The Elder's branching paths: root walls split the track into its three lanes for a stretch
// (you can't change lane inside), then they merge again. Some branches end in a root wall; it
// can be seen from the start, so look ahead. A blocked branch may have a torch under a low
// root: slide under it and the wall ahead burns away.
internal sealed partial class SurfGame
{
	private readonly List<(float z0, float z1, int boundary)> m_dividers = new();
	private int m_branchGroup;

	// Lanes are split by root walls here? (boundary 0 is between lanes 0 and 1, 1 between 1 and 2.)
	private bool CanSwitch(int from, int to)
	{
		int boundary = Mathf.Min(from, to);
		for (int i = m_dividers.Count - 1; i >= 0; i--)
		{
			(float z0, float z1, int b) = m_dividers[i];
			if (z1 < m_z - 30f)
			{
				m_dividers.RemoveAt(i);
				continue;
			}
			if (b == boundary && m_z >= z0 - 0.4f && m_z <= z1 + 0.2f)
			{
				m_audio.Play(Sfx.HitLog, 0.5f, 1.3f);
				m_shake = Mathf.Max(m_shake, 0.08f);
				return false;
			}
		}
		return true;
	}

	private void SpawnBranches(BossDef def, BiomeRuntime b, int sections)
	{
		float length = Mathf.Max(30f, m_speed * 3.2f);
		const float gap = 12f;
		float z = AheadZ(2.2f);
		float total = 0f;
		for (int s = 0; s < sections; s++)
		{
			int group = ++m_branchGroup;
			float zs = z + s * (length + gap);
			float ze = zs + length;
			total = ze - z;
			foreach (int boundary in new[] { 0, 1 })
			{
				m_dividers.Add((zs, ze, boundary));
				float x = (boundary - 0.5f) * LaneWidth;
				// 1.14.2 (Sam): the road splits into separate paths with a strip of wild ground
				// between them, overgrown with the Elder's tangled vines (no more logs).
				GroundStrip(b, x, zs - 0.5f, ze + 0.5f, 0.8f);
				bool vines = false;
				for (float pz = zs; pz < ze; pz += 1.6f)
				{
					Quaternion lean = Quaternion.Euler(Random.Range(-22f, 22f), Random.Range(0f, 360f), Random.Range(-22f, 22f));
					vines |= VinePiece(def, new Vector3(x + Random.Range(-0.2f, 0.2f), 0f, pz + Random.Range(-0.5f, 0.5f)), Random.Range(1.2f, 2.3f), lean) != null;
				}
				if (!vines)
				{
					for (float pz = zs; pz < ze - 0.1f; pz += 6f)
					{
						float piece = Mathf.Min(6f, ze - pz);
						RootPiece(b, def, new Vector3(x, 0f, pz + piece * 0.5f), new Vector3(0.45f, 1.3f, piece + 0.3f), Quaternion.Euler(90f, 0f, 0f));
					}
				}
			}
			// One or two open branches; each blocked one has a torch under a root more often than not.
			List<int> lanes = new() { 0, 1, 2 };
			int open = Random.value < 0.5f ? 1 : 2;
			List<int> openLanes = new();
			for (int k = 0; k < open; k++)
			{
				int pick = lanes[Random.Range(0, lanes.Count)];
				lanes.Remove(pick);
				openLanes.Add(pick);
			}
			foreach (int lane in lanes)
			{
				float wz = ze - 1.5f;
				// A thicket of vines closing the path.
				GameObject wall = new GameObject("VineWall");
				wall.layer = m_layer;
				wall.transform.SetParent(m_stage, false);
				wall.transform.localPosition = new Vector3(LaneX(lane), RoadY(wz), wz);
				int grown = 0;
				for (int k = 0; k < 4; k++)
				{
					GameObject? v = VinePiece(def, new Vector3(LaneX(lane) + (k - 1.5f) * LaneWidth * 0.27f, 0f, wz + Random.Range(-0.3f, 0.3f)), Random.Range(2.3f, 3f),
						Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), (k - 1.5f) * -9f), false);
					if (v != null)
					{
						v.transform.SetParent(wall.transform, true);
						grown++;
					}
				}
				if (grown == 0)
				{
					Destroy(wall);
					wall = RootPiece(b, def, new Vector3(LaneX(lane), 0f, wz), new Vector3(LaneWidth * 0.9f, 2.6f, 1f), Quaternion.Euler(0f, 0f, 90f), false);
				}
				m_things.Add(new Thing
				{
					Go = wall,
					Kind = Kind.Block,
					Lane = lane,
					Z = wz,
					HalfLength = 0.6f,
					Group = group,
					Label = def.Name,
					CrashText = $"{def.Name}'s roots closed that path! (Look ahead, or slide under a torch to burn them.)",
				});
				if (Random.value < 0.6f)
				{
					SpawnTorchRoot(b, lane, zs + length * 0.35f, group, def);
				}
			}
			// The last open path carries something to throw at the Elder.
			if (s == sections - 1 && ItemTemplate(def.HitItem) is GameObject hit)
			{
				SpawnPickup(hit, openLanes[0], zs + length * 0.6f, Kind.BossHit);
			}
		}
		m_banner = "The roots split the path! Pick a branch.";
		m_bannerTime = 2f;
		// No other attacks until you're through.
		float seconds = (total + (z - m_z)) / Mathf.Max(1f, m_speed);
		m_bossAttackTimer = Mathf.Max(m_bossAttackTimer, seconds + 0.6f);
		m_bossHitTimer = Mathf.Max(m_bossHitTimer, seconds + 3f);
	}

	// A log (the biome's own) or a tinted bar standing in for a root, on the road at local x/z.
	private GameObject RootPiece(BiomeRuntime b, BossDef def, Vector3 at, Vector3 size, Quaternion rotation, bool scenery = true)
	{
		GameObject go;
		if (b.Logs.Count > 0)
		{
			go = Fitted(Picture(b.Logs[Random.Range(0, b.Logs.Count)]), rotation, size, false);
		}
		else
		{
			go = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Destroy(go.GetComponent<Collider>());
			go.layer = m_layer;
			go.transform.SetParent(m_stage, false);
			go.transform.localScale = size;
			go.GetComponent<Renderer>().sharedMaterial = TintMaterial(def.Tint);
			at.y += size.y * 0.5f;
		}
		go.transform.localPosition = new Vector3(at.x, RoadY(at.z) + at.y, at.z);
		if (scenery)
		{
			m_things.Add(new Thing { Go = go, Kind = Kind.Scenery, Z = at.z, HalfLength = size.z * 0.5f });
		}
		return go;
	}

	// A low root to slide under, with a burning torch beneath it.
	private void SpawnTorchRoot(BiomeRuntime b, int lane, float z, int group, BossDef def)
	{
		// A vine arching across the path at head height (1.14.2, Sam: vines, not logs), its ends
		// rooted beside the path.
		GameObject root = new GameObject("TorchVine");
		root.layer = m_layer;
		root.transform.SetParent(m_stage, false);
		root.transform.localPosition = new Vector3(LaneX(lane), RoadY(z) + 1.3f, z);
		bool vine = false;
		foreach (float side in new[] { -1f, 1f })
		{
			// Two vines leaning in from each side, meeting over the middle.
			GameObject? v = VinePiece(def, new Vector3(LaneX(lane) + side * LaneWidth * 0.5f, 0f, z + side * 0.15f), 2.3f,
				Quaternion.Euler(0f, Random.Range(0f, 360f), side * 58f), false);
			if (v != null)
			{
				v.transform.SetParent(root.transform, true);
				vine = true;
			}
		}
		if (!vine)
		{
			GameObject bar = RootPiece(b, def, new Vector3(LaneX(lane), 1.3f, z), new Vector3(LaneWidth, 0.6f, 0.6f), Quaternion.Euler(0f, 0f, 90f), false);
			bar.transform.SetParent(root.transform, true);
		}
		GameObject? torch = Pictures.Prefab("piece_groundtorch_wood");
		if (torch != null)
		{
			GameObject t = Instantiate(Picture(torch), root.transform, true);
			t.transform.position = root.transform.position - m_stage.up * 1.3f;
			t.transform.rotation = m_stage.rotation;
			LightUp(t);
		}
		Light glow = new GameObject("TorchGlow").AddComponent<Light>();
		glow.gameObject.layer = m_layer;
		glow.transform.SetParent(root.transform, false);
		glow.transform.position = root.transform.position + new Vector3(0f, -0.6f, 0f);
		glow.type = LightType.Point;
		glow.range = 5f;
		glow.intensity = 1.8f;
		glow.color = new Color(1f, 0.6f, 0.25f);
		glow.cullingMask = 1 << m_layer;
		m_things.Add(new Thing
		{
			Go = root,
			Kind = Kind.High,
			Lane = lane,
			Z = z,
			HalfLength = 0.35f,
			Torch = true,
			Group = group,
			Label = def.Name,
			CrashText = $"You ran into {def.Name}'s root! (Slide under it to grab the torch.)",
		});
	}

	// Slid under a torch: the root wall at the end of this branch burns away.
	private void BurnBranch(Thing torch)
	{
		bool burned = false;
		for (int i = m_things.Count - 1; i >= 0; i--)
		{
			Thing t = m_things[i];
			if (t.Kind == Kind.Block && t.Group == torch.Group && t.Lane == torch.Lane)
			{
				GameObject? fire = FindFx("vfx_FireAddFuel|vfx_fireball_hit|fx_fireball_staff_explosion|piece_groundtorch_wood");
				if (fire != null)
				{
					GameObject f = Instantiate(Picture(fire), m_stage, false);
					f.transform.localPosition = t.Go.transform.localPosition;
					LightUp(f);
					m_things.Add(new Thing { Go = f, Kind = Kind.Scenery, Z = t.Z, HalfLength = 1f, Life = 4f });
				}
				DestroyThing(t);
				m_things.RemoveAt(i);
				burned = true;
			}
		}
		m_audio.Play(Sfx.Coin, 1f, 0.6f);
		m_banner = burned ? "You grab the torch: the roots ahead burn away!" : "You grab a torch.";
		m_bannerTime = 1.6f;
	}

	// One of the Elder's vines (its TentaRoot), standing still at a local spot, about `height`
	// tall. Null if the boss has no vines.
	private GameObject? VinePiece(BossDef def, Vector3 at, float height, Quaternion rotation, bool scenery = true)
	{
		GameObject? prefab = def.Vines.Length > 0 ? Pictures.Prefab(def.Vines) : null;
		if (prefab == null)
		{
			return null;
		}
		CreatureInfo info = CreatureInfo.Of(prefab);
		float scale = info.Height > 0.3f ? Mathf.Clamp(height / info.Height, 0.15f, 3f) : height / 2.6f;
		GameObject go = Creature(prefab, rotation, scale);
		go.transform.localPosition = new Vector3(at.x, RoadY(at.z) + at.y - 0.15f, at.z);
		if (scenery)
		{
			m_things.Add(new Thing { Go = go, Kind = Kind.Scenery, Z = at.z, HalfLength = 1f });
		}
		return go;
	}

	// A strip of the land's own ground laid over the road, splitting it into separate paths.
	private void GroundStrip(BiomeRuntime b, float x, float z0, float z1, float width)
	{
		for (float z = z0; z < z1; z += 4f)
		{
			float len = Mathf.Min(4.2f, z1 - z + 0.2f);
			GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
			Destroy(quad.GetComponent<Collider>());
			quad.name = "PathDivide";
			quad.layer = m_layer;
			quad.transform.SetParent(m_stage, false);
			float zc = z + len * 0.5f;
			float slope = Mathf.Atan2(RoadY(zc + 1f) - RoadY(zc - 1f), 2f) * Mathf.Rad2Deg;
			quad.transform.localPosition = new Vector3(x, RoadY(zc) + 0.05f, zc);
			quad.transform.localRotation = Quaternion.Euler(90f - slope, 0f, 0f);
			quad.transform.localScale = new Vector3(width, len, 1f);
			Renderer r = quad.GetComponent<Renderer>();
			r.sharedMaterial = b.Ground;
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			m_things.Add(new Thing { Go = quad, Kind = Kind.Scenery, Z = zc, HalfLength = len * 0.5f });
		}
	}
}
