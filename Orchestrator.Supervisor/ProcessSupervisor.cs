// Project: Orchestrator.Supervisor (Class Library)
// References: Orchestrator.Core, System.Threading.Channels, System.Text.Json

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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
        private readonly ConcurrentDictionary<string, List<Process>> _processes = new();
        private readonly ILogStreamService _logStream;

        public ProcessSupervisor(ILogStreamService logStream)
        {
            _logStream = logStream;
        }

        /// <inheritdoc />
public Task StartAsync(string serviceName, int count = 1)
        {
            if (!OrchestratorConfig.Current.Services.TryGetValue(serviceName, out var cfg))
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
                _logStream.Push("_supervisor", $"Starting Process {serviceName}.");

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
    
                // hook exit
                proc.Exited += (sender, args) =>
                {
                    _logStream.Push("_supervisor", $"Process {proc.Id} exited with code {proc.ExitCode}");
                    // push a log line if you like
                    _logStream.Push(serviceName, $"Process {proc.Id} exited with code {proc.ExitCode}");

                    // report the updated service status (you'll need to implement this)
                    ReportServiceStatus(serviceName);
                    Thread.Sleep(60000);

                    // remove from our list
                    if(list.Contains(proc))
                        list.Remove(proc);


                };

                proc.OutputDataReceived += (sender, e) => _logStream.Push(serviceName, e.Data);
                proc.ErrorDataReceived += (sender, e) => _logStream.Push(serviceName, e.Data);

                proc.Start();
                _logStream.Push("_supervisor", $"Started Process {serviceName}, {proc.Id}.");
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                list.Add(proc);
            }

            // initial status report
            ReportServiceStatus(serviceName);
            return Task.CompletedTask;
        }

        

        /// <inheritdoc />
        public Task StopAsync(string serviceName, int count = 1)
        {
            if (_processes.TryGetValue(serviceName, out var list))
            {
                var toStop = list.Take(count).ToList();
                foreach (var p in toStop)
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                        // ignore failures
                    }
                    finally
                    {
                        p.Dispose();
                        list.Remove(p);
                    }
                }
            }

            // Report updated status after stopping
            ReportServiceStatus(serviceName);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IEnumerable<ServiceStatus>> ListStatusAsync()
        {
            var statuses = OrchestratorConfig.Current.Services.Keys.Select(name =>
            {
                _processes.TryGetValue(name, out var list);
                var running = list?.Count ?? 0;
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

        /// <summary>
        /// Serializes and pushes the current status of a service to the log stream.
        /// </summary>
        private void ReportServiceStatus(string serviceName)
        {
            var status = ListStatusAsync().Result.FirstOrDefault(s => s.Name == serviceName);
            if (status != null)
            {
                var json = JsonSerializer.Serialize(status);
                _logStream.Push("ServiceStatus", json);
            }
        }
    }
}
