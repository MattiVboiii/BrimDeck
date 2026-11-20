using System.Text.Json;

namespace BrimDeck.Core;

// Best-effort reading of Cursor's local Composer/Agent turn state from state.vscdb (cursorDiskKV / composerHeaders).
// Cursor does not publish a supported API for this; fields may change between Cursor builds.
public sealed record CursorAgentSnapshot(bool Active, string Name, string Model, DateTimeOffset CheckedAt, string? Note = null)
{
    public static CursorAgentSnapshot Idle(DateTimeOffset checkedAt, string? note = null) => new(false, "", "", checkedAt, note);
}

public static class CursorAgentActivity
{
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(4);
    private const int RecentComposerLimit = 25;
    private static readonly HashSet<string> TerminalStatus = new(StringComparer.OrdinalIgnoreCase)
    { "completed", "none", "aborted", "cancelled", "canceled" };

    // Instantaneous reading (no grace). The watcher applies Grace across polls.
    public static CursorAgentSnapshot Read(IDesktopSources desktop, string cursorDatabase, DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(cursorDatabase)) return CursorAgentSnapshot.Idle(now);
            foreach (var composerId in RecentComposerIds(desktop, cursorDatabase))
            {
                if (ReadComposer(desktop, cursorDatabase, composerId) is not { } composer) continue;
                if (!IsActive(composer)) continue;
                return new CursorAgentSnapshot(true, DisplayName(composer), ModelName(composer), now);
            }
            return CursorAgentSnapshot.Idle(now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            return CursorAgentSnapshot.Idle(now, Loc.T("暂时无法读取 Cursor 代理状态", "Cursor agent state could not be read for now."));
        }
    }

    // Pure parse for tests: composer JSON.
    public static bool IsActive(string composerJson)
    {
        using var doc = JsonDocument.Parse(composerJson);
        return IsActive(doc.RootElement);
    }

    public static bool IsActive(JsonElement composer)
    {
        if (HasGeneratingBubbles(composer)) return true;
        if (composer.Get("isContinuationInProgress").ValueKind == JsonValueKind.True) return true;
        // Mid-turn Cursor often sets status to a terminal value (commonly "aborted") with an empty
        // generatingBubbleIds list while the model is thinking between tool steps. unfinishedRunAt
        // stays set on composerData for the whole open run and is cleared when the turn finishes.
        if (HasUnfinishedRun(composer)) return true;
        var status = composer.Get("status").Text();
        if (status.Length > 0 && !TerminalStatus.Contains(status)) return true;
        // Do not treat toolFormerData.status == "loading" as active on its own: Cursor often leaves
        // stale loading bubbles after a turn has already completed and unfinishedRunAt was cleared.
        return false;
    }

    public static bool HasUnfinishedRun(JsonElement composer) =>
        composer.Get("unfinishedRunAt").ValueKind is JsonValueKind.Number;

    public static string DisplayName(JsonElement composer)
    {
        var name = composer.Get("name").Text().Trim();
        return name.Length > 0 ? name : Loc.T("Cursor 代理", "Cursor agent");
    }

    public static string ModelName(JsonElement composer)
    {
        var config = composer.Get("modelConfig");
        foreach (var key in new[] { "modelName", "model", "defaultModel" })
        {
            var text = config.Get(key).Text().Trim();
            if (text.Length > 0 && !text.Equals("default", StringComparison.OrdinalIgnoreCase)) return text;
        }
        return "";
    }

    public static bool HasGeneratingBubbles(JsonElement composer)
    {
        var gens = composer.Get("generatingBubbleIds");
        if (gens.ValueKind == JsonValueKind.Array) return gens.GetArrayLength() > 0;
        if (gens.ValueKind == JsonValueKind.Object)
        {
            foreach (var _ in gens.EnumerateObject()) return true;
        }
        return false;
    }

    private static IEnumerable<string> RecentComposerIds(IDesktopSources desktop, string database)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var headers = desktop.QueryDatabase(database,
            "SELECT composerId FROM composerHeaders ORDER BY COALESCE(recency, lastUpdatedAt, createdAt) DESC LIMIT " + RecentComposerLimit);
        if (headers is not null)
            foreach (var row in headers)
                if (row is { Length: > 0 } && row[0] is { Length: > 0 } id && seen.Add(id))
                    yield return id;

        // Composers mid-run may sit outside the header sample; unfinishedRunAt is the cheap filter.
        var unfinished = desktop.QueryDatabase(database,
            "SELECT key FROM cursorDiskKV WHERE key LIKE 'composerData:%' AND json_extract(value, '$.unfinishedRunAt') IS NOT NULL LIMIT " + RecentComposerLimit);
        if (unfinished is null) yield break;
        foreach (var row in unfinished)
        {
            if (row is not { Length: > 0 } || row[0] is not { Length: > 0 } key) continue;
            const string prefix = "composerData:";
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var id = key[prefix.Length..];
            if (id.Length > 0 && seen.Add(id)) yield return id;
        }
    }

    private static JsonElement? ReadComposer(IDesktopSources desktop, string database, string composerId)
    {
        var rows = desktop.QueryDatabase(database, "SELECT value FROM cursorDiskKV WHERE key = 'composerData:" + Escape(composerId) + "' LIMIT 1");
        if (rows is not { Count: > 0 } || rows[0] is not { Length: > 0 } || rows[0][0] is not { Length: > 0 } json) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException) { return null; }
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
