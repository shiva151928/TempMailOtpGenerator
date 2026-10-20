using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using TempMailOtp.Api.Providers;

namespace TempMailOtp.Api.Services.Inbox;

public interface IProviderSecretProtector
{
    string? Protect(string? secret);

    string? Unprotect(string? protectedSecret);
}

/// <summary>Encrypts per-inbox provider credentials at rest with ASP.NET Core Data Protection.</summary>
public sealed class ProviderSecretProtector(IDataProtectionProvider dataProtectionProvider) : IProviderSecretProtector
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("TempMailOtp.ProviderSecret.v1");

    public string? Protect(string? secret) => secret is null ? null : _protector.Protect(secret);

    public string? Unprotect(string? protectedSecret)
    {
        if (protectedSecret is null)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedSecret);
        }
        catch (CryptographicException ex)
        {
            throw new EmailProviderException(
                "The stored credentials for this inbox can no longer be decrypted (the server's data protection keys changed). Generate a new address.",
                EmailProviderErrorKind.CredentialsUnavailable,
                ex);
        }
    }
}
