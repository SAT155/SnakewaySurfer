using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SnakewaySurfer;

// Turns vanilla prefabs into lifeless "pictures" for the minigame: copies with every script,
// collider and rigidbody removed, on our own render layer, so nothing in them ever touches the
// world, the network or other players.
internal static class Pictures
{
	private static Dictionary<string, GameObject>? s_monsters;
	private static Dictionary<string, GameObject>? s_items;

	public static void ClearCaches()
	{
		s_monsters = null;
		s_items = null;
	}

	public static List<string> SplitList(string list) =>
		list.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(s => s.Trim())
			.Where(s => s.Length > 0)
			.ToList();

	// "greyling" -> the Greyling prefab, found through Character.m_name ("$enemy_greyling").
	// A plain prefab name works too.
	public static GameObject? Monster(string key)
	{
		if (ZNetScene.instance == null)
		{
			return null;
		}
		if (s_monsters == null)
		{
			s_monsters = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
			foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
			{
				if (prefab == null)
				{
					continue;
				}
				Character character = prefab.GetComponent<Character>();
				if (character == null || character is Player || string.IsNullOrEmpty(character.m_name) || !character.m_name.StartsWith("$enemy_"))
				{
					continue;
				}
				string k = character.m_name.Substring("$enemy_".Length);
				// Several prefabs can share a name (event variants, summons); the shortest prefab
				// name is nearly always the plain wild one.
				if (!s_monsters.TryGetValue(k, out GameObject existing) || prefab.name.Length < existing.name.Length)
				{
					s_monsters[k] = prefab;
				}
			}
		}
		if (s_monsters.TryGetValue(key, out GameObject found))
		{
			return found;
		}
		GameObject byName = ZNetScene.instance.GetPrefab(key);
		return byName != null && byName.GetComponent<Character>() != null ? byName : null;
	}

	// "raspberries" -> the Raspberry item prefab, found through m_shared.m_name ("$item_raspberries").
	// A plain item prefab name works too.
	public static GameObject? Item(string key)
	{
		if (ObjectDB.instance == null)
		{
			return null;
		}
		if (s_items == null)
		{
			s_items = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
			foreach (GameObject prefab in ObjectDB.instance.m_items)
			{
				ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null!;
				if (drop == null || drop.m_itemData?.m_shared == null)
				{
					continue;
				}
				string name = drop.m_itemData.m_shared.m_name ?? "";
				if (!name.StartsWith("$item_"))
				{
					continue;
				}
				string k = name.Substring("$item_".Length);
				if (!s_items.TryGetValue(k, out GameObject existing) || prefab!.name.Length < existing.name.Length)
				{
					s_items[k] = prefab!;
				}
			}
		}
		if (s_items.TryGetValue(key, out GameObject found))
		{
			return found;
		}
		GameObject byName = ObjectDB.instance.GetItemPrefab(key);
		return byName != null && byName.GetComponent<ItemDrop>() != null ? byName : null;
	}

	public static GameObject? Prefab(string name)
	{
		if (ZNetScene.instance == null)
		{
			return null;
		}
		GameObject prefab = ZNetScene.instance.GetPrefab(name);
		if (prefab == null && ObjectDB.instance != null)
		{
			prefab = ObjectDB.instance.GetItemPrefab(name);
		}
		return prefab;
	}

	// A lifeless copy under an inactive holder. Awake never runs on the copy because the holder
	// is inactive, so no ZNetView registers with the network.
	public static GameObject MakePicture(GameObject source, Transform inactiveHolder, int layer)
	{
		GameObject copy = Object.Instantiate(source, inactiveHolder, false);
		copy.name = source.name + "_SurfPicture";
		copy.transform.localPosition = Vector3.zero;
		copy.transform.localRotation = Quaternion.identity;
		Strip(copy);
		SetLayer(copy, layer);
		foreach (Animator animator in copy.GetComponentsInChildren<Animator>(true))
		{
			animator.fireEvents = false;
			animator.applyRootMotion = false;
			animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
		}
		return copy;
	}

	public static void Strip(GameObject go)
	{
		// 1.16.70 (Sam: "These birds are stuck in the sky"): a place's own birds (gulls, crows)
		// fly by their RandomFlyingBird script; stripped of it they hang frozen, so they go.
		foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
		{
			if (mb != null && mb.gameObject != go && mb.GetType().Name == "RandomFlyingBird")
			{
				Object.DestroyImmediate(mb.gameObject);
			}
		}
		// A component another one still depends on ([RequireComponent]) can't go yet (Unity logs
		// "Can't remove X because Y depends on it"), so each pass removes only the ones nothing
		// left requires. ZNetView last of all, as the notes advise.
		for (int pass = 0; pass < 8; pass++)
		{
			MonoBehaviour[] all = go.GetComponentsInChildren<MonoBehaviour>(true);
			bool removed = false;
			foreach (MonoBehaviour mb in all)
			{
				if (mb == null || mb is ZNetView || IsRequired(mb))
				{
					continue;
				}
				Object.DestroyImmediate(mb);
				removed = true;
			}
			if (!removed)
			{
				break;
			}
		}
		foreach (ZNetView view in go.GetComponentsInChildren<ZNetView>(true))
		{
			Object.DestroyImmediate(view);
		}
		foreach (Joint joint in go.GetComponentsInChildren<Joint>(true))
		{
			Object.DestroyImmediate(joint);
		}
		foreach (Collider collider in go.GetComponentsInChildren<Collider>(true))
		{
			Object.DestroyImmediate(collider);
		}
		foreach (Rigidbody body in go.GetComponentsInChildren<Rigidbody>(true))
		{
			Object.DestroyImmediate(body);
		}
		foreach (AudioSource audio in go.GetComponentsInChildren<AudioSource>(true))
		{
			Object.DestroyImmediate(audio);
		}
	}

	private static readonly Dictionary<Type, Type[]> s_requires = new();

	private static Type[] Requires(Type type)
	{
		if (!s_requires.TryGetValue(type, out Type[] types))
		{
			List<Type> list = new();
			foreach (object attr in type.GetCustomAttributes(typeof(RequireComponent), true))
			{
				RequireComponent rc = (RequireComponent)attr;
				foreach (Type t in new[] { rc.m_Type0, rc.m_Type1, rc.m_Type2 })
				{
					if (t != null)
					{
						list.Add(t);
					}
				}
			}
			types = list.ToArray();
			s_requires[type] = types;
		}
		return types;
	}

	// Does another component still on the same GameObject require this one?
	private static bool IsRequired(Component c)
	{
		Type type = c.GetType();
		foreach (Component other in c.GetComponents<Component>())
		{
			if (other == null || other == c)
			{
				continue;
			}
			foreach (Type required in Requires(other.GetType()))
			{
				if (required.IsAssignableFrom(type))
				{
					return true;
				}
			}
		}
		return false;
	}

	public static void SetLayer(GameObject go, int layer)
	{
		foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
		{
			t.gameObject.layer = layer;
		}
	}

	// World-space bounds of the visible meshes (particles and lines skipped, they balloon it).
	public static bool MeshBounds(GameObject go, out Bounds bounds)
	{
		bounds = default;
		bool any = false;
		foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
		{
			if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer))
			{
				continue;
			}
			if (!r.enabled || r.bounds.size.sqrMagnitude < 1e-6f)
			{
				continue;
			}
			if (!any)
			{
				bounds = r.bounds;
				any = true;
			}
			else
			{
				bounds.Encapsulate(r.bounds);
			}
		}
		return any;
	}
}
