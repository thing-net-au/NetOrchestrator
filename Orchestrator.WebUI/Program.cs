using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
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

            // 1) Load your orchestrator.json
            builder.Configuration
                   .SetBasePath(AppContext.BaseDirectory)
                   .AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true);
            var cfg = new OrchestratorConfig();
            cfg.Load(builder.Configuration);

            // 2) Blazor Web App hosting
            builder.Services.AddRazorComponents()
                            .AddInteractiveServerComponents();          // Server‐side interactivity :contentReference[oaicite:0]{index=0}
            builder.Services.AddHttpClient("OrcApi", c =>
                c.BaseAddress = new Uri(OrchestratorConfig.Current.Web.ApiBaseUrl));

            // 3) Kestrel on UI port
            builder.WebHost.ConfigureKestrel(opts =>
                opts.ListenAnyIP(OrchestratorConfig.Current.Web.UiPort, listen => listen.UseHttps()));

            var app = builder.Build();

            // 4) Static files & routing
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();   // serve wwwroot/* :contentReference[oaicite:1]{index=1}
            app.UseRouting();
            app.UseAntiforgery();
            // 5) Wire up your App component as the only endpoint
            app.MapRazorComponents<App>()
               .AddInteractiveServerRenderMode();  // fully prerender then hydrate via SignalR :contentReference[oaicite:2]{index=2}

            app.Run();
        }
    }
}
