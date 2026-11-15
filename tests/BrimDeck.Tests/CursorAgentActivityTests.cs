using BrimDeck.Core;

static class CursorAgentActivityTests
{
    public static void Run(Action<string, bool> check)
    {
        check("Empty composer is idle", !CursorAgentActivity.IsActive("""{"status":"none","generatingBubbleIds":[]}"""));
        check("Completed composer is idle", !CursorAgentActivity.IsActive("""{"status":"completed","name":"Done","generatingBubbleIds":[]}"""));
        check("Aborted composer without tools is idle", !CursorAgentActivity.IsActive("""{"status":"aborted","generatingBubbleIds":[],"unfinishedRunAt":1794774773837}"""));

        check("generatingBubbleIds marks active",
            CursorAgentActivity.IsActive("""{"status":"none","generatingBubbleIds":["b1"]}"""));
        check("isContinuationInProgress marks active",
            CursorAgentActivity.IsActive("""{"status":"completed","isContinuationInProgress":true,"generatingBubbleIds":[]}"""));
        check("Non-terminal status marks active",
            CursorAgentActivity.IsActive("""{"status":"generating","generatingBubbleIds":[]}"""));

        var loading = """{"type":2,"toolFormerData":{"name":"read_file_v2","status":"loading"}}""";
        var done = """{"type":2,"toolFormerData":{"name":"read_file_v2","status":"completed"}}""";
        check("Loading tool bubble marks active even when status is aborted",
            CursorAgentActivity.IsActive("""{"status":"aborted","generatingBubbleIds":[],"name":"Live agent mode for AI"}""", [loading]));
        check("Completed tool bubbles keep an aborted composer idle",
            !CursorAgentActivity.IsActive("""{"status":"aborted","generatingBubbleIds":[]}""", [done]));
        check("Error tool status is finished",
            !CursorAgentActivity.IsActive("""{"status":"none"}""", ["""{"toolFormerData":{"status":"error"}}"""]));

        using var named = System.Text.Json.JsonDocument.Parse("""{"name":"  Plan mode  ","modelConfig":{"modelName":"default","model":"gpt-5.4"}}""");
        check("Display name trims composer name", CursorAgentActivity.DisplayName(named.RootElement) == "Plan mode");
        check("Model prefers modelName unless it is default", CursorAgentActivity.ModelName(named.RootElement) == "gpt-5.4");

        using var onlyDefault = System.Text.Json.JsonDocument.Parse("""{"name":"X","modelConfig":{"modelName":"default"}}""");
        check("Default model name is omitted", CursorAgentActivity.ModelName(onlyDefault.RootElement) == "");

        var desktop = new FakeDesktop();
        var idle = CursorAgentActivity.Read(desktop, Path.Combine(Path.GetTempPath(), "missing-cursor-state.vscdb"), DateTimeOffset.Now);
        check("Missing database yields idle", !idle.Active && idle.Note is null);

        desktop.Headers = [["c-active"]];
        desktop.Values["composerData:c-active"] = """{"status":"aborted","name":"Live","generatingBubbleIds":[],"modelConfig":{"modelName":"composer-1"}}""";
        desktop.Bubbles["c-active"] = [loading];
        var path = Path.Combine(Path.GetTempPath(), "brimdeck-cursor-agent-test.vscdb");
        File.WriteAllBytes(path, [0]);
        try
        {
            var active = CursorAgentActivity.Read(desktop, path, DateTimeOffset.Now);
            check("Read reports active composer with loading tools", active.Active && active.Name == "Live");
            desktop.Bubbles["c-active"] = [done];
            var after = CursorAgentActivity.Read(desktop, path, DateTimeOffset.Now);
            check("Read reports idle when tools finished", !after.Active);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    private sealed class FakeDesktop : IDesktopSources
    {
        public List<string?[]> Headers = [];
        public Dictionary<string, string> Values = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> Bubbles = new(StringComparer.Ordinal);

        public string? ReadCursorToken(string database) => null;
        public Task<IReadOnlyList<LocalEndpoint>> FindAntigravityAsync(CancellationToken cancellation) => Task.FromResult<IReadOnlyList<LocalEndpoint>>([]);

        public IReadOnlyList<string?[]>? QueryDatabase(string database, string sql)
        {
            if (sql.Contains("composerHeaders", StringComparison.Ordinal)) return Headers;
            if (sql.Contains("unfinishedRunAt", StringComparison.Ordinal)) return [];
            if (sql.Contains("json_extract(value, '$.toolFormerData.status')", StringComparison.Ordinal))
            {
                var id = ComposerId(sql);
                foreach (var bubble in Bubbles.GetValueOrDefault(id) ?? [])
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(bubble);
                    if (CursorAgentActivity.ToolUnfinished(doc.RootElement)) return [[bubble]];
                }
                return [];
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

        private static string ComposerId(string sql)
        {
            const string mark = "bubbleId:";
            int start = sql.IndexOf(mark, StringComparison.Ordinal) + mark.Length;
            int end = sql.IndexOf(':', start);
            return end < 0 ? "" : sql[start..end].Replace("''", "'", StringComparison.Ordinal);
        }
    }
}
