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
	// Every IMGUI colour is multiplied by this (menu fades, the loading screen fading out).
	private static float s_guiAlpha = 1f;

	// 1.16.16 (Sam): the menu comes and goes like a dream. It drifts in out of a slow swirl, its
	// buttons float and sway, the one you're on swells with a soft glow, and when you pick one it
	// brightens while the others drift apart and fade.
	private float m_menuOpen;
	private float m_menuClose = -1f;
	private bool m_menuLeaving;
	private (int c, int r) m_menuPicked = (-1, -1);
	private System.Action? m_menuAfter;
	private readonly Dictionary<(int, int), float> m_menuHover = new();
	private const float MenuOpenTime = 1.1f;
	private const float MenuCloseTime = 0.85f;

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
		m_menuOpen = 0f;
		m_menuClose = -1f;
		m_menuLeaving = false;
		m_menuPicked = (-1, -1);
		m_menuAfter = null;
		m_menuHover.Clear();
		m_audio.Play("sfx_demister_start|sfx_runestone_interact", 0.45f, 0.85f);
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
		float udt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
		m_menuOpen += udt;
		for (int r = 0; r < m_menuRows; r++)
		{
			for (int c = 0; c < m_menuCols; c++)
			{
				bool on = m_menuPicked == (c, r) || (m_menuClose < 0f && m_menuCol == c && m_menuRow == r);
				float h = m_menuHover.TryGetValue((c, r), out float v) ? v : 0f;
				m_menuHover[(c, r)] = Mathf.MoveTowards(h, on ? 1f : 0f, udt * (on ? 6f : 3f));
			}
		}
		if (m_menuClose >= 0f)
		{
			m_menuClose += udt;
			if (m_menuClose >= MenuCloseTime && m_menuAfter != null)
			{
				System.Action after = m_menuAfter;
				m_menuAfter = null;
				after();
			}
			return;
		}
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
					MenuHoverSound();
					break;
				}
			}
		}
		if (m_menuOpen > 0.4f && KeyDown(KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter))
		{
			PickMenu(m_menuCol, m_menuRow);
		}
	}

	private void MenuHoverSound() => m_audio.Play("sfx_gui_select|sfx_gui_button", 0.3f, Random.Range(0.92f, 1.08f));

	// The one chosen swells and brightens while the rest drift apart, then the run loads.
	private void PickMenu(int c, int r)
	{
		if (m_menuClose >= 0f)
		{
			return;
		}
		m_menuPicked = (c, r);
		m_menuClose = 0f;
		m_menuAfter = () => StartColumn(c, r);
		m_audio.Play("sfx_runestone_activate|sfx_gui_select", 0.55f);
	}

	// Leaving: everything drifts apart and fades.
	private void CloseMenu(System.Action after)
	{
		m_menuLeaving = true;
		m_menuClose = 0f;
		m_menuAfter = after;
		m_audio.Play("sfx_gui_inventory_close|sfx_gui_button", 0.5f);
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
		if (!Plugin.LoadingScreen.Value)
		{
			m_audio.Play(Sfx.Go);
		}
		BeginLoad(ResetRun);
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
		GUI.color = new Color(c.r, c.g, c.b, c.a * s_guiAlpha);
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

	private static float Smooth(float x)
	{
		x = Mathf.Clamp01(x);
		return x * x * (3f - 2f * x);
	}

	private static float Hash(float x) => Mathf.Repeat(Mathf.Sin(x * 12.9898f) * 43758.547f, 1f);

	private static Vector2 Rotate(Vector2 v, float degrees)
	{
		float a = degrees * Mathf.Deg2Rad;
		float cos = Mathf.Cos(a);
		float sin = Mathf.Sin(a);
		return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
	}

	private void MenuGui(float k, float w, float h)
	{
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
		float t = Time.unscaledTime;
		float baseAlpha = s_guiAlpha;
		float open = Smooth(m_menuOpen / MenuOpenTime);
		float close = m_menuClose < 0f ? 0f : Smooth(m_menuClose / MenuCloseTime);
		float shown = open * (1f - close);

		// The world sinks into a deep violet dusk (deeper still as a run is picked).
		float veil = m_menuLeaving ? 0.55f * open * (1f - close) : Mathf.Lerp(0.55f * open, 0.92f, close);
		Fill(new Rect(0, 0, w, h), new Color(0.05f, 0.02f, 0.1f, veil));
		DreamMotes(w, h, k, t, shown);

		// The title surfaces slowly and breathes.
		float ta = Smooth((m_menuOpen - 0.15f) / 0.9f) * (1f - close);
		s_guiAlpha = baseAlpha * ta * (0.85f + 0.15f * Mathf.Sin(t * 1.7f));
		Shadowed(new Rect(0, h * 0.06f - (1f - ta) * 24f * k + Mathf.Sin(t * 0.9f) * 4f * k, w, 90 * k), "Where to?", m_big!);
		s_guiAlpha = baseAlpha;

		float gap = 14f * k;
		float bw = Mathf.Min(220f * k, (w * 0.9f - gap * (m_menuCols - 1)) / Mathf.Max(1, m_menuCols));
		float bh = bw * 0.62f;
		float gridW = m_menuCols * bw + (m_menuCols - 1) * gap;
		float gridH = m_menuRows * bh + (m_menuRows - 1) * gap;
		float x0 = (w - gridW) * 0.5f;
		float y0 = Mathf.Max(h * 0.2f, (h - gridH) * 0.5f);
		Vector2 centre = new Vector2(w * 0.5f, y0 + gridH * 0.5f);
		bool interactive = m_menuClose < 0f && m_menuOpen > 0.35f;
		Event e = Event.current;
		for (int r = 0; r < m_menuRows; r++)
		{
			for (int c = 0; c < m_menuCols; c++)
			{
				Rect rect = new Rect(x0 + c * (bw + gap), y0 + r * (bh + gap), bw, bh);
				bool unlocked = m_unlocked.Contains((c, r));
				bool hover = interactive && unlocked && rect.Contains(e.mousePosition);
				if (hover && e.type == EventType.MouseMove && (m_menuCol != c || m_menuRow != r))
				{
					m_menuCol = c;
					m_menuRow = r;
					MenuHoverSound();
				}
				if (hover && e.type == EventType.MouseDown && e.button == 0)
				{
					e.Use();
					PickMenu(c, r);
				}
				if (e.type == EventType.Repaint)
				{
					MenuCell(rect, c, r, unlocked, centre, t, k, close, baseAlpha);
				}
			}
		}
		s_guiAlpha = baseAlpha * shown;
		Shadowed(new Rect(0, Mathf.Min(h - 70 * k, y0 + gridH + 30 * k), w, 50 * k),
			$"Arrows or mouse to pick, Space or click to run     {Plugin.ExitKey.Value}: back", m_small!);
		s_guiAlpha = baseAlpha;
	}

	// One button, where the dream has it now.
	private void MenuCell(Rect rect, int c, int r, bool unlocked, Vector2 centre, float t, float k, float close, float baseAlpha)
	{
		float seed = c * 1.37f + r * 2.71f;
		float jitter = Hash(seed + 0.5f);
		float turn = jitter < 0.5f ? 1f : -1f;
		// In one by one, out of a slow swirl near the middle.
		float a = Smooth((m_menuOpen - 0.06f * c - 0.09f * r - 0.15f * jitter) / 0.75f);
		float hv = m_menuHover.TryGetValue((c, r), out float v) ? v : 0f;
		bool picked = !m_menuLeaving && m_menuPicked == (c, r);
		float swirl = (1f - a) * (45f + 35f * jitter) * turn;
		float spread = Mathf.Lerp(0.4f, 1f, a);
		float scale = Mathf.Lerp(0.55f, 1f, a);
		float alpha = a;
		if (close > 0f)
		{
			if (picked)
			{
				scale *= 1f + 0.3f * close;
				alpha *= 1f - Smooth((close - 0.45f) / 0.55f);
			}
			else
			{
				spread *= 1f + 0.6f * close;
				swirl -= 30f * close * turn;
				scale *= 1f - 0.25f * close;
				alpha *= 1f - Mathf.Clamp01(close * 1.6f);
			}
		}
		if (alpha <= 0.01f)
		{
			return;
		}
		Vector2 pos = centre + Rotate((rect.center - centre) * spread, swirl);
		// Afloat: each bobs and sways in its own time.
		pos.y += Mathf.Sin(t * 1.25f + seed * 2f) * 3.5f * k * a;
		pos.x += Mathf.Sin(t * 0.7f + seed) * 2f * k * a;
		float tilt = Mathf.Sin(t * 0.8f + seed * 1.3f) * 1.4f * a + swirl * 0.3f;
		// The one you're on swells and rises.
		scale *= 1f + 0.08f * hv;
		pos.y -= 5f * k * hv;

		Matrix4x4 m = GUI.matrix;
		GUIUtility.RotateAroundPivot(tilt, pos);
		GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), pos);
		Rect at = new Rect(pos.x - rect.width * 0.5f, pos.y - rect.height * 0.5f, rect.width, rect.height);
		if (hv > 0.01f && unlocked)
		{
			// A soft, breathing glow around it.
			s_guiAlpha = baseAlpha * alpha;
			for (int i = 1; i <= 3; i++)
			{
				float grow = (3f + i * 6f + Mathf.Sin(t * 2.6f + i) * 2.5f) * k;
				Fill(new Rect(at.x - grow, at.y - grow, at.width + grow * 2f, at.height + grow * 2f), new Color(0.78f, 0.68f, 1f, 0.13f * hv / i));
			}
		}
		s_guiAlpha = baseAlpha * alpha * (unlocked ? Mathf.Lerp(0.8f, 1f, hv) : 1f);
		(int biome, bool sea) = MenuColumn(c);
		MenuButton(at, biome, r, unlocked, unlocked && m_menuCol == c && m_menuRow == r, k, sea);
		if (picked && close > 0f)
		{
			s_guiAlpha = baseAlpha * alpha;
			Fill(at, new Color(1f, 0.97f, 0.9f, 0.55f * Mathf.Sin(Mathf.Clamp01(close) * Mathf.PI)));
		}
		s_guiAlpha = baseAlpha;
		GUI.matrix = m;
	}

	// Faint motes of light drifting up through the dusk.
	private static void DreamMotes(float w, float h, float k, float t, float alpha)
	{
		if (alpha <= 0.01f)
		{
			return;
		}
		for (int i = 0; i < 28; i++)
		{
			float s1 = Hash(i * 3.1f);
			float s2 = Hash(i * 7.7f + 1f);
			float s3 = Hash(i * 1.3f + 5f);
			float y = 1f - Mathf.Repeat(s1 + t * (0.012f + 0.02f * s2), 1f);
			float x = s3 + Mathf.Sin(t * 0.3f + i) * 0.03f;
			float size = (5f + 9f * s2) * k;
			float twinkle = 0.5f + 0.5f * Mathf.Sin(t * (0.8f + s1) + i);
			Color col = s3 < 0.6f ? new Color(0.8f, 0.74f, 1f) : new Color(1f, 0.88f, 0.62f);
			col.a = alpha * (0.12f + 0.25f * twinkle) * Mathf.Sin(y * Mathf.PI);
			Star(new Vector2(x * w, y * h), size, col);
		}
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
