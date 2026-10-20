using System.Security.Cryptography;
using System.Text;

namespace TempMailOtp.Api.Services.Inbox;

/// <summary>
/// Inbox identifiers and access tokens. Both are cryptographically random; only a SHA-256 hash of the
/// token is stored, and verification uses a constant-time comparison.
/// </summary>
public static class InboxTokens
{
    public const int PublicIdLength = 22; // 16 random bytes, base64url without padding
    private const int MaxTokenLength = 128;

    public static string CreatePublicId() => ToBase64Url(RandomNumberGenerator.GetBytes(16));

    public static string CreateAccessToken() => ToBase64Url(RandomNumberGenerator.GetBytes(32));

    public static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static bool VerifyToken(string? token, byte[] expectedHash)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength || expectedHash.Length != 32)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(HashToken(token), expectedHash);
    }

    public static bool IsValidPublicId(string? publicId) =>
        publicId is { Length: PublicIdLength }
        && publicId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
