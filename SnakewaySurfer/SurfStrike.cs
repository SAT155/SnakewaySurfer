using UnityEngine;

namespace SnakewaySurfer;

// Fair warning, kept in one place: no attack may hurt the runner unless it was announced. An
// attack counts as announced when a lane marker has lain on the runner's lane for a moment, or
// when the attack itself (a visible thing, not an unseen zone) has been in view that long.
// Anything else is forgiven at the moment it would have hit, and logged ("Unwarned: ..."), so
// each attack that still strikes without warning shows up by name in the game log and in a
// self-test report, to be given a proper warning of its own.
//
// Only attacks are judged (strikes, blows, clouds, anything short-lived a creature or boss
// does): creatures' bodies, logs and walls are in plain sight and are the placement service's
// business (SurfGate.cs).
internal sealed partial class SurfGame
{
	// How long a warning must have shown, in seconds.
	// 1.16.x (playtest: an Elder's root came up under the runner with no chance to react): 0.8 s,
	// time to see it and change lane or roll; it was 0.4 s.
	private const float MinWarning = AttackRecord.MinWarning;

	private int m_unwarned;

	// The thing's attack record, made the first time it's needed and kept in step with the thing
	// (a creature can turn heavy mid-attack); when it was first warned is kept.
	private AttackRecord RecordOf(Thing t)
	{
		bool labelled = t.Label.Length > 0;
		AttackTraits traits = new AttackTraits
		{
			Name = labelled ? t.Label : "(obstacle)",
			Low = t.Kind == Kind.Low,
			High = t.Kind == Kind.High,
			Jumpable = t.Kind == Kind.Block && t.ClearHeight > 0f && t.ClearHeight < Plugin.JumpHeight.Value * 0.85f,
			JumpOrSlide = t.JumpOrSlide,
			Labelled = labelled,
			// Only a labelled thing's smash, rise or giant's size makes it heavy to the shield;
			// unlabelled and heavy is a wall or a wreck, which a roll doesn't pass.
			Heavy = t.Heavy || (labelled && (t.Smashes > 0 || t.Rise || IsGiant(t.Label))),
			Wall = t.Wall,
			Lanes = t.HitLanes.Length > 0 ? t.HitLanes : new[] { t.Lane },
			Strike = t.Threat || t.Life > 0f,
		};
		AttackRecord rec = AttackRecord.From(traits);
		if (t.Rec != null)
		{
			rec.WarnedAt = t.Rec.WarnedAt;
			rec.LandsAt = t.Rec.LandsAt;
		}
		t.Rec = rec;
		return rec;
	}

	private bool FairlyWarned(Thing t)
	{
		if (!Plugin.FairWarning.Value || m_sea.AtSea || t.Label.Length == 0 || !(t.Threat || t.Life > 0f))
		{
			return true;
		}
		AttackRecord rec = RecordOf(t);
		MarkerNote? m = MarkerOn(m_lane, 1.5f);
		if (m != null)
		{
			rec.NoteWarning(m.Born);
		}
		if (rec.Warned(m_clock))
		{
			return true;
		}
		float seen = t.Born >= 0f ? m_clock - t.Born : 0f;
		if (seen < MinWarning || t.Go == null)
		{
			return false;
		}
		foreach (Renderer r in t.Go.GetComponentsInChildren<Renderer>())
		{
			if (r.enabled)
			{
				return true;
			}
		}
		return false;
	}

	private void ForgiveUnwarned(Thing t, string why)
	{
		t.Collidable = false;
		m_unwarned++;
		string biome = m_biomes.Count > 0 ? m_biomes[Mathf.Clamp(BiomeAt(m_z), 0, m_biomes.Count - 1)].Def.Name : "?";
		float seen = t.Born >= 0f ? m_clock - t.Born : 0f;
		string line = $"{t.Label} [{t.Kind}{(t.Heavy ? ", heavy" : "")}{(t.Move.Length > 0 ? ", " + t.Move : "")}] in {biome}"
			+ $"{(InCrypt(m_z) ? " dungeon" : "")}{(m_bossSeg != null ? " boss fight" : "")}, {m_z:0} m, lane {m_lane} (its lane {t.Lane}), "
			+ $"in view for {seen:0.0} s, {WarnedFor()}; forgiven ({m_unwarned} this run); \"{why}\"";
		Plugin.Log.LogInfo("Unwarned: " + line);
		if (Touring)
		{
			m_tourHits.AppendLine($"  [forgiven, no warning, {m_tourTime:0.0} s] {line}");
		}
	}
}
