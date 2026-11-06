using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.Identity.Domain;
using Odm.Modules.Identity.Infrastructure;
using Odm.SharedKernel;

namespace Odm.Modules.Identity.Endpoints;

public sealed record ApiKeyDto(
    Guid Id, string Name, string Role, string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt, bool Expired);

public sealed record CreateApiKeyRequest(string? Name, int? ValidDays);

/// <summary>One-time response on creation: the only one that contains the plaintext key.</summary>
public sealed record ApiKeyCreatedDto(Guid Id, string Name, string Key, DateTimeOffset? ExpiresAt);

/// <summary>Management of API keys (admins only).</summary>
public static class ApiKeyEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api-keys").WithTags("Auth").RequireAuthorization(Policies.Admin);

        group.MapGet("/", async (IdentityDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var keys = await db.ApiKeys.AsNoTracking().OrderBy(k => k.Name).ToListAsync(ct);
            return Results.Ok(keys.Select(k => ToDto(k, now)));
        });

        group.MapPost("/", async (CreateApiKeyRequest request, HttpContext http, IdentityDbContext db, CancellationToken ct) =>
        {
            var name = request.Name?.Trim() ?? "";
            if (name.Length is 0 or > ApiKey.MaxNameLength)
            {
                return Invalid("name", $"Name is required (at most {ApiKey.MaxNameLength} characters).");
            }

            if (request.ValidDays is < 1 or > 3650)
            {
                return Invalid("validDays", "Validity must be between 1 and 3650 days (empty = no expiry).");
            }

            if (await db.ApiKeys.AnyAsync(k => k.Name == name, ct))
            {
                return Invalid("name", "An API key with this name already exists.", StatusCodes.Status409Conflict);
            }

            var createdBy = http.User.FindFirst("name")?.Value ?? "";
            var validFor = request.ValidDays is { } days ? TimeSpan.FromDays(days) : (TimeSpan?)null;
            var (key, plain) = ApiKey.Issue(name, createdBy, DateTimeOffset.UtcNow, validFor);
            db.ApiKeys.Add(key);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/api-keys/{key.Id}", new ApiKeyCreatedDto(key.Id, key.Name, plain, key.ExpiresAt));
        });

        group.MapDelete("/{id:guid}", async (Guid id, IdentityDbContext db, CancellationToken ct) =>
        {
            var deleted = await db.ApiKeys.Where(k => k.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static ApiKeyDto ToDto(ApiKey k, DateTimeOffset now) =>
        new(k.Id, k.Name, ApiKey.Role, k.CreatedBy, k.CreatedAt, k.ExpiresAt, k.LastUsedAt, k.IsExpired(now));

    private static IResult Invalid(string field, string message, int status = StatusCodes.Status400BadRequest) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] }, statusCode: status);
}
