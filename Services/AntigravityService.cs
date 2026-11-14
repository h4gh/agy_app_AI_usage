using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AiUsageMonitor.Models;

namespace AiUsageMonitor.Services;

public sealed class AntigravityService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<AccountUsage> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var daemonDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".gemini", "antigravity", "daemon");

            if (!Directory.Exists(daemonDir))
                return AccountUsage.Unavailable("Antigravity", "Složka daemon nenalezena");

            var livePids = Process.GetProcessesByName("language_server")
                .Select(p =>
                {
                    try { return p.Id; }
                    finally { p.Dispose(); }
                })
                .ToHashSet();

            var hubFiles = new DirectoryInfo(daemonDir)
                .GetFiles("hub_*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(10)
                .ToList();

            foreach (var file in hubFiles)
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file.FullName, ct));
                var root = doc.RootElement;
                if (!root.TryGetProperty("pid", out var pidEl) ||
                    !root.TryGetProperty("httpPort", out var portEl) ||
                    !root.TryGetProperty("csrfToken", out var csrfEl))
                    continue;

                var pid = pidEl.GetInt32();
                if (livePids.Count > 0 && !livePids.Contains(pid))
                    continue;

                var port = portEl.GetInt32();
                var csrf = csrfEl.GetString();
                if (string.IsNullOrWhiteSpace(csrf))
                    continue;

                var result = await QueryHubAsync(port, csrf, ct);
                if (result is not null)
                    return result;
            }

            return AccountUsage.Unavailable("Antigravity", "Antigravity neběží");
        }
        catch (Exception ex)
        {
            return AccountUsage.Unavailable("Antigravity", $"Chyba: {ex.Message}");
        }
    }

    private static async Task<AccountUsage?> QueryHubAsync(int port, string csrfToken, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                $"http://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/GetUserStatus");

            req.Headers.Add("X-Codeium-Csrf-Token", csrfToken);
            req.Headers.Add("Connect-Protocol-Version", "1");
            req.Content = new StringContent(
                "{\"metadata\":{\"ideName\":\"antigravity\",\"extensionName\":\"antigravity\",\"locale\":\"en\"}}",
                Encoding.UTF8,
                "application/json");

            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("userStatus", out var userStatus) ||
                !userStatus.TryGetProperty("cascadeModelConfigData", out var configData) ||
                !configData.TryGetProperty("clientModelConfigs", out var configs))
                return null;

            var modelsByFamily = new Dictionary<string, ModelQuota>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in configs.EnumerateArray())
            {
                if (!item.TryGetProperty("label", out var labelEl) ||
                    !item.TryGetProperty("quotaInfo", out var quotaEl) ||
                    !quotaEl.TryGetProperty("remainingFraction", out var remEl))
                    continue;

                var rawLabel = labelEl.GetString() ?? "";
                var family = NormalizeFamily(rawLabel);
                if (modelsByFamily.ContainsKey(family))
                    continue;

                var remaining = Math.Clamp(remEl.GetDouble(), 0.0, 1.0);
                var usedPct = Math.Round((1.0 - remaining) * 100.0, 1);

                DateTimeOffset? resetUtc = null;
                if (quotaEl.TryGetProperty("resetTime", out var resetEl) &&
                    DateTimeOffset.TryParse(resetEl.GetString(), out var parsedReset))
                {
                    resetUtc = parsedReset;
                }

                modelsByFamily[family] = new ModelQuota(family, usedPct, resetUtc);
            }

            if (modelsByFamily.Count == 0)
                return AccountUsage.Unavailable("Antigravity", "Žádná kvóta k zobrazení");

            // Hydrogen uses the short (~5h) reset window; Argon uses the multi-day/weekly window.
            var hydrogen = modelsByFamily.Values.FirstOrDefault(m =>
                m.Name.Contains("Hydrogen", StringComparison.OrdinalIgnoreCase));
            var argon = modelsByFamily.Values.FirstOrDefault(m =>
                m.Name.Contains("Argon", StringComparison.OrdinalIgnoreCase));

            var fiveHour = hydrogen ?? modelsByFamily.Values.First();
            var weekly = argon ?? modelsByFamily.Values.Last();

            return new AccountUsage(
                PlatformName: "Antigravity",
                IsAvailable: true,
                StatusMessage: null,
                FiveHourUsedPercent: fiveHour.UsedPercent,
                FiveHourResetUtc: fiveHour.ResetUtc,
                WeeklyUsedPercent: weekly.UsedPercent,
                WeeklyResetUtc: weekly.ResetUtc,
                Models: modelsByFamily.Values.ToList(),
                UpdatedAtUtc: DateTimeOffset.UtcNow);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeFamily(string label)
    {
        var idx = label.IndexOf(" (", StringComparison.Ordinal);
        return idx > 0 ? label[..idx].Trim() : label.Trim();
    }
}
