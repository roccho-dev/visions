using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Visions.Core;

namespace Visions.Viewer;

public sealed class LocalViewer : IAsyncDisposable
{
    private readonly WebApplication app;
    public string Url { get; }
    private LocalViewer(WebApplication app, string url) { this.app = app; Url = url; }

    public static async Task<LocalViewer> StartAsync(Func<string?> selectedCapture)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var prefix = "/s/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        app.Use(async (context, next) =>
        {
            var req = context.Request;
            var origin = req.Headers.Origin.ToString();
            bool hostOk = req.Host.Host == "127.0.0.1";
            bool originOk = origin.Length == 0 || origin == $"http://{req.Host}";
            bool peerOk = context.Connection.RemoteIpAddress is { } peer && IPAddress.IsLoopback(peer);
            if (!hostOk || !originOk || !peerOk || req.Headers["Sec-Fetch-Site"] == "cross-site")
            { context.Response.StatusCode = 403; return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            // ui/maxGraph owns inline layout styles; scripts remain same-origin and eval is forbidden.
            var styles = req.Path == prefix + "/process-lens.html" ? "'self' 'unsafe-inline'" : "'self'";
            context.Response.Headers["Content-Security-Policy"] =
                $"default-src 'none'; script-src 'self'; style-src {styles}; img-src data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            await next(context);
        });
        // Only the build's exact embedded manifest is routable. No filesystem or arbitrary source path is exposed.
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith("Assets/", StringComparison.Ordinal)))
        {
            var name = resource["Assets/".Length..].Replace('\\', '/');
            if (name.Split('/').Any(part => part is "" or "." or "..")) throw new InvalidOperationException("invalid_asset");
            var type = Path.GetExtension(name) switch
            {
                ".html" => "text/html; charset=utf-8",
                ".js" or ".mjs" => "text/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                _ => "text/plain; charset=utf-8"
            };
            using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("missing_asset");
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            app.MapGet(prefix + (name == "index.html" ? "/" : "/" + name), () => Results.Text(content, type));
        }
        app.MapGet(prefix + "/model", () => Results.Json(Projection.Build(Journal.Read(selectedCapture())), Contract.Json));
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new(app, address + prefix + "/");
    }
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await app.StopAsync(timeout.Token);
        await app.DisposeAsync();
    }
}
