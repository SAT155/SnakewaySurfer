using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// 1.16.102 (SAT: "Hugin doesn't need to be a 3d object or use the in game chat bubbles"): the
// ravens are a still portrait taken once from their own model, drawn as flat UI in a fixed
// spot at the top right, with a speech bubble under it. Nothing follows the camera, so
// nothing hitches. Used in the run (SurfRaven.cs) and in the world (SurfStory.cs).
// The game's image module is built against a newer netstandard than the mod compiles with, so
// its PNG and JPG calls are made by name (1.16.164).
internal static class Img
{
	private static readonly System.Type? s_type = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");

	public static bool Load(Texture2D tex, byte[] bytes)
	{
		System.Reflection.MethodInfo? m = s_type?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
		return m != null && m.Invoke(null, new object[] { tex, bytes }) is bool ok && ok;
	}

	public static byte[]? Jpg(Texture2D tex, int quality)
	{
		System.Reflection.MethodInfo? m = s_type?.GetMethod("EncodeToJPG", new[] { typeof(Texture2D), typeof(int) });
		return m?.Invoke(null, new object[] { tex, quality }) as byte[];
	}

	public static byte[]? Png(Texture2D tex)
	{
		System.Reflection.MethodInfo? m = s_type?.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });
		return m?.Invoke(null, new object[] { tex }) as byte[];
	}
}

internal static class RavenUi
{
	private static readonly Dictionary<GameObject, Texture2D?> s_portraits = new();
	private static Texture2D? s_disc;
	private static Texture2D? s_ring;
	private static GUIStyle? s_bubble;
	private static readonly Dictionary<GameObject, int> s_tries = new();
	private static readonly Texture2D?[] s_fallback = new Texture2D?[2];
	private static readonly bool[] s_fallbackLooked = new bool[2];

	// 1.16.164: a drawn picture of each raven ships inside the mod, so his circle is never empty
	// when the picture taken from his model fails.
	public static Texture2D? Fallback(bool munin)
	{
		int i = munin ? 1 : 0;
		if (s_fallbackLooked[i])
		{
			return s_fallback[i];
		}
		s_fallbackLooked[i] = true;
		try
		{
			System.Reflection.Assembly asm = typeof(RavenUi).Assembly;
			string want = munin ? "munin.png" : "hugin.png";
			foreach (string name in asm.GetManifestResourceNames())
			{
				if (!name.EndsWith(want, System.StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				using System.IO.Stream stream = asm.GetManifestResourceStream(name)!;
				byte[] bytes = new byte[stream.Length];
				int read = 0;
				while (read < bytes.Length)
				{
					int n = stream.Read(bytes, read, bytes.Length - read);
					if (n <= 0)
					{
						break;
					}
					read += n;
				}
				Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
				if (Img.Load(t, bytes))
				{
					s_fallback[i] = t;
				}
				break;
			}
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Raven drawn picture failed: {e.Message}");
		}
		return s_fallback[i];
	}

	// For the self-test (SurfTest.cs): which picture a raven's circle shows.
	public static string PortraitState(Texture2D? portrait, bool munin) =>
		portrait != null ? "picture from the model" : Fallback(munin) != null ? "drawn picture" : "EMPTY";
	private static Texture2D? s_bubbleTex;

	// Call from Update or LateUpdate, never from OnGUI (it renders a camera).
	private static readonly HashSet<GameObject> s_noMeshLogged = new();

	public static Texture2D? Portrait(GameObject? template)
	{
		if (template == null)
		{
			return null;
		}
		if (Plugin.DrawnRaven.Value)
		{
			return null;
		}
		if (s_portraits.TryGetValue(template, out Texture2D? done))
		{
			return done;
		}
		Texture2D? tex = null;
		GameObject? copy = null;
		GameObject? rig = null;
		RenderTexture? rt = null;
		RenderTexture prev = RenderTexture.active;
		try
		{
			int layer = Plugin.RenderLayer.Value;
			copy = Object.Instantiate(template);
			copy.SetActive(true);
			foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
			{
				t.gameObject.layer = layer;
			}
			copy.transform.position = new Vector3(0f, -6000f, 0f);
			copy.transform.rotation = Quaternion.Euler(0f, SurfStory.HeadYaw(copy) - 20f, 0f);
			Animator? anim = copy.GetComponentInChildren<Animator>();
			if (anim != null)
			{
				anim.Rebind();
				anim.Update(0.5f);
			}
			// The raven's own animation can switch its model off again (its pose while hidden), which
			// left nothing to picture: switched back on here, after the animation has been applied.
			foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
			{
				if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer))
				{
					continue;
				}
				r.enabled = true;
				for (Transform up = r.transform; up != null && up != copy.transform; up = up.parent)
				{
					if (!up.gameObject.activeSelf)
					{
						up.gameObject.SetActive(true);
					}
				}
			}
			foreach (SkinnedMeshRenderer smr in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				smr.updateWhenOffscreen = true;
			}
			if (!Pictures.MeshBounds(copy, out Bounds b) || b.size.y < 0.01f)
			{
				if (s_noMeshLogged.Add(template))
				{
					Plugin.Log.LogWarning($"Raven portrait of {template.name}: no visible mesh to picture ({copy.GetComponentsInChildren<Renderer>(true).Length} renderers, {copy.GetComponentsInChildren<Renderer>(false).Length} active); using the drawn one.");
				}
				s_portraits[template] = null;
				return null;
			}
			rig = new GameObject("RavenPortraitRig");
			Camera cam = rig.AddComponent<Camera>();
			cam.enabled = false;
			cam.clearFlags = CameraClearFlags.SolidColor;
			cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
			cam.cullingMask = 1 << layer;
			cam.fieldOfView = 24f;
			cam.allowHDR = false;
			float radius = Mathf.Max(b.extents.y, b.extents.x, b.extents.z);
			// 1.16.164: a raven is under a metre across. If the model claims far more, frame its
			// skeleton instead (the reported size has been wrong before).
			if (radius > 2.5f)
			{
				Vector3 sum = Vector3.zero;
				List<Vector3> joints = new();
				foreach (SkinnedMeshRenderer smr in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
				{
					foreach (Transform bone in smr.bones)
					{
						if (bone != null)
						{
							joints.Add(bone.position);
							sum += bone.position;
						}
					}
				}
				if (joints.Count > 0)
				{
					Vector3 mid = sum / joints.Count;
					float far = 0.2f;
					foreach (Vector3 j in joints)
					{
						far = Mathf.Max(far, Vector3.Distance(j, mid));
					}
					Plugin.Log.LogInfo($"Raven portrait of {template.name}: model claims a radius of {radius:0.0} m; framing its skeleton instead ({Mathf.Min(far, 2f):0.00} m).");
					b = new Bounds(mid, Vector3.one * Mathf.Min(far, 2f) * 2f);
					radius = Mathf.Min(far, 2f);
				}
			}
			float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
			cam.nearClipPlane = 0.05f;
			cam.farClipPlane = dist + radius * 4f;
			rig.transform.position = b.center + new Vector3(0f, radius * 0.15f, dist);
			rig.transform.rotation = Quaternion.LookRotation(b.center - rig.transform.position);
			Light key = new GameObject("Key").AddComponent<Light>();
			key.transform.SetParent(rig.transform, false);
			key.transform.rotation = Quaternion.Euler(35f, 200f, 0f);
			key.type = LightType.Directional;
			key.intensity = 1.5f;
			key.cullingMask = 1 << layer;
			Light rim = new GameObject("Rim").AddComponent<Light>();
			rim.transform.SetParent(rig.transform, false);
			rim.transform.rotation = Quaternion.Euler(20f, 20f, 0f);
			rim.type = LightType.Directional;
			rim.intensity = 1.2f;
			rim.color = new Color(0.7f, 0.82f, 1f);
			rim.cullingMask = 1 << layer;
			// 1.16.150 (SAT: the circle was empty): the game's deferred path and shaders leave no
			// usable alpha, so draw forward, fog off, once on black and once on white; what
			// changes between them is see-through (as the boss's sky picture does).
			const int size = 256;
			rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
			cam.renderingPath = RenderingPath.Forward;
			cam.targetTexture = rt;
			bool fog = RenderSettings.fog;
			RenderSettings.fog = false;
			Color32[] px = new Color32[size * size];
			int seenCount = 0;
			float fill = 0f;
			// 1.16.164: the model's reported size can't be trusted (it has claimed to be 35 m tall, which
			// left the bird a speck that was then thrown away). So look at what was actually drawn and
			// zoom in on it until the bird fills the picture.
			for (int pass = 0; pass < 4; pass++)
			{
				Color32[] onBlack = Shot(cam, rt, Color.black, size);
				Color32[] onWhite = Shot(cam, rt, Color.white, size);
				seenCount = 0;
				int minX = size, minY = size, maxX = -1, maxY = -1;
				for (int i = 0; i < px.Length; i++)
				{
					Color32 k0 = onBlack[i];
					Color32 k1 = onWhite[i];
					int seen = Mathf.Clamp(255 - ((k1.r - k0.r) + (k1.g - k0.g) + (k1.b - k0.b)) / 3, 0, 255);
					if (seen < 3)
					{
						px[i] = new Color32(0, 0, 0, 0);
						continue;
					}
					seenCount++;
					int x = i % size, y = i / size;
					minX = Mathf.Min(minX, x);
					maxX = Mathf.Max(maxX, x);
					minY = Mathf.Min(minY, y);
					maxY = Mathf.Max(maxY, y);
					// A touch brighter: a black bird on a dark disc.
					float un = 255f / seen * 1.35f;
					px[i] = new Color32((byte)Mathf.Min(255, k0.r * un + 12), (byte)Mathf.Min(255, k0.g * un + 12), (byte)Mathf.Min(255, k0.b * un + 16), (byte)seen);
				}
				fill = seenCount > 0 ? Mathf.Max(maxX - minX + 1, maxY - minY + 1) / (float)size : 0f;
				Plugin.Log.LogInfo($"Raven portrait of {template.name}, pass {pass}: {seenCount} of {px.Length} pixels, fills {fill:0.00} of the picture, view angle {cam.fieldOfView:0.0}.");
				if (seenCount == 0 || (fill >= 0.6f && fill <= 0.97f) || pass == 3)
				{
					break;
				}
				// Turn to the middle of what was drawn and narrow (or widen) the view to fit it.
				Vector3 aim = new Vector3((minX + maxX + 1) * 0.5f / size, (minY + maxY + 1) * 0.5f / size, 0f);
				Ray ray = cam.ViewportPointToRay(aim);
				rig.transform.rotation = Quaternion.LookRotation(ray.direction, Vector3.up);
				cam.fieldOfView = Mathf.Clamp(cam.fieldOfView * (fill > 0.97f ? 1.5f : fill / 0.8f), 0.5f, 60f);
			}
			RenderSettings.fog = fog;
			cam.targetTexture = null;
			if (seenCount >= 400 && fill >= 0.3f)
			{
				tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
				tex.SetPixels32(px);
				tex.Apply();
			}
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Raven portrait failed: {e.Message}");
			tex = null;
		}
		finally
		{
			RenderTexture.active = prev;
			if (rt != null)
			{
				RenderTexture.ReleaseTemporary(rt);
			}
			if (copy != null)
			{
				Object.DestroyImmediate(copy);
			}
			if (rig != null)
			{
				Object.DestroyImmediate(rig);
			}
		}
		if (tex == null)
		{
			// Try again on the next two calls (the model may not have been ready), then settle for
			// the drawn picture (Fallback).
			s_tries.TryGetValue(template, out int tries);
			s_tries[template] = ++tries;
			if (tries < 3)
			{
				return null;
			}
			Plugin.Log.LogWarning($"Raven portrait of {template.name}: no usable picture after {tries} tries; using the drawn one.");
		}
		s_portraits[template] = tex;
		return tex;
	}

	private static Color32[] Shot(Camera cam, RenderTexture rt, Color background, int size)
	{
		cam.backgroundColor = new Color(background.r, background.g, background.b, 0f);
		cam.Render();
		RenderTexture.active = rt;
		Texture2D read = new Texture2D(size, size, TextureFormat.RGBA32, false);
		read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
		read.Apply();
		Color32[] px = read.GetPixels32();
		Object.Destroy(read);
		return px;
	}

	private static Texture2D Disc(bool ring)
	{
		Texture2D? have = ring ? s_ring : s_disc;
		if (have != null)
		{
			return have;
		}
		const int n = 128;
		Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
		Color[] px = new Color[n * n];
		for (int y = 0; y < n; y++)
		{
			for (int x = 0; x < n; x++)
			{
				float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
				float a = ring ? Mathf.Clamp01(1f - Mathf.Abs(d - 0.95f) / 0.04f) : Mathf.Clamp01((0.97f - d) / 0.03f);
				px[y * n + x] = new Color(1f, 1f, 1f, a);
			}
		}
		t.SetPixels(px);
		t.Apply();
		if (ring)
		{
			s_ring = t;
		}
		else
		{
			s_disc = t;
		}
		return t;
	}

	private static GUIStyle Bubble(float k)
	{
		if (s_bubbleTex == null)
		{
			const int n = 32;
			const float r = 12f;
			s_bubbleTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
			Color[] px = new Color[n * n];
			for (int y = 0; y < n; y++)
			{
				for (int x = 0; x < n; x++)
				{
					float cx = Mathf.Clamp(x + 0.5f, r, n - r);
					float cy = Mathf.Clamp(y + 0.5f, r, n - r);
					float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
					px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - d));
				}
			}
			s_bubbleTex.SetPixels(px);
			s_bubbleTex.Apply();
		}
		s_bubble ??= new GUIStyle(GUI.skin.label)
		{
			wordWrap = true,
			alignment = TextAnchor.UpperLeft,
			richText = false,
		};
		s_bubble.normal.background = s_bubbleTex;
		s_bubble.border = new RectOffset(12, 12, 12, 12);
		s_bubble.padding = new RectOffset(Mathf.RoundToInt(16 * k), Mathf.RoundToInt(16 * k), Mathf.RoundToInt(10 * k), Mathf.RoundToInt(12 * k));
		s_bubble.fontSize = Mathf.RoundToInt(23 * k);
		s_bubble.normal.textColor = new Color(0.16f, 0.11f, 0.06f);
		return s_bubble;
	}

	// Slot 0 is Hugin's spot at the top right; slot 1 is just left of it (Munin).
	// age: seconds since he arrived; leave: seconds since he started to go (or -1).
	public static void Draw(Texture2D? portrait, string text, float textAge, float age, float leave, int slot, bool munin, float alpha)
	{
		if (Event.current.type != EventType.Repaint)
		{
			return;
		}
		float k = Mathf.Max(0.6f, Screen.height / 1080f);
		float inA = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.35f));
		float outA = leave >= 0f ? 1f - Mathf.Clamp01(leave / 0.4f) : 1f;
		float a = inA * outA * alpha;
		if (a <= 0.01f)
		{
			return;
		}
		float size = 150f * k;
		float gap = 18f * k;
		// Fixed, under the minimap's corner, whatever status effects are showing.
		float x = Screen.width - size - 28f * k - slot * (size + gap) + (1f - inA) * 50f * k;
		float y = Screen.height * 0.27f + Mathf.Sin(Time.unscaledTime * 1.8f + slot) * 3f * k;
		Color old = GUI.color;
		Rect pr = new Rect(x, y, size, size);
		bool drawn = portrait == null;
		portrait ??= Fallback(munin);
		// The drawn Hugin is black, so his disc is pale; everything else sits on a dark one.
		GUI.color = drawn && !munin && portrait != null ? new Color(0.66f, 0.72f, 0.82f, 0.92f * a) : new Color(0.1f, 0.1f, 0.14f, 0.78f * a);
		GUI.DrawTexture(pr, Disc(false));
		if (portrait != null)
		{
			GUI.color = new Color(1f, 1f, 1f, a);
			GUI.DrawTexture(new Rect(x + size * 0.04f, y + size * 0.04f, size * 0.92f, size * 0.92f), portrait, ScaleMode.ScaleToFit, true);
		}
		GUI.color = munin ? new Color(0.85f, 0.88f, 0.95f, a) : new Color(0.95f, 0.78f, 0.35f, a);
		GUI.DrawTexture(pr, Disc(true));
		if (text.Length > 0)
		{
			// Typed out as he says it.
			int shown = Mathf.Clamp(Mathf.FloorToInt((textAge - 0.15f) * 45f), 0, text.Length);
			if (shown > 0)
			{
				GUIStyle style = Bubble(k);
				float bw = Mathf.Min(460f * k, Screen.width * 0.4f);
				float bh = style.CalcHeight(new GUIContent(text), bw);
				float bx = Mathf.Min(Screen.width - bw - 20f * k, x + size - bw * 0.85f);
				Color oldBg = GUI.backgroundColor;
				GUI.color = new Color(1f, 1f, 1f, 0.94f * a);
				GUI.backgroundColor = munin ? new Color(0.9f, 0.92f, 0.97f) : new Color(0.97f, 0.93f, 0.82f);
				GUI.Label(new Rect(bx, y + size + 8f * k, bw, bh), text.Substring(0, shown), style);
				GUI.backgroundColor = oldBg;
			}
		}
		GUI.color = old;
	}
}
