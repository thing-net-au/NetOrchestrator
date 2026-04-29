using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.IPC;
using Orchestrator.Scheduler;
using Orchestrator.Supervisor;

namespace Orchestrator
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await Host.CreateDefaultBuilder(args)
                 .UseWindowsService()
                 .UseSystemd()
                 .ConfigureAppConfiguration((ctx, cfg) =>
                 {
                     cfg.SetBasePath(AppContext.BaseDirectory)
                        .AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true);
                 })
                 .ConfigureServices((ctx, services) =>
                 {
                     // 1) Bind OrchestratorConfig via IOptions<> (no static singleton)
                     services.Configure<OrchestratorConfig>(ctx.Configuration);

                     // 2) Core services
                     services.AddSingleton<ILogStreamService, LogStreamService>();

                     // 3) ProcessSupervisor: singleton + IHostedService for graceful shutdown
                     services.AddSingleton<ProcessSupervisor>();
                     services.AddSingleton<IProcessSupervisor>(sp => sp.GetRequiredService<ProcessSupervisor>());
                     services.AddHostedService(sp => sp.GetRequiredService<ProcessSupervisor>());

                     // 4) IPC server
                     services.AddSingleton<IIpcServer, IpcServer>();

                     // 5) PolicyScheduler: singleton as IInternalHealth + IHostedService (single instance)
                     services.AddSingleton<PolicyScheduler>();
                     services.AddSingleton<IInternalHealth>(sp => sp.GetRequiredService<PolicyScheduler>());
                     services.AddHostedService(sp => sp.GetRequiredService<PolicyScheduler>());

                     // 6) IpcBackgroundService: singleton as IInternalHealth + IHostedService (single instance)
                     services.AddSingleton<IpcBackgroundService>();
                     services.AddSingleton<IInternalHealth>(sp => sp.GetRequiredService<IpcBackgroundService>());
                     services.AddHostedService(sp => sp.GetRequiredService<IpcBackgroundService>());

                     // 7) Startup and heartbeat workers
                     services.AddHostedService<InitialProcessLauncher>();
                     services.AddHostedService<Worker>();
                 })
                 .Build()
                 .RunAsync();
        }
    }
}
