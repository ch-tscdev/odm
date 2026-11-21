using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Odm.Modules.Software.Application;

/// <summary>
/// Short-lived, signed download links for PSADT package versions. The web UI sends its token in the Authorization
/// header, which a plain browser download cannot do; streaming a multi-gigabyte ZIP through JavaScript would keep it in
/// memory. So an operator first requests a link (authorized and audited), and the browser then downloads it natively.
/// The link is bound to one version and expires after <see cref="Lifetime"/>. Stateless, so it works with several
/// server instances. The HMAC key is derived from the secrets master key and is used for nothing else.
/// Format (Base64url): version ID (16 bytes) | expiry in Unix seconds (8 bytes) | HMAC-SHA256 (32 bytes).
/// </summary>
public sealed partial class PackageDownloadTokens(IOptions<SecretsOptions> options, TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private const int PayloadSize = 16 + 8;
    private const int MacSize = 32;

    private readonly byte[] _key = HMACSHA256.HashData(
        Convert.FromBase64String(options.Value.MasterKey), Encoding.UTF8.GetBytes("odm-package-download"));

    public (string Token, DateTimeOffset ExpiresAt) Create(Guid versionId)
    {
        var expiresAt = time.GetUtcNow().Add(Lifetime);
        var buffer = new byte[PayloadSize + MacSize];
        versionId.TryWriteBytes(buffer.AsSpan(0, 16));
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(16, 8), expiresAt.ToUnixTimeSeconds());
        HMACSHA256.HashData(_key, buffer.AsSpan(0, PayloadSize), buffer.AsSpan(PayloadSize));
        return (Base64Url.EncodeToString(buffer), expiresAt);
    }

    /// <summary>"Package name-version.zip" with every run of characters other than letters, digits and . _ - replaced by "_".</summary>
    public static string FileName(string packageName, string version)
    {
        var name = FileNameUnsafe().Replace($"{packageName}-{version}", "_").Trim('.', '_');
        return (name.Length == 0 ? "package" : name[..Math.Min(name.Length, 120)]) + ".zip";
    }

    [GeneratedRegex(@"[^A-Za-z0-9._\-]+")]
    private static partial Regex FileNameUnsafe();

    /// <summary>True if the token is authentic, belongs to this version and has not expired.</summary>
    public bool IsValid(string? token, Guid versionId)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128)
        {
            return false;
        }

        byte[] buffer;
        try
        {
            buffer = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            return false;
        }

        if (buffer.Length != PayloadSize + MacSize)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[MacSize];
        HMACSHA256.HashData(_key, buffer.AsSpan(0, PayloadSize), expected);
        if (!CryptographicOperations.FixedTimeEquals(expected, buffer.AsSpan(PayloadSize)))
        {
            return false;
        }

        var expiresAt = BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(16, 8));
        return new Guid(buffer.AsSpan(0, 16)) == versionId && time.GetUtcNow().ToUnixTimeSeconds() <= expiresAt;
    }
}
