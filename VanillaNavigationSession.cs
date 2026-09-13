using System.Security.Cryptography;
using Honorbuddy5875.Runtime;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

namespace Honorbuddy5875.Navigation;

// Runtime ownership stays outside QuestBot and profiles. All map reads are build-specific.
internal sealed class VanillaNavigationSession : IDisposable
{
    private const uint CurrentMap = 0xB4E378;
    private readonly ulong _module;
    private readonly int _pid;
    private readonly ulong _guid;
    private readonly VanillaMeshPathfinder _mesh;
    private readonly INavigationProvider _previous;
    internal MeshNavigator Provider { get; }
    internal int MapId => _mesh.MapId;

    internal VanillaNavigationSession(string directory)
    {
        _module = ObjectManager.ModuleBase;
        _pid = ObjectManager.WoWProcess?.Id ?? throw new InvalidOperationException("No WoW process.");
        _guid = ObjectManager.LocalGuid;
        if (_guid == 0) throw new InvalidOperationException("No local player.");
        string? executable = File.ReadLines($"/proc/{_pid}/maps")
            .Select(line => line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 6)
            .Select(parts => System.Text.RegularExpressions.Regex.Replace(parts[5], @"\\([0-7]{3})",
                m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString()))
            .FirstOrDefault(path => Path.GetFileName(path).Equals("WoW.exe", StringComparison.OrdinalIgnoreCase));
        if (executable is null) throw new InvalidOperationException("Cannot locate mapped WoW.exe.");
        using (var file = File.OpenRead(executable))
            if (Convert.ToHexString(SHA256.HashData(file)) != "B4756D38EF207C02ED651F4952BD89A70B4857B73A33413339E1B285B28D2DC7")
                throw new InvalidOperationException("Navigation requires the verified build 5875 executable.");
        // Lua IsInInstance reads the current Map.dbc index, then instance type at record+8.
        Verify(0x48A750, [0xA1, 0x78, 0xE3, 0xB4, 0x00]);
        Verify(0x48A765, [0x8B, 0x0D, 0xA8, 0xDA, 0xC0, 0x00]);
        Verify(0x495D45, [0x89, 0x35, 0x78, 0xE3, 0xB4, 0x00]);
        int map = ReadMap();
        _mesh = new VanillaMeshPathfinder(directory, map);
        _previous = Navigator.NavigationProvider;
        Provider = new MeshNavigator(_mesh, ReadPosition, () => Navigator.PlayerMover);
        try { Navigator.NavigationProvider = Provider; }
        catch { _mesh.Dispose(); throw; }
    }
    private ulong Address(uint preferred) => _module + preferred - Vanilla5875.PreferredImageBase;
    private void Verify(uint address, byte[] expected)
    {
        for (int i = 0; i < expected.Length; i++)
            if (ObjectManager.Read<byte>(Address(address) + (uint)i) != expected[i])
                throw new InvalidDataException($"Navigation code anchor differs at 0x{address:X}.");
    }
    private int ReadMap()
    {
        int map = ObjectManager.Read<int>(Address(CurrentMap));
        if (map is not (0 or 1)) throw new InvalidOperationException("Navigation supports only Vanilla world maps 0 and 1.");
        uint records = ObjectManager.Read<uint>(Address(0xC0DAA8));
        if (!ObjectManager.IsPlausiblePointer(records))
            throw new InvalidDataException("Current Map.dbc table pointer is invalid.");
        uint row = ObjectManager.Read<uint>((ulong)records + (uint)map * 4);
        if (!ObjectManager.IsPlausiblePointer(row) ||
            ObjectManager.Read<int>(row) != map || ObjectManager.Read<uint>(row + 8ul) != 0)
            throw new InvalidDataException("Current Map.dbc row is inconsistent.");
        return map;
    }
    private NavigationPosition? ReadPosition()
    {
        if (ObjectManager.ModuleBase != _module || ObjectManager.WoWProcess?.Id != _pid ||
            ObjectManager.LocalGuid != _guid || ObjectManager.Me is not { IsValid: true } me) return null;
        int map = ReadMap();
        WoWPoint point = me.Location;
        if (ReadMap() != map || ObjectManager.LocalGuid != _guid) return null;
        return new(point, map);
    }
    public void Dispose()
    {
        try
        {
            if (ReferenceEquals(Navigator.NavigationProvider, Provider)) Navigator.NavigationProvider = _previous;
            else Provider.Clear();
        }
        finally { _mesh.Dispose(); }
    }
}
