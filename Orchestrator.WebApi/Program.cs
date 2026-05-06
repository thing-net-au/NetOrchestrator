using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.IPC;
using Orchestrator.Scheduler;
using Orchestrator.Supervisor;

namespace Orchestrator.WebApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 1) Load JSON config
            builder.Configuration.AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true);

            // Bind config eagerly for Kestrel setup (before DI container is built)
            var startupCfg = builder.Configuration.Get<OrchestratorConfig>()
                             ?? new OrchestratorConfig();

            var apiPort = startupCfg.Web.ApiPort;
            var bindIp = startupCfg.Web.BindIP ?? "127.0.0.1";

            builder.WebHost.ConfigureKestrel(opts =>
            {
                opts.Listen(IPAddress.Parse(bindIp), apiPort);
                // To enable HTTPS: opts.Listen(IPAddress.Parse(bindIp), apiPort,
                //     listenOpts => listenOpts.UseHttps());
            });

            // 2) Register OrchestratorConfig via IOptions<> (DI-friendly, no static singleton)
            builder.Services.Configure<OrchestratorConfig>(builder.Configuration);

            // 3) Core services
            builder.Services.AddSingleton<ILogStreamService, LogStreamService>();

            // ProcessSupervisor: singleton + IHostedService for graceful shutdown
            builder.Services.AddSingleton<ProcessSupervisor>();
            builder.Services.AddSingleton<IProcessSupervisor>(
                sp => sp.GetRequiredService<ProcessSupervisor>());
            builder.Services.AddHostedService(
                sp => sp.GetRequiredService<ProcessSupervisor>());

            builder.Services.AddSingleton<IIpcServer, IpcServer>();

            // PolicyScheduler: single instance as IInternalHealth + IHostedService
            builder.Services.AddSingleton<PolicyScheduler>();
            builder.Services.AddSingleton<IInternalHealth>(
                sp => sp.GetRequiredService<PolicyScheduler>());
            builder.Services.AddHostedService(
                sp => sp.GetRequiredService<PolicyScheduler>());

            // IpcBackgroundService: single instance as IInternalHealth + IHostedService
            builder.Services.AddSingleton<IpcBackgroundService>();
            builder.Services.AddSingleton<IInternalHealth>(
                sp => sp.GetRequiredService<IpcBackgroundService>());
            builder.Services.AddHostedService(
                sp => sp.GetRequiredService<IpcBackgroundService>());

            // 4) Controllers + Swagger
            builder.Services.AddControllers();
            builder.Services.AddHealthChecks();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Orchestrator API",
                    Version = "v1",
                    Description = "HTTP API for managing supervised .NET services"
                });
            });

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
            });

            // 5) Build
            var app = builder.Build();

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Starting Orchestrator.WebApi on {BindIp}:{ApiPort} with {ServiceCount} configured services.",
                bindIp, apiPort, startupCfg.Services.Count);

            // 6) Middleware
            app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Request-ID"] = context.TraceIdentifier;
                await next();
            });

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Orchestrator API V1"));
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseRouting();
            app.UseCors("AllowAll");
            app.MapControllers();

            app.MapHealthChecks("/health/live", new HealthCheckOptions());
            app.MapHealthChecks("/health/ready", new HealthCheckOptions());

            // SSE: per-service log stream
            app.MapGet("/api/services/{name}/logs/stream", async context =>
            {
                var name = (string)context.Request.RouteValues["name"]!;
                var logs = context.RequestServices.GetRequiredService<ILogStreamService>();
                context.Response.Headers.Append("Content-Type", "text/event-stream");
                await foreach (var line in logs.StreamAsync(name))
                {
                    await context.Response.WriteAsync($"data: {line}\n\n");
                    await context.Response.Body.FlushAsync();
                }
            });

            // SSE: internal status stream
            app.MapGet("/api/status/stream", async context =>
            {
                var log = context.RequestServices.GetRequiredService<ILogStreamService>();
                context.Response.Headers.Append("Content-Type", "text/event-stream");
                await foreach (var json in log.StreamAsync("InternalStatus"))
                {
                    await context.Response.WriteAsync($"data: {json}\n\n");
                    await context.Response.Body.FlushAsync();
                }
            });

            // Prometheus-compatible metrics endpoint
            app.MapGet("/metrics", async context =>
            {
                var supervisor = context.RequestServices.GetRequiredService<IProcessSupervisor>();
                var statuses = (await supervisor.ListStatusAsync()).ToList();

                var sb = new StringBuilder();
                sb.AppendLine("# HELP orchestrator_running_instances Number of running instances per service");
                sb.AppendLine("# TYPE orchestrator_running_instances gauge");
                foreach (var s in statuses)
                    sb.AppendLine($"orchestrator_running_instances{{service=\"{s.Name}\"}} {s.RunningInstances}");

                sb.AppendLine("# HELP orchestrator_last_report_timestamp_seconds Last status report time (Unix seconds)");
                sb.AppendLine("# TYPE orchestrator_last_report_timestamp_seconds gauge");
                foreach (var s in statuses)
                {
                    var ts = s.LastReportAt.HasValue
                        ? new DateTimeOffset(s.LastReportAt.Value, TimeSpan.Zero).ToUnixTimeSeconds()
                        : 0;
                    sb.AppendLine($"orchestrator_last_report_timestamp_seconds{{service=\"{s.Name}\"}} {ts}");
                }

                var schedulers = context.RequestServices.GetServices<IInternalHealth>();
                sb.AppendLine("# HELP orchestrator_component_healthy Whether an internal component is healthy (1=yes, 0=no)");
                sb.AppendLine("# TYPE orchestrator_component_healthy gauge");
                foreach (var h in schedulers)
                {
                    var st = h.GetStatus();
                    sb.AppendLine($"orchestrator_component_healthy{{component=\"{st.Name}\"}} {(st.IsHealthy ? 1 : 0)}");
                }

                context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
                await context.Response.WriteAsync(sb.ToString());
            });

            app.Run();
        }
    }
}
