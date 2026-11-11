# SnakewaySurfer

A BepInEx mod for Valheim: a three-lane endless-runner minigame through every biome, started
with the console command `surfsup`. Player-facing details are in
[`thunderstore/SnakewaySurfer/README.md`](thunderstore/SnakewaySurfer/README.md). Shared
modding notes live in [SAT155/SnakeWood](https://github.com/SAT155/SnakeWood) (`docs/`).

## How it works

- **`Plugin.cs`**: config, the `surfsup` command (added in a `Terminal.InitTerminal` postfix).
- **`SurfGame.cs`**: the minigame. A stage transform is placed `Stage Height` (4000 m) above the
  player. Everything on it is a "picture" (`Pictures.MakePicture`): a copy of a vanilla prefab
  made under an inactive holder, with every MonoBehaviour, collider, rigidbody, joint and audio
  source destroyed, on render layer 30. A second camera with `cullingMask = 1 << 30` and a
  higher depth draws it; the main camera's mask is set to 0 meanwhile (restored in
  `OnDestroy`). The HUD root is hidden in `LateUpdate`. `SurfCameraFog` swaps fog and ambient
  light from `OnPreCull` to `OnPostRender` (and each `LateUpdate` through the game's camera, with
  `_SunFogColor`); `SurfHaze.cs` adds fading fog-coloured sheets across the far end of the view.
  - Track and land are cube meshes; the track borrows a build piece's material.
  - Rows of obstacles every 16–39 m: monsters (change lane), logs (jump), hanging logs or flyers
    (slide), coins. Collision is lane + distance checks, no physics.
  - The player's avatar is a picture of the local player's animator object (so it wears their
    gear), animated through `forward_speed`, `onGround`, `crouching`, `jump`, `stagger`.
- **`PlayerHooks.cs`**: the real character. `Character.UpdateWalking` postfix overwrites the
  synced `forward_speed` so others see running in place; `Player.SetupVisEquipment` postfix
  sets the helmet hash to `HelmetDverger` (synced through the ZDO, so vanilla clients see it);
  `Player.TakeInput`/`SetControls` lock the character; `Menu.Update` is skipped so Esc doesn't
  open the menu.
- **`Rewards.cs`**: Run XP via `Skills.RaiseSkill` fed one level at a time; food by
  `$item_` key, cooldown stored in `Player.m_customData["SnakewaySurfer_LastFoodUtc"]`.
- **`Biomes.cs`**: default lists per biome. Monsters and foods are looked up by localization key
  (`Character.m_name` / `m_shared.m_name`), so prefab names don't have to be known.

## Builds and releases

`.github/workflows/build.yml` builds against the Valheim dedicated server (SteamCMD app 896660)
and BepInExPack, packages `thunderstore/SnakewaySurfer`, and releases `v{MAJOR.MINOR}.{run}` with
the r2modman-ready zip attached on every default-branch push that changes the mod, or on any
manual run. `decompile.yml` prints game types for reference. Never published to Thunderstore
by CI.

## License

MIT, see [LICENSE](LICENSE).
