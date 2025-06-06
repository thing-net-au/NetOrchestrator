using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Orchestrator.IPC;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Core.Extensions;
using Orchestrator.Supervisor;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Orchestrator.Core;
using Microsoft.Net.Http.Headers;

namespace Orchestrator.WebApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // 0) Ensure correct working folder
            var exeFolder = AppContext.BaseDirectory;
            Directory.SetCurrentDirectory(exeFolder);


            var builder = WebApplication.CreateBuilder(args);

            // 1) Run as a service under Windows or systemd
            builder.Host
                   .UseWindowsService()
                   .UseSystemd();

            // 0) work directory + config
            builder.Configuration
                   .SetBasePath(exeFolder)
                   .AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true);

            // 1) bind IpcSettings
            builder.Services.AddOptions();
            builder.Services.Configure<IpcSettings>(builder.Configuration.GetSection("Ipc"));
            builder.Services.AddSingleton<TcpJsonClient<Envelope>>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<IpcSettings>>().Value;
                return new TcpJsonClient<Envelope>(opts.Host, opts.LogPort);
            });
            builder.Services.AddSingleton<LogStreamService>();
            builder.Services.AddSingleton<IEnvelopeStreamService, EnvelopeStreamService>();
            builder.Services.AddSingleton<IConsoleLogStreamService, ConsoleLogStreamService>();
            builder.Services.AddSingleton<IProcessSupervisor, ProcessSupervisor>();

            builder.Services.AddControllers();

            // 4) our “startup” hosted service that spins up two TcpJsonClient<T>
            builder.Services.AddHostedService<StartupJsonClients>();

            // 5) swagger + CORS
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new() { Title = "Orchestrator API", Version = "v1" }));
            builder.Services.AddCors(o => o.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

            builder.WebHost.ConfigureKestrel(opts =>
            opts.ListenAnyIP(OrchestratorConfig.Current.Web.ApiPort));
            //opts.ListenAnyIP(OrchestratorConfig.Current.Web.UiPort, listen => listen.UseHttps()));

            var app = builder.Build();
            app.Use(async (httpContext, next) =>
            {
                httpContext.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
                httpContext.Response.Headers[HeaderNames.Pragma] = "no-cache";
                httpContext.Response.Headers[HeaderNames.Expires] = "0";

                await next();
            });
            app.UseCors("AllowAll");
            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Orchestrator API V1"));
            }

            app.MapControllers();

            // SSE for WorkerStatus (deserialized payload)
            // SSE for ConsoleLogMessage (or WorkerStatus, etc)
            app.MapGet("/api/services/{name}/logs/stream", async ctx =>
            {
                var name = (string)ctx.Request.RouteValues["name"]!;
                var logs = ctx.RequestServices.GetRequiredService<IConsoleLogStreamService>();
                ctx.Response.Headers.Add("Content-Type", "text/event-stream");

                await foreach (var env in logs.StreamAsync(name))
                {
                    // Deserialize the payload to your expected type:
                    //var msg = JsonSerializer.Deserialize<ConsoleLogMessage>(env.Details)!;
                    var json = JsonSerializer.Serialize(env);
                    await ctx.Response.WriteAsync($"data: {json}\n\n");
                    await ctx.Response.Body.FlushAsync();
                }
            });

            // SSE for ServiceStatus updates
            app.MapGet("/api/services/stream", async ctx =>
            {
                var stream = ctx.RequestServices.GetRequiredService<IEnvelopeStreamService>();
                ctx.Response.Headers.Add("Content-Type", "text/event-stream");

                await foreach (var status in stream.StreamAsync<ServiceStatus>("ServiceStatus"))
                {
                    var json = JsonSerializer.Serialize(status);
                    await ctx.Response.WriteAsync($"data: {json}\n\n");
                    await ctx.Response.Body.FlushAsync();
                }
            });
            // SSE for InternalStatus (still envelope-based)
            app.MapGet("/api/status/stream", async ctx =>
            {
                var logs = ctx.RequestServices.GetRequiredService<IEnvelopeStreamService>();
                ctx.Response.Headers.Add("Content-Type", "text/event-stream");

                await foreach (var status in logs.StreamAsync<InternalStatus>("HostHeartBeat"))
                {
                    var json = JsonSerializer.Serialize(status);
                    await ctx.Response.WriteAsync($"data: {json}\n\n");
                    await ctx.Response.Body.FlushAsync();
                }
            });

            app.Run();


        }
    }

}
