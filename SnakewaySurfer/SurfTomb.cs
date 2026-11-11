using UnityEngine;

namespace SnakewaySurfer;

// 1.16.94 (SAT: "Character deaths can make a dreamy tombstone, mimicking the regular game"):
// when a run ends on land, the game's own Player_tombstone rises out of the track where you
// fell, pale and glowing like the rest of the dream, with a soft wisp of spirit smoke.
internal sealed partial class SurfGame
{
	private GameObject? m_tomb;
	private float m_tombTime = -1f;
	private float m_tombY;

	private void RaiseTomb()
	{
		ClearTomb();
		if (m_atSea || m_avatar == null)
		{
			return;
		}
		GameObject? prefab = Pictures.Prefab("Player_tombstone");
		if (prefab == null)
		{
			return;
		}
		float z = m_z + 1.2f;
		GameObject go = Instantiate(Picture(prefab), m_stage, false);
		go.SetActive(true);
		m_tombY = RoadY(z);
		go.transform.localPosition = new Vector3(m_x, m_tombY - 1.4f, z);
		go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
		// Dreamlike: the stone's own look, washed pale blue with a faint glow.
		foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
		{
			foreach (Material m in r.materials)
			{
				if (m.HasProperty("_Color"))
				{
					m.color = Color.Lerp(m.color, new Color(0.7f, 0.82f, 1f), 0.55f);
				}
				if (m.HasProperty("_EmissionColor"))
				{
					m.EnableKeyword("_EMISSION");
					m.SetColor("_EmissionColor", new Color(0.18f, 0.26f, 0.45f));
				}
			}
		}
		var lightGo = new GameObject("SurfTombGlow");
		lightGo.transform.SetParent(go.transform, false);
		lightGo.transform.localPosition = new Vector3(0f, 1.2f, -0.6f);
		Light l = lightGo.AddComponent<Light>();
		l.type = LightType.Point;
		l.color = new Color(0.55f, 0.7f, 1f);
		l.range = 5f;
		l.intensity = 1.4f;
		l.shadows = LightShadows.None;
		l.cullingMask = 1 << m_layer;
		m_things.Add(new Thing { Go = go, Kind = Kind.Scenery, Z = z, HalfLength = 1f, Collidable = false });
		m_tomb = go;
		m_tombTime = 0f;
		if (FindFx("vfx_odin_despawn|vfx_ghost_death|vfx_Potion_stamina_medium") is GameObject wisp)
		{
			PlayFx(wisp, new Vector3(m_x, m_tombY + 0.4f, z), Quaternion.identity, 2.5f);
		}
		m_audio.Play("sfx_odin_despawn|sfx_ghost_death|sfx_dream_start", 0.7f, 1f);
	}

	// Rises over a second and a half, then breathes gently while the crash screen is up.
	private void UpdateTomb(float dt)
	{
		if (m_tomb == null || m_tombTime < 0f)
		{
			return;
		}
		m_tombTime += dt;
		float u = Mathf.SmoothStep(0f, 1f, m_tombTime / 1.5f);
		Vector3 p = m_tomb.transform.localPosition;
		p.y = Mathf.Lerp(m_tombY - 1.4f, m_tombY, u) + (u >= 1f ? Mathf.Sin(m_tombTime * 1.3f) * 0.04f : 0f);
		m_tomb.transform.localPosition = p;
	}

	private void ClearTomb()
	{
		if (m_tomb != null)
		{
			Destroy(m_tomb);
		}
		m_tomb = null;
		m_tombTime = -1f;
	}
}
