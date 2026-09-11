using TreeSharp;

namespace Styx.Logic.BehaviorTree;

public sealed class StatusTextChangedEventArgs : EventArgs
{
    public StatusTextChangedEventArgs(string text) => Text = text;
    public string Text { get; }
}

/// <summary>
/// Build-5875 clean-room TreeRoot using the lifecycle recovered from
/// Honorbuddy 2.0.0.5999 MethodDefs 11766-11777.
/// </summary>
public static class TreeRoot
{
    private static readonly object Sync = new();
    private static Thread? _worker;
    private static volatile bool _isRunning;
    private static string _statusText = string.Empty;
    private static string _goalText = string.Empty;

    public static byte TicksPerSecond { get; set; } = 15;
    public static Styx.BotBase? Current => Styx.BotManager.Current;
    public static bool IsRunning => _isRunning;
    public static Exception? LastWorkerException { get; private set; }
    public static long TickCount { get; private set; }
    public static long RootRestartCount { get; private set; }

    public static string StatusText
    {
        get => _statusText;
        set
        {
            value ??= string.Empty;
            if (_statusText == value)
                return;
            _statusText = value;
            StatusTextChanged?.Invoke(null, new StatusTextChangedEventArgs(value));
        }
    }

    public static string GoalText
    {
        get => _goalText;
        set => _goalText = value ?? string.Empty;
    }

    public static event EventHandler<StatusTextChangedEventArgs>? StatusTextChanged;

    public static void Start()
    {
        lock (Sync)
        {
            if (_isRunning)
                return;

            Styx.BotBase bot = Current
                ?? throw new InvalidOperationException("No current BotBase has been selected.");
            if (!Styx.StyxWoW.IsInGame)
                throw new InvalidOperationException("WoW is not in game or LocalPlayer is unavailable.");
            if (!bot.RequirementsMet)
                throw new InvalidOperationException($"Bot '{bot.Name}' reports RequirementsMet=false.");
            if (TicksPerSecond == 0)
                throw new InvalidOperationException("TicksPerSecond must be greater than zero.");

            LastWorkerException = null;
            TickCount = 0;
            RootRestartCount = 0;
            Styx.WoWPulsator.ResetCounters();

            bot.DoInitialize();
            bot.Start();
            bot.Root.Start(null);

            _isRunning = true;
            StatusText = $"Running {bot.Name}";
            Styx.BotEvents.RaiseBotStart();

            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "Honorbuddy5875.TreeRoot"
            };
            _worker.Start();
        }
    }

    public static void Stop()
    {
        Thread? worker;
        lock (Sync)
        {
            if (!_isRunning && _worker is null)
                return;
            _isRunning = false;
            worker = _worker;
        }

        if (worker is not null && worker != Thread.CurrentThread)
            worker.Join(TimeSpan.FromSeconds(2));

        lock (Sync)
        {
            Styx.BotBase? bot = Current;
            if (bot is not null)
            {
                bot.Stop();
                bot.Root.Stop(null);
            }

            _worker = null;
            StatusText = "Stopped";
            Styx.BotEvents.RaiseBotStop();
        }
    }

    /// <summary>
    /// Exposed for deterministic diagnostics. The normal path is Start(),
    /// which calls this from a 15 Hz worker just like the recovered TreeRoot.
    /// </summary>
    public static void TickOnce()
    {
        Styx.BotBase bot = Current
            ?? throw new InvalidOperationException("No current BotBase has been selected.");

        if (!Styx.StyxWoW.IsInGame)
        {
            StatusText = "Not in game";
            return;
        }

        Styx.WoWPulsator.Pulse(bot.PulseFlags);
        bot.Pulse();

        Composite root = bot.Root;
        root.Tick(null);
        TickCount++;

        // Recovered 2012 behavior: RunStatus.Running has underlying value 1.
        // Any completed root is stopped and restarted for the next pulse.
        if (root.LastStatus != RunStatus.Running)
        {
            root.Stop(null);
            root.Start(null);
            RootRestartCount++;
        }
    }

    private static void WorkerLoop()
    {
        try
        {
            while (_isRunning)
            {
                long started = Environment.TickCount64;
                TickOnce();

                int period = Math.Max(1, 1000 / TicksPerSecond);
                long elapsed = Environment.TickCount64 - started;
                int sleep = period - (int)Math.Min(period, Math.Max(0, elapsed));
                if (sleep > 0)
                    Thread.Sleep(sleep);
            }
        }
        catch (Exception ex)
        {
            LastWorkerException = ex;
            _isRunning = false;
            StatusText = $"Worker failed: {ex.GetType().Name}";
        }
    }
}
