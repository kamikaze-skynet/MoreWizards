# MoreWizards

A [BepInEx](https://github.com/BepInEx/BepInEx) IL2CPP mod for **The Spell Brigade** that lets lobbies have **more than 4 wizards** (8 by default, up to 16), and scales the game so big parties still get a real fight.

**Download:** [Thunderstore — girthy_mods/MoreWizards](https://thunderstore.io/c/the-spell-brigade/p/girthy_mods/MoreWizards/) (install with r2modman or Gale).

Every player in the lobby needs the mod on the same version. The mod enforces this with a join handshake.

## Features

| Area | What it does |
|---|---|
| Lobby | Raises the 4-player cap (Steam + EOS lobbies, matchmaking, host approval, "server full"). Extra seats arranged around the table, extra seat cameras, player list entries and colours. An invite counter under the Invite Friends button. |
| Difficulty | Per wizard above `ScalingStartsAbove` (default 4): more enemy health, damage, spawns and horde size, plus extra elite copies and extra *Stronger Enemies* curse stacks, which unlock the game's own extra wave events and elite waves. |
| Bosses | Faster attacks, add groups during the fight, and a chance of **twin bosses**. Both must die to win. |
| Revives | Tune how much extra wizards add to the shared revive pool. |
| Objectives | Fixes capture points stalling with 5+ players (vanilla gives 0× speed), and lets capture/sacrifice zones keep growing past 4. |
| Multiplayer | Join handshake (same mod version required). The host's settings sync to everyone at run start. Configurable shop time limit. |

See [`thunderstore/README.md`](thunderstore/README.md) for the full player-facing description and every config option.

## Building

Requirements:
- [.NET SDK 8](https://dotnet.microsoft.com/download). The plugin targets `net6.0`, matching BepInEx 6 IL2CPP.
- The game, with **BepInExPack IL2CPP 6.0.788** installed and launched once, so `BepInEx\interop` has been generated.

```powershell
# Game-folder BepInEx install
dotnet build src\MoreWizards.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\The Spell Brigade"

# r2modman / Gale profile
dotnet build src\MoreWizards.csproj -c Release -p:BepInExDir="$env:APPDATA\r2modmanPlus-local\TheSpellBrigade\profiles\Default\BepInEx"

# Also copy the DLL into BepInEx\plugins
dotnet build src\MoreWizards.csproj -c Release -p:GameDir="..." -p:CopyToPlugins=true
```

The project references every DLL in `BepInEx\core` and `BepInEx\interop`. Nothing from the game is committed to this repo.

### Packaging for Thunderstore

```powershell
.\package.ps1 -GameDir "D:\SteamLibrary\steamapps\common\The Spell Brigade"
# -> dist\girthy_mods-MoreWizards-<version>.zip
```

Bump the version in `thunderstore/manifest.json`, `src/MoreWizards.csproj` and `Plugin.Version` together, and add a `thunderstore/CHANGELOG.md` entry.

## Project layout

```
src/
  Plugin.cs            config + startup (applies native patches, then Harmony patches)
  NativePatcher.cs     byte-signature patches of compiled code (player cap, objective fixes, shop timer)
  LobbyPatches.cs      grows 4-sized lobby/run structures (seats, cameras, colours, spawn points, UI lists, herding table)
  SeatLayout.cs        where lobby seats 5+ go (ring / second row / line) and their cameras
  InviteCounter.cs     "3/8 wizards • 5 invites left" label
  DifficultyPatches.cs enemy health/damage/spawn/cap scaling
  EnemyExtras.cs       extra elites, Stronger Enemies stacks, boss attack speed, boss adds
  TwinBoss.cs          twin final boss + "both must die" victory gating
  ScalingPatches.cs    revive pool tuning, capture speed fix (non-inlined copy)
  ModHandshake.cs      join handshake via the connection payload's Version string
  HostSync.cs          host -> clients settings sync (Netcode named message) + effective settings layer (S)
thunderstore/          manifest, icon, README, CHANGELOG shipped in the Thunderstore zip
tools/                 reverse-engineering helpers used to find and verify the patches (see below)
```

## How it works

The Spell Brigade is an IL2CPP Unity 6 game using Netcode for GameObjects (Steam via Facepunch, plus EOS). Two kinds of patch are used:

**Harmony patches** (through BepInEx's Il2CppInterop) cover normal methods: lobby objects' `Awake`, `EnemyHealthContainer.CalculateMaxHealth`, `RunManager`, `EndBossObjective`, and so on. Server-only logic checks `NetworkManager.Singleton.IsServer`. RPC methods that run twice on the host (send stage, then execute stage) check `__rpc_exec_stage`.

**Native patches** cover values that IL2CPP compiled directly into machine code, which Harmony can't reach:
- `NetworkingConstants.MaxConnectedPlayers = 4` appears as `cmp eax, 4` / `mov ecx, 4` in about 10 methods.
- The capture-point speed switch (1→1.0×, 2→1.5×, 3→2.0×, 4→2.5×, otherwise **0×**) is inlined into two methods.
- The capture/sacrifice radius clamp `Clamp01((players-1)/3)` is inlined in four places.
- The shop time limit `StoreSecondsLimit = 30f` is stored as `mov [reg+0x190], 0x41F00000` in three places.

`NativePatcher` finds each method's native code through `il2cpp_class_get_method_from_name`, scans the method body for a byte signature, and writes the new value only if that signature matches **exactly once**. If a game update changes the code, the patch is skipped with a warning; nothing else is touched.

**Join handshake.** Clients add `|MoreWizards/<version>` to the `Version` field of the game's JSON connection payload. During `HostState.OnApprovalCheck`, a prefix on `BuildVersionProvider.IsIncompatibleWith` rejects anyone without the matching marker. The joining player then gets the game's own "incompatible version" message. Everywhere else, such as matchmaking's lobby filter, the marker is stripped so modded players can still join vanilla lobbies.

**Host sync.** Enemy damage is calculated on the client that gets hit, so clients must use the host's numbers. At `RunManager.StartRunOnServerRpc` the host sends its settings with `CustomMessagingManager.SendNamedMessageToAll`. Clients apply them through the `S` settings layer for that run only; their config file isn't changed.

## Updating after a game patch

When the game updates, BepInEx regenerates `interop`. Most Harmony patches keep working. Native signatures may move, and the log will say `signature not found`. To find the new bytes:

1. Install Python 3 with `pip install capstone pefile`.
2. Dump every method address, including compiler-generated lambdas:
   ```powershell
   dotnet run --project tools\methods -p:GameDir="..." -- "<game>\GameAssembly.dll" "<game>\TheSpellBrigade_Data\il2cpp_data\Metadata\global-metadata.dat" tools\methods.tsv
   ```
3. Inspect and verify with the scripts in `tools/`. Set `SPELLBRIGADE_DIR` to your game folder first.
   - `gadis.py "Class::Method"` disassembles a method and labels its calls by name.
   - `bytes.py "Class::Method=HEXSIG"` checks that a signature is unique within the method.
   - `callers.py 0xRVA` lists every caller of a function, which tells you whether a method is inlined.
   - `rdf.py 0xNEXT+0xDISP` reads a RIP-relative float constant.
   - `scan.py` looks for 4-player-capped patterns: `(n-1)/3` lerps and small `switch` statements.
   - `dotnet run --project tools\literals -- <GameAssembly> <metadata> <RVA>...` resolves string literals, such as localization keys.
4. Update the `Signature` and `ImmOffset` values in `NativePatcher.cs`.

These outputs contain game data, so `.gitignore` keeps them out of the repo.

## Related

- [StarterSpells](https://github.com/kamikaze-skynet/StarterSpells): pick any spell as your wizard's starting spell. It works alongside MoreWizards.

## License

[MIT](LICENSE). The Spell Brigade is © Bolt Blaster Games. This is an unofficial fan mod.
