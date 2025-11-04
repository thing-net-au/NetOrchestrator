using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orchestrator.Peer
{
/// <summary>
    /// Background service that manages P2P networking and state synchronization
    /// </summary>
    public class PeerNetworkService : BackgroundService, IInternalHealth
    {
      private readonly ILogger<PeerNetworkService> _logger;
        private readonly IPeerDiscoveryService _discovery;
        private readonly IPeerStateManager _stateManager;
   private readonly IProcessSupervisor _supervisor;
        private DateTime _lastActivity = DateTime.UtcNow;

public PeerNetworkService(
   ILogger<PeerNetworkService> _logger,
          IPeerDiscoveryService discovery,
            IPeerStateManager stateManager,
            IProcessSupervisor supervisor)
        {
       this._logger = _logger;
       _discovery = discovery;
            _stateManager = stateManager;
 _supervisor = supervisor;

// Subscribe to events
            _discovery.PeerDiscovered += OnPeerDiscovered;
            _discovery.PeerLost += OnPeerLost;
            _stateManager.RemoteServiceStatusChanged += OnRemoteStatusChanged;
    }

 public InternalStatus GetStatus() => new InternalStatus
        {
        Name = "PeerNetwork",
    IsHealthy = true,
  Details = $"Peers: {_discovery.GetPeers().Count()}, Last activity: {_lastActivity:O}",
          Timestamp = DateTime.UtcNow
        };

        public override async Task StartAsync(CancellationToken ct)
        {
 _logger.LogInformation("Starting P2P network service");
         await _discovery.StartAsync(ct);
            await base.StartAsync(ct);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("P2P network service running");

       // Periodically sync local state with peers
         while (!stoppingToken.IsCancellationRequested)
    {
     try
      {
     await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

         // Get local service statuses and share with peers
          var statuses = await _supervisor.ListStatusAsync();
            foreach (var status in statuses)
        {
       await _stateManager.ShareServiceStatusAsync(status, stoppingToken);
          }

      _lastActivity = DateTime.UtcNow;
     }
       catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
   break;
      }
    catch (Exception ex)
           {
           _logger.LogWarning(ex, "Error in P2P sync loop");
      }
            }
        }

        public override async Task StopAsync(CancellationToken ct)
        {
      _logger.LogInformation("Stopping P2P network service");
            await _discovery.StopAsync(ct);
    await base.StopAsync(ct);
        }

 private void OnPeerDiscovered(PeerInfo peer)
        {
_logger.LogInformation("?? Peer discovered: {Hostname} ({IpAddress}) - {ServiceCount} services",
       peer.Hostname, peer.IpAddress, peer.ServiceCount);
            _lastActivity = DateTime.UtcNow;
  }

        private void OnPeerLost(PeerInfo peer)
        {
            _logger.LogWarning("? Peer lost: {Hostname} ({IpAddress})",
         peer.Hostname, peer.IpAddress);
            _lastActivity = DateTime.UtcNow;
        }

        private void OnRemoteStatusChanged(string peerId, ServiceStatus status)
        {
   _logger.LogDebug("?? Remote status update from {PeerId}: {Service} = {State}",
          peerId, status.Name, status.State);
      _lastActivity = DateTime.UtcNow;
        }

      public override void Dispose()
        {
            _discovery.PeerDiscovered -= OnPeerDiscovered;
            _discovery.PeerLost -= OnPeerLost;
        _stateManager.RemoteServiceStatusChanged -= OnRemoteStatusChanged;
     base.Dispose();
        }
    }
}
