namespace AiUsageMonitor.Models;

public sealed record ModelQuota(
    string Name,
    double UsedPercent,
    DateTimeOffset? ResetUtc
);

public sealed record AccountUsage(
    string PlatformName,
    bool IsAvailable,
    string? StatusMessage,
    double? FiveHourUsedPercent,
    DateTimeOffset? FiveHourResetUtc,
    double? WeeklyUsedPercent,
    DateTimeOffset? WeeklyResetUtc,
    IReadOnlyList<ModelQuota> Models,
    DateTimeOffset UpdatedAtUtc
)
{
    public static AccountUsage Unavailable(string platformName, string message) =>
        new(platformName, false, message, null, null, null, null, [], DateTimeOffset.UtcNow);
}
