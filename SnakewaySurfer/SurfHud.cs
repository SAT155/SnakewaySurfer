using HarmonyLib;
using UnityEngine;

namespace SnakewaySurfer;

// 1.14 (SAT): the run speaks through sounds, flashes and the HUD instead of pop-up text.
// Stamina for sprinting and blocking, parries, the red hit flash, the summoning item's icon with
// its count, and the boss's health bar.
internal sealed partial class SurfGame
{
	// ---------------------------------------------------------------- stamina, sprint, block

	private float m_stamina = 100f;
	private float m_staminaIdle;
	private bool m_sprinting;
	private bool m_blocking;
	private float m_blockPressed = -99f;
	private float m_sprintLook;
	// 1.16.1 (SAT): a bat in the face knocks the guard down for this long.
	private float m_guardDown;
	// How far the block's slow-down has eased in (0..1).
	private float m_blockSlow;
	// Skill XP earned this run, given when it ends: Blocking (1 a block, 2 a parry, as the game
	// gives) and Dodge (1 a dodged attack).
	private float m_blockXp;
	private float m_dodgeXp;

	private static float MaxStamina => Mathf.Max(10f, Plugin.MaxStamina.Value);

	// When a roll was refused for want of stamina (the bar flashes red a moment).
	private float m_staminaShort = -99f;

	// 1.16.69 (SAT: "Roll should cost stamina"): false, with a tired grunt and the bar flashing,
	// when there isn't enough left.
	private bool SpendRollStamina() => SpendMoveStamina(Plugin.RollStamina.Value);

	// 1.16.x (playtest: jumping was free while rolling cost stamina): a jump costs stamina too,
	// as in the game, and is refused the same way when there isn't enough.
	private bool SpendJumpStamina() => SpendMoveStamina(Plugin.JumpStamina.Value);

	private bool SpendMoveStamina(float baseCost)
	{
		float cost = baseCost * (Has("meadstam") ? 0.5f : 1f) * WetStaminaCost;
		if (cost <= 0f)
		{
			return true;
		}
		if (m_stamina < cost)
		{
			m_staminaShort = Time.time;
			m_audio.Play("sfx_gui_inventory_close|sfx_gui_button", 0.6f, 0.7f);
			return false;
		}
		m_stamina -= cost;
		m_staminaIdle = 0f;
		return true;
	}

	private void ResetMoves()
	{
		m_stamina = MaxStamina;
		m_staminaIdle = 0f;
		m_sprinting = false;
		m_blocking = false;
		m_blockPressed = -99f;
		m_guardDown = 0f;
		m_blockSlow = 0f;
		m_blockXp = 0f;
		m_dodgeXp = 0f;
		m_flash2 = 0f;
		m_iconPulse = 0f;
		AvatarBool("blocking", false);
	}

	// Sprint and block from the keys; returns how they change the running speed.
	private float UpdateMoves(float dt, bool canInput)
	{
		m_guardDown = Mathf.Max(0f, m_guardDown - dt);
		bool block = canInput && (Input.GetKey(Plugin.BlockKey.Value) || m_botBlock) && m_guardDown <= 0f;
		if (block && !m_blocking)
		{
			m_blockPressed = m_runTime;
		}
		m_blocking = block;
		if (m_blocking)
		{
			// 1.16.1 (SAT): holding the shield up tires you.
			m_stamina = Mathf.Max(0f, m_stamina - Plugin.BlockHoldStamina.Value * dt);
		}
		bool sprint = canInput && !block && Input.GetKey(Plugin.SprintKey.Value);
		// A sprint needs a little stamina to start, then runs until it's gone.
		m_sprinting = sprint && m_stamina > (m_sprinting ? 0f : 8f);
		if (m_sprinting)
		{
			m_stamina = Mathf.Max(0f, m_stamina - Plugin.SprintStamina.Value * dt);
		}
		if (m_sprinting || m_blocking)
		{
			m_staminaIdle = 0f;
		}
		else
		{
			m_staminaIdle += dt;
			if (m_staminaIdle > 1f)
			{
				m_stamina = Mathf.Min(MaxStamina, m_stamina + Plugin.StaminaRegen.Value * WetStaminaRegen * dt);
			}
		}
		AvatarBool("blocking", m_blocking);
		m_sprintLook = Mathf.MoveTowards(m_sprintLook, m_sprinting ? 1f : 0f, dt * 4f);
		// Slower behind the shield (1.16.1, SAT: it helps to pick a way through), easing in and
		// out over a quarter of a second.
		m_blockSlow = Mathf.MoveTowards(m_blockSlow, m_blocking ? 1f : 0f, dt * 4f);
		return m_sprinting ? Plugin.SprintSpeed.Value : Mathf.Lerp(1f, Plugin.BlockSpeed.Value, m_blockSlow);
	}

	// A creature's hit while you hold block: a parry if block went up just before it, else
	// a block that costs stamina. Logs, gas and the like can't be blocked. True: no harm done.
	private bool TryBlock(Thing t)
	{
		if (!m_blocking || t.Label.Length == 0 || t.JumpOrSlide)
		{
			return false;
		}
		// Heavy blows (trolls, abominations, smashes, shockwaves) can't be blocked, only parried.
		bool heavy = t.Heavy || t.Smashes > 0 || t.Rise;
		if (m_runTime - m_blockPressed <= ParryWindowNow)
		{
			t.Collidable = false;
			if (t.Anim != null)
			{
				t.Anim.SetTrigger("stagger");
			}
			else if (m_bossAnim != null && m_boss != null && t.Label == m_boss.Name)
			{
				SetTrigger(m_bossAnim, "stagger");
			}
			PlayBlock(true);
			Flash(new Color(1f, 0.95f, 0.75f), 0.55f);
			m_shake = Mathf.Max(m_shake, 0.15f);
			// A parry also gives back a little breath.
			m_stamina = Mathf.Min(MaxStamina, m_stamina + 10f);
			m_blockXp += 2f;
			Plugin.Log.LogInfo($"Parried {t.Label}.");
			return true;
		}
		float cost = Plugin.BlockStamina.Value * (Has("meadstam") ? 0.5f : 1f) * WetStaminaCost;
		m_staminaIdle = 0f;
		if (heavy)
		{
			// Too heavy: it smashes through the guard.
			m_audio.Play("sfx_unarmed_blocked|sfx_wood_blocked", 1f, 0.7f);
			return false;
		}
		if (m_stamina < cost)
		{
			// Guard broken: the hit gets through.
			m_stamina = 0f;
			m_audio.Play("sfx_unarmed_blocked|sfx_wood_blocked", 1f, 0.8f);
			return false;
		}
		m_stamina -= cost;
		t.Collidable = false;
		m_blockXp += 1f;
		PlayBlock(false);
		Flash(new Color(1f, 0.85f, 0.4f), 0.25f);
		m_shake = Mathf.Max(m_shake, 0.25f);
		// The blow still knocks you back a step.
		m_slowTime = Mathf.Max(m_slowTime, 0.4f);
		SetTrigger("stagger");
		return true;
	}

	private void PlayBlock(bool parry)
	{
		// The game's own block sounds: your shield's (or weapon's), and the perfect block.
		EffectList? effects = null;
		try
		{
			if (parry)
			{
				effects = Traverse.Create(m_player).Field("m_perfectBlockEffect").GetValue<EffectList>();
			}
			else
			{
				ItemDrop.ItemData? blocker = Traverse.Create(m_player).Method("GetCurrentBlocker").GetValue<ItemDrop.ItemData>();
				effects = blocker?.m_shared != null ? Traverse.Create(blocker.m_shared).Field("m_blockEffect").GetValue<EffectList>() : null;
			}
		}
		catch
		{
			effects = null;
		}
		if (effects?.m_effectPrefabs != null && effects.m_effectPrefabs.Length > 0)
		{
			m_audio.PlayEffects(effects, 1f);
		}
		else
		{
			m_audio.Play(parry ? "sfx_perfectblock" : "sfx_metal_shield_blocked|sfx_wood_blocked|sfx_unarmed_blocked");
		}
	}

	// The game's own hurt sounds for your character.
	private void PlayHurt()
	{
		if (m_player != null && m_player.m_hitEffects?.m_effectPrefabs != null && m_player.m_hitEffects.m_effectPrefabs.Length > 0)
		{
			m_audio.PlayEffects(m_player.m_hitEffects, 1f);
		}
		else
		{
			m_audio.Play(Sfx.HitMonster, 1f, 0.9f);
		}
	}

	// ---------------------------------------------------------------- flashes

	private float m_flash2;
	private Color m_flashColor = Color.red;
	private static Texture2D? s_vignette;

	private void Flash(Color color, float strength)
	{
		m_flashColor = color;
		m_flash2 = Mathf.Max(m_flash2, strength);
	}

	// Red at the edges fading to clear in the middle, like the game's damage flash.
	private static Texture2D Vignette()
	{
		if (s_vignette != null)
		{
			return s_vignette;
		}
		const int n = 128;
		Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
		Color[] px = new Color[n * n];
		for (int y = 0; y < n; y++)
		{
			for (int x = 0; x < n; x++)
			{
				float dx = (x + 0.5f) / n * 2f - 1f;
				float dy = (y + 0.5f) / n * 2f - 1f;
				float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
				float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, r));
				px[y * n + x] = new Color(1f, 1f, 1f, a);
			}
		}
		tex.SetPixels(px);
		tex.Apply();
		s_vignette = tex;
		return tex;
	}

	// ---------------------------------------------------------------- HUD

	private float m_iconPulse;
	private readonly System.Collections.Generic.Dictionary<string, Sprite?> m_icons = new();

	private Sprite? ItemIcon(string names)
	{
		if (m_icons.TryGetValue(names, out Sprite? icon))
		{
			return icon;
		}
		icon = null;
		foreach (string name in names.Split('|'))
		{
			GameObject? prefab = Pictures.Prefab(name.Trim());
			ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null!;
			if (drop != null && drop.m_itemData?.m_shared?.m_icons != null && drop.m_itemData.m_shared.m_icons.Length > 0)
			{
				icon = drop.m_itemData.m_shared.m_icons[0];
				break;
			}
		}
		m_icons[names] = icon;
		return icon;
	}

	private static void DrawSprite(Rect r, Sprite sprite, Color tint)
	{
		Texture2D tex = sprite.texture;
		Rect tr = sprite.textureRect;
		Rect uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
		Color old = GUI.color;
		GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * s_guiAlpha);
		GUI.DrawTextureWithTexCoords(r, tex, uv, true);
		GUI.color = old;
	}

	private static void Bar(Rect r, float fill, Color color)
	{
		Color old = GUI.color;
		GUI.color = new Color(0f, 0f, 0f, 0.55f * s_guiAlpha);
		GUI.DrawTexture(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Texture2D.whiteTexture);
		GUI.color = new Color(color.r, color.g, color.b, color.a * s_guiAlpha);
		GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill), r.height), Texture2D.whiteTexture);
		GUI.color = old;
	}

	private static string KeyName(KeyCode key) => key switch
	{
		KeyCode.Mouse0 => "Left mouse",
		KeyCode.Mouse1 => "Right mouse",
		KeyCode.Mouse2 => "Middle mouse",
		_ => key.ToString(),
	};

	private void HudGui(float k, float w, float h)
	{
		// The hit (or block) flash over the edges of the screen.
		if (m_flash2 > 0.001f)
		{
			Color old = GUI.color;
			GUI.color = new Color(m_flashColor.r, m_flashColor.g, m_flashColor.b, Mathf.Clamp01(m_flash2) * s_guiAlpha);
			GUI.DrawTexture(new Rect(0, 0, w, h), Vignette(), ScaleMode.StretchToFill, true);
			GUI.color = old;
		}

		if (m_biomes.Count == 0)
		{
			return;
		}

		// The serpent of the deep's health, with the ballista bolt that hurts it.
		if (m_seaBoss != null && !m_seaBoss.Dead)
		{
			float bw = 520f * k;
			Rect bar = new Rect((w - bw) * 0.5f, 62f * k, bw, 14f * k);
			Shadowed(new Rect(0, 22f * k, w, 40f * k), m_seaBoss.Label, m_mid!);
			Bar(bar, (float)m_seaBoss.Hp / Mathf.Max(1, m_seaBoss.MaxHp), Color.Lerp(new Color(0.75f, 0.08f, 0.05f), Color.white, m_seaBossFlash * 0.6f));
			if (ItemIcon("TurretBoltWood|TurretBolt") is Sprite bolt)
			{
				float s = 44f * k;
				DrawSprite(new Rect(bar.xMax + 12f * k, bar.center.y - s * 0.5f, s, s), bolt, Color.white);
			}
		}
		// The boss's health, as in the game: its name over a red bar at the top.
		else if (m_boss != null && m_bossSink < 0f)
		{
			float bw = 520f * k;
			Rect bar = new Rect((w - bw) * 0.5f, 62f * k, bw, 14f * k);
			Shadowed(new Rect(0, 22f * k, w, 40f * k), m_boss.Name, m_mid!);
			float health = 1f - (float)m_bossHits / Mathf.Max(1, BossHitsNeeded);
			Bar(bar, health, Color.Lerp(new Color(0.75f, 0.08f, 0.05f), Color.white, m_iconPulse * 0.6f));
			Sprite? hit = ItemIcon(m_boss.HitItem);
			if (hit != null)
			{
				// What hurts it, beside the bar.
				float s = 44f * k;
				DrawSprite(new Rect(bar.xMax + 12f * k, bar.center.y - s * 0.5f, s, s), hit, Color.white);
			}
		}
		else
		{
			// The summoning item: its icon with how many you have.
			Segment here = SegAt(m_z);
			BossDef? boss = BossFor(here);
			if (boss != null && here.End == float.MaxValue)
			{
				Sprite? icon = ItemIcon(boss.Item);
				int have = Mathf.Min(m_items, boss.Amount);
				float s = 64f * k * (1f + 0.35f * m_iconPulse);
				float cx = w * 0.5f;
				float cy = 58f * k;
				Color tint = have >= boss.Amount ? new Color(1f, 0.9f, 0.5f) : Color.white;
				if (icon != null)
				{
					DrawSprite(new Rect(cx - 70f * k - s * 0.5f, cy - s * 0.5f, s, s), icon, tint);
				}
				Shadowed(new Rect(cx - 30f * k, cy - 25f * k, 160f * k, 50f * k), $"{have}/{boss.Amount}", m_mid!);
			}
		}

		// A spear in hand: its icon by the stamina bar, with the key to throw it.
		if (m_spears > 0 && m_boss != null && ItemIcon(m_boss.HitItem) is Sprite spear)
		{
			float s = 56f * k * (1f + 0.3f * m_iconPulse);
			for (int i = 0; i < m_spears; i++)
			{
				DrawSprite(new Rect(w * 0.5f + 200f * k + i * 50f * k, h - 100f * k, s, s), spear, Color.white);
			}
			Shadowed(new Rect(w * 0.5f + 200f * k, h - 44f * k, 300f * k, 40f * k), KeyName(Plugin.ThrowKey.Value), m_small!);
		}

		// Armed for a duel: the weapon, with a pip for each swing left to parry.
		if (Dueling && m_boss != null && ItemIcon(m_wieldName.Length > 0 ? m_wieldName : m_boss.HitItem) is Sprite weapon)
		{
			float s = 56f * k * (1f + 0.3f * m_iconPulse);
			DrawSprite(new Rect(w * 0.5f + 200f * k, h - 100f * k, s, s), weapon, Color.white);
			for (int i = 0; i < m_duelTries; i++)
			{
				Star(new Vector2(w * 0.5f + 270f * k + i * 22f * k, h - 72f * k), 16f * k, new Color(1f, 0.82f, 0.2f));
			}
		}

		// At sea: the wind, the ship and its sail (SurfSea.cs).
		if (m_atSea && m_seaPhase == 2)
		{
			SeaHud(k, w, h);
		}

		// Stamina, as in the game: a yellow bar, shown while it isn't full.
		bool shortNow = Time.time - m_staminaShort < 0.6f;
		if (m_stamina < MaxStamina - 0.01f || m_sprinting || m_blocking || shortNow)
		{
			float sw = 360f * k;
			bool low = m_stamina < Plugin.BlockStamina.Value || shortNow;
			Color c = low && Mathf.FloorToInt(Time.time * 6f) % 2 == 0 ? new Color(1f, 0.35f, 0.2f) : new Color(1f, 0.78f, 0.15f);
			Bar(new Rect((w - sw) * 0.5f, h - 70f * k, sw, 12f * k), m_stamina / MaxStamina, c);
		}
	}
}
