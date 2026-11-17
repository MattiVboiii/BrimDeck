namespace BrimDeck.Core;

// Burn-rate estimate of how long a quota lasts at the pace so far in its window: elapsed × remaining% / used%.
// Needs Window (minutes), UsedPercent and a ResetAt still ahead. Formatting matches UI.ShortReset's day/hour/minute steps.
public static class QuotaPace
{
    public static TimeSpan? TryEstimate(UsageMetric metric, DateTimeOffset now)
    {
        if (metric.Window is not int window || window <= 0) return null;
        if (metric.ResetAt is not { } reset) return null;
        if (metric.UsedPercent is not { } used) return null;
        var untilReset = reset - now;
        if (untilReset <= TimeSpan.Zero) return null;
        var elapsed = TimeSpan.FromMinutes(window) - untilReset;
        if (elapsed <= TimeSpan.Zero) return null;
        if (used is <= 0 or >= 100) return null;
        double remaining = 100 - used;
        long ticks = (long)(elapsed.Ticks * (remaining / used));
        if (ticks <= 0) return null;
        return TimeSpan.FromTicks(ticks);
    }

    // "~2d 3h" / "~2 天 3 时", same granularity as the reset countdown.
    public static string ShortLeft(TimeSpan left)
    {
        if (left <= TimeSpan.Zero) return "";
        if (left.TotalDays >= 1) return Loc.T($"~{(int)left.TotalDays} 天 {left.Hours} 时", $"~{(int)left.TotalDays}d {left.Hours}h");
        if (left.TotalHours >= 1) return Loc.T($"~{left.Hours} 时 {left.Minutes} 分", $"~{left.Hours}h {left.Minutes}m");
        int minutes = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
        return Loc.T($"~{minutes} 分", $"~{minutes}m");
    }

    // Estimate alone, or "~2h · in 3h" when a reset (or other) countdown is already shown on the right.
    public static string Line(UsageMetric metric, string rightText, DateTimeOffset now)
    {
        if (TryEstimate(metric, now) is not { } left) return rightText;
        var estimate = ShortLeft(left);
        if (estimate.Length == 0) return rightText;
        return rightText.Length > 0 ? estimate + Loc.T(" · ", " · ") + rightText : estimate;
    }

    public static string? Tooltip(UsageMetric metric, DateTimeOffset now)
    {
        if (TryEstimate(metric, now) is not { } left) return null;
        var estimate = ShortLeft(left);
        if (estimate.Length == 0) return null;
        return Loc.T($"按当前用量约还可使用 {estimate.TrimStart('~')}", $"About {estimate.TrimStart('~')} left at current pace");
    }
}
