using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// 1.15.2 (Sam: Eikthyr needs bigger lightning on his attacks). A storm boss's blows come down
// as real bolts from the sky: a jagged, flickering, branching streak with a white core, a burst
// of light where it lands that lights up the whole track, his own lightning effect at double
// size, and a harder shake. While a blow winds up his antlers glow brighter and throw sparks,
// so the light itself tells you one is coming. His antler shots carry their own light.
internal sealed partial class SurfGame
{
	private sealed class Bolt
	{
		public GameObject Go = null!;
		public LineRenderer Core = null!;
		public LineRenderer Glow = null!;
		public readonly List<LineRenderer> Forks = new();
		public Light? Light;
		public Vector3 Top;
		public Vector3 Bottom;
		public float Age;
		public float Life;
		public float Width;
		public float Jag;
		public float LightPower;
		public float Rejag;
	}

	private readonly List<Bolt> m_bolts = new();
	private Material? m_boltCore;
	private Material? m_boltGlow;
	private Light? m_antlerGlow;
	private float m_sparkTimer;

	private Material BoltMaterial(bool core)
	{
		Material? mat = core ? m_boltCore : m_boltGlow;
		if (mat == null)
		{
			Shader shader = Shader.Find("Sprites/Default");
			mat = new Material(shader) { renderQueue = core ? 3150 : 3140 };
			if (core)
			{
				m_boltCore = mat;
			}
			else
			{
				m_boltGlow = mat;
			}
		}
		return mat;
	}

	private LineRenderer BoltLine(GameObject parent, bool core, string name)
	{
		GameObject go = new GameObject(name) { layer = m_layer };
		go.transform.SetParent(parent.transform, false);
		LineRenderer line = go.AddComponent<LineRenderer>();
		line.useWorldSpace = false;
		line.sharedMaterial = BoltMaterial(core);
		line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		line.receiveShadows = false;
		line.numCapVertices = 2;
		line.numCornerVertices = 1;
		line.alignment = LineAlignment.View;
		return line;
	}

	// A bolt from `top` down to `bottom` (stage space), lasting `life` seconds. `light`: how
	// bright the flash where it lands is (0 for none).
	private void LightningBolt(Vector3 top, Vector3 bottom, float width, float life, float light, Color tint)
	{
		GameObject go = new GameObject("Bolt") { layer = m_layer };
		go.transform.SetParent(m_stage, false);
		Bolt b = new Bolt
		{
			Go = go,
			Top = top,
			Bottom = bottom,
			Life = life,
			Width = width,
			Jag = Mathf.Clamp(Vector3.Distance(top, bottom) * 0.06f, 0.3f, 3f),
			LightPower = light,
		};
		b.Glow = BoltLine(go, false, "Glow");
		b.Core = BoltLine(go, true, "Core");
		Color glow = new Color(tint.r, tint.g, tint.b, 0.4f);
		b.Glow.startColor = glow;
		b.Glow.endColor = glow;
		Color core = Color.Lerp(Color.white, tint, 0.15f);
		b.Core.startColor = core;
		b.Core.endColor = core;
		int forks = width > 0.3f ? Random.Range(1, 4) : 0;
		for (int i = 0; i < forks; i++)
		{
			LineRenderer f = BoltLine(go, true, "Fork");
			f.startColor = core;
			f.endColor = new Color(core.r, core.g, core.b, 0f);
			b.Forks.Add(f);
		}
		if (light > 0f)
		{
			Light l = new GameObject("Flash") { layer = m_layer }.AddComponent<Light>();
			l.transform.SetParent(go.transform, false);
			l.transform.localPosition = bottom + Vector3.up * 2f;
			l.type = LightType.Point;
			l.color = Color.Lerp(Color.white, tint, 0.4f);
			l.range = 12f + 6f * light;
			l.intensity = 0f;
			l.shadows = LightShadows.None;
			l.cullingMask = 1 << m_layer;
			b.Light = l;
		}
		Rejag(b);
		m_bolts.Add(b);
	}

	private static void JaggedLine(LineRenderer line, Vector3 from, Vector3 to, int points, float jag)
	{
		line.positionCount = points;
		Vector3 dir = to - from;
		Vector3 side = Vector3.Cross(dir.normalized, Vector3.up);
		if (side.sqrMagnitude < 0.01f)
		{
			side = Vector3.right;
		}
		side.Normalize();
		Vector3 depth = Vector3.Cross(dir.normalized, side);
		for (int i = 0; i < points; i++)
		{
			float p = i / (float)(points - 1);
			Vector3 pos = from + dir * p;
			if (i > 0 && i < points - 1)
			{
				float j = jag * Mathf.Sin(p * Mathf.PI);
				pos += side * Random.Range(-j, j) + depth * Random.Range(-j, j) * 0.5f;
			}
			line.SetPosition(i, pos);
		}
	}

	// A bolt lying across a lane that keeps re-striking (Eikthyr's ground shockwave).
	private sealed class Crackle : MonoBehaviour
	{
		public LineRenderer? Glow;
		public LineRenderer? Core;
		public Light? Light;
		public Vector3 From;
		public Vector3 To;
		private float m_next;

		private void Update()
		{
			if (Core == null || Glow == null)
			{
				return;
			}
			m_next -= Time.deltaTime;
			if (m_next > 0f)
			{
				return;
			}
			m_next = Random.Range(0.05f, 0.09f);
			JaggedLine(Core, From, To, 9, 0.22f);
			Glow.positionCount = Core.positionCount;
			for (int i = 0; i < Core.positionCount; i++)
			{
				Glow.SetPosition(i, Core.GetPosition(i));
			}
			if (Light != null)
			{
				Light.intensity = Random.Range(1.4f, 2.8f);
			}
		}
	}

	// A fresh zig-zag: real bolts flicker as they re-strike along slightly different paths.
	private static void Rejag(Bolt b)
	{
		JaggedLine(b.Core, b.Top, b.Bottom, 14, b.Jag);
		b.Glow.positionCount = b.Core.positionCount;
		for (int i = 0; i < b.Core.positionCount; i++)
		{
			b.Glow.SetPosition(i, b.Core.GetPosition(i));
		}
		foreach (LineRenderer f in b.Forks)
		{
			int at = Random.Range(3, 9);
			Vector3 start = b.Core.GetPosition(at);
			Vector3 down = (b.Bottom - b.Top).normalized;
			Vector3 off = Vector3.Cross(down, Vector3.up);
			if (off.sqrMagnitude < 0.01f)
			{
				off = Vector3.right;
			}
			Vector3 end = start + down * Random.Range(3f, 7f) + off.normalized * Random.Range(-4f, 4f);
			JaggedLine(f, start, end, 6, b.Jag * 0.5f);
		}
	}

	private void UpdateBolts(float dt)
	{
		for (int i = m_bolts.Count - 1; i >= 0; i--)
		{
			Bolt b = m_bolts[i];
			b.Age += dt;
			if (b.Go == null || b.Age >= b.Life)
			{
				if (b.Go != null)
				{
					Destroy(b.Go);
				}
				m_bolts.RemoveAt(i);
				continue;
			}
			float p = b.Age / b.Life;
			// Two or three re-strikes, then it thins out.
			float flicker = p < 0.6f ? (Mathf.Sin(b.Age * 70f) > -0.3f ? 1f : 0.35f) : 1f - (p - 0.6f) / 0.4f;
			b.Rejag -= dt;
			if (b.Rejag <= 0f && p < 0.6f)
			{
				b.Rejag = 0.06f;
				Rejag(b);
			}
			float w = b.Width * flicker;
			b.Core.startWidth = w;
			b.Core.endWidth = w * 0.7f;
			b.Glow.startWidth = w * 4f;
			b.Glow.endWidth = w * 3.2f;
			foreach (LineRenderer f in b.Forks)
			{
				f.startWidth = w * 0.5f;
				f.endWidth = 0.02f;
			}
			if (b.Light != null)
			{
				b.Light.intensity = b.LightPower * 6f * Mathf.Clamp01(1f - p) * flicker;
			}
		}
		UpdateAntlerGlow(dt);
	}

	private void ClearBolts()
	{
		foreach (Bolt b in m_bolts)
		{
			if (b.Go != null)
			{
				Destroy(b.Go);
			}
		}
		m_bolts.Clear();
		if (m_antlerGlow != null)
		{
			Destroy(m_antlerGlow.gameObject);
			m_antlerGlow = null;
		}
	}

	// Where a storm boss's blow lands: a bolt from high above, a burst of light over the whole
	// track, his lightning effect at double size, and a harder shake.
	private void StormStrike(BossDef def, Vector3 at, bool big = true)
	{
		Vector3 top = at + new Vector3(Random.Range(-6f, 6f), 30f, ChaseSign * Random.Range(4f, 12f));
		LightningBolt(top, at, big ? 0.55f : 0.35f, 0.45f, big ? 1.5f : 1f, def.Tint);
		GameObject? fx = FindFx(Sfx.LightningFx);
		if (fx != null)
		{
			PlayFx(fx, at, Quaternion.identity, 3f, null, 2f);
		}
		m_flash = Mathf.Max(m_flash, big ? 1f : 0.7f);
		m_shake = Mathf.Max(m_shake, big ? 0.45f : 0.3f);
	}

	private Vector3 AntlerPoint(BossDef def)
	{
		return m_bossGo!.transform.localPosition + Vector3.up * def.MaxHeight * 0.9f;
	}

	// While a blow winds up, the antlers light up and spark: brighter the closer it is.
	private void UpdateAntlerGlow(float dt)
	{
		BossDef? def = m_boss;
		float charge = 0f;
		if (def != null && def.Storm && m_bossGo != null && m_bossGo.activeInHierarchy && m_bossSink < 0f)
		{
			foreach (ChaseStrike s in m_chaseStrikes)
			{
				if (!s.Landed)
				{
					charge = Mathf.Max(charge, 1f - Mathf.Clamp01(s.Warn / 1.6f));
				}
			}
		}
		if (charge <= 0f)
		{
			if (m_antlerGlow != null)
			{
				m_antlerGlow.intensity = Mathf.MoveTowards(m_antlerGlow.intensity, 0f, dt * 20f);
			}
			return;
		}
		if (m_antlerGlow == null)
		{
			m_antlerGlow = new GameObject("AntlerGlow") { layer = m_layer }.AddComponent<Light>();
			m_antlerGlow.transform.SetParent(m_stage, false);
			m_antlerGlow.type = LightType.Point;
			m_antlerGlow.shadows = LightShadows.None;
			m_antlerGlow.cullingMask = 1 << m_layer;
			m_antlerGlow.color = Color.Lerp(Color.white, def!.Tint, 0.6f);
		}
		Vector3 head = AntlerPoint(def!);
		m_antlerGlow.transform.localPosition = head;
		m_antlerGlow.range = 6f + 8f * charge;
		m_antlerGlow.intensity = (1f + 4f * charge) * Random.Range(0.75f, 1f);
		m_sparkTimer -= dt;
		if (m_sparkTimer <= 0f)
		{
			m_sparkTimer = Mathf.Lerp(0.3f, 0.07f, charge);
			Vector3 to = head + Random.onUnitSphere * Random.Range(1f, 2.5f) + Vector3.up;
			LightningBolt(head, to, 0.08f + 0.1f * charge, 0.12f, 0f, def!.Tint);
		}
	}

	// An antler shot carries its own light and leaves the antlers with a crackle.
	private void ChargeShot(BossDef def, GameObject shot)
	{
		Light l = new GameObject("ShotLight") { layer = m_layer }.AddComponent<Light>();
		l.transform.SetParent(shot.transform, false);
		l.type = LightType.Point;
		l.color = Color.Lerp(Color.white, def.Tint, 0.5f);
		l.range = 9f;
		l.intensity = 3f;
		l.shadows = LightShadows.None;
		l.cullingMask = 1 << m_layer;
		if (m_bossGo != null)
		{
			Vector3 head = AntlerPoint(def);
			LightningBolt(head, shot.transform.localPosition, 0.2f, 0.2f, 0.8f, def.Tint);
		}
		m_flash = Mathf.Max(m_flash, 0.35f);
	}
}
