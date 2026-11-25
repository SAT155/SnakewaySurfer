using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SnakewaySurfer;

// 1.17 (run structure step 3): each Journey (a biome at a star level) has three marks:
// 1) finish it (beat its boss), 2) its biome's challenge, 3) finish with a life to spare.
// All three on a Journey open the same biome one star up (to 3 stars).
// Marks are kept per character in m_customData "SnakewaySurfer.Marks" as "biome:stars:mask,...",
// best of all runs. Unity-free, so the checks on every build (SnakewaySurfer.Tests) compile it.
internal enum ChallengeKind
{
	// Roll through attacks or creatures.
	Rolls,
	// Block attacks (a parry doesn't count as a block).
	Blocks,
	// Parry attacks.
	Parries,
	// Parry the boss (parries in the boss stage).
	BossParries,
	// No hits in the biome's dungeon (with no dungeon this Journey: the whole travel stage).
	CleanDungeon,
	// No hits in the travel stage (for the Plains: the crossing).
	CleanTravel,
	// No hits in the hunt stage.
	CleanHunt,
	// No hits in the boss stage.
	CleanBoss,
}

internal sealed class Challenge
{
	public int Biome;
	public ChallengeKind Kind;
	public int Count;
	// For the log, the win screen and surfprogress.
	public string Short = "";
	// What Hugin says as the Journey starts.
	public string Hugin = "";
}

// What a Journey has done so far, counted by the run.
internal struct JourneyTally
{
	public int Rolls;
	public int Blocks;
	public int Parries;
	public int BossParries;
	public int HitsTravel;
	public int HitsDungeon;
	public int HitsHunt;
	public int HitsBoss;
	public bool SawDungeon;
	// The Journey started at its beginning (not from a stone part way in), so the travel stage
	// was run in full.
	public bool FromStart;
}

internal static class JourneyGoals
{
	public const int Finish = 1;
	public const int ChallengeMark = 2;
	public const int Life = 4;
	public const int AllMarks = 7;
	public const int MaxStars = 3;
	public const string MarksKey = "SnakewaySurfer.Marks";

	public static readonly Challenge[] Challenges =
	{
		new Challenge { Biome = 0, Kind = ChallengeKind.Rolls, Count = 3, Short = "roll through 3",
			Hugin = "Your trial: roll through three attacks. Through, not around. I'll be counting." },
		new Challenge { Biome = 1, Kind = ChallengeKind.CleanDungeon, Count = 0, Short = "no hits in the dungeon",
			Hugin = "Your trial: come out of the dungeon without a scratch. No dungeon? Then the whole way to the ward stone." },
		new Challenge { Biome = 2, Kind = ChallengeKind.Blocks, Count = 5, Short = "block 5",
			Hugin = "Your trial: block five blows. The swamp hits hard and often. Hold your shield up." },
		new Challenge { Biome = 3, Kind = ChallengeKind.Parries, Count = 3, Short = "parry 3",
			Hugin = "Your trial: parry three attacks. Wait for the glint, then raise the shield." },
		new Challenge { Biome = 4, Kind = ChallengeKind.CleanTravel, Count = 0, Short = "no hits at sea",
			Hugin = "Your trial: cross the sea without a single hit. The serpent disagrees. Prove it wrong." },
		new Challenge { Biome = 5, Kind = ChallengeKind.BossParries, Count = 3, Short = "parry the boss 3",
			Hugin = "Your trial: parry the Queen three times. She hates that. Do it anyway." },
		new Challenge { Biome = 6, Kind = ChallengeKind.CleanBoss, Count = 0, Short = "no hits on the boss",
			Hugin = "Your trial: face Fader and take no hits. None at all. I'll look away if it helps." },
		new Challenge { Biome = 7, Kind = ChallengeKind.CleanHunt, Count = 0, Short = "no hits in the hunt",
			Hugin = "Your trial: gather everything for the hunt without a scratch. The cold won't help." },
	};

	public static Challenge? For(int biome) => Array.Find(Challenges, c => c.Biome == biome);

	// Whether the challenge is met. stagesDone: 0 while travelling, 1 once the hunt starts,
	// 2 once the boss stage starts, 3 when the Journey is won. Counted ones are met as soon as
	// the count is reached; clean ones only once their stage is behind you.
	public static bool Met(Challenge c, JourneyTally t, int stagesDone)
	{
		switch (c.Kind)
		{
			case ChallengeKind.Rolls:
				return t.Rolls >= c.Count;
			case ChallengeKind.Blocks:
				return t.Blocks >= c.Count;
			case ChallengeKind.Parries:
				return t.Parries >= c.Count;
			case ChallengeKind.BossParries:
				return t.BossParries >= c.Count;
			case ChallengeKind.CleanDungeon:
				return stagesDone >= 1 && t.FromStart && (t.SawDungeon ? t.HitsDungeon == 0 : t.HitsTravel == 0);
			case ChallengeKind.CleanTravel:
				return stagesDone >= 1 && t.FromStart && t.HitsTravel == 0;
			case ChallengeKind.CleanHunt:
				return stagesDone >= 2 && t.HitsHunt == 0;
			case ChallengeKind.CleanBoss:
				return stagesDone >= 3 && t.HitsBoss == 0;
			default:
				return false;
		}
	}

	// "A life to spare": at least Lives - 1 lives left (2 of the default 3), never more than you
	// start with, never under 1.
	public static int LivesNeeded(int livesSetting, int startLives) => Math.Max(1, Math.Min(livesSetting - 1, startLives));

	// ---------------------------------------------------------------- storage

	public static Dictionary<(int biome, int stars), int> Parse(string? value)
	{
		Dictionary<(int, int), int> marks = new();
		if (string.IsNullOrEmpty(value))
		{
			return marks;
		}
		foreach (string part in value!.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
		{
			string[] bits = part.Split(':');
			if (bits.Length == 3
				&& int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int b)
				&& int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)
				&& int.TryParse(bits[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m)
				&& b >= 0 && s >= 0 && s <= MaxStars)
			{
				marks.TryGetValue((b, s), out int old);
				marks[(b, s)] = old | (m & AllMarks);
			}
		}
		return marks;
	}

	public static string Format(Dictionary<(int biome, int stars), int> marks)
	{
		List<(int, int)> keys = new(marks.Keys);
		keys.Sort();
		StringBuilder sb = new();
		foreach ((int b, int s) in keys)
		{
			if (marks[(b, s)] == 0)
			{
				continue;
			}
			if (sb.Length > 0)
			{
				sb.Append(',');
			}
			sb.Append(b.ToString(CultureInfo.InvariantCulture)).Append(':').Append(s.ToString(CultureInfo.InvariantCulture)).Append(':').Append(marks[(b, s)].ToString(CultureInfo.InvariantCulture));
		}
		return sb.ToString();
	}

	public static int Of(Dictionary<(int biome, int stars), int> marks, int biome, int stars) => marks.TryGetValue((biome, stars), out int m) ? m : 0;

	// Adds marks to a Journey; returns the ones that are new.
	public static int Add(Dictionary<(int biome, int stars), int> marks, int biome, int stars, int add)
	{
		int old = Of(marks, biome, stars);
		int fresh = add & AllMarks & ~old;
		if (fresh != 0)
		{
			marks[(biome, stars)] = old | fresh;
		}
		return fresh;
	}

	// A star Journey opens once all three marks are held one star below.
	public static bool StarsOpened(Dictionary<(int biome, int stars), int> marks, int biome, int stars) =>
		stars <= 0 || (stars <= MaxStars && Of(marks, biome, stars - 1) == AllMarks);

	public static int Count(int mask) => (mask & 1) + ((mask >> 1) & 1) + ((mask >> 2) & 1);
}
