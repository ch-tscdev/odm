using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.Agents.Application;
using Odm.Modules.Agents.Domain;
using Odm.Modules.Agents.Infrastructure;
using Odm.SharedKernel;

namespace Odm.Modules.Agents.Endpoints;

/// <summary>Management of registration tokens and agent jobs for the web UI.</summary>
internal static class AdminEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Listing and deleting needs an operator; creating is also open to API keys with the role "Enrollment" (automation).
        var tokens = endpoints.MapGroup("/agents/tokens").WithTags("Agents");

        tokens.MapGet("/", async (AgentsDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var rows = await db.Tokens.AsNoTracking().OrderByDescending(t => t.CreatedAt).Take(200).ToListAsync(ct);
            return Results.Ok(rows.Select(t =>
                new TokenDto(
                    t.Id, t.Label, t.CreatedAt, t.ExpiresAt, t.UsedAt, t.StatusAt(now), t.MaxUses, Math.Max(t.UseCount, t.IsUsedUp && t.MaxUses == 1 ? 1 : 0))));
        }).RequireAuthorization(Policies.Operator);

        tokens.MapPost("/", async (CreateTokenRequest request, HttpContext http, AgentsDbContext db, CancellationToken ct) =>
        {
            // Automation only gets one-time tokens with a short life: a leaked token can register at most one device.
            if (EnrollmentLimits.Applies(http.User) && EnrollmentLimits.Problem(request) is { } limit)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["token"] = [limit] });
            }

            var label = request.Label?.Trim() ?? "";
            if (label.Length is 0 or > 128)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["label"] = ["Label is required (at most 128 characters)."]
                });
            }

            var hours = Math.Clamp(request.ValidHours ?? 24, 1, 24 * 365);
            var (token, plain) = RegistrationToken.Issue(label, TimeSpan.FromHours(hours), request.MaxUses ?? 1);
            db.Tokens.Add(token);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/agents/tokens/{token.Id}",
                new TokenCreatedDto(token.Id, token.Label, plain, token.ExpiresAt));
        }).RequireAuthorization(Policies.Enrollment);

        tokens.MapDelete("/{id:guid}", async (Guid id, AgentsDbContext db, CancellationToken ct) =>
        {
            var deleted = await db.Tokens.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        }).RequireAuthorization(Policies.Operator);

        // Remove a device's certificate binding (lost key, expired certificate): the agent then obtains
        // a new certificate with its secret.
        endpoints.MapDelete("/agents/devices/{deviceId:guid}/certificate", async (Guid deviceId, AgentsDbContext db, CancellationToken ct) =>
            {
                var updated = await db.Credentials
                    .Where(c => c.DeviceId == deviceId && c.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.CertThumbprint, (string?)null)
                        .SetProperty(c => c.CertNotAfter, (DateTimeOffset?)null), ct);
                return updated == 0 ? Results.NotFound() : Results.NoContent();
            })
            .WithTags("Agents")
            .RequireAuthorization(Policies.Admin);

        var jobs = endpoints.MapGroup("/agents/jobs").WithTags("Agents")
            .RequireAuthorization(Policies.Operator);

        jobs.MapGet("/", async (Guid deviceId, int? take, AgentsDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Jobs.AsNoTracking()
                .Where(j => j.DeviceId == deviceId)
                .OrderByDescending(j => j.CreatedAt)
                .Take(Math.Clamp(take ?? 20, 1, 100))
                .ToListAsync(ct);
            return Results.Ok(rows.Select(ToView));
        });

        jobs.MapPost("/", async (CreateJobRequest request, AgentsDbContext db, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (request.Type is null || !JobTypes.UserCreatable.Contains(request.Type)) errors["type"] = ["Unknown job type."];
            if (request.Payload is { Length: > 4000 }) errors["payload"] = ["At most 4000 characters."];
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            // Without a registered agent the job would never be delivered.
            var hasAgent = await db.Credentials.AnyAsync(c => c.DeviceId == request.DeviceId && c.RevokedAt == null, ct);
            if (!hasAgent)
            {
                return Results.Conflict();
            }

            var job = AgentJob.Create(request.DeviceId, request.Type!, request.Payload);
            db.Jobs.Add(job);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/agents/jobs/{job.Id}", ToView(job));
        });
    }

    private static JobViewDto ToView(AgentJob j) =>
        new(j.Id, j.DeviceId, j.Type, j.Status.ToString(), j.CreatedAt, j.CompletedAt, j.Output);
}
