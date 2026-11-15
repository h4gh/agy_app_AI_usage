using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiUsageMonitor.Models;

namespace AiUsageMonitor.Services;

public sealed class ClaudeService
{
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private const string TokenEndpoint = "https://platform.claude.com/v1/oauth/token";
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<AccountUsage> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var credPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", ".credentials.json");

            if (!File.Exists(credPath))
                return AccountUsage.Unavailable("Claude Code", "Spusť v terminálu: claude auth login");

            var jsonText = await File.ReadAllTextAsync(credPath, ct);
            var rootNode = JsonNode.Parse(jsonText)?.AsObject();
            var oauthNode = rootNode?["claudeAiOauth"]?.AsObject();
            if (rootNode is null || oauthNode is null)
                return AccountUsage.Unavailable("Claude Code", "Spusť v terminálu: claude auth login");

            var token = oauthNode["accessToken"]?.GetValue<string>();
            var refreshToken = oauthNode["refreshToken"]?.GetValue<string>();
            var expiresAtMs = oauthNode["expiresAt"]?.GetValue<long>() ?? 0;

            if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(refreshToken))
                return AccountUsage.Unavailable("Claude Code", "Spusť v terminálu: claude auth login");

            var isExpiringSoon = expiresAtMs <= DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds();
            if ((string.IsNullOrWhiteSpace(token) || isExpiringSoon) && !string.IsNullOrWhiteSpace(refreshToken))
            {
                var refreshed = await TryRefreshTokenAsync(credPath, rootNode, oauthNode, refreshToken, ct);
                if (refreshed is not null)
                    token = refreshed;
            }

            if (string.IsNullOrWhiteSpace(token))
                return AccountUsage.Unavailable("Claude Code", "Token vypršel — spusť claude auth login");

            var resp = await SendUsageRequestAsync(token, ct);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden &&
                !string.IsNullOrWhiteSpace(refreshToken))
            {
                resp.Dispose();
                var refreshed = await TryRefreshTokenAsync(credPath, rootNode, oauthNode, refreshToken, ct);
                if (refreshed is not null)
                {
                    resp = await SendUsageRequestAsync(refreshed, ct);
                }
                else
                {
                    return AccountUsage.Unavailable("Claude Code", "Token vypršel — spusť claude auth login");
                }
            }

            using (resp)
            {
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
        }
        catch (Exception ex)
        {
            return AccountUsage.Unavailable("Claude Code", $"Chyba: {ex.Message}");
        }
    }

    private static async Task<HttpResponseMessage> SendUsageRequestAsync(string token, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        req.Headers.Add("User-Agent", "claude-code/2.0");
        return await Http.SendAsync(req, ct);
    }

    private static async Task<string?> TryRefreshTokenAsync(
        string credPath,
        JsonObject rootNode,
        JsonObject oauthNode,
        string refreshToken,
        CancellationToken ct)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                grant_type = "refresh_token",
                refresh_token = refreshToken,
                client_id = ClientId
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
            req.Headers.Add("User-Agent", "claude-code/2.0");
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var newAccess = root.TryGetProperty("access_token", out var atEl) ? atEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(newAccess))
                return null;

            oauthNode["accessToken"] = newAccess;
            if (root.TryGetProperty("refresh_token", out var rtEl) && !string.IsNullOrWhiteSpace(rtEl.GetString()))
                oauthNode["refreshToken"] = rtEl.GetString();

            var expiresIn = root.TryGetProperty("expires_in", out var expEl) && expEl.ValueKind == JsonValueKind.Number
                ? expEl.GetDouble()
                : 28800;
            oauthNode["expiresAt"] = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeMilliseconds();

            await File.WriteAllTextAsync(
                credPath,
                rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                ct);

            return newAccess;
        }
        catch
        {
            return null;
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
