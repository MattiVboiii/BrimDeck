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
    private static readonly HashSet<string> FinishedTool = new(StringComparer.OrdinalIgnoreCase)
    { "completed", "error", "cancelled", "canceled" };

    // Instantaneous reading (no grace). The watcher applies Grace across polls.
    public static CursorAgentSnapshot Read(IDesktopSources desktop, string cursorDatabase, DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(cursorDatabase)) return CursorAgentSnapshot.Idle(now);
            foreach (var composerId in RecentComposerIds(desktop, cursorDatabase))
            {
                if (ReadComposer(desktop, cursorDatabase, composerId) is not { } composer) continue;
                bool tools = HasUnfinishedTools(desktop, cursorDatabase, composerId);
                if (!IsActive(composer, tools)) continue;
                return new CursorAgentSnapshot(true, DisplayName(composer), ModelName(composer), now);
            }
            return CursorAgentSnapshot.Idle(now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            return CursorAgentSnapshot.Idle(now, Loc.T("暂时无法读取 Cursor 代理状态", "Cursor agent state could not be read for now."));
        }
    }

    // Pure parse for tests: composer JSON plus optional bubble JSON texts.
    public static bool IsActive(string composerJson, IEnumerable<string>? bubbleJsons = null)
    {
        using var doc = JsonDocument.Parse(composerJson);
        bool tools = false;
        if (bubbleJsons is not null)
            foreach (var bubble in bubbleJsons)
            {
                try
                {
                    using var bubbleDoc = JsonDocument.Parse(bubble);
                    if (ToolUnfinished(bubbleDoc.RootElement)) { tools = true; break; }
                }
                catch (JsonException) { }
            }
        return IsActive(doc.RootElement, tools);
    }

    public static bool IsActive(JsonElement composer, bool hasUnfinishedTool)
    {
        if (HasGeneratingBubbles(composer)) return true;
        if (composer.Get("isContinuationInProgress").ValueKind == JsonValueKind.True) return true;
        var status = composer.Get("status").Text();
        if (status.Length > 0 && !TerminalStatus.Contains(status)) return true;
        if (hasUnfinishedTool) return true;
        return false;
    }

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

    public static bool ToolUnfinished(JsonElement bubble)
    {
        var status = bubble.Get("toolFormerData").Get("status").Text();
        return status.Length > 0 && !FinishedTool.Contains(status);
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

    private static bool HasUnfinishedTools(IDesktopSources desktop, string database, string composerId)
    {
        // Prefer a JSON path when SQLite has json1; fall back to a bounded key scan.
        var rows = desktop.QueryDatabase(database,
            "SELECT value FROM cursorDiskKV WHERE key LIKE 'bubbleId:" + Escape(composerId) + ":%' " +
            "AND json_extract(value, '$.toolFormerData.status') IS NOT NULL " +
            "AND lower(json_extract(value, '$.toolFormerData.status')) NOT IN ('completed','error','cancelled','canceled') LIMIT 1");
        if (rows is { Count: > 0 }) return true;
        if (rows is not null) return false;

        var bubbles = desktop.QueryDatabase(database,
            "SELECT value FROM cursorDiskKV WHERE key LIKE 'bubbleId:" + Escape(composerId) + ":%' LIMIT 80");
        if (bubbles is null) return false;
        foreach (var row in bubbles)
        {
            if (row is not { Length: > 0 } || row[0] is not { Length: > 0 } json) continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (ToolUnfinished(doc.RootElement)) return true;
            }
            catch (JsonException) { }
        }
        return false;
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
