using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;
using Orchestrator.Core;
using Orchestrator.WebUI.Components;

namespace Orchestrator.WebUI
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var exeFolder = AppContext.BaseDirectory;
            Directory.SetCurrentDirectory(exeFolder);

            var builder = WebApplication.CreateBuilder(args);
            // Add support for Windows and systemd services
            builder.Host.UseWindowsService();
            builder.Host.UseSystemd();


            // 1) Load your orchestrator.json
            builder.Configuration
                   .SetBasePath(AppContext.BaseDirectory)
                   .AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true);
            var cfg = new OrchestratorConfig();
            cfg.Load(builder.Configuration);
            builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(options => { options.DetailedErrors = true; });

            // 2) Blazor Web App hosting
            builder.Services.AddRazorComponents()
                            .AddInteractiveServerComponents();          // Server‐side interactivity :contentReference[oaicite:0]{index=0}
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddHttpClient("OrcApi", (sp, client) =>
            {
                var httpContext = sp.GetRequiredService<IHttpContextAccessor>().HttpContext
                                  ?? throw new InvalidOperationException("No HttpContext");
                var request = httpContext.Request;
                // Build a UriBuilder off the incoming request
                var origin = new UriBuilder
                {
                    Scheme = request.Scheme,                     // http or https
                    Host = request.Host.Host,                  // e.g. "localhost" or "api.myapp.com"
                    Port = 5001                                 // default api port
                }.Uri;

                client.BaseAddress = origin;
            });

            // 3) Kestrel on UI port
            builder.WebHost.ConfigureKestrel(opts =>
                opts.ListenAnyIP(OrchestratorConfig.Current.Web.UiPort));
                //opts.ListenAnyIP(OrchestratorConfig.Current.Web.UiPort, listen => listen.UseHttps()));

            var app = builder.Build();
            app.Use(async (httpContext, next) =>
            {
                httpContext.Response.Headers[HeaderNames.CacheControl] = "no-cache";
                await next();
            });
            // 4) Static files & routing
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    var path = ctx.File.PhysicalPath;
                    if (path.Contains(Path.Combine("_framework", "")))
                    {
                        ctx.Context.Response.Headers["Cache-Control"] =
                            "no-cache, no-store, must-revalidate";
                        ctx.Context.Response.Headers["Pragma"] = "no-cache";
                        ctx.Context.Response.Headers["Expires"] = "0";
                    }
                }
            }); 
            app.UseRouting();
            app.UseAntiforgery();
            // 5) Wire up your App component as the only endpoint
            app.MapRazorComponents<App>()
               .AddInteractiveServerRenderMode();  // fully prerender then hydrate via SignalR :contentReference[oaicite:2]{index=2}

            app.Run();
        }
    }
}
