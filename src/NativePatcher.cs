using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

namespace MoreWizards;

/// <summary>
/// The game stores its 4-player cap as a C# const (NetworkingConstants.MaxConnectedPlayers = 4),
/// which IL2CPP bakes into machine code as an immediate value. Harmony can't touch those,
/// so we find each compiled method by name, scan its body for the exact instruction bytes
/// that hold the "4", and rewrite that immediate to MaxPlayers.
/// Each site is matched by a byte signature; if a game update changes the code, the
/// signature won't match and that patch is skipped with a warning instead of corrupting memory.
/// </summary>
internal static class NativePatcher
{
    private sealed class Site
    {
        public string Description;
        public string Assembly;
        public string Class;
        public string Nested;     // compiler-generated nested type (async state machines), or null
        public string Method;
        public int ArgCount;      // -1 = any
        public byte[] Signature;
        public int ImmOffset;     // offset of the immediate inside the signature
        public int ImmSize;       // 1 (imm8) or 4 (imm32)
        public int ScanLength;    // how many bytes of the method to search
        public byte[] Patch;      // for fix sites: bytes written at ImmOffset instead of the player count
    }

    private static byte[] Hex(string s)
    {
        s = s.Replace(" ", "");
        var b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return b;
    }

    private static readonly List<Site> Sites = new()
    {
        // --- Lobby creation (how many members the online lobby accepts) ---
        new Site { Description = "Steam lobby size (CreateLobbyAsync(4))",
            Assembly = "Networking.Steam.dll", Class = "SteamLobbyService", Nested = "<InitializeAsHost>d__3",
            Method = "MoveNext", ArgCount = 0, Signature = Hex("B9 04 00 00 00 E8"), ImmOffset = 1, ImmSize = 4, ScanLength = 0x500 },
        new Site { Description = "EOS lobby size (host)",
            Assembly = "Networking.EOS.dll", Class = "EOSLobbyService", Nested = "<InitializeAsHost>d__24",
            Method = "MoveNext", ArgCount = 0, Signature = Hex("48 C7 44 24 68 04 00 00 00"), ImmOffset = 5, ImmSize = 4, ScanLength = 0xA00 },
        new Site { Description = "EOS lobby size (CreateLobbyOptions)",
            Assembly = "Networking.EOS.dll", Class = "EOSLobbyService", Nested = null,
            Method = "CreateLobbyOptions", ArgCount = 0, Signature = Hex("48 C7 43 08 04 00 00 00"), ImmOffset = 4, ImmSize = 4, ScanLength = 0x180 },

        // --- Lobby browsing / matchmaking (free slots = 4 - members) ---
        new Site { Description = "EOS free-slot count (GetSlotsAvailable)",
            Assembly = "Networking.EOS.dll", Class = "EOSLobbySearcher", Nested = null,
            Method = "GetSlotsAvailable", ArgCount = 1, Signature = Hex("B9 04 00 00 00 2B C8"), ImmOffset = 1, ImmSize = 4, ScanLength = 0x60 },
        new Site { Description = "EOS free-slot count (GetLobbies)",
            Assembly = "Networking.EOS.dll", Class = "EOSLobbySearcher", Nested = "<GetLobbies>d__8",
            Method = "MoveNext", ArgCount = 0, Signature = Hex("BE 04 00 00 00 2B F0"), ImmOffset = 1, ImmSize = 4, ScanLength = 0xB00 },
        new Site { Description = "Matchmaking: lobby has places left",
            Assembly = "Runtime.dll", Class = "LobbyFiller", Nested = null,
            Method = "LobbyHasPlacesLeft", ArgCount = 0, Signature = Hex("83 F8 04 0F 9C C0"), ImmOffset = 2, ImmSize = 1, ScanLength = 0xA0 },
        new Site { Description = "Matchmaking: squad filling",
            Assembly = "Runtime.dll", Class = "LobbyFiller", Nested = "<TryToFillSquad>d__5",
            Method = "MoveNext", ArgCount = 0, Signature = Hex("83 F8 04 0F 8D"), ImmOffset = 2, ImmSize = 1, ScanLength = 0xC00 },

        // --- Host-side connection approval ("server full" checks) ---
        new Site { Description = "Host approval check",
            Assembly = "Runtime.dll", Class = "HostState", Nested = null,
            Method = "OnApprovalCheck", ArgCount = 2, Signature = Hex("83 F8 04 0F 8D"), ImmOffset = 2, ImmSize = 1, ScanLength = 0x340 },
        new Site { Description = "Host connection status",
            Assembly = "Runtime.dll", Class = "HostState", Nested = null,
            Method = "GetConnectionStatus", ArgCount = 1, Signature = Hex("83 F8 04 0F 8D"), ImmOffset = 2, ImmSize = 1, ScanLength = 0x1C0 },
        new Site { Description = "Server full check",
            Assembly = "Runtime.dll", Class = "NetworkStateMachine", Nested = null,
            Method = "IsServerFull", ArgCount = 0, Signature = Hex("83 F8 04 0F 9D C0"), ImmOffset = 2, ImmSize = 1, ScanLength = 0xF0 },
    };

    // Capture objective speed is a switch on players-in-zone: 1->1.0x, 2->1.5x, 3->2.0x, 4->2.5x, anything else -> 0x.
    // With 5+ wizards on the point, capture progress stops completely. The switch's last case is
    // "cmp reg,1 / je load_4p_value / xorps (=0)". Turning that je (74) into jmp (EB) makes 5+ players
    // use the 4-player speed. The switch is inlined into these methods, so each copy is patched.
    private static readonly List<Site> CaptureSpeedSites = new()
    {
        new Site { Description = "Capture speed for 5+ players (IncreaseCaptureTime)",
            Assembly = "Runtime.dll", Class = "CapturePoint", Method = "IncreaseCaptureTime", ArgCount = 1,
            Signature = Hex("41 83 F8 01 74 05 0F 57 F6"), ImmOffset = 4, Patch = Hex("EB"), ScanLength = 0xE0 },
        new Site { Description = "Capture speed for 5+ players (UpdateProgressOnOwner)",
            Assembly = "Runtime.dll", Class = "CapturePoint", Method = "UpdateProgressOnOwner", ArgCount = 0,
            Signature = Hex("83 F9 01 74 05 0F 57 F6"), ImmOffset = 3, Patch = Hex("EB"), ScanLength = 0x330 },
    };

    // Capture/sacrifice zone radius = 7 + 1.25 * Clamp01((players - 1) / 3), so it stops growing at 4 players.
    // Turning the upper clamp's jbe (76) into jmp (EB) lets the zone keep growing for each extra wizard
    // (8 players -> ~9.9 instead of 8.25). The formula is inlined in a few places; each copy is patched.
    private static readonly List<Site> ZoneRadiusSites = new()
    {
        new Site { Description = "Capture zone radius past 4 players",
            Assembly = "Runtime.dll", Class = "CapturePoint", Method = "GetRadiusForNumberOfPlayers", ArgCount = 0,
            Signature = Hex("0F 2F C1 76 08 0F 28 C1 EB 03"), ImmOffset = 3, Patch = Hex("EB"), ScanLength = 0xA0 },
        new Site { Description = "Capture zone radius past 4 players (variant change)",
            Assembly = "Runtime.dll", Class = "CapturePoint", Method = "OnActiveVariantIndexChanged", ArgCount = 2,
            Signature = Hex("0F 2F C8 76 08 0F 28 C8 EB 03"), ImmOffset = 3, Patch = Hex("EB"), ScanLength = 0x170 },
        new Site { Description = "Sacrifice zone radius past 4 players",
            Assembly = "Runtime.dll", Class = "SacrificePoint", Method = "GetRadiusForNumberOfPlayers", ArgCount = 0,
            Signature = Hex("0F 2F C1 76 08 0F 28 C1 EB 03"), ImmOffset = 3, Patch = Hex("EB"), ScanLength = 0xA0 },
        new Site { Description = "Sacrifice zone radius past 4 players (spawn)",
            Assembly = "Runtime.dll", Class = "SacrificePoint", Method = "OnNetworkSpawn", ArgCount = 0,
            Signature = Hex("0F 2F C8 76 08 0F 28 C8 EB 03"), ImmOffset = 3, Patch = Hex("EB"), ScanLength = 0x3B0 },
    };

    // Shop time limit. StoreSecondsLimit (30f) is a C# const, compiled as "mov dword [reg+0x190], 0x41F00000" (30.0f)
    // into the store's countdown field in three places. The signature stops before the float, so it can be re-patched
    // (e.g. when a client adopts the host's value).
    private static readonly List<Site> ShopTimerSites = new()
    {
        new Site { Description = "Shop time limit (SetChoosingPlayersRpc)",
            Assembly = "Runtime.dll", Class = "StoreManager", Method = "SetChoosingPlayersRpc", ArgCount = 1,
            Signature = Hex("C7 87 90 01 00 00"), ImmOffset = 6, ScanLength = 0x450 },
        new Site { Description = "Shop time limit (StartCountdown)",
            Assembly = "Runtime.dll", Class = "StoreManager", Method = "StartCountdown", ArgCount = 0,
            Signature = Hex("C7 81 90 01 00 00"), ImmOffset = 6, ScanLength = 0x50 },
        new Site { Description = "Shop time limit (selection loop)",
            Assembly = "Runtime.dll", Class = "StoreManager", Nested = "<HandleMultiplayerStoreSelection>d__82", Method = "MoveNext", ArgCount = 0,
            Signature = Hex("C7 87 90 01 00 00"), ImmOffset = 6, ScanLength = 0x150 },
    };

    private static float appliedShopSeconds = 30f;

    /// <summary>Sets the shop countdown (vanilla 30s). Safe to call again with a new value.</summary>
    public static void ApplyShopTimer(float seconds)
    {
        seconds = Math.Clamp(seconds, 5f, 300f);
        if (Math.Abs(seconds - appliedShopSeconds) < 0.01f) return;

        byte[] value = BitConverter.GetBytes(seconds);
        int ok = 0;
        foreach (var site in ShopTimerSites)
        {
            site.Patch = value;
            try { if (Apply(site, 0)) ok++; }
            catch (Exception e) { Plugin.Log.LogWarning($"[native] {site.Description}: error {e.Message}"); }
        }
        if (ok == ShopTimerSites.Count) appliedShopSeconds = seconds;
        Plugin.Log.LogInfo($"[native] Shop time limit set to {seconds:0.#}s ({ok}/{ShopTimerSites.Count} sites).");
    }

    /// <summary>Fixes for objective code that assumes at most 4 players.</summary>
    public static void ApplyObjectiveFixes(bool captureSpeed, bool zoneRadius)
    {
        var sites = new List<Site>();
        if (captureSpeed) sites.AddRange(CaptureSpeedSites);
        if (zoneRadius) sites.AddRange(ZoneRadiusSites);
        if (sites.Count == 0) return;

        int ok = 0;
        foreach (var site in sites)
        {
            try { if (Apply(site, 0)) ok++; }
            catch (Exception e) { Plugin.Log.LogWarning($"[native] {site.Description}: error {e.Message}"); }
        }
        Plugin.Log.LogInfo($"[native] {ok}/{sites.Count} objective fixes applied.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

    private const uint PAGE_EXECUTE_READWRITE = 0x40;

    public static void ApplyAll(int maxPlayers)
    {
        int ok = 0;
        foreach (var site in Sites)
        {
            try
            {
                if (Apply(site, maxPlayers)) ok++;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[native] {site.Description}: error {e.Message}");
            }
        }
        Plugin.Log.LogInfo($"[native] {ok}/{Sites.Count} player-cap sites patched to {maxPlayers}.");
        if (ok < Sites.Count)
            Plugin.Log.LogWarning("[native] Some sites were not patched - the game may have updated. Lobbies may still cap at 4 in some paths.");
    }

    private static IntPtr GetMethodPointer(Site s)
    {
        IntPtr klass = IL2CPP.GetIl2CppClass(s.Assembly, "", s.Class);
        if (klass == IntPtr.Zero) throw new Exception($"class {s.Class} not found");
        if (s.Nested != null)
        {
            klass = IL2CPP.GetIl2CppNestedType(klass, s.Nested);
            if (klass == IntPtr.Zero) throw new Exception($"nested type {s.Nested} not found");
        }
        IntPtr method = IL2CPP.il2cpp_class_get_method_from_name(klass, s.Method, s.ArgCount);
        if (method == IntPtr.Zero) throw new Exception($"method {s.Method} not found");
        // MethodInfo.methodPointer is the first field of the native MethodInfo struct.
        IntPtr code = Marshal.ReadIntPtr(method);
        if (code == IntPtr.Zero) throw new Exception("method has no native code");
        return code;
    }

    private static bool Apply(Site s, int maxPlayers)
    {
        IntPtr code = GetMethodPointer(s);
        var body = new byte[s.ScanLength];
        Marshal.Copy(code, body, 0, body.Length);

        int found = -1, count = 0;
        for (int i = 0; i <= body.Length - s.Signature.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < s.Signature.Length; j++)
                if (body[i + j] != s.Signature[j]) { match = false; break; }
            if (match) { if (found < 0) found = i; count++; }
        }

        if (count == 0)
        {
            // Already patched (e.g. plugin reloaded)? Look for the patched form before complaining.
            Plugin.Log.LogWarning($"[native] {s.Description}: signature not found, skipped.");
            return false;
        }
        if (count > 1)
        {
            Plugin.Log.LogWarning($"[native] {s.Description}: signature matched {count} times, skipped to be safe.");
            return false;
        }

        IntPtr target = code + found + s.ImmOffset;
        byte[] value = s.Patch ?? (s.ImmSize == 1
            ? new[] { (byte)Math.Clamp(maxPlayers, 1, 127) }
            : BitConverter.GetBytes(maxPlayers));

        if (!VirtualProtect(target, (UIntPtr)value.Length, PAGE_EXECUTE_READWRITE, out uint old))
            throw new Exception($"VirtualProtect failed ({Marshal.GetLastWin32Error()})");
        Marshal.Copy(value, 0, target, value.Length);
        VirtualProtect(target, (UIntPtr)value.Length, old, out _);
        FlushInstructionCache(GetCurrentProcess(), target, (UIntPtr)value.Length);

        if (s.Patch == null) Plugin.Log.LogInfo($"[native] {s.Description}: 4 -> {maxPlayers}");
        else if (s.Patch.Length == 1) Plugin.Log.LogInfo($"[native] {s.Description}: applied");
        return true;
    }
}
