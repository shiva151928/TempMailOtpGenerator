namespace TempMailOtp.Api.Data.Entities;

/// <summary>
/// The OTP detected in a received email. At most one per email.
/// </summary>
public class DetectedOtp
{
    public Guid Id { get; set; }

    public Guid ReceivedEmailId { get; set; }

    public ReceivedEmail ReceivedEmail { get; set; } = null!;

    public string Code { get; set; } = string.Empty;

    /// <summary>Heuristic confidence between 0 and 1.</summary>
    public double Confidence { get; set; }

    /// <summary>The verification phrase nearest to the code, if any (for example "verification code").</summary>
    public string? MatchedPhrase { get; set; }

    /// <summary>Where the code was found: "subject" or "body".</summary>
    public string Source { get; set; } = string.Empty;

    public DateTime DetectedAt { get; set; }
}
