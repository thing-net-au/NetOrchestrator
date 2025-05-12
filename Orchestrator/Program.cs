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

            await Host.CreateDefaultBuilder(args)
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

                    services.AddSingleton<ILogStreamService, LogStreamService>();
                    services.AddSingleton<IProcessSupervisor, ProcessSupervisor>();
                    services.AddSingleton<IInternalHealth, PolicyScheduler>();
                    services.AddHostedService<PolicyScheduler>();

                    // 2a) WorkerStatus server registration
                    // inside ConfigureServices(…):
                    services.AddSingleton<TcpJsonServer<WorkerStatus>>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptions<IpcSettings>>().Value;
                        var srv = new TcpJsonServer<WorkerStatus>(
                            opts.Host,
                            opts.LogPort,
                            replayCount: opts.HistorySize,
                            historySize: opts.HistorySize
                        );
                        srv.MessageReceived += ws =>
                            sp.GetRequiredService<ILogStreamService>()
                              .Push(ws.ServiceName, JsonSerializer.Serialize(ws));
                        return srv;
                    });
                    services.AddSingleton<IHostedService, TcpJsonServerHost<WorkerStatus>>();

                    services.AddSingleton<TcpJsonServer<InternalStatus>>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptions<IpcSettings>>().Value;
                        var srv = new TcpJsonServer<InternalStatus>(
                            opts.Host,
                            opts.StatusPort,
                            replayCount: opts.HistorySize,
                            historySize: opts.HistorySize
                        );
                        srv.MessageReceived += st =>
                            sp.GetRequiredService<ILogStreamService>()
                              .Push("InternalStatus", JsonSerializer.Serialize(st));
                        return srv;
                    });
                    services.AddSingleton<IHostedService, TcpJsonServerHost<InternalStatus>>();

                    // 2) Register the two TcpJsonClient<T> factories for WorkerStatus & InternalStatus:
                    services.AddSingleton<TcpJsonClient<WorkerStatus>>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptions<IpcSettings>>().Value;
                        return new TcpJsonClient<WorkerStatus>(opts.Host, opts.LogPort);
                    });
                    services.AddSingleton<TcpJsonClient<InternalStatus>>(sp =>
                    {
                        var opts = sp.GetRequiredService<IOptions<IpcSettings>>().Value;
                        return new TcpJsonClient<InternalStatus>(opts.Host, opts.StatusPort);
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

                // 6) Run until service stop
                .RunConsoleAsync();
        }
    }
}
