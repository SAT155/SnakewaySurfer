using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// (SAT: the mist itself was not there in the Mistlands): the game's own mist. Its
// particle system (ParticleMist, the one that drifts round you in the real Mistlands) is copied
// without its script, and puffs are set down round the runner the way the game does it: thick
// on the verges and across the track far ahead, thin over the lanes close by, so what is about
// to hit you can still be read. Only in the Mistlands, and not inside its mine.
internal sealed partial class SurfGame
{
	private ParticleSystem? m_mist;
	private bool m_mistLooked;
	private float m_mistTimer;

	private ParticleSystem? MistSystem()
	{
		if (m_mistLooked)
		{
			return m_mist;
		}
		m_mistLooked = true;
		try
		{
			ParticleMist? source = null;
			foreach (ParticleMist pm in Resources.FindObjectsOfTypeAll<ParticleMist>())
			{
				if (pm != null && pm.GetComponent<ParticleSystem>() != null)
				{
					source = pm;
					break;
				}
			}
			if (source == null)
			{
				Plugin.Log.LogInfo("No mist in this game to borrow.");
				return null;
			}
			// Copied under a switched-off parent so its script never wakes, then stripped of it.
			GameObject copy = Instantiate(source.gameObject, m_templates, false);
			foreach (MonoBehaviour mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
			{
				DestroyImmediate(mb);
			}
			copy.name = "SurfMist";
			Pictures.SetLayer(copy, m_layer);
			copy.transform.SetParent(m_stage, false);
			copy.transform.localPosition = Vector3.zero;
			copy.transform.localRotation = Quaternion.identity;
			copy.SetActive(true);
			m_mist = copy.GetComponent<ParticleSystem>();
			ParticleSystem.MainModule main = m_mist.main;
			ParticleSystem.EmissionModule emission = m_mist.emission;
			emission.enabled = false;
			m_mist.Play(true);
			Plugin.Log.LogInfo($"Mist borrowed from the game: up to {main.maxParticles} puffs, each lasting {main.startLifetime.constantMax:0.0} s, space {main.simulationSpace}.");
		}
		catch (System.Exception e)
		{
			Plugin.Log.LogWarning($"Couldn't borrow the game's mist: {e.Message}");
			m_mist = null;
		}
		return m_mist;
	}

	// (SAT, of the storm that was left behind by mistake: "Mistlands can use that thunder and
	// lightning flicker".) The Mistlands' own thunderstorm, its clouds flickering white and violet
	// with thunder after, hangs over the open Mistlands stretches: the game's 'Mistlands_thunder'
	// weather effects, carried on the camera as a boss's weather is. Off in the mine and in the
	// Queen's fight (which brings its own), and taken down when you leave or the run ends.
	private readonly List<GameObject> m_mistStorm = new();
	private bool m_mistStormLooked;

	private void UpdateMistStorm(bool want)
	{
		if (want && !m_mistStormLooked)
		{
			m_mistStormLooked = true;
			try
			{
				EnvSetup? env = EnvMan.instance != null ? EnvMan.instance.GetEnv("Mistlands_thunder") : null;
				if (env?.m_psystems != null && m_camera != null)
				{
					foreach (GameObject ps in env.m_psystems)
					{
						if (ps == null)
						{
							continue;
						}
						GameObject copy = Instantiate(ps, m_camera.transform, false);
						copy.transform.localPosition = Vector3.zero;
						Pictures.SetLayer(copy, m_layer);
						copy.SetActive(false);
						foreach (ParticleSystem p in copy.GetComponentsInChildren<ParticleSystem>(true))
						{
							ParticleSystem.CollisionModule collision = p.collision;
							collision.enabled = false;
						}
						m_mistStorm.Add(copy);
					}
				}
				Plugin.Log.LogInfo($"Mistlands storm: {m_mistStorm.Count} of the game's 'Mistlands_thunder' effects borrowed.");
			}
			catch (System.Exception e)
			{
				Plugin.Log.LogWarning($"Couldn't borrow the Mistlands storm: {e.Message}");
			}
		}
		foreach (GameObject go in m_mistStorm)
		{
			if (go != null && go.activeSelf != want)
			{
				go.SetActive(want);
			}
		}
	}

	private void ClearMistStorm()
	{
		foreach (GameObject go in m_mistStorm)
		{
			if (go != null)
			{
				Destroy(go);
			}
		}
		m_mistStorm.Clear();
		m_mistStormLooked = false;
	}

	private void UpdateMist(float dt)
	{
		bool storm = Plugin.MistStorm.Value && m_state == State.Running && m_biomes.Count > 0 && !m_atSea && m_bossEnvDef == null
			&& m_biomes[BiomeAt(m_z)].Def.Name == "Mistlands" && !InCrypt(m_z);
		if (storm || m_mistStorm.Count > 0)
		{
			UpdateMistStorm(storm);
		}
		float amount = Plugin.MistAmount.Value;
		if (amount <= 0f || m_state != State.Running || m_biomes.Count == 0 || m_atSea)
		{
			return;
		}
		m_mistTimer -= dt;
		if (m_mistTimer > 0f)
		{
			return;
		}
		m_mistTimer = 0.1f;
		// In the Mistlands, or coming up to them.
		float ahead = m_z + 60f;
		bool here = m_biomes[BiomeAt(m_z)].Def.Name == "Mistlands";
		bool soon = m_biomes[BiomeAt(ahead)].Def.Name == "Mistlands";
		if (!here && !soon)
		{
			return;
		}
		ParticleSystem? ps = MistSystem();
		if (ps == null)
		{
			return;
		}
		int puffs = Mathf.RoundToInt(10f * amount);
		ParticleSystem.EmitParams emit = default;
		for (int i = 0; i < puffs; i++)
		{
			float z = m_z + Random.Range(-10f, 90f);
			if (m_biomes[BiomeAt(z)].Def.Name != "Mistlands" || InCrypt(z))
			{
				continue;
			}
			float x = Random.Range(-45f, 45f);
			float gap = z - m_z;
			// Over the lanes: none close by, some from 35 m out.
			if (Mathf.Abs(x) < TrackHalf + 2.5f && (gap < 35f || Random.value < 0.6f))
			{
				continue;
			}
			float ground = Mathf.Abs(x) < TrackHalf + 1f ? RoadY(z) : HillHeight(x, z);
			Vector3 local = new Vector3(x, ground + Random.Range(0.5f, 4.5f), z);
			emit.position = ps.main.simulationSpace == ParticleSystemSimulationSpace.World ? m_stage.TransformPoint(local) : local;
			ps.Emit(emit, 1);
		}
	}
}
