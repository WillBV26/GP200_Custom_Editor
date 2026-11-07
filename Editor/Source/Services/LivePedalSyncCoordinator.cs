namespace Gp200.Replica;

/// <summary>
/// Centralized Auto-Sync Live Engine & Bidirectional Coordinator (Pilar 2).
/// Automatically transmits any parameter, routing, bypass, model, or scene change
/// from the UI to the Valeton GP-200 within 25 ms (with intelligent debouncing),
/// coordinates hardware-to-UI live listening with zero flicker, and powers the
/// one-click fast re-synchronization across all views.
/// </summary>
public sealed class LivePedalSyncCoordinator
{
    public static LivePedalSyncCoordinator Instance { get; } = new();

    private EditorWindow? _editor;
    private CancellationTokenSource? _debounceCts;
    private readonly object _lock = new();

    public bool IsLiveConnected => _editor?.IsLiveConnected ?? false;

    public event Action<bool>? LiveSyncStatusChanged;
    public event Action<PresetDocument>? HardwareStateDispatched;

    public void Attach(EditorWindow editor)
    {
        _editor = editor;
    }

    public void NotifyStatusChanged(bool connected)
    {
        try
        {
            LiveSyncStatusChanged?.Invoke(connected);
        }
        catch
        {
            // Safeguard against closed window event listeners
        }
    }

    /// <summary>
    /// Enqueues any UI change (slider move, ON/OFF toggle, ComboBox model change,
    /// chain reordering, or Scene switch) to be transmitted to the GP-200 in &lt;25 ms.
    /// If the edit requires a full working patch buffer dump (such as stock chain routing / words 13/14),
    /// it triggers a transparent background patch sync.
    /// </summary>
    public void EnqueueAutoSync(PresetDocument candidate, bool requiresPatchBufferDump = false)
    {
        if (_editor == null) return;

        lock (_lock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(25, token);
                    if (token.IsCancellationRequested) return;

                    await _editor.Dispatcher.InvokeAsync(async () =>
                    {
                        if (!_editor.IsLiveConnected) return;

                        if (requiresPatchBufferDump)
                        {
                            await _editor.SyncChainToDeviceSilentAsync();
                        }
                        else
                        {
                            _editor.QueueLive(candidate);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    // Debounced by a newer incoming change within 25ms
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LivePedalSyncCoordinator: {ex.Message}");
                }
            }, token);
        }
    }

    /// <summary>
    /// Fast re-synchronization action ('🔄 Re-Sincronizar con Pedal').
    /// Reads the active preset from the physical GP-200 and refreshes all editor views in real time.
    /// </summary>
    public async Task ForceFullSyncWithPedalAsync()
    {
        if (_editor == null) return;

        await _editor.Dispatcher.InvokeAsync(async () =>
        {
            await _editor.ReSyncWithPedalAsync();
        });
    }

    /// <summary>
    /// Dispatches in-place hardware changes (knob turned on pedal, footswitch pressed, expression moved)
    /// to all open views without redrawing or flickering.
    /// </summary>
    public void DispatchHardwareEvent(PresetDocument actual)
    {
        try
        {
            HardwareStateDispatched?.Invoke(actual);
        }
        catch
        {
            // Safeguard against closed window event listeners
        }
    }
}

