using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Supervisor;
using Orchestrator.Scheduler;
using Orchestrator.IPC;
using Orchestrator.Core;
using Microsoft.Extensions.Options;    // for TcpJsonServer<T>, IpcSettings

namespace Orchestrator
{
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            // 1) Ensure working directory is your exe folder
            var exeFolder = AppContext.BaseDirectory;
            Directory.SetCurrentDirectory(exeFolder);

            var host = Host.CreateDefaultBuilder(args)
                // 2) Run as a true service
                .UseWindowsService()
                .UseSystemd()

                // 3) Load orchestrator.json
                .ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.SetBasePath(exeFolder)
                       .AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true);
                })

                // 4) Register all services
                .ConfigureServices((ctx, services) =>
                {
                    // in your Program.cs → ConfigureServices(...)
                    services.AddOptions();                                         // 1) make Configure<T> work
                    services.Configure<IpcSettings>(ctx.Configuration.GetSection("Ipc"));
                    services.AddSingleton<IConfigurationLoader, OrchestratorConfig>(sp =>
                    {
                        var loader = new OrchestratorConfig();
                        loader.Load(sp.GetRequiredService<IConfiguration>());
                        return loader;
                    });

                    services.AddSingleton<IEnvelopeStreamService, LogStreamService>();
                    services.AddSingleton<IProcessSupervisor, ProcessSupervisor>();
                    services.AddSingleton<IInternalHealth, ProcessScheduler>();
                    services.AddHostedService<ProcessScheduler>();

                    // 2a) WorkerStatus server registration
                    // inside ConfigureServices(…):
                    services.AddSingleton<TcpJsonServer<Envelope>>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptions<IpcSettings>>().Value;
                        var srv = new TcpJsonServer<Envelope>(
                            opts.Host,
                            opts.LogPort,
                            replayCount: opts.HistorySize,
                            historySize: opts.HistorySize
                        );
                        srv.MessageReceived += ws =>
                        srv.BroadcastAsync(ws);
                        //sp.GetRequiredService<ILogStreamService>()
                        //.Push(ws.ServiceName, JsonSerializer.Serialize(ws));
                        return srv;
                    });
                    services.AddSingleton<IHostedService, TcpJsonServerHost<Envelope>>();

                            // 2) Register the two TcpJsonClient<T> factories for WorkerStatus & InternalStatus:
                    services.AddSingleton<TcpJsonClient<Envelope>>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptions<IpcSettings>>().Value;
                        return new TcpJsonClient<Envelope>(opts.Host, opts.LogPort);
                    });
  
                    // 3) finally register your Worker which will connect as a client to those two servers
                    services.AddHostedService<Worker>();

                })

                // 5) Console logging (also goes to EventLog)
                .ConfigureLogging((ctx, lb) =>
                {
                    lb.AddSimpleConsole(o => o.SingleLine = true)
                      .AddEventLog()
                      .SetMinimumLevel(LogLevel.Information);
                })
                .Build();
            await host.RunAsync();
        }

    }
}
