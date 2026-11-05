using Microsoft.AspNetCore.Components;

namespace Ursacopy.Server;

/// <summary>Page that reloads its data on a timer (progress of a running copy, disk plugged in...).</summary>
public abstract class AutoRefreshBase : ComponentBase, IDisposable
{
    readonly CancellationTokenSource _cts = new();
    protected abstract void Load();
    protected virtual TimeSpan Interval => TimeSpan.FromSeconds(2);

    protected override void OnInitialized()
    {
        Load();
        _ = LoopAsync();
    }

    async Task LoopAsync()
    {
        using var t = new PeriodicTimer(Interval);
        try
        {
            while (await t.WaitForNextTickAsync(_cts.Token))
            {
                try { Load(); } catch { /* transient (disk busy): keep the previous data */ }
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose() => _cts.Cancel();
}
