using System;
using System.Collections.Generic;
using SnakewaySurfer;

// Checks for the placement service. Two kinds:
//  1. Hand-made tracks with a known answer.
//  2. Thousands of random tracks. Each one the service calls passable is run by a simulated
//     player that obeys the game's own jump, roll and lane-change limits and tries every
//     possible sequence of moves. If that player can't get through, the service was wrong.
internal static class Program
{
	private static int s_failed;

	private static void Check(bool ok, string what)
	{
		if (!ok)
		{
			s_failed++;
			Console.WriteLine("FAILED: " + what);
		}
	}

	private static Placement Track(params (int lane, float from, float to, Need need)[] slots)
	{
		Placement p = new Placement();
		p.Reset(0f);
		foreach ((int lane, float from, float to, Need need) in slots)
		{
			p.Add(lane, from, to, need, "test");
		}
		return p;
	}

	private static int Main(string[] args)
	{
		Pace pace = Pace.At(12f);
		Console.WriteLine($"At 12 m/s: jump clears {pace.JumpClear:0.0} m after {pace.JumpRise:0.0} m, lands after {pace.JumpTotal:0.0} m; roll {pace.RollClear:0.0} m, again after {pace.RollAgain:0.0} m; react {pace.React:0.0} m.");

		// ---- hand-made
		Check(Track().Passable(pace, out _), "an empty track is passable");
		Check(Track((0, 30, 31, Need.Jump), (1, 30, 31, Need.Roll)).Passable(pace, out _), "a log and a flyer with a free lane");
		Check(!Track((0, 30, 31, Need.Wall), (1, 30, 31, Need.Wall), (2, 30, 31, Need.Wall)).Passable(pace, out float stuck) && stuck > 29f && stuck < 32f, "three walls abreast stop the run, and it says where");
		Check(Track((0, 30, 31, Need.Wall), (1, 30, 31, Need.Wall), (2, 30, 31, Need.Jump)).Passable(pace, out _), "two walls and a log");
		Check(!Track((0, 30, 31, Need.Jump), (1, 30, 31, Need.Jump), (2, 30, 31, Need.Jump),
			(0, 32, 33, Need.Roll), (1, 32, 33, Need.Roll), (2, 32, 33, Need.Roll)).Passable(pace, out _), "a jump then a roll 1 m on, in every lane, is unfair");
		Check(Track((0, 30, 31, Need.Jump), (1, 30, 31, Need.Jump), (2, 30, 31, Need.Jump),
			(0, 50, 51, Need.Roll), (1, 50, 51, Need.Roll), (2, 50, 51, Need.Roll)).Passable(pace, out _), "a jump then a roll 19 m on is fine");
		Check(!Track((0, 30, 31, Need.Roll), (1, 30, 31, Need.Roll), (2, 30, 31, Need.Roll),
			(0, 42, 43, Need.Roll), (1, 42, 43, Need.Roll), (2, 42, 43, Need.Roll)).Passable(pace, out _), "two rolls inside one roll's cooldown, in every lane, is unfair");
		Check(Track((0, 30, 31, Need.Roll), (1, 30, 31, Need.Roll), (2, 30, 31, Need.Roll),
			(0, 34, 35, Need.Roll), (1, 34, 35, Need.Roll), (2, 34, 35, Need.Roll)).Passable(pace, out _), "two rows one roll carries through");
		Check(!Track((0, 30, 50, Need.Jump), (1, 30, 31, Need.Wall), (2, 30, 31, Need.Wall)).Passable(pace, out _), "nothing 20 m long can be jumped");
		Check(!Track((0, 30, 31, Need.Wall), (1, 30, 31, Need.Wall), (1, 31.5f, 32.5f, Need.Wall), (2, 31.5f, 32.5f, Need.Wall), (0, 33, 34, Need.Wall), (1, 33, 34, Need.Wall)).Passable(pace, out _), "a zigzag quicker than lanes can be changed is unfair");
		Check(Track((0, 30, 31, Need.Wall), (1, 30, 31, Need.Wall), (1, 45, 46, Need.Wall), (2, 45, 46, Need.Wall), (0, 60, 61, Need.Wall), (1, 60, 61, Need.Wall)).Passable(pace, out _), "a slow zigzag is fine");
		Check(Track((0, 30, 33, Need.Either), (1, 30, 33, Need.Either), (2, 30, 33, Need.Either)).Passable(pace, out _), "gas across every lane is passable");
		{
			// Forgetting what is behind keeps what it implied.
			Placement p = Track((0, 30, 31, Need.Roll), (1, 30, 31, Need.Roll), (2, 30, 31, Need.Roll));
			p.Passed(38f, pace);
			p.Add(0, 42, 43, Need.Roll, "late");
			p.Add(1, 42, 43, Need.Roll, "late");
			p.Add(2, 42, 43, Need.Roll, "late");
			Check(!p.Passable(pace, out _), "the roll behind you still counts after the record moves on");
		}

		// ---- random tracks against a simulated player
		Random rng = new Random(155);
		int tracks = args.Length > 0 ? int.Parse(args[0]) : 300;
		int passable = 0;
		int simPassable = 0;
		int wrong = 0;
		int gated = 0;
		int removed = 0;
		int added = 0;
		for (int n = 0; n < tracks; n++)
		{
			float speed = 8f + (float)rng.NextDouble() * 12f;
			Pace p = Pace.At(speed);
			List<(int lane, float from, float to, Need need)> slots = RandomTrack(rng, out _);
			Placement place = Track(slots.ToArray());
			bool says = place.Passable(p, out _);
			bool sim = Player.CanRun(slots, speed);
			passable += says ? 1 : 0;
			simPassable += sim ? 1 : 0;
			if (says && !sim)
			{
				wrong++;
				if (wrong <= 3)
				{
					Console.WriteLine($"  called passable but the player can't run it (speed {speed:0.0}):");
					foreach ((int lane, float from, float to, Need need) in slots)
					{
						Console.WriteLine($"    lane {lane} {from:0.0}-{to:0.0} {need}");
					}
				}
			}

			// The gate as the game uses it: rows arrive one at a time, and whatever closes the
			// track is taken out again. The result must always be runnable.
			Placement gate = new Placement();
			gate.Reset(0f);
			List<(int lane, float from, float to, Need need)> kept = new();
			RandomTrack(rng, out List<List<(int lane, float from, float to, Need need)>> rows);
			foreach (List<(int lane, float from, float to, Need need)> row in rows)
			{
				List<Slot> mine = new();
				foreach ((int lane, float from, float to, Need need) in row)
				{
					mine.Add(gate.Add(lane, from, to, need, "row"));
					added++;
				}
				while (mine.Count > 0 && !gate.Passable(p, out _))
				{
					gate.Remove(mine[mine.Count - 1]);
					mine.RemoveAt(mine.Count - 1);
					removed++;
				}
				foreach (Slot s in mine)
				{
					kept.Add((s.Lane, s.From, s.To, s.Need));
				}
			}
			if (!gate.Passable(p, out _) || !Player.CanRun(kept, speed))
			{
				gated++;
			}
		}
		Console.WriteLine($"{tracks} random tracks: the service calls {passable} passable, the simulated player can run {simPassable}.");
		Console.WriteLine($"Gate: {removed} of {added} things taken out to keep every track runnable.");
		Check(wrong == 0, $"{wrong} tracks were called passable that the simulated player can't run");
		Check(gated == 0, $"{gated} gated tracks were not runnable");
		Check(passable > tracks / 5, "the random tracks should include plenty of passable ones");
		Check(passable >= simPassable * 0.6, "the service shouldn't refuse most of what a player can run");

		Console.WriteLine(s_failed == 0 ? "All placement checks passed." : $"{s_failed} placement checks FAILED.");
		return s_failed == 0 ? 0 : 1;
	}

	// Rows like the game's, but meaner: closer together and fuller.
	private static List<(int lane, float from, float to, Need need)> RandomTrack(Random rng, out List<List<(int lane, float from, float to, Need need)>> rows)
	{
		List<(int lane, float from, float to, Need need)> all = new();
		rows = new();
		float z = 20f;
		int count = 2 + rng.Next(4);
		for (int r = 0; r < count; r++)
		{
			List<(int lane, float from, float to, Need need)> row = new();
			int filled = 2 + rng.Next(2);
			int skip = rng.Next(3);
			for (int lane = 0; lane < 3; lane++)
			{
				if (filled < 3 && lane == skip && (filled == 2 || rng.Next(2) == 0))
				{
					continue;
				}
				if (filled == 1 && lane != (skip + 1) % 3)
				{
					continue;
				}
				double roll = rng.NextDouble();
				Need need = roll < 0.3 ? Need.Jump : roll < 0.55 ? Need.Roll : roll < 0.62 ? Need.Either : Need.Wall;
				float half = need == Need.Either ? 1.6f : need == Need.Roll && rng.Next(3) == 0 ? 1.6f : 0.35f;
				float at = z + (float)rng.NextDouble() * 3f;
				row.Add((lane, at - half, at + half, need));
			}
			rows.Add(row);
			all.AddRange(row);
			z += rng.Next(2) == 0 ? 1.5f + (float)rng.NextDouble() * 6f : 8f + (float)rng.NextDouble() * 22f;
		}
		return all;
	}
}

// A player that obeys the game's limits (SurfGame.cs: gravity 32, jump 1.6 m, logs cleared above
// 0.75 m, gas above 0.7 m, a roll lasts 0.8 s and can start again 1.25 s after the last began,
// lane changes 0.18 s apart) and tries every sequence of moves.
internal static class Player
{
	private const float Tick = 0.02f;

	public static bool CanRun(List<(int lane, float from, float to, Need need)> slots, float speed)
	{
		float end = 0f;
		foreach ((int _, float _, float to, Need _) in slots)
		{
			end = Math.Max(end, to);
		}
		end += 3f;
		float v = (float)Math.Sqrt(2f * 32f * 1.6f);
		int jumpTicks = (int)Math.Ceiling(2f * v / 32f / Tick);
		int rollTicks = (int)(0.8f / Tick);
		int againTicks = (int)Math.Ceiling(1.25f / Tick);
		int switchTicks = (int)Math.Ceiling(0.18f / Tick);
		// State: lane, ticks into the jump (0 = on the ground), ticks since the roll began
		// (againTicks = free to roll), ticks until the next lane change.
		HashSet<int> now = new();
		for (int lane = 0; lane < 3; lane++)
		{
			now.Add(Pack(lane, 0, againTicks, 0));
		}
		int steps = (int)Math.Ceiling(end / (speed * Tick));
		for (int i = 1; i <= steps && now.Count > 0; i++)
		{
			float z = i * speed * Tick;
			HashSet<int> then = new();
			foreach (int s in now)
			{
				Unpack(s, out int lane, out int jump, out int roll, out int sw);
				for (int d = -1; d <= 1; d++)
				{
					int nl = lane + d;
					if (nl < 0 || nl > 2 || (d != 0 && sw > 0))
					{
						continue;
					}
					int nsw = d != 0 ? switchTicks : Math.Max(0, sw - 1);
					// 0 = carry on, 1 = jump, 2 = roll.
					for (int move = 0; move < 3; move++)
					{
						int nj = jump > 0 ? jump + 1 : 0;
						if (nj > jumpTicks)
						{
							nj = 0;
						}
						int nr = Math.Min(againTicks, roll + 1);
						if (move == 1)
						{
							if (jump > 0)
							{
								continue;
							}
							nj = 1;
							// A jump ends the roll (its cooldown runs on).
							if (nr < rollTicks)
							{
								nr = rollTicks;
							}
						}
						else if (move == 2)
						{
							if (jump > 0 || roll < againTicks)
							{
								continue;
							}
							nr = 0;
						}
						float t = nj * Tick;
						float y = nj > 0 ? v * t - 16f * t * t : 0f;
						bool rolling = nr < rollTicks && nj == 0;
						bool hit = false;
						foreach ((int sl, float from, float to, Need need) in slots)
						{
							if (sl != nl || z < from || z > to)
							{
								continue;
							}
							if (need == Need.Wall
								|| (need == Need.Jump && y <= 0.75f)
								|| (need == Need.Roll && !rolling)
								|| (need == Need.Either && y <= 0.7f && !rolling))
							{
								hit = true;
								break;
							}
						}
						if (!hit)
						{
							then.Add(Pack(nl, nj, nr, nsw));
						}
					}
				}
			}
			now = then;
		}
		return now.Count > 0;
	}

	private static int Pack(int lane, int jump, int roll, int sw) => lane | (jump << 2) | (roll << 10) | (sw << 18);

	private static void Unpack(int s, out int lane, out int jump, out int roll, out int sw)
	{
		lane = s & 3;
		jump = (s >> 2) & 255;
		roll = (s >> 10) & 255;
		sw = (s >> 18) & 255;
	}
}
