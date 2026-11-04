using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SnakewaySurfer;

// "surfprobe": writes what makes the current spot look the way it does (weather, fog, light,
// camera effects, terrain material, nearby creatures) to the BepInEx log, so a biome's real
// look can be copied into the minigame. Read-only; changes nothing.
internal static class Probe
{
	public static void Run(Terminal terminal)
	{
		StringBuilder sb = new();
		Player p = Player.m_localPlayer;
		Vector3 pos = p != null ? p.transform.position : Vector3.zero;
		sb.AppendLine($"[surfprobe] ===== {DateTime.Now:HH:mm:ss} at {pos} =====");
		try
		{
			EnvMan env = EnvMan.instance;
			if (env != null)
			{
				object? current = Field(env, "m_currentEnv");
				sb.AppendLine($"biome {env.GetCurrentBiome()}, environment '{(current is EnvSetup es ? es.m_name : "?")}', day fraction {Field(env, "m_smoothDayFraction")}");
				Light sun = env.m_dirLight;
				if (sun != null)
				{
					sb.AppendLine($"sun: color {sun.color} intensity {sun.intensity:0.000} rotation {sun.transform.eulerAngles} shadows {sun.shadows} strength {sun.shadowStrength:0.00} bias {sun.shadowBias:0.000} mask {sun.cullingMask:X} bounce {sun.bounceIntensity:0.00}");
				}
			}
			sb.AppendLine($"fog: {RenderSettings.fog} mode {RenderSettings.fogMode} density {RenderSettings.fogDensity:0.0000} color {RenderSettings.fogColor} start {RenderSettings.fogStartDistance} end {RenderSettings.fogEndDistance}");
			sb.AppendLine($"ambient: mode {RenderSettings.ambientMode} light {RenderSettings.ambientLight} sky {RenderSettings.ambientSkyColor} equator {RenderSettings.ambientEquatorColor} ground {RenderSettings.ambientGroundColor} intensity {RenderSettings.ambientIntensity:0.00}");
			sb.AppendLine($"skybox: {(RenderSettings.skybox != null ? RenderSettings.skybox.name + " / " + RenderSettings.skybox.shader.name : "none")}, reflections {RenderSettings.defaultReflectionMode} x{RenderSettings.reflectionIntensity:0.00}");
			sb.AppendLine($"quality: level {QualitySettings.GetQualityLevel()} shadows {QualitySettings.shadows} distance {QualitySettings.shadowDistance} cascades {QualitySettings.shadowCascades} pixel lights {QualitySettings.pixelLightCount} colour space {QualitySettings.activeColorSpace}");

			Camera cam = Camera.main;
			if (cam != null)
			{
				sb.AppendLine($"main camera '{cam.name}': fov {cam.fieldOfView:0.0} near {cam.nearClipPlane} far {cam.farClipPlane} clear {cam.clearFlags} bg {cam.backgroundColor} hdr {cam.allowHDR} path {cam.actualRenderingPath} mask {cam.cullingMask:X}");
				foreach (Component c in cam.GetComponents<Component>())
				{
					sb.AppendLine($"  camera component {c.GetType().FullName}{(c is Behaviour b ? (b.enabled ? "" : " (off)") : "")}");
				}
			}
			foreach (Camera c in Camera.allCameras)
			{
				sb.AppendLine($"camera '{c.name}' depth {c.depth} mask {c.cullingMask:X} clear {c.clearFlags} enabled {c.enabled}");
			}

			// Post-processing volumes and their effect settings (by reflection, any version).
			foreach (MonoBehaviour mb in Object.FindObjectsOfType<MonoBehaviour>())
			{
				string type = mb.GetType().Name;
				if (!type.Contains("PostProcess") && !type.Contains("Volume"))
				{
					continue;
				}
				sb.AppendLine($"{type} on '{mb.gameObject.name}' enabled {mb.enabled} layer {mb.gameObject.layer}");
				object? profile = Field(mb, "sharedProfile") ?? Field(mb, "profile") ?? Prop(mb, "sharedProfile");
				if (profile != null && Field(profile, "settings") is IList settings)
				{
					foreach (object s in settings)
					{
						sb.AppendLine($"  effect {s.GetType().Name}: {Describe(s)}");
					}
				}
			}

			// The terrain material under the player.
			Heightmap hm = p != null ? Heightmap.FindHeightmap(pos) : Object.FindObjectOfType<Heightmap>();
			Material? terrain = hm != null ? (hm.GetComponent<MeshRenderer>()?.sharedMaterial ?? hm.m_material) : null;
			if (terrain != null)
			{
				sb.AppendLine($"terrain material '{terrain.name}' shader '{terrain.shader.name}'");
				Shader sh = terrain.shader;
				for (int i = 0; i < sh.GetPropertyCount(); i++)
				{
					string n = sh.GetPropertyName(i);
					ShaderPropertyType t = sh.GetPropertyType(i);
					string v = t switch
					{
						ShaderPropertyType.Color => terrain.GetColor(n).ToString(),
						ShaderPropertyType.Vector => terrain.GetVector(n).ToString(),
						ShaderPropertyType.Float or ShaderPropertyType.Range => terrain.GetFloat(n).ToString("0.###"),
						ShaderPropertyType.Texture => terrain.GetTexture(n) is Texture tex ? $"{tex.GetType().Name} '{tex.name}' {tex.width}x{tex.height} scale {terrain.GetTextureScale(n)}" : "none",
						_ => "",
					};
					sb.AppendLine($"  {n} ({t}) = {v}");
				}
			}

			// Creatures nearby: their real sizes.
			if (p != null)
			{
				foreach (Character c in Character.GetAllCharacters())
				{
					if (c == null || c == p || Vector3.Distance(c.transform.position, pos) > 60f)
					{
						continue;
					}
					CapsuleCollider cap = c.GetComponent<CapsuleCollider>();
					Bounds b = default;
					bool any = Pictures.MeshBounds(c.gameObject, out b);
					sb.AppendLine($"creature {Utils.GetPrefabName(c.gameObject)} '{c.m_name}' level {c.GetLevel()} scale {c.transform.localScale} collider h {(cap != null ? cap.height : 0f):0.00} r {(cap != null ? cap.radius : 0f):0.00} render h {(any ? b.size.y : 0f):0.00} walk {c.m_walkSpeed} run {c.m_runSpeed} anim '{c.GetComponentInChildren<Animator>()?.GetCurrentAnimatorClipInfo(0) is AnimatorClipInfo[] ci && ci.Length > 0 ? ci[0].clip.name : ""}'");
				}
			}
		}
		catch (Exception e)
		{
			sb.AppendLine("probe error: " + e);
		}
		Plugin.Log.LogInfo(sb.ToString());
		terminal?.AddString("surfprobe: written to the BepInEx log (LogOutput.log). Do it in each biome you want copied, at day if you can.");
	}

	private static string Describe(object o)
	{
		List<string> parts = new();
		foreach (FieldInfo f in o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
		{
			object? v = f.GetValue(o);
			object? inner = v != null ? (Field(v, "value") ?? Prop(v, "value")) : null;
			parts.Add($"{f.Name}={(inner ?? v)}");
		}
		return string.Join(", ", parts);
	}

	private static object? Field(object? target, string name)
	{
		FieldInfo? f = target?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		return f?.GetValue(target);
	}

	private static object? Prop(object? target, string name)
	{
		try
		{
			PropertyInfo? pi = target?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
			return pi?.GetValue(target);
		}
		catch
		{
			return null;
		}
	}
}
