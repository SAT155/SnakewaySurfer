using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// One of a creature's own attacks, read from the weapon items it spawns with (Humanoid default and
// random items). The animation name is what Attack.Start triggers on the creature's animator.
internal sealed class CreatureAttack
{
	public string Item = "";
	public string Animation = "";
	public Attack.AttackType Type;
	public GameObject? Projectile;
	public GameObject? Attach;
	// The weapon's size in the item prefab; VisEquipment keeps it when parenting to the hand.
	public Vector3 AttachScale = Vector3.one;
	public bool LeftHand;
	public int AnimationState;
	public string Sound = "";

	public bool Ranged => Projectile != null;
}

// What the minigame needs to know about a creature prefab, read once before it's turned into a
// lifeless picture (which loses all its scripts).
internal sealed class CreatureInfo
{
	public readonly List<CreatureAttack> Melee = new();
	public readonly List<CreatureAttack> Ranged = new();
	public float WalkSpeed = 2f;
	public float RunSpeed = 5f;
	public bool Flying;
	public string? RightHand;
	public string? LeftHand;

	public bool Fast => RunSpeed >= 7f;

	private static readonly Dictionary<GameObject, CreatureInfo> s_cache = new();

	public static CreatureInfo Of(GameObject prefab)
	{
		if (s_cache.TryGetValue(prefab, out CreatureInfo info))
		{
			return info;
		}
		info = new CreatureInfo();
		Character character = prefab.GetComponent<Character>();
		if (character != null)
		{
			info.WalkSpeed = character.m_walkSpeed;
			info.RunSpeed = character.m_runSpeed;
			info.Flying = character.m_flying;
		}
		VisEquipment vis = prefab.GetComponent<VisEquipment>();
		if (vis != null)
		{
			info.RightHand = PathOf(prefab.transform, vis.m_rightHand);
			info.LeftHand = PathOf(prefab.transform, vis.m_leftHand);
		}
		Humanoid humanoid = character as Humanoid;
		if (humanoid != null)
		{
			HashSet<GameObject> seen = new();
			void Add(GameObject? item)
			{
				if (item == null || !seen.Add(item))
				{
					return;
				}
				ItemDrop drop = item.GetComponent<ItemDrop>();
				ItemDrop.ItemData.SharedData? shared = drop != null ? drop.m_itemData?.m_shared : null;
				if (shared == null || shared.m_attack == null || string.IsNullOrEmpty(shared.m_attack.m_attackAnimation))
				{
					return;
				}
				switch (shared.m_itemType)
				{
					case ItemDrop.ItemData.ItemType.Shield:
					case ItemDrop.ItemData.ItemType.Helmet:
					case ItemDrop.ItemData.ItemType.Chest:
					case ItemDrop.ItemData.ItemType.Legs:
					case ItemDrop.ItemData.ItemType.Shoulder:
					case ItemDrop.ItemData.ItemType.Utility:
					case ItemDrop.ItemData.ItemType.Trophy:
						return;
				}
				Attack attack = shared.m_attack;
				Transform? attach = item.transform.Find("attach") ?? item.transform.Find("attach_skin");
				CreatureAttack a = new CreatureAttack
				{
					Item = item.name,
					Animation = attack.m_attackAnimation,
					Type = attack.m_attackType,
					Projectile = attack.m_attackType == Attack.AttackType.Projectile ? attack.m_attackProjectile : null,
					Attach = attach != null ? attach.gameObject : null,
					AttachScale = attach != null ? attach.lossyScale : Vector3.one,
					LeftHand = shared.m_itemType == ItemDrop.ItemData.ItemType.Bow,
					AnimationState = (int)shared.m_animationState,
					Sound = FirstSound(attack.m_startEffect) ?? FirstSound(attack.m_triggerEffect) ?? "",
				};
				(a.Ranged ? info.Ranged : info.Melee).Add(a);
			}
			foreach (GameObject item in humanoid.m_defaultItems ?? new GameObject[0])
			{
				Add(item);
			}
			foreach (GameObject item in humanoid.m_randomWeapon ?? new GameObject[0])
			{
				Add(item);
			}
			foreach (Humanoid.ItemSet set in humanoid.m_randomSets ?? new Humanoid.ItemSet[0])
			{
				foreach (GameObject item in set.m_items ?? new GameObject[0])
				{
					Add(item);
				}
			}
		}
		if (Plugin.DebugLog.Value)
		{
			Plugin.Log.LogInfo($"{prefab.name}: walk {info.WalkSpeed:0.0}, run {info.RunSpeed:0.0}, melee [{string.Join(", ", info.Melee.ConvertAll(a => $"{a.Item}:{a.Animation}{(a.Attach != null ? "+attach" : "")}"))}], ranged [{string.Join(", ", info.Ranged.ConvertAll(a => $"{a.Item}:{a.Animation}->{a.Projectile!.name}"))}]");
		}
		s_cache[prefab] = info;
		return info;
	}

	private static string? FirstSound(EffectList? effects)
	{
		if (effects?.m_effectPrefabs == null)
		{
			return null;
		}
		foreach (EffectList.EffectData e in effects.m_effectPrefabs)
		{
			if (e != null && e.m_enabled && e.m_prefab != null && e.m_prefab.GetComponentInChildren<ZSFX>(true) != null)
			{
				return e.m_prefab.name;
			}
		}
		return null;
	}

	private static string? PathOf(Transform root, Transform? t)
	{
		if (t == null)
		{
			return null;
		}
		List<string> parts = new();
		while (t != null && t != root)
		{
			parts.Insert(0, t.name);
			t = t.parent;
		}
		return t == root ? string.Join("/", parts) : null;
	}

	public CreatureAttack? PickMelee(string prefer = "")
	{
		if (Melee.Count == 0)
		{
			return null;
		}
		if (prefer.Length > 0)
		{
			foreach (CreatureAttack a in Melee)
			{
				if (a.Animation.ToLowerInvariant().Contains(prefer) || a.Item.ToLowerInvariant().Contains(prefer))
				{
					return a;
				}
			}
		}
		return Melee[Random.Range(0, Melee.Count)];
	}

	public CreatureAttack? PickRanged() => Ranged.Count == 0 ? null : Ranged[Random.Range(0, Ranged.Count)];
}
