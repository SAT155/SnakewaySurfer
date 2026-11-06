using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// 1.16.2: what the surfer holds for a fight. Moder's Draugr Fang bow, the silver atgeir when
// she's down, or a boss's own weapon for a duel shows in the hand the game uses for it
// (VisEquipment's hand joints, as VisEquipment.AttachItem places it), and whatever you wore in
// your hands is put away meanwhile. The avatar takes the weapon's stance and plays its own
// clips: the bow's draw and release, a weapon's chain of blows, and the dodge roll.
internal sealed partial class SurfGame
{
	private GameObject? m_wielded;
	private Renderer[] m_wieldedRenderers = new Renderer[0];
	private string m_wieldName = "";
	private ItemDrop.ItemData.SharedData? m_wieldShared;
	private readonly List<GameObject> m_stowed = new();
	private float m_stanceF;
	private int m_stanceI;
	private bool m_stanceSaved;
	// Turned round for a roll away from a boss, and the roll's way back towards the camera.
	private float m_avatarYaw;
	private float m_rollAway = -1f;
	private const float RollAwayTime = 1.5f;

	private Transform? AvatarHand(bool left)
	{
		if (m_avatar == null || m_player == null || m_player.m_visEquipment == null || m_player.m_animator == null)
		{
			return null;
		}
		Transform root = m_player.m_animator.transform;
		Transform? joint = left ? m_player.m_visEquipment.m_leftHand : m_player.m_visEquipment.m_rightHand;
		List<string> parts = new();
		while (joint != null && joint != root)
		{
			parts.Insert(0, joint.name);
			joint = joint.parent;
		}
		return joint == root && parts.Count > 0 ? m_avatar.transform.Find(string.Join("/", parts)) : null;
	}

	private static ItemDrop.ItemData.SharedData? SharedOf(GameObject? prefab) =>
		prefab != null && prefab.GetComponent<ItemDrop>() is ItemDrop drop ? drop.m_itemData?.m_shared : null;

	// A weapon you swing (not a bow, staff or bomb).
	private static bool IsMelee(ItemDrop.ItemData.SharedData? shared) =>
		shared?.m_attack != null && !string.IsNullOrEmpty(shared.m_attack.m_attackAnimation)
		&& shared.m_itemType != ItemDrop.ItemData.ItemType.Bow && shared.m_attack.m_attackProjectile == null
		&& shared.m_attack.m_attackType != Attack.AttackType.Projectile && shared.m_attack.m_attackType != Attack.AttackType.None;

	// Puts the first of these items that exists in the avatar's hand. `meleeOnly`: only a weapon
	// you swing. False when there's none (the avatar keeps what it has).
	private bool Wield(string names, bool meleeOnly = false)
	{
		if (m_avatar == null)
		{
			return false;
		}
		GameObject? prefab = null;
		foreach (string name in names.Split('|'))
		{
			GameObject? p = name.Trim().Length > 0 ? Pictures.Item(name.Trim()) : null;
			if (p != null && SharedOf(p) is ItemDrop.ItemData.SharedData sh && (!meleeOnly || IsMelee(sh)))
			{
				prefab = p;
				break;
			}
		}
		ItemDrop.ItemData.SharedData? shared = SharedOf(prefab);
		if (prefab == null || shared == null)
		{
			return false;
		}
		if (m_wielded != null && m_wieldName == prefab.name)
		{
			return true;
		}
		Unwield();
		bool left = shared.m_itemType == ItemDrop.ItemData.ItemType.Bow || shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
		Transform? hand = AvatarHand(left);
		Transform? attach = prefab.transform.Find("attach") ?? prefab.transform.Find("attach_skin");
		if (hand == null || attach == null)
		{
			return false;
		}
		StowHands();
		GameObject weapon = Instantiate(Picture(attach.gameObject), hand, false);
		weapon.SetActive(true);
		weapon.transform.localPosition = Vector3.zero;
		weapon.transform.localRotation = Quaternion.identity;
		// VisEquipment keeps the item's own size when parenting to the joint (rigs are often
		// scaled), so undo the joint's scale.
		Vector3 want = attach.lossyScale;
		Vector3 bone = hand.lossyScale;
		weapon.transform.localScale = new Vector3(
			want.x / Mathf.Max(1e-4f, Mathf.Abs(bone.x)),
			want.y / Mathf.Max(1e-4f, Mathf.Abs(bone.y)),
			want.z / Mathf.Max(1e-4f, Mathf.Abs(bone.z)));
		Pictures.SetLayer(weapon, m_layer);
		m_wielded = weapon;
		m_wieldedRenderers = weapon.GetComponentsInChildren<Renderer>(true);
		m_wieldName = prefab.name;
		m_wieldShared = shared;
		SetStance((int)shared.m_animationState);
		return true;
	}

	// Puts away what's in both hands (the bow and the atgeir both want two).
	private void StowHands()
	{
		foreach (bool side in new[] { true, false })
		{
			Transform? hand = AvatarHand(side);
			if (hand == null)
			{
				continue;
			}
			foreach (Transform child in hand)
			{
				if (child.gameObject.activeSelf && child.gameObject != m_wielded)
				{
					child.gameObject.SetActive(false);
					m_stowed.Add(child.gameObject);
				}
			}
		}
	}

	private void Unwield()
	{
		if (m_wielded != null)
		{
			Destroy(m_wielded);
		}
		m_wielded = null;
		m_wieldedRenderers = new Renderer[0];
		m_wieldName = "";
		m_wieldShared = null;
		foreach (GameObject go in m_stowed)
		{
			if (go != null)
			{
				go.SetActive(true);
			}
		}
		m_stowed.Clear();
		RestoreStance();
		AvatarBool("bow_aim", false);
	}

	// The animator's weapon stance (Humanoid sets "statef" and "statei" from the item).
	private void SetStance(int state)
	{
		if (m_avatarAnim == null)
		{
			return;
		}
		if (!m_stanceSaved)
		{
			m_stanceSaved = true;
			m_stanceF = m_avatarParams.Contains(Animator.StringToHash("statef")) ? m_avatarAnim.GetFloat("statef") : 0f;
			m_stanceI = m_avatarParams.Contains(Animator.StringToHash("statei")) ? m_avatarAnim.GetInteger("statei") : 0;
		}
		AvatarFloat("statef", state);
		if (m_avatarParams.Contains(Animator.StringToHash("statei")))
		{
			m_avatarAnim.SetInteger("statei", state);
		}
	}

	private void RestoreStance()
	{
		if (!m_stanceSaved || m_avatarAnim == null)
		{
			return;
		}
		m_stanceSaved = false;
		AvatarFloat("statef", m_stanceF);
		if (m_avatarParams.Contains(Animator.StringToHash("statei")))
		{
			m_avatarAnim.SetInteger("statei", m_stanceI);
		}
	}

	// The trigger for a blow of a weapon's chain (Attack.Start triggers its animation plus the
	// chain level when it has several), or null when the avatar has none.
	private string? SwingTrigger(ItemDrop.ItemData.SharedData? shared, int level)
	{
		if (m_avatarAnim == null)
		{
			return null;
		}
		string anim = shared?.m_attack != null && !string.IsNullOrEmpty(shared.m_attack.m_attackAnimation) ? shared.m_attack.m_attackAnimation : "unarmed_attack";
		int chain = shared?.m_attack != null ? Mathf.Max(1, shared.m_attack.m_attackChainLevels) : 2;
		if (chain > 1 && m_avatarParams.Contains(Animator.StringToHash(anim + (level % chain))))
		{
			return anim + (level % chain);
		}
		if (m_avatarParams.Contains(Animator.StringToHash(anim)))
		{
			return anim;
		}
		return chain > 1 && m_avatarParams.Contains(Animator.StringToHash(anim + "0")) ? anim + "0" : null;
	}

	// Plays that blow now.
	private void AvatarSwing(ItemDrop.ItemData.SharedData? shared, int level)
	{
		if (SwingTrigger(shared, level) is string trigger)
		{
			m_avatarAnim!.SetTrigger(trigger);
		}
	}

	// The weapon you hold for a melee blow: the one put in your hand, else your own if it's
	// one you swing.
	private ItemDrop.ItemData.SharedData? SwingWeapon()
	{
		if (IsMelee(m_wieldShared))
		{
			return m_wieldShared;
		}
		ItemDrop.ItemData? own = m_player != null ? m_player.GetCurrentWeapon() : null;
		return IsMelee(own?.m_shared) ? own!.m_shared : null;
	}

	// A dodge roll back away from a boss: you turn and roll towards the camera, then run on.
	private void RollAway()
	{
		m_rollAway = 0f;
		SetTrigger("dodge");
		m_audio.Play(Sfx.Slide, 1f, 0.95f);
	}

	// Called from PlaceAvatar: the turn and the drift back for the roll.
	private Vector3 RollAwayOffset(float dt)
	{
		float back = 0f;
		float yaw = AimYaw();
		if (m_rollAway >= 0f)
		{
			m_rollAway += dt;
			float t = m_rollAway;
			// Turned round for the roll, then back to the run.
			yaw = t < 0.85f ? 180f : 0f;
			back = t < 0.6f ? Mathf.SmoothStep(0f, 2.4f, t / 0.6f) : Mathf.SmoothStep(2.4f, 0f, (t - 0.6f) / (RollAwayTime - 0.6f));
			if (t >= RollAwayTime)
			{
				m_rollAway = -1f;
			}
		}
		m_avatarYaw = Mathf.MoveTowardsAngle(m_avatarYaw, yaw, 900f * dt);
		return new Vector3(0f, 0f, -back);
	}
}
