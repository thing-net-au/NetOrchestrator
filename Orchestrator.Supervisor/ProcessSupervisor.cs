using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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


        private const int CTRL_C_EVENT = 0;
        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handlerRoutine, bool add);

        private delegate bool ConsoleCtrlDelegate(uint ctrlType);


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

        private static string Sanitize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            return input.Replace("\u0000", "").Replace("\r", "\\r").Replace("\n", "\\n").Trim();
        }

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
              //     continue;
                }
                if (!string.IsNullOrEmpty(cfg.WorkingDirectory)
                    && !Directory.Exists(cfg.WorkingDirectory))
                {
                    var msg = new ConsoleLogMessage
                    {
                        Name = "_supervisor",
                        PID = 0,
                        Details = $"Working dir '{cfg.WorkingDirectory}' not found."
                    };
                    _ = _client.SendAsync(new Envelope("ConsoleLogMessage", msg));
               //     continue;
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

                    string capturedServiceName = serviceName;

                    proc.Exited += async (s, e) =>
                    {
                        await Task.Delay(100); // allow buffer flush

                        var exitMsg = new ConsoleLogMessage
                        {
                            Name = "_supervisor",
                            PID = proc.Id,
                            Details = $"Process exited with code {proc.ExitCode}"
                        };
                        _ = _client.SendAsync(new Envelope("ConsoleLogMessage", exitMsg));
                        ReportServiceStatus(capturedServiceName);
                        list.Remove(proc);
                    };

                    proc.OutputDataReceived += async (s, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                        {
                            var msg = new ConsoleLogMessage
                            {
                                Name = capturedServiceName,
                                PID = proc.Id,
                                Details = Sanitize(e.Data)
                            };
                            await _client.SendAsync(new Envelope("ConsoleLogMessage", msg));
                            await Task.Delay(10); // backoff to avoid congestion
                        }
                    };

                    proc.ErrorDataReceived += async (s, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                        {
                            var msg = new ConsoleLogMessage
                            {
                                Name = capturedServiceName,
                                PID = proc.Id,
                                Details = Sanitize(e.Data)
                            };
                            await _client.SendAsync(new Envelope("ConsoleLogMessage", msg));
                            await Task.Delay(10); // backoff to avoid congestion
                        }
                    };

                    _ = _client.SendAsync(new Envelope("ConsoleLogMessage", new ConsoleLogMessage
                    {
                        Name = "_supervisor",
                        PID = 0,
                        Details = $"Starting {capturedServiceName} in '{psi.WorkingDirectory}'"
                    }));
                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    list.Add(proc);

                    _ = _client.SendAsync(new Envelope("ConsoleLogMessage", new ConsoleLogMessage
                    {
                        Name = "_supervisor",
                        PID = proc.Id,
                        Details = $"Started {capturedServiceName} (pid={proc.Id})"
                    }));
                }
                catch (Exception ex)
                {
                    _ = _client.SendAsync(new Envelope("ConsoleLogMessage", new ConsoleLogMessage
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
                        _ = _client.SendAsync(new Envelope("ConsoleLogMessage", new ConsoleLogMessage
                        {
                            Name = serviceName,
                            PID = proc.Id,
                            Details = "Process killed"
                        }));
                     Thread.Sleep(250); // delay disposal to ensure stdout delivery
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
        /// <summary>
        /// Send Ctrl+C to each tracked process, wait up to 30s for it to exit, then kill any survivors.
        /// </summary>
        public async Task ShutdownAllAsync()
        {
            var all = _processes
                .SelectMany(kvp => kvp.Value)
                .Where(p => !p.HasExited)
                .ToList();

            foreach (var proc in all)
            {
                try
                {
                    // 1) Attach to the child’s console (or the parent if it has none)
                    if (!AttachConsole((uint)proc.Id))
                    {
                        // Fallback: attach to our own parent console
                        AttachConsole(ATTACH_PARENT_PROCESS);
                    }

                    // 2) Suppress our own handler so we don’t kill ourselves
                    SetConsoleCtrlHandler(null, true);

                    // 3) Send Ctrl+C to the group
                    GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);

                    // 4) Detach and restore
                    FreeConsole();
                    SetConsoleCtrlHandler(null, false);
                }
                catch
                {
                    // ignore any P/Invoke failures
                }
            }

            // 5) Wait for up to 30s for all to exit
            var tasks = all.Select(p => WaitForExitAsync(p, TimeSpan.FromSeconds(30))).ToArray();
            await Task.WhenAll(tasks);

            // 6) Kill any still-running processes
            foreach (var proc in all.Where(p => !p.HasExited))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Wrap Process.WaitForExitAsync with timeout.
        /// </summary>
        private static async Task WaitForExitAsync(Process proc, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<object?>();
            proc.EnableRaisingEvents = true;
            proc.Exited += (s, e) => tcs.TrySetResult(null);

            if (proc.HasExited)
                return;

            var delay = Task.Delay(timeout);
            var winner = await Task.WhenAny(tcs.Task, delay);
            // if delay won, we just return and caller will kill
        }
    }
}
