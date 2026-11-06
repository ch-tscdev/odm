using System.Security.Cryptography;
using System.Text;
using Odm.SharedKernel;

namespace Odm.Modules.Identity.Domain;

/// <summary>
/// API key for automation (e.g. Ansible). A key only has the role <see cref="Role"/>: it may create one-time registration
/// tokens and nothing else. Only the SHA-256 hash of the secret is stored; the plaintext is shown once when the key is created.
/// </summary>
public sealed class ApiKey : Entity
{
    /// <summary>Recognisable prefix (secret scanners, logs). Format: <c>odmk_&lt;id&gt;.&lt;secret&gt;</c>.</summary>
    public const string Prefix = "odmk_";

    /// <summary>The only role an API key has.</summary>
    public const string Role = "Enrollment";

    public const int MaxNameLength = 64;

    /// <summary>The last use is written at most this often, not on every request.</summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    public string Name { get; private set; } = "";
    public string SecretHash { get; private set; } = "";
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }

    private ApiKey() { }

    public static (ApiKey Key, string Plain) Issue(string name, string createdBy, DateTimeOffset now, TimeSpan? validFor)
    {
        var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var key = new ApiKey
        {
            Name = name,
            CreatedBy = createdBy,
            CreatedAt = now,
            ExpiresAt = validFor is { } v ? now + v : null,
            SecretHash = Hash(secret)
        };
        return (key, $"{Prefix}{key.Id:N}.{secret}");
    }

    /// <summary>Splits a presented key into id and secret; false for anything that is not a well-formed key.</summary>
    public static bool TryParse(string? presented, out Guid id, out string secret)
    {
        id = Guid.Empty;
        secret = "";
        if (presented is null || !presented.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = presented.AsSpan(Prefix.Length);
        var dot = rest.IndexOf('.');
        if (dot != 32 || rest.Length - dot - 1 != 64 || !Guid.TryParseExact(rest[..dot], "N", out id))
        {
            return false;
        }

        secret = rest[(dot + 1)..].ToString();
        return true;
    }

    public bool Matches(string secret) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(secret)), Encoding.ASCII.GetBytes(SecretHash));

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } end && now >= end;

    /// <summary>Records the use; returns true if the value changed and must be saved.</summary>
    public bool Touch(DateTimeOffset now)
    {
        if (LastUsedAt is { } last && now - last < TouchInterval)
        {
            return false;
        }

        LastUsedAt = now;
        return true;
    }

    private static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
