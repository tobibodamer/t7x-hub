using MatchmakingServer.SignalR;

using Serilog;

namespace MatchmakingServer;

public static class ApplicationSetup
{
    public static void UseRequestLogging(this IApplicationBuilder app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms ({ClientAppName}/{ClientAppVersion})";
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                if (httpContext.Request.Headers.TryGetValue("X-App-Name", out var appNameValues) && appNameValues.Count != 0)
                {
                    diagnosticContext.Set("ClientAppName", appNameValues[0] ?? "");
                }
                else
                {
                    diagnosticContext.Set("ClientAppName", "Unknown");
                }

                if (httpContext.Request.Headers.TryGetValue("X-App-Version", out var appVersionValues) && appVersionValues.Count != 0)
                {
                    diagnosticContext.Set("ClientAppVersion", appVersionValues[0] ?? "");
                }
                else
                {
                    diagnosticContext.Set("ClientAppVersion", "?");
                }
            };
        });
    }

    public static void MapHubs(this IEndpointRouteBuilder app)
    {
        app.MapHub<QueueingHub>("/Queue");
        app.MapHub<PartyHub>("/Party");
        app.MapHub<SocialHub>("/Social");
    }
}
