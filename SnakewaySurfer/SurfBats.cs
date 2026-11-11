using UnityEngine;

namespace SnakewaySurfer;

// Bats (1.14, SAT): always a swarm, and more of a nuisance than a danger. A cloud of them
// flutters about two lanes at head height; flying into one costs no life, but it slows you,
// takes some stamina, flaps in your face and knocks your guard down (1.16.1). Roll under them,
// or pick the lane they're not in.
internal sealed partial class SurfGame
{
	private static bool IsBat(GameObject prefab) => prefab.name.StartsWith("Bat", System.StringComparison.OrdinalIgnoreCase);

	private void SpawnBatSwarm(SpawnEntry e, int lane, float z, int biomeIndex)
	{
		int other = lane == 1 ? (Random.value < 0.5f ? 0 : 2) : 1;
		int count = Random.Range(6, 10);
		for (int k = 0; k < count; k++)
		{
			int l = Random.value < 0.55f ? lane : other;
			Thing t = SpawnCreature(e, l, z + Random.Range(-1f, 7f), Kind.High, Random.Range(1.25f, 2.1f), biomeIndex);
			t.Nuisance = true;
			t.Swoop = false;
			t.Chaser = false;
			t.FreeX = true;
			t.X = LaneX(l);
			t.Seed = Random.value * 20f;
			t.HalfLength = 0.3f;
		}
		if (e.Prefab != null && CreatureInfo.Of(e.Prefab).AlertSound is string sound && sound.Length > 0)
		{
			m_audio.Play(sound, 0.6f);
		}
	}

	// Flitting about its lane, never still.
	private void UpdateBat(Thing t, float dt)
	{
		float time = Time.time * 2.6f + t.Seed;
		t.X = LaneX(t.Lane) + Mathf.Sin(time) * LaneWidth * 0.45f + Mathf.Sin(time * 2.3f) * 0.3f;
		float y = t.BaseY + Mathf.Sin(time * 1.7f) * 0.35f;
		t.Go.transform.localPosition = new Vector3(t.X, RoadY(t.Z) + y, t.Z);
		t.Go.transform.localRotation = Quaternion.Euler(0f, 180f + Mathf.Cos(time) * 50f, 0f);
	}

	private void BatHit(Thing t)
	{
		t.Collidable = false;
		m_slowTime = Mathf.Max(m_slowTime, 0.7f);
		m_stamina = Mathf.Max(0f, m_stamina - 15f);
		m_staminaIdle = 0f;
		m_shake = Mathf.Max(m_shake, 0.12f);
		Flash(new Color(0.12f, 0.08f, 0.1f), 0.45f);
		// 1.16.1 (SAT): flapping in your face, they knock your guard down; it comes back up a
		// moment after (hold block again).
		if (m_blocking)
		{
			m_audio.Play("sfx_unarmed_blocked|sfx_wood_blocked", 0.7f, 1.2f);
		}
		m_guardDown = Mathf.Max(m_guardDown, 1.2f);
		m_blocking = false;
		AvatarBool("blocking", false);
		string sound = t.Info != null && t.Info.AlertSound.Length > 0 ? t.Info.AlertSound : Sfx.HitMonster;
		m_audio.Play(sound, 0.9f, 1.15f);
	}
}
