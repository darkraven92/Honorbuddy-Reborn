using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Styx.Logic.Pathing;

namespace Honorbuddy5875.Navigation;

internal interface IMeshPathfinder
{
    int MapId { get; }
    WoWPoint[] FindPath(WoWPoint from, WoWPoint to, int maxHops);
}

// Immutable map ownership. No profile logic or movement in the format adapter.
internal sealed class VanillaMeshPathfinder : IMeshPathfinder, IDisposable
{
    private readonly NavHandle _handle;
    private readonly object _gate = new();
    public int MapId { get; }
    internal int TileCount { get; }
    internal string? LastError { get; private set; }

    internal VanillaMeshPathfinder(string directory, int mapId)
    {
        if (mapId is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(mapId), "Only Vanilla world maps 0 and 1 are supported.");
        MapId = mapId;
        string parameterFile = Path.Combine(directory, $"{mapId:D3}.mmap");
        if (new FileInfo(parameterFile).Length != 28)
            throw new InvalidDataException("VMaNGOS mmap parameters must contain exactly 28 bytes.");
        byte[] parameters = File.ReadAllBytes(parameterFile);
        _handle = Native.Create(parameters, parameters.Length);
        if (_handle.IsInvalid) { _handle.Dispose(); throw new InvalidDataException("Invalid VMaNGOS mmap parameters."); }
        try
        {
            var files = Directory.GetFiles(directory, $"{mapId:D3}????.mmtile").Order(StringComparer.Ordinal).ToArray();
            if (files.Length == 0 || files.Length > 4096) throw new InvalidDataException("No mesh tiles, or too many tiles.");
            foreach (string file in files)
            {
                if (new FileInfo(file).Length is < 120 or > 64 * 1024 * 1024) throw new InvalidDataException($"Invalid tile size: {file}");
                byte[] bytes = File.ReadAllBytes(file);
                uint U(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
                if (U(0) != 0x4d4d4150 || U(4) != 7 || U(8) != 6 || U(12) != bytes.Length - 20 || U(16) > 1)
                    throw new InvalidDataException($"Incompatible mmap tile header: {file}");
                byte[] payload = bytes[20..];
                if (Native.AddTile(_handle, payload, payload.Length) != 1)
                    throw new InvalidDataException($"Detour rejected tile: {file}");
                TileCount++;
            }
        }
        catch { _handle.Dispose(); throw; }
    }

    public WoWPoint[] FindPath(WoWPoint from, WoWPoint to, int maxHops)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
            if (!MeshNavigator.Finite(from) || !MeshNavigator.Finite(to) || maxHops is < 1 or > 65535)
            { LastError = "Invalid coordinates or maxHops."; return []; }
            float[] output = new float[32768 * 3];
            int count = Native.Path(_handle, [from.X, from.Y, from.Z], [to.X, to.Y, to.Z], output, output.Length / 3, maxHops);
            LastError = count > 0 ? null : count switch
            {
                -2 => "Start or destination is outside the walkable mesh (including elevation).",
                -3 => "Incomplete corridor, missing tiles, or query capacity exceeded.",
                -4 => "Unsupported off-mesh traversal or missing ground height.",
                _ => "Detour query failed."
            };
            if (count <= 0) return [];
            var points = new WoWPoint[count];
            for (int i = 0; i < count; i++) points[i] = new(output[i * 3], output[i * 3 + 1], output[i * 3 + 2]);
            return points;
        }
    }
    public void Dispose() { lock (_gate) _handle.Dispose(); }

    private sealed class NavHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public NavHandle() : base(true) { }
        protected override bool ReleaseHandle() { Native.Destroy(handle); return true; }
    }
    private static class Native
    {
        private const string Library = "hb_navigation";
        [DllImport(Library, EntryPoint = "hb_nav_create", CallingConvention = CallingConvention.Cdecl)] internal static extern NavHandle Create(byte[] data, int size);
        [DllImport(Library, EntryPoint = "hb_nav_destroy", CallingConvention = CallingConvention.Cdecl)] internal static extern void Destroy(IntPtr context);
        [DllImport(Library, EntryPoint = "hb_nav_add_tile", CallingConvention = CallingConvention.Cdecl)] internal static extern int AddTile(NavHandle context, byte[] data, int size);
        [DllImport(Library, EntryPoint = "hb_nav_path", CallingConvention = CallingConvention.Cdecl)] internal static extern int Path(NavHandle context, float[] from, float[] to, [Out] float[] output, int capacity, int maxHops);
    }
}
