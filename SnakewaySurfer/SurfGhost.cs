using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace SnakewaySurfer;

// 1.18 (Raven Race ghosts, step 1): each Journey remembers your fastest win and plays it back
// as a pale copy of your viking running the road with you. A rune by the stage marker shows
// whether you're ahead (green) or behind (red). The ghost fades when the boss stage starts,
// and the win screen sets your time against it. It can't touch anything, and only you see it.
// Runs picked up from a checkpoint don't record (they didn't run the whole Journey).
// Files: BepInEx/config/SnakewaySurfer/Ghosts/<character>/<biome>_<stars>.ghost (GhostTrack.cs).
internal sealed partial class SurfGame
{
	private static readonly Color s_ghostTint = new Color(0.62f, 0.86f, 1f, 1f);
	private const float GhostAlpha = 0.32f;

	// This run, being recorded (null when not a whole Journey or ghosts are off).
	private GhostTrack? m_ghostRec;
	// The best run of this Journey so far, raced against (null: none yet).
	private GhostTrack? m_ghostBest;
	private GameObject? m_ghost;
	private Renderer[] m_ghostRenderers = new Renderer[0];
	private Animator? m_ghostAnim;
	private readonly HashSet<int> m_ghostParams = new();
	private Material? m_ghostMat;
	private float m_ghostShow;
	private int m_ghostIdx = -1;
	private float m_ghostLastY;
	private bool m_ghostRolled;
	// -1 you're behind it, 1 ahead, 0 level (or no ghost to race).
	private int m_ghostLead;
	private bool m_ghostRacing;
	// For the win screen: the ghost's time (-1: there was none) and whether this run is the new one.
	private float m_ghostPrevTime = -1f;
	private bool m_ghostNewBest;

	// At the start of each run, once it's known whether it starts from the beginning.
	private void ResetGhost()
	{
		m_ghostRec = null;
		m_ghostBest = null;
		m_ghostIdx = -1;
		m_ghostLastY = 0f;
		m_ghostRolled = false;
		m_ghostLead = 0;
		m_ghostRacing = false;
		m_ghostShow = 0f;
		m_ghostPrevTime = -1f;
		m_ghostNewBest = false;
		ShowGhost(0f);
		if (!m_journey || !Plugin.RaceGhost.Value || Touring || m_player == null || !m_tally.FromStart)
		{
			return;
		}
		m_ghostRec = new GhostTrack { Biome = m_journeyBiome, Stars = m_journeyStars };
		m_ghostBest = LoadGhost(m_journeyBiome, m_journeyStars);
		m_ghostPrevTime = m_ghostBest != null ? m_ghostBest.Time : -1f;
		Plugin.Log.LogInfo(m_ghostBest != null
			? $"Ghost: racing your best {Progress.Describe(m_journeyBiome, m_journeyStars)} Journey, {FormatTime(m_ghostBest.Time)} ({m_ghostBest.Samples.Count} samples)."
			: $"Ghost: no ghost yet for {Progress.Describe(m_journeyBiome, m_journeyStars)}; this run is recorded.");
	}

	// The road you're on now is open (not at sea, in a dungeon, at a camp or in a shop).
	private bool OnOpenRoad => !m_sea.AtSea && !m_camping && !m_shopOpen && !InCrypt(m_z);

	// The game's own roll started (SurfGame.Roll).
	private void NoteGhostRoll() => m_ghostRolled = true;

	// Each frame while running (after UpdateJourney).
	private void UpdateGhost()
	{
		if (m_ghostRec == null || m_journeyStage < 0)
		{
			return;
		}
		if (m_journeyStage < 2)
		{
			byte flags = (byte)((m_ghostRolled ? GhostTrack.Rolled : 0) | (OnOpenRoad ? 0 : GhostTrack.Hidden));
			m_ghostRec.Record(m_runTime, m_x, m_y, m_z, flags);
		}
		m_ghostRolled = false;
	}

	// On the win (WinJourney): a faster run becomes the ghost.
	private void GhostWon()
	{
		if (m_ghostRec == null || m_player == null)
		{
			return;
		}
		m_ghostRec.Time = m_wonTime;
		if (!GhostTrack.Beats(m_wonTime, m_ghostBest))
		{
			Plugin.Log.LogInfo($"Ghost: {FormatTime(m_wonTime)}, not faster than your best {FormatTime(m_ghostPrevTime)}.");
			return;
		}
		m_ghostNewBest = true;
		SaveGhost(m_ghostRec);
		m_ghostBest = m_ghostRec;
		Plugin.Log.LogInfo($"Ghost: new best {FormatTime(m_wonTime)} saved ({m_ghostRec.Samples.Count} samples){(m_ghostPrevTime > 0f ? $", was {FormatTime(m_ghostPrevTime)}" : "")}.");
	}

	// The line under the time on the win screen ("" with no ghost).
	private string GhostWinLine()
	{
		if (m_ghostRec == null)
		{
			return "";
		}
		if (m_ghostPrevTime <= 0f)
		{
			return "Your ghost will race you next time";
		}
		int diff = Mathf.RoundToInt(Mathf.Abs(m_wonTime - m_ghostPrevTime));
		return m_ghostNewBest
			? $"You beat your ghost by {diff} s: new best!"
			: diff == 0 ? "Level with your ghost" : $"Your ghost was {diff} s faster";
	}

	private static string FormatTime(float seconds)
	{
		int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
		return $"{s / 60}:{s % 60:00}";
	}

	// ---------------------------------------------------------------- the ghost itself

	// A see-through, pale blue copy of your viking (made once per session, like the avatar).
	private void MakeGhost()
	{
		if (m_ghost != null || m_player == null || m_player.m_animator == null)
		{
			return;
		}
		GameObject ghost = Pictures.MakePicture(m_player.m_animator.gameObject, m_templates, m_layer);
		ghost.name = "SurfGhost";
		ghost.transform.SetParent(m_stage, false);
		ghost.transform.localRotation = Quaternion.identity;
		ghost.transform.localScale = Vector3.one;
		Shader? shader = Shader.Find("Sprites/Default");
		if (shader == null)
		{
			Plugin.Log.LogWarning("Ghost: no see-through shader found; ghosts are off this session.");
			Destroy(ghost);
			m_ghostBest = null;
			return;
		}
		m_ghostMat = new Material(shader) { color = new Color(s_ghostTint.r, s_ghostTint.g, s_ghostTint.b, 0f), renderQueue = 3000 };
		List<Renderer> shown = new();
		foreach (Renderer r in ghost.GetComponentsInChildren<Renderer>(true))
		{
			if (r == null)
			{
				continue;
			}
			if (!r.enabled || (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)))
			{
				// Particles and hidden gear stay off.
				r.enabled = false;
				continue;
			}
			Material[] mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
			for (int i = 0; i < mats.Length; i++)
			{
				mats[i] = m_ghostMat;
			}
			r.sharedMaterials = mats;
			r.shadowCastingMode = ShadowCastingMode.Off;
			r.receiveShadows = false;
			shown.Add(r);
		}
		m_ghostRenderers = shown.ToArray();
		m_ghostAnim = ghost.GetComponent<Animator>() ?? ghost.GetComponentInChildren<Animator>();
		m_ghostParams.Clear();
		if (m_ghostAnim != null)
		{
			foreach (AnimatorControllerParameter p in m_ghostAnim.parameters)
			{
				m_ghostParams.Add(p.nameHash);
			}
		}
		m_ghost = ghost;
		ShowGhost(0f);
		Plugin.Log.LogInfo($"Ghost: made ({m_ghostRenderers.Length} parts, animator {(m_ghostAnim != null ? "yes" : "none")}).");
	}

	private void ShowGhost(float alpha)
	{
		if (m_ghostMat != null)
		{
			m_ghostMat.color = new Color(s_ghostTint.r, s_ghostTint.g, s_ghostTint.b, alpha);
		}
		bool on = alpha > 0.005f;
		foreach (Renderer r in m_ghostRenderers)
		{
			if (r != null && r.enabled != on)
			{
				r.enabled = on;
			}
		}
	}

	// Each frame (after PlaceAvatar).
	private void PlaceGhost(float dt)
	{
		m_ghostRacing = false;
		m_ghostLead = 0;
		bool want = false;
		if (m_ghostBest != null && m_journey && (m_state == State.Running || m_state == State.Countdown) && m_journeyStage < 2)
		{
			if (m_ghost == null && m_avatar != null)
			{
				MakeGhost();
			}
			if (m_ghost != null && m_ghostBest != null)
			{
				float t = m_state == State.Running ? m_runTime : 0f;
				if (m_ghostBest.At(t, out float gx, out float gy, out float gz, out bool hidden))
				{
					m_ghostRacing = true;
					float lead = m_z - gz;
					m_ghostLead = lead > 1f ? 1 : lead < -1f ? -1 : 0;
					want = !hidden && OnOpenRoad && Mathf.Abs(gz - m_z) < 80f;
					m_ghost.transform.localPosition = new Vector3(gx, RoadY(gz) + gy, gz);
					AnimateGhost(t, gy);
				}
			}
		}
		m_ghostShow = Mathf.MoveTowards(m_ghostShow, want ? 1f : 0f, dt * 2.5f);
		if (m_ghost == null)
		{
			return;
		}
		// Fainter when it's right on top of you, so it never hides your own viking.
		float near = m_ghostRacing ? Mathf.Clamp01((Mathf.Abs(m_ghost.transform.localPosition.z - m_z) - 0.5f) / 3f) : 1f;
		ShowGhost(m_ghostShow * GhostAlpha * Mathf.Lerp(0.35f, 1f, near));
	}

	private void AnimateGhost(float t, float y)
	{
		if (m_ghostAnim == null || m_ghostBest == null)
		{
			return;
		}
		int idx = m_ghostBest.IndexAt(t);
		bool running = m_state == State.Running;
		GhostFloat("forward_speed", running ? m_runAnimSpeed : 0f);
		GhostFloat("sideway_speed", 0f);
		GhostBool("onGround", y <= 0.05f);
		GhostBool("crouching", false);
		GhostBool("inWater", false);
		if (running && y > 0.05f && m_ghostLastY <= 0.05f)
		{
			GhostTrigger("jump");
		}
		if (running && idx > m_ghostIdx && m_ghostBest.RolledBetween(m_ghostIdx, idx))
		{
			GhostTrigger("dodge");
		}
		m_ghostAnim.speed = running ? Mathf.Clamp(m_speed / 9f, 1f, 1.7f) : 1f;
		m_ghostIdx = idx;
		m_ghostLastY = y;
	}

	private void GhostFloat(string name, float value)
	{
		if (m_ghostAnim != null && m_ghostParams.Contains(Animator.StringToHash(name)))
		{
			m_ghostAnim.SetFloat(name, value);
		}
	}

	private void GhostBool(string name, bool value)
	{
		if (m_ghostAnim != null && m_ghostParams.Contains(Animator.StringToHash(name)))
		{
			m_ghostAnim.SetBool(name, value);
		}
	}

	private void GhostTrigger(string name)
	{
		if (m_ghostAnim != null && m_ghostParams.Contains(Animator.StringToHash(name)))
		{
			m_ghostAnim.SetTrigger(name);
		}
	}

	// A small diamond left of the stage name: green while you lead your ghost, red while it
	// leads, white when level. Nothing without a ghost.
	private void GhostRuneGui(Rect label, string text, GUIStyle style, float k)
	{
		if (!m_ghostRacing)
		{
			return;
		}
		float textW = style.CalcSize(new GUIContent(text)).x;
		float size = 12f * k;
		Vector2 c = new Vector2(label.xMax - textW - 14f * k, label.center.y);
		Color col = m_ghostLead > 0 ? new Color(0.45f, 1f, 0.5f, 1f) : m_ghostLead < 0 ? new Color(1f, 0.4f, 0.35f, 1f) : new Color(0.85f, 0.92f, 1f, 1f);
		Matrix4x4 m = GUI.matrix;
		GUIUtility.RotateAroundPivot(45f, c);
		float h = size * 0.5f;
		Fill(new Rect(c.x - h - 2f * k, c.y - h - 2f * k, size + 4f * k, size + 4f * k), new Color(0f, 0f, 0f, 0.6f));
		Fill(new Rect(c.x - h, c.y - h, size, size), col);
		GUI.matrix = m;
	}

	// ---------------------------------------------------------------- files

	private string? GhostDir()
	{
		if (m_player == null)
		{
			return null;
		}
		long id = Game.instance != null && Game.instance.GetPlayerProfile() != null ? Game.instance.GetPlayerProfile().GetPlayerID() : 0L;
		string name = m_player.GetPlayerName();
		foreach (char bad in Path.GetInvalidFileNameChars())
		{
			name = name.Replace(bad, '_');
		}
		return Path.Combine(Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "SnakewaySurfer"), "Ghosts"), $"{name}_{id}");
	}

	private string? GhostPath(int biome, int stars)
	{
		string? dir = GhostDir();
		return dir == null ? null : Path.Combine(dir, $"{biome}_{stars}.ghost");
	}

	private GhostTrack? LoadGhost(int biome, int stars)
	{
		string? path = GhostPath(biome, stars);
		if (path == null || !File.Exists(path))
		{
			return null;
		}
		try
		{
			using FileStream f = File.OpenRead(path);
			GhostTrack? g = GhostTrack.Read(f);
			if (g == null || g.Biome != biome || g.Stars != stars)
			{
				Plugin.Log.LogWarning($"Ghost: {path} isn't a ghost this version can read; it'll be replaced by your next win.");
				return null;
			}
			return g;
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Ghost: couldn't read {path}: {e.Message}");
			return null;
		}
	}

	private void SaveGhost(GhostTrack g)
	{
		string? path = GhostPath(g.Biome, g.Stars);
		if (path == null)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			string tmp = path + ".tmp";
			using (FileStream f = File.Create(tmp))
			{
				g.Write(f);
			}
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Move(tmp, path);
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Ghost: couldn't save {path}: {e.Message}");
		}
	}
}
