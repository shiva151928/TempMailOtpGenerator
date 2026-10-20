using TempMailOtp.Api.Data.Entities;
using TempMailOtp.Api.Providers;

namespace TempMailOtp.Api.Services.Inbox;

internal static class InboxMailboxMapper
{
    /// <summary>Builds the provider view of an inbox, decrypting its stored credentials.</summary>
    public static ProviderMailbox ToProviderMailbox(this TemporaryInbox inbox, IProviderSecretProtector protector) =>
        new(inbox.EmailAddress, inbox.ProviderAccountId, protector.Unprotect(inbox.ProtectedProviderSecret));
}
