using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Honorbuddy5875.Runtime;

internal sealed class LinuxProcessMemory : IDisposable
{
    public int ProcessId { get; }

    public LinuxProcessMemory(int processId)
    {
        if (!Directory.Exists($"/proc/{processId}"))
            throw new ArgumentException($"PID {processId} does not exist.", nameof(processId));
        ProcessId = processId;
    }

    public unsafe void ReadBytes(ulong address, Span<byte> destination)
    {
        if (destination.IsEmpty)
            return;

        fixed (byte* destinationPointer = destination)
        {
            var local = new Iovec { Base = (nint)destinationPointer, Length = (nuint)destination.Length };
            var remote = new Iovec { Base = checked((nint)address), Length = (nuint)destination.Length };

            nint bytesRead = process_vm_readv(ProcessId, ref local, 1, ref remote, 1, 0);
            if (bytesRead == -1)
                throw new Win32Exception(Marshal.GetLastPInvokeError(),
                    $"process_vm_readv failed at 0x{address:X}");
            if ((nuint)bytesRead != (nuint)destination.Length)
                throw new IOException(
                    $"Short memory read at 0x{address:X}: expected {destination.Length}, got {bytesRead}.");
        }
    }

    public T Read<T>(ulong address) where T : unmanaged
    {
        Span<byte> buffer = stackalloc byte[Marshal.SizeOf<T>()];
        ReadBytes(address, buffer);
        return MemoryMarshal.Read<T>(buffer);
    }

    public uint ReadPointer32(ulong address) => Read<uint>(address);
    public void Dispose() { }

    [StructLayout(LayoutKind.Sequential)]
    private struct Iovec
    {
        public nint Base;
        public nuint Length;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern nint process_vm_readv(
        int pid,
        ref Iovec localIov,
        nuint localIovCount,
        ref Iovec remoteIov,
        nuint remoteIovCount,
        nuint flags);
}

internal static class LinuxProcessLocator
{
    public static int FindWineProcess(string searchText)
    {
        foreach (string directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out int pid))
                continue;
            try
            {
                string commandLine = File.ReadAllText(Path.Combine(directory, "cmdline")).Replace('\0', ' ');
                string comm = File.ReadAllText(Path.Combine(directory, "comm")).Trim();
                if (commandLine.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    comm.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    return pid;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        throw new InvalidOperationException($"Could not find a process matching '{searchText}'.");
    }

    public static ulong FindModuleBase(int pid, string moduleSearchText)
    {
        string mapsPath = $"/proc/{pid}/maps";
        var candidates = new List<(ulong Start, ulong FileOffset)>();

        foreach (string line in File.ReadLines(mapsPath))
        {
            if (!line.Contains(moduleSearchText, StringComparison.OrdinalIgnoreCase))
                continue;
            string[] columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 5)
                continue;
            string[] range = columns[0].Split('-', 2);
            if (range.Length != 2 ||
                !ulong.TryParse(range[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong start) ||
                !ulong.TryParse(columns[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong offset))
                continue;
            candidates.Add((start, offset));
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException($"No mapping containing '{moduleSearchText}' was found in {mapsPath}.");

        var zeroOffset = candidates.Where(x => x.FileOffset == 0).OrderBy(x => x.Start).FirstOrDefault();
        return zeroOffset != default ? zeroOffset.Start : candidates.Min(x => x.Start - x.FileOffset);
    }
}
