using HarmonyLib;
using UnityEngine;

namespace SnakewaySurfer;

// What happens to the real character while its player is surfing. All of it goes through vanilla
// synced data (the animator floats in ZSyncAnimation, the helmet hash in the player's ZDO), so
// other players see it with or without the mod.
internal static class PlayerHooks
{
	private static bool IsSurfer(Character c) => SurfGame.Active != null && c != null && c == Player.m_localPlayer;

	// Re-applies equipment visuals, which puts the circlet on (or takes it off once the game is gone).
	public static void RefreshHelmet(Player player)
	{
		if (player != null && player.m_visEquipment != null)
		{
			player.SetupVisEquipment(player.m_visEquipment, false);
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.SetupVisEquipment))]
	private static class CircletPatch
	{
		private static void Postfix(Player __instance, VisEquipment visEq, bool isRagdoll)
		{
			if (isRagdoll || !IsSurfer(__instance) || visEq == null)
			{
				return;
			}
			string item = Plugin.CircletItem.Value.Trim();
			if (item.Length > 0)
			{
				visEq.SetHelmetItem(item.GetStableHashCode());
			}
		}
	}

	// Running in place: vanilla sets the walk/run animation floats from the body's real speed
	// at the end of UpdateWalking; overwrite them after it so the legs keep running while the
	// body stands still. Synced to everyone by ZSyncAnimation.
	[HarmonyPatch(typeof(Character), nameof(Character.UpdateWalking))]
	private static class RunInPlacePatch
	{
		private static void Postfix(Character __instance)
		{
			if (!SurfGame.RunningInPlace || !IsSurfer(__instance) || __instance.m_zanim == null)
			{
				return;
			}
			__instance.m_zanim.SetFloat(Character.s_forwardSpeed, __instance.m_runSpeed);
			__instance.m_zanim.SetFloat(Character.s_sidewaySpeed, 0f);
			__instance.m_zanim.SetBool(Character.s_onGround, true);
		}
	}

	// The keys belong to the minigame: the character doesn't move, attack, jump or use hotkeys.
	[HarmonyPatch(typeof(Player), nameof(Player.TakeInput))]
	private static class TakeInputPatch
	{
		private static void Postfix(Player __instance, ref bool __result)
		{
			if (IsSurfer(__instance))
			{
				__result = false;
			}
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
	private static class SetControlsPatch
	{
		private static void Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold,
			ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold, ref bool jump,
			ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
		{
			if (!IsSurfer(__instance))
			{
				return;
			}
			movedir = Vector3.zero;
			attack = attackHold = secondaryAttack = secondaryAttackHold = false;
			block = blockHold = jump = crouch = run = autoRun = dodge = false;
		}
	}

	// Escape stops the minigame; don't let it open the pause menu in the same press.
	[HarmonyPatch(typeof(Menu), "Update")]
	private static class MenuPatch
	{
		private static bool Prefix() => SurfGame.Active == null;
	}

	// Enter that runs "surfsup" in the console could also open the chat in the same frame, which
	// then swallowed every key. The chat is paused while surfing.
	[HarmonyPatch(typeof(Chat), "Update")]
	private static class ChatPatch
	{
		private static bool Prefix(object __instance) => !(SurfGame.Active != null && __instance is Chat);
	}

	[HarmonyPatch(typeof(GameCamera), "LateUpdate")]
	private static class GameCameraPatch
	{
		private static void Postfix() => SurfGame.Active?.ApplyGameCamera();
	}
}
