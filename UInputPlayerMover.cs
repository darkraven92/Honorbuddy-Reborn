using System.ComponentModel;
using System.Runtime.InteropServices;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

namespace Honorbuddy5875.Movement;

public sealed class UInputPlayerMover : PlayerMover
{
    private readonly UInputKeyboard _keyboard = new();
    private bool _disposed;

    public override void Move(WoWMovement.MovementDirection direction)
    {
        ThrowIfDisposed();
        foreach ((WoWMovement.MovementDirection flag, ushort code) in KeyMap)
            if ((direction & flag) != 0)
                _keyboard.SetKey(code, true);
    }

    public override void MoveStop(WoWMovement.MovementDirection direction)
    {
        if (_disposed) return;
        foreach ((WoWMovement.MovementDirection flag, ushort code) in KeyMap)
            if ((direction & flag) != 0)
                _keyboard.SetKey(code, false);
    }

    public override void MoveStop()
    {
        if (_disposed) return;
        _keyboard.ReleaseAll();
    }

    public override void Dispose()
    {
        if (_disposed) return;
        try { _keyboard.ReleaseAll(); }
        finally
        {
            _keyboard.Dispose();
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UInputPlayerMover));
    }

    private static readonly (WoWMovement.MovementDirection Flag, ushort Code)[] KeyMap =
    {
        (WoWMovement.MovementDirection.Forward, 17),
        (WoWMovement.MovementDirection.Backwards, 31),
        (WoWMovement.MovementDirection.TurnLeft, 30),
        (WoWMovement.MovementDirection.TurnRight, 32),
        (WoWMovement.MovementDirection.StrafeLeft, 16),
        (WoWMovement.MovementDirection.StrafeRight, 18),
        (WoWMovement.MovementDirection.JumpAscend, 57),
        (WoWMovement.MovementDirection.Descend, 46)
    };
}

internal sealed class UInputKeyboard : IDisposable
{
    private const string DevicePath = "/dev/uinput";
    private const int O_WRONLY = 0x0001;
    private const int O_NONBLOCK = 0x0800;
    private const ushort EV_SYN = 0x00;
    private const ushort EV_KEY = 0x01;
    private const ushort SYN_REPORT = 0;
    private const nuint UI_SET_EVBIT = 0x40045564;
    private const nuint UI_SET_KEYBIT = 0x40045565;
    private const nuint UI_DEV_CREATE = 0x00005501;
    private const nuint UI_DEV_DESTROY = 0x00005502;
    private const nuint UI_DEV_SETUP = 0x405C5503;
    private const ushort BUS_USB = 0x03;

    private readonly HashSet<ushort> _held = new();
    private int _fd = -1;
    private bool _created;

    public UInputKeyboard(bool mouse = false, IEnumerable<ushort>? extraKeys = null)
    {
        _fd = Native.open(DevicePath, O_WRONLY | O_NONBLOCK);
        if (_fd < 0)
            ThrowLastError($"Could not open {DevicePath}");

        try
        {
            IoctlInt(UI_SET_EVBIT, EV_SYN, "UI_SET_EVBIT(EV_SYN)");
            IoctlInt(UI_SET_EVBIT, EV_KEY, "UI_SET_EVBIT(EV_KEY)");
            if (mouse)
            {
                IoctlInt(UI_SET_EVBIT, 2, "UI_SET_EVBIT(EV_REL)");
                IoctlInt(0x40045566, 0, "UI_SET_RELBIT(REL_X)");
                IoctlInt(0x40045566, 1, "UI_SET_RELBIT(REL_Y)");
            }
            foreach (ushort code in (mouse ? new ushort[] { 0x110, 0x111 } : new ushort[] { 1, 15, 16, 17, 18, 20, 30, 31, 32, 46, 57 })
                .Concat(extraKeys ?? []).Distinct())
                IoctlInt(UI_SET_KEYBIT, code, $"UI_SET_KEYBIT({code})");

            UInputSetup setup = new()
            {
                Id = new InputId
                {
                    Bustype = BUS_USB,
                    Vendor = 0x1209,
                    Product = 0x5875,
                    Version = 1
                },
                Name = mouse ? "Honorbuddy5875 Virtual Mouse" : "Honorbuddy5875 Virtual Keyboard",
                FfEffectsMax = 0
            };

            if (Native.ioctl_setup(_fd, UI_DEV_SETUP, ref setup) < 0)
                ThrowLastError("UI_DEV_SETUP failed");
            if (Native.ioctl_int(_fd, UI_DEV_CREATE, 0) < 0)
                ThrowLastError("UI_DEV_CREATE failed");
            _created = true;
            Thread.Sleep(200);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void SetKey(ushort code, bool down)
    {
        if (_fd < 0) throw new ObjectDisposedException(nameof(UInputKeyboard));
        bool isHeld = _held.Contains(code);
        if (down == isHeld) return;

        WriteEvent(EV_KEY, code, down ? 1 : 0);
        if (down) _held.Add(code); else _held.Remove(code);
        WriteEvent(EV_SYN, SYN_REPORT, 0);
    }

    public void ReleaseAll()
    {
        if (_fd < 0 || _held.Count == 0) return;
        foreach (ushort code in _held.ToArray())
            WriteEvent(EV_KEY, code, 0);
        WriteEvent(EV_SYN, SYN_REPORT, 0);
        _held.Clear();
    }

    public void Tap(ushort code, int holdMilliseconds = 35)
    {
        if (_fd < 0) throw new ObjectDisposedException(nameof(UInputKeyboard));
        if (holdMilliseconds < 1) holdMilliseconds = 1;
        try
        {
            SetKey(code, true);
            Thread.Sleep(holdMilliseconds);
        }
        finally { SetKey(code, false); }
    }


    private void IoctlInt(nuint request, int value, string description)
    {
        if (Native.ioctl_int(_fd, request, value) < 0)
            ThrowLastError(description);
    }

    private void WriteEvent(ushort type, ushort code, int value)
    {
        InputEvent ev = new()
        {
            TvSec = 0,
            TvUsec = 0,
            Type = type,
            Code = code,
            Value = value
        };
        int expected = Marshal.SizeOf<InputEvent>();
        nint written = Native.write_event(_fd, ref ev, (nuint)expected);
        if (written != expected)
            ThrowLastError("Writing uinput event failed");
    }

    public void Dispose()
    {
        if (_fd < 0) return;
        try
        {
            try { ReleaseAll(); } catch { }
            if (_created)
                Native.ioctl_int(_fd, UI_DEV_DESTROY, 0);
        }
        finally
        {
            Native.close(_fd);
            _fd = -1;
            _created = false;
        }
    }

    private static void ThrowLastError(string operation)
    {
        int error = Marshal.GetLastPInvokeError();
        string hint = error == 13
            ? " Permission denied. /dev/uinput normally requires root or membership in the input group."
            : string.Empty;
        throw new Win32Exception(error, $"{operation}.{hint}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputEvent
    {
        public long TvSec;
        public long TvUsec;
        public ushort Type;
        public ushort Code;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputId
    {
        public ushort Bustype;
        public ushort Vendor;
        public ushort Product;
        public ushort Version;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct UInputSetup
    {
        public InputId Id;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string Name;

        public uint FfEffectsMax;
    }

    private static class Native
    {
        [DllImport("libc", SetLastError = true)]
        public static extern int open(string pathname, int flags);

        [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
        public static extern int ioctl_int(int fd, nuint request, int value);

        [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
        public static extern int ioctl_setup(int fd, nuint request, ref UInputSetup setup);

        [DllImport("libc", SetLastError = true, EntryPoint = "write")]
        public static extern nint write_event(int fd, ref InputEvent buffer, nuint count);

        [DllImport("libc", SetLastError = true)]
        public static extern int close(int fd);
    }
}
