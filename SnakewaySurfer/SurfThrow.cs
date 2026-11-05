using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// 1.14 (Sam): hurting a boss takes timing. Eikthyr: grab a spear and throw it (Throw Key) when
// he's lined up with you; it flies down your lane and he weaves between the lanes, so a badly
// timed throw misses. Moder's ballista bolt (SurfBoss.cs) flies too, so you lead her.
internal sealed partial class SurfGame
{
	private sealed class Spear
	{
		public GameObject Go = null!;
		public float X;
		public float Z;
		public float Y;
		public float Dir;
	}

	private int m_spears;
	private readonly List<Spear> m_flying = new();
	private const float SpearSpeed = 32f;

	private void ResetSpears()
	{
		m_spears = 0;
		foreach (Spear s in m_flying)
		{
			if (s.Go != null)
			{
				Destroy(s.Go);
			}
		}
		m_flying.Clear();
		if (m_bolt != null)
		{
			Destroy(m_bolt);
			m_bolt = null;
		}
	}

	private void PickUpSpear()
	{
		m_spears = Mathf.Min(m_spears + 1, 2);
		m_iconPulse = 1f;
		m_audio.Play("sfx_equip_start|sfx_pickable_pick", 1f, 0.9f);
	}

	private void UpdateSpears(float dt, bool canInput)
	{
		if (m_spears > 0 && canInput && m_boss != null && Input.GetKeyDown(Plugin.ThrowKey.Value))
		{
			ThrowSpear();
		}
		for (int i = m_flying.Count - 1; i >= 0; i--)
		{
			Spear s = m_flying[i];
			if (s.Go == null || m_boss == null || m_bossGo == null)
			{
				if (s.Go != null)
				{
					Destroy(s.Go);
				}
				m_flying.RemoveAt(i);
				continue;
			}
			// It keeps your forward speed and flies on from there.
			s.Z += (m_speed + s.Dir * SpearSpeed) * dt;
			float drop = Mathf.Clamp01(Mathf.Abs(s.Z - m_z) / 40f);
			s.Go.transform.localPosition = new Vector3(s.X, RoadY(s.Z) + s.Y - drop * 0.6f, s.Z);
			float bossZ = m_bossGo.transform.localPosition.z;
			if ((bossZ - s.Z) * s.Dir > 0f)
			{
				continue;
			}
			float bossX = m_bossGo.transform.localPosition.x;
			bool hit = Mathf.Abs(bossX - s.X) < 1.5f && m_bossHidden <= 0f && m_bossGo.activeInHierarchy;
			Destroy(s.Go);
			m_flying.RemoveAt(i);
			if (hit)
			{
				m_audio.Play(Sfx.HitMonster, 1f, 0.8f);
				BossHit();
				return;
			}
			m_audio.Play(Sfx.Miss, 0.8f, 0.9f);
		}
	}

	private void ThrowSpear()
	{
		m_spears--;
		float dir = m_bossGo != null && m_bossGo.transform.localPosition.z < m_z ? -1f : 1f;
		GameObject? prefab = null;
		foreach (string name in m_boss!.HitItem.Split('|'))
		{
			prefab = Pictures.Item(name.Trim());
			if (prefab != null)
			{
				break;
			}
		}
		GameObject go;
		if (prefab != null)
		{
			// Point the spear's long side down the track: fit it once to see which way it lies.
			Quaternion turn = Quaternion.identity;
			GameObject probe = Fitted(Picture(prefab), Quaternion.identity, new Vector3(2.2f, 2.2f, 2.2f), true, 3f);
			if (Pictures.MeshBounds(probe, out Bounds b))
			{
				Vector3 size = b.size;
				if (size.x >= size.y && size.x >= size.z)
				{
					turn = Quaternion.Euler(0f, 90f, 0f);
				}
				else if (size.y >= size.x && size.y >= size.z)
				{
					turn = Quaternion.Euler(90f, 0f, 0f);
				}
			}
			if (turn == Quaternion.identity)
			{
				go = probe;
			}
			else
			{
				Destroy(probe);
				go = Fitted(Picture(prefab), turn, new Vector3(2.2f, 2.2f, 2.2f), true, 3f);
			}
		}
		else
		{
			go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
			Destroy(go.GetComponent<Collider>());
			go.transform.SetParent(m_stage, false);
			go.transform.localScale = new Vector3(0.12f, 1.1f, 0.12f);
			go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
		}
		Pictures.SetLayer(go, m_layer);
		Spear s = new Spear { Go = go, X = m_x, Z = m_z + dir * 0.6f, Y = m_y + 1.6f, Dir = dir };
		go.transform.localPosition = new Vector3(s.X, RoadY(s.Z) + s.Y, s.Z);
		m_flying.Add(s);
		SetTrigger("attack_throw");
		m_audio.Play("sfx_spear_throw|sfx_spear_swing|sfx_club_swing", 1f, 1f);
	}
}
