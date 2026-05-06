using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

            builder.Configuration.AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true);
            var cfg = new OrchestratorConfig();
            cfg.Load(builder.Configuration);
            builder.Services.AddSingleton(cfg as IConfigurationLoader);

            var apiPort = OrchestratorConfig.Current.Web.ApiPort;
            builder.WebHost.ConfigureKestrel(opts => opts.ListenAnyIP(apiPort));

            builder.Services.AddSingleton<ILogStreamService, LogStreamService>();
            builder.Services.AddSingleton<IProcessSupervisor, ProcessSupervisor>();
            builder.Services.AddSingleton<IIpcServer, IpcServer>();
            builder.Services.AddHostedService<PolicyScheduler>();
            builder.Services.AddHostedService<IpcBackgroundService>();

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
                {
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                });
            });

            var app = builder.Build();

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Starting Orchestrator.WebApi on port {ApiPort} with {ServiceCount} configured services.",
                OrchestratorConfig.Current.Web.ApiPort,
                OrchestratorConfig.Current.Services.Count);

            app.Use(async (context, next) =>
            {
                var requestId = context.TraceIdentifier;
                context.Response.Headers["X-Request-ID"] = requestId;
                await next();
            });

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Orchestrator API V1"));
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

            app.Run();
        }
    }
}
