# SnakewaySurfer

Waiting at the longhouse for your friends? Open the console (F5) and type **`surfsup`**.

Your viking auto-runs down the Snakeway, a three-lane track through every biome in order:
Meadows, Black Forest, Swamp, Mountains, Plains, Mistlands, Ashlands and Deep North (which
never ends). You dodge each biome's monsters, jump over fallen logs, slide under hanging logs
and flying monsters, and grab coins. You run a little faster with every biome, and from the
Black Forest on some monsters charge at you.

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
world, the game stops straight away.

## Rewards

At the end of each run:

- **Run XP**: by default one Run level's worth for each biome's length of track you ran
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
