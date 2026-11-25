using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SnakewaySurfer;

// The `surfkit` console command: a picture book of the game's own dungeon rooms and biomes, so the
// minigame's dungeons can be built to look like them. For every room of the burial chambers,
// sunken crypts, frost caves and infested mines it saves three pictures (a floor plan with the
// roof cut away, and two views from inside) and writes down what the room is made of and where
// each piece sits. Nothing is placed in the world: the rooms are stripped copies on the
// minigame's own layer, far below the map, removed as soon as they are pictured.
internal static class Kit
{
	private sealed class Runner : MonoBehaviour
	{
	}

	private static Runner? s_runner;
	private static bool s_background;

	public static bool Running => s_runner != null;

	public static void Run(Terminal context)
	{
		if (s_runner != null)
		{
			context.AddString("Already running.");
			return;
		}
		s_runner = new GameObject("SnakewaySurferKit").AddComponent<Runner>();
		// Keeps going while you use another window (don't minimise the game: nothing is drawn then).
		s_background = Application.runInBackground;
		Application.runInBackground = true;
		Object.DontDestroyOnLoad(s_runner.gameObject);
		s_runner.StartCoroutine(Work(context));
	}

	private static bool Wanted(string theme) =>
		theme.IndexOf("Crypt", StringComparison.OrdinalIgnoreCase) >= 0
		|| theme.IndexOf("Cave", StringComparison.OrdinalIgnoreCase) >= 0
		|| theme.IndexOf("Dverg", StringComparison.OrdinalIgnoreCase) >= 0;

	private static string Safe(string s)
	{
		StringBuilder b = new StringBuilder();
		foreach (char c in s)
		{
			b.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
		}
		return b.ToString();
	}

	private static string BaseName(string name)
	{
		int i = name.IndexOf(" (", StringComparison.Ordinal);
		return i > 0 && name.EndsWith(")") ? name.Substring(0, i) : name;
	}

	private static IEnumerator Work(Terminal context)
	{
		string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "SnakewaySurferTest", "kit_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
		StringBuilder text = new StringBuilder();
		int done = 0;
		int failed = 0;
		GameObject? holder = null;
		GameObject? rig = null;
		RenderTexture? rt = null;
		Color ambient = RenderSettings.ambientLight;
		bool fog = RenderSettings.fog;
		List<DungeonDB.RoomData> rooms = new();
		try
		{
			Directory.CreateDirectory(dir);
			foreach (DungeonDB.RoomData room in DungeonDB.GetRooms())
			{
				if (room != null && room.m_prefab.IsValid && Wanted(room.m_theme.ToString()))
				{
					rooms.Add(room);
				}
			}
			holder = new GameObject("SnakewaySurferKitHolder");
			holder.SetActive(false);
			rig = new GameObject("SnakewaySurferKitCamera");
			rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"surfkit couldn't start: {e}");
			rooms.Clear();
		}
		context.AddString($"Picturing {rooms.Count} dungeon rooms (about a minute), then visiting each biome (a few minutes).");
		text.AppendLine($"SnakewaySurfer {Plugin.BuildVersion} dungeon room book, {rooms.Count} rooms");
		foreach (DungeonDB.RoomData room in rooms)
		{
			if (done >= 320)
			{
				break;
			}
			try
			{
				One(room, holder!, rig!, rt!, dir, text);
				done++;
			}
			catch (Exception e)
			{
				failed++;
				text.AppendLine($"ROOM (failed) theme={room.m_theme}: {e.Message}");
			}
			yield return null;
		}
		RenderSettings.ambientLight = ambient;
		RenderSettings.fog = fog;
		context.AddString("Rooms done. Now visiting each biome: close this console (F5) and keep your hands off until you are back where you started.");
		yield return new WaitForSeconds(5f);
		yield return Biomes(dir);
		try
		{
			File.WriteAllText(Path.Combine(dir, "rooms.txt"), text.ToString());
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"surfkit couldn't write rooms.txt: {e.Message}");
		}
		if (holder != null)
		{
			Object.Destroy(holder);
		}
		if (rig != null)
		{
			Object.Destroy(rig);
		}
		if (rt != null)
		{
			rt.Release();
			Object.Destroy(rt);
		}
		string msg = $"surfkit: {done} rooms and the biomes pictured{(failed > 0 ? $", {failed} failed" : "")}. Folder: {dir}";
		Plugin.Log.LogInfo(msg);
		context.AddString(msg + " (zip it and send it).");
		try
		{
			Application.OpenURL("file:///" + dir.Replace('\\', '/'));
		}
		catch (Exception)
		{
		}
		Application.runInBackground = s_background;
		if (s_runner != null)
		{
			Object.Destroy(s_runner.gameObject);
			s_runner = null;
		}
	}

	private const int Size = 448;

	// ---------------------------------------------------------------- the real biomes
	// The second half of the book (SAT: "this tool might as well capture each biome too"): you
	// are taken to a stretch of each biome in this world (the same one the minigame borrows its
	// ground from), four pictures are taken at midday, and everything standing within 64 m is
	// counted. You can't be hurt on the way and are put back where you started.
	private static IEnumerator Biomes(string dir)
	{
		Player? p = Player.m_localPlayer;
		if (p == null || WorldGenerator.instance == null || EnvMan.instance == null)
		{
			yield break;
		}
		StringBuilder text = new StringBuilder();
		text.AppendLine($"SnakewaySurfer {Plugin.BuildVersion} biome book");
		Vector3 home = p.transform.position;
		Quaternion homeRot = p.transform.rotation;
		bool god = p.InGodMode();
		bool ghost = p.InGhostMode();
		bool debugTime = EnvMan.instance.m_debugTimeOfDay;
		float debugAt = EnvMan.instance.m_debugTime;
		p.SetGodMode(true);
		p.SetGhostMode(true);
		EnvMan.instance.m_debugTimeOfDay = true;
		EnvMan.instance.m_debugTime = 0.5f;
		foreach (Heightmap.Biome biome in SpawnTables.s_order)
		{
			if (Player.m_localPlayer != p || p == null || p.IsDead())
			{
				break;
			}
			Vector2 spot = default;
			bool found = false;
			try
			{
				found = SurfGame.BiomeSpot(biome, out spot);
			}
			catch (Exception e)
			{
				text.AppendLine($"BIOME {biome}: {e.Message}");
			}
			if (!found)
			{
				text.AppendLine($"BIOME {biome}: no stretch of it found in this world");
				continue;
			}
			float ground = WorldGenerator.instance.GetHeight(spot.x, spot.y);
			Vector3 at = new Vector3(spot.x, Mathf.Max(ground, 30f) + 2f, spot.y);
			if (!p.TeleportTo(at, Quaternion.identity, true))
			{
				text.AppendLine($"BIOME {biome}: couldn't travel there");
				continue;
			}
			float waited = 0f;
			yield return new WaitForSeconds(1f);
			while (p != null && p.IsTeleporting() && waited < 90f)
			{
				waited += 0.5f;
				yield return new WaitForSeconds(0.5f);
			}
			// Let the trees and rocks finish appearing.
			yield return new WaitForSeconds(6f);
			if (p == null)
			{
				break;
			}
			try
			{
				Census(biome, p.transform.position, text);
			}
			catch (Exception e)
			{
				text.AppendLine($"BIOME {biome}: {e.Message}");
			}
			string[] ways = { "north", "east", "south", "west" };
			for (int i = 0; i < 4; i++)
			{
				p.SetLookDir(Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward, 0f);
				yield return new WaitForSeconds(0.8f);
				yield return new WaitForEndOfFrame();
				Shot(Path.Combine(dir, $"biome_{Safe(biome.ToString())}_{ways[i]}.jpg"));
			}
		}
		if (p != null && Player.m_localPlayer == p)
		{
			p.TeleportTo(home, homeRot, true);
			float waited = 0f;
			yield return new WaitForSeconds(1f);
			while (p != null && p.IsTeleporting() && waited < 90f)
			{
				waited += 0.5f;
				yield return new WaitForSeconds(0.5f);
			}
			yield return new WaitForSeconds(2f);
			if (p != null)
			{
				p.SetGodMode(god);
				p.SetGhostMode(ghost);
			}
		}
		if (EnvMan.instance != null)
		{
			EnvMan.instance.m_debugTimeOfDay = debugTime;
			EnvMan.instance.m_debugTime = debugAt;
		}
		try
		{
			File.WriteAllText(Path.Combine(dir, "biomes.txt"), text.ToString());
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"surfkit couldn't write biomes.txt: {e.Message}");
		}
	}

	// Everything standing within 64 m (about three of the game's zones in area), by name.
	private static void Census(Heightmap.Biome biome, Vector3 at, StringBuilder text)
	{
		const float reach = 64f;
		Dictionary<string, int> counts = new();
		Dictionary<string, float> nearest = new();
		int total = 0;
		if (ZNetScene.instance != null)
		{
			foreach (ZNetView view in ZNetScene.instance.m_instances.Values)
			{
				if (view == null || view.GetComponent<Character>() != null)
				{
					continue;
				}
				Vector3 d = view.transform.position - at;
				d.y = 0f;
				if (d.magnitude > reach)
				{
					continue;
				}
				string name = BaseName(view.name.Replace("(Clone)", ""));
				counts.TryGetValue(name, out int n);
				counts[name] = n + 1;
				if (!nearest.TryGetValue(name, out float near) || d.magnitude < near)
				{
					nearest[name] = d.magnitude;
				}
				total++;
			}
		}
		EnvSetup? env = EnvMan.instance != null ? EnvMan.instance.GetCurrentEnvironment() : null;
		string loop = env != null && env.m_ambientLoop != null ? env.m_ambientLoop.name : "-";
		text.AppendLine($"BIOME {biome} at {at.x:0},{at.z:0} height {at.y:0.#}; weather {(env != null ? env.m_name : "?")}, background sound {loop}; {total} things within {reach:0} m ({total / (Mathf.PI * reach * reach) * 4096f:0} per 64 m zone)");
		List<KeyValuePair<string, int>> sorted = new(counts);
		sorted.Sort((x, y) => y.Value.CompareTo(x.Value));
		foreach (KeyValuePair<string, int> c in sorted)
		{
			text.AppendLine($"    {c.Key} x{c.Value} (nearest {nearest[c.Key]:0} m)");
		}
	}

	// The screen as it is, shrunk to 1280 wide.
	private static void Shot(string file)
	{
		try
		{
			int w = Screen.width;
			int h = Screen.height;
			RenderTexture prev = RenderTexture.active;
			RenderTexture.active = null;
			Texture2D full = new Texture2D(w, h, TextureFormat.RGB24, false);
			full.ReadPixels(new Rect(0, 0, w, h), 0, 0);
			full.Apply();
			int tw = Mathf.Min(w, 1280);
			int th = Mathf.Max(1, Mathf.RoundToInt(h * (tw / (float)w)));
			RenderTexture rt = RenderTexture.GetTemporary(tw, th, 0);
			Graphics.Blit(full, rt);
			RenderTexture.active = rt;
			Texture2D small = new Texture2D(tw, th, TextureFormat.RGB24, false);
			small.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
			small.Apply();
			RenderTexture.active = prev;
			RenderTexture.ReleaseTemporary(rt);
			byte[]? jpg = Img.Jpg(small, 80);
			if (jpg != null)
			{
				File.WriteAllBytes(file, jpg);
			}
			Object.Destroy(full);
			Object.Destroy(small);
		}
		catch (Exception e)
		{
			Plugin.Log.LogWarning($"surfkit couldn't save {file}: {e.Message}");
		}
	}

	private static void One(DungeonDB.RoomData room, GameObject holder, GameObject rig, RenderTexture rt, string dir, StringBuilder text)
	{
		room.m_prefab.Load();
		GameObject? copy = null;
		RenderTexture prev = RenderTexture.active;
		try
		{
			GameObject? asset = room.m_prefab.Asset;
			if (asset == null)
			{
				text.AppendLine($"ROOM (no asset) theme={room.m_theme}");
				return;
			}
			Room? info = asset.GetComponent<Room>();
			string size = info != null ? $"{info.m_size.x}x{info.m_size.y}x{info.m_size.z}" : "?";
			string flags = info != null ? $"{(info.m_entrance ? " entrance" : "")}{(info.m_endCap ? " endcap" : "")}{(info.m_divider ? " divider" : "")}" : "";
			int layer = Plugin.RenderLayer.Value;
			// Made under a switched-off parent so nothing in it wakes up (no network objects, no
			// spawners), then stripped to its looks.
			copy = Object.Instantiate(asset, holder.transform, false);
			Pictures.Strip(copy);
			Pictures.SetLayer(copy, layer);
			foreach (Light l in copy.GetComponentsInChildren<Light>(true))
			{
				l.cullingMask = 1 << layer;
			}
			copy.transform.SetParent(null, false);
			copy.transform.position = new Vector3(0f, -6000f, 0f);
			copy.transform.rotation = Quaternion.identity;
			copy.SetActive(true);

			// What it is made of.
			Dictionary<string, int> counts = new();
			List<string> lines = new();
			bool any = false;
			Bounds b = default;
			foreach (MeshRenderer r in copy.GetComponentsInChildren<MeshRenderer>(false))
			{
				if (!r.enabled)
				{
					continue;
				}
				if (!any)
				{
					b = r.bounds;
					any = true;
				}
				else
				{
					b.Encapsulate(r.bounds);
				}
				// The piece is the nearest parent that isn't a bare model level.
				Transform piece = r.transform;
				while (piece.parent != null && piece.parent != copy.transform && IsModelLevel(piece.name))
				{
					piece = piece.parent;
				}
				string name = BaseName(piece.name);
				counts.TryGetValue(name, out int n);
				counts[name] = n + 1;
				if (lines.Count < 160)
				{
					Vector3 p = piece.position - copy.transform.position;
					Vector3 s = r.bounds.size;
					string mat = r.sharedMaterial != null ? r.sharedMaterial.name : "-";
					lines.Add($"    {name} | in {BaseName(piece.parent != null ? piece.parent.name : "")} | at {p.x:0.#},{p.y:0.#},{p.z:0.#} | yaw {piece.eulerAngles.y:0} | size {s.x:0.#}x{s.y:0.#}x{s.z:0.#} | {mat}");
				}
			}
			string file = Safe(room.m_theme.ToString()) + "__" + Safe(asset.name);
			text.AppendLine($"ROOM {asset.name} theme={room.m_theme} size={size}{flags} drawn={(any ? $"{b.size.x:0.#}x{b.size.y:0.#}x{b.size.z:0.#}" : "nothing")} pictures={file}");
			List<KeyValuePair<string, int>> sorted = new(counts);
			sorted.Sort((x, y) => y.Value.CompareTo(x.Value));
			StringBuilder made = new StringBuilder("  made of: ");
			for (int i = 0; i < sorted.Count && i < 30; i++)
			{
				made.Append(sorted[i].Key).Append(" x").Append(sorted[i].Value).Append(", ");
			}
			text.AppendLine(made.ToString());
			foreach (string line in lines)
			{
				text.AppendLine(line);
			}
			if (!any)
			{
				return;
			}

			// The pictures.
			foreach (Transform t in rig.transform)
			{
				Object.Destroy(t.gameObject);
			}
			Camera cam = rig.GetComponent<Camera>();
			if (cam == null)
			{
				cam = rig.AddComponent<Camera>();
				cam.enabled = false;
				cam.clearFlags = CameraClearFlags.SolidColor;
				cam.allowHDR = false;
				cam.renderingPath = RenderingPath.Forward;
				Light lamp = rig.AddComponent<Light>();
				lamp.type = LightType.Point;
				lamp.range = 60f;
				lamp.intensity = 1.3f;
				lamp.cullingMask = 1 << layer;
			}
			cam.cullingMask = 1 << layer;
			cam.backgroundColor = new Color(0.3f, 0.05f, 0.3f, 1f);
			cam.targetTexture = rt;
			RenderSettings.fog = false;
			RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);

			// 1: the floor plan, from above with everything over head height cut away.
			float cut = b.min.y + Mathf.Min(2.4f, b.size.y * 0.6f);
			cam.orthographic = true;
			cam.orthographicSize = Mathf.Max(b.size.x, b.size.z) * 0.5f + 1f;
			rig.transform.position = new Vector3(b.center.x, cut + 100f, b.center.z);
			rig.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
			cam.nearClipPlane = 100f;
			cam.farClipPlane = 100f + b.size.y + 5f;
			Save(cam, rt, Path.Combine(dir, file + "__plan.jpg"));

			// 2 and 3: standing in the middle, looking each way along its longer side.
			cam.orthographic = false;
			cam.fieldOfView = 75f;
			cam.nearClipPlane = 0.1f;
			cam.farClipPlane = 200f;
			Vector3 along = b.size.z >= b.size.x ? Vector3.forward : Vector3.right;
			rig.transform.position = new Vector3(b.center.x, b.min.y + Mathf.Min(1.7f, b.size.y * 0.5f), b.center.z);
			rig.transform.rotation = Quaternion.LookRotation(along);
			Save(cam, rt, Path.Combine(dir, file + "__in1.jpg"));
			rig.transform.rotation = Quaternion.LookRotation(-along);
			Save(cam, rt, Path.Combine(dir, file + "__in2.jpg"));
			cam.targetTexture = null;
		}
		finally
		{
			RenderTexture.active = prev;
			if (copy != null)
			{
				Object.DestroyImmediate(copy);
			}
			try
			{
				room.m_prefab.Release();
			}
			catch (Exception)
			{
			}
		}
	}

	private static bool IsModelLevel(string name)
	{
		string n = name.ToLowerInvariant();
		return n == "default" || n == "new" || n == "worn" || n == "broken" || n == "high" || n == "low" || n == "lod" || n == "model" || n == "visual"
			|| n.StartsWith("lod") || n.StartsWith("stone") || n.StartsWith("_enabled") || n.Contains("_cube") || n == "original";
	}

	private static void Save(Camera cam, RenderTexture rt, string path)
	{
		cam.Render();
		RenderTexture.active = rt;
		Texture2D read = new Texture2D(Size, Size, TextureFormat.RGB24, false);
		read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
		read.Apply();
		byte[]? bytes = Img.Jpg(read, 72);
		Object.Destroy(read);
		if (bytes != null)
		{
			File.WriteAllBytes(path, bytes);
		}
	}
}
