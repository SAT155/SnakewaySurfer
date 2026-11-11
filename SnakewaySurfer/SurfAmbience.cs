using System.Collections.Generic;
using UnityEngine;

namespace SnakewaySurfer;

// (SAT: ambient and creature sounds are big cues, a Gjall's deep call in the distance
// above all): each biome sounds like itself. Its weather's own background loop plays under the
// run, the game's random background noises for that biome (birds, creaks, night sounds) come and
// go, and now and then one of its creatures calls from far off. All of it is the game's own
// sound, played only to the runner.
internal sealed partial class SurfGame
{
	private AudioSource? m_ambLoop;
	private AudioSource? m_ambOnce;
	private int m_ambBiome = -1;
	private float m_ambTarget;
	private AudioClip? m_ambNext;
	private float m_ambRandomIn = 6f;
	private float m_ambCallIn = 14f;
	private readonly List<AudioClip> m_ambClips = new();

	// Creatures heard far off, by biome: sound prefabs, the first that exists.
	private static string FarCalls(string biome) => biome switch
	{
		"Black Forest" => "sfx_greydwarf_idle|sfx_troll_idle|sfx_greydwarf_alerted",
		"Swamp" => "sfx_draugr_idle|sfx_wraith_idle|sfx_blob_idle",
		"Mountains" => "sfx_wolf_haul|sfx_wolf_howl|sfx_wolf_alerted",
		"Plains" => "sfx_lox_idle|sfx_goblin_idle|sfx_deathsquito_idle",
		"Mistlands" => "sfx_gjall_idle_vocals|sfx_gjall_idle_blowout|sfx_gjall_alerted",
		"Ashlands" => "sfx_charred_idle|sfx_morgen_idle|sfx_fallenvalkyrie_idle",
		_ => "",
	};

	private void UpdateAmbience(float dt)
	{
		float master = Plugin.AmbienceVolume.Value * Plugin.SoundVolume.Value;
		bool on = master > 0f && m_state == State.Running && m_biomes.Count > 0;
		if (!on && m_ambLoop == null)
		{
			return;
		}
		if (m_ambLoop == null)
		{
			GameObject host = new GameObject("SurfAmbience");
			host.transform.SetParent(transform, false);
			m_ambLoop = host.AddComponent<AudioSource>();
			m_ambLoop.loop = true;
			m_ambLoop.spatialBlend = 0f;
			m_ambLoop.playOnAwake = false;
			m_ambLoop.volume = 0f;
			m_ambOnce = host.AddComponent<AudioSource>();
			m_ambOnce.spatialBlend = 0f;
			m_ambOnce.playOnAwake = false;
		}
		int biome = on ? BiomeAt(m_z) : -1;
		if (biome != m_ambBiome)
		{
			m_ambBiome = biome;
			m_ambClips.Clear();
			m_ambNext = null;
			m_ambTarget = 0f;
			if (biome >= 0 && biome < SpawnTables.s_order.Length)
			{
				Heightmap.Biome b = SpawnTables.s_order[biome];
				string found = "no loop";
				try
				{
					EnvSetup? env = GameWeather.EnvOf(b);
					if (env != null && env.m_ambientLoop != null)
					{
						m_ambNext = env.m_ambientLoop;
						m_ambTarget = Mathf.Clamp01(env.m_ambientVol > 0f ? env.m_ambientVol : 0.3f);
						found = $"loop '{env.m_ambientLoop.name}' at {m_ambTarget:0.00}";
					}
					if (AudioMan.instance != null)
					{
						foreach (var a in AudioMan.instance.m_randomAmbients)
						{
							if (a == null || (a.m_biome & b) == 0)
							{
								continue;
							}
							m_ambClips.AddRange(a.m_randomAmbientClips);
							m_ambClips.AddRange(EnvMan.IsDaylight() ? a.m_randomAmbientClipsDay : a.m_randomAmbientClipsNight);
						}
					}
				}
				catch (System.Exception e)
				{
					Plugin.Log.LogWarning($"Biome ambience: {e.Message}");
				}
				m_ambClips.RemoveAll(c => c == null);
				Plugin.Log.LogInfo($"{m_biomes[biome].Def.Name} ambience: {found}, {m_ambClips.Count} random background sounds, far calls '{FarCalls(m_biomes[biome].Def.Name)}'.");
				m_ambRandomIn = Random.Range(3f, 8f);
				m_ambCallIn = Random.Range(8f, 16f);
			}
		}
		// Quieter inside a dungeon, as behind a door.
		float want = on ? m_ambTarget * master * (InCrypt(m_z) ? 0.25f : 1f) : 0f;
		if (m_ambNext != null && m_ambLoop.clip != m_ambNext)
		{
			// Fade the old biome's loop out, then start the new one.
			m_ambLoop.volume = Mathf.MoveTowards(m_ambLoop.volume, 0f, dt / 1.5f);
			if (m_ambLoop.volume <= 0.001f || !m_ambLoop.isPlaying)
			{
				m_ambLoop.clip = m_ambNext;
				m_ambLoop.Play();
			}
		}
		else
		{
			m_ambLoop.volume = Mathf.MoveTowards(m_ambLoop.volume, want, dt / 2f);
			if (!on && m_ambLoop.volume <= 0.001f && m_ambLoop.isPlaying)
			{
				m_ambLoop.Stop();
			}
		}
		if (!on || biome < 0 || InCrypt(m_z) || m_ambOnce == null)
		{
			return;
		}
		m_ambRandomIn -= dt;
		if (m_ambRandomIn <= 0f && m_ambClips.Count > 0)
		{
			m_ambRandomIn = Random.Range(5f, 12f);
			m_ambOnce.panStereo = Random.Range(-0.8f, 0.8f);
			m_ambOnce.PlayOneShot(m_ambClips[Random.Range(0, m_ambClips.Count)], Mathf.Clamp01(0.55f * master));
		}
		m_ambCallIn -= dt;
		if (m_ambCallIn <= 0f)
		{
			m_ambCallIn = Random.Range(16f, 34f);
			string calls = FarCalls(m_biomes[biome].Def.Name);
			if (calls.Length > 0 && m_bossSeg == null)
			{
				// Far off: quiet and a little low.
				m_audio.Play(calls, 0.3f * Plugin.AmbienceVolume.Value, 0.92f);
			}
		}
	}
}
