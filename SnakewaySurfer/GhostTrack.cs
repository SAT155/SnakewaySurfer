using System;
using System.Collections.Generic;
using System.IO;

namespace SnakewaySurfer;

// 1.18 (Raven Race ghosts, step 1): a Journey run as it was run, to race against later.
// Where the runner was (lane offset x, height y, distance z) ten times a second, with a flag
// for a roll started and one for "off the open road" (at sea, in a dungeon, at a camp), where
// the ghost isn't shown. Recording stops when the boss stage starts; Time is the win time.
// Unity-free, so the checks on every build (SnakewaySurfer.Tests) compile it.
internal struct GhostSample
{
	public float T;
	public float X;
	public float Y;
	public float Z;
	public byte Flags;
}

internal sealed class GhostTrack
{
	public const byte Rolled = 1;
	public const byte Hidden = 2;
	public const float Step = 0.1f;
	// About 40 minutes: a run longer than that stops recording (the ghost just ends early).
	public const int MaxSamples = 24000;
	private const int Magic = 0x31475353; // "SSG1"
	private const int FileVersion = 1;

	public int Biome;
	public int Stars;
	// The win time in seconds (0 until won).
	public float Time;
	public readonly List<GhostSample> Samples = new();

	private float m_next;
	private byte m_pending;

	public float LastT => Samples.Count > 0 ? Samples[Samples.Count - 1].T : 0f;

	// Each frame while recording. A roll between two samples is kept for the next one.
	public void Record(float t, float x, float y, float z, byte flags)
	{
		m_pending |= (byte)(flags & Rolled);
		if (Samples.Count >= MaxSamples || (Samples.Count > 0 && t < m_next))
		{
			return;
		}
		Samples.Add(new GhostSample { T = t, X = x, Y = y, Z = z, Flags = (byte)((flags & Hidden) | m_pending) });
		m_pending = 0;
		m_next = t + Step;
	}

	// The last sample at or before t (-1 before the first).
	public int IndexAt(float t)
	{
		int lo = 0;
		int hi = Samples.Count - 1;
		int found = -1;
		while (lo <= hi)
		{
			int mid = (lo + hi) / 2;
			if (Samples[mid].T <= t)
			{
				found = mid;
				lo = mid + 1;
			}
			else
			{
				hi = mid - 1;
			}
		}
		return found;
	}

	// Where the ghost is at time t, eased between samples. False before the first sample or
	// after the last (the ghost has gone on to its boss).
	public bool At(float t, out float x, out float y, out float z, out bool hidden)
	{
		x = y = z = 0f;
		hidden = true;
		int i = IndexAt(t);
		if (i < 0 || t > LastT)
		{
			return false;
		}
		GhostSample a = Samples[i];
		GhostSample b = i + 1 < Samples.Count ? Samples[i + 1] : a;
		float span = b.T - a.T;
		float f = span > 0.0001f ? Math.Min(1f, Math.Max(0f, (t - a.T) / span)) : 0f;
		x = a.X + (b.X - a.X) * f;
		y = a.Y + (b.Y - a.Y) * f;
		z = a.Z + (b.Z - a.Z) * f;
		hidden = ((a.Flags | b.Flags) & Hidden) != 0;
		return true;
	}

	// Whether a roll started in samples (from, to].
	public bool RolledBetween(int from, int to)
	{
		for (int i = Math.Max(0, from + 1); i <= to && i < Samples.Count; i++)
		{
			if ((Samples[i].Flags & Rolled) != 0)
			{
				return true;
			}
		}
		return false;
	}

	public void Write(Stream stream)
	{
		using BinaryWriter w = new BinaryWriter(stream);
		w.Write(Magic);
		w.Write(FileVersion);
		w.Write(Biome);
		w.Write(Stars);
		w.Write(Time);
		w.Write(Samples.Count);
		foreach (GhostSample s in Samples)
		{
			w.Write(s.T);
			w.Write(s.X);
			w.Write(s.Y);
			w.Write(s.Z);
			w.Write(s.Flags);
		}
	}

	// Null for anything that isn't a whole ghost file of this version.
	public static GhostTrack? Read(Stream stream)
	{
		try
		{
			using BinaryReader r = new BinaryReader(stream);
			if (r.ReadInt32() != Magic || r.ReadInt32() != FileVersion)
			{
				return null;
			}
			GhostTrack g = new GhostTrack { Biome = r.ReadInt32(), Stars = r.ReadInt32(), Time = r.ReadSingle() };
			int count = r.ReadInt32();
			if (count < 0 || count > MaxSamples || !(g.Time > 0f))
			{
				return null;
			}
			for (int i = 0; i < count; i++)
			{
				g.Samples.Add(new GhostSample { T = r.ReadSingle(), X = r.ReadSingle(), Y = r.ReadSingle(), Z = r.ReadSingle(), Flags = r.ReadByte() });
			}
			return g;
		}
		catch (EndOfStreamException)
		{
			return null;
		}
	}

	// A new run beats the saved one when it's faster (or there's none).
	public static bool Beats(float time, GhostTrack? best) => time > 0f && (best == null || time < best.Time - 0.01f);
}
