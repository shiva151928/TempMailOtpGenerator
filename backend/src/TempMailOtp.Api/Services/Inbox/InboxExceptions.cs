namespace TempMailOtp.Api.Services.Inbox;

/// <summary>The inbox does not exist, was deleted, or the access token does not match (indistinguishable on purpose).</summary>
public sealed class InboxNotFoundException()
    : Exception("This inbox does not exist, was deleted, or the access token is not valid.");

/// <summary>No access token was supplied.</summary>
public sealed class InboxTokenMissingException()
    : Exception("Send the inbox access token in the X-Inbox-Token header.");

public sealed class InboxExpiredException(DateTime expiresAt)
    : Exception("This inbox has expired. Generate a new address to keep receiving email.")
{
    public DateTime ExpiresAt { get; } = expiresAt;
}

public sealed class MessageNotFoundException()
    : Exception("This message does not exist in the inbox.");

/// <summary>A demo-only endpoint was called while a real provider is configured.</summary>
public sealed class DemoModeRequiredException()
    : Exception("This action is only available in demo mode.");
