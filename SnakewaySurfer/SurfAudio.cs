using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SnakewaySurfer;

// The minigame's sounds: clips borrowed from vanilla sfx_* prefabs (their ZSFX clip lists),
// played as 2D sounds from our own AudioSources, so only the surfer hears them. Nothing is
// spawned in the world.
internal sealed class SurfAudio
{
	private sealed class Sound
	{
		public AudioClip[] Clips = new AudioClip[0];
		public float Volume = 1f;
		public float MinPitch = 1f;
		public float MaxPitch = 1f;
		public AudioMixerGroup? Mixer;
	}

	private static Dictionary<string, ZSFX>? s_byName;
	private readonly Dictionary<string, Sound?> m_sounds = new();
	private readonly AudioSource[] m_sources;
	private int m_next = 1;

	public SurfAudio(GameObject host)
	{
		m_sources = new AudioSource[10];
		for (int i = 0; i < m_sources.Length; i++)
		{
			AudioSource source = host.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.spatialBlend = 0f;
			source.loop = false;
			m_sources[i] = source;
		}
	}

	private static ZSFX? FindSfx(string name)
	{
		GameObject? prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
		if (prefab != null)
		{
			ZSFX sfx = prefab.GetComponentInChildren<ZSFX>(true);
			if (sfx != null)
			{
				return sfx;
			}
		}
		if (s_byName == null)
		{
			// Effect prefabs aren't all registered in ZNetScene; find any loaded ZSFX by its
			// root object's name, once.
			s_byName = new Dictionary<string, ZSFX>();
			foreach (ZSFX sfx in Resources.FindObjectsOfTypeAll<ZSFX>())
			{
				if (sfx == null)
				{
					continue;
				}
				string root = sfx.transform.root.gameObject.name;
				if (!s_byName.ContainsKey(root))
				{
					s_byName[root] = sfx;
				}
			}
		}
		return s_byName.TryGetValue(name, out ZSFX found) && found != null ? found : null;
	}

	private Sound? Load(string name)
	{
		if (m_sounds.TryGetValue(name, out Sound? cached))
		{
			return cached;
		}
		Sound? sound = null;
		ZSFX? sfx = FindSfx(name);
		if (sfx != null)
		{
			AudioSource src = sfx.GetComponent<AudioSource>();
			AudioClip[] clips = sfx.m_audioClips != null && sfx.m_audioClips.Length > 0
				? sfx.m_audioClips
				: src != null && src.clip != null ? new[] { src.clip } : new AudioClip[0];
			if (clips.Length > 0)
			{
				sound = new Sound
				{
					Clips = clips,
					Volume = Mathf.Clamp01((sfx.m_minVol + sfx.m_maxVol) * 0.5f) * (src != null ? src.volume : 1f),
					MinPitch = sfx.m_minPitch,
					MaxPitch = sfx.m_maxPitch,
					Mixer = src != null ? src.outputAudioMixerGroup : null,
				};
			}
		}
		if (sound == null)
		{
			Plugin.Log.LogWarning($"Sound {name} not found; it stays silent.");
		}
		m_sounds[name] = sound;
		return sound;
	}

	// Footsteps get their own source so they never cut off other sounds.
	public void Play(string name, float volume = 1f, float pitch = 1f, bool step = false)
	{
		if (!Plugin.Sounds.Value || string.IsNullOrEmpty(name))
		{
			return;
		}
		// "a|b|c": the first one this game version has.
		Sound? sound = null;
		foreach (string option in name.Split('|'))
		{
			sound = Load(option);
			if (sound != null)
			{
				break;
			}
		}
		if (sound == null)
		{
			return;
		}
		AudioSource source;
		if (step)
		{
			source = m_sources[0];
		}
		else
		{
			source = m_sources[m_next];
			m_next = m_next + 1 >= m_sources.Length ? 1 : m_next + 1;
		}
		source.Stop();
		source.outputAudioMixerGroup = sound.Mixer;
		source.clip = sound.Clips[Random.Range(0, sound.Clips.Length)];
		source.pitch = Random.Range(sound.MinPitch, sound.MaxPitch) * pitch;
		source.volume = Mathf.Clamp01(sound.Volume * volume * Plugin.SoundVolume.Value);
		source.Play();
	}
}
