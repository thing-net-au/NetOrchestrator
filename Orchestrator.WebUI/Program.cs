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
         // 2) Load your orchestrator.json from the output folder
            builder.Configuration
                   .SetBasePath(AppContext.BaseDirectory)
                   .AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true);
            var cfg = new OrchestratorConfig();
            cfg.Load(builder.Configuration);


            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddHttpClient("OrcApi", c =>
    c.BaseAddress = new Uri(OrchestratorConfig.Current.Web.ApiBaseUrl));
            builder.WebHost.ConfigureKestrel(opts =>
                opts.ListenAnyIP(OrchestratorConfig.Current.Web.UiPort, listenOpts => listenOpts.UseHttps()));


            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseStaticFiles();
            app.UseAntiforgery();

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();


        }
    }
}

