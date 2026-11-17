using BrimDeck.Core;

internal static class QuotaPaceTests
{
    internal static void Run(Action<string, bool> check)
    {
        Loc.Use(Loc.English);
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

        // Half the 5-hour window gone, 40% used → remaining 60% at that pace lasts 1.5× elapsed = 3.75 h.
        var mid = Metric(40, 300, now.AddHours(2.5));
        var midLeft = QuotaPace.TryEstimate(mid, now);
        check("Mid-window pace: half elapsed at 40% used leaves 3.75 hours",
            midLeft is { } left && Almost(left, TimeSpan.FromHours(3.75))
            && QuotaPace.ShortLeft(left) == "~3h 45m"
            && QuotaPace.Line(mid, "in 2h 30m", now) == "~3h 45m · in 2h 30m");

        check("Unused quota has no pace estimate",
            QuotaPace.TryEstimate(Metric(0, 300, now.AddHours(2)), now) is null
            && QuotaPace.Line(Metric(0, 300, now.AddHours(2)), "in 2h", now) == "in 2h");

        check("Exhausted quota has no pace estimate",
            QuotaPace.TryEstimate(Metric(100, 300, now.AddHours(1)), now) is null);

        check("Missing window has no pace estimate",
            QuotaPace.TryEstimate(new UsageMetric(MetricKind.Percent, "每周额度") { Percent = 40, ResetAt = now.AddDays(1) }, now) is null);

        check("Reset already due has no pace estimate",
            QuotaPace.TryEstimate(Metric(40, 300, now.AddMinutes(-1)), now) is null);

        check("Elapsed not yet started (reset farther than the window) has no pace estimate",
            QuotaPace.TryEstimate(Metric(10, 300, now.AddHours(6)), now) is null);

        check("Estimate alone when there is no reset text",
            QuotaPace.Line(mid, "", now) == "~3h 45m");

        check("ShortLeft uses days when the estimate spans a day or more",
            QuotaPace.ShortLeft(TimeSpan.FromDays(2) + TimeSpan.FromHours(3)) == "~2d 3h"
            && QuotaPace.ShortLeft(TimeSpan.FromMinutes(0.4)) == "~1m");

        check("Tooltip names the current-pace estimate",
            QuotaPace.Tooltip(mid, now) == "About 3h 45m left at current pace"
            && QuotaPace.Tooltip(Metric(0, 300, now.AddHours(2)), now) is null);

        Loc.Use(Loc.Chinese);
        check("Chinese ShortLeft and line match the English structure",
            QuotaPace.ShortLeft(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(45)) == "~3 时 45 分"
            && QuotaPace.Line(mid, "2 时 30 分后", now) == "~3 时 45 分 · 2 时 30 分后"
            && QuotaPace.Tooltip(mid, now) == "按当前用量约还可使用 3 时 45 分");
        Loc.Use(Loc.English);

        // Cursor's older plan: 23 days elapsed in a 30-day cycle, 58.2% used → ~16.5 days left at that pace.
        var cursor = new UsageMetric(MetricKind.Percent, "Cursor 模型")
        { Percent = 58.2, Window = 30 * 24 * 60, ResetAt = now.AddDays(7) };
        var cursorLeft = QuotaPace.TryEstimate(cursor, now);
        check("Cursor billing-period pace puts the estimate left of the reset countdown",
            cursorLeft is { } days && Almost(days, TimeSpan.FromDays(23) * (41.8 / 58.2))
            && QuotaPace.Line(cursor, "in 7d 0h", now).StartsWith("~", StringComparison.Ordinal)
            && QuotaPace.Line(cursor, "in 7d 0h", now).Contains(" · in 7d 0h", StringComparison.Ordinal));
    }

    private static UsageMetric Metric(double used, int windowMinutes, DateTimeOffset reset) =>
        new(MetricKind.Percent, "5 小时额度") { Percent = used, Window = windowMinutes, ResetAt = reset };

    private static bool Almost(TimeSpan actual, TimeSpan expected) =>
        Math.Abs((actual - expected).TotalSeconds) < 1;
}
