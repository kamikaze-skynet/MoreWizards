using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace MoreWizards;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "girthy_mods.MoreWizards";
    public const string Name = "MoreWizards";
    public const string Version = "1.4.0";

    internal static new ManualLogSource Log;

    // Lobby
    internal static ConfigEntry<int> MaxPlayers;

    // Difficulty
    internal static ConfigEntry<bool> ScalingEnabled;
    internal static ConfigEntry<int> ScalingStartsAbove;
    internal static ConfigEntry<float> HealthPerExtraPlayer;
    internal static ConfigEntry<float> DamagePerExtraPlayer;
    internal static ConfigEntry<float> SpawnsPerExtraPlayer;
    internal static ConfigEntry<float> EnemyCapPerExtraPlayer;
    internal static ConfigEntry<float> FlatHealthMultiplier;
    internal static ConfigEntry<float> FlatDamageMultiplier;
    internal static ConfigEntry<float> FlatSpawnMultiplier;

    // Revives
    internal static ConfigEntry<float> ExtraPlayerReviveShare;
    internal static ConfigEntry<int> BonusRevivesPerExtraPlayer;

    // Objectives
    internal static ConfigEntry<bool> FixCaptureSpeed;
    internal static ConfigEntry<bool> ZonesGrowPastFour;

    // Enemies (elites / events)
    internal static ConfigEntry<float> ElitesPerExtraPlayer;
    internal static ConfigEntry<float> StrongerEnemiesStacksPerExtraPlayer;

    // Bosses
    internal static ConfigEntry<float> BossAttackSpeedPerExtraPlayer;
    internal static ConfigEntry<bool> BossAddsEnabled;
    internal static ConfigEntry<float> BossAddsPerExtraPlayer;
    internal static ConfigEntry<float> BossAddsIntervalSeconds;
    internal static ConfigEntry<float> TwinBossChancePerExtraPlayer;
    internal static ConfigEntry<float> TwinBossSpacing;

    // Shop
    internal static ConfigEntry<float> ShopTimeLimitSeconds;

    // Multiplayer
    internal static ConfigEntry<bool> RequireModForJoiners;
    internal static ConfigEntry<bool> SyncHostSettings;

    // Lobby layout
    internal static ConfigEntry<string> SeatLayoutMode;
    internal static ConfigEntry<float> ExtraSeatOutwardOffset;

    // UI
    internal static ConfigEntry<bool> ShowInviteCounter;
    internal static ConfigEntry<float> InviteCounterOffsetY;
    internal static ConfigEntry<float> InviteCounterFontSize;

    public override void Load()
    {
        Log = base.Log;

        MaxPlayers = Config.Bind("Lobby", "MaxPlayers", 8,
            new ConfigDescription("Maximum wizards in one lobby (vanilla is 4). Every player in the lobby needs this mod installed.",
                new AcceptableValueRange<int>(2, 16)));

        ScalingEnabled = Config.Bind("Difficulty", "Enabled", true,
            "Make the game harder as more players join. Only the host's settings matter for spawns; keep everyone on the same config for consistent enemy health.");
        ScalingStartsAbove = Config.Bind("Difficulty", "ScalingStartsAbove", 4,
            new ConfigDescription("Extra scaling kicks in for every player above this number. The base game already scales up to 4 players, so 4 means 'only add difficulty for the extra wizards'. Set to 1 to scale from the second player.",
                new AcceptableValueRange<int>(1, 16)));
        HealthPerExtraPlayer = Config.Bind("Difficulty", "HealthPerExtraPlayer", 0.25f,
            "Extra enemy health per player above the threshold, on top of the game's own +50%/player boss & elite scaling. 0.25 = +25% per extra wizard.");
        DamagePerExtraPlayer = Config.Bind("Difficulty", "DamagePerExtraPlayer", 0.10f,
            "Extra enemy attack damage per player above the threshold. 0.10 = +10% per extra wizard.");
        SpawnsPerExtraPlayer = Config.Bind("Difficulty", "SpawnsPerExtraPlayer", 0.20f,
            "Extra enemy spawn rate per player above the threshold (more enemies per wave, shorter spawn interval, higher minimum horde size). 0.20 = +20% per extra wizard.");
        EnemyCapPerExtraPlayer = Config.Bind("Difficulty", "EnemyCapPerExtraPlayer", 0.20f,
            "Raises the max number of enemies alive at once per player above the threshold. Higher values = bigger hordes but heavier on the host's CPU/network.");
        FlatHealthMultiplier = Config.Bind("Difficulty", "FlatHealthMultiplier", 1f,
            "Always-on enemy health multiplier, regardless of player count. 1.0 = vanilla.");
        FlatDamageMultiplier = Config.Bind("Difficulty", "FlatDamageMultiplier", 1f,
            "Always-on enemy damage multiplier, regardless of player count. 1.0 = vanilla.");
        FlatSpawnMultiplier = Config.Bind("Difficulty", "FlatSpawnMultiplier", 1f,
            "Always-on enemy spawn multiplier, regardless of player count. 1.0 = vanilla.");

        ExtraPlayerReviveShare = Config.Bind("Revives", "ExtraPlayerReviveShare", 1f,
            "The shared revive pool is the sum of every wizard's revives, so it already grows with the party. This sets how much each wizard above ScalingStartsAbove adds compared to a normal wizard: 1.0 = vanilla (full share), 0.5 = half, 0 = extra wizards add no revives (harder). Host's setting is used.");
        BonusRevivesPerExtraPlayer = Config.Bind("Revives", "BonusRevivesPerExtraPlayer", 0,
            new ConfigDescription("Flat revives added (or removed, if negative) to the pool for each wizard above ScalingStartsAbove. Host's setting is used.",
                new AcceptableValueRange<int>(-5, 10)));

        FixCaptureSpeed = Config.Bind("Objectives", "FixCaptureSpeed", true,
            "Vanilla capture points stop progressing when 5+ wizards stand on them (speed 0). This gives 5+ wizards the 4-player speed (2.5x) instead.");
        ZonesGrowPastFour = Config.Bind("Objectives", "ZonesGrowPastFour", true,
            "Capture and sacrifice zones get bigger for every wizard, not just up to 4 (8 wizards: radius ~9.9 instead of 8.25).");

        ElitesPerExtraPlayer = Config.Bind("Difficulty", "ElitesPerExtraPlayer", 0.25f,
            "Extra copies of each elite per wizard above the threshold. 0.25 with 8 wizards = 1 extra elite each time one spawns. Fractions are a chance. 0 = off.");
        StrongerEnemiesStacksPerExtraPlayer = Config.Bind("Difficulty", "StrongerEnemiesStacksPerExtraPlayer", 0.5f,
            "Extra wizards count as stacks of the game's 'Stronger Enemies' curse, which unlocks extra wave events and elite waves. 0.5 with 8 wizards = +2 stacks. Rounded down. 0 = off.");

        BossAttackSpeedPerExtraPlayer = Config.Bind("Bosses", "AttackSpeedPerExtraPlayer", 0.08f,
            "Bosses attack more often per wizard above the threshold. 0.08 with 8 wizards = 1.32x attack rate. 0 = off.");
        BossAddsEnabled = Config.Bind("Bosses", "AddsEnabled", true,
            "While a boss is alive, it periodically brings in a group of the current wave's enemies (only with more wizards than the threshold).");
        BossAddsPerExtraPlayer = Config.Bind("Bosses", "AddsPerExtraPlayer", 1f,
            "Enemies per add group, per wizard above the threshold. 1.0 with 8 wizards = 4 enemies per group.");
        BossAddsIntervalSeconds = Config.Bind("Bosses", "AddsIntervalSeconds", 20f,
            new ConfigDescription("Seconds between add groups during a boss fight.", new AcceptableValueRange<float>(5f, 120f)));

        TwinBossChancePerExtraPlayer = Config.Bind("Bosses", "TwinBossChancePerExtraPlayer", 0.15f,
            new ConfigDescription("Chance per wizard above the threshold that the final boss arrives with a twin. 0.15 = 15% with 5 wizards, 30% with 6, 60% with 8. Both must die to win. 0 = never, 1 = always with 5+.",
                new AcceptableValueRange<float>(0f, 1f)));
        TwinBossSpacing = Config.Bind("Bosses", "TwinBossSpacing", 9f,
            "How far to the side of the normal boss spawn the twin appears.");

        ShopTimeLimitSeconds = Config.Bind("Shop", "TimeLimitSeconds", 30f,
            new ConfigDescription("Seconds everyone gets to pick in a level-up shop before the game picks for them (vanilla 30). The game warns at 10s left. Note: the game can kick players who repeatedly run out of time, so don't set this too low.",
                new AcceptableValueRange<float>(10f, 120f)));

        RequireModForJoiners = Config.Bind("Multiplayer", "RequireModForJoiners", true,
            "When you host, only players with the same MoreWizards version can join. Others get the game's 'incompatible version' message instead of a broken session.");
        SyncHostSettings = Config.Bind("Multiplayer", "SyncHostSettings", true,
            "When you join someone, use the host's difficulty and shop settings for that run (your config file is not changed).");

        SeatLayoutMode = Config.Bind("Lobby", "SeatLayout", "Auto",
            new ConfigDescription("Where lobby seats 5+ go. Auto = Ring if the chairs surround the table, otherwise SecondRow. Ring = in the gaps around the table. SecondRow = behind the existing chairs. Line = in a row after the last chair.",
                new AcceptableValueList<string>("Auto", "Ring", "SecondRow", "Line")));
        ExtraSeatOutwardOffset = Config.Bind("Lobby", "ExtraSeatOutwardOffset", 0.5f,
            "How far extra seats sit outward from the table (Ring) or how much extra depth the second row gets (SecondRow).");

        ShowInviteCounter = Config.Bind("UI", "ShowInviteCounter", true,
            "Show 'X/8 wizards - N invites left' under the Invite Friends button in the lobby.");
        InviteCounterOffsetY = Config.Bind("UI", "InviteCounterOffsetY", -6f,
            "Vertical position of the counter relative to the bottom of the Invite Friends button. Negative = further down.");
        InviteCounterFontSize = Config.Bind("UI", "InviteCounterFontSize", 0f,
            "Font size of the counter. 0 = automatic (matches the button text).");

        Log.LogInfo($"{Name} {Version} loading. MaxPlayers={MaxPlayers.Value}");

        NativePatcher.ApplyAll(MaxPlayers.Value);
        NativePatcher.ApplyObjectiveFixes(FixCaptureSpeed.Value, ZonesGrowPastFour.Value);
        NativePatcher.ApplyShopTimer(ShopTimeLimitSeconds.Value);

        var harmony = new Harmony(Guid);
        foreach (var type in new[] { typeof(LobbyPatches), typeof(DifficultyPatches), typeof(ScalingPatches), typeof(EnemyExtras),
                                     typeof(TwinBoss), typeof(ModHandshake), typeof(HostSync), typeof(InviteCounter) })
        {
            try { harmony.PatchAll(type); }
            catch (Exception ex) { Log.LogError($"Failed to apply {type.Name}: {ex}"); }
        }

        Log.LogInfo($"{Name} ready.");
    }
}
