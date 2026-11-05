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

	// Once the clip after a trigger is playing: seconds until its blow, or -1 if none yet.
	public static float Remaining(Animator anim)
	{
		bool transition = anim.IsInTransition(0);
		AnimatorClipInfo[] clips = transition ? anim.GetNextAnimatorClipInfo(0) : anim.GetCurrentAnimatorClipInfo(0);
		AnimatorStateInfo state = transition ? anim.GetNextAnimatorStateInfo(0) : anim.GetCurrentAnimatorStateInfo(0);
		foreach (AnimatorClipInfo info in clips)
		{
			float hit = HitTime(info.clip);
			if (hit < 0f || info.clip.length <= 0f)
			{
				continue;
			}
			float now = (state.normalizedTime % 1f) * info.clip.length;
			float rate = Mathf.Max(0.05f, Mathf.Abs(state.speed * state.speedMultiplier) * Mathf.Max(0.05f, anim.speed));
			return Mathf.Max(0f, (hit - now) / rate);
		}
		return -1f;
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
	}

	private readonly List<AnimWatch> m_animWatches = new();

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
		m_animWatches.Add(new AnimWatch { Anim = anim, Trigger = trigger, OnKnown = onKnown });
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
			}
			w.Since += dt;
			if (w.Since > 1.5f)
			{
				AnimTiming.ReportMissing(w.Anim, w.Trigger);
				m_animWatches.RemoveAt(i);
			}
		}
	}
}
