// Copyright © Puneeth DC Ltd. All rights reserved.

using System.Net.Http;
using HtmlLogViewer.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HtmlLogViewer;

/// <summary>
/// Extension methods to register the Puneeth DC GDS Log Viewer into an ASP.NET Core application.
/// </summary>
public static class LogViewerExtensions
{
    /// <summary>
    /// Registers the Log Viewer controller and binds <see cref="LogViewerOptions"/> from the
    /// <c>"LogViewer"</c> section of <c>appsettings.json</c>.
    /// Any property absent from the configuration section retains its hardcoded default.
    /// </summary>
    /// <param name="mvcBuilder">
    /// The <see cref="IMvcBuilder"/> returned by <c>services.AddControllers()</c>.
    /// </param>
    /// <param name="configure">
    /// Optional delegate applied <em>after</em> the appsettings binding, allowing programmatic
    /// overrides on top of whatever is in the config file.
    /// </param>
    /// <returns>The same <see cref="IMvcBuilder"/> for fluent chaining.</returns>
    /// <example>
    /// Minimal — all settings come from the <c>"LogViewer"</c> appsettings section (or defaults):
    /// <code>
    /// services.AddControllers().AddLogViewer();
    /// </code>
    /// With programmatic override on top of appsettings:
    /// <code>
    /// services.AddControllers().AddLogViewer(o => o.PageTitle = "My App Logs");
    /// </code>
    /// </example>
    public static IMvcBuilder AddLogViewer(
        this IMvcBuilder mvcBuilder,
        Action<LogViewerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(mvcBuilder);

        // Bind from the "LogViewer" appsettings section.
        // Properties missing from the section keep their hardcoded defaults defined in
        // LogViewerOptions, so the section itself is entirely optional.
        mvcBuilder.Services
            .AddOptions<LogViewerOptions>()
            .BindConfiguration("LogViewer");

        // Apply any programmatic overrides on top of the config-bound values.
        if (configure is not null)
            mvcBuilder.Services.PostConfigure<LogViewerOptions>(configure);

        // Make ASP.NET Core discover LogViewerController from this assembly.
        mvcBuilder.AddApplicationPart(typeof(LogViewerController).Assembly);

        // Register the route convention through IConfigureOptions<MvcOptions> so it receives
        // the fully-resolved LogViewerOptions (appsettings + overrides) from the DI container.
        mvcBuilder.Services.AddSingleton<IConfigureOptions<MvcOptions>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<LogViewerOptions>>().Value;
            return new ConfigureOptions<MvcOptions>(
                o => o.Conventions.Add(new LogViewerRouteConvention(opts)));
        });

        // Named HttpClient used by the remote-host proxy endpoints.
        // The primary handler is resolved from DI so SkipRemoteSslValidation is applied at startup.
        mvcBuilder.Services
            .AddHttpClient("logviewer-remote")
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var opts    = sp.GetRequiredService<IOptions<LogViewerOptions>>().Value;
                var handler = new HttpClientHandler();
                if (opts.SkipRemoteSslValidation)
                    handler.ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                return handler;
            })
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "HtmlLogViewer-RemoteProxy/1.0");
            });

        return mvcBuilder;
    }
}
