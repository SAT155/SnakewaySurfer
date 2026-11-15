using UnityEngine;

namespace SnakewaySurfer;

// The game side of the layout layer (SurfLayout.cs): a region's rect on this screen, text drawn
// to fit its region, and the `surflayout` page that outlines every region at once.
internal sealed partial class SurfGame
{
	// Set by the `surflayout` console command.
	internal static bool LayoutDebug;

	private static Rect RectOf(Region r)
	{
		Box b = Layout.Of(r, Screen.width, Screen.height);
		return new Rect(b.X, b.Y, b.W, b.H);
	}

	// Shadowed text in a region, shrunk (not under `min` pixels) until it fits the box.
	private static void FitShadowed(Region region, string text, GUIStyle style, int min = 12)
	{
		Rect r = RectOf(region);
		int size = style.fontSize;
		GUIContent content = new GUIContent(text);
		for (int i = 0; i < 6; i++)
		{
			Vector2 need = style.CalcSize(content);
			float needW = style.wordWrap ? r.width : need.x;
			float needH = style.wordWrap ? style.CalcHeight(content, r.width) : need.y;
			int fit = Layout.FitSize(style.fontSize, needW, needH, r.width, r.height, min);
			if (fit >= style.fontSize)
			{
				break;
			}
			style.fontSize = fit;
		}
		Shadowed(r, text, style);
		style.fontSize = size;
	}

	// Every region outlined and named, all at once (surflayout), over whatever is showing.
	private void LayoutDebugGui()
	{
		if (!LayoutDebug || Event.current.type != EventType.Repaint)
		{
			return;
		}
		m_layoutLabel ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Bold };
		m_layoutLabel.fontSize = Mathf.Max(12, Mathf.RoundToInt(18 * Screen.height / 1080f));
		int i = 0;
		foreach (Region region in Layout.All)
		{
			Rect r = RectOf(region);
			Color c = Color.HSVToRGB(i++ * 0.13f % 1f, 0.8f, 1f);
			Fill(r, new Color(c.r, c.g, c.b, 0.12f));
			Fill(new Rect(r.x, r.y, r.width, 2), c);
			Fill(new Rect(r.x, r.yMax - 2, r.width, 2), c);
			Fill(new Rect(r.x, r.y, 2, r.height), c);
			Fill(new Rect(r.xMax - 2, r.y, 2, r.height), c);
			m_layoutLabel.normal.textColor = c;
			GUI.Label(new Rect(r.x + 6, r.y + 4, r.width - 12, 40), $"{region}  {r.width:0}x{r.height:0}", m_layoutLabel);
		}
	}

	private GUIStyle? m_layoutLabel;
}
