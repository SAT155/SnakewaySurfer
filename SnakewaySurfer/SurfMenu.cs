using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// 1.14.2 (Sam): `surfsup` opens a grid of the places you can start from, one button per biome
// (across) and star lap (down), each in its biome's colours with its boss's summoning item and
// its stars. After a crash: run again from the same start, retry the biome you fell in, or
// go back to the grid.
internal sealed partial class SurfGame
{
	private int m_menuCol;
	private int m_menuRow;
	private readonly HashSet<(int biome, int stars)> m_unlocked = new();
	private int m_menuCols;
	private int m_menuRows;
	// 1.16: the Ocean's column (the crossing before the Plains), or -1.
	private int m_menuSeaCol = -1;
	private static Texture2D? s_white;

	private void EnterMenu()
	{
		m_unlocked.Clear();
		int lap = Plugin.LoopBiomes.Value <= 0 ? m_biomes.Count : Mathf.Clamp(Plugin.LoopBiomes.Value, 1, m_biomes.Count);
		int allowed = Mathf.Clamp(Progress.AllowedBiomes(lap), 1, m_biomes.Count);
		int maxStars = 0;
		m_menuSeaCol = Crossing && allowed > SeaTo ? SeaTo : -1;
		foreach ((int b, int s) in Progress.Checkpoints(m_player))
		{
			if (b < allowed)
			{
				if (m_menuSeaCol >= 0 && b == SeaTo)
				{
					// Beating Moder opens the crossing; making it opens the Plains.
					m_unlocked.Add((m_menuSeaCol, s));
					if (Progress.Landed(m_player, s, SeaTo))
					{
						m_unlocked.Add((m_menuSeaCol + 1, s));
					}
				}
				else
				{
					m_unlocked.Add((MenuColumnOf(b), s));
				}
				maxStars = Mathf.Max(maxStars, s);
			}
		}
		m_unlocked.Add((0, 0));
		m_menuCols = allowed + (m_menuSeaCol >= 0 ? 1 : 0);
		m_menuRows = Plugin.LoopBiomes.Value <= 0 ? 1 : Mathf.Min(4, maxStars + 1);
		int startCol = m_startSea && m_menuSeaCol >= 0 ? m_menuSeaCol : MenuColumnOf(m_startBiome);
		(m_menuCol, m_menuRow) = (Mathf.Clamp(startCol, 0, m_menuCols - 1), Mathf.Clamp(m_startStars, 0, m_menuRows - 1));
		if (!m_unlocked.Contains((m_menuCol, m_menuRow)) && m_menuSeaCol >= 0 && m_menuCol == m_menuSeaCol + 1)
		{
			// The Plains not reached yet this lap: its crossing, then.
			m_menuCol = m_menuSeaCol;
		}
		if (!m_unlocked.Contains((m_menuCol, m_menuRow)))
		{
			(m_menuCol, m_menuRow) = (0, 0);
		}
		m_state = State.Menu;
		m_stateTime = 0f;
	}

	// Only worth showing when there's more than one place to start.
	private bool MenuWorthIt()
	{
		int lap = Plugin.LoopBiomes.Value <= 0 ? m_biomes.Count : Mathf.Clamp(Plugin.LoopBiomes.Value, 1, m_biomes.Count);
		int allowed = Progress.AllowedBiomes(lap);
		int n = 0;
		foreach ((int b, _) in Progress.Checkpoints(m_player))
		{
			if (b < allowed)
			{
				n++;
			}
		}
		return Plugin.StartMenu.Value && n > 1;
	}

	private void UpdateMenu(bool canInput)
	{
		if (!canInput)
		{
			return;
		}
		int dc = KeyDown(KeyCode.A, KeyCode.LeftArrow) ? -1 : KeyDown(KeyCode.D, KeyCode.RightArrow) ? 1 : 0;
		int dr = KeyDown(KeyCode.W, KeyCode.UpArrow) ? -1 : KeyDown(KeyCode.S, KeyCode.DownArrow) ? 1 : 0;
		if (dc != 0 || dr != 0)
		{
			// Step to the next unlocked button that way.
			int c = m_menuCol;
			int r = m_menuRow;
			for (int i = 0; i < 8; i++)
			{
				c = Mathf.Clamp(c + dc, 0, m_menuCols - 1);
				r = Mathf.Clamp(r + dr, 0, m_menuRows - 1);
				if (m_unlocked.Contains((c, r)))
				{
					m_menuCol = c;
					m_menuRow = r;
					m_audio.Play(Sfx.Lane, 0.5f, 1.2f);
					break;
				}
			}
		}
		if (m_stateTime > 0.3f && KeyDown(KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter))
		{
			StartColumn(m_menuCol, m_menuRow);
		}
	}

	// The menu's column for a biome (past the Ocean's column, one further along).
	private int MenuColumnOf(int biome) => m_menuSeaCol >= 0 && biome >= SeaTo ? biome + 1 : biome;

	// The biome a menu column starts in, and whether it's the crossing before it.
	private (int biome, bool sea) MenuColumn(int col) =>
		m_menuSeaCol < 0 || col < m_menuSeaCol ? (col, false) : col == m_menuSeaCol ? (SeaTo, true) : (col - 1, false);

	private void StartColumn(int col, int stars)
	{
		(int biome, bool sea) = MenuColumn(col);
		StartFrom(biome, stars, sea);
	}

	private void StartFrom(int biome, int stars, bool sea = false)
	{
		m_startBiome = biome;
		m_startStars = stars;
		m_startSea = sea;
		// "Run again" keeps this start.
		m_explicitStart = true;
		m_audio.Play(Sfx.Go);
		ResetRun();
	}

	// Where you fell: that biome (a boss arena counts as its biome) and lap, or the crossing
	// (its coast included).
	private (int biome, int stars, bool sea) CrashPlace()
	{
		Segment s = SegAt(m_z);
		if (m_atSea || s.Sea || s.Shore)
		{
			return (SeaTo, s.Stars, true);
		}
		return (s.Biome, s.Rush ? 3 : s.Stars, false);
	}

	private static Texture2D White()
	{
		if (s_white == null)
		{
			s_white = Texture2D.whiteTexture;
		}
		return s_white;
	}

	private static void Fill(Rect r, Color c)
	{
		Color old = GUI.color;
		GUI.color = c;
		GUI.DrawTexture(r, White());
		GUI.color = old;
	}

	// A little gold star: two squares turned against each other.
	private static void Star(Vector2 centre, float size, Color c)
	{
		Matrix4x4 m = GUI.matrix;
		for (int i = 0; i < 2; i++)
		{
			GUIUtility.RotateAroundPivot(i == 0 ? 0f : 45f, centre);
			Fill(new Rect(centre.x - size * 0.35f, centre.y - size * 0.35f, size * 0.7f, size * 0.7f), c);
			GUI.matrix = m;
		}
	}

	private void MenuGui(float k, float w, float h)
	{
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
		Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.45f));
		Shadowed(new Rect(0, h * 0.06f, w, 90 * k), "Where to?", m_big!);

		float gap = 14f * k;
		float bw = Mathf.Min(220f * k, (w * 0.9f - gap * (m_menuCols - 1)) / Mathf.Max(1, m_menuCols));
		float bh = bw * 0.62f;
		float gridW = m_menuCols * bw + (m_menuCols - 1) * gap;
		float gridH = m_menuRows * bh + (m_menuRows - 1) * gap;
		float x0 = (w - gridW) * 0.5f;
		float y0 = Mathf.Max(h * 0.2f, (h - gridH) * 0.5f);
		Event e = Event.current;
		for (int r = 0; r < m_menuRows; r++)
		{
			for (int c = 0; c < m_menuCols; c++)
			{
				Rect rect = new Rect(x0 + c * (bw + gap), y0 + r * (bh + gap), bw, bh);
				bool open = m_unlocked.Contains((c, r));
				bool hover = open && rect.Contains(e.mousePosition);
				if (hover && e.type == EventType.MouseMove && (m_menuCol != c || m_menuRow != r))
				{
					m_menuCol = c;
					m_menuRow = r;
				}
				if (hover && e.type == EventType.MouseDown && e.button == 0)
				{
					e.Use();
					StartColumn(c, r);
					return;
				}
				if (e.type == EventType.Repaint)
				{
					(int biome, bool sea) = MenuColumn(c);
					MenuButton(rect, biome, r, open, open && m_menuCol == c && m_menuRow == r, k, sea);
				}
			}
		}
		Shadowed(new Rect(0, Mathf.Min(h - 70 * k, y0 + gridH + 30 * k), w, 50 * k),
			$"Arrows or mouse to pick, Space or click to run     {Plugin.ExitKey.Value}: back", m_small!);
	}

	// One button: the biome's sky over its ground, its name, its boss's summoning item, and a
	// star for each lap. Locked ones are dark.
	private void MenuButton(Rect r, int biome, int stars, bool open, bool selected, float k, bool sea = false)
	{
		BiomeRuntime b = m_biomes[Mathf.Clamp(biome, 0, m_biomes.Count - 1)];
		float dim = open ? 1f : 0.3f;
		if (sea)
		{
			OceanButton(r, stars, open, selected, k);
			return;
		}
		if (selected)
		{
			float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f);
			Fill(new Rect(r.x - 5 * k, r.y - 5 * k, r.width + 10 * k, r.height + 10 * k), new Color(1f, 0.85f, 0.35f, pulse));
		}
		Color sky = b.Sky * dim;
		sky.a = 1f;
		Color ground = b.Def.Ground * dim;
		ground.a = 1f;
		// Night falls a little with each star.
		sky = Color.Lerp(sky, new Color(0.05f, 0.06f, 0.15f), stars * 0.2f);
		Fill(new Rect(r.x, r.y, r.width, r.height * 0.6f), sky);
		Fill(new Rect(r.x, r.y + r.height * 0.6f, r.width, r.height * 0.4f), ground);
		Fill(new Rect(r.x, r.y + r.height * 0.6f - 2 * k, r.width, 4 * k), Color.Lerp(sky, ground, 0.5f) * 0.8f);
		BossDef? boss = Progress.BossOf(biome);
		if (boss != null && ItemIcon(boss.Item) is Sprite icon)
		{
			float s = r.height * 0.42f;
			DrawSprite(new Rect(r.xMax - s - 6 * k, r.y + 6 * k, s, s), icon, new Color(1f, 1f, 1f, open ? 1f : 0.35f));
		}
		GUIStyle style = m_small!;
		TextAnchor anchor = style.alignment;
		style.alignment = TextAnchor.LowerLeft;
		Color text = style.normal.textColor;
		style.normal.textColor = open ? Color.white : new Color(0.6f, 0.6f, 0.6f);
		Shadowed(new Rect(r.x + 8 * k, r.y, r.width - 16 * k, r.height - 6 * k), b.Def.Name, style);
		style.alignment = anchor;
		style.normal.textColor = text;
		for (int i = 0; i < stars; i++)
		{
			Star(new Vector2(r.x + 18 * k + i * 22 * k, r.y + 18 * k), 18 * k, open ? new Color(1f, 0.82f, 0.2f) : new Color(0.4f, 0.4f, 0.4f));
		}
	}

	// The Ocean's button: sky over grey-green sea with a line of surf, the serpent's trophy.
	private void OceanButton(Rect r, int stars, bool open, bool selected, float k)
	{
		float dim = open ? 1f : 0.3f;
		if (selected)
		{
			float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f);
			Fill(new Rect(r.x - 5 * k, r.y - 5 * k, r.width + 10 * k, r.height + 10 * k), new Color(1f, 0.85f, 0.35f, pulse));
		}
		Color sky = new Color(0.55f * dim, 0.68f * dim, 0.8f * dim, 1f);
		sky = Color.Lerp(sky, new Color(0.05f, 0.06f, 0.15f), stars * 0.2f);
		Color water = new Color(0.1f * dim, 0.27f * dim, 0.33f * dim, 1f);
		Fill(new Rect(r.x, r.y, r.width, r.height * 0.55f), sky);
		Fill(new Rect(r.x, r.y + r.height * 0.55f, r.width, r.height * 0.45f), water);
		Fill(new Rect(r.x, r.y + r.height * 0.55f - 1 * k, r.width, 3 * k), new Color(0.85f * dim, 0.9f * dim, 0.95f * dim, 0.8f));
		if (ItemIcon("TrophySerpent") is Sprite icon)
		{
			float s = r.height * 0.42f;
			DrawSprite(new Rect(r.xMax - s - 6 * k, r.y + 6 * k, s, s), icon, new Color(1f, 1f, 1f, open ? 1f : 0.35f));
		}
		GUIStyle style = m_small!;
		TextAnchor anchor = style.alignment;
		style.alignment = TextAnchor.LowerLeft;
		Color text = style.normal.textColor;
		style.normal.textColor = open ? Color.white : new Color(0.6f, 0.6f, 0.6f);
		Shadowed(new Rect(r.x + 8 * k, r.y, r.width - 16 * k, r.height - 6 * k), "Ocean", style);
		style.alignment = anchor;
		style.normal.textColor = text;
		for (int i = 0; i < stars; i++)
		{
			Star(new Vector2(r.x + 18 * k + i * 22 * k, r.y + 18 * k), 18 * k, open ? new Color(1f, 0.82f, 0.2f) : new Color(0.4f, 0.4f, 0.4f));
		}
	}
}
