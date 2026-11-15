using System.Windows.Threading;
using BrimDeck.Core;
using BrimDeck.Native;

namespace BrimDeck.Matti;

// Polls Cursor's local Composer state and publishes Active/Idle with a short grace so tool steps do not flicker.
public sealed class CursorAgentWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IDesktopSources _desktop;
    private readonly Func<DeckSettings> _settings;
    private readonly Func<DataLocations> _locations;
    private readonly DispatcherTimer _timer;
    private readonly object _gate = new();
    private bool _busy;
    private DateTimeOffset? _lastActiveAt;
    private CursorAgentSnapshot _lastActive = CursorAgentSnapshot.Idle(DateTimeOffset.MinValue);
    private CursorAgentSnapshot _current = CursorAgentSnapshot.Idle(DateTimeOffset.MinValue);

    public CursorAgentSnapshot Current { get { lock (_gate) return _current; } }
    public event Action? Changed;

    public CursorAgentWatcher(Dispatcher dispatcher, Func<DeckSettings> settings, Func<DataLocations>? locations = null, IDesktopSources? desktop = null)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        _locations = locations ?? DataLocations.Detect;
        _desktop = desktop ?? new DesktopSources();
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(2.5) };
        _timer.Tick += OnTick;
    }

    private void OnTick(object? sender, EventArgs e) => _ = PollAsync();

    public void Apply()
    {
        var settings = _settings();
        bool want = settings.CursorAgentLive && settings.EnabledApps.Any(app => app.QuotaSource == ProviderId.Cursor || app.UsageSource == ProviderId.Cursor);
        if (want) { if (!_timer.IsEnabled) { _timer.Start(); _ = PollAsync(); } }
        else
        {
            _timer.Stop();
            PublishIdle();
        }
    }

    public void Dispose() { _timer.Stop(); _timer.Tick -= OnTick; }

    private async Task PollAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var settings = _settings();
            if (!settings.CursorAgentLive || !settings.EnabledApps.Any(app => app.QuotaSource == ProviderId.Cursor || app.UsageSource == ProviderId.Cursor))
            {
                PublishIdle();
                return;
            }
            // Composer state is only meaningful while Cursor itself is running; skip the DB read otherwise.
            if (!CursorRunning())
            {
                PublishIdle();
                return;
            }
            var database = _locations().CursorDatabase;
            var now = DateTimeOffset.Now;
            var snap = await Task.Run(() => CursorAgentActivity.Read(_desktop, database, now)).ConfigureAwait(false);
            if (snap.Active)
            {
                lock (_gate) { _lastActiveAt = now; _lastActive = snap; }
                Publish(snap);
                return;
            }
            DateTimeOffset? graceFrom;
            CursorAgentSnapshot kept;
            lock (_gate) { graceFrom = _lastActiveAt; kept = _lastActive; }
            if (graceFrom is { } at && now - at < CursorAgentActivity.Grace)
                Publish(kept with { CheckedAt = now });
            else
                Publish(snap);
        }
        finally { _busy = false; }
    }

    private void PublishIdle()
    {
        lock (_gate) { _lastActiveAt = null; }
        Publish(CursorAgentSnapshot.Idle(DateTimeOffset.Now));
    }

    // Cursor's main process on Windows is Cursor.exe (Electron). Cheap Toolhelp snapshot, not a full Process.GetProcesses.
    private static bool CursorRunning() => ProcessSnapshot.Any("Cursor.exe");

    private void Publish(CursorAgentSnapshot snap)
    {
        bool changed;
        lock (_gate)
        {
            changed = snap.Active != _current.Active || snap.Name != _current.Name || snap.Model != _current.Model;
            _current = snap;
        }
        if (!changed) return;
        void Raise() => Changed?.Invoke();
        if (_dispatcher.CheckAccess()) Raise();
        else _dispatcher.BeginInvoke(Raise);
    }
}
