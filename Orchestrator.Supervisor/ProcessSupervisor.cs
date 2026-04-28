using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Supervisor
{
    /// <summary>
    /// Supervises .NET processes: launch, monitor health, capture logs, and report status.
    /// Implements <see cref="IHostedService"/> to clean up all child processes on shutdown.
    /// </summary>
    public class ProcessSupervisor : IProcessSupervisor, IHostedService
    {
        // Inner list is protected by lock(list) for all mutations
        private readonly ConcurrentDictionary<string, List<Process>> _processes = new();
        // Tracks intentional stops to suppress auto-restart
        private readonly ConcurrentDictionary<string, bool> _stoppingServices = new();

        private readonly ILogStreamService _logStream;
        private readonly IOptions<OrchestratorConfig> _config;
        private readonly ILogger<ProcessSupervisor> _logger;

        /// <summary>Backoff in milliseconds before restarting a crashed process.</summary>
        private const int RestartBackoffMs = 5000;

        public ProcessSupervisor(
            ILogStreamService logStream,
            IOptions<OrchestratorConfig> config,
            ILogger<ProcessSupervisor> logger)
        {
            _logStream = logStream;
            _config = config;
            _logger = logger;
        }

        // --- IHostedService (graceful shutdown) --------------------------------

        Task IHostedService.StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        async Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ProcessSupervisor shutting down – stopping all managed processes.");
            var shutdownTimeout = TimeSpan.FromSeconds(30);

            foreach (var serviceName in _processes.Keys.ToList())
            {
                _stoppingServices[serviceName] = true;
                await StopAllInstancesAsync(serviceName, shutdownTimeout, cancellationToken);
            }
        }

        // --- IProcessSupervisor -----------------------------------------------

        /// <inheritdoc />
        public Task StartAsync(string serviceName, int count = 1)
        {
            if (!_config.Value.Services.TryGetValue(serviceName, out var cfg))
                throw new ArgumentException($"Service '{serviceName}' is not configured.");

            var list = _processes.GetOrAdd(serviceName, _ => new List<Process>());

            for (int i = 0; i < count; i++)
            {
                var psi = new ProcessStartInfo("dotnet", $"{cfg.ExecutablePath} {cfg.Arguments}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                if (!string.IsNullOrEmpty(cfg.WorkingDirectory))
                    psi.WorkingDirectory = cfg.WorkingDirectory;

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                proc.Exited += (_, _) =>
                {
                    int exitCode;
                    try { exitCode = proc.ExitCode; } catch { exitCode = -1; }

                    _logger.LogInformation(
                        "Process {ProcessId} for service {ServiceName} exited with code {ExitCode}.",
                        proc.Id, serviceName, exitCode);

                    // Remove from tracking list
                    lock (list)
                    {
                        list.Remove(proc);
                    }

                    // Auto-restart only for unexpected exits (not intentional stops)
                    bool intentional = _stoppingServices.GetValueOrDefault(serviceName, false);
                    if (!intentional && exitCode != 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            _logger.LogWarning(
                                "Service {ServiceName} crashed (exit code {ExitCode}). Restarting in {BackoffMs}ms.",
                                serviceName, exitCode, RestartBackoffMs);
                            await Task.Delay(RestartBackoffMs);
                            try
                            {
                                if (!_stoppingServices.GetValueOrDefault(serviceName, false))
                                    await StartAsync(serviceName, 1);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to restart service {ServiceName}.", serviceName);
                            }
                        });
                    }

                    _ = ReportServiceStatusAsync(serviceName);
                };

                proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null) _logStream.Push(serviceName, e.Data);
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) _logStream.Push(serviceName, e.Data);
                };

                _logger.LogInformation(
                    "Starting process {ServiceName}, WorkingDirectory='{WorkingDirectory}'.",
                    serviceName, proc.StartInfo.WorkingDirectory);
                proc.Start();
                _logger.LogInformation("Started process {ServiceName}, PID={ProcessId}.", serviceName, proc.Id);
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                lock (list) { list.Add(proc); }
            }

            _ = ReportServiceStatusAsync(serviceName);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(string serviceName, int count = 1)
        {
            _stoppingServices[serviceName] = true;

            if (_processes.TryGetValue(serviceName, out var list))
            {
                List<Process> toStop;
                lock (list) { toStop = list.Take(count).ToList(); }

                foreach (var p in toStop)
                {
                    try
                    {
                        if (!p.HasExited)
                            p.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error killing process {ProcessId}.", p.Id);
                    }
                    finally
                    {
                        lock (list) { list.Remove(p); }
                        p.Dispose();
                    }
                }
            }

            // Clear stopping flag once we've finished the intentional stop
            if (_processes.TryGetValue(serviceName, out var remaining))
            {
                lock (remaining)
                {
                    if (remaining.Count == 0)
                        _stoppingServices.TryRemove(serviceName, out _);
                }
            }

            _ = ReportServiceStatusAsync(serviceName);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IEnumerable<ServiceStatus>> ListStatusAsync()
        {
            var statuses = _config.Value.Services.Keys.Select(name =>
            {
                _processes.TryGetValue(name, out var list);

                List<Process> snapshot;
                if (list != null)
                    lock (list) { snapshot = list.Where(p => { try { return !p.HasExited; } catch { return false; } }).ToList(); }
                else
                    snapshot = new List<Process>();

                bool? lastHealthy = null;
                if (snapshot.Count > 0)
                {
                    lastHealthy = snapshot.Any(p =>
                    {
                        try
                        {
                            if (OperatingSystem.IsWindows())
                                return p.Responding;
                            return !p.HasExited;
                        }
                        catch { return false; }
                    });
                }

                return new ServiceStatus
                {
                    Name = name,
                    RunningInstances = snapshot.Count,
                    State = snapshot.Count > 0 ? State.Running : State.Stopped,
                    LastReportAt = DateTime.UtcNow,
                    LastHealthy = lastHealthy,
                    ProcessIds = snapshot.Select(p => { try { return p.Id; } catch { return -1; } })
                                         .Where(id => id >= 0).ToList()
                };
            });

            return Task.FromResult(statuses);
        }

        // --- Private helpers ---------------------------------------------------

        private async Task ReportServiceStatusAsync(string serviceName)
        {
            try
            {
                var statuses = await ListStatusAsync();
                var status = statuses.FirstOrDefault(s => s.Name == serviceName);
                if (status != null)
                {
                    var json = JsonSerializer.Serialize(status);
                    _logStream.Push("ServiceStatus", json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reporting status for service {ServiceName}.", serviceName);
            }
        }

        private async Task StopAllInstancesAsync(
            string serviceName, TimeSpan timeout, CancellationToken ct)
        {
            if (!_processes.TryGetValue(serviceName, out var list)) return;

            List<Process> procs;
            lock (list) { procs = list.ToList(); }

            foreach (var p in procs)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill(entireProcessTree: true);
                        await Task.WhenAny(
                            Task.Run(() => p.WaitForExit(), ct),
                            Task.Delay(timeout, ct));
                        if (!p.HasExited) p.Kill(entireProcessTree: true);
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }

            lock (list) { list.Clear(); }
        }
    }
}
