using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Supervisor;
using Orchestrator.Scheduler;
using Orchestrator.IPC;

namespace Orchestrator
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var exeFolder = AppContext.BaseDirectory;
            Directory.SetCurrentDirectory(exeFolder);

            var config = new ConfigurationBuilder()
                .SetBasePath(exeFolder)
                .AddJsonFile("orchestrator.json", optional: false, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection()
                .AddLogging(lb => lb.AddSimpleConsole().SetMinimumLevel(LogLevel.Information))
                .AddSingleton<IConfiguration>(config)

                // Core health providers
                .AddSingleton<IInternalHealth, PolicyScheduler>()
                // …any other IInternalHealth…

                // Bind your new IpcSettings
                .Configure<IpcSettings>(config.GetSection("Ipc"))

                // Register the concrete BasicIpcClient
                .AddSingleton(sp => {
                    var opts = sp.GetRequiredService<IOptions<IpcSettings>>().Value;
                    return new BasicIpcClient(
                        token => TransportFactories.NamedPipeStreamFactory(opts.Endpoint, token)
                    );
                })

                // Register your Worker (now taking BasicIpcClient)
                .AddSingleton<Worker>();

            using var provider = services.BuildServiceProvider();
            var logger = provider.GetRequiredService<ILogger<Program>>();
            var worker = provider.GetRequiredService<Worker>();

            logger.LogInformation("Console host starting");
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            await worker.RunAsync(cts.Token);
            logger.LogInformation("Console host exiting");
        }
    }
}
