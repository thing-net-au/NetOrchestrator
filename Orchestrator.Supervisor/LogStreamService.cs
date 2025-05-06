using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Supervisor
{
    /// <summary>
    /// Streams log messages from processes to connected clients.
    /// </summary>
    public class LogStreamService : ILogStreamService
    {
        private readonly ConcurrentDictionary<string, Channel<string>> _channels = new();

        /// <inheritdoc />
        public void Push(string serviceName, string message)
        {
            if (message == null) return;
            var channel = _channels.GetOrAdd(serviceName, _ => Channel.CreateUnbounded<string>());
            channel.Writer.TryWrite(message);
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<string> StreamAsync(string serviceName)
        {
            var channel = _channels.GetOrAdd(serviceName, _ => Channel.CreateUnbounded<string>());
            while (await channel.Reader.WaitToReadAsync())
            {
                while (channel.Reader.TryRead(out var msg))
                {
                    yield return msg;
                }
            }
        }
    }

    /// <summary>
    /// Supervises .NET processes: launch, monitor health, capture logs, and restart when necessary.
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

                var proc = Process.Start(psi);
                if (proc == null) continue;

                proc.OutputDataReceived += (sender, e) => _logStream.Push(serviceName, e.Data);
                proc.ErrorDataReceived += (sender, e) => _logStream.Push(serviceName, e.Data);
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                list.Add(proc);
            }

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
                        // ignore
                    }
                    finally
                    {
                        p.Dispose();
                        list.Remove(p);
                    }
                }
            }

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
                return new ServiceStatus { Name = name, RunningInstances = running, State = state };
            });

            return Task.FromResult(statuses);
        }
    }
}
