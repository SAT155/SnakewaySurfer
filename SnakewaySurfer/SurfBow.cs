using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SnakewaySurfer;

// 1.16.2 (SAT): Moder is shot down with the Draugr Fang and obsidian arrows, aimed with the
// mouse through the game's own crosshair, instead of a ballista. Grabbing the arrows stops the
// run for a few seconds: the bow is in your hand, she weaves over the track, and you hold to
// draw (the bow's own draw clip; the circle closes in and turns yellow as in Hud.UpdateCrosshair)
// and let go to loose. A full draw flies fast and true; a snap shot is slow, drops and strays.
// Each arrow flies on its real path (gravity from the arrow's own projectile) and hits her
// wherever that path goes through her, wings and all, not only where it was aimed.
internal sealed partial class SurfGame
{
	private float m_aimTime;
	private Vector2 m_aim;
	private float m_aimClock;
	private const float AimSeconds = 6f;
	private const int ArrowsPerGrab = 3;
	private const string ModerBow = "BowDraugrFang|BowHuntsman|BowFineWood|Bow";
	private const string ModerArrow = "ArrowObsidian|ArrowIron|ArrowFlint";
	// How far off she weaves while you aim (closer than she flies).
	private const float AimDistance = 32f;
	private int m_arrowsLeft;
	private float m_draw;
	private bool m_drawing;
	// Draw keys must be let go once after the grab (Space may still be down from a jump).
	private bool m_drawArmed;
	// After the hit: a beat to see the arrow stick, then the run goes on.
	private float m_aimDone = -1f;
	private bool m_arrowLanded;
	private string m_drawState = "bow_aim";
	private float m_drawTime = 0.9f;
	private SkinnedMeshRenderer[] m_bossBody = new SkinnedMeshRenderer[0];

	private sealed class Arrow
	{
		public GameObject? Go;
		public Vector3 Pos;
		public Vector3 Vel;
		public float Gravity;
		public float Life;
		public bool Stuck;
		public EffectList? HitFx;
	}

	private readonly List<Arrow> m_arrows = new();

	private void StartAim()
	{
		m_aimTime = AimSeconds;
		m_aim = new Vector2(0.5f, 0.62f);
		m_speed = 0f;
		m_arrowsLeft = ArrowsPerGrab;
		m_draw = 0f;
		m_drawing = false;
		m_drawArmed = false;
		m_aimDone = -1f;
		m_arrowLanded = false;
		m_drawState = "bow_aim";
		m_drawTime = 0.9f;
		if (Wield(ModerBow) && m_wieldShared?.m_attack != null)
		{
			m_drawState = DrawState(m_wieldShared.m_attack);
			m_drawTime = Mathf.Clamp(DrawDuration(m_wieldShared.m_attack), 0.4f, 1.6f);
		}
		m_bossBody = m_bossGo != null ? m_bossGo.GetComponentsInChildren<SkinnedMeshRenderer>() : new SkinnedMeshRenderer[0];
		m_audio.Play("sfx_equip_start|sfx_pickable_pick", 0.9f, 1f);
	}

	// The bow's draw state and time, read by name (they're Attack fields on the bow's attack).
	private static string DrawState(Attack attack) =>
		typeof(Attack).GetField("m_drawAnimationState")?.GetValue(attack) is string s && s.Length > 0 ? s : "bow_aim";

	private static float DrawDuration(Attack attack) =>
		typeof(Attack).GetField("m_drawDurationMin")?.GetValue(attack) is float f && f > 0.05f ? f : 0.9f;

	private void UpdateAim(float dt, bool canInput)
	{
		m_aimTime -= dt;
		m_aimClock += dt;
		m_speed = 0f;
		if (m_bossGo != null && m_boss != null)
		{
			// She weaves about over the track while you aim (sidesteps, when she's down on it).
			bool down = !Flies(m_boss) || m_bossLanded;
			float x = Mathf.Sin(m_aimClock * 1.7f) * (down ? 2.5f : 7f);
			float y = down ? 0f : m_boss.Fly + 1.5f + Mathf.Sin(m_aimClock * 2.6f) * 2.2f;
			m_bossZ = m_chase ? m_z - ChaseDistance(m_boss) - 6f : m_z + Mathf.Min(AimDistance, BossDistance(m_boss));
			m_bossOff = m_bossZ - m_z;
			m_bossGo.transform.localPosition = Vector3.Lerp(m_bossGo.transform.localPosition, new Vector3(x, RoadY(m_bossZ) + y, m_bossZ), dt * 4f);
			m_bossGo.transform.localRotation = Quaternion.RotateTowards(m_bossGo.transform.localRotation, Quaternion.Euler(0f, m_chase ? 0f : 180f, 0f), 360f * dt);
		}
		if (m_aimDone >= 0f)
		{
			m_aimDone -= dt;
			if (m_aimDone < 0f)
			{
				EndAim();
			}
			return;
		}
		bool keys = Input.GetKey(KeyCode.Mouse0) || Input.GetKey(KeyCode.Space) || Input.GetKey(Plugin.ThrowKey.Value);
		if (canInput)
		{
			// The mouse aims as it turns the camera in the game (its sensitivity and invert
			// setting); the arrow keys and WASD work too.
			Vector2 look = MouseLook();
			float vfov = Mathf.Max(10f, Fov);
			float hfov = vfov * Screen.width / Mathf.Max(1f, Screen.height);
			Vector2 move = new Vector2(look.x / hfov, look.y / vfov);
			float sx = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
			float sy = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
			move += new Vector2(sx * 0.5f, sy * 0.5f * Screen.width / Mathf.Max(1f, Screen.height)) * dt;
			m_aim += Vector2.ClampMagnitude(move, 0.25f);
			m_aim = new Vector2(Mathf.Clamp(m_aim.x, 0.03f, 0.97f), Mathf.Clamp(m_aim.y, 0.05f, 0.95f));
			if (!keys)
			{
				m_drawArmed = true;
			}
		}
		bool hold = canInput && m_drawArmed && keys && m_arrowsLeft > 0 && m_aimTime > 0f;
		if (hold)
		{
			if (!m_drawing)
			{
				m_drawing = true;
				m_draw = 0f;
				AvatarBool(m_drawState, true);
				m_audio.Play("sfx_bow_draw", 0.9f, Random.Range(0.97f, 1.03f));
			}
			m_draw = Mathf.MoveTowards(m_draw, 1f, dt / m_drawTime);
		}
		else if (m_drawing)
		{
			LooseArrow();
		}
		// Out of arrows or time, once the last one has landed.
		if (!m_drawing && (m_arrowsLeft <= 0 || m_aimTime <= 0f) && !ArrowsFlying())
		{
			EndAim();
		}
	}

	private void EndAim()
	{
		m_aimTime = 0f;
		m_aimDone = -1f;
		m_drawing = false;
		m_draw = 0f;
		AvatarBool(m_drawState, false);
		Unwield();
	}

	private bool ArrowsFlying()
	{
		foreach (Arrow a in m_arrows)
		{
			if (!a.Stuck && a.Go != null)
			{
				return true;
			}
		}
		return false;
	}

	private static MethodInfo? s_mouseDelta;
	private static object? s_mouseTarget;
	private static bool s_mouseLooked;

	// The mouse's movement this frame as the game reads it for looking about (ZInput, found
	// by name), scaled by the game's mouse sensitivity.
	private static Vector2 MouseLook()
	{
		if (!s_mouseLooked)
		{
			s_mouseLooked = true;
			s_mouseDelta = typeof(ZInput).GetMethod("GetMouseDelta", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance, null, System.Type.EmptyTypes, null);
			if (s_mouseDelta != null && !s_mouseDelta.IsStatic)
			{
				s_mouseTarget = typeof(ZInput).GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
					?? typeof(ZInput).GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
				if (s_mouseTarget == null)
				{
					s_mouseDelta = null;
				}
			}
		}
		Vector2 delta = Vector2.zero;
		if (s_mouseDelta != null)
		{
			try
			{
				delta = (Vector2)s_mouseDelta.Invoke(s_mouseTarget, null);
			}
			catch
			{
				s_mouseDelta = null;
			}
		}
		else
		{
			try
			{
				delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
			}
			catch
			{
				delta = Vector2.zero;
			}
		}
		delta *= PlayerController.m_mouseSens;
		if (PlayerController.m_invertMouse)
		{
			delta.y = -delta.y;
		}
		return delta;
	}

	// Where an arrow leaves: the bow in your hand, else your chest (world space).
	private Vector3 BowPoint()
	{
		if (m_wielded != null)
		{
			return m_wielded.transform.position;
		}
		return m_avatar != null ? m_avatar.transform.position + m_stage.up * 1.5f : m_camera.transform.position;
	}

	private void LooseArrow()
	{
		float draw = m_draw;
		m_drawing = false;
		m_draw = 0f;
		AvatarBool(m_drawState, false);
		AvatarSwing(m_wieldShared, 0);
		m_audio.Play("sfx_bow_fire", 1f, Random.Range(0.95f, 1.05f));
		if (m_arrowsLeft <= 0 || m_camera == null)
		{
			return;
		}
		m_arrowsLeft--;
		// Towards the point under the crosshair, as far off as she is (the game aims the
		// arrow from the bow at what the crosshair is on).
		Ray ray = m_camera.ScreenPointToRay(new Vector3(m_aim.x * Screen.width, m_aim.y * Screen.height, 0f));
		float depth = m_bossGo != null && m_boss != null
			? Vector3.Distance(m_camera.transform.position, m_bossGo.transform.position + m_stage.up * m_boss.MaxHeight * 0.4f)
			: 30f;
		Vector3 target = ray.origin + ray.direction * depth;
		Vector3 from = BowPoint();
		// A snap shot strays and flies slow; a full draw flies fast and true.
		float spread = Mathf.Lerp(7f, 0.6f, draw);
		Vector3 dir = (target - from).normalized;
		dir = Quaternion.AngleAxis(Random.Range(-spread, spread), m_camera.transform.up) * Quaternion.AngleAxis(Random.Range(-spread, spread), m_camera.transform.right) * dir;
		float speed = Mathf.Lerp(16f, 62f, draw);
		ItemDrop.ItemData.SharedData? ammo = null;
		foreach (string name in ModerArrow.Split('|'))
		{
			ammo = SharedOf(Pictures.Item(name));
			if (ammo != null)
			{
				break;
			}
		}
		GameObject? shot = ammo?.m_attack?.m_attackProjectile;
		Projectile? projectile = shot != null ? shot.GetComponent<Projectile>() : null;
		GameObject go = ArrowPicture(shot);
		Vector3 pos = m_stage.InverseTransformPoint(from);
		Vector3 vel = m_stage.InverseTransformDirection(dir) * speed;
		go.transform.localPosition = pos;
		go.transform.localRotation = Quaternion.LookRotation(vel);
		m_arrows.Add(new Arrow
		{
			Go = go,
			Pos = pos,
			Vel = vel,
			Gravity = projectile != null ? Mathf.Clamp(projectile.m_gravity, 1f, 12f) : 5f,
			HitFx = projectile != null ? projectile.m_hitEffects : null,
		});
	}

	// The arrow as the game draws it in flight (its projectile), else the item.
	private GameObject ArrowPicture(GameObject? shot)
	{
		GameObject? go = null;
		if (shot != null)
		{
			go = Instantiate(Picture(shot), m_stage, false);
			go.SetActive(true);
			foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
			{
				ps.Play(true);
			}
			if (go.GetComponentsInChildren<MeshRenderer>(true).Length == 0)
			{
				Destroy(go);
				go = null;
			}
		}
		if (go == null)
		{
			GameObject? item = null;
			foreach (string name in ModerArrow.Split('|'))
			{
				item = Pictures.Item(name);
				if (item != null)
				{
					break;
				}
			}
			go = item != null ? Fitted(Picture(item), Quaternion.identity, new Vector3(0.12f, 0.12f, 1.1f), false) : GameObject.CreatePrimitive(PrimitiveType.Cube);
			if (item == null)
			{
				Destroy(go.GetComponent<Collider>());
				go.transform.localScale = new Vector3(0.05f, 0.05f, 1f);
			}
		}
		go.transform.SetParent(m_stage, false);
		Pictures.SetLayer(go, m_layer);
		return go;
	}

	// Each frame (aiming or not): arrows fly on their path, stick where they hit.
	private void UpdateArrows(float dt)
	{
		for (int i = m_arrows.Count - 1; i >= 0; i--)
		{
			Arrow a = m_arrows[i];
			if (a.Go == null)
			{
				m_arrows.RemoveAt(i);
				continue;
			}
			a.Life += dt;
			if (a.Stuck)
			{
				if (a.Life > 1.8f)
				{
					Destroy(a.Go);
					m_arrows.RemoveAt(i);
				}
				continue;
			}
			Vector3 from = a.Pos;
			a.Vel += Vector3.down * (a.Gravity * dt);
			a.Pos += a.Vel * dt;
			a.Go.transform.localPosition = a.Pos;
			if (a.Vel.sqrMagnitude > 0.01f)
			{
				a.Go.transform.localRotation = Quaternion.LookRotation(a.Vel);
			}
			if (!m_arrowLanded && ArrowHitsBoss(from, a.Pos, out Vector3 at))
			{
				ArrowHit(a, at);
				continue;
			}
			float ground = RoadY(a.Pos.z);
			if (a.Pos.y < ground)
			{
				// Into the ground, where it stays a moment.
				a.Pos = new Vector3(a.Pos.x, ground, a.Pos.z);
				a.Go.transform.localPosition = a.Pos;
				a.Stuck = true;
				a.Life = 0.6f;
				m_audio.Play("sfx_arrow_hit", Mathf.Clamp01(1.2f - Mathf.Abs(a.Pos.z - m_z) / 40f) * 0.5f, 1.1f);
			}
			else if (a.Life > 3f)
			{
				Destroy(a.Go);
				m_arrows.RemoveAt(i);
			}
		}
	}

	// Did the arrow's path this frame go through her? A sphere about her middle, or her body
	// as drawn (the box of each skinned mesh, a little inside its edges).
	private bool ArrowHitsBoss(Vector3 from, Vector3 to, out Vector3 at)
	{
		at = to;
		if (m_bossGo == null || m_boss == null || !m_bossGo.activeInHierarchy || m_bossHidden > 0f || m_bossSink >= 0f)
		{
			return false;
		}
		Vector3 centre = m_bossGo.transform.localPosition + Vector3.up * (m_boss.MaxHeight * 0.4f);
		float radius = Mathf.Max(2f, m_boss.MaxHeight * 0.5f);
		Vector3 seg = to - from;
		float t = seg.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(centre - from, seg) / seg.sqrMagnitude) : 0f;
		Vector3 closest = from + seg * t;
		if ((closest - centre).sqrMagnitude <= radius * radius)
		{
			at = closest;
			return true;
		}
		Vector3 wFrom = m_stage.TransformPoint(from);
		Vector3 wSeg = m_stage.TransformPoint(to) - wFrom;
		float length = wSeg.magnitude;
		if (length < 1e-4f)
		{
			return false;
		}
		Ray ray = new Ray(wFrom, wSeg / length);
		foreach (SkinnedMeshRenderer r in m_bossBody)
		{
			if (r == null || !r.enabled)
			{
				continue;
			}
			Bounds b = r.bounds;
			b.extents *= 0.8f;
			if (b.IntersectRay(ray, out float d) && d <= length)
			{
				at = m_stage.InverseTransformPoint(ray.GetPoint(d));
				return true;
			}
		}
		return false;
	}

	private void ArrowHit(Arrow a, Vector3 at)
	{
		m_arrowLanded = true;
		a.Pos = at;
		a.Stuck = true;
		a.Life = 0f;
		if (a.Go != null)
		{
			a.Go.transform.localPosition = at;
			if (m_bossGo != null)
			{
				// It stays in her as she reels.
				a.Go.transform.SetParent(m_bossGo.transform, true);
			}
		}
		PlayEffectParticles(a.HitFx, at);
		m_audio.PlayEffects(a.HitFx, 1f);
		m_audio.Play("sfx_arrow_hit", 1f, 0.95f);
		Flash(new Color(1f, 0.95f, 0.75f), 0.35f);
		BossHit();
		// One hit per grab: the rest of the quiver is spent.
		m_arrowsLeft = 0;
		if (m_drawing)
		{
			m_drawing = false;
			AvatarBool(m_drawState, false);
		}
		if (m_aimTime > 0f)
		{
			m_aimDone = 0.7f;
		}
	}

	private void ClearArrows()
	{
		foreach (Arrow a in m_arrows)
		{
			if (a.Go != null)
			{
				Destroy(a.Go);
			}
		}
		m_arrows.Clear();
		if (m_aimTime > 0f)
		{
			EndAim();
		}
	}

	// While aiming the avatar turns a little towards the crosshair.
	private float AimYaw()
	{
		if (m_aimTime <= 0f || m_camera == null || m_avatar == null || m_aimDone >= 0f)
		{
			return 0f;
		}
		Ray ray = m_camera.ScreenPointToRay(new Vector3(m_aim.x * Screen.width, m_aim.y * Screen.height, 0f));
		Vector3 local = m_stage.InverseTransformPoint(ray.origin + ray.direction * AimDistance) - m_avatar.transform.localPosition;
		return Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Max(1f, local.z)) * Mathf.Rad2Deg, -40f, 40f);
	}

	private void AimGui(float k, float w, float h)
	{
		if (m_aimDone >= 0f)
		{
			return;
		}
		float x = m_aim.x * w;
		float y = (1f - m_aim.y) * h;
		Hud? hud = Hud.instance;
		// The game's crosshair: the dot, and the bow's circle closing in (and turning yellow)
		// as you draw.
		UnityEngine.UI.Image? dotImage = hud != null && hud.m_crosshair != null ? hud.m_crosshair.GetComponent<UnityEngine.UI.Image>() : null;
		Sprite? dot = dotImage != null ? dotImage.sprite : null;
		if (dot != null)
		{
			float ds = HudSize(dotImage!, 32f) * k;
			DrawSprite(new Rect(x - ds * 0.5f, y - ds * 0.5f, ds, ds), dot, new Color(1f, 1f, 1f, 0.9f));
		}
		else
		{
			Shadowed(new Rect(x - 60 * k, y - 60 * k, 120 * k, 120 * k), "+", m_big!);
		}
		if (m_drawing && hud != null && hud.m_crosshairBow != null && hud.m_crosshairBow.sprite != null)
		{
			float rs = HudSize(hud.m_crosshairBow, 128f) * k * Mathf.Lerp(1f, 0.15f, m_draw);
			DrawSprite(new Rect(x - rs * 0.5f, y - rs * 0.5f, rs, rs), hud.m_crosshairBow.sprite, Color.Lerp(new Color(1f, 1f, 1f, 0f), Color.yellow, m_draw));
		}
		// The arrows left in the quiver, under it, and the time left as a thin bar.
		if (ItemIcon(ModerArrow) is Sprite arrow)
		{
			float s = 30f * k;
			for (int i = 0; i < m_arrowsLeft; i++)
			{
				DrawSprite(new Rect(x - (m_arrowsLeft * s) * 0.5f + i * s, y + 44f * k, s, s), arrow, Color.white);
			}
		}
		Bar(new Rect(x - 50 * k, y + 80 * k, 100 * k, 6 * k), Mathf.Clamp01(m_aimTime / AimSeconds), new Color(1f, 0.85f, 0.3f));
		if (Plugin.PopupText.Value)
		{
			Shadowed(new Rect(w * 0.1f, h * 0.12f, w * 0.8f, 100 * k),
				$"Mouse to aim, hold to draw, let go to loose  ({Mathf.Max(0f, m_aimTime):0.0} s)", m_mid!);
		}
	}

	// A HUD image's size at the HUD's own layout (its rect), else a guess.
	private static float HudSize(UnityEngine.UI.Image image, float fallback)
	{
		float size = image.rectTransform != null ? image.rectTransform.rect.width : 0f;
		return size > 4f && size < 600f ? size : fallback;
	}
}
