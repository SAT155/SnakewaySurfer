using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SnakewaySurfer;

// 1.16.3 (Sam: a wind gauge like the game's): a ring round the ship's wheel, with a dark arc at
// the bow where the wind is too far ahead to sail by (Ship.GetWindAngleFactor gives no drive
// once the wind comes from within about 37 to 41 degrees of the bow; WindAngleFactor here is
// the same), and the wind's icon riding the ring on the side it blows from, turned the way it
// blows and dimmer the less it fills the sail. The icon in the dark arc: the sail does nothing,
// so turn off the wind or take to the oars.
internal sealed partial class SurfGame
{
	private static Texture2D? s_gaugeRing;
	private static Texture2D? s_gaugeArc;
	private static Texture2D? s_gaugeDisc;

	// A band round a square picture from `inner` to `outer` (fractions of its half width), only
	// within `arc` degrees either side of the top when arc > 0, fading out over `fade` more.
	private static Texture2D GaugeTexture(float inner, float outer, float arc, float fade)
	{
		const int n = 256;
		Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
		Color[] px = new Color[n * n];
		// About two pixels of soft edge.
		const float soft = 4f / n;
		for (int y = 0; y < n; y++)
		{
			for (int x = 0; x < n; x++)
			{
				float dx = (x + 0.5f) / n * 2f - 1f;
				float dy = (y + 0.5f) / n * 2f - 1f;
				float r = Mathf.Sqrt(dx * dx + dy * dy);
				float a = Mathf.Clamp01((r - inner) / soft + 0.5f) * Mathf.Clamp01((outer - r) / soft + 0.5f);
				if (arc > 0f)
				{
					// (Rows go up the picture, so +y is its top as GUI draws it.)
					float angle = Mathf.Abs(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg);
					a *= 1f - Mathf.Clamp01((angle - arc) / Mathf.Max(0.01f, fade));
				}
				px[y * n + x] = new Color(1f, 1f, 1f, a);
			}
		}
		tex.SetPixels(px);
		tex.Apply();
		return tex;
	}

	private void WindGauge(Rect rose, float k)
	{
		s_gaugeRing ??= GaugeTexture(0.8f, 0.97f, 0f, 0f);
		s_gaugeArc ??= GaugeTexture(0.77f, 1f, 37f, 4f);
		s_gaugeDisc ??= GaugeTexture(-1f, 0.8f, 0f, 0f);
		Color old = GUI.color;
		GUI.color = new Color(0f, 0f, 0f, 0.35f);
		GUI.DrawTexture(rose, s_gaugeDisc);
		GUI.color = new Color(0.95f, 0.9f, 0.78f, 0.7f);
		GUI.DrawTexture(rose, s_gaugeRing);
		// The no-go arc turns with the bow (ahead is up on screen).
		Matrix4x4 m = GUI.matrix;
		GUIUtility.RotateAroundPivot(m_heading, rose.center);
		GUI.color = new Color(0.07f, 0.06f, 0.06f, 0.92f);
		GUI.DrawTexture(rose, s_gaugeArc);
		GUI.matrix = m;

		// The wind on the ring, on the side it comes from.
		float factor = WindAngleFactor();
		float pulse = 1f + 0.25f * m_windShift * Mathf.Abs(Mathf.Sin(Time.time * 10f));
		Color tint = Color.Lerp(new Color(0.25f, 0.25f, 0.25f), Color.white, factor);
		float from = (m_windAngle + 180f) * Mathf.Deg2Rad;
		Vector2 at = rose.center + new Vector2(Mathf.Sin(from), -Mathf.Cos(from)) * (rose.width * 0.5f * 0.885f);
		float ws = rose.width * 0.27f * pulse;
		Rect icon = new Rect(at.x - ws * 0.5f, at.y - ws * 0.5f, ws, ws);
		// A dark spot behind it so it reads over the ring.
		GUI.color = new Color(0f, 0f, 0f, 0.5f);
		GUI.DrawTexture(new Rect(at.x - ws * 0.55f, at.y - ws * 0.55f, ws * 1.1f, ws * 1.1f), s_gaugeDisc);
		GUI.color = old;
		if (m_hudWind != null)
		{
			// The game's icon points the way the wind blows when unturned.
			DrawRotated(icon, m_hudWind, m_windAngle, tint);
		}
		else
		{
			GUIUtility.RotateAroundPivot(m_windAngle, at);
			Fill(new Rect(at.x - 3f * k, at.y - ws * 0.4f, 6f * k, ws * 0.8f), tint);
			Star(new Vector2(at.x, at.y - ws * 0.4f), 12f * k, tint);
			GUI.matrix = m;
		}
	}

	// Once: what the game's ship HUD is made of, for the log (names, pictures and sizes).
	private static void LogShipHud(Hud hud)
	{
		if (hud.m_shipHudRoot == null)
		{
			return;
		}
		StringBuilder sb = new StringBuilder("Ship HUD:");
		int lines = 0;
		void Walk(Transform t, int depth)
		{
			if (lines++ > 60)
			{
				return;
			}
			Image? img = t.GetComponent<Image>();
			RectTransform? rt = t as RectTransform;
			sb.Append('\n').Append(' ', depth * 2).Append(t.name);
			if (img != null && img.sprite != null)
			{
				sb.Append(" [").Append(img.sprite.name).Append(']');
			}
			if (rt != null)
			{
				sb.Append($" {rt.rect.width:0}x{rt.rect.height:0} at {rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0} rot {rt.localEulerAngles.z:0}");
			}
			foreach (Transform child in t)
			{
				Walk(child, depth + 1);
			}
		}
		Walk(hud.m_shipHudRoot.transform, 0);
		Plugin.Log.LogInfo(sb.ToString());
	}
}
