using System.Diagnostics;
using Honorbuddy5875.Runtime;
using Styx.WoWInternals.WoWObjects;

namespace Styx.WoWInternals;

public delegate void ObjectListUpdateFinishedDelegate(object context);

/// <summary>
/// Build-5875 implementation of the public ObjectManager surface recovered from Honorbuddy 2.0.0.5999.
/// This compatibility phase is read-only and intentionally does not implement BlueMagic/EndScene/injection.
/// </summary>
public class ObjectManager
{
    private static readonly Dictionary<ulong, WoWObject> Objects = new();
    private static readonly Dictionary<ulong, WoWObject> StaleObjects = new();
    private static readonly object UpdateLock = new();
    private static LinuxProcessMemory? _memory;
    private static ulong _moduleBase;
    private static Process? _wowProcess;
    private static LocalPlayer? _me;

    private ObjectManager() { }

    public static event ObjectListUpdateFinishedDelegate? OnObjectListUpdateFinished;

    public static Process? WoWProcess => _wowProcess;
    public static LocalPlayer? Me => _me;
    public static ulong ModuleBase => _moduleBase;
    public static uint ObjectManagerAddress { get; private set; }
    public static ulong LocalGuid { get; private set; }
    public static bool IsInGame => _memory is not null && LocalGuid != 0 && _me is { IsValid: true };
    public static List<WoWObject> ObjectList => Objects.Values.ToList();

    // Compatibility bootstrap used only by the clean-room 5875 port.
    public static void Initialize5875(int? processId = null)
    {
        Shutdown5875();
        int pid = processId ?? LinuxProcessLocator.FindWineProcess("WoW.exe");
        _wowProcess = Process.GetProcessById(pid);
        _moduleBase = LinuxProcessLocator.FindModuleBase(pid, "WoW.exe");
        _memory = new LinuxProcessMemory(pid);
        Update();
    }

    public static void Shutdown5875()
    {
        lock (UpdateLock)
        {
            foreach (WoWObject obj in Objects.Values)
                obj.InvalidateFromManager();
            Objects.Clear();
            StaleObjects.Clear();
            _me = null;
            LocalGuid = 0;
            ObjectManagerAddress = 0;
            _moduleBase = 0;
            _memory?.Dispose();
            _memory = null;
            _wowProcess?.Dispose();
            _wowProcess = null;
        }
    }

    public static void Update()
    {
        lock (UpdateLock)
        {
            LinuxProcessMemory memory = Memory;
            uint objectManager = memory.ReadPointer32(_moduleBase + Vanilla5875.ObjectManagerPointerRva);
            EnsurePointer(objectManager, "ObjectManager");
            ObjectManagerAddress = objectManager;

            LocalGuid = memory.Read<ulong>((ulong)objectManager + Vanilla5875.LocalGuidFromObjectManager);
            if (LocalGuid == 0)
                throw new InvalidDataException("Local GUID is zero. Log into a character before updating ObjectManager.");

            foreach (WoWObject obj in Objects.Values)
                obj.BaseAddressFromManager = 0;

            uint current = memory.ReadPointer32((ulong)objectManager + Vanilla5875.FirstObjectFromObjectManager);
            var visited = new HashSet<uint>();
            int count = 0;

            while (IsPlausiblePointer(current) &&
                   count < Vanilla5875.MaximumObjectCount &&
                   visited.Add(current))
            {
                ulong guid = memory.Read<ulong>((ulong)current + Vanilla5875.GuidFromObject);
                var type = (WoWObjectType)memory.Read<uint>((ulong)current + Vanilla5875.ObjectTypeFromObject);
                if ((uint)type > (uint)WoWObjectType.Corpse)
                    throw new InvalidDataException($"Object 0x{current:X8} has invalid type {(uint)type}.");

                if (guid != 0)
                {
                    if (Objects.TryGetValue(guid, out WoWObject? existing))
                    {
                        existing.BaseAddressFromManager = current;
                    }
                    else
                    {
                        WoWObject created = CreateObject(current, guid, type);
                        Objects.Add(guid, created);
                        if (created is LocalPlayer local)
                            _me = local;
                    }
                }

                uint next = memory.ReadPointer32((ulong)current + Vanilla5875.NextObjectFromObject);
                count++;
                if (next == current)
                    break;
                current = next;
            }

            StaleObjects.Clear();
            foreach ((ulong guid, WoWObject obj) in Objects)
            {
                if (obj.BaseAddress == 0)
                    StaleObjects.Add(guid, obj);
            }

            foreach ((ulong guid, WoWObject obj) in StaleObjects)
            {
                Objects.Remove(guid);
                obj.InvalidateFromManager();
                if (ReferenceEquals(obj, _me))
                    _me = null;
            }

            if (_me is not null)
                OnObjectListUpdateFinished?.Invoke(_me);
        }
    }

    public static List<T> GetObjectsOfType<T>() where T : WoWObject
        => GetObjectsOfType<T>(false, false);

    public static List<T> GetObjectsOfType<T>(bool allowInheritance) where T : WoWObject
        => GetObjectsOfType<T>(allowInheritance, false);

    public static List<T> GetObjectsOfType<T>(bool allowInheritance, bool includeMeIfFound) where T : WoWObject
    {
        Type requested = typeof(T);
        var result = new List<T>();
        foreach (WoWObject obj in Objects.Values)
        {
            Type actual = obj.GetType();
            bool typeMatch = actual == requested || (allowInheritance && actual.IsSubclassOf(requested));
            if (!typeMatch)
                continue;
            if (!includeMeIfFound && ReferenceEquals(obj, _me))
                continue;
            if (obj is T typed)
                result.Add(typed);
        }
        return result;
    }

    public static T? GetAnyObjectByGuid<T>(ulong guid) where T : WoWObject
    {
        if (guid == 0 || !Objects.TryGetValue(guid, out WoWObject? obj))
            return null;
        return obj as T;
    }

    public static T? GetObjectByGuid<T>(ulong guid) where T : WoWObject
    {
        T? result = GetAnyObjectByGuid<T>(guid);
        return result is { IsValid: true } ? result : null;
    }

    internal static T Read<T>(ulong address) where T : unmanaged => Memory.Read<T>(address);
    internal static uint ReadPointer32(ulong address) => Memory.ReadPointer32(address);
    internal static bool IsPlausiblePointer(uint value)
        => value >= Vanilla5875.MinimumUserAddress && (value & 1u) == 0;

    private static LinuxProcessMemory Memory
        => _memory ?? throw new InvalidOperationException("ObjectManager has not been initialized.");

    private static WoWObject CreateObject(uint address, ulong guid, WoWObjectType type) => type switch
    {
        WoWObjectType.Item => new WoWItem(address, guid),
        WoWObjectType.Container => new WoWContainer(address, guid),
        WoWObjectType.Unit => new WoWUnit(address, guid),
        WoWObjectType.Player when guid == LocalGuid => new LocalPlayer(address, guid),
        WoWObjectType.Player => new WoWPlayer(address, guid),
        WoWObjectType.GameObject => new WoWGameObject(address, guid),
        WoWObjectType.DynamicObject => new WoWDynamicObject(address, guid),
        WoWObjectType.Corpse => new WoWCorpse(address, guid),
        _ => new WoWObject(address, guid)
    };

    private static void EnsurePointer(uint address, string name)
    {
        if (!IsPlausiblePointer(address))
            throw new InvalidDataException($"{name} pointer is invalid: 0x{address:X8}.");
    }
}
