using System;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orchestrator.Core;
using Orchestrator.WebUI.Components;

namespace Orchestrator.WebUI
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 1) Load orchestrator.json
            builder.Configuration
                   .SetBasePath(AppContext.BaseDirectory)
                   .AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true);

            // Bind config eagerly for Kestrel setup
            var startupCfg = builder.Configuration.Get<OrchestratorConfig>()
                             ?? new OrchestratorConfig();
            var uiPort = startupCfg.Web.UiPort;
            var bindIp = startupCfg.Web.BindIP ?? "127.0.0.1";
            var apiBase = startupCfg.Web.ApiBaseUrl
                          ?? $"http://{bindIp}:{startupCfg.Web.ApiPort}";

            builder.WebHost.ConfigureKestrel(opts =>
            {
                opts.Listen(IPAddress.Parse(bindIp), uiPort);
                // To enable HTTPS: opts.Listen(IPAddress.Parse(bindIp), uiPort,
                //     listenOpts => listenOpts.UseHttps());
            });

            // 2) Register OrchestratorConfig via IOptions<>
            builder.Services.Configure<OrchestratorConfig>(builder.Configuration);

            // 3) Blazor Server
            builder.Services.AddServerSideBlazor()
                .AddCircuitOptions(o => { o.DetailedErrors = true; });
            builder.Services.AddRazorComponents()
                            .AddInteractiveServerComponents();

            // 4) HttpClient for Orchestrator API — no IHttpContextAccessor needed
            builder.Services.AddHttpClient("OrcApi", client =>
            {
                client.BaseAddress = new Uri(apiBase.TrimEnd('/') + "/");
            });

            var app = builder.Build();

            // 5) Pipeline
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAntiforgery();
            app.MapRazorComponents<App>()
               .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
