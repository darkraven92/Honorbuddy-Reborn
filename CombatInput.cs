namespace Honorbuddy5875.Movement;

/// <summary>
/// Minimal combat input surface for build 5875 validation.
/// Linux input-event key code KEY_T = 20. This probe expects WoW's
/// "Attack Target" action to be bound to T. No memory writes or injection.
/// </summary>
public sealed class UInputCombatActions : IDisposable
{
    private const ushort KeyAttackTarget = 20;
    private readonly UInputKeyboard _keyboard = new();
    private bool _disposed;

    public void ToggleAttackTarget()
    {
        ThrowIfDisposed();
        _keyboard.Tap(KeyAttackTarget, 40);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _keyboard.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UInputCombatActions));
    }
}
