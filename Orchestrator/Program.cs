using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.IPC;
using Orchestrator.Scheduler;
using Orchestrator.Supervisor;
using Scrutor;

namespace Orchestrator
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await Host.CreateDefaultBuilder(args)
                 .UseWindowsService()  // Windows service
                 .UseSystemd()         // systemd on Linux
                 .ConfigureAppConfiguration((ctx, cfg) =>
                 {
                     cfg.SetBasePath(AppContext.BaseDirectory)
                        .AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true);
                 })
                 .ConfigureServices((ctx, services) =>
                 {
                     // 1) bind config
                     var c = new OrchestratorConfig();
                     c.Load(ctx.Configuration);
                     services.AddSingleton<IConfigurationLoader>(c);

                     // 2) core & supervisor
                     services.AddSingleton<ILogStreamService, LogStreamService>();
                     services.AddSingleton<IProcessSupervisor, ProcessSupervisor>();
                     services.AddSingleton<IIpcServer, IpcServer>();

                     // 3) background workers
                     // Make sure your scheduler and supervisor implement IInternalHealth:
                     services.AddSingleton<IInternalHealth, PolicyScheduler>();
                     //services.AddSingleton<IInternalHealth, ProcessSupervisor>();

                     services.AddHostedService<PolicyScheduler>();
                     services.AddHostedService<IpcBackgroundService>();

                     // 4) kick off initial processes
                     services.AddHostedService<InitialProcessLauncher>();
                     services.AddHostedService<Worker>();

                     services.Scan(scan => scan
    // Scan the host assembly…
    .FromAssemblyOf<Program>()
    // …and the scheduler assembly
    .FromAssemblyOf<Orchestrator.Scheduler.PolicyScheduler>()
    // …and the supervisor assembly
    .FromAssemblyOf<Orchestrator.Supervisor.ProcessSupervisor>()
    // Then pick up those that implement IInternalHealth
    .AddClasses(classes => classes.AssignableTo<IInternalHealth>())
    .AsImplementedInterfaces()
    .WithSingletonLifetime()
);

                 })
                    .Build()
                 .RunAsync();

        }
    }
}