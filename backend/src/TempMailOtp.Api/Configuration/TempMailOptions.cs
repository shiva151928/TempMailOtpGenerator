using System.ComponentModel.DataAnnotations;

namespace TempMailOtp.Api.Configuration;

/// <summary>Bound from the "TempMail" configuration section.</summary>
public sealed class TempMailOptions
{
    public const string SectionName = "TempMail";

    /// <summary>How long an inbox stays usable after it is generated.</summary>
    [Range(1, 1440)]
    public int InboxLifetimeMinutes { get; set; } = 60;

    /// <summary>How often the browser polls for new messages.</summary>
    [Range(3, 300)]
    public int ClientPollIntervalSeconds { get; set; } = 8;

    /// <summary>Minimum time between automatic provider syncs for one inbox (protects the provider).</summary>
    [Range(1, 300)]
    public int SyncIntervalSeconds { get; set; } = 5;

    /// <summary>Minimum time between manual refreshes for one inbox.</summary>
    [Range(0, 60)]
    public int ManualRefreshMinIntervalSeconds { get; set; } = 2;

    /// <summary>Maximum new messages downloaded per sync.</summary>
    [Range(1, 100)]
    public int MaxMessagesPerSync { get; set; } = 25;

    /// <summary>Whether the background cleanup of expired inboxes runs.</summary>
    public bool CleanupEnabled { get; set; } = true;

    [Range(1, 1440)]
    public int CleanupIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Expired inboxes are kept this long (returning 410 Gone) before they and their emails are deleted.
    /// </summary>
    [Range(0, 1440)]
    public int ExpiredRetentionMinutes { get; set; } = 15;

    /// <summary>Origins allowed by CORS (the Angular app). No trailing slashes.</summary>
    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>Bound from the "RateLimiting" configuration section. Limits are per client IP address.</summary>
public sealed class ApiRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int GeneratePermitLimit { get; set; } = 10;

    public int GenerateWindowSeconds { get; set; } = 600;

    public int RefreshPermitLimit { get; set; } = 30;

    public int RefreshWindowSeconds { get; set; } = 60;

    public int GlobalPermitLimit { get; set; } = 300;

    public int GlobalWindowSeconds { get; set; } = 60;
}
