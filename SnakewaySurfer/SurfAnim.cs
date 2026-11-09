using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// When an attack animation actually lands its blow. In the game a clip calls OnAttackTrigger
// (an animation event) at the moment of the hit, and that's when the damage happens. Our
// creature copies don't fire events, so we read the clips: the event's time in the clip that
// plays after the trigger is how long the wind-up takes. Learned per creature and attack, so
// a blow, shockwave or shot can start that long before it should land.
internal static class AnimTiming
{
	private const string HitEvent = "OnAttackTrigger";
	private static readonly Dictionary<string, float> s_delay = new();
	private static readonly Dictionary<AnimationClip, float> s_hitTime = new();
	private static readonly HashSet<string> s_reported = new();

	private static string Key(Animator anim, string trigger)
	{
		RuntimeAnimatorController? c = anim.runtimeAnimatorController;
		return (c != null ? c.name : anim.gameObject.name) + "|" + trigger;
	}

	// Seconds from the clip's start to its blow (the first OnAttackTrigger), or -1.
	public static float HitTime(AnimationClip? clip)
	{
		if (clip == null)
		{
			return -1f;
		}
		if (!s_hitTime.TryGetValue(clip, out float t))
		{
			t = -1f;
			foreach (AnimationEvent e in clip.events)
			{
				if (e.functionName == HitEvent && (t < 0f || e.time < t))
				{
					t = e.time;
				}
			}
			s_hitTime[clip] = t;
		}
		return t;
	}

	// The wind-up of this attack on this creature: learned if it has played before, else a
	// guess from the clip names (a trigger "attack_slam" and a clip "Troll_attack_slam"), else
	// the fallback.
	public static float Delay(Animator? anim, string trigger, float fallback)
	{
		if (anim == null || string.IsNullOrEmpty(trigger))
		{
			return fallback;
		}
		string key = Key(anim, trigger);
		if (s_delay.TryGetValue(key, out float d))
		{
			return d;
		}
		float guess = Guess(anim, trigger);
		if (guess > 0f)
		{
			// A little for the transition into the clip.
			s_delay[key] = guess + 0.1f;
			Plugin.Log.LogInfo($"Attack timing {key}: guessed {guess + 0.1f:0.00} s from its clip.");
			return guess + 0.1f;
		}
		return fallback;
	}

	private static float Guess(Animator anim, string trigger)
	{
		RuntimeAnimatorController? c = anim.runtimeAnimatorController;
		if (c == null)
		{
			return -1f;
		}
		string want = Normal(trigger);
		string bare = Normal(trigger.Replace("attack", ""));
		AnimationClip? best = null;
		int bestScore = 0;
		foreach (AnimationClip clip in c.animationClips)
		{
			if (HitTime(clip) < 0f)
			{
				continue;
			}
			string name = Normal(clip.name);
			int score = name.EndsWith(want) ? 3 : bare.Length > 0 && name.EndsWith(bare) ? 2 : bare.Length > 0 && name.Contains(bare) ? 1 : 0;
			if (score > bestScore)
			{
				best = clip;
				bestScore = score;
			}
		}
		return best != null ? HitTime(best) : -1f;
	}

	private static string Normal(string s) => s.ToLowerInvariant().Replace("_", "").Replace(" ", "");

	// 1.16.94 (Sam: "why do we have to seemingly guess at the animation timings?"): no more guessing
	// from clip names on a creature's first attack. A hidden copy plays each of its attack triggers
	// through the game's own animator, frame by frame, until the clip with the OnAttackTrigger event
	// is playing; time so far plus time left to that event is the real wind-up, transitions and
	// clip speeds included. Done once per creature kind, when its picture is first made.
	public static void Prewarm(Animator anim, IEnumerable<string> triggers)
	{
		if (anim == null || anim.runtimeAnimatorController == null)
		{
			return;
		}
		HashSet<string> names = new();
		foreach (AnimatorControllerParameter p in anim.parameters)
		{
			if (p.type == AnimatorControllerParameterType.Trigger)
			{
				names.Add(p.name);
			}
		}
		bool any = false;
		foreach (string trigger in triggers)
		{
			if (string.IsNullOrEmpty(trigger) || !names.Contains(trigger) || s_delay.ContainsKey(Key(anim, trigger)))
			{
				continue;
			}
			any = true;
			anim.Rebind();
			anim.Update(0f);
			anim.SetTrigger(trigger);
			const float step = 1f / 60f;
			float t = 0f;
			for (int i = 0; i < 240; i++)
			{
				anim.Update(step);
				t += step;
				float left = Remaining(anim);
				if (left >= 0f)
				{
					s_delay[Key(anim, trigger)] = t + left;
					Plugin.Log.LogInfo($"Attack timing {Key(anim, trigger)}: {t + left:0.00} s, played through the animator.");
					break;
				}
			}
		}
		if (any)
		{
			anim.Rebind();
			anim.Update(0f);
		}
	}

	public static void Learn(Animator anim, string trigger, float delay)
	{
		string key = Key(anim, trigger);
		if (!s_delay.TryGetValue(key, out float old) || Mathf.Abs(old - delay) > 0.15f)
		{
			Plugin.Log.LogInfo($"Attack timing {key}: blow {delay:0.00} s after the trigger (was {(s_delay.ContainsKey(key) ? old.ToString("0.00") : "unknown")}).");
		}
		s_delay[key] = delay;
	}

	public static void ReportMissing(Animator anim, string trigger)
	{
		if (s_reported.Add(Key(anim, trigger)))
		{
			Plugin.Log.LogInfo($"Attack animation '{trigger}' on {anim.gameObject.name} didn't reach a blow (no OnAttackTrigger clip played).");
		}
	}

	// 1.16.1: an estimate from the clip itself is kept as learned, with a note in the log.
	public static void Estimate(Animator anim, string trigger, float delay, string clip)
	{
		string key = Key(anim, trigger);
		if (!s_delay.ContainsKey(key))
		{
			Plugin.Log.LogInfo($"Attack timing {key}: no blow marked in '{clip}', so about {delay:0.00} s (a little before the middle of the clip).");
			s_delay[key] = delay;
		}
	}

	// Once the clip after a trigger is playing: seconds until its blow, or -1 if none yet.
	// 1.16.1 (Sam: abomination and troll log swings landed late): every layer is looked at, not
	// only the base one, since some attacks play on a layer of their own.
	public static float Remaining(Animator anim)
	{
		for (int layer = 0; layer < anim.layerCount; layer++)
		{
			if (layer > 0 && anim.GetLayerWeight(layer) < 0.01f)
			{
				continue;
			}
			bool transition = anim.IsInTransition(layer);
			AnimatorClipInfo[] clips = transition ? anim.GetNextAnimatorClipInfo(layer) : anim.GetCurrentAnimatorClipInfo(layer);
			AnimatorStateInfo state = transition ? anim.GetNextAnimatorStateInfo(layer) : anim.GetCurrentAnimatorStateInfo(layer);
			foreach (AnimatorClipInfo info in clips)
			{
				float hit = HitTime(info.clip);
				if (hit < 0f || info.clip.length <= 0f)
				{
					continue;
				}
				float now = (state.normalizedTime % 1f) * info.clip.length;
				return Mathf.Max(0f, (hit - now) / Rate(anim, state));
			}
		}
		return -1f;
	}

	private static float Rate(Animator anim, AnimatorStateInfo state) => Mathf.Max(0.05f, Mathf.Abs(state.speed * state.speedMultiplier) * Mathf.Max(0.05f, anim.speed));

	// The clip playing (or being blended into) on the base layer.
	public static AnimationClip? Playing(Animator anim)
	{
		bool transition = anim.IsInTransition(0);
		AnimatorClipInfo[] clips = transition ? anim.GetNextAnimatorClipInfo(0) : anim.GetCurrentAnimatorClipInfo(0);
		AnimationClip? best = null;
		float weight = -1f;
		foreach (AnimatorClipInfo info in clips)
		{
			if (info.clip != null && info.weight > weight)
			{
				best = info.clip;
				weight = info.weight;
			}
		}
		return best;
	}

	// A one-shot clip that started after the trigger (not the one playing before it, and not a
	// looping walk or run): the attack's own, even when its blow isn't marked.
	public static bool NewClip(Animator anim, AnimationClip? before, out AnimationClip? clip, out float now, out float rate)
	{
		bool transition = anim.IsInTransition(0);
		AnimatorClipInfo[] clips = transition ? anim.GetNextAnimatorClipInfo(0) : anim.GetCurrentAnimatorClipInfo(0);
		AnimatorStateInfo state = transition ? anim.GetNextAnimatorStateInfo(0) : anim.GetCurrentAnimatorStateInfo(0);
		foreach (AnimatorClipInfo info in clips)
		{
			if (info.clip == null || info.clip == before || info.clip.isLooping || info.clip.length < 0.2f)
			{
				continue;
			}
			clip = info.clip;
			now = (state.normalizedTime % 1f) * info.clip.length;
			rate = Rate(anim, state);
			return true;
		}
		clip = null;
		now = 0f;
		rate = 1f;
		return false;
	}
}

internal sealed partial class SurfGame
{
	// Attacks just triggered, watched until their clip shows when the blow lands.
	private sealed class AnimWatch
	{
		public Animator Anim = null!;
		public string Trigger = "";
		public float Since;
		public System.Action<float>? OnKnown;
		// What played before the trigger, and the attack clip once it starts, with the blow
		// guessed from it (for clips that don't mark their blow).
		public AnimationClip? Before;
		public AnimationClip? Clip;
		public float Guess = -1f;
	}

	private readonly List<AnimWatch> m_animWatches = new();

	// Before an attack has been seen once: a guess from the creature's size. 1.16.1: big ones
	// wind up longer (a troll's slam takes 1.3 s, a skeleton's swing 0.8 s).
	private static float FallbackWindup(Thing t) => Mathf.Clamp(0.35f + 0.15f * t.Height, 0.4f, 1.4f);

	// Triggers an attack animation and watches it: `onKnown` gets the seconds left until the
	// blow as soon as the clip is playing (to land the hit with it).
	private void TriggerWatched(Animator? anim, string trigger, System.Action<float>? onKnown = null)
	{
		if (anim == null || string.IsNullOrEmpty(trigger))
		{
			return;
		}
		string? used = SetTrigger(anim, trigger);
		if (used == null)
		{
			AnimTiming.ReportMissing(anim, trigger);
			return;
		}
		m_animWatches.Add(new AnimWatch { Anim = anim, Trigger = trigger, OnKnown = onKnown, Before = AnimTiming.Playing(anim) });
	}

	private void UpdateAnimWatches(float dt)
	{
		for (int i = m_animWatches.Count - 1; i >= 0; i--)
		{
			AnimWatch w = m_animWatches[i];
			if (w.Anim == null)
			{
				m_animWatches.RemoveAt(i);
				continue;
			}
			// Checked from the frame after the trigger, when the transition has begun.
			if (w.Since > 0f)
			{
				float left = AnimTiming.Remaining(w.Anim);
				if (left >= 0f)
				{
					AnimTiming.Learn(w.Anim, w.Trigger, w.Since + left);
					w.OnKnown?.Invoke(left);
					m_animWatches.RemoveAt(i);
					continue;
				}
				if (w.Clip == null && AnimTiming.NewClip(w.Anim, w.Before, out AnimationClip? clip, out float now, out float rate))
				{
					// The attack's clip is playing but doesn't mark its blow: big swings land a
					// little before the middle. The hit moves there now; a marked blow found
					// later in the attack still wins.
					w.Clip = clip;
					float lands = Mathf.Max(0f, clip!.length * 0.45f - now) / rate;
					w.Guess = w.Since + lands;
					w.OnKnown?.Invoke(lands);
				}
			}
			w.Since += dt;
			// Long wind-ups (a troll's log, an abomination's) can take a while to show their blow.
			if (w.Since > 2.6f || (w.Guess >= 0f && w.Since > w.Guess + 0.4f))
			{
				if (w.Clip != null)
				{
					AnimTiming.Estimate(w.Anim, w.Trigger, w.Guess, w.Clip.name);
				}
				else
				{
					AnimTiming.ReportMissing(w.Anim, w.Trigger);
				}
				m_animWatches.RemoveAt(i);
			}
		}
	}
}

internal sealed partial class SurfGame
{
	// Learns each attack's real wind-up from a hidden, active copy of a new creature picture.
	private static readonly HashSet<GameObject> s_warmed = new();

	private void PrewarmTimings(GameObject prefab, GameObject picture)
	{
		if (!s_warmed.Add(prefab) || prefab.GetComponent<Character>() == null)
		{
			return;
		}
		CreatureInfo info = CreatureInfo.Of(prefab);
		List<string> triggers = new();
		foreach (CreatureAttack a in info.Melee)
		{
			triggers.Add(a.Animation);
		}
		foreach (CreatureAttack a in info.Ranged)
		{
			triggers.Add(a.Animation);
		}
		if (triggers.Count == 0)
		{
			return;
		}
		GameObject copy = Instantiate(picture, m_stage, false);
		try
		{
			copy.transform.localPosition = new Vector3(0f, -500f, 0f);
			foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
			{
				r.enabled = false;
			}
			Animator? anim = copy.GetComponentInChildren<Animator>(true);
			if (anim != null)
			{
				AnimTiming.Prewarm(anim, triggers);
			}
		}
		catch (System.Exception ex)
		{
			Plugin.Log.LogWarning($"Attack timing warm-up for {prefab.name} failed: {ex.Message}");
		}
		finally
		{
			DestroyImmediate(copy);
		}
	}
}
