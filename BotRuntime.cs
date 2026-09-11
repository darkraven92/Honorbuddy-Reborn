using TreeSharp;
using Styx.Logic;
using Styx.WoWInternals;

namespace Styx;

[Flags]
public enum PulseFlags : uint
{
    Objects = 1,
    Plugins = 2,
    BotEvents = 8,
    Targeting = 32,
    Looting = 64,
    InfoPanel = 128,
    Lua = 256,
    All = Objects | Plugins | BotEvents | Targeting | Looting | InfoPanel | Lua
}

public abstract class BotBase
{
    private bool _initialized;
    public abstract string Name { get; }
    public abstract Composite Root { get; }
    public abstract PulseFlags PulseFlags { get; }
    public virtual object? ConfigurationForm => null;
    public virtual bool IsPrimaryType => true;
    public virtual bool RequirementsMet => false;
    public bool Initialized => _initialized;
    public virtual void Pulse() { }
    public virtual void Initialize() { }
    public virtual void Start() { }
    public virtual void Stop() { }

    public void DoInitialize()
    {
        if (_initialized) return;
        Initialize();
        _initialized = true;
    }

    public override string ToString() => Name ?? string.Empty;
}

public sealed class BotChangedEventArgs : EventArgs
{
    public BotChangedEventArgs(BotBase? previous, BotBase? current)
    {
        Previous = previous;
        Current = current;
    }
    public BotBase? Previous { get; }
    public BotBase? Current { get; }
}

public static class BotEvents
{
    public static event EventHandler? OnBotStart;
    public static event EventHandler? OnBotStop;
    public static event EventHandler<BotChangedEventArgs>? OnBotChanged;
    internal static void RaiseBotStart() => OnBotStart?.Invoke(null, EventArgs.Empty);
    internal static void RaiseBotStop() => OnBotStop?.Invoke(null, EventArgs.Empty);
    internal static void RaiseBotChanged(BotBase? previous, BotBase? current)
        => OnBotChanged?.Invoke(null, new BotChangedEventArgs(previous, current));
}

public sealed class BotManager
{
    private readonly Dictionary<string, BotBase> _bots = new(StringComparer.OrdinalIgnoreCase);
    private BotManager() { }
    public static BotManager Instance { get; } = new();
    public static BotBase? Current { get; private set; }
    public Dictionary<string, BotBase> Bots => new(_bots, StringComparer.OrdinalIgnoreCase);

    public void SetCurrent(BotBase bot)
    {
        ArgumentNullException.ThrowIfNull(bot);
        if (Styx.Logic.BehaviorTree.TreeRoot.IsRunning)
            Styx.Logic.BehaviorTree.TreeRoot.Stop();
        BotBase? previous = Current;
        Current = bot;
        BotEvents.RaiseBotChanged(previous, bot);
    }

    public void Add(string name, BotBase bot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(bot);
        if (!_bots.ContainsKey(name)) _bots.Add(name, bot);
    }
    public void Add(BotBase bot) => Add(bot.Name, bot);
    public bool Remove(string name) => _bots.Remove(name);
    public Dictionary<string, BotBase> GetBots() => new(_bots, StringComparer.OrdinalIgnoreCase);
}

public static class WoWPulsator
{
    public static long PulseCount { get; private set; }
    public static long ObjectPulseCount { get; private set; }
    public static long TargetingPulseCount { get; private set; }
    public static PulseFlags DeferredFlagsSeen { get; private set; }

    public static void Pulse(PulseFlags flags)
    {
        PulseCount++;
        if ((flags & PulseFlags.Objects) != 0)
        {
            ObjectManager.Update();
            ObjectPulseCount++;
        }
        if ((flags & PulseFlags.Targeting) != 0)
        {
            Targeting.Instance.Pulse();
            TargetingPulseCount++;
        }

        DeferredFlagsSeen |= flags &
            (PulseFlags.Plugins | PulseFlags.BotEvents | PulseFlags.Looting |
             PulseFlags.InfoPanel | PulseFlags.Lua);
    }

    internal static void ResetCounters()
    {
        PulseCount = 0;
        ObjectPulseCount = 0;
        TargetingPulseCount = 0;
        DeferredFlagsSeen = 0;
        Targeting.Instance.ResetCounters();
    }
}
