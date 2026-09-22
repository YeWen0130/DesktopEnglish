using System.Windows.Threading;

namespace DesktopEnglish.Services;

/// <summary>
/// Marshals work onto the WPF UI thread. Toast activation callbacks and background timer ticks arrive on
/// arbitrary threads, so every UI touch goes through this abstraction.
/// </summary>
public interface IDispatcherService
{
    bool IsOnUiThread { get; }

    void Invoke(Action action);

    Task InvokeAsync(Func<Task> action);

    Task<T> InvokeAsync<T>(Func<Task<T>> action);
}

/// <summary>Default implementation backed by the WPF application dispatcher.</summary>
public sealed class DispatcherService(Dispatcher dispatcher) : IDispatcherService
{
    public bool IsOnUiThread => dispatcher.CheckAccess();

    public void Invoke(Action action)
    {
        if (IsOnUiThread)
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    public Task InvokeAsync(Func<Task> action) => dispatcher.InvokeAsync(action).Task.Unwrap();

    public Task<T> InvokeAsync<T>(Func<Task<T>> action) => dispatcher.InvokeAsync(action).Task.Unwrap();
}
