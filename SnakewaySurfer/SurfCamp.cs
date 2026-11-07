using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.80, Sam's idea: on the serpent of the deep's last hit you throw a harpoon (the game's
// Abyssal Harpoon) that trails a rope back to the ship, and she tows its floating body to the
// Plains, where it's left on the sand. You come off the ship slowly, worn out, build a
// campfire and sit: eat, drink, wait, a little more mead and more food. Then a dreamy question:
// keep dreaming (you yawn, stretch and run on into the new biome) or open your eyes (back to
// the game; the checkpoint is saved). The same break comes after every biome boss, and the camp
// grows with the star level: a fire alone; then a cooking spit and a log bench; then torches
// and a fur rug; then a cauldron and a bed roll.
internal sealed partial class SurfGame
{
	// ---------------------------------------------------------------- the harpoon tow

	private SeaFoe? m_towed;
	private LineRenderer? m_rope;
	private GameObject? m_harpoon;
	private float m_harpoonT = -1f;
	private Vector3 m_harpoonFrom;
	private const float HarpoonFlight = 0.45f;

	private void FireHarpoon(SeaFoe f)
	{
		if (!Plugin.CampBreak.Value || m_ship == null)
		{
			return;
		}
		m_towed = f;
		m_harpoonT = 0f;
		m_harpoonFrom = SternPoint();
		GameObject? spear = Pictures.Item("SpearChitin") ?? Pictures.Item("SpearBronze") ?? Pictures.Item("SpearFlint");
		if (spear != null)
		{
			m_harpoon = new GameObject("Harpoon") { layer = m_layer };
			m_harpoon.transform.SetParent(m_stage, false);
			GameObject model = Instantiate(Picture(spear), m_harpoon.transform, false);
			model.transform.localPosition = Vector3.zero;
			if (Pictures.MeshBounds(model, out Bounds b))
			{
				// Point first along its longest side.
				Quaternion turn = b.size.x >= b.size.y && b.size.x >= b.size.z ? Quaternion.Euler(0f, 90f, 0f)
					: b.size.y >= b.size.z ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
				model.transform.localRotation = turn;
				float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
				model.transform.localScale = Vector3.one * Mathf.Clamp(2.2f / Mathf.Max(0.05f, longest), 0.2f, 6f);
			}
			Pictures.SetLayer(m_harpoon, m_layer);
		}
		m_rope = MakeRope();
		// The throw.
		ItemDrop.ItemData.SharedData? shared = spear != null ? spear.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
		string? throwAnim = shared?.m_secondaryAttack?.m_attackAnimation;
		if (!string.IsNullOrEmpty(throwAnim))
		{
			SetTrigger(throwAnim!);
		}
		m_audio.Play("sfx_spear_throw|sfx_bow_fire|fx_turret_fire", 0.9f, 0.9f);
	}

	private LineRenderer MakeRope()
	{
		GameObject go = new GameObject("TowRope") { layer = m_layer };
		go.transform.SetParent(m_stage, false);
		LineRenderer rope = go.AddComponent<LineRenderer>();
		rope.useWorldSpace = false;
		rope.positionCount = 14;
		rope.startWidth = rope.endWidth = 0.08f;
		Material mat = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3000 };
		rope.sharedMaterial = mat;
		rope.startColor = rope.endColor = new Color(0.42f, 0.3f, 0.18f, 1f);
		rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		return rope;
	}

	private Vector3 SternPoint()
	{
		Vector2 s = ShipCenter() - HeadingVector() * (m_shipLength * 0.45f);
		return new Vector3(s.x, m_boatY + 1.3f, s.y);
	}

	private Vector3 TowedHead(SeaFoe f)
	{
		Vector2 h = f.P + Dir2(f.Facing) * (f.Length * 0.42f);
		return new Vector3(h.x, SeaHeight(h.x, h.y) + 0.25f, h.y);
	}

	// Each frame for the dead serpent of the deep while it's in tow (UpdateSeaBoss).
	private void TowBody(SeaFoe f, float dt)
	{
		f.T = Mathf.Min(f.T, 1.3f);
		f.Y = Mathf.MoveTowards(f.Y, -0.35f, dt);
		if (m_harpoonT >= 0f && m_harpoonT < HarpoonFlight)
		{
			// Still flying: the body drifts where it fell.
			return;
		}
		Vector2 fwd = HeadingVector();
		Vector2 stern = ShipCenter() - fwd * (m_shipLength * 0.45f);
		Vector2 want = stern - fwd * (f.Length * 0.42f + 7f);
		f.P = Vector2.Lerp(f.P, want, 1f - Mathf.Exp(-0.9f * dt));
		Vector2 toShip = stern - f.P;
		if (toShip.sqrMagnitude > 0.01f)
		{
			f.Facing = Mathf.MoveTowardsAngle(f.Facing, Angle2(toShip), 40f * dt);
		}
	}

	// Each frame at sea: the harpoon's flight and the rope.
	private void UpdateTow(float dt)
	{
		if (m_towed == null)
		{
			return;
		}
		if (m_towed.Go == null)
		{
			EndTow();
			return;
		}
		Vector3 from = m_ship != null ? SternPoint() : m_harpoonFrom;
		Vector3 head = TowedHead(m_towed);
		Vector3 end = head;
		if (m_harpoonT >= 0f && m_harpoonT < HarpoonFlight)
		{
			m_harpoonT += dt;
			float p = Mathf.Clamp01(m_harpoonT / HarpoonFlight);
			end = Vector3.Lerp(m_harpoonFrom, head, p) + Vector3.up * (Mathf.Sin(p * Mathf.PI) * 2.5f);
			if (m_harpoon != null)
			{
				Vector3 next = Vector3.Lerp(m_harpoonFrom, head, Mathf.Min(1f, p + 0.05f)) + Vector3.up * (Mathf.Sin(Mathf.Min(1f, p + 0.05f) * Mathf.PI) * 2.5f);
				m_harpoon.transform.localPosition = end;
				if ((next - end).sqrMagnitude > 1e-4f)
				{
					m_harpoon.transform.localRotation = Quaternion.LookRotation(next - end);
				}
			}
			if (m_harpoonT >= HarpoonFlight)
			{
				// Hooked.
				m_audio.Play("sfx_spear_hit|sfx_arrow_hit|sfx_wood_hit", 1f, 0.8f);
				SpawnSplash(new Vector2(head.x, head.z), 1f);
				m_shake = Mathf.Max(m_shake, 0.2f);
				m_lurchV += Random.Range(-10f, 10f);
			}
		}
		else if (m_harpoon != null)
		{
			// Stuck in its head.
			m_harpoon.transform.localPosition = head;
			Vector3 dir = head - from;
			if (dir.sqrMagnitude > 1e-4f)
			{
				m_harpoon.transform.localRotation = Quaternion.LookRotation(dir);
			}
		}
		if (m_rope != null)
		{
			float tight = m_harpoonT >= HarpoonFlight ? Mathf.Clamp01(m_boatSpeed / 4f) : 0f;
			for (int i = 0; i < m_rope.positionCount; i++)
			{
				float t = i / (float)(m_rope.positionCount - 1);
				Vector3 p = Vector3.Lerp(from, end, t);
				p.y -= Mathf.Sin(t * Mathf.PI) * Mathf.Lerp(1.1f, 0.25f, tight);
				p.y = Mathf.Max(p.y, SeaHeight(p.x, p.z) + 0.05f);
				m_rope.SetPosition(i, p);
			}
		}
	}

	private void EndTow()
	{
		m_towed = null;
		m_harpoonT = -1f;
		if (m_rope != null)
		{
			Destroy(m_rope.gameObject);
		}
		m_rope = null;
		if (m_harpoon != null)
		{
			Destroy(m_harpoon);
		}
		m_harpoon = null;
	}

	// On landfall: the body is left on the beach beside where you come ashore (out of the
	// sea's list, as scenery).
	private void BeachCarcass()
	{
		SeaFoe? f = m_towed;
		EndTow();
		if (f == null || f.Go == null)
		{
			return;
		}
		m_seaFoes.Remove(f);
		foreach (GameObject m in f.Markers)
		{
			if (m != null)
			{
				Destroy(m);
			}
		}
		f.Markers.Clear();
		if (f.Anim != null)
		{
			f.Anim.enabled = false;
		}
		float side = m_x >= 0f ? -1f : 1f;
		float x = side * (TrackHalf + 7f);
		float z = m_leapToZ + 3f;
		f.Go.transform.localPosition = new Vector3(x, HillHeight(x, z) + 0.2f, z);
		f.Go.transform.localRotation = Quaternion.Euler(0f, side < 0f ? 60f : -60f, 0f) * Quaternion.Euler(0f, 0f, 150f);
		m_things.Add(new Thing { Go = f.Go, Kind = Kind.Scenery, Z = z, HalfLength = f.Length * 0.5f, Collidable = false });
	}

	// ---------------------------------------------------------------- the camp

	private bool m_camping;
	private float m_campT;
	private float m_campZ;
	private float m_campWalk;
	private float m_campWalkEnd;
	private bool m_campCrouch;
	private float m_campView;
	private bool m_campSea;
	private Segment? m_campSeg;
	private string m_campLoot = "";
	private string m_campLootSea = "";
	private readonly List<(float at, Action act)> m_campSteps = new();
	private readonly List<GameObject> m_campProps = new();
	private Light? m_campLight;
	private GameObject? m_campHeld;
	private bool m_campMenu;
	private float m_campMenuT;
	private int m_campPick;
	private float m_campLeaveT = -1f;
	private float m_campCrackle = -1f;

	private bool Camping => m_camping;

	// A beaten boss (not in the rush): the camp once you're out of its arena. Moder's waits for
	// the far shore of the crossing.
	private void CampAfterBoss(Segment seg, Segment next, BossDef def)
	{
		if (!Plugin.CampBreak.Value || seg.Rush)
		{
			return;
		}
		string loot = BossCraftDrop(def);
		if (next.Sea)
		{
			m_campLootSea = loot;
			return;
		}
		m_campSeg = seg;
		m_campLoot = loot;
	}

	// 1.16.80 (Sam: "Some of the boss items are nice to have if they are used in crafting
	// something, except the eikthyr horns ... But not the crypt key or wishbone").
	private static string BossCraftDrop(BossDef def) => def.Prefab switch
	{
		"Dragon" => "DragonTear",
		"GoblinKing" => "YagluthDrop",
		"SeekerQueen" => "QueenDrop",
		"Fader" => "FaderDrop",
		_ => "",
	};

	// From UpdateRun: the camp starts once you're past the arena.
	private void CheckCampStart()
	{
		if (m_campSeg == null || m_camping || m_state != State.Running)
		{
			return;
		}
		if (m_campSeg.End != float.MaxValue && m_z > m_campSeg.End + 18f && !CryptCovers(m_z + 10f))
		{
			string loot = m_campLoot;
			m_campSeg = null;
			m_campLoot = "";
			StartCamp(false, loot);
		}
	}

	private void StartCamp(bool sea, string loot)
	{
		if (!Plugin.CampBreak.Value || m_player == null)
		{
			return;
		}
		m_camping = true;
		m_campSea = sea;
		m_campT = 0f;
		m_campMenu = false;
		m_campLeaveT = -1f;
		m_campPick = 0;
		m_lane = 1;
		m_x = 0f;
		m_y = 0f;
		m_vy = 0f;
		m_slide = 0f;
		m_campWalk = sea ? 1.1f : 1.6f;
		m_campWalkEnd = sea ? 4.5f : 3f;
		m_campZ = m_z + m_campWalk * m_campWalkEnd;
		ResetMoves();
		ResetSpears();
		EndCombo();
		// Nothing comes at you while you rest.
		for (int i = m_things.Count - 1; i >= 0; i--)
		{
			Thing t = m_things[i];
			if (t.Kind != Kind.Ground && t.Kind != Kind.Scenery && t.Z > m_z - 5f && t.Z < m_campZ + 70f)
			{
				DestroyThing(t);
				m_things.RemoveAt(i);
			}
		}
		m_nextRowZ = Mathf.Max(m_nextRowZ, m_campZ + 60f);
		BuildCampSteps(loot);
		Plugin.Log.LogInfo($"Camp at {m_campZ:0} m ({(sea ? "after the crossing" : "after the boss")}).");
	}

	private int CampStars => Mathf.Clamp(SegAt(m_z).Stars, 0, 3);

	private void BuildCampSteps(string loot)
	{
		m_campSteps.Clear();
		float t = m_campWalkEnd;
		int stars = CampStars;
		string food = m_campSea ? "SerpentMeatCooked|CookedLoxMeat|CookedMeat" : stars >= 2 ? "CookedLoxMeat|CookedMeat" : "CookedMeat|CookedDeerMeat";
		string mead = stars >= 2 ? "TankardOdin|Tankard" : "Tankard|TankardAnniversary";
		// Down on a knee to build the fire.
		m_campSteps.Add((t, () => { m_campWalk = 0f; m_campCrouch = true; Wield("Hammer"); }));
		for (int i = 0; i < 3; i++)
		{
			float at = t + 0.3f + i * 0.7f;
			m_campSteps.Add((at, () =>
			{
				AvatarSwing(m_wieldShared, 0);
				m_audio.Play("sfx_build_hammer_wood|sfx_wood_hit", 0.8f, Random.Range(0.9f, 1.1f));
			}));
		}
		m_campSteps.Add((t + 2.3f, () => { BuildCamp(stars); Unwield(); m_campCrouch = false; }));
		t += 3f;
		// Sit by it.
		m_campSteps.Add((t, () => { SetTrigger("emote_sit"); AvatarBool("emote_sit", true); }));
		t += 1.6f;
		Bite(ref t, food, 3);
		t += 1.4f;
		Sip(ref t, mead, 2);
		t += 2.2f;
		Bite(ref t, food, 2);
		t += 1.2f;
		Sip(ref t, mead, 3);
		t += 1.2f;
		m_campSteps.Add((t, () =>
		{
			// Rested: the lives come back, and anything the boss left.
			m_lives = Mathf.Max(m_lives, Plugin.Lives.Value);
			m_livesFlash = 1f;
			if (loot.Length > 0 && m_player != null && Rewards.GiveBossDrop(m_player, loot, 1 + CampStars) is string got)
			{
				m_lootText = (m_lootText.Length > 0 ? m_lootText + ", " : "") + got;
				m_audio.Play("sfx_coins_pile_destroyed|sfx_pickup", 0.7f, 1f);
				m_iconPulse = 1f;
			}
		}));
		t += 1.4f;
		m_campSteps.Add((t, () => { m_campMenu = true; m_campMenuT = 0f; m_audio.Play("sfx_demister_start|sfx_runestone_interact", 0.4f, 0.8f); }));
		m_campSteps.Sort((a, b) => a.at.CompareTo(b.at));
	}

	private void Bite(ref float t, string food, int bites)
	{
		string f = food;
		m_campSteps.Add((t, () => Hold(f)));
		for (int i = 0; i < bites; i++)
		{
			m_campSteps.Add((t + 0.5f + i * 0.85f, () => m_audio.Play("sfx_eat|sfx_pickup", 0.8f, Random.Range(0.9f, 1.1f))));
		}
		t += 0.5f + bites * 0.85f + 0.3f;
		m_campSteps.Add((t, () => Hold(null)));
	}

	private void Sip(ref float t, string mead, int sips)
	{
		string m = mead;
		m_campSteps.Add((t, () => Hold(m)));
		for (int i = 0; i < sips; i++)
		{
			m_campSteps.Add((t + 0.5f + i * 1f, () => m_audio.Play("sfx_drink|sfx_eat", 0.8f, Random.Range(0.9f, 1.05f))));
		}
		t += 0.5f + sips * 1f + 0.3f;
		m_campSteps.Add((t, () => Hold(null)));
	}

	// Something small in the right hand (food and tankards have no grip point to wield by).
	private void Hold(string? names)
	{
		if (m_campHeld != null)
		{
			Destroy(m_campHeld);
		}
		m_campHeld = null;
		if (names == null)
		{
			return;
		}
		GameObject? prefab = null;
		foreach (string n in names.Split('|'))
		{
			prefab = Pictures.Item(n.Trim()) ?? Pictures.Prefab(n.Trim());
			if (prefab != null)
			{
				break;
			}
		}
		Transform? hand = AvatarHand(false);
		if (prefab == null || hand == null)
		{
			return;
		}
		m_campHeld = Instantiate(Picture(prefab), hand, false);
		m_campHeld.SetActive(true);
		m_campHeld.transform.localPosition = Vector3.zero;
		m_campHeld.transform.localRotation = Quaternion.identity;
		if (Pictures.MeshBounds(m_campHeld, out Bounds b))
		{
			float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
			m_campHeld.transform.localScale *= Mathf.Clamp(0.28f / Mathf.Max(0.01f, longest), 0.05f, 4f);
		}
		Pictures.SetLayer(m_campHeld, m_layer);
	}

	// The camp, bigger with the stars.
	private void BuildCamp(int stars)
	{
		float y = RoadY(m_campZ + 1.9f);
		Vector3 fire = new Vector3(0f, y, m_campZ + 1.9f);
		CampProp(stars >= 3 ? "fire_pit_iron|fire_pit" : "fire_pit|piece_firepit", fire, 0f, 1f);
		if (stars >= 1)
		{
			CampProp(stars >= 3 ? "piece_cookingstation_iron|piece_cookingstation" : "piece_cookingstation", fire, 0f, 1f);
			CampProp("piece_logbench01|piece_bench01", new Vector3(0f, RoadY(m_campZ + 4.2f), m_campZ + 4.2f), 180f, 1f);
		}
		if (stars >= 2)
		{
			CampProp("piece_groundtorch_wood|piece_groundtorch", new Vector3(-2.6f, RoadY(m_campZ + 1f), m_campZ + 1f), 0f, 1f);
			CampProp("piece_groundtorch_wood|piece_groundtorch", new Vector3(2.6f, RoadY(m_campZ + 1f), m_campZ + 1f), 0f, 1f);
			CampProp("rug_fur|rug_wolf|rug_deer", new Vector3(0f, RoadY(m_campZ) + 0.02f, m_campZ - 0.2f), 0f, 1f);
		}
		if (stars >= 3)
		{
			CampProp("piece_cauldron|Cauldron", new Vector3(1.7f, RoadY(m_campZ + 2.4f), m_campZ + 2.4f), -30f, 1f);
			CampProp("piece_bed02|bed", new Vector3(-2.2f, RoadY(m_campZ - 1.2f), m_campZ - 1.2f), 90f, 1f);
		}
		// The fire's light and crackle.
		GameObject lightGo = new GameObject("CampFireLight") { layer = m_layer };
		lightGo.transform.SetParent(m_stage, false);
		lightGo.transform.localPosition = fire + Vector3.up * 0.8f;
		m_campLight = lightGo.AddComponent<Light>();
		m_campLight.type = LightType.Point;
		m_campLight.color = new Color(1f, 0.6f, 0.25f);
		m_campLight.range = 9f;
		m_campLight.intensity = 0f;
		m_campLight.shadows = LightShadows.None;
		m_campProps.Add(lightGo);
		m_things.Add(new Thing { Go = lightGo, Kind = Kind.Scenery, Z = fire.z, HalfLength = 1f, Collidable = false });
		if (FindFx("vfx_Place_wood_wall|vfx_Place_workbench|vfx_SawDust") is GameObject dust)
		{
			PlayFx(dust, fire + Vector3.up * 0.3f, Quaternion.identity, 2f);
		}
		if (FindFx("vfx_FireAddFuel|vfx_fireplace_add_fuel|fx_fireplace_addfuel") is GameObject flare)
		{
			PlayFx(flare, fire + Vector3.up * 0.3f, Quaternion.identity, 2f);
		}
		m_audio.Play("sfx_FireAddFuel|sfx_fire_addfuel|sfx_build_hammer_wood", 0.9f, 1f);
		m_campCrackle = 0f;
	}

	private void CampProp(string names, Vector3 at, float yaw, float scale)
	{
		foreach (string n in names.Split('|'))
		{
			GameObject? prefab = Pictures.Prefab(n.Trim());
			if (prefab == null)
			{
				continue;
			}
			GameObject go = Instantiate(Picture(prefab), m_stage, false);
			go.SetActive(true);
			go.transform.localPosition = at;
			go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
			go.transform.localScale = Vector3.one * scale;
			Pictures.SetLayer(go, m_layer);
			m_campProps.Add(go);
			m_things.Add(new Thing { Go = go, Kind = Kind.Scenery, Z = at.z, HalfLength = 1f, Collidable = false });
			return;
		}
	}

	// Each frame of the camp, in place of the run.
	private void UpdateCamp(float dt, bool canInput)
	{
		m_campT += dt;
		m_hurtTime = Mathf.Max(0f, m_hurtTime - dt);
		if (m_campWalk > 0f && m_campT < m_campWalkEnd)
		{
			m_z = Mathf.MoveTowards(m_z, m_campZ, m_campWalk * dt);
		}
		while (m_campSteps.Count > 0 && m_campSteps[0].at <= m_campT)
		{
			Action act = m_campSteps[0].act;
			m_campSteps.RemoveAt(0);
			try
			{
				act();
			}
			catch (Exception e)
			{
				Plugin.Log.LogWarning("Camp step failed: " + e.Message);
			}
		}
		if (m_campCrackle >= 0f)
		{
			m_campCrackle -= dt;
			if (m_campCrackle <= 0f)
			{
				m_campCrackle = Random.Range(2.2f, 3.2f);
				m_audio.Play("sfx_fire_loop|sfx_fireplace_loop|sfx_FireAddFuel", 0.3f, Random.Range(0.9f, 1.1f));
			}
		}
		if (m_campLight != null)
		{
			float want = 2.2f + 0.5f * Mathf.PerlinNoise(Time.time * 6f, 0.7f);
			m_campLight.intensity = Mathf.MoveTowards(m_campLight.intensity, want, dt * 3f);
		}
		if (m_campLeaveT >= 0f)
		{
			// Up, a yawn and a stretch, then off.
			m_campLeaveT += dt;
			if (m_campLeaveT > 1.7f)
			{
				EndCamp();
			}
			return;
		}
		if (m_campMenu)
		{
			m_campMenuT += dt;
			if (canInput && m_campMenuT > 0.6f)
			{
				if (KeyDown(KeyCode.LeftArrow, KeyCode.A, KeyCode.UpArrow, KeyCode.W))
				{
					m_campPick = 0;
					MenuHoverSound();
				}
				else if (KeyDown(KeyCode.RightArrow, KeyCode.D, KeyCode.DownArrow, KeyCode.S))
				{
					m_campPick = 1;
					MenuHoverSound();
				}
				else if (KeyDown(KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter, KeyCode.E))
				{
					PickCamp(m_campPick);
				}
			}
		}
	}

	private void PickCamp(int pick)
	{
		if (!m_campMenu)
		{
			return;
		}
		m_campMenu = false;
		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
		if (pick == 1)
		{
			// Open your eyes: back to the game (the checkpoint is already saved).
			m_audio.Play("sfx_raven_teleport|sfx_gui_select", 0.6f);
			Crash("You rested by the fire. The dream will be here when you come back.");
			ClearCampScene(false);
			Quit();
			return;
		}
		m_audio.Play("sfx_runestone_activate|sfx_gui_select", 0.6f, 1.1f);
		Hold(null);
		SetTrigger("emote_stop");
		AvatarBool("emote_sit", false);
		// A big stretch for the yawn.
		SetTrigger("emote_flex");
		m_campLeaveT = 0f;
	}

	private void EndCamp()
	{
		m_camping = false;
		m_campLeaveT = -1f;
		m_campSteps.Clear();
		Hold(null);
		Unwield();
		SetTrigger("emote_stop");
		AvatarBool("emote_sit", false);
		m_campCrouch = false;
		m_campWalk = 0f;
		m_campCrackle = -1f;
		// A clear start before anything comes.
		m_nextRowZ = Mathf.Max(m_nextRowZ, m_z + 45f);
		m_audio.Play(Sfx.Go, 0.7f);
	}

	// Run over / reset: the camp's things (the props are scenery and go with the track).
	private void ClearCampScene(bool all)
	{
		m_camping = false;
		m_campMenu = false;
		m_campSteps.Clear();
		m_campLeaveT = -1f;
		Hold(null);
		m_campCrackle = -1f;
		if (all)
		{
			foreach (GameObject go in m_campProps)
			{
				if (go != null)
				{
					Destroy(go);
				}
			}
			m_campProps.Clear();
			m_campLight = null;
			m_campSeg = null;
			m_campLoot = "";
			m_campLootSea = "";
			m_campView = 0f;
			EndTow();
		}
	}

	// The avatar while camping (after PlaceAvatar's run settings).
	private void CampAvatar()
	{
		if (!m_camping)
		{
			return;
		}
		bool walking = m_campWalk > 0f && m_campT < m_campWalkEnd;
		AvatarFloat("forward_speed", walking ? m_campWalk : 0f);
		AvatarBool("crouching", m_campCrouch);
		if (m_avatarAnim != null)
		{
			m_avatarAnim.speed = walking ? 0.8f : 1f;
		}
	}

	// A low view from beside the fire, eased in and out.
	private void CampCamera(float dt)
	{
		m_campView = Mathf.MoveTowards(m_campView, m_camping && m_campLeaveT < 1f ? 1f : 0f, dt / 1.6f);
		if (m_campView <= 0f || m_camera == null)
		{
			return;
		}
		float zc = m_camping ? m_campZ : m_z;
		Vector3 focus = new Vector3(0f, RoadY(zc + 0.8f) + 0.9f, zc + 0.8f);
		Vector3 eye = new Vector3(4.2f, RoadY(zc - 2.6f) + 2.4f, zc - 2.6f);
		Vector3 pos = m_stage.TransformPoint(eye);
		Quaternion rot = Quaternion.LookRotation(m_stage.TransformPoint(focus) - pos);
		float b = Mathf.SmoothStep(0f, 1f, m_campView);
		m_camera.transform.position = Vector3.Lerp(m_camera.transform.position, pos, b);
		m_camera.transform.rotation = Quaternion.Slerp(m_camera.transform.rotation, rot, b);
		m_camFinal = m_camera.transform.position;
		m_camFinalRot = m_camera.transform.rotation;
	}

	// The question, in the dream's look.
	private void CampMenuGui(float k, float w, float h)
	{
		if (!m_campMenu)
		{
			return;
		}
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
		float t = Time.unscaledTime;
		float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(m_campMenuT / 1.2f));
		float baseAlpha = s_guiAlpha;
		Fill(new Rect(0, 0, w, h), new Color(0.05f, 0.02f, 0.1f, 0.45f * open));
		DreamMotes(w, h, k, t, open);
		s_guiAlpha = baseAlpha * open * (0.85f + 0.15f * Mathf.Sin(t * 1.7f));
		Shadowed(new Rect(w * 0.1f, h * 0.18f + Mathf.Sin(t * 0.9f) * 4f * k, w * 0.8f, 80 * k), "The fire burns low. Rest a while?", m_big!);
		s_guiAlpha = baseAlpha * open;
		Shadowed(new Rect(w * 0.15f, h * 0.28f, w * 0.7f, 90 * k),
			"This dreamworld will wait for you. Come back any time and carry on from here.", m_mid!);
		string[] labels = { "Keep dreaming", "Open your eyes" };
		string[] notes = { "yawn, stretch, and run on", "wake up back in your world" };
		float bw = 340f * k;
		float bh = 120f * k;
		float gap = 60f * k;
		float x0 = w * 0.5f - bw - gap * 0.5f;
		float y0 = h * 0.45f;
		Event e = Event.current;
		for (int i = 0; i < 2; i++)
		{
			float sway = Mathf.Sin(t * 1.3f + i * 2.1f) * 5f * k;
			Rect r = new Rect(x0 + i * (bw + gap), y0 + sway, bw, bh);
			bool hover = r.Contains(e.mousePosition);
			if (hover && e.type == EventType.MouseMove && m_campPick != i)
			{
				m_campPick = i;
				MenuHoverSound();
			}
			if (hover && e.type == EventType.MouseDown && e.button == 0 && m_campMenuT > 0.6f)
			{
				e.Use();
				PickCamp(i);
				s_guiAlpha = baseAlpha;
				return;
			}
			bool on = m_campPick == i;
			float pop = on ? 1f + 0.04f * Mathf.Sin(t * 3f) : 1f;
			Rect rr = new Rect(r.center.x - r.width * pop * 0.5f, r.center.y - r.height * pop * 0.5f, r.width * pop, r.height * pop);
			Fill(rr, on ? new Color(0.35f, 0.25f, 0.5f, 0.55f) : new Color(0.12f, 0.08f, 0.18f, 0.45f));
			Shadowed(new Rect(rr.x, rr.y + 14f * k, rr.width, 50f * k), labels[i], m_big!);
			Shadowed(new Rect(rr.x, rr.y + 66f * k, rr.width, 40f * k), notes[i], m_mid!);
		}
		Shadowed(new Rect(0, y0 + bh + 40f * k, w, 40f * k), "Arrows or mouse to pick, Space or click", m_small!);
		s_guiAlpha = baseAlpha;
	}
}
