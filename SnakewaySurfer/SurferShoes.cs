using System;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SnakewaySurfer;

// The boss rush reward: Golden Surfer Shoes, a trinket made from a vanilla stamina trinket
// (its model and adrenaline bar) gilded, with a winged emblem on its icon. +15% movement speed;
// full adrenaline adds 5% more speed and 2.5x stamina regen for the vanilla trinket's duration.
// Only players with the mod have the item (others don't see it on you; it's never dropped in
// the world by the mod, and it disappears from the bag if the mod is removed).
internal static class SurferShoes
{
	public const string PrefabName = "SnakewaySurfer_GoldenShoes";
	private const string BaseName = "TrinketBronzeStamina";
	private const string EarnedKey = "SnakewaySurfer.GoldenShoes";
	private const string OwedKey = "SnakewaySurfer.GoldenShoesOwed";

	private static GameObject? s_prefab;
	private static GameObject? s_holder;

	public static GameObject? Prefab => s_prefab;

	private static void Build(ObjectDB db)
	{
		if (s_prefab != null)
		{
			return;
		}
		GameObject source = db.GetItemPrefab(BaseName);
		if (source == null || source.GetComponent<ItemDrop>() == null)
		{
			Plugin.Log.LogWarning($"No {BaseName} in this game version: no golden shoes.");
			return;
		}
		if (s_holder == null)
		{
			// Inactive, so the copy never wakes up (no ZNetView registers).
			s_holder = new GameObject("SnakewaySurfer_Items");
			s_holder.SetActive(false);
			Object.DontDestroyOnLoad(s_holder);
		}
		GameObject clone = Object.Instantiate(source, s_holder.transform, false);
		clone.name = PrefabName;
		ItemDrop drop = clone.GetComponent<ItemDrop>();
		ItemDrop.ItemData.SharedData shared = (ItemDrop.ItemData.SharedData)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(drop.m_itemData.m_shared, null);
		drop.m_itemData.m_shared = shared;
		drop.m_itemData.m_dropPrefab = clone;
		shared.m_name = "Golden Surfer Shoes";
		shared.m_description = "Golden viking shoes with a winged emblem, won by beating every boss in a row on the Snakeway. Full adrenaline: even faster, and your stamina comes back much quicker.";
		shared.m_movementModifier = 0.15f;
		shared.m_maxQuality = 1;
		shared.m_value = 0;
		shared.m_teleportable = true;
		shared.m_fullAdrenalineSE = MakeRush(shared.m_fullAdrenalineSE);
		try
		{
			if (shared.m_icons != null && shared.m_icons.Length > 0 && shared.m_icons[0] != null)
			{
				shared.m_icons = new[] { GoldIcon(shared.m_icons[0]) };
			}
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"Golden shoes icon: {e.Message}");
		}
		Gild(clone);
		s_prefab = clone;
	}

	private static StatusEffect MakeRush(StatusEffect? original)
	{
		SE_Stats se;
		if (original is SE_Stats stats)
		{
			se = Object.Instantiate(stats);
		}
		else
		{
			se = ScriptableObject.CreateInstance<SE_Stats>();
			se.m_ttl = 20f;
			if (original != null)
			{
				se.m_icon = original.m_icon;
				se.m_ttl = original.m_ttl > 0f ? original.m_ttl : 20f;
			}
		}
		se.name = "SE_SnakewaySurferRush";
		se.m_name = "Surfer's rush";
		se.m_speedModifier += 0.05f;
		se.m_staminaRegenMultiplier = Mathf.Max(se.m_staminaRegenMultiplier, 1f) * 2.5f;
		return se;
	}

	// Every material of the model gold and shiny.
	private static void Gild(GameObject go)
	{
		Color gold = new Color(1f, 0.76f, 0.22f);
		foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
		{
			Material[] mats = r.sharedMaterials;
			for (int i = 0; i < mats.Length; i++)
			{
				if (mats[i] == null)
				{
					continue;
				}
				Material m = new Material(mats[i]) { name = mats[i].name + "_Gold" };
				if (m.HasProperty("_Color"))
				{
					m.color = gold;
				}
				if (m.HasProperty("_Metallic"))
				{
					m.SetFloat("_Metallic", 0.85f);
				}
				if (m.HasProperty("_Glossiness"))
				{
					m.SetFloat("_Glossiness", 0.75f);
				}
				mats[i] = m;
			}
			r.sharedMaterials = mats;
		}
	}

	// The vanilla icon, gilded, with a small wing (our own emblem) in the lower right.
	private static Sprite GoldIcon(Sprite src)
	{
		Texture2D tex = src.texture;
		Rect rect = src.textureRect;
		int w = Mathf.Max(1, (int)rect.width);
		int h = Mathf.Max(1, (int)rect.height);
		RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
		RenderTexture previous = RenderTexture.active;
		Texture2D icon = new Texture2D(w, h, TextureFormat.RGBA32, false);
		try
		{
			// The icon atlas isn't readable from scripts: copy it through the GPU.
			Graphics.Blit(tex, rt);
			RenderTexture.active = rt;
			icon.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0);
		}
		finally
		{
			RenderTexture.active = previous;
			RenderTexture.ReleaseTemporary(rt);
		}
		Color[] px = icon.GetPixels();
		Color gold = new Color(1f, 0.78f, 0.25f);
		for (int i = 0; i < px.Length; i++)
		{
			Color c = px[i];
			float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
			Color g = gold * Mathf.Lerp(0.45f, 1.35f, lum);
			px[i] = new Color(Mathf.Clamp01(g.r), Mathf.Clamp01(g.g), Mathf.Clamp01(g.b), c.a);
		}
		// Three feathers fanning up and left from a point in the lower-right corner.
		Color wing = new Color(1f, 0.97f, 0.85f, 1f);
		Vector2 root = new Vector2(w * 0.86f, h * 0.14f);
		for (int f = 0; f < 3; f++)
		{
			float angle = (150f - f * 22f) * Mathf.Deg2Rad;
			float length = w * (0.34f - f * 0.06f);
			Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
			for (float t = 0f; t <= 1f; t += 0.5f / length)
			{
				Vector2 p = root + dir * (t * length);
				// Feathers taper toward their tips.
				float radius = Mathf.Lerp(w * 0.05f, w * 0.012f, t);
				for (int dy = -(int)radius - 1; dy <= radius + 1; dy++)
				{
					for (int dx = -(int)radius - 1; dx <= radius + 1; dx++)
					{
						int x = (int)p.x + dx;
						int y = (int)p.y + dy;
						if (x < 0 || y < 0 || x >= w || y >= h || dx * dx + dy * dy > radius * radius)
						{
							continue;
						}
						px[y * w + x] = wing;
					}
				}
			}
		}
		icon.SetPixels(px);
		icon.Apply();
		return Sprite.Create(icon, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), src.pixelsPerUnit);
	}

	private static void Register(ObjectDB db)
	{
		try
		{
			Build(db);
			if (s_prefab == null || db.m_items.Contains(s_prefab))
			{
				return;
			}
			db.m_items.Add(s_prefab);
			AccessTools.Method(typeof(ObjectDB), "UpdateRegisters").Invoke(db, null);
			if (ZNetScene.instance != null)
			{
				AddToScene(ZNetScene.instance);
			}
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"Couldn't add the golden shoes: {e}");
		}
	}

	[HarmonyPatch(typeof(ObjectDB), "Awake")]
	private static class ObjectDBAwake
	{
		private static void Postfix(ObjectDB __instance) => Register(__instance);
	}

	[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
	private static class ObjectDBCopy
	{
		private static void Postfix(ObjectDB __instance) => Register(__instance);
	}

	// So the game can spawn it when you drop it from your bag yourself.
	[HarmonyPatch(typeof(ZNetScene), "Awake")]
	private static class ZNetSceneAwake
	{
		private static void Postfix(ZNetScene __instance)
		{
			if (ObjectDB.instance != null)
			{
				Build(ObjectDB.instance);
			}
			AddToScene(__instance);
		}
	}

	private static void AddToScene(ZNetScene scene)
	{
		try
		{
			if (s_prefab == null || scene.m_prefabs.Contains(s_prefab))
			{
				return;
			}
			scene.m_prefabs.Add(s_prefab);
			var named = (System.Collections.Generic.Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);
			named[PrefabName.GetStableHashCode()] = s_prefab;
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"Couldn't add the golden shoes to the scene: {e.Message}");
		}
	}

	// Called when a full boss rush is beaten. Once per character.
	public static string Give(Player player)
	{
		if (player.m_customData.ContainsKey(EarnedKey))
		{
			return "You already have the Golden Surfer Shoes: a big Run bonus instead.";
		}
		player.m_customData[OwedKey] = "1";
		return TryDeliver(player) ?? "No room in your bag: the Golden Surfer Shoes wait for a free slot (they're given when a run ends).";
	}

	// Gives owed shoes if there's room; null if they're still owed.
	public static string? TryDeliver(Player player)
	{
		if (!player.m_customData.ContainsKey(OwedKey))
		{
			return null;
		}
		if (s_prefab == null)
		{
			return "The Golden Surfer Shoes couldn't be made in this game version.";
		}
		Inventory inventory = player.GetInventory();
		if (!inventory.CanAddItem(s_prefab, 1))
		{
			return null;
		}
		inventory.AddItem(s_prefab, 1);
		player.m_customData.Remove(OwedKey);
		player.m_customData[EarnedKey] = "1";
		return "You earned the Golden Surfer Shoes! (+15% movement speed; equip them as your trinket)";
	}
}
