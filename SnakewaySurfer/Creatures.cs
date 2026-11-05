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
	// Visual effects the attack spawns (a blob's gas cloud, a slam's dust), pictured when it fires.
	public readonly List<GameObject> Effects = new();
	// Where it lands: the attack's hit effects, what it spawns (a stomp's lightning, the
	// Elder's roots, meteors) and its projectile's impact, pictured when a strike hits.
	public readonly List<GameObject> Impact = new();
	// Sounds as it starts (wind-up) and as it lands.
	public readonly List<string> StartSounds = new();
	public readonly List<string> ImpactSounds = new();
	public bool Area => Type == Attack.AttackType.Area;

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
	// The sound it makes when it notices you (BaseAI.m_alertedEffects), else its idle sound.
	public string AlertSound = "";
	// First footstep sound of its FootStep component.
	public string StepSound = "";
	// The sound it makes when it wakes (MonsterAI.m_wakeupEffects), e.g. an abomination
	// bursting out of the ground.
	public string WakeSound = "";
	// Real standing height from its collider (render bounds of an unanimated picture lie).
	public float Height;

	public bool Fast => RunSpeed >= 7f;

	// Star looks (LevelEffects, read before the copy loses its scripts): index 0 is 1 star.
	public string? LevelPath;
	public string? MainRenderPath;
	public string? BaseEnablePath;
	public readonly List<(float scale, float hue, float saturation, float value, bool emissive, Color emission, string? enablePath)> Levels = new();
	// What it says (NpcTalk greetings and chatter), as localization keys.
	public readonly List<string> Talk = new();

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
		BaseAI ai = prefab.GetComponent<BaseAI>();
		if (ai != null)
		{
			info.AlertSound = FirstSound(ai.m_alertedEffects) ?? FirstSound(ai.m_idleSound) ?? "";
		}
		if (ai is MonsterAI monster)
		{
			info.WakeSound = FirstSound(monster.m_wakeupEffects) ?? "";
		}
		CapsuleCollider capsule = prefab.GetComponent<CapsuleCollider>();
		if (capsule != null)
		{
			float axis = capsule.direction == 1 ? prefab.transform.lossyScale.y : capsule.direction == 0 ? prefab.transform.lossyScale.x : prefab.transform.lossyScale.z;
			info.Height = capsule.direction == 1 ? capsule.height * axis : capsule.radius * 2f * prefab.transform.lossyScale.y;
		}
		FootStep steps = prefab.GetComponentInChildren<FootStep>(true);
		if (steps != null && steps.m_effects != null)
		{
			foreach (FootStep.StepEffect step in steps.m_effects)
			{
				if (info.StepSound.Length > 0)
				{
					break;
				}
				foreach (GameObject fx in step?.m_effectPrefabs ?? new GameObject[0])
				{
					if (fx != null && fx.GetComponentInChildren<ZSFX>(true) != null)
					{
						info.StepSound = fx.name;
						break;
					}
				}
			}
		}
		LevelEffects levels = prefab.GetComponentInChildren<LevelEffects>(true);
		if (levels != null)
		{
			info.LevelPath = levels.transform == prefab.transform ? "" : PathOf(prefab.transform, levels.transform);
			info.MainRenderPath = levels.m_mainRender != null ? (levels.m_mainRender.transform == prefab.transform ? "" : PathOf(prefab.transform, levels.m_mainRender.transform)) : null;
			info.BaseEnablePath = levels.m_baseEnableObject != null ? PathOf(prefab.transform, levels.m_baseEnableObject.transform) : null;
			foreach (LevelEffects.LevelSetup l in levels.m_levelSetups ?? new List<LevelEffects.LevelSetup>())
			{
				info.Levels.Add((l.m_scale, l.m_hue, l.m_saturation, l.m_value, l.m_setEmissiveColor, l.m_emissiveColor, l.m_enableObject != null ? PathOf(prefab.transform, l.m_enableObject.transform) : null));
			}
		}
		NpcTalk talk = prefab.GetComponentInChildren<NpcTalk>(true);
		if (talk != null)
		{
			info.Talk.AddRange(talk.m_randomGreets ?? new List<string>());
			info.Talk.AddRange(talk.m_randomTalk ?? new List<string>());
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
				AddVisuals(attack.m_triggerEffect, a.Effects);
				AddVisuals(attack.m_startEffect, a.Effects);
				AddVisuals(shared.m_triggerEffect, a.Effects);
				AddVisuals(shared.m_startEffect, a.Effects);
				AddSounds(attack.m_startEffect, a.StartSounds);
				AddSounds(shared.m_startEffect, a.StartSounds);
				AddSounds(attack.m_triggerEffect, a.ImpactSounds);
				AddSounds(shared.m_triggerEffect, a.ImpactSounds);
				foreach (EffectList? fx in new[] { attack.m_hitEffect, attack.m_hitTerrainEffect, attack.m_burstEffect, shared.m_hitEffect, shared.m_hitTerrainEffect })
				{
					AddVisuals(fx, a.Impact);
					AddSounds(fx, a.ImpactSounds);
				}
				AddSpawned(attack.m_spawnOnTrigger, a, 0);
				AddSpawned(attack.m_attackProjectile, a, 0);
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
			Plugin.Log.LogInfo($"{prefab.name}: walk {info.WalkSpeed:0.0}, run {info.RunSpeed:0.0}, height {info.Height:0.0}, alert '{info.AlertSound}', step '{info.StepSound}', wake '{info.WakeSound}', melee [{string.Join(", ", info.Melee.ConvertAll(a => $"{a.Item}:{a.Animation}{(a.Attach != null ? "+attach" : "")}"))}], ranged [{string.Join(", ", info.Ranged.ConvertAll(a => $"{a.Item}:{a.Animation}->{a.Projectile!.name}"))}]");
		}
		s_cache[prefab] = info;
		return info;
	}

	private static void AddVisuals(EffectList? effects, List<GameObject> into)
	{
		if (effects?.m_effectPrefabs == null)
		{
			return;
		}
		foreach (EffectList.EffectData e in effects.m_effectPrefabs)
		{
			// Visual effects (a sound prefab with particles too keeps its look; the copy is silent,
			// its sound is played separately).
			if (e != null && e.m_enabled && e.m_prefab != null && e.m_prefab.GetComponentInChildren<ParticleSystem>(true) != null
				&& !into.Contains(e.m_prefab))
			{
				into.Add(e.m_prefab);
			}
		}
	}

	private static void AddSounds(EffectList? effects, List<string> into)
	{
		if (effects?.m_effectPrefabs == null)
		{
			return;
		}
		foreach (EffectList.EffectData e in effects.m_effectPrefabs)
		{
			if (e != null && e.m_enabled && e.m_prefab != null && e.m_prefab.GetComponentInChildren<ZSFX>(true) != null && !into.Contains(e.m_prefab.name))
			{
				into.Add(e.m_prefab.name);
			}
		}
	}

	// Something an attack spawns (an area, roots, a meteor shower) or fires: its own look if it
	// has one, and the effects and sounds of its hits and of what it spawns in turn.
	private static void AddSpawned(GameObject? go, CreatureAttack a, int depth)
	{
		if (go == null || depth > 2)
		{
			return;
		}
		bool projectile = go.GetComponent<Projectile>() != null;
		if (!projectile && !a.Impact.Contains(go) && (go.GetComponentInChildren<ParticleSystem>(true) != null || go.GetComponentInChildren<Renderer>(true) != null))
		{
			a.Impact.Add(go);
		}
		foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
		{
			if (mb == null)
			{
				continue;
			}
			foreach (System.Reflection.FieldInfo f in mb.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
			{
				object? v = f.GetValue(mb);
				string n = f.Name.ToLowerInvariant();
				if (v is EffectList list && (n.Contains("hit") || n.Contains("spawn") || n.Contains("start") || n.Contains("explo")))
				{
					AddVisuals(list, a.Impact);
					AddSounds(list, a.ImpactSounds);
				}
				else if (v is GameObject child && child != go && (n.Contains("spawn") || n.Contains("onhit")) && child.GetComponent<Character>() == null)
				{
					AddSpawned(child, a, depth + 1);
				}
				else if (v is GameObject[] children && (n.Contains("spawn") || n.Contains("onhit")))
				{
					foreach (GameObject c in children)
					{
						if (c != null && c != go && c.GetComponent<Character>() == null)
						{
							AddSpawned(c, a, depth + 1);
						}
					}
				}
			}
		}
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
