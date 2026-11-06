namespace Odm.SharedKernel.Text;

/// <summary>
/// Lists in the configuration may also be given as one comma- or semicolon-separated value, so that a single variable in
/// <c>.env</c> can hold several entries (e.g. <c>ODM_SMTP_TO=a@x.ch, b@x.ch</c>). Empty entries are dropped.
/// </summary>
public static class ConfigList
{
    public static string[] Split(IEnumerable<string>? values) =>
        (values ?? [])
            .SelectMany(v => (v ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
