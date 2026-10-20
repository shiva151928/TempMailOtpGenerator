namespace TempMailOtp.Api.Configuration;

public static class EmailProviderNames
{
    public const string MailTm = "MailTm";
    public const string Demo = "Demo";

    public static bool IsKnown(string? name) => IsMailTm(name) || IsDemo(name);

    public static bool IsMailTm(string? name) => string.Equals(name, MailTm, StringComparison.OrdinalIgnoreCase);

    public static bool IsDemo(string? name) => string.Equals(name, Demo, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Bound from the "EmailProvider" configuration section.</summary>
public sealed class EmailProviderOptions
{
    public const string SectionName = "EmailProvider";

    /// <summary>
    /// "MailTm" (default, real email via the public mail.tm API) or "Demo"
    /// (clearly labeled simulation that never receives real email).
    /// </summary>
    public string Provider { get; set; } = EmailProviderNames.MailTm;

    public MailTmOptions MailTm { get; set; } = new();
}

/// <summary>
/// Settings for mail.tm. Any service exposing the same API (for example mail.gw) can be used by changing <see cref="BaseUrl"/>.
/// mail.tm needs no API key: each inbox is a mail.tm account whose random password is stored encrypted on the server.
/// </summary>
public sealed class MailTmOptions
{
    public string BaseUrl { get; set; } = "https://api.mail.tm/";

    public string DisplayName { get; set; } = "mail.tm";

    /// <summary>mail.tm's terms require a visible link back to mail.tm.</summary>
    public string AttributionText { get; set; } = "Temporary addresses powered by mail.tm";

    public string AttributionUrl { get; set; } = "https://mail.tm";

    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Outbound request budget. mail.tm allows 8 requests per second per IP address.</summary>
    public int MaxRequestsPerSecond { get; set; } = 6;

    public int DomainCacheMinutes { get; set; } = 30;
}
