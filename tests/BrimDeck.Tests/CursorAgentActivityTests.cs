using BrimDeck.Core;

static class CursorAgentActivityTests
{
    public static void Run(Action<string, bool> check)
    {
        check("Empty composer is idle", !CursorAgentActivity.IsActive("""{"status":"none","generatingBubbleIds":[]}"""));
        check("Completed composer is idle", !CursorAgentActivity.IsActive("""{"status":"completed","name":"Done","generatingBubbleIds":[]}"""));
        check("Aborted composer without an open run is idle",
            !CursorAgentActivity.IsActive("""{"status":"aborted","generatingBubbleIds":[]}"""));

        check("generatingBubbleIds marks active",
            CursorAgentActivity.IsActive("""{"status":"none","generatingBubbleIds":["b1"]}"""));
        check("isContinuationInProgress marks active",
            CursorAgentActivity.IsActive("""{"status":"completed","isContinuationInProgress":true,"generatingBubbleIds":[]}"""));
        check("Non-terminal status marks active",
            CursorAgentActivity.IsActive("""{"status":"generating","generatingBubbleIds":[]}"""));
        check("unfinishedRunAt marks active while thinking (aborted status, no bubbles)",
            CursorAgentActivity.IsActive("""{"status":"aborted","generatingBubbleIds":[],"unfinishedRunAt":1794774773837}"""));
        check("Completed composer with unfinishedRunAt cleared is idle",
            !CursorAgentActivity.IsActive("""{"status":"completed","generatingBubbleIds":[],"name":"Done"}"""));

        var desktop = new FakeDesktop();
        var idle = CursorAgentActivity.Read(desktop, Path.Combine(Path.GetTempPath(), "missing-cursor-state.vscdb"), DateTimeOffset.Now);
        check("Missing database yields idle", !idle.Active && idle.Note is null);

        using var named = System.Text.Json.JsonDocument.Parse("""{"name":"  Plan mode  ","modelConfig":{"modelName":"default","model":"gpt-5.4"}}""");
        check("Display name trims composer name", CursorAgentActivity.DisplayName(named.RootElement) == "Plan mode");
        check("Model prefers modelName unless it is default", CursorAgentActivity.ModelName(named.RootElement) == "gpt-5.4");

        using var onlyDefault = System.Text.Json.JsonDocument.Parse("""{"name":"X","modelConfig":{"modelName":"default"}}""");
        check("Default model name is omitted", CursorAgentActivity.ModelName(onlyDefault.RootElement) == "");

        desktop.Headers = [["c-done"], ["c-active"]];
        // Cursor often leaves toolFormerData.status at "loading" after a turn completes. That must
        // not keep the indicator active once unfinishedRunAt is cleared.
        desktop.Values["composerData:c-done"] =
            """{"status":"completed","name":"Stuck tools","generatingBubbleIds":[],"modelConfig":{"modelName":"composer-1"}}""";
        desktop.Values["composerData:c-active"] =
            """{"status":"aborted","name":"Thinking","generatingBubbleIds":[],"unfinishedRunAt":1794774773837,"modelConfig":{"modelName":"composer-1"}}""";
        var path = Path.Combine(Path.GetTempPath(), "brimdeck-cursor-agent-test.vscdb");
        File.WriteAllBytes(path, [0]);
        try
        {
            var thinking = CursorAgentActivity.Read(desktop, path, DateTimeOffset.Now);
            check("Read prefers an open unfinishedRunAt over a completed neighbor",
                thinking.Active && thinking.Name == "Thinking");

            desktop.Headers = [["c-done"]];
            desktop.Values.Remove("composerData:c-active");
            var after = CursorAgentActivity.Read(desktop, path, DateTimeOffset.Now);
            check("Read reports idle for completed composer even if stale loading tools exist elsewhere",
                !after.Active);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    private sealed class FakeDesktop : IDesktopSources
    {
        public List<string?[]> Headers = [];
        public Dictionary<string, string> Values = new(StringComparer.Ordinal);

        public string? ReadCursorToken(string database) => null;
        public Task<IReadOnlyList<LocalEndpoint>> FindAntigravityAsync(CancellationToken cancellation) => Task.FromResult<IReadOnlyList<LocalEndpoint>>([]);

        public IReadOnlyList<string?[]>? QueryDatabase(string database, string sql)
        {
            if (sql.Contains("composerHeaders", StringComparison.Ordinal)) return Headers;
            if (sql.Contains("unfinishedRunAt", StringComparison.Ordinal))
            {
                var rows = new List<string?[]>();
                foreach (var (key, value) in Values)
                {
                    if (!key.StartsWith("composerData:", StringComparison.Ordinal)) continue;
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(value);
                        if (doc.RootElement.TryGetProperty("unfinishedRunAt", out var at) && at.ValueKind == System.Text.Json.JsonValueKind.Number)
                            rows.Add([key]);
                    }
                    catch (System.Text.Json.JsonException) { }
                }
                return rows;
            }
            if (sql.Contains("SELECT value FROM cursorDiskKV WHERE key = 'composerData:", StringComparison.Ordinal))
            {
                var start = sql.IndexOf("composerData:", StringComparison.Ordinal) + "composerData:".Length;
                var end = sql.IndexOf('\'', start);
                var key = "composerData:" + sql[start..end].Replace("''", "'", StringComparison.Ordinal);
                return Values.TryGetValue(key, out var value) ? [[value]] : [];
            }
            return [];
        }
    }
}
