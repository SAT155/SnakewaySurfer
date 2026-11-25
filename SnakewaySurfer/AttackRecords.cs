using System;

namespace SnakewaySurfer;

// Attack records (architecture step 3): every creature and boss attack, and everything else that
// can hurt the runner, is described one way: the lanes it covers, when it was announced, when it
// lands, and what gets the runner past it. The rules that used to be spread over Hit, TryBlock and
// the fair-warning check (SurfStrike.cs) read from the record instead, so they can't disagree,
// and the game gives every threat a record the first time it is seen (SurfGame.RecordOf), so no
// attack exists without one.
//
// It knows nothing of Unity, so the same file is compiled into the checks that run on every build
// (SnakewaySurfer.Tests), which hold SAT's rules:
//  - a roll on the ground always gets past (i-frames), except a dungeon wall or a wreck at sea;
//  - giants (trolls, abominations...) and other heavy blows can't be blocked, only parried;
//  - logs, gas and other unlabelled things can't be blocked or parried;
//  - no attack hurts unless it was announced at least MinWarning (0.8 s) before.

// What gets the runner past an attack.
[Flags]
internal enum Counter
{
	None = 0,
	// Change lane.
	Dodge = 1,
	Jump = 2,
	Roll = 4,
	Parry = 8,
	Block = 16,
}

// What the game knows about a threat when it makes its record.
internal struct AttackTraits
{
	public string Name;
	// Kind: low (jump it), high (roll under it), or a block in the lane.
	public bool Low;
	public bool High;
	// Short enough to jump (a block with a low clear height), or gas to jump or roll through.
	public bool Jumpable;
	public bool JumpOrSlide;
	// A creature or a creature's attack (it has a label): those can be blocked and parried.
	public bool Labelled;
	// Too heavy to block (giants, smashes, ambushers, heavy blows).
	public bool Heavy;
	// A wall or bare obstacle: no jump or roll over it in this lane (the placement service's
	// view; a roll's i-frames still pass it unless it's a wreck).
	public bool Wall;
	// Running into a wall or wrecking at sea: unlabelled and heavy. A roll doesn't help.
	public bool Wreck => !Labelled && Heavy;
	public int[] Lanes;
	// A short-lived attack (strike, blow, cloud) rather than a body in plain sight.
	public bool Strike;
}

internal sealed class AttackRecord
{
	public const float MinWarning = 0.8f;

	public string Name = "";
	public int[] Lanes = Array.Empty<int>();
	public Counter Counters;
	public bool Heavy;
	public bool Strike;
	// When the warning first showed (marker on its lane, or the thing itself in view); NaN: not yet.
	public float WarnedAt = float.NaN;
	// When it lands (a strike's moment); NaN: it's there as long as it lasts.
	public float LandsAt = float.NaN;

	public bool Has(Counter c) => (Counters & c) != 0;

	public static AttackRecord From(in AttackTraits t)
	{
		Counter c = Counter.Dodge;
		if (t.Wall)
		{
			c = Counter.Dodge;
		}
		else if (t.JumpOrSlide)
		{
			c |= Counter.Jump | Counter.Roll;
		}
		else if (t.Low)
		{
			c |= Counter.Jump;
		}
		else if (t.High)
		{
			c |= Counter.Roll;
		}
		else if (t.Jumpable)
		{
			c |= Counter.Jump;
		}
		// SAT: a roll on the ground always has i-frames; only a wall or a wreck still counts.
		if (!t.Wreck)
		{
			c |= Counter.Roll;
		}
		// Only creatures' blows can be met with a shield: not logs, walls, or gas.
		if (t.Labelled && !t.JumpOrSlide)
		{
			c |= Counter.Parry;
			if (!t.Heavy)
			{
				c |= Counter.Block;
			}
		}
		return new AttackRecord
		{
			Name = t.Name ?? "",
			Lanes = t.Lanes ?? Array.Empty<int>(),
			Counters = c,
			Heavy = t.Heavy,
			Strike = t.Strike,
		};
	}

	// Announced long enough before `now`?
	public bool Warned(float now) => !float.IsNaN(WarnedAt) && now - WarnedAt >= MinWarning;

	public void NoteWarning(float now)
	{
		if (float.IsNaN(WarnedAt))
		{
			WarnedAt = now;
		}
	}

	public string CounterText()
	{
		string s = "";
		foreach (Counter c in new[] { Counter.Dodge, Counter.Jump, Counter.Roll, Counter.Parry, Counter.Block })
		{
			if (Has(c))
			{
				s += (s.Length > 0 ? "/" : "") + c.ToString().ToLowerInvariant();
			}
		}
		return s.Length > 0 ? s : "none";
	}
}
