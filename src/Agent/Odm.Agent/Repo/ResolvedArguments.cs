using Odm.Agent.Jobs;

namespace Odm.Agent.Repo;

/// <summary>
/// Additional arguments and environment variables of a job: arguments split, secrets substituted, secret values kept
/// for output redaction.
/// </summary>
public sealed record ResolvedArguments(
    IReadOnlyList<string> Tokens, IReadOnlyCollection<string> SecretValues,
    IReadOnlyDictionary<string, string>? Environment = null)
{
    /// <summary>
    /// Split first, then substitute: this way a secret containing spaces or quotation marks stays a single argument.
    /// Secret values are only fetched from the server now, not delivered with the job.
    /// </summary>
    public static Task<ResolvedArguments> ResolveAsync(
        string? template, ServerJob job, JobContext context, CancellationToken ct) =>
        ResolveAsync(template, null, job, context, ct);

    /// <param name="environment">NAME=value per line (validated by the server); null = no own variables.</param>
    public static async Task<ResolvedArguments> ResolveAsync(
        string? template, string? environment, ServerJob job, JobContext context, CancellationToken ct)
    {
        var tokens = ArgumentSplitter.Split(template);
        var variables = ParseEnvironment(environment);
        if (!tokens.Any(SecretSubstitution.HasReferences) && !variables.Values.Any(SecretSubstitution.HasReferences))
        {
            return new ResolvedArguments(tokens, [], variables);
        }

        var secrets = await context.Server.ResolveSecretsAsync(context.State, job.Id, ct);
        var resolved = tokens.Select(t => SecretSubstitution.Apply(t, secrets)).ToList();
        var resolvedVariables = variables.ToDictionary(v => v.Key, v => SecretSubstitution.Apply(v.Value, secrets), StringComparer.Ordinal);
        return new ResolvedArguments(resolved, secrets.Values.ToList(), resolvedVariables);
    }

    /// <summary>One NAME=value per line; lines without "=" or without a name are skipped.</summary>
    public static Dictionary<string, string> ParseEnvironment(string? text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in (text ?? "").Split('\n'))
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        return result;
    }
}
