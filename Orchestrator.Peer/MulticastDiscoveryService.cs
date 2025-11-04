using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
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
    /// Multicast-based peer discovery service
    /// </summary>
    public class MulticastDiscoveryService : IPeerDiscoveryService, IDisposable
    {
     private readonly ILogger<MulticastDiscoveryService> _logger;
        private readonly PeerConfig _config;
        private readonly PeerInfo _localPeer;
     private readonly ConcurrentDictionary<string, PeerInfo> _peers = new();
        
    private UdpClient? _udpClient;
        private IPEndPoint? _multicastEndpoint;
      private CancellationTokenSource? _cts;
    private Task? _receiveTask;
        private Task? _heartbeatTask;

        public event Action<PeerInfo>? PeerDiscovered;
        public event Action<PeerInfo>? PeerLost;
     public event Action<PeerMessage>? MessageReceived;

        public PeerInfo LocalPeer => _localPeer;

 public MulticastDiscoveryService(
      ILogger<MulticastDiscoveryService> logger,
            PeerConfig? config = null)
        {
      _logger = logger;
        _config = config ?? OrchestratorConfig.Current.Peer;
        
            // Initialize local peer info
        _localPeer = new PeerInfo
            {
                PeerId = Guid.NewGuid().ToString(),
        Hostname = Environment.MachineName,
    IpAddress = GetLocalIPAddress(),
    ApiPort = OrchestratorConfig.Current.Web.ApiPort,
        IpcPort = 6000, // Default IPC port
           Status = PeerStatus.Online
          };

            // Add metadata from config
    foreach (var kvp in _config.Metadata)
     {
   _localPeer.Metadata[kvp.Key] = kvp.Value;
            }
        }

        public IEnumerable<PeerInfo> GetPeers()
        {
     CleanupStalePeers();
       return _peers.Values.Where(p => p.Status == PeerStatus.Online);
        }

        public PeerInfo? GetPeer(string peerId)
 {
       return _peers.TryGetValue(peerId, out var peer) ? peer : null;
      }

        public async Task StartAsync(CancellationToken ct = default)
        {
 if (!_config.Enabled)
            {
   _logger.LogInformation("P2P networking is disabled in configuration");
         return;
  }

          _logger.LogInformation("Starting multicast discovery on {Group}:{Port}", 
        _config.MulticastGroup, _config.MulticastPort);

          try
       {
     // Create multicast endpoint
     _multicastEndpoint = new IPEndPoint(
    IPAddress.Parse(_config.MulticastGroup), 
          _config.MulticastPort);

                // Create UDP client
 _udpClient = new UdpClient();
       _udpClient.Client.SetSocketOption(
   SocketOptionLevel.Socket, 
       SocketOptionName.ReuseAddress, 
       true);

           _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _config.MulticastPort));

      // Join multicast group
 var multicastAddress = IPAddress.Parse(_config.MulticastGroup);
    _udpClient.JoinMulticastGroup(multicastAddress);

  _logger.LogInformation("Joined multicast group {Group}", _config.MulticastGroup);

           // Start background tasks
          _cts = new CancellationTokenSource();
          _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token), _cts.Token);
    _heartbeatTask = Task.Run(() => HeartbeatLoop(_cts.Token), _cts.Token);

     // Announce presence
   await AnnounceAsync(_cts.Token);

       _logger.LogInformation("Multicast discovery started (Peer: {PeerId})", _localPeer.PeerId);
   }
            catch (Exception ex)
  {
    _logger.LogError(ex, "Failed to start multicast discovery");
           throw;
     }
    }

        public async Task StopAsync(CancellationToken ct = default)
        {
  _logger.LogInformation("Stopping multicast discovery");

 try
{
   // Send goodbye message
      if (_udpClient != null && _multicastEndpoint != null)
                {
     var goodbye = new PeerMessage
            {
               Type = PeerMessageType.Goodbye,
              SenderId = _localPeer.PeerId,
                 Payload = JsonSerializer.Serialize(_localPeer)
    };

                await SendMessageAsync(goodbye);
            }

     // Stop background tasks
                _cts?.Cancel();

     if (_receiveTask != null)
    await _receiveTask;
           if (_heartbeatTask != null)
      await _heartbeatTask;

    // Leave multicast group and cleanup
          _udpClient?.DropMulticastGroup(IPAddress.Parse(_config.MulticastGroup));
                _udpClient?.Close();
     _udpClient?.Dispose();

       _logger.LogInformation("Multicast discovery stopped");
            }
    catch (Exception ex)
    {
    _logger.LogWarning(ex, "Error during multicast discovery shutdown");
     }
        }

        public async Task BroadcastAsync(PeerMessage message, CancellationToken ct = default)
        {
      message.SenderId = _localPeer.PeerId;
            await SendMessageAsync(message);
        }

        public async Task SendToAsync(string peerId, PeerMessage message, CancellationToken ct = default)
    {
         message.SenderId = _localPeer.PeerId;
      message.TargetId = peerId;
            await SendMessageAsync(message);
     }

      private async Task SendMessageAsync(PeerMessage message)
      {
            if (_udpClient == null || _multicastEndpoint == null)
         return;

          try
{
      var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);

     await _udpClient.SendAsync(bytes, bytes.Length, _multicastEndpoint);
            }
      catch (Exception ex)
         {
       _logger.LogWarning(ex, "Failed to send multicast message");
      }
        }

        private async Task AnnounceAsync(CancellationToken ct)
        {
            var announce = new PeerMessage
    {
     Type = PeerMessageType.Announce,
             SenderId = _localPeer.PeerId,
                Payload = JsonSerializer.Serialize(_localPeer)
          };

            await SendMessageAsync(announce);
    _logger.LogInformation("Announced presence to network");
        }

        private async Task HeartbeatLoop(CancellationToken ct)
        {
            var interval = TimeSpan.FromMilliseconds(_config.HeartbeatInterval);

            while (!ct.IsCancellationRequested)
     {
          try
        {
           await Task.Delay(interval, ct);

              // Update local peer info
_localPeer.LastSeen = DateTimeOffset.UtcNow;
          _localPeer.ServiceCount = OrchestratorConfig.Current.Services.Count;

          var heartbeat = new PeerMessage
             {
     Type = PeerMessageType.Heartbeat,
            SenderId = _localPeer.PeerId,
      Payload = JsonSerializer.Serialize(_localPeer)
        };

             await SendMessageAsync(heartbeat);

                // Cleanup stale peers
      CleanupStalePeers();
       }
     catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
            break;
        }
  catch (Exception ex)
  {
          _logger.LogWarning(ex, "Error in heartbeat loop");
        }
    }
        }

private async Task ReceiveLoop(CancellationToken ct)
        {
      while (!ct.IsCancellationRequested && _udpClient != null)
       {
              try
          {
           var result = await _udpClient.ReceiveAsync();
  var json = Encoding.UTF8.GetString(result.Buffer);
          var message = JsonSerializer.Deserialize<PeerMessage>(json);

  if (message != null)
         {
            await HandleMessageAsync(message);
       }
          }
     catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
      break;
       }
 catch (Exception ex)
          {
         if (!ct.IsCancellationRequested)
      {
          _logger.LogWarning(ex, "Error receiving multicast message");
          }
       }
    }
 }

    private async Task HandleMessageAsync(PeerMessage message)
   {
            // Ignore our own messages
 if (message.SenderId == _localPeer.PeerId)
                return;

            // Check if message is targeted to someone else
    if (!string.IsNullOrEmpty(message.TargetId) && message.TargetId != _localPeer.PeerId)
              return;

         try
          {
switch (message.Type)
        {
         case PeerMessageType.Announce:
case PeerMessageType.Heartbeat:
  await HandlePeerUpdateAsync(message);
     break;

   case PeerMessageType.Goodbye:
          HandlePeerGoodbye(message);
     break;

            case PeerMessageType.StatusUpdate:
         case PeerMessageType.ControlCommand:
     case PeerMessageType.Query:
 case PeerMessageType.Response:
               // Raise event for higher-level handling
          MessageReceived?.Invoke(message);
             break;
     }
  }
          catch (Exception ex)
 {
         _logger.LogWarning(ex, "Error handling message from peer {PeerId}", message.SenderId);
            }
        }

private async Task HandlePeerUpdateAsync(PeerMessage message)
        {
   try
          {
        var peerInfo = JsonSerializer.Deserialize<PeerInfo>(message.Payload);
    if (peerInfo == null)
   return;

             var isNew = !_peers.ContainsKey(peerInfo.PeerId);

             peerInfo.LastSeen = DateTimeOffset.UtcNow;
          peerInfo.Status = PeerStatus.Online;
       _peers[peerInfo.PeerId] = peerInfo;

        if (isNew)
             {
    _logger.LogInformation("Discovered new peer: {PeerId} ({Hostname})", 
peerInfo.PeerId, peerInfo.Hostname);
  PeerDiscovered?.Invoke(peerInfo);

       // Respond with our announcement if this is their announce
                    if (message.Type == PeerMessageType.Announce)
         {
          await Task.Delay(Random.Shared.Next(100, 500)); // Stagger responses
          await AnnounceAsync(CancellationToken.None);
      }
  }
    }
   catch (Exception ex)
    {
    _logger.LogWarning(ex, "Failed to handle peer update");
            }
   }

        private void HandlePeerGoodbye(PeerMessage message)
        {
            try
  {
        var peerInfo = JsonSerializer.Deserialize<PeerInfo>(message.Payload);
         if (peerInfo != null && _peers.TryRemove(peerInfo.PeerId, out var removed))
    {
    _logger.LogInformation("Peer departed: {PeerId} ({Hostname})", 
            removed.PeerId, removed.Hostname);
         removed.Status = PeerStatus.Offline;
        PeerLost?.Invoke(removed);
    }
            }
       catch (Exception ex)
   {
     _logger.LogWarning(ex, "Failed to handle peer goodbye");
            }
        }

        private void CleanupStalePeers()
        {
            var timeout = TimeSpan.FromMilliseconds(_config.PeerTimeout);
 var stale = _peers.Values
    .Where(p => !p.IsAlive(timeout))
                .ToList();

            foreach (var peer in stale)
 {
          if (_peers.TryRemove(peer.PeerId, out var removed))
        {
     _logger.LogWarning("Peer timeout: {PeerId} ({Hostname})", 
      removed.PeerId, removed.Hostname);
        removed.Status = PeerStatus.Offline;
                 PeerLost?.Invoke(removed);
    }
            }
}

        private string GetLocalIPAddress()
    {
            try
      {
        var host = Dns.GetHostEntry(Dns.GetHostName());
    var ipAddress = host.AddressList
          .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork 
    && !IPAddress.IsLoopback(ip));

     return ipAddress?.ToString() ?? "127.0.0.1";
     }
      catch
      {
 return "127.0.0.1";
        }
        }

        public void Dispose()
     {
          StopAsync().Wait();
         _cts?.Dispose();
     _udpClient?.Dispose();
     }
    }
}
