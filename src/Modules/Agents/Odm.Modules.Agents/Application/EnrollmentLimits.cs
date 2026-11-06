using System.Security.Claims;

namespace Odm.Modules.Agents.Application;

/// <summary>
/// Limits for registration tokens created by automation (API keys with the role "Enrollment"): one use, at most 24 hours.
/// Signed-in operators keep all options (reusable tokens for images, longer validity).
/// </summary>
public static class EnrollmentLimits
{
    public const int MaxValidHours = 24;

    /// <summary>True for callers that are neither operator nor admin, i.e. API keys.</summary>
    public static bool Applies(ClaimsPrincipal user) => !user.IsInRole("Operator") && !user.IsInRole("Admin");

    public static string? Problem(CreateTokenRequest request) =>
        request.MaxUses is { } uses && uses != 1 ? "API keys can only create one-time tokens."
        : request.ValidHours is > MaxValidHours ? $"API keys can create tokens valid for at most {MaxValidHours} hours."
        : null;
}
