using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.3 (Sam: "The serpent stops to perform its breath attack"): a serpent swims under the
// water to a spot well ahead of the ship and a little off its course, comes up there and stops
// dead. It rears and turns with the ship while a wide red streak runs down the water from its
// jaws toward you, then blows a jet of freezing spray down the streak. Steer out of the streak
// before it blows (a block takes the spray, as it does a bite). Serpents take turns between the
// breath and the lunge; the serpent of the deep breathes too, wider.
internal sealed partial class SurfGame
{
	private const float BreathTime = 1.1f;

	// Where a serpent comes up to breathe: ahead of the ship and a little to one side.
	private Vector2 BreathSpot(SeaFoe f, float ahead)
	{
		return ShipCenter() + HeadingVector() * (ahead + m_boatSpeed * 0.5f) + new Vector2(f.Side * 4.5f, 0f);
	}

	private float BreathWindup(SeaFoe f) => f.Boss ? Mathf.Max(1.2f, 1.6f - 0.1f * SeaStars) : Mathf.Max(1f, 1.4f - 0.1f * SeaStars);

	// Up out of the water where it is, facing the ship.
	private void StartBreath(SeaFoe f)
	{
		Vector2 ship = ShipCenter();
		Vector2 shipVel = HeadingVector() * m_boatSpeed;
		f.Phase = 4;
		f.T = 0f;
		f.Struck = false;
		// (Wait counts how far down the streak the spray has been laid, once it blows.)
		f.Wait = -1f;
		f.Dir = Vector2.zero;
		f.Aim = ship + shipVel * (BreathWindup(f) + 0.35f);
		f.Facing = Angle2(f.Aim - f.P);
		SpawnSplash(f.P, f.Boss ? 2f : 1.1f);
		m_audio.Play("sfx_serpent_taunt|sfx_serpent_alerted", 1f, f.Boss ? 0.75f : 0.95f);
		TriggerWatched(f.Anim, "taunt");
	}

	// 0 still breathing, 1 done, 2 parried (it reels).
	private int UpdateBreath(SeaFoe f, float dt, string why)
	{
		Vector2 ship = ShipCenter();
		Vector2 shipVel = HeadingVector() * m_boatSpeed;
		float windup = BreathWindup(f);
		float width = f.Boss ? 7f : 5f;
		// Stopped dead, reared up out of the water: the ship comes on toward it.
		f.Dir = Vector2.zero;
		f.Y = Mathf.MoveTowards(f.Y, f.Boss ? 0f : -0.1f, dt * 5f);
		if (f.T < windup * 0.75f)
		{
			// It turns with the ship until just before it blows.
			f.Aim = ship + shipVel * Mathf.Max(0f, windup - f.T + 0.35f);
			f.Facing = Mathf.MoveTowardsAngle(f.Facing, Angle2(f.Aim - f.P), 120f * dt);
		}
		Vector2 dir = Dir2(f.Facing);
		Vector2 mouth = f.P + dir * (f.Length * 0.3f);
		// Down to the ship and on past it, so slowing down doesn't get you out of it.
		float reach = Mathf.Clamp(Vector2.Distance(mouth, f.Aim) + 12f, 16f, 42f);
		SerpentMarker(f, mouth, mouth + dir * reach, width);
		float blow = f.T - windup;
		if (blow < 0f)
		{
			// It blinks just before it blows, as the lunge's does.
			f.Markers[0].SetActive(f.T < windup - 0.35f || Mathf.FloorToInt(f.T * 12f) % 2 == 0);
			return 0;
		}
		f.Markers[0].SetActive(true);
		if (f.Wait < 0f)
		{
			f.Wait = 0f;
			TriggerWatched(f.Anim, f.Attack?.Animation ?? "attack");
			AttackSound(f.Attack, false, 1f);
			m_audio.Play("sfx_dragon_coldbreath_start", 0.9f, f.Boss ? 0.7f : 0.85f);
			m_audio.Play("sfx_ship_waterimpact|sfx_serpent_attack", 0.8f, 0.8f);
			m_shake = Mathf.Max(m_shake, f.Boss ? 0.3f : 0.15f);
		}
		// The jet runs out down the streak in a moment and lies on it until the breath is spent.
		float jet = reach * Mathf.Clamp01(blow / 0.3f);
		Vector2 across = new Vector2(dir.y, -dir.x);
		GameObject? cold = FindFx("vfx_dragon_coldbreath");
		while (f.Wait <= jet)
		{
			Vector2 at = mouth + dir * f.Wait;
			if (cold != null && Mathf.RoundToInt(f.Wait / 4f) % 3 == 0)
			{
				float y = SeaHeight(at.x, at.y) + (f.Wait < 1f ? 1.4f : 0.6f);
				PlayFx(cold, new Vector3(at.x, y, at.y), Quaternion.Euler(0f, f.Facing, 0f), Mathf.Max(0.5f, BreathTime - blow) + 0.8f, null, f.Boss ? 1.6f : 1.2f);
			}
			SpawnSplash(at + across * Random.Range(-0.3f, 0.3f) * width, f.Boss ? 1.3f : 0.9f);
			AddFoam(at + across * Random.Range(-0.4f, 0.4f) * width, Random.Range(0.8f, 1.4f) * width * 0.3f, 1.4f, 0.6f, 2.2f);
			f.Wait += 4f;
		}
		if (!f.Struck)
		{
			for (float d = 0f; d <= jet; d += 1.5f)
			{
				if (ShipDistance(mouth + dir * d) < width * 0.5f)
				{
					f.Struck = true;
					if (SeaHit(f, false, why) == 2)
					{
						HideMarkers(f);
						return 2;
					}
					break;
				}
			}
		}
		if (blow >= BreathTime)
		{
			HideMarkers(f);
			return 1;
		}
		return 0;
	}

	// A swimming serpent's body swings from side to side (PlaceFoe).
	private float SerpentSwing(SeaFoe f) =>
		!f.Dead && (f.Kind == "serpent" || f.Kind == "boss") && f.Phase == 0 ? Mathf.Sin(m_seaClock * 4.5f + f.Bob) * 8f : 0f;
}
