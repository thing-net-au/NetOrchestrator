using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Supervisor
{
    /// <summary>
    /// Supervises .NET processes: launch, monitor health, capture logs, and report status.
    /// </summary>
    public class ProcessSupervisor : IProcessSupervisor
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, Process>> _processes = new();
        private readonly ILogStreamService _logStream;
        private readonly ILogger<ProcessSupervisor> _logger;

        public ProcessSupervisor(ILogStreamService logStream, ILogger<ProcessSupervisor> logger)
        {
            _logStream = logStream;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task StartAsync(string serviceName, int count = 1)
        {
            if (count <= 0)
            {
                _logger.LogWarning("Ignoring StartAsync for service {ServiceName} with non-positive count {Count}.", serviceName, count);
                return;
            }

            if (!OrchestratorConfig.Current.Services.TryGetValue(serviceName, out var cfg))
            {
                throw new ArgumentException($"Service '{serviceName}' is not configured.");
            }

            var serviceProcesses = _processes.GetOrAdd(serviceName, _ => new ConcurrentDictionary<int, Process>());

            for (var i = 0; i < count; i++)
            {
                var psi = BuildProcessStartInfo(cfg);
                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                proc.Exited += (_, _) =>
                {
                    serviceProcesses.TryRemove(proc.Id, out _);
                    _logStream.Push("_supervisor", $"Process {proc.Id} exited with code {proc.ExitCode}");
                    _logStream.Push(serviceName, $"Process {proc.Id} exited with code {proc.ExitCode}");
                    _logger.LogInformation("Service {ServiceName} process {ProcessId} exited with code {ExitCode}.", serviceName, proc.Id, proc.ExitCode);
                    _ = ReportServiceStatusAsync(serviceName);
                };

                proc.OutputDataReceived += (_, e) => _logStream.Push(serviceName, e.Data);
                proc.ErrorDataReceived += (_, e) => _logStream.Push(serviceName, e.Data);

                _logger.LogInformation("Starting service process {ServiceName} in directory {WorkingDirectory}.", serviceName, proc.StartInfo.WorkingDirectory);
                _logStream.Push("_supervisor", $"Starting Process {serviceName} WorkingDirectory='{proc.StartInfo.WorkingDirectory}'.");

                try
                {
                    proc.Start();
                    serviceProcesses[proc.Id] = proc;
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    _logger.LogInformation("Started service process {ServiceName} with PID {ProcessId}.", serviceName, proc.Id);
                    _logStream.Push("_supervisor", $"Started Process {serviceName}, {proc.Id}.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start service process {ServiceName}.", serviceName);
                    proc.Dispose();
                    throw;
                }
            }

            await ReportServiceStatusAsync(serviceName);
        }

        /// <inheritdoc />
        public async Task StopAsync(string serviceName, int count = 1)
        {
            if (count <= 0)
            {
                _logger.LogWarning("Ignoring StopAsync for service {ServiceName} with non-positive count {Count}.", serviceName, count);
                return;
            }

            if (_processes.TryGetValue(serviceName, out var serviceProcesses))
            {
                foreach (var process in serviceProcesses.Values.Take(count).ToList())
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            _logger.LogInformation("Killed service process {ServiceName} with PID {ProcessId}.", serviceName, process.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to stop service process {ServiceName} with PID {ProcessId}.", serviceName, process.Id);
                    }
                    finally
                    {
                        serviceProcesses.TryRemove(process.Id, out _);
                        process.Dispose();
                    }
                }
            }

            await ReportServiceStatusAsync(serviceName);
        }

        /// <inheritdoc />
        public Task<IEnumerable<ServiceStatus>> ListStatusAsync()
        {
            var statuses = OrchestratorConfig.Current.Services.Keys.Select(name =>
            {
                _processes.TryGetValue(name, out var serviceProcesses);
                var running = serviceProcesses?.Count ?? 0;
                var state = running > 0 ? State.Running : State.Stopped;

                return new ServiceStatus
                {
                    Name = name,
                    RunningInstances = running,
                    State = state,
                    LastReportAt = DateTime.UtcNow
                };
            });

            return Task.FromResult(statuses);
        }

        private static ProcessStartInfo BuildProcessStartInfo(ServiceConfig cfg)
        {
            var executablePath = cfg.ExecutablePath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException($"Service '{cfg.Name}' has empty executable path.");
            }

            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add(executablePath);
            foreach (var arg in TokenizeArguments(cfg.Arguments))
            {
                psi.ArgumentList.Add(arg);
            }

            if (!string.IsNullOrWhiteSpace(cfg.WorkingDirectory))
            {
                psi.WorkingDirectory = cfg.WorkingDirectory;
            }

            return psi;
        }

        private static IEnumerable<string> TokenizeArguments(string? arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                yield break;
            }

            foreach (var token in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return token;
            }
        }

        /// <summary>
        /// Serializes and pushes the current status of a service to the log stream.
        /// </summary>
        private async Task ReportServiceStatusAsync(string serviceName)
        {
            var status = (await ListStatusAsync()).FirstOrDefault(s => s.Name == serviceName);
            if (status == null)
            {
                return;
            }

            var json = JsonSerializer.Serialize(status);
            _logStream.Push("ServiceStatus", json);
        }
    }
}
