using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Supervisor;
using Orchestrator.Scheduler;
using Orchestrator.IPC;
using Orchestrator.Core.Models;

namespace Orchestrator.WebApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // 1) Create the builder
            var builder = WebApplication.CreateBuilder(args);

            // 2) Load JSON config
            builder.Configuration.AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true);
            var cfg = new OrchestratorConfig();
            cfg.Load(builder.Configuration);
            builder.Services.AddSingleton(cfg as IConfigurationLoader);
            var apiPort = OrchestratorConfig.Current.Web.ApiPort;
            builder.WebHost.ConfigureKestrel(opts =>
                opts.ListenAnyIP(apiPort));
                //opts.ListenAnyIP(apiPort, listenOpts => listenOpts.UseHttps()));

            // 3) Register orchestrator services
            builder.Services.AddSingleton<ILogStreamService, LogStreamService>();
            builder.Services.AddSingleton<IProcessSupervisor, ProcessSupervisor>();
            builder.Services.AddSingleton<IIpcServer, IpcServer>();
            builder.Services.AddHostedService<PolicyScheduler>();
            builder.Services.AddHostedService<IpcBackgroundService>();

            // 4) Add controllers + Swagger
            builder.Services.AddControllers();
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
            // Disabled CORS for now
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy
                        .AllowAnyOrigin()    // allow requests from *any* host
                        .AllowAnyMethod()    // allow GET, POST, PUT, DELETE, etc.
                        .AllowAnyHeader();   // allow all headers
                });
            });
            /*
                      builder.Services.AddCors(options =>
                      {
                          options.AddDefaultPolicy(policy =>
                              policy
                                .WithOrigins("https://localhost:5001", "http://localhost:5000")
                                .AllowAnyHeader()
                                .AllowAnyMethod()
                                .AllowCredentials());
                      });
                      */
            // …


            // 5) Build the app
            var app = builder.Build();

            // 6) Middleware pipeline
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

            // SSE log stream
            app.MapGet("/api/services/{name}/logs/stream", async context =>
            {
                var name = (string)context.Request.RouteValues["name"]!;
                var logs = context.RequestServices.GetRequiredService<ILogStreamService>();
                context.Response.Headers.Add("Content-Type", "text/event-stream");
                await foreach (var line in logs.StreamAsync(name))
                    await context.Response.WriteAsync($"data: {line}\n\n");
            });
            app.MapGet("/api/status/stream", async context =>
            {
                var log = context.RequestServices.GetRequiredService<ILogStreamService>();
                context.Response.Headers.Add("Content-Type", "text/event-stream");
                await foreach (var json in log.StreamAsync("InternalStatus"))
                {
                    // each json is a serialized InternalStatus
                    await context.Response.WriteAsync($"data: {json}\n\n");
                    await context.Response.Body.FlushAsync();
                }
            });
            // 7) Run
            app.Run();
        }
    }
}
