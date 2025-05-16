using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.IPC;

namespace Orchestrator.Supervisor
{
    /// <summary>
    /// Supervises .NET processes: launch, monitor health, capture logs, and report status.
    /// Now directly transmits envelopes over the wire using TcpJsonClient<Envelope>.
    /// </summary>
    public class ProcessSupervisor : IProcessSupervisor, IInternalHealth
    {
        private readonly ConcurrentDictionary<string, List<Process>> _processes = new();
        private readonly TcpJsonClient<Envelope> _client;
        private readonly OrchestratorConfig _config;

        public ProcessSupervisor(TcpJsonClient<Envelope> client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _config = new OrchestratorConfig();
        }

        public InternalStatus GetStatus() => new InternalStatus
        {
            Name = nameof(ProcessSupervisor),
            IsHealthy = true,
            Details = $"Tracking: {string.Join(", ", _processes.Keys)}"
        };

        public Task StartAsync(string serviceName, int count = 1)
        {
            if (!OrchestratorConfig.Current.Services.TryGetValue(serviceName, out var cfg))
                throw new ArgumentException($"Service '{serviceName}' is not configured.");

            var list = _processes.GetOrAdd(serviceName, _ => new List<Process>());
            for (int i = 0; i < count; i++)
            {
                if (!File.Exists(cfg.ExecutablePath))
                {
                    var msg = new ConsoleLogMessage
                    {
                        Name = "_supervisor",
                        PID = 0,
                        Details = $"Executable '{cfg.ExecutablePath}' not found."
                    };
                    _ = _client.SendAsync(new Envelope("ConsoleLogMessage", msg));
                    continue;
                }
                if (!string.IsNullOrEmpty(cfg.WorkingDirectory)
                    && !Directory.Exists(cfg.WorkingDirectory))
                {
                    var msg = new 
                    {
                        Name = "_supervisor",
                        PID = 0,
                        Setails = $"Working dir '{cfg.WorkingDirectory}' not found."
                    };
                    _ = _client.SendAsync(new Envelope("ConsoleLogMessage", msg));
                    continue;
                }

                try
                {
                    var psi = new ProcessStartInfo("dotnet", $"{cfg.ExecutablePath} {cfg.Arguments}")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = cfg.WorkingDirectory ?? AppContext.BaseDirectory
                    };
                    var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                    proc.Exited += (s, e) =>
                    {
                        var exitMsg = new ConsoleLogMessage
                        {
                            Name  = "_supervisor",
                            PID = proc.Id,
                            Details = $"Process exited with code {proc.ExitCode}"
                        };
                        _ = _client.SendAsync(new Envelope("_supervisor", exitMsg));
                        ReportServiceStatus(serviceName);
                        Thread.Sleep(60_000);
                        list.Remove(proc);
                    };
                    proc.OutputDataReceived += (s, e) =>
                        _ = _client.SendAsync(new Envelope(serviceName, new ConsoleLogMessage
                        {
                            Name = serviceName,
                            PID = proc.Id,
                            Details = e.Data ?? string.Empty
                        }));
                    proc.ErrorDataReceived += (s, e) =>
                        _ = _client.SendAsync(new Envelope(serviceName, new ConsoleLogMessage
                        {
                            Name = serviceName,
                            PID = proc.Id,
                            Details = e.Data ?? string.Empty
                        }));

                    _ = _client.SendAsync(new Envelope("_supervisor", new ConsoleLogMessage
                    {
                        Name = "_supervisor",
                        PID = 0,
                        Details = $"Starting {serviceName} in '{psi.WorkingDirectory}'"
                    }));
                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    list.Add(proc);

                    _ = _client.SendAsync(new Envelope("_supervisor", new ConsoleLogMessage
                    {
                        Name = "_supervisor",
                        PID = proc.Id,
                        Details = $"Started {serviceName} (pid={proc.Id})"
                    }));
                }
                catch (Exception ex)
                {
                    _ = _client.SendAsync(new Envelope("_supervisor", new ConsoleLogMessage
                    {
                        Name = "_supervisor",
                        PID = 0,
                        Details = $"Failed to start {serviceName}: {ex.Message}"
                    }));
                }
            }

            ReportServiceStatus(serviceName);
            return Task.CompletedTask;
        }

        public Task StopAsync(string serviceName, int count = 1)
        {
            if (_processes.TryGetValue(serviceName, out var list))
            {
                foreach (var proc in list.Take(count).ToList())
                {
                    try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
                    catch { /* ignore */ }
                    finally
                    {
                        _ = _client.SendAsync(new Envelope(serviceName, new ConsoleLogMessage
                        {
                            Name = serviceName,
                            PID = proc.Id,
                            Details = "Process killed"
                        }));
                        proc.Dispose();
                        list.Remove(proc);
                    }
                }
            }
            ReportServiceStatus(serviceName);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<ServiceStatus>> ListStatusAsync()
        {
            var statuses = OrchestratorConfig.Current.Services.Keys.Select(name =>
            {
                _processes.TryGetValue(name, out var list);
                return new ServiceStatus
                {
                    Name = name,
                    RunningInstances = list?.Count ?? 0,
                    State = (list?.Count ?? 0) > 0 ? State.Running : State.Stopped,
                    LastReportAt = DateTime.UtcNow
                };
            });
            return Task.FromResult(statuses);
        }

        private void ReportServiceStatus(string serviceName)
        {
            var status = ListStatusAsync()
                         .Result
                         .FirstOrDefault(s => s.Name == serviceName);
            if (status == null) return;

            _ = _client.SendAsync(new Envelope("ServiceStatus", status));
        }
    }
}
