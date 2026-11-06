namespace Odm.Host;

/// <summary>Values of the security headers that every response carries (see Program.cs).</summary>
public static class SecurityHeaders
{
    /// <summary>
    /// Only resources from the server itself. Styles allow 'unsafe-inline' because the UI sets style attributes (e.g. widths);
    /// scripts never run inline. No framing, no plugins, no foreign form targets.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
}
