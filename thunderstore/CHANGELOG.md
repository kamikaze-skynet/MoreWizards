## 1.5.4
- **Everyone uses the host's settings.** While you're in someone's game, this mod reads the host's config instead of yours (needs `SyncHostSettings` on, the default), from the lobby on. Your own seat layout, invite counter and boss health bar display stay yours. Your config file isn't changed, and your own values are back when you leave. If the host doesn't have the mod, it stays off for you in that game.

# Changelog

## 1.5.3
- Each boss gets its own health bar over its head (with name and %) when there's more than one boss. The big bar at the top stays. Settings: `OverheadHealthBars`, `OverheadBarsForSingleBoss`, `OverheadBarHeight` in [Extra Bosses].

## 1.5.2
- Fix: with extra bosses, the boss health bar disappeared when the first boss died. The bar now moves to the next living boss, and the defeat sequence plays only for the last one. This runs on every player's game.

## 1.5.1
- Fix: Extra Bosses and host settings sync never turned on. They hooked a RunManager method the game doesn't have, so both failed to load ("Failed to apply ExtraBosses/HostSync" in the log). They now hook RunManager.OnDestroy.

## 1.5.0
- Twin bosses became **extra bosses** with host controls in a new `[Extra Bosses]` section:
  - `MinPlayers`: fewest wizards needed (1 allows solo).
  - `MaxExtraBosses`: up to 5 extra bosses.
  - `Boss2ChancePercent` to `Boss6ChancePercent`: each extra boss's chance, rolled separately.
  - `Spacing`: extra bosses alternate left and right of the normal spawn.
- Every boss must die to win, and the health bar moves to the next living boss.
- Removed `[Bosses] TwinBossChancePerExtraPlayer` and `TwinBossSpacing` (replaced by the settings above).

## 1.4.1
- More reliable player count: if the game's singleton lookup comes back empty, the mod finds the player manager itself. The log now prints `Player count: N (source)` whenever it changes.
- Boss adds use the same fallback to find the enemy spawner.

## 1.4.0
- New: twin bosses. With more wizards than `ScalingStartsAbove`, the final boss can spawn with a twin, and both must die to win. `[Bosses] TwinBossChancePerExtraPlayer` (default 15% per extra wizard), `TwinBossSpacing`.

## 1.3.0
- New: join handshake. Hosts only accept players with the same MoreWizards version; others get the game's "incompatible version" message. `[Multiplayer] RequireModForJoiners`.
- New: host settings sync. At run start the host's difficulty and shop settings are sent to everyone for that run. `[Multiplayer] SyncHostSettings`.
- New: more elites per extra wizard. `[Difficulty] ElitesPerExtraPlayer`.
- New: extra wizards count as *Stronger Enemies* curse stacks, unlocking extra wave events and elite waves. `[Difficulty] StrongerEnemiesStacksPerExtraPlayer`.
- New: bosses attack faster and bring in enemy groups with bigger parties. `[Bosses]` section.
- New: configurable shop time limit (vanilla 30s). `[Shop] TimeLimitSeconds`.
- New: lobby seats 5+ are arranged around the table or in a second row, with matching seat cameras. `[Lobby] SeatLayout`.

## 1.2.0
- Fix: capture points stalled when 5+ wizards stood on them (vanilla 0× speed above 4). 5+ now use the 4-player speed. Config: `[Objectives] FixCaptureSpeed`.
- New: capture and sacrifice zones keep growing past 4 players. Config: `[Objectives] ZonesGrowPastFour`.
- New: `[Revives]` section. `ExtraPlayerReviveShare` and `BonusRevivesPerExtraPlayer` tune how much extra wizards add to the shared revive pool (defaults = vanilla).

## 1.1.0
- New: an invite counter under the lobby's Invite Friends button ("3/8 wizards • 5 invites left"; turns orange when full).
- New: `[UI]` config section with ShowInviteCounter, InviteCounterOffsetY and InviteCounterFontSize.
- Cleaned-up source; no behaviour changes to the lobby or difficulty systems.

## 1.0.0
- Lobby cap raised to 8 (configurable 2–16) across Steam, EOS, matchmaking and host checks.
- Extra lobby seats, cameras, player list entries, colours, spawn points and herding targets.
- Difficulty scaling for extra wizards (health, damage, spawns, enemy cap).
