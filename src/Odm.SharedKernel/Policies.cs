namespace Odm.SharedKernel;

/// <summary>Names of the authorization policies. Defined in the Identity module, used by all modules.</summary>
public static class Policies
{
    public const string Operator = "Operator";
    public const string Admin = "Admin";

    /// <summary>
    /// Create registration tokens: operators and admins (signed in), or an API key with the role "Enrollment".
    /// The only policy that accepts API keys.
    /// </summary>
    public const string Enrollment = "Enrollment";

    /// <summary>Authenticated agent (own scheme, not for users).</summary>
    public const string Agent = "Agent";
}
