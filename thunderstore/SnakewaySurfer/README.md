# SnakewaySurfer

Waiting at the longhouse for your friends? Open the console (F5) and type **`surfsup`**.

Your viking auto-runs down the Snakeway, a three-lane track through every biome in order:
Meadows, Black Forest, Swamp, Mountains, Plains, Mistlands, Ashlands and Deep North (which
never ends). You dodge each biome's monsters, jump over fallen logs, slide under hanging logs
and flying monsters, and grab coins. You run a little faster with every biome, and the road
rolls more and more. Monsters use their own attacks: fast ones charge at you, slow ones walk,
and archers, mages and stone throwers shoot down the track toward your lane (slide under the
shot or change lane). In the Swamp you splash through shallow water.

In the Black Forest, watch out for trolls: one beside the track swings at its lane just as you run past, and another jumps down straddling the track with
its feet in both outer lanes, so you have to slide between its legs.

To everyone else, you're just running in place at the longhouse wearing a Dverger circlet.
Friends without the mod see that too.

## Controls

| Key | Does |
| --- | --- |
| A / D or ← / → | Change lane |
| W, ↑ or Space | Jump |
| S, ↓, Ctrl or C | Slide (in the air: drop fast) |
| Space (after a crash) | Run again |
| Esc | Stop and go back to the world |

Your character can't move or use hotkeys while you play. If something hurts you in the real
world, the game stops straight away (health lost because food is wearing off doesn't count).

The minigame has its own sounds (countdown, footsteps, jumps, slides, coins, new biomes,
crashes), borrowed from the game and heard only by you.

## Rewards

At the end of each run:

- **Run XP**: by default a quarter of a Run level's worth for each biome's length of track you ran
  (fractions count). It's measured at your current Run level, so it slows down as your skill
  gets high, the same way vanilla does.
- **One random food from the furthest biome you reached**, if the run lasted at least 30 seconds
  and you haven't had food from the minigame in the last 30 minutes (real time, saved on your
  character). With full bags, it drops at your feet.

## Config

Everything is in `BepInEx/config/com.snakesquad.snakewaysurfer.cfg` (or Configuration Manager):
biome length, speeds, jump and slide, camera, the reward amounts and cooldown, and for each
biome its monsters, flyers, scenery, logs and foods. Monsters and foods are named by the game's
own text keys (`greydwarfbrute`, `carrotsoup`), so you can add any creature or item. Names the
game doesn't have are skipped and logged.

## Multiplayer

Client-only. Nothing is spawned in the world: the track is built from lifeless copies of game
objects that only your own camera can see. The running animation and the circlet go through the
game's normal syncing, so they work on a vanilla dedicated server, with or without Valheim Plus,
and for players without the mod.

## Changes

- **1.5**: creatures come from the game's own spawn tables for each biome (the right
  creatures, as common as they really are, in their real group sizes, so greydwarfs swarm);
  real sizes from each creature's collider (no more tiny abominations or stone golems);
  deathsquitos dive in from the side (slide under them); big creatures stomp in and shake the
  camera; slow attackers wind up earlier; waiting creatures may taunt; abominations burst out
  of the ground, and in the Swamp one rises straddling the track (slide between its legs);
  attack effects such as the blob's gas show and linger in the lane; lighting and fog follow
  each biome's real daytime weather, and the fog works now; `surfprobe` console command logs
  a spot's look for tuning; Allow Pause / Pause Key test settings. 1.5.x: event-only and everywhere-invasion creatures
  left out of the spawn tables. Later 1.5.x: the minigame is drawn through the game's own
  camera (its haze, bloom and colour grading; Use Game Camera off goes back to the old
  camera), and the land uses the game's real terrain shader with each biome's ground, with
  the track as a dirt path (Game Terrain, Track Style dirt/paved/stone). Creatures whose spawn entries are spread evenly
  over many biomes (mod and late-game anywhere spawns) and strays from a creature's home biome
  are left out; how common each is follows its spawn cap more than its spawn rate. Location Monsters per biome adds
  creatures that live at places rather than in the spawn tables (troll caves, crypts, villages),
  at modest weights.
- **1.4**: creatures play their real attack animations with their own weapons (the classic
  stiff troll log swing still turns up now and then); ranged monsters shoot at your lane; fast
  monsters charge, slow ones walk; the road rolls, more in each biome; hillier Swamp with water
  to run through; textured ground; Run XP lowered to 0.25 levels per biome (an untouched old
  setting of 1 is changed once). 1.4.7: the track was invisible (or glaring white); it now uses
  the floor piece's texture on a plain shader. 1.4.8: weapons in hands and shots are their real size (they took
  the bone's scale), lighter swamp water. 1.4.9: monsters are their real size (only giants are shrunk to
  fit a lane; before, everything was squeezed to 2.2 m), and they make their own alert sound
  when they notice you. They stand idle until then, then charge or walk at you
  with their own footstep sounds. 1.4.10: the land uses the game's own terrain textures
  when found; chat can't steal the keys any more; Invincible and Game Speed test settings
  (off by default). 1.4.11: short creatures (necks, blobs, ticks...) can be jumped over. 1.4.12: track no longer black.
- **1.3**: rolling hills beside the track (tallest in the Mountains); rocks and trees never
  reach over the track; the legs troll leaps in from beside the track instead of hanging in
  the air; darker track; coin sound (with fallbacks) on its own channel apart from footsteps.
- **1.2**: Black Forest troll set pieces (log slam, slide between the legs); darker lighting
  and thicker per-biome fog (Swamp and Mistlands thickest); fog strength and ambient light
  settings.
- **1.1**: sounds; food wearing off no longer counts as being hit (the game stopped after a
  few seconds); no more "Can't remove Humanoid" errors in the log.
- **1.0**: first version.
