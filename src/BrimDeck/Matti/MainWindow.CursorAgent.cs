using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BrimDeck.Core;
using BrimDeck.Matti;

namespace BrimDeck;

public partial class MainWindow
{
    private static readonly object AgentRegionTag = new();
    private CursorAgentSnapshot _agent = CursorAgentSnapshot.Idle(DateTimeOffset.MinValue);
    private bool _agentKnown;
    private DispatcherTimer? _agentPulse;

    private void AttachCursorAgentWatcher()
    {
        var current = _app.CursorAgent.Current;
        _agent = current;
        _agentKnown = current.CheckedAt > DateTimeOffset.MinValue;
        // Follow panels only mirror state; the lead owns the watcher subscription.
        if (_lead is not null) return;
        _app.CursorAgent.Changed += OnCursorAgentChanged;
        // Do not render or walk App.Decks here: the lead is still inside its constructor, so App.Deck is null.
        Loaded += (_, _) => ApplyCursorAgent(_app.CursorAgent.Current, _app.CursorAgent.Current.CheckedAt > DateTimeOffset.MinValue);
    }

    private void OnCursorAgentChanged()
    {
        if (_lead is not null) return;
        var agent = _app.CursorAgent.Current;
        var known = agent.CheckedAt > DateTimeOffset.MinValue;
        // App.Decks prepends App.Deck; during startup that is still null.
        if (_app.Deck is null)
        {
            _agent = agent;
            _agentKnown = known;
            return;
        }
        foreach (var deck in _app.Decks)
            if (deck is not null) deck.ApplyCursorAgent(agent, known);
    }

    internal void ApplyCursorAgent(CursorAgentSnapshot agent, bool known)
    {
        _agent = agent; _agentKnown = known;
        if (!IsLoaded) return;
        if (IsUsagePage) RenderUsage();
        RenderCompact();
    }

    private void AddCursorAgentChip(AppEntry entry, Grid head, Layout layout, double chipTop, double chipLift, ref double occupied)
    {
        if (!Settings.CursorAgentLive || entry.QuotaSource != ProviderId.Cursor && entry.UsageSource != ProviderId.Cursor) return;
        if (!_agentKnown && !_agent.Active) return;
        bool active = _agent.Active;
        var label = active ? Loc.T("Cursor 代理运行中", "Cursor agent running") : Loc.T("Cursor 代理空闲", "Cursor agent idle");
        var color = active ? Look.Accent : Look.Tertiary;
        var text = UI.Centered(label, layout.Plan, color);
        text.VerticalAlignment = VerticalAlignment.Top;
        if (chipLift != 0) text.RenderTransform = new TranslateTransform(0, chipLift);
        var chip = new Border
        {
            Child = text,
            BorderBrush = UI.Brush(active ? Look.Accent : Look.Outline),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Look.Corner(4)),
            Padding = new Thickness(layout.Tier >= Tier.Dense ? 4 : 5, 0, layout.Tier >= Tier.Dense ? 4 : 5, 0),
            Margin = new Thickness(6, chipTop, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = AgentTooltip()
        };
        int stretch = head.ColumnDefinitions.Count - 1;
        head.ColumnDefinitions.Insert(stretch, new ColumnDefinition { Width = GridLength.Auto });
        foreach (var child in head.Children.OfType<FrameworkElement>())
            if (Grid.GetColumn(child) >= stretch) Grid.SetColumn(child, Grid.GetColumn(child) + 1);
        Grid.SetColumn(chip, stretch);
        head.Children.Add(chip);
        chip.Measure(new Size(double.PositiveInfinity, layout.Head));
        occupied += chip.DesiredSize.Width + 6;
    }

    private string? AgentTooltip()
    {
        if (!_agent.Active) return Loc.T("Cursor 代理当前空闲（本机 Composer 状态，尽力而为）", "Cursor agent is idle (local Composer state, best-effort).");
        var parts = new List<string> { Loc.T("Cursor 代理运行中", "Cursor agent running") };
        if (_agent.Name.Length > 0) parts.Add(_agent.Name);
        if (_agent.Model.Length > 0) parts.Add(_agent.Model);
        parts.Add(Loc.T("读取本机 Composer 状态，尽力而为", "Reads local Composer state, best-effort"));
        return string.Join(" · ", parts);
    }

    // Collapsed CompactPart.Agent: only while CursorAgentLive is on and a turn is active. Width participates in FixedFace.
    private FrameworkElement? CompactAgentBadge(double scale, out double width)
    {
        width = 0;
        StopAgentPulse();
        if (!Settings.ShowsAgent(EffectiveStyle) || !_agent.Active) return null;
        double s = CompactMusicLayout.SafeScale(scale);
        double font = Math.Max(1, CompactMusicLayout.Half(11 * s));
        double line = Math.Max(1, CompactMusicLayout.Whole(16 * s));
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var status = UI.Fixed(Loc.T("Cursor 代理运行中", "Cursor agent running"), font, line, Look.Accent, FontWeights.SemiBold);
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = Math.Max(6, CompactMusicLayout.Whole(7 * s)),
            Height = Math.Max(6, CompactMusicLayout.Whole(7 * s)),
            Fill = UI.Brush(Look.Accent),
            Margin = new Thickness(0, 0, CompactMusicLayout.Whole(5 * s), 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = AgentTooltip(),
            Tag = AgentRegionTag
        };
        row.Children.Add(dot);
        row.Children.Add(status);
        row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        width = DevicePixels.Up(row.DesiredSize.Width, dpi);
        row.Width = width;
        row.Height = Math.Max(line, dot.Height);
        EnsureAgentPulse(row);
        return row;
    }

    private void EnsureAgentPulse(FrameworkElement? chip = null)
    {
        if (!Animates) { StopAgentPulse(); return; }
        if (_agentPulse is not null) return;
        var target = chip ?? CompactRings.Children.OfType<FrameworkElement>()
            .SelectMany(WalkPanels)
            .FirstOrDefault(e => ReferenceEquals(e.Tag, AgentRegionTag));
        var dot = (target as Panel)?.Children.OfType<System.Windows.Shapes.Ellipse>().FirstOrDefault();
        if (dot is null) return;
        var fade = new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(0.9))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        dot.BeginAnimation(UIElement.OpacityProperty, fade);
        _agentPulse = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _agentPulse.Start();
    }

    private static IEnumerable<FrameworkElement> WalkPanels(FrameworkElement root)
    {
        yield return root;
        if (root is Panel panel)
            foreach (var child in panel.Children.OfType<FrameworkElement>())
                foreach (var nested in WalkPanels(child))
                    yield return nested;
    }

    private void StopAgentPulse()
    {
        foreach (var panel in WalkPanels(CompactRings).OfType<StackPanel>().Where(p => ReferenceEquals(p.Tag, AgentRegionTag)))
            foreach (var child in panel.Children.OfType<UIElement>())
                child.BeginAnimation(UIElement.OpacityProperty, null);
        if (_agentPulse is not null) { _agentPulse.Stop(); _agentPulse = null; }
    }
}
