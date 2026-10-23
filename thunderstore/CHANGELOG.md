# Changelog

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
