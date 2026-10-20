namespace TempMailOtp.Api.Providers;

public enum EmailProviderErrorKind
{
    /// <summary>The provider could not be reached or returned a server error.</summary>
    Unavailable,

    /// <summary>The provider (or our outbound limiter) is throttling requests.</summary>
    RateLimited,

    /// <summary>The provider rejected our request or returned something we could not understand.</summary>
    InvalidResponse,

    /// <summary>The mailbox no longer exists at the provider.</summary>
    MailboxNotFound,

    /// <summary>Stored credentials for the mailbox could not be used.</summary>
    CredentialsUnavailable,
}

/// <summary>
/// A failure talking to the email provider. <see cref="Exception.Message"/> is safe to show to users;
/// technical details are kept in the inner exception for logs.
/// </summary>
public sealed class EmailProviderException : Exception
{
    public EmailProviderException(string message, EmailProviderErrorKind kind, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public EmailProviderErrorKind Kind { get; }

    public TimeSpan? RetryAfter { get; init; }
}
