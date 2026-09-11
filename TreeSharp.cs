namespace TreeSharp;

// Honorbuddy 2.0.0.5999 uses the older TreeSharp numeric ordering.
// Recovered from the embedded 2012 IL:
//   Failure = 0, Running = 1, Success = 2.
public enum RunStatus
{
    Failure = 0,
    Running = 1,
    Success = 2
}

public delegate RunStatus ActionDelegate(object? context);
public delegate void ActionSucceedDelegate(object? context);
public delegate bool CanRunDecoratorDelegate(object? context);
public delegate object? ContextChangeHandler(object? context);

/// <summary>
/// Clean-room implementation of the small TreeSharp surface required by the
/// recovered Honorbuddy TreeRoot/BotBase layer. It intentionally mirrors the
/// 2012 Start -> Tick -> Stop lifecycle instead of newer coroutine APIs.
/// </summary>
public abstract class Composite
{
    private IEnumerator<RunStatus>? _enumerator;

    protected Composite()
    {
        Guid = System.Guid.NewGuid();
    }

    public RunStatus? LastStatus { get; protected set; }
    public Composite? Parent { get; internal set; }
    public Guid Guid { get; }
    public bool IsRunning => LastStatus == RunStatus.Running;

    protected virtual void OnStart(object? context) { }
    protected virtual void OnStop(object? context) { }
    protected abstract IEnumerable<RunStatus> Execute(object? context);

    public virtual void Start(object? context)
    {
        StopEnumeratorOnly();
        LastStatus = null;
        OnStart(context);
        _enumerator = Execute(context).GetEnumerator();
    }

    public virtual RunStatus Tick(object? context)
    {
        if (_enumerator is null)
            throw new InvalidOperationException("Cannot run Tick before running Start first!");

        if (!_enumerator.MoveNext())
            throw new InvalidOperationException(
                $"Composite {GetType().Name} completed without yielding a final RunStatus.");

        LastStatus = _enumerator.Current;
        return LastStatus.Value;
    }

    public virtual void Stop(object? context)
    {
        try
        {
            OnStop(context);
        }
        finally
        {
            StopEnumeratorOnly();
            // The 2012 Composite::Stop IL converts Running to Failure.
            if (LastStatus == RunStatus.Running)
                LastStatus = RunStatus.Failure;
        }
    }

    private void StopEnumeratorOnly()
    {
        _enumerator?.Dispose();
        _enumerator = null;
    }
}

public sealed class Action : Composite
{
    private readonly ActionDelegate? _runner;
    private readonly ActionSucceedDelegate? _succeedRunner;

    public Action(ActionDelegate runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public Action(ActionSucceedDelegate runner)
    {
        _succeedRunner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public RunStatus RunAction(object? context)
    {
        if (_runner is not null)
            return _runner(context);
        if (_succeedRunner is not null)
        {
            _succeedRunner(context);
            return RunStatus.Success;
        }
        return RunStatus.Failure;
    }

    protected override IEnumerable<RunStatus> Execute(object? context)
    {
        while (true)
        {
            RunStatus status = RunAction(context);
            yield return status;
            if (status != RunStatus.Running)
                yield break;
        }
    }
}

public sealed class PrioritySelector : Composite
{
    private readonly Composite[] _children;
    private readonly ContextChangeHandler? _contextChanger;

    public PrioritySelector(params Composite[] children)
        : this(null, children)
    {
    }

    public PrioritySelector(ContextChangeHandler? contextChanger, params Composite[] children)
    {
        _contextChanger = contextChanger;
        _children = children ?? throw new ArgumentNullException(nameof(children));
        foreach (Composite child in _children)
            child.Parent = this;
    }

    protected override IEnumerable<RunStatus> Execute(object? context)
    {
        object? childContext = _contextChanger is null ? context : _contextChanger(context);

        foreach (Composite child in _children)
        {
            child.Start(childContext);
            try
            {
                while (true)
                {
                    RunStatus status = child.Tick(childContext);
                    if (status == RunStatus.Running)
                    {
                        yield return RunStatus.Running;
                        continue;
                    }

                    if (status == RunStatus.Success)
                    {
                        yield return RunStatus.Success;
                        yield break;
                    }

                    break;
                }
            }
            finally
            {
                child.Stop(childContext);
            }
        }

        yield return RunStatus.Failure;
    }
}

public sealed class Sequence : Composite
{
    private readonly Composite[] _children;
    private readonly ContextChangeHandler? _contextChanger;

    public Sequence(params Composite[] children)
        : this(null, children)
    {
    }

    public Sequence(ContextChangeHandler? contextChanger, params Composite[] children)
    {
        _contextChanger = contextChanger;
        _children = children ?? throw new ArgumentNullException(nameof(children));
        foreach (Composite child in _children)
            child.Parent = this;
    }

    protected override IEnumerable<RunStatus> Execute(object? context)
    {
        object? childContext = _contextChanger is null ? context : _contextChanger(context);

        foreach (Composite child in _children)
        {
            child.Start(childContext);
            try
            {
                while (true)
                {
                    RunStatus status = child.Tick(childContext);
                    if (status == RunStatus.Running)
                    {
                        yield return RunStatus.Running;
                        continue;
                    }

                    if (status == RunStatus.Failure)
                    {
                        yield return RunStatus.Failure;
                        yield break;
                    }

                    break;
                }
            }
            finally
            {
                child.Stop(childContext);
            }
        }

        yield return RunStatus.Success;
    }
}

public class Decorator : Composite
{
    private readonly CanRunDecoratorDelegate _runner;

    public Decorator(CanRunDecoratorDelegate runner, Composite decoratedChild)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        DecoratedChild = decoratedChild ?? throw new ArgumentNullException(nameof(decoratedChild));
        DecoratedChild.Parent = this;
    }

    public Composite DecoratedChild { get; }
    public bool CanRun(object? context) => _runner(context);

    protected override IEnumerable<RunStatus> Execute(object? context)
    {
        if (!CanRun(context))
        {
            yield return RunStatus.Failure;
            yield break;
        }

        DecoratedChild.Start(context);
        try
        {
            while (true)
            {
                RunStatus status = DecoratedChild.Tick(context);
                yield return status;
                if (status != RunStatus.Running)
                    yield break;
            }
        }
        finally
        {
            DecoratedChild.Stop(context);
        }
    }
}
