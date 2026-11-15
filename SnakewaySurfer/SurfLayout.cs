using System;

namespace SnakewaySurfer;

// The interface layout layer (architecture step 4): fixed screen regions instead of rects placed
// by hand at each draw call. Stats top left, Hugin top right, the boss bar top centre, banners and
// hints in the middle, prompts and key help at the bottom, the crash screen's panel in the centre.
// Each region is a box in pixels for a screen of w x h (k = h / 1080, the scale the whole HUD
// uses); text drawn into a region is shrunk until it fits its box (SurfGame.FitShadowed).
// `surflayout` in the console outlines every region with its name, all at once.
//
// The numbers are the ones the hand-placed rects used, so nothing moved; draw calls move over to
// regions a few at a time (the rest of the ~120 rects are still placed by hand).
//
// It knows nothing of Unity, so the same file is compiled into the checks that run on every build
// (SnakewaySurfer.Tests): every region stays on screen and regions that share a screen don't
// overlap, at the common resolutions.
internal enum Region
{
	// Biome, distance, coins, speed, lives (top left).
	Stats,
	// Hugin's picture and speech bubble (right, under the minimap).
	Hugin,
	// A boss's or sea boss's name over its health bar (top centre).
	BossName,
	// The big countdown number.
	Countdown,
	// Big banner text (upper middle).
	Banner,
	// A hint (middle).
	Hint,
	// Key help under the countdown (lower middle).
	Prompts,
	// The crash screen: its dark panel, and the cause, summary and keys inside it.
	CrashPanel,
	CrashCause,
	CrashSummary,
	CrashKeys,
	// 1.17: a Journey's biome and stage with its three pips (top centre, under the stats).
	Stage,
	// A Journey's win screen: its panel, and the title, summary and keys inside it.
	WinPanel,
	WinTitle,
	// The three Journey marks, under the title.
	WinMarks,
	WinSummary,
	WinKeys,
}

internal struct Box
{
	public float X, Y, W, H;

	public Box(float x, float y, float w, float h)
	{
		X = x;
		Y = y;
		W = w;
		H = h;
	}

	public float Right => X + W;
	public float Bottom => Y + H;

	public bool Overlaps(Box o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

	public bool Inside(float w, float h) => X >= -0.5f && Y >= -0.5f && Right <= w + 0.5f && Bottom <= h + 0.5f;
}

internal static class Layout
{
	public static readonly Region[] All = (Region[])Enum.GetValues(typeof(Region));

	// Regions that can show at the same time, so must not overlap. (A banner or hint shows only
	// while Hugin doesn't talk; the boss name is centred text under the stats' left-aligned lines.)
	public static readonly (Region a, Region b)[] Together =
	{
		(Region.Stats, Region.Hugin), (Region.Stats, Region.Banner), (Region.Stats, Region.Hint), (Region.Banner, Region.Hint),
		(Region.Countdown, Region.Prompts), (Region.Stats, Region.Prompts),
		(Region.CrashCause, Region.CrashSummary), (Region.CrashSummary, Region.CrashKeys), (Region.CrashCause, Region.CrashKeys),
		(Region.Stage, Region.Stats), (Region.Stage, Region.Hugin), (Region.Stage, Region.Banner), (Region.Stage, Region.Hint),
		(Region.WinTitle, Region.WinSummary), (Region.WinSummary, Region.WinKeys), (Region.WinTitle, Region.WinKeys),
		(Region.WinTitle, Region.WinMarks), (Region.WinMarks, Region.WinSummary), (Region.WinMarks, Region.WinKeys),
	};

	public static Box Of(Region r, float w, float h)
	{
		float k = h / 1080f;
		switch (r)
		{
			case Region.Stats:
				// (The text's two lines, then the health bar, ability icons and wet mark under it.)
				return new Box(24 * k, 18 * k, Math.Min(1400 * k, w - 48 * k), 130 * k);
			case Region.Hugin:
			{
				// (SurfRavenUi.Draw: never under 0.6 scale, so it stays readable on small screens;
				// the picture at the right, its bubble below it reaching left.)
				float hk = Math.Max(0.6f, k);
				float size = 150f * hk;
				float x = w - size - 28f * hk;
				float bw = Math.Min(460f * hk, w * 0.4f);
				float bx = Math.Min(w - bw - 20f * hk, x + size - bw * 0.85f);
				float left = Math.Min(x, bx);
				float right = Math.Max(x + size, bx + bw);
				return new Box(left, h * 0.27f, right - left, size + 8f * hk + 160f * hk);
			}
			case Region.BossName:
				return new Box(w * 0.3f, 22 * k, w * 0.4f, 40 * k);
			case Region.Countdown:
				return new Box(0, h * 0.25f, w, 120 * k);
			case Region.Banner:
				// (Centred where the old 300-high box centred it, 0.2 h + 60, but only as tall as two
				// big lines, so it clears the stats; longer text shrinks to fit.)
				return new Box(0, h * 0.2f - 30 * k, w, 180 * k);
			case Region.Hint:
				return new Box(0, h * 0.52f, w, 120 * k);
			case Region.Prompts:
				return new Box(w * 0.15f, h * 0.62f, w * 0.7f, 200 * k);
			case Region.CrashPanel:
				return new Box(w * 0.08f, h * 0.17f, w * 0.84f, h * 0.7f);
			case Region.CrashCause:
				return new Box(w * 0.1f, h * 0.2f, w * 0.8f, 120 * k);
			case Region.CrashSummary:
				return new Box(w * 0.1f, h * 0.32f, w * 0.8f, Math.Min(300 * k, h * 0.68f - h * 0.32f));
			case Region.CrashKeys:
				return new Box(w * 0.1f, h * 0.68f, w * 0.8f, Math.Min(140 * k, h * 0.87f - h * 0.68f));
			case Region.Stage:
				// (Between the stats' box and the banner's, which starts at 186 k.)
				return new Box(w * 0.3f, 152 * k, w * 0.4f, 30 * k);
			case Region.WinPanel:
				return Of(Region.CrashPanel, w, h);
			case Region.WinTitle:
				return Of(Region.CrashCause, w, h);
			case Region.WinMarks:
				// (The top of the crash summary's box.)
				return new Box(w * 0.1f, h * 0.32f, w * 0.8f, 50 * k);
			case Region.WinSummary:
			{
				Box s = Of(Region.CrashSummary, w, h);
				return new Box(s.X, s.Y + 56 * k, s.W, s.H - 56 * k);
			}
			case Region.WinKeys:
				return Of(Region.CrashKeys, w, h);
			default:
				return new Box(0, 0, w, h);
		}
	}

	// The font size that makes text measured at `size` (needW x needH) fit a box, never under
	// `min`. Text already fitting keeps its size.
	public static int FitSize(int size, float needW, float needH, float boxW, float boxH, int min)
	{
		if (size <= min || (needW <= boxW + 0.5f && needH <= boxH + 0.5f))
		{
			return size;
		}
		float scale = Math.Min(needW > 0f ? boxW / needW : 1f, needH > 0f ? boxH / needH : 1f);
		int fit = (int)Math.Floor(size * Math.Min(1f, scale));
		return Math.Max(min, Math.Min(size - 1, fit));
	}
}
