using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SnakewaySurfer;

// "surfcompare": puts the minigame's picture of the creature you're looking at right beside the
// real one, copying the real one's animator parameters each frame, and logs both sizes and what
// both are playing. Shows at a glance where the minigame's size, pose, weapon or animation
// differs from the game. Local only: the picture has no scripts, colliders or network object.
internal sealed class Compare : MonoBehaviour
{
	private static Compare? s_active;

	private Character m_real = null!;
	private Animator? m_realAnim;
	private Animator? m_picAnim;
	private GameObject m_picture = null!;
	private Transform m_holder = null!;
	private float m_offset;
	private float m_logTimer;
	private float m_life = 90f;
	private readonly List<(string name, AnimatorControllerParameterType type)> m_params = new();

	public static void Run(Terminal terminal)
	{
		if (s_active != null)
		{
			Object.Destroy(s_active.gameObject);
			s_active = null;
			terminal.AddString("surfcompare: off.");
			return;
		}
		Character? target = Target();
		if (target == null)
		{
			terminal.AddString("surfcompare: look at a creature within 40 m first.");
			return;
		}
		GameObject? prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Utils.GetPrefabName(target.gameObject)) : null;
		if (prefab == null)
		{
			terminal.AddString("surfcompare: couldn't find that creature's prefab.");
			return;
		}
		GameObject go = new GameObject("SurfCompare");
		Compare c = go.AddComponent<Compare>();
		c.Setup(target, prefab);
		s_active = c;
		terminal.AddString($"surfcompare: the minigame's {prefab.name} is beside the real one for 90 s (type surfcompare again to remove it). Details in the log.");
	}

	private static Character? Target()
	{
		Camera cam = Camera.main;
		if (cam == null)
		{
			return null;
		}
		Character? best = null;
		float bestScore = float.MaxValue;
		foreach (Character c in Character.GetAllCharacters())
		{
			if (c == null || c is Player)
			{
				continue;
			}
			Vector3 to = c.GetCenterPoint() - cam.transform.position;
			float d = to.magnitude;
			if (d > 40f)
			{
				continue;
			}
			float angle = Vector3.Angle(cam.transform.forward, to);
			if (angle > 25f)
			{
				continue;
			}
			float score = angle + d * 0.2f;
			if (score < bestScore)
			{
				bestScore = score;
				best = c;
			}
		}
		return best;
	}

	private void Setup(Character real, GameObject prefab)
	{
		m_real = real;
		m_realAnim = real.GetComponentInChildren<Animator>();
		GameObject holder = new GameObject("Templates");
		holder.SetActive(false);
		holder.transform.SetParent(transform, false);
		m_holder = holder.transform;

		CreatureInfo info = CreatureInfo.Of(prefab);
		GameObject template = Pictures.MakePicture(prefab, m_holder, real.gameObject.layer);
		m_picture = Instantiate(template, transform, false);
		// The real one's scale includes its level and world modifiers; match it so only the
		// minigame's own differences show.
		m_picture.transform.localScale = real.transform.localScale;
		m_offset = Mathf.Max(1.5f, info.Height * 0.6f + 1f);
		m_picAnim = m_picture.GetComponentInChildren<Animator>();
		CreatureAttack? attack = info.PickMelee() ?? info.PickRanged();
		if (attack != null)
		{
			Arm(m_picture, info, attack);
		}
		if (m_realAnim != null)
		{
			foreach (AnimatorControllerParameter p in m_realAnim.parameters)
			{
				if (p.type != AnimatorControllerParameterType.Trigger)
				{
					m_params.Add((p.name, p.type));
				}
			}
		}
		Place();
		Log(info, "start");
	}

	private void Arm(GameObject creature, CreatureInfo info, CreatureAttack attack)
	{
		string? path = attack.LeftHand ? info.LeftHand : info.RightHand;
		if (attack.Attach == null || string.IsNullOrEmpty(path))
		{
			return;
		}
		Transform hand = creature.transform.Find(path);
		if (hand == null)
		{
			return;
		}
		GameObject weapon = Instantiate(Pictures.MakePicture(attack.Attach, m_holder, creature.layer), hand, false);
		weapon.transform.localPosition = Vector3.zero;
		weapon.transform.localRotation = Quaternion.identity;
		Vector3 want = attack.AttachScale * creature.transform.localScale.x;
		Vector3 bone = hand.lossyScale;
		weapon.transform.localScale = new Vector3(want.x / Mathf.Max(1e-4f, Mathf.Abs(bone.x)), want.y / Mathf.Max(1e-4f, Mathf.Abs(bone.y)), want.z / Mathf.Max(1e-4f, Mathf.Abs(bone.z)));
	}

	private void Place()
	{
		Transform t = m_real.transform;
		m_picture.transform.SetPositionAndRotation(t.position + t.right * m_offset, t.rotation);
	}

	private void LateUpdate()
	{
		m_life -= Time.deltaTime;
		if (m_real == null || m_life <= 0f)
		{
			Destroy(gameObject);
			return;
		}
		Place();
		if (m_realAnim != null && m_picAnim != null)
		{
			foreach ((string name, AnimatorControllerParameterType type) in m_params)
			{
				switch (type)
				{
					case AnimatorControllerParameterType.Float:
						m_picAnim.SetFloat(name, m_realAnim.GetFloat(name));
						break;
					case AnimatorControllerParameterType.Int:
						m_picAnim.SetInteger(name, m_realAnim.GetInteger(name));
						break;
					case AnimatorControllerParameterType.Bool:
						m_picAnim.SetBool(name, m_realAnim.GetBool(name));
						break;
				}
			}
			// Triggers can't be read, so when the real one changes state the picture follows.
			AnimatorStateInfo real = m_realAnim.GetCurrentAnimatorStateInfo(0);
			AnimatorStateInfo pic = m_picAnim.GetCurrentAnimatorStateInfo(0);
			if (real.fullPathHash != pic.fullPathHash && !m_picAnim.IsInTransition(0))
			{
				m_picAnim.CrossFade(real.fullPathHash, 0.15f, 0, real.normalizedTime);
			}
			m_picAnim.speed = m_realAnim.speed;
		}
		m_logTimer -= Time.deltaTime;
		if (m_logTimer <= 0f)
		{
			m_logTimer = 3f;
			Log(null, "now");
		}
	}

	private void Log(CreatureInfo? info, string when)
	{
		StringBuilder sb = new();
		sb.Append($"[surfcompare] {m_real.name} {when}: ");
		if (info != null)
		{
			sb.Append($"minigame height {info.Height:0.00} (Monster Height {Plugin.MonsterHeight.Value:0.0} shrinks it x{(info.Height > 0.3f ? Mathf.Min(1f, Plugin.MonsterHeight.Value / info.Height) : 1f):0.00}), walk {info.WalkSpeed:0.0} run {info.RunSpeed:0.0}; ");
		}
		sb.Append($"real scale {m_real.transform.lossyScale.x:0.00} level {m_real.GetLevel()}");
		CapsuleCollider cap = m_real.GetComponent<CapsuleCollider>();
		if (cap != null)
		{
			sb.Append($" collider h {cap.height * m_real.transform.lossyScale.y:0.00}");
		}
		if (Pictures.MeshBounds(m_real.gameObject, out Bounds rb))
		{
			sb.Append($" render h {rb.size.y:0.00} w {rb.size.x:0.00}");
		}
		if (Pictures.MeshBounds(m_picture, out Bounds pb))
		{
			sb.Append($"; picture render h {pb.size.y:0.00} w {pb.size.x:0.00}, feet {pb.min.y - m_picture.transform.position.y:+0.00;-0.00} vs real {(Pictures.MeshBounds(m_real.gameObject, out Bounds r2) ? r2.min.y - m_real.transform.position.y : 0f):+0.00;-0.00}");
		}
		sb.Append($"; anim real {Clips(m_realAnim)} speed {(m_realAnim != null ? m_realAnim.speed : 0f):0.00}, picture {Clips(m_picAnim)}");
		if (m_realAnim != null)
		{
			List<string> values = new();
			foreach ((string name, AnimatorControllerParameterType type) in m_params)
			{
				values.Add(type == AnimatorControllerParameterType.Float ? $"{name}={m_realAnim.GetFloat(name):0.00}" : type == AnimatorControllerParameterType.Int ? $"{name}={m_realAnim.GetInteger(name)}" : $"{name}={m_realAnim.GetBool(name)}");
			}
			sb.Append($"; real params {string.Join(" ", values)}");
		}
		Plugin.Log.LogInfo(sb.ToString());
	}

	private static string Clips(Animator? anim)
	{
		if (anim == null)
		{
			return "none";
		}
		List<string> names = new();
		foreach (AnimatorClipInfo c in anim.GetCurrentAnimatorClipInfo(0))
		{
			names.Add($"{c.clip.name}({c.weight:0.0})");
		}
		return names.Count > 0 ? string.Join("+", names) : "?";
	}

	private void OnDestroy()
	{
		if (s_active == this)
		{
			s_active = null;
		}
	}
}
