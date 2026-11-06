using UnityEngine;
using Random = UnityEngine.Random;

namespace SnakewaySurfer;

// 1.16.9: the Ashlands' dungeon, a charred fortress. The same halls and corners as the crypts
// (SurfCrypt.cs), in grausten, lit by lava lanterns: charred warriors, their archers and mages,
// twitchers, spawners that raise more of them, spiked beams to slide under, flametal gates that
// drop as you come (roll under), and channels of lava running across the halls (jump them).
internal sealed partial class SurfGame
{
	private static readonly CryptTheme s_charredFortress = new CryptTheme
	{
		Name = "charred fortress",
		Biome = "Ashlands",
		// The fortress's own kit (spiked beams, flametal gates, lava lanterns) lives in its rooms.
		Rooms = Room.Theme.FortressRuins | Room.Theme.AshlandRuins,
		Walls = "Piece_grausten_wall_4x2|Piece_grausten_wall_2x2|Grausten_Wall_4x2|stone_wall_4x2",
		Floors = "Piece_grausten_floor_2x2|Piece_grausten_floor_4x4|Grausten_Floor_2x2|stone_floor_2x2",
		Pillars = "Piece_grausten_pillarbeam_small|Piece_grausten_pillarbase_small|stone_pillar",
		TallPillar = "Piece_grausten_pillarbeam_medium|Piece_grausten_pillarbase_tapered|stone_pillar_high",
		WallHeight = 3f,
		Monsters = "Charred_Melee:3:2, Charred_Archer:1.6, Charred_Mage:0.6, Charred_Twitcher:1:2",
		Raised = "Charred_Melee:2, Charred_Twitcher:1",
		Pile = "charredspawner|CharredStone_Spawner|grausten_pile",
		RiseSound = "sfx_charred_mage_spawn_ground|sfx_charred_alert",
		RiseFx = "vfx_RockHit",
		Chest = "TreasureChest_charredfortress|TreasureChest_ashland_stone",
		Low = "grausten_pile|Grausten_box_low|Pickable_Ashstone",
		LowUpright = true,
		High = "Fortress_Spikey_Beam|Piece_grausten_pillarbeam_small|stone_pillar",
		Decor = new[]
		{
			"Pickable_Charredskull", "ashland_pot1_red", "ashland_pot2_green", "ashland_pot3_red", "grausten_pile", "Pickable_Ashstone",
			"Charred_Skull",
		},
		GateTorch = "lavalantern|piece_groundtorch_wood",
		GateLight = new Color(1f, 0.45f, 0.15f),
		Ambient = new Color(0.12f, 0.07f, 0.06f),
		Fog = new Color(0.04f, 0.015f, 0.01f),
		Idle = "sfx_charred_idle",
		IdleRare = "sfx_charred_twitcher_idle|sfx_charred_spawner_loop",
		SconceGap = 12f,
		Crystals = true,
		Sconce = "lavalantern|piece_groundtorch_wood",
		SconceColor = new Color(1f, 0.5f, 0.2f),
		SconceSize = new Vector3(0.7f, 1.4f, 0.7f),
		IronGates = true,
		GateBars = "flametal_gate|flametalgate|iron_grate|iron_wall_2x2",
		LavaChance = 0.6f,
	};

	// Lava across some of the halls, clear of their corners and gates.
	private void PlanLava(CryptRun c)
	{
		if (c.Theme.LavaChance <= 0f)
		{
			return;
		}
		int last = c.Legs.Count - 1;
		foreach (CryptLeg leg in c.Legs)
		{
			float from = leg.A + (leg.Index == 0 ? 18f : 11f);
			float to = leg.B - (leg.Index == last ? 18f : 13f);
			if (to - from < 1f || Random.value > c.Theme.LavaChance)
			{
				continue;
			}
			float z = Random.Range(from, to);
			if (c.Gates.Exists(g => Mathf.Abs(g.Z - z) < 9f))
			{
				continue;
			}
			c.Lava.Add(z);
		}
	}

	// A channel of glowing lava across the whole hall, edged with dark crust: jump it.
	private void BuildLava(Transform root, float z, float y)
	{
		const float width = 1.6f;
		GameObject lava = GameObject.CreatePrimitive(PrimitiveType.Cube);
		Destroy(lava.GetComponent<Collider>());
		lava.name = "CryptLava";
		lava.layer = m_layer;
		lava.transform.SetParent(root, false);
		lava.transform.localPosition = new Vector3(0f, y + 0.02f, z);
		lava.transform.localScale = new Vector3(CryptHalf * 2f, 0.05f, width);
		Renderer r = lava.GetComponent<Renderer>();
		r.sharedMaterial = LavaMaterial();
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		foreach (int side in new[] { -1, 1 })
		{
			GameObject crust = GameObject.CreatePrimitive(PrimitiveType.Cube);
			Destroy(crust.GetComponent<Collider>());
			crust.layer = m_layer;
			crust.transform.SetParent(root, false);
			crust.transform.localPosition = new Vector3(0f, y + 0.03f, z + side * (width * 0.5f + 0.12f));
			crust.transform.localScale = new Vector3(CryptHalf * 2f, 0.06f, 0.26f);
			crust.GetComponent<Renderer>().sharedMaterial = SolidTint(new Color(0.12f, 0.05f, 0.03f));
		}
		Light glow = new GameObject("CryptLavaGlow").AddComponent<Light>();
		glow.gameObject.layer = m_layer;
		glow.transform.SetParent(root, false);
		glow.transform.localPosition = new Vector3(0f, y + 0.7f, z);
		glow.type = LightType.Point;
		glow.range = 7f;
		glow.intensity = 1.8f;
		glow.color = new Color(1f, 0.42f, 0.12f);
		glow.shadows = LightShadows.None;
		glow.cullingMask = 1 << m_layer;
		glow.enabled = false;
		m_crypt!.Glows.Add((glow, z));
		for (int lane = 0; lane < 3; lane++)
		{
			GameObject spot = new GameObject("CryptLavaLane");
			spot.layer = m_layer;
			spot.transform.SetParent(m_stage, false);
			spot.transform.localPosition = new Vector3(LaneX(lane), y, z);
			m_things.Add(new Thing
			{
				Go = spot,
				Kind = Kind.Low,
				Lane = lane,
				Z = z,
				HalfLength = width * 0.5f,
				CrashText = $"You ran into the lava in a {m_crypt.Theme.Name}! (Jump the glowing channels.)",
			});
		}
	}
}
