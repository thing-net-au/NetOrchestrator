using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Orchestrator.IPC;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Supervisor;
using Microsoft.Extensions.Options;

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

            // 1) Load orchestrator.json via IConfiguration
            builder.Configuration
                   .SetBasePath(exeFolder)
                   .AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true);

            // Bind IPC settings (Host, LogPort, StatusPort)
            builder.Services.Configure<IpcSettings>(builder.Configuration.GetSection("Ipc"));

            // 2) Core & in-memory log broker
            builder.Services.AddSingleton<ILogStreamService, LogStreamService>();

            // 3) Host the WorkerStatus TCP/JSON server
            builder.Services.AddSingleton<TcpJsonServer<WorkerStatus>>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<IpcSettings>>().Value;
                var server = new TcpJsonServer<WorkerStatus>(opts.Host, opts.LogPort, replayCount: 50);
                server.MessageReceived += status =>
                {
                    // on each incoming WorkerStatus, push into the in-memory log stream
                    var logs = sp.GetRequiredService<ILogStreamService>();
                    logs.Push(status.ServiceName, status.ToJson());
                };
                server.Start();
                return server;
            });

            // 4) Host the InternalStatus TCP/JSON server
            builder.Services.AddSingleton<TcpJsonServer<InternalStatus>>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<IpcSettings>>().Value;
                var server = new TcpJsonServer<InternalStatus>(opts.Host, opts.StatusPort, replayCount: 50);
                server.MessageReceived += istat =>
                {
                    var logs = sp.GetRequiredService<ILogStreamService>();
                    logs.Push("InternalStatus", istat.ToJson());
                };
                server.Start();
                return server;
            });

            // 5) Supervisor & controllers
            builder.Services.AddSingleton<IProcessSupervisor, ProcessSupervisor>();
            builder.Services.AddControllers();

            // 6) Swagger & CORS
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Orchestrator API", Version = "v1" }));
            builder.Services.AddCors(opts =>
                opts.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

            // 7) Build & middleware pipeline
            var app = builder.Build();

            app.UseCors("AllowAll");
            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Orchestrator API V1"));
            }

            app.UseRouting();
            app.MapControllers();

            // 8) SSE endpoints driven by the in-memory LogStreamService
            app.MapGet("/api/services/{name}/logs/stream", async ctx =>
            {
                var name = (string)ctx.Request.RouteValues["name"]!;
                var logs = ctx.RequestServices.GetRequiredService<ILogStreamService>();
                ctx.Response.Headers.Add("Content-Type", "text/event-stream");
                await foreach (var line in logs.StreamAsync(name))
                    await ctx.Response.WriteAsync($"data: {line}\n\n");
            });
            app.MapGet("/api/status/stream", async ctx =>
            {
                var logs = ctx.RequestServices.GetRequiredService<ILogStreamService>();
                ctx.Response.Headers.Add("Content-Type", "text/event-stream");
                await foreach (var json in logs.StreamAsync("InternalStatus"))
                {
                    await ctx.Response.WriteAsync($"data: {json}\n\n");
                    await ctx.Response.Body.FlushAsync();
                }
            });

            app.Run();
        }
    }

}
