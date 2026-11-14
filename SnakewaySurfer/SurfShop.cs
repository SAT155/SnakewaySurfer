using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.110 (SAT: "build the shop and make use of coins"; design in the trader abilities doc):
// coins you pick up go into a bank kept on your character. The traders by the track (any lane
// now) stop the run and open a dreamy store window with three of their abilities. An ability
// lasts until you beat that biome's boss or the run ends; a checkpoint retry keeps it. Its item
// icon shows by the lives.
internal sealed partial class SurfGame
{
	private sealed class Ability
	{
		public string Id = "";
		public string Name = "";
		public string Icon = "";
		public string Text = "";
		public int Price;
		public string Trader = "";
	}

	private static readonly Ability[] s_abilities =
	{
		new Ability { Id = "honey", Name = "Honey snack", Icon = "Honey", Text = "+1 life right away", Price = 30, Trader = "BogWitch" },
		new Ability { Id = "raspberry", Name = "Raspberry bush luck", Icon = "Raspberry", Text = "Coins picked up in quick succession give +1 extra each", Price = 35, Trader = "Haldor" },
		new Ability { Id = "deer", Name = "Deer hide boots", Icon = "DeerHide", Text = "Lane changes are 20% quicker", Price = 40, Trader = "Haldor" },
		new Ability { Id = "boar", Name = "Boar sniff", Icon = "TrophyBoar", Text = "Summoning items turn up more often", Price = 40, Trader = "Hildir" },
		new Ability { Id = "meadstam", Name = "Mead of stamina", Icon = "MeadStaminaMinor", Text = "Jumping, rolling and blocking cost half the stamina", Price = 70, Trader = "BogWitch" },
		new Ability { Id = "troll", Name = "Troll leather", Icon = "TrollHide", Text = "One free hit: the first knock does no harm", Price = 75, Trader = "Haldor" },
		new Ability { Id = "feather", Name = "Feather fall", Icon = "Feathers", Text = "Jumps float a little longer", Price = 80, Trader = "Hildir" },
		new Ability { Id = "magnet", Name = "Coin magnet", Icon = "Coins", Text = "Coins in the next lane fly to you", Price = 90, Trader = "Haldor" },
		new Ability { Id = "meadheal", Name = "Mead of healing", Icon = "MeadHealthMinor", Text = "Each blow you land on a boss gives back 1 life", Price = 120, Trader = "BogWitch" },
		new Ability { Id = "serpent", Name = "Serpent scale shield", Icon = "SerpentScale", Text = "Parry window is twice as long", Price = 140, Trader = "Hildir" },
		new Ability { Id = "golden", Name = "Golden hat", Icon = "HelmetYule", Text = "Every coin counts double", Price = 260, Trader = "Haldor" },
		new Ability { Id = "valkyrie", Name = "Valkyrie's wings", Icon = "TrophyGoblinKing", Text = "On your last life, a fatal blow lifts you back to the middle lane once", Price = 300, Trader = "Hildir" },
	};

	private const string BankKey = "SnakewaySurfer.Bank";
	private const string OwnedKey = "SnakewaySurfer.Abilities";
	private readonly HashSet<string> m_owned = new();
	private int m_coinsBanked;
	private bool m_shopOpen;
	private TraderStop? m_shopTrader;
	private readonly List<Ability> m_shopStock = new();
	private float m_shopTime;
	private float m_lastCoinAt = -10f;
	private bool m_trollUsed;
	private bool m_valkyrieUsed;
	private readonly Dictionary<string, Sprite?> m_shopIcons = new();

	private bool Has(string id) => m_owned.Contains(id);

	private float ParryWindowNow => Plugin.ParryWindow.Value * (Has("serpent") ? 2f : 1f);

	private int Bank
	{
		get => m_player != null && m_player.m_customData.TryGetValue(BankKey, out string v) && int.TryParse(v, out int n) ? n : 0;
		set
		{
			if (m_player != null)
			{
				m_player.m_customData[BankKey] = Mathf.Max(0, value).ToString();
			}
		}
	}

	// This run's coins not yet in the bank go in (at a shop and when the run ends).
	private void DepositCoins()
	{
		if (m_coins > m_coinsBanked)
		{
			Bank += m_coins - m_coinsBanked;
		}
		m_coinsBanked = m_coins;
	}

	private int CoinGain()
	{
		int n = Has("golden") ? 2 : 1;
		if (Has("raspberry") && m_runTime - m_lastCoinAt < 0.9f)
		{
			n++;
		}
		m_lastCoinAt = m_runTime;
		return n;
	}

	private void SaveOwned()
	{
		if (m_player != null)
		{
			m_player.m_customData[OwnedKey] = string.Join(",", m_owned);
		}
	}

	// In ResetRun: a fresh run starts with none; a checkpoint retry keeps them.
	private void ResetShop(bool resuming)
	{
		m_coinsBanked = 0;
		m_shopOpen = false;
		m_trollUsed = false;
		m_valkyrieUsed = false;
		m_owned.Clear();
		if (resuming && m_player != null && m_player.m_customData.TryGetValue(OwnedKey, out string v))
		{
			foreach (string id in v.Split(','))
			{
				if (id.Length > 0)
				{
					m_owned.Add(id);
				}
			}
		}
		SaveOwned();
	}

	// Beating a boss ends what you bought in its biome.
	private void AbilitiesEnd()
	{
		m_owned.Clear();
		SaveOwned();
	}

	private void OpenShop(TraderStop t)
	{
		DepositCoins();
		int stars = SegAt(m_z).Stars;
		List<Ability> pool = new();
		foreach (Ability a in s_abilities)
		{
			if (a.Trader == t.Def.Prefab && (a.Price < 200 || stars >= 2) && (a.Id == "honey" || !m_owned.Contains(a.Id)))
			{
				pool.Add(a);
			}
		}
		m_shopStock.Clear();
		while (pool.Count > 0 && m_shopStock.Count < 3)
		{
			int i = Random.Range(0, pool.Count);
			m_shopStock.Add(pool[i]);
			pool.RemoveAt(i);
		}
		m_shopStock.Sort((x, y) => x.Price.CompareTo(y.Price));
		if (m_shopStock.Count == 0)
		{
			return;
		}
		m_shopTrader = t;
		m_shopOpen = true;
		m_shopTime = 0f;
		m_audio.Play("sfx_gui_inventory_open|sfx_gui_button", 0.7f);
	}

	private void CloseShop()
	{
		m_shopOpen = false;
		m_shopTrader = null;
		// A moment's grace as the run picks up again.
		m_hurtTime = Mathf.Max(m_hurtTime, 3f);
		m_audio.Play("sfx_gui_inventory_close|sfx_gui_button", 0.6f);
	}

	private void Buy(Ability a)
	{
		if (Bank < a.Price || (a.Id != "honey" && m_owned.Contains(a.Id)))
		{
			m_audio.Play("sfx_gui_inventory_close|sfx_gui_button", 0.6f, 0.7f);
			return;
		}
		Bank -= a.Price;
		if (a.Id == "honey")
		{
			m_lives = Mathf.Min(Plugin.StartLives + 1, m_lives + 1);
			m_livesFlash = 1.2f;
		}
		else
		{
			m_owned.Add(a.Id);
			SaveOwned();
		}
		m_audio.Play("sfx_coins_placed|sfx_gui_craftitem_end", 0.9f);
		if (m_shopTrader?.Anim != null)
		{
			SetTrigger(m_shopTrader.Anim, "Talk");
		}
		if (m_shopTrader != null)
		{
			PlayTraderSound(m_shopTrader, 1, 0.9f);
		}
	}

	// Each frame while the window is open, instead of the run.
	private void UpdateShop(float dt, bool canInput)
	{
		m_shopTime += dt;
		m_speed = 0f;
		if (!canInput || m_shopTime < 0.4f)
		{
			return;
		}
		for (int i = 0; i < m_shopStock.Count; i++)
		{
			if (Input.GetKeyDown(KeyCode.Alpha1 + i))
			{
				Buy(m_shopStock[i]);
			}
		}
		if (KeyDown(KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter, KeyCode.E))
		{
			CloseShop();
		}
	}

	private Sprite? IconOf(string item)
	{
		if (!m_shopIcons.TryGetValue(item, out Sprite? s))
		{
			GameObject? prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item) : null;
			ItemDrop? drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
			s = drop != null ? drop.m_itemData.GetIcon() : null;
			m_shopIcons[item] = s;
		}
		return s;
	}

	private static void DrawSprite(Rect r, Sprite? s, float alpha)
	{
		if (s == null || s.texture == null)
		{
			return;
		}
		Rect tr = s.textureRect;
		Texture t = s.texture;
		Color old = GUI.color;
		GUI.color = new Color(1f, 1f, 1f, alpha);
		GUI.DrawTextureWithTexCoords(r, t, new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height));
		GUI.color = old;
	}

	// The game's store window, dreamed: the trader's wares in rows with icon, words and price.
	private void ShopGui(float k, float w, float h)
	{
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
		float a = Mathf.SmoothStep(0f, 1f, m_shopTime / 0.35f) * s_guiAlpha;
		Fill(new Rect(0, 0, w, h), new Color(0.05f, 0.02f, 0.1f, 0.45f * a));
		// 1.16.150 (SAT: long notes ran over the names): wider, taller rows, notes in smaller type
		// under the name with room for two lines.
		float pw = Mathf.Min(900f * k, w * 0.92f);
		float rowH = 112f * k;
		float ph = 120f * k + rowH * m_shopStock.Count + 70f * k;
		Rect p = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f + Mathf.Sin(Time.unscaledTime * 1.3f) * 3f * k, pw, ph);
		Fill(p, new Color(0.12f, 0.08f, 0.16f, 0.88f * a));
		Fill(new Rect(p.x, p.y, p.width, 3f * k), new Color(0.95f, 0.78f, 0.35f, a));
		Fill(new Rect(p.x, p.yMax - 3f * k, p.width, 3f * k), new Color(0.95f, 0.78f, 0.35f, a));
		float old = s_guiAlpha;
		s_guiAlpha = a;
		string who = m_shopTrader?.Def.Prefab switch { "BogWitch" => "The Bog Witch", string n => n, null => "Trader" };
		Shadowed(new Rect(p.x, p.y + 14f * k, p.width, 50f * k), who, m_mid!);
		DrawSprite(new Rect(p.x + p.width * 0.5f - 70f * k, p.y + 66f * k, 30f * k, 30f * k), IconOf("Coins"), a);
		Shadowed(new Rect(p.x + p.width * 0.5f - 34f * k, p.y + 62f * k, 200f * k, 40f * k), Bank.ToString(), LeftStyle(m_mid!));
		Event e = Event.current;
		for (int i = 0; i < m_shopStock.Count; i++)
		{
			Ability ab = m_shopStock[i];
			Rect row = new Rect(p.x + 20f * k, p.y + 112f * k + i * rowH, p.width - 40f * k, rowH - 10f * k);
			bool owned = ab.Id != "honey" && m_owned.Contains(ab.Id);
			bool afford = Bank >= ab.Price && !owned;
			bool hover = row.Contains(e.mousePosition);
			Fill(row, new Color(1f, 1f, 1f, (hover && afford ? 0.12f : 0.05f) * a));
			DrawSprite(new Rect(row.x + 10f * k, row.y + 10f * k, row.height - 20f * k, row.height - 20f * k), IconOf(ab.Icon), afford || owned ? a : a * 0.5f);
			float tx = row.x + row.height + 6f * k;
			Shadowed(new Rect(tx, row.y + 4f * k, row.xMax - 196f * k - tx, 38f * k), $"{i + 1}. {ab.Name}", LeftStyle(m_mid!));
			m_shopNote ??= new GUIStyle(m_small!) { alignment = TextAnchor.UpperLeft, wordWrap = true };
			m_shopNote.fontSize = Mathf.RoundToInt(m_small!.fontSize * 0.8f);
			Shadowed(new Rect(tx, row.y + 44f * k, row.xMax - 196f * k - tx, rowH - 54f * k), ab.Text, m_shopNote);
			Shadowed(new Rect(row.xMax - 150f * k, row.y + 20f * k, 140f * k, 40f * k), owned ? "Owned" : ab.Price.ToString(), LeftStyle(m_mid!));
			if (!owned)
			{
				DrawSprite(new Rect(row.xMax - 186f * k, row.y + 26f * k, 28f * k, 28f * k), IconOf("Coins"), afford ? a : a * 0.4f);
			}
			if (hover && afford && e.type == EventType.MouseDown && e.button == 0 && m_shopTime > 0.4f)
			{
				e.Use();
				Buy(ab);
			}
		}
		Rect leave = new Rect(p.x, p.yMax - 56f * k, p.width, 40f * k);
		if (leave.Contains(e.mousePosition) && e.type == EventType.MouseDown && e.button == 0 && m_shopTime > 0.4f)
		{
			e.Use();
			CloseShop();
		}
		Shadowed(leave, "1-3 or click to buy     Space or click here: run on", m_small!);
		s_guiAlpha = old;
	}

	private GUIStyle? m_shopNote;

	private static readonly Dictionary<GUIStyle, GUIStyle> s_left = new();

	private static GUIStyle LeftStyle(GUIStyle s)
	{
		if (!s_left.TryGetValue(s, out GUIStyle l))
		{
			l = new GUIStyle(s) { alignment = TextAnchor.MiddleLeft, wordWrap = true };
			s_left[s] = l;
		}
		return l;
	}

	// The icons of what you own, small, by the lives.
	private void AbilityIconsGui(float x, float y, float k)
	{
		foreach (Ability ab in s_abilities)
		{
			if (m_owned.Contains(ab.Id))
			{
				DrawSprite(new Rect(x, y, 30f * k, 30f * k), IconOf(ab.Icon), 0.9f * s_guiAlpha);
				x += 34f * k;
			}
		}
	}
}
