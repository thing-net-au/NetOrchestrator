using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            var host = Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .UseSystemd()
                .ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.SetBasePath(AppContext.BaseDirectory)
                        .AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true);
                })
                .ConfigureServices((ctx, services) =>
                {
                    var c = new OrchestratorConfig();
                    c.Load(ctx.Configuration);
                    services.AddSingleton<IConfigurationLoader>(c);

                    services.AddSingleton<ILogStreamService, LogStreamService>();
                    services.AddSingleton<IProcessSupervisor, ProcessSupervisor>();
                    services.AddSingleton<IIpcServer, IpcServer>();

                    services.AddSingleton<IInternalHealth, PolicyScheduler>();

                    services.AddHostedService<PolicyScheduler>();
                    services.AddHostedService<IpcBackgroundService>();
                    services.AddHostedService<InitialProcessLauncher>();
                    services.AddHostedService<Worker>();

                    services.Scan(scan => scan
                        .FromAssemblyOf<Program>()
                        .FromAssemblyOf<PolicyScheduler>()
                        .FromAssemblyOf<ProcessSupervisor>()
                        .AddClasses(classes => classes.AssignableTo<IInternalHealth>())
                        .AsImplementedInterfaces()
                        .WithSingletonLifetime());
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation(
                "Orchestrator host starting. ServicesConfigured={ServiceCount}, HealthIntervalMs={HealthInterval}, Runtime={RuntimeVersion}.",
                OrchestratorConfig.Current.Services.Count,
                OrchestratorConfig.Current.Global.HealthCheckInterval,
                Environment.Version);

            await host.RunAsync();
        }
    }
}
