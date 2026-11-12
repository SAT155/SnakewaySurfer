using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SnakewaySurfer;

// 1.16.84 (SAT): "add a portal building that you can build and place with hammer that only costs
// ectoplasm and feathers. Be sure to set it's category appropriately. ... these can be set for
// the different biomes if a user just wants to go straight to running, reset them to make em
// revert back to sleep required to start dream."
// The Strange Portal: the game's wooden portal, glowing the story portal's sickly green, in the
// hammer's Misc tab beside the real portal, for 10 ectoplasm and 10 feathers, anywhere (no
// workbench). Use it (E) to pick where it leads: one of your checkpoints, or "the dream" (the
// default, and where it goes back to after the last checkpoint). Walk into it: set to a
// checkpoint you run from there; set to the dream it's the ravens' strange portal (before their
// story starts it begins it; after, Hugin reminds you that the dream only comes in your sleep).
internal static class SurfPortalPiece
{
	public const string PrefabName = "piece_surf_strangeportal";
	// 1.16.x (SAT: "It needs to work for some players without any mods"): a built Strange Portal
	// is saved as the vanilla wooden portal plus this ZDO flag. Players and servers without the
	// mod see a plain wooden portal instead of logging "Missing prefab hash" every frame; modded
	// players turn it back into the Strange Portal as it loads (TeleportWorldAwake below).
	public static readonly int PrefabHash = PrefabName.GetStableHashCode();
	public static readonly int VanillaHash = "portal_wood".GetStableHashCode();
	public static readonly int FlagKey = "surf_strangeportal".GetStableHashCode();
	private static GameObject? s_prefab;
	private static GameObject? s_holder;

	private static GameObject? Make(GameObject? portal)
	{
		if (s_prefab != null || portal == null)
		{
			return s_prefab;
		}
		s_holder = new GameObject("SnakewaySurfer_Prefabs");
		s_holder.SetActive(false);
		Object.DontDestroyOnLoad(s_holder);
		GameObject copy = Object.Instantiate(portal, s_holder.transform, false);
		copy.name = PrefabName;
		Convert(copy, true);
		Piece? piece = copy.GetComponent<Piece>();
		if (piece != null)
		{
			piece.m_category = Piece.PieceCategory.Misc;
			piece.m_craftingStation = null;
		}
		s_prefab = copy;
		Plugin.Log.LogInfo("Made the Strange Portal piece.");
		return s_prefab;
	}

	// Turns a wooden portal (the prefab copy, or a loaded vanilla portal flagged as ours) into a
	// Strange Portal: no tag to type and no real teleport, so everything of the portal's own goes.
	private static void Convert(GameObject go, bool immediate)
	{
		TeleportWorld? tw = go.GetComponent<TeleportWorld>();
		StrangePortal marker = go.AddComponent<StrangePortal>();
		marker.Swirl = tw != null && tw.m_target_found != null ? tw.m_target_found.gameObject : null;
		marker.Model = tw != null ? tw.m_model : null;
		marker.Centre = tw != null ? tw.m_proximityRoot : null;
		foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
		{
			if (mb != null && mb.GetType().Name.StartsWith("Teleport"))
			{
				mb.enabled = false;
				if (immediate)
				{
					Object.DestroyImmediate(mb);
				}
				else
				{
					Object.Destroy(mb);
				}
			}
		}
		Piece? piece = go.GetComponent<Piece>();
		if (piece != null)
		{
			piece.m_name = "Strange Portal";
			piece.m_description = "It hums like a half-remembered dream. Use it to choose where it leads: one of your checkpoints, or back to the dream that only comes in your sleep.";
			Piece? ours = s_prefab != null ? s_prefab.GetComponent<Piece>() : null;
			if (ours != null && ours.m_resources != null && ours.m_resources.Length > 0)
			{
				piece.m_resources = ours.m_resources;
			}
		}
	}

	// A wooden portal loading in: if its save says it's a Strange Portal, make it one.
	[HarmonyPatch(typeof(TeleportWorld), "Awake")]
	private static class TeleportWorldAwake
	{
		private static void Postfix(TeleportWorld __instance)
		{
			if (__instance == null || __instance.GetComponent<StrangePortal>() != null)
			{
				return;
			}
			ZNetView? nview = __instance.GetComponent<ZNetView>();
			if (nview != null && nview.IsValid() && nview.GetZDO().GetBool(FlagKey))
			{
				Convert(__instance.gameObject, false);
			}
		}
	}

	// A Strange Portal saved by an old version under its own name, in a game where that name
	// isn't known (the piece switched off, or its model not found): the game logs "Missing prefab
	// hash" for it every time it tries to load it, for everyone. Such portals are rewritten here
	// as the vanilla wooden portal plus our flag, which any game can load. Checked now and then.
	private static float s_sweepAt;
	private static bool s_missingLogged;

	public static void Sweep()
	{
		if (Time.realtimeSinceStartup < s_sweepAt || ZDOMan.instance == null || ZNetScene.instance == null || Player.m_localPlayer == null)
		{
			return;
		}
		s_sweepAt = Time.realtimeSinceStartup + 20f;
		if (ZNetScene.instance.GetPrefab(PrefabHash) != null)
		{
			// Known here: each one converts itself as it loads (StrangePortal.Awake).
			return;
		}
		if (!s_missingLogged)
		{
			s_missingLogged = true;
			Plugin.Log.LogInfo($"The Strange Portal piece isn't registered in this game (setting {(Plugin.PortalPiece.Value ? "on, but the wooden portal wasn't found to copy" : "off")}); old Strange Portals in the world are being turned into wooden portals that every game can load.");
		}
		int fixedCount = 0;
		try
		{
			List<ZDO> found = new();
			foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
			{
				if (zdo != null && zdo.GetPrefab() == PrefabHash)
				{
					found.Add(zdo);
				}
			}
			foreach (ZDO zdo in found)
			{
				zdo.SetOwner(ZDOMan.GetSessionID());
				zdo.SetPrefab(VanillaHash);
				zdo.Set(FlagKey, true);
				fixedCount++;
			}
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Couldn't rewrite old Strange Portals: {e.Message}");
		}
		if (fixedCount > 0)
		{
			Plugin.Log.LogInfo($"Rewrote {fixedCount} old Strange Portal(s) as wooden portals; the \"Missing prefab hash\" messages for them stop now.");
		}
	}

	private static GameObject? PortalFrom(List<GameObject> prefabs) => prefabs.Find(g => g != null && g.name == "portal_wood");

	[HarmonyPatch(typeof(ZNetScene), "Awake")]
	private static class ZNetSceneAwake
	{
		private static void Prefix(ZNetScene __instance)
		{
			if (!Plugin.PortalPiece.Value)
			{
				return;
			}
			GameObject? prefab = Make(PortalFrom(__instance.m_prefabs));
			if (prefab == null)
			{
				Plugin.Log.LogWarning($"Strange Portal: no wooden portal among the game's {__instance.m_prefabs.Count} prefabs to copy; the piece is unavailable this session.");
			}
			if (prefab != null && !__instance.m_prefabs.Contains(prefab))
			{
				__instance.m_prefabs.Add(prefab);
			}
		}
	}

	[HarmonyPatch(typeof(ObjectDB), "Awake")]
	private static class ObjectDBAwake
	{
		private static void Postfix(ObjectDB __instance) => AddToHammer(__instance);
	}

	[HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
	private static class ObjectDBCopy
	{
		private static void Postfix(ObjectDB __instance) => AddToHammer(__instance);
	}

	private static void AddToHammer(ObjectDB db)
	{
		if (!Plugin.PortalPiece.Value || db == null || db.m_items == null || db.m_items.Count == 0)
		{
			return;
		}
		GameObject? hammer = db.GetItemPrefab("Hammer");
		PieceTable? table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces : null;
		if (table == null)
		{
			return;
		}
		GameObject? prefab = Make(PortalFrom(table.m_pieces));
		if (prefab == null)
		{
			return;
		}
		Piece? piece = prefab.GetComponent<Piece>();
		ItemDrop? ecto = db.GetItemPrefab("Ectoplasm")?.GetComponent<ItemDrop>();
		ItemDrop? feathers = db.GetItemPrefab("Feathers")?.GetComponent<ItemDrop>();
		if (piece != null && ecto != null && feathers != null)
		{
			piece.m_resources = new[]
			{
				new Piece.Requirement { m_resItem = ecto, m_amount = 10, m_recover = true },
				new Piece.Requirement { m_resItem = feathers, m_amount = 10, m_recover = true },
			};
		}
		if (!table.m_pieces.Contains(prefab))
		{
			int at = table.m_pieces.FindIndex(g => g != null && g.name == "portal_wood");
			table.m_pieces.Insert(at >= 0 ? at + 1 : table.m_pieces.Count, prefab);
		}
	}
}

// On each built Strange Portal.
internal sealed class StrangePortal : MonoBehaviour, Hoverable, Interactable
{
	public GameObject? Swirl;
	public MeshRenderer? Model;
	public Transform? Centre;

	private const string TargetKey = "surf_portal_target";
	private ZNetView? m_nview;
	private bool m_away = true;
	private float m_cool;

	// Runs after ZNetView.Awake (this component was added after it). A Strange Portal just built,
	// or one saved by an older version under its own prefab hash, is saved from now on as the
	// vanilla wooden portal plus our flag, so unmodded players and servers can load it.
	private void Awake()
	{
		ZNetView? nview = GetComponent<ZNetView>();
		if (nview == null || !nview.IsValid())
		{
			return;
		}
		ZDO zdo = nview.GetZDO();
		if (zdo.GetPrefab() != SurfPortalPiece.PrefabHash)
		{
			return;
		}
		if (!nview.IsOwner())
		{
			nview.ClaimOwnership();
		}
		zdo.SetPrefab(SurfPortalPiece.VanillaHash);
		zdo.Set(SurfPortalPiece.FlagKey, true);
	}

	private void Start()
	{
		m_nview = GetComponent<ZNetView>();
		if (Swirl != null)
		{
			Swirl.SetActive(true);
			foreach (ParticleSystem ps in Swirl.GetComponentsInChildren<ParticleSystem>(true))
			{
				ps.gameObject.SetActive(true);
				ParticleSystem.MainModule main = ps.main;
				main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.95f, 0.35f), new Color(0.6f, 0.2f, 0.85f));
				ps.Play(true);
			}
			foreach (Light l in Swirl.GetComponentsInChildren<Light>(true))
			{
				l.enabled = true;
				l.color = new Color(0.55f, 0.9f, 0.4f);
			}
		}
		if (Model != null)
		{
			Material mat = Model.material;
			mat.EnableKeyword("_EMISSION");
			mat.SetColor("_EmissionColor", new Color(0.35f, 0.9f, 0.25f) * 2.2f);
		}
	}

	private string Target => m_nview != null && m_nview.IsValid() ? m_nview.GetZDO().GetString(TargetKey, "") : "";

	private static string Describe(string target)
	{
		string[] bits = target.Split(':');
		if (bits.Length == 2 && int.TryParse(bits[0], out int b) && int.TryParse(bits[1], out int s))
		{
			return Progress.Describe(b, s);
		}
		return "the dream (sleep to dream the run)";
	}

	public string GetHoverText()
	{
		return "Strange Portal\nLeads to: " + Describe(Target) + "\n[<color=yellow><b>$KEY_Use</b></color>] Change where it leads";
	}

	public string GetHoverName() => "Strange Portal";

	public float GetHoverOffset() => 0f;

	public bool Interact(Humanoid user, bool hold, bool alt)
	{
		if (hold || m_nview == null || !m_nview.IsValid() || user != Player.m_localPlayer)
		{
			return false;
		}
		Player p = (Player)user;
		List<(int biome, int stars)> list = Progress.Checkpoints(p);
		list.Sort((a, b) => a.stars != b.stars ? a.stars.CompareTo(b.stars) : a.biome.CompareTo(b.biome));
		List<string> targets = new() { "" };
		foreach ((int b, int s) in list)
		{
			targets.Add(b + ":" + s);
		}
		int i = targets.IndexOf(Target);
		string next = targets[(i + 1) % targets.Count];
		m_nview.ClaimOwnership();
		m_nview.GetZDO().Set(TargetKey, next);
		p.Message(MessageHud.MessageType.Center, "The portal now leads to " + Describe(next));
		return true;
	}

	public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

	private void Update()
	{
		Player? p = Player.m_localPlayer;
		if (p == null || m_nview == null || !m_nview.IsValid() || SurfGame.Active != null)
		{
			return;
		}
		m_cool -= Time.deltaTime;
		Vector3 centre = Centre != null ? Centre.position : transform.position + Vector3.up;
		Vector3 d = p.transform.position - centre;
		float flat = new Vector2(d.x, d.z).magnitude;
		bool inside = flat < 1.2f && Mathf.Abs(d.y) < 2.5f;
		if (!inside)
		{
			m_away = true;
			return;
		}
		if (!m_away || m_cool > 0f || p.IsTeleporting() || p.InBed() || p.IsDead())
		{
			return;
		}
		m_away = false;
		m_cool = 3f;
		string target = Target;
		string[] bits = target.Split(':');
		if (bits.Length == 2 && int.TryParse(bits[0], out int b) && int.TryParse(bits[1], out int s))
		{
			SurfGame.Begin(p, null, new[] { "surfsup", (b + 1).ToString(), s.ToString() });
			return;
		}
		SurfStory.Instance?.EnterBuiltPortal(p);
	}
}
