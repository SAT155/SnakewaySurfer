using System;
using System.Collections.Generic;

namespace SnakewaySurfer;

// The placement service: one record of everything standing on the three lanes ahead, and one
// answer to "can a runner still get through?". Every row the game lays out is checked against
// it before the player sees it; whatever would leave no fair way through is taken out again
// (SurfGate.cs). It knows nothing of Unity, so the same file is compiled into the checks that
// run on every build (SnakewaySurfer.Tests), where thousands of random tracks are laid out and
// each one this service calls passable is then run by a simulated player.
//
// "Fair" here means a way through exists for a runner who jumps, rolls and changes lane no
// faster than the game allows, with a moment to react between one move and the next.

// What a runner must do to get past something.
internal enum Need
{
	// Jump over it (logs, low creatures).
	Jump,
	// Roll under or past it (high logs, flyers, a creature's attack).
	Roll,
	// Either will do (a gas cloud).
	Either,
	// No way past in this lane.
	Wall,
}

internal sealed class Slot
{
	public int Lane;
	public float From;
	public float To;
	public Need Need;
	public string What = "";
	public object? Owner;
}

// How far each move carries the runner at the present speed, in metres.
internal struct Pace
{
	// From take-off until high enough to clear something, and how long it stays cleared.
	public float JumpRise;
	public float JumpClear;
	// Take-off to landing.
	public float JumpTotal;
	// How long a roll lasts, and how soon another can start.
	public float RollClear;
	public float RollAgain;
	// A moment to react before the next move.
	public float React;
	// Between one lane change and the next.
	public float Switch;

	// clearHeight: how high the runner must be to clear a log (0.75 m in the game).
	public static Pace At(float speed, float jumpHeight = 1.6f, float slideTime = 0.8f, float gravity = 32f, float clearHeight = 0.75f, float reactTime = 0.22f)
	{
		speed = Math.Max(1f, speed);
		float v = (float)Math.Sqrt(2f * gravity * jumpHeight);
		float total = 2f * v / gravity;
		// Time above clearHeight: v t - g t^2 / 2 = h.
		float disc = v * v - 2f * gravity * Math.Min(clearHeight, jumpHeight * 0.95f);
		float root = (float)Math.Sqrt(Math.Max(0f, disc));
		float rise = (v - root) / gravity;
		float clear = 2f * root / gravity;
		return new Pace
		{
			JumpRise = rise * speed,
			JumpClear = clear * speed,
			JumpTotal = total * speed,
			RollClear = slideTime * speed,
			RollAgain = (slideTime + 0.45f) * speed,
			React = reactTime * speed,
			Switch = 0.18f * speed,
		};
	}
}

internal sealed class Placement
{
	public const float Step = 0.5f;

	// One way a runner could have got to a lane.
	private struct State
	{
		// From where a new jump, roll or lane change may begin.
		public float JumpOk;
		public float RollOk;
		public float SwitchOk;
		// Up to where the jump or roll in progress carries.
		public float JumpCover;
		public float RollCover;
		// Where the last thing rolled under ended (a jump can't begin before it).
		public float RollNeededTo;

		public bool Beats(in State o) =>
			JumpOk <= o.JumpOk && RollOk <= o.RollOk && SwitchOk <= o.SwitchOk
			&& JumpCover >= o.JumpCover && RollCover >= o.RollCover && RollNeededTo <= o.RollNeededTo;
	}

	private readonly List<Slot> m_slots = new();
	private List<State>[] m_base = Fresh(float.NegativeInfinity);
	private float m_baseZ;

	public IReadOnlyList<Slot> Slots => m_slots;

	// How many things the gate has taken out, and the last few, for the logs.
	public int Removed;

	private static List<State>[] Fresh(float z)
	{
		List<State>[] s = new List<State>[3];
		for (int l = 0; l < 3; l++)
		{
			s[l] = new List<State>
			{
				new State { JumpOk = z, RollOk = z, SwitchOk = z, JumpCover = float.NegativeInfinity, RollCover = float.NegativeInfinity, RollNeededTo = float.NegativeInfinity },
			};
		}
		return s;
	}

	public void Reset(float z)
	{
		m_slots.Clear();
		m_base = Fresh(float.NegativeInfinity);
		m_baseZ = z;
		Removed = 0;
	}

	public Slot Add(int lane, float from, float to, Need need, string what, object? owner = null)
	{
		Slot s = new Slot { Lane = lane, From = Math.Min(from, to), To = Math.Max(from, to), Need = need, What = what, Owner = owner };
		m_slots.Add(s);
		return s;
	}

	public void Remove(Slot s) => m_slots.Remove(s);

	// Forget what stands where, but not what the runner has already had to do.
	public void Clear() => m_slots.Clear();

	public void RemoveOwner(object owner) => m_slots.RemoveAll(s => ReferenceEquals(s.Owner, owner));

	// The runner has reached z: what lies well behind is settled and forgotten.
	public void Passed(float z, Pace pace)
	{
		float to = z - 6f;
		if (to - m_baseZ < 12f)
		{
			return;
		}
		List<State>[] at = Run(m_base, m_baseZ, to, pace, out _);
		bool any = at[0].Count + at[1].Count + at[2].Count > 0;
		// (If the record says nobody could be here, the runner got here anyway: start afresh.)
		m_base = any ? at : Fresh(float.NegativeInfinity);
		m_baseZ = m_baseZ + Step * (float)Math.Ceiling((to - m_baseZ) / Step);
		m_slots.RemoveAll(s => s.To < m_baseZ - 1f);
	}

	// Can a runner get from where the record starts to past the last thing on it?
	public bool Passable(Pace pace, out float stuckAt)
	{
		float end = m_baseZ;
		foreach (Slot s in m_slots)
		{
			end = Math.Max(end, s.To);
		}
		List<State>[] at = Run(m_base, m_baseZ, end + 4f, pace, out stuckAt);
		return at[0].Count + at[1].Count + at[2].Count > 0;
	}

	private List<State>[] Run(List<State>[] start, float from, float to, Pace pace, out float stuckAt)
	{
		stuckAt = from;
		List<State>[] now = new List<State>[3];
		for (int l = 0; l < 3; l++)
		{
			now[l] = new List<State>(start[l]);
		}
		List<Slot>[] byLane = { new List<Slot>(), new List<Slot>(), new List<Slot>() };
		foreach (Slot s in m_slots)
		{
			if (s.Lane >= 0 && s.Lane < 3 && s.To >= from && s.From <= to)
			{
				byLane[s.Lane].Add(s);
			}
		}
		List<State> work = new();
		List<State> grown = new();
		for (float z = from; z < to; z += Step)
		{
			float next = z + Step;
			List<State>[] then = { new List<State>(), new List<State>(), new List<State>() };
			for (int l = 0; l < 3; l++)
			{
				foreach (State s in now[l])
				{
					for (int d = -1; d <= 1; d++)
					{
						int nl = l + d;
						if (nl < 0 || nl > 2)
						{
							continue;
						}
						State moved = s;
						if (d != 0)
						{
							if (s.SwitchOk > z)
							{
								continue;
							}
							moved.SwitchOk = z + pace.Switch;
						}
						// Everything in the new lane that the runner meets during this step.
						work.Clear();
						work.Add(moved);
						foreach (Slot slot in byLane[nl])
						{
							// (Anything touched anywhere in this step, however thin.)
							if (slot.From > next || slot.To < z)
							{
								continue;
							}
							grown.Clear();
							foreach (State w in work)
							{
								Meet(w, slot, z, pace, grown);
							}
							work.Clear();
							work.AddRange(grown);
							if (work.Count == 0)
							{
								break;
							}
						}
						foreach (State w in work)
						{
							Keep(then[nl], w);
						}
					}
				}
			}
			now = then;
			if (now[0].Count + now[1].Count + now[2].Count == 0)
			{
				stuckAt = next;
				return now;
			}
		}
		stuckAt = to;
		return now;
	}

	// The ways state s gets past the slot, having been at z one step before entering it.
	private static void Meet(State s, Slot slot, float z, Pace pace, List<State> into)
	{
		if (slot.Need == Need.Wall)
		{
			return;
		}
		if (slot.Need == Need.Jump || slot.Need == Need.Either)
		{
			if (s.JumpCover >= slot.To)
			{
				into.Add(s);
			}
			else
			{
				// Take off early enough to be clear on arrival.
				float start = z - pace.JumpRise;
				if (s.JumpOk <= start && s.RollNeededTo <= start && z + pace.JumpClear >= slot.To)
				{
					State j = s;
					j.JumpCover = z + pace.JumpClear;
					j.RollCover = float.NegativeInfinity;
					j.JumpOk = start + pace.JumpTotal + pace.React;
					j.RollOk = Math.Max(s.RollOk, start + pace.JumpTotal + pace.React);
					into.Add(j);
				}
			}
		}
		if (slot.Need == Need.Roll || slot.Need == Need.Either)
		{
			if (s.RollCover >= slot.To)
			{
				State r = s;
				r.RollNeededTo = Math.Max(r.RollNeededTo, slot.To);
				into.Add(r);
			}
			// (A runner in the air can't roll: only once the jump is over, which RollOk holds.)
			else if (s.RollOk <= z && z + pace.RollClear >= slot.To)
			{
				State r = s;
				r.RollCover = z + pace.RollClear;
				r.JumpCover = float.NegativeInfinity;
				r.RollOk = z + pace.RollAgain;
				r.JumpOk = Math.Max(s.JumpOk, z + pace.React);
				r.RollNeededTo = Math.Max(r.RollNeededTo, slot.To);
				into.Add(r);
			}
		}
	}

	private static void Keep(List<State> list, State s)
	{
		for (int i = list.Count - 1; i >= 0; i--)
		{
			if (list[i].Beats(s))
			{
				return;
			}
			if (s.Beats(list[i]))
			{
				list.RemoveAt(i);
			}
		}
		if (list.Count < 24)
		{
			list.Add(s);
		}
	}
}
