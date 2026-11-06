using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Odm.Modules.Agents;
using Odm.Modules.Audit;
using Odm.Modules.Agents.Infrastructure;
using Odm.Modules.Devices;
using Odm.Modules.Identity;
using Odm.Modules.Notifications;
using Odm.Modules.OsUpdates;
using Odm.Modules.Software;
using Odm.SharedKernel;
using Odm.SharedKernel.Net;
using Odm.Host;
using Odm.SharedKernel.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Optional: HTTPS directly at ODM (needed for agent client certificates without a proxy). Tls:CertificateFile and
// Tls:KeyFile are PEM files; ODM then listens on 8080 (HTTP) and Tls:Port (HTTPS, default 8443).
if (builder.Configuration["Tls:CertificateFile"] is { Length: > 0 } certificateFile)
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Kestrel:Endpoints:Http:Url"] = "http://+:8080",
        ["Kestrel:Endpoints:Https:Url"] = $"https://+:{builder.Configuration.GetValue("Tls:Port", 8443)}",
        ["Kestrel:Endpoints:Https:Certificate:Path"] = certificateFile,
        ["Kestrel:Endpoints:Https:Certificate:KeyPath"] = builder.Configuration["Tls:KeyFile"]
    });
}

// Order = initialization order. New modules are registered here.
IModule[] modules =
[
    new IdentityModule(),
    new AuditModule(),
    new DevicesModule(),
    new AgentsModule(),
    new SoftwareModule(),
    new NotificationsModule(),
    new OsUpdatesModule()
];

builder.Services
    .AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "Database:ConnectionString is missing.")
    .ValidateOnStart();

// mTLS for agents: HTTPS endpoints request a client certificate but do not require it (browsers without a certificate
// keep working). Whether a certificate is valid and the right one is checked by the agent authentication (Agents module).
var mtlsMode = builder.Configuration["Agents:Mtls:Mode"];
if (!string.IsNullOrWhiteSpace(mtlsMode) && !mtlsMode.Equals("Off", StringComparison.OrdinalIgnoreCase))
{
    builder.WebHost.ConfigureKestrel(kestrel => kestrel.ConfigureHttpsDefaults(https =>
    {
        https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
        https.ClientCertificateValidation = (_, _, _) => true;
    }));
}

// Behind a reverse proxy (Proxy:ForwardedHeaders=true) client address and scheme come from X-Forwarded-For / -Proto.
// Proxy:TrustedProxies (addresses or CIDR ranges) names the peers that are believed. Without it every peer would be
// believed, so anyone reaching the port directly could fake the client address (rate limits, audit log); the server
// refuses to start in that case unless Proxy:TrustAllProxies=true is set deliberately (e.g. a pod only reachable via
// the ingress). Forwarding a client certificate (Agents:Mtls:ForwardedCertificateHeader) always requires TrustedProxies.
var forwardedHeaders = builder.Configuration.GetValue<bool>("Proxy:ForwardedHeaders");
if (forwardedHeaders)
{
    var trusted = TrustedProxies.FromConfiguration(builder.Configuration);
    var trustAll = builder.Configuration.GetValue<bool>("Proxy:TrustAllProxies");
    if (trusted.Networks.Count == 0 && !trustAll)
    {
        throw new InvalidOperationException(
            "Proxy:ForwardedHeaders is on but Proxy:TrustedProxies is empty, so every client could fake its address. " +
            "Set Proxy__TrustedProxies__0 to the address or network of the reverse proxy " +
            "(or Proxy__TrustAllProxies=true if the server is only reachable through the proxy).");
    }

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in trusted.Networks)
        {
            options.KnownIPNetworks.Add(network);
        }
    });
}

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

foreach (var module in modules)
{
    module.ConfigureServices(builder.Services, builder.Configuration);
}

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler();

if (forwardedHeaders && builder.Configuration.GetValue<bool>("Proxy:TrustAllProxies") &&
    TrustedProxies.FromConfiguration(builder.Configuration).Networks.Count == 0)
{
    app.Logger.LogWarning(
        "Proxy:TrustAllProxies is set: X-Forwarded-For is believed from every peer. Make sure the server is reachable only through the proxy.");
}

// Remember the directly connected peer before forwarded headers replace the remote address: only trusted proxies
// may hand on a client certificate.
app.Use((context, next) =>
{
    context.Items[ClientCertificateProvider.PeerItemKey] = context.Connection.RemoteIpAddress;
    return next(context);
});

if (forwardedHeaders)
{
    app.UseForwardedHeaders();
}

// After the forwarded headers, so that HSTS is also sent when TLS ends at the proxy.
// Security headers for every response (the UI is a single-page app served from the same origin).
app.Use((context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    headers["Content-Security-Policy"] = SecurityHeaders.ContentSecurityPolicy;
    if (context.Request.IsHttps)
    {
        headers["Strict-Transport-Security"] = "max-age=31536000";
    }

    return next(context);
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

var api = app.MapGroup("/api");
foreach (var module in modules)
{
    module.MapEndpoints(api);
}

// Version and runtime information for administrators (shown in the UI).
api.MapGet("/system/info", (IConfiguration configuration) =>
    {
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "?";
        var plus = version.IndexOf('+');
        return Results.Ok(new
        {
            Version = plus > 0 ? version[..plus] : version,
            Runtime = RuntimeInformation.FrameworkDescription,
            DatabaseProvider = configuration["Database:Provider"] ?? "Postgres",
        });
    })
    .RequireAuthorization(Odm.SharedKernel.Policies.Admin);

api.MapFallback(() => Results.NotFound());

// SPA-Fallback (Production: Frontend liegt in wwwroot)
app.MapFallbackToFile("index.html");

foreach (var module in modules)
{
    await module.InitializeAsync(app.Services, app.Lifetime.ApplicationStopping);
}

app.Run();
