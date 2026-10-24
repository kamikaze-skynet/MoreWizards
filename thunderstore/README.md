# MoreWizards

Lets The Spell Brigade run lobbies of **more than 4 wizards**: 8 by default, and up to 16 in the config. Everything that should grow with the party does, so bigger groups still get a real fight.

## Features

**Bigger lobbies**
- Raises the 4-player cap for Steam and Epic (EOS) lobbies, matchmaking, host approval and the "server full" check.
- Extra lobby seats arranged around the table (or in a second row), with matching seat cameras, extra player list entries and colours.
- An invite counter under the Invite Friends button: `3/8 wizards • 5 invites left`.

**Harder with more wizards** (each applies per wizard above `ScalingStartsAbove`, default 4, except extra bosses, which have their own `MinPlayers`)
- Enemies: more health, more damage, faster spawns, bigger hordes.
- **Elites:** extra copies of each elite.
- **Wave events:** extra wizards count as stacks of the game's *Stronger Enemies* curse, which unlocks the extra events and elite waves the developers made for that curse.
- **Bosses:** attack more often, and bring in groups of the current wave's enemies during the fight.
- **Extra bosses:** the final boss can arrive with up to 5 copies of itself. The host chooses the minimum number of wizards (as low as 1, for solo), how many extra bosses there can be, and the chance of each one. Each boss has its own health bar over its head, the big bar moves to the next living boss as each falls, and you only win once every boss is down.
- **Revives:** tune how much extra wizards add to the shared revive pool.

**Fixes for 5+ players**
- Capture points no longer **stall** when 5+ wizards stand on them (the base game gives 0× speed above 4).
- Capture and sacrifice zones keep growing past 4 players.
- Extra spawn points, and herding targets extended past 4.

**Multiplayer safety**
- **Join handshake:** when you host, only players with the same MoreWizards version can join. Everyone else gets the game's normal "incompatible version" message instead of a broken session. You can still join vanilla lobbies.
- **Host settings sync:** at the start of each run the host's difficulty and shop settings are sent to everyone, so all players share one difficulty. Your own config file isn't changed.
- **Shop timer:** set how long everyone gets to pick in level-up shops (vanilla 30s).

## Important

- **Every player in the lobby needs this mod, on the same version.** The handshake enforces this.
- Back up your save before using any mods: `%USERPROFILE%\AppData\LocalLow\BoltBlasterGames\TheSpellBrigade`.

## Install

**Mod manager (recommended).** Install with r2modman or Gale, then click **Start modded**. BepInEx IL2CPP installs automatically. The first launch takes a minute or two while BepInEx sets itself up.

**Manual install:**
1. Install [BepInExPack IL2CPP](https://thunderstore.io/c/the-spell-brigade/p/BepInEx/BepInExPack_IL2CPP/) into the game folder and launch the game once.
2. Copy `MoreWizards.dll` into `The Spell Brigade\BepInEx\plugins\`.

## Config

Edit `BepInEx\config\girthy_mods.MoreWizards.cfg`, or use the config editor in your mod manager. "Extra wizard" means each wizard above `ScalingStartsAbove`.

| Section | Setting | Default | What it does |
|---|---|---|---|
| Lobby | MaxPlayers | 8 | Maximum wizards per lobby (2–16) |
| Lobby | SeatLayout | Auto | Where seats 5+ go: Auto, Ring, SecondRow, Line |
| Lobby | ExtraSeatOutwardOffset | 0.5 | How far out from the table extra seats sit |
| Difficulty | Enabled | true | Turn player-count scaling on or off |
| Difficulty | ScalingStartsAbove | 4 | Scaling applies to each wizard above this number |
| Difficulty | HealthPerExtraPlayer | 0.25 | +25% enemy health per extra wizard |
| Difficulty | DamagePerExtraPlayer | 0.10 | +10% enemy damage per extra wizard |
| Difficulty | SpawnsPerExtraPlayer | 0.20 | +20% spawn rate per extra wizard |
| Difficulty | EnemyCapPerExtraPlayer | 0.20 | +20% max enemies alive at once per extra wizard |
| Difficulty | ElitesPerExtraPlayer | 0.25 | Extra elite copies per extra wizard (fractions are a chance) |
| Difficulty | StrongerEnemiesStacksPerExtraPlayer | 0.5 | Stronger Enemies curse stacks per extra wizard (rounded down) |
| Difficulty | Flat*Multiplier | 1.0 | Always-on health, damage and spawn multipliers |
| Bosses | AttackSpeedPerExtraPlayer | 0.08 | +8% boss attack rate per extra wizard |
| Bosses | AddsEnabled | true | Bosses bring in enemy groups during the fight |
| Bosses | AddsPerExtraPlayer | 1.0 | Enemies per group, per extra wizard |
| Bosses | AddsIntervalSeconds | 20 | Seconds between groups |
| Extra Bosses | Enabled | true | Extra bosses on or off |
| Extra Bosses | MinPlayers | 5 | Fewest wizards needed for extra bosses (1 = solo too) |
| Extra Bosses | MaxExtraBosses | 1 | How many extra bosses there can be (1–5) |
| Extra Bosses | Boss2ChancePercent … Boss6ChancePercent | 50, 25, 10, 5, 5 | Chance of each extra boss, rolled separately |
| Extra Bosses | Spacing | 9 | How far apart the bosses spawn |
| Extra Bosses | OverheadHealthBars | true | Health bar over each boss's head when there's more than one boss |
| Extra Bosses | OverheadBarsForSingleBoss | false | Also show it when there's only one boss |
| Extra Bosses | OverheadBarHeight | 0.5 | Extra height of the overhead bars above the head |
| Revives | ExtraPlayerReviveShare | 1.0 | Revives each extra wizard adds vs a normal wizard (1 = vanilla, 0 = none) |
| Revives | BonusRevivesPerExtraPlayer | 0 | Flat revives added (or removed) per extra wizard |
| Objectives | FixCaptureSpeed | true | 5+ wizards capture at the 4-player speed instead of stalling |
| Objectives | ZonesGrowPastFour | true | Capture and sacrifice zones keep growing per wizard |
| Shop | TimeLimitSeconds | 30 | Seconds to pick in level-up shops before the game picks for you |
| Multiplayer | RequireModForJoiners | true | Only players with the same MoreWizards version can join your lobby |
| Multiplayer | SyncHostSettings | true | Use the host's difficulty and shop settings when you join |
| UI | ShowInviteCounter | true | Show the invite counter in the lobby |
| UI | InviteCounterOffsetY | -6 | Move the counter up or down |
| UI | InviteCounterFontSize | 0 | Counter text size (0 = automatic) |

Example: 8 wizards on the defaults means 4 extra wizards, so enemies get **2× health**, **1.4× damage** and **1.8× spawns**. Each elite comes with 1 extra copy, there are +2 Stronger Enemies stacks, and bosses attack 1.32× as often with 4 enemies joining every 20s, and there's a 50% chance of a second boss. All of this stacks on top of the game's own scaling.

## What already scales with player count

The base game already scales these with no cap, so they work for 5+ wizards without help:
- Revive pool (sum of every wizard's revives)
- Party XP needed per level (grows with party size)
- Boss and elite health (+50% per extra wizard)
- Objective targets: collectables, hydra healers, pushables, clean-zone size

## Known limits

- Extra bosses: only one boss health bar is shown at a time. It follows the original boss, then moves to each extra boss in turn.

- The shop timer is shared with the game's "kick players who keep running out of time" rule, so very low values can get people kicked.
- A game update may move the patched code. If that happens, the log (`BepInEx\LogOutput.log`) reports `signature not found` and that one feature is skipped until the mod is updated.

### Extra boss examples

| Want | Settings |
|---|---|
| Always 2 bosses, even solo | `MinPlayers = 1`, `MaxExtraBosses = 1`, `Boss2ChancePercent = 100` |
| Big party chaos | `MinPlayers = 6`, `MaxExtraBosses = 3`, chances `100, 50, 25` |
| Rare surprise | `MinPlayers = 1`, `MaxExtraBosses = 1`, `Boss2ChancePercent = 10` |

## Multiplayer settings

In multiplayer everyone plays with the **host's** settings: the host's config is sent to every player from the lobby on, and the mod reads it instead of yours while you're connected. Your own seat layout, invite counter and boss health bar display stay yours. Your config file is never changed. If the host doesn't have this mod, it stays off for you in that game.
