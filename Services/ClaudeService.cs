using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using AiUsageMonitor.Models;

namespace AiUsageMonitor.Services;

public sealed class ClaudeService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public async Task<AccountUsage> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var credPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", ".credentials.json");

            if (!File.Exists(credPath))
                return AccountUsage.Unavailable("Claude Code", "Spusť v terminálu: claude auth login");

            using var credDoc = JsonDocument.Parse(await File.ReadAllTextAsync(credPath, ct));
            if (!credDoc.RootElement.TryGetProperty("claudeAiOauth", out var oauthEl) ||
                !oauthEl.TryGetProperty("accessToken", out var tokenEl))
                return AccountUsage.Unavailable("Claude Code", "Spusť v terminálu: claude auth login");

            var token = tokenEl.GetString();
            if (string.IsNullOrWhiteSpace(token))
                return AccountUsage.Unavailable("Claude Code", "Spusť v terminálu: claude auth login");

            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            req.Headers.Add("User-Agent", "claude-code/2.0");

            using var resp = await Http.SendAsync(req, ct);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return AccountUsage.Unavailable("Claude Code", "Token vypršel — spusť claude auth login");

            if (!resp.IsSuccessStatusCode)
                return AccountUsage.Unavailable("Claude Code", $"HTTP {(int)resp.StatusCode}");

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var (fivePct, fiveReset) = ParseBucket(root, "five_hour");
            var (weekPct, weekReset) = ParseBucket(root, "seven_day");

            var models = new List<ModelQuota>();
            if (fivePct.HasValue)
                models.Add(new ModelQuota("5h Session", fivePct.Value, fiveReset));
            if (weekPct.HasValue)
                models.Add(new ModelQuota("Weekly (All)", weekPct.Value, weekReset));

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name.StartsWith("seven_day_", StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.ValueKind == JsonValueKind.Object)
                {
                    var (pct, reset) = ParseBucket(root, prop.Name);
                    if (pct.HasValue)
                    {
                        var niceName = "Weekly (" + char.ToUpperInvariant(prop.Name[10]) + prop.Name[11..] + ")";
                        models.Add(new ModelQuota(niceName, pct.Value, reset));
                    }
                }
            }

            return new AccountUsage(
                PlatformName: "Claude Code",
                IsAvailable: fivePct.HasValue || weekPct.HasValue,
                StatusMessage: null,
                FiveHourUsedPercent: fivePct,
                FiveHourResetUtc: fiveReset,
                WeeklyUsedPercent: weekPct,
                WeeklyResetUtc: weekReset,
                Models: models,
                UpdatedAtUtc: DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return AccountUsage.Unavailable("Claude Code", $"Chyba: {ex.Message}");
        }
    }

    private static (double? UsedPct, DateTimeOffset? ResetUtc) ParseBucket(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var bucket) || bucket.ValueKind != JsonValueKind.Object)
            return (null, null);

        double? pct = null;
        if (bucket.TryGetProperty("utilization", out var utilEl) && utilEl.ValueKind == JsonValueKind.Number)
            pct = Math.Clamp(Math.Round(utilEl.GetDouble(), 1), 0, 100);

        DateTimeOffset? reset = null;
        if (bucket.TryGetProperty("resets_at", out var resetEl) &&
            resetEl.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(resetEl.GetString(), out var parsed))
        {
            reset = parsed;
        }

        return (pct, reset);
    }
}
