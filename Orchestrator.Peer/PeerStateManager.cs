using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Peer
{
 /// <summary>
    /// Manages peer state synchronization and remote command execution
    /// </summary>
    public class PeerStateManager : IPeerStateManager, IDisposable
    {
     private readonly ILogger<PeerStateManager> _logger;
        private readonly IPeerDiscoveryService _discovery;
        private readonly PeerConfig _config;
  private readonly ConcurrentDictionary<string, Dictionary<string, ServiceStatus>> _peerStates = new();
 private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingCommands = new();

public event Action<string, ServiceStatus>? RemoteServiceStatusChanged;

   public PeerStateManager(
ILogger<PeerStateManager> logger,
IPeerDiscoveryService discovery)
{
   _logger = logger;
      _discovery = discovery;
         _config = OrchestratorConfig.Current.Peer;

       // Subscribe to discovery events
_discovery.MessageReceived += OnMessageReceived;
  _discovery.PeerLost += OnPeerLost;
     }

     public async Task ShareServiceStatusAsync(ServiceStatus status, CancellationToken ct = default)
        {
  if (!_config.Enabled || !_config.AutoSync)
  return;

  try
   {
  var message = new PeerMessage
     {
Type = PeerMessageType.StatusUpdate,
    Payload = JsonSerializer.Serialize(status)
   };

     await _discovery.BroadcastAsync(message, ct);
    }
    catch (Exception ex)
    {
 _logger.LogWarning(ex, "Failed to share service status for {ServiceName}", status.Name);
    }
 }

public async Task<IEnumerable<ServiceStatus>> GetClusterServiceStatusAsync(CancellationToken ct = default)
 {
            if (!_config.Enabled)
    return Enumerable.Empty<ServiceStatus>();

  var allStatuses = new List<ServiceStatus>();

 // Aggregate statuses from all peers
  foreach (var peerStates in _peerStates.Values)
      {
    allStatuses.AddRange(peerStates.Values);
     }

  return allStatuses;
        }

  public async Task<bool> ExecuteRemoteCommandAsync(
       string peerId, 
 PeerControlCommand command, 
   CancellationToken ct = default)
{
   if (!_config.Enabled || !_config.EnableClusterCommands)
{
_logger.LogWarning("Cluster commands are disabled");
      return false;
   }

   try
 {
var tcs = new TaskCompletionSource<bool>();
     _pendingCommands[command.CommandId] = tcs;

   var message = new PeerMessage
  {
   Type = PeerMessageType.ControlCommand,
Payload = JsonSerializer.Serialize(command),
  TargetId = peerId
     };

      await _discovery.SendToAsync(peerId, message, ct);

   // Wait for response with timeout
    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), ct);
  var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

   if (completedTask == tcs.Task)
     {
      return await tcs.Task;
     }
      else
        {
   _logger.LogWarning("Timeout waiting for response from peer {PeerId}", peerId);
     return false;
   }
   }
     catch (Exception ex)
       {
    _logger.LogError(ex, "Failed to execute remote command on peer {PeerId}", peerId);
  return false;
    }
finally
      {
 _pendingCommands.TryRemove(command.CommandId, out _);
    }
        }

        public async Task<Dictionary<string, bool>> ExecuteClusterCommandAsync(
  PeerControlCommand command, 
  CancellationToken ct = default)
   {
      var results = new Dictionary<string, bool>();
 var peers = _discovery.GetPeers().ToList();

       if (!peers.Any())
      {
   _logger.LogInformation("No peers available for cluster command");
     return results;
   }

     _logger.LogInformation("Executing cluster command {Action} for {Service} on {PeerCount} peers",
command.Action, command.ServiceName, peers.Count);

            // Execute command on all peers in parallel
       var tasks = peers.Select(async peer =>
    {
         var success = await ExecuteRemoteCommandAsync(peer.PeerId, command, ct);
        return (peer.PeerId, success);
     });

            var completedTasks = await Task.WhenAll(tasks);

   foreach (var (peerId, success) in completedTasks)
  {
      results[peerId] = success;
}

var successCount = results.Count(r => r.Value);
    _logger.LogInformation("Cluster command completed: {SuccessCount}/{TotalCount} succeeded",
  successCount, results.Count);

   return results;
        }

  private void OnMessageReceived(PeerMessage message)
  {
   try
     {
  switch (message.Type)
   {
     case PeerMessageType.StatusUpdate:
 HandleStatusUpdate(message);
   break;

       case PeerMessageType.ControlCommand:
      HandleControlCommand(message);
break;

     case PeerMessageType.Response:
    HandleCommandResponse(message);
    break;
      }
  }
  catch (Exception ex)
  {
     _logger.LogWarning(ex, "Error processing peer message");
    }
        }

   private void HandleStatusUpdate(PeerMessage message)
{
  try
{
  var status = JsonSerializer.Deserialize<ServiceStatus>(message.Payload);
       if (status == null)
      return;

   // Store the status from this peer
    if (!_peerStates.ContainsKey(message.SenderId))
   {
       _peerStates[message.SenderId] = new Dictionary<string, ServiceStatus>();
            }

    _peerStates[message.SenderId][status.Name] = status;

   // Raise event for listeners
 RemoteServiceStatusChanged?.Invoke(message.SenderId, status);

 _logger.LogDebug("Received status update for {Service} from peer {PeerId}: {State}, {Instances} instances",
  status.Name, message.SenderId, status.State, status.RunningInstances);
  }
  catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to handle status update from peer {SenderId}", message.SenderId);
   }
    }

  private void HandleControlCommand(PeerMessage message)
   {
try
     {
       var command = JsonSerializer.Deserialize<PeerControlCommand>(message.Payload);
    if (command == null)
  return;

  _logger.LogInformation("Received remote command from {SenderId}: {Action} {Service}",
  message.SenderId, command.Action, command.ServiceName);

     // TODO: Execute command locally via IProcessSupervisor
      // For now, just send a success response
            var response = new PeerMessage
  {
Type = PeerMessageType.Response,
     Payload = JsonSerializer.Serialize(new { CommandId = command.CommandId, Success = true }),
     TargetId = message.SenderId
       };

  _discovery.SendToAsync(message.SenderId, response).Wait();
   }
    catch (Exception ex)
     {
  _logger.LogError(ex, "Failed to handle control command from peer {SenderId}", message.SenderId);
   }
        }

  private void HandleCommandResponse(PeerMessage message)
{
try
   {
  var response = JsonSerializer.Deserialize<Dictionary<string, object>>(message.Payload);
    if (response != null && response.TryGetValue("CommandId", out var cmdIdObj))
 {
          var commandId = cmdIdObj.ToString();
    if (commandId != null && _pendingCommands.TryGetValue(commandId, out var tcs))
    {
    var success = response.TryGetValue("Success", out var successObj) 
   && successObj is bool b && b;
  tcs.TrySetResult(success);
      }
      }
    }
    catch (Exception ex)
{
  _logger.LogWarning(ex, "Failed to handle command response from peer {SenderId}", message.SenderId);
}
      }

        private void OnPeerLost(PeerInfo peer)
     {
          // Remove all states from lost peer
 _peerStates.TryRemove(peer.PeerId, out _);
_logger.LogInformation("Removed state for lost peer {PeerId}", peer.PeerId);
  }

public void Dispose()
        {
       _discovery.MessageReceived -= OnMessageReceived;
     _discovery.PeerLost -= OnPeerLost;
  }
    }
}
