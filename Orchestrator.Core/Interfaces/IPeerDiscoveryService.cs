using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces
{
    /// <summary>
    /// Service for peer-to-peer discovery and communication
    /// </summary>
    public interface IPeerDiscoveryService
    {
        /// <summary>Get local peer information</summary>
        PeerInfo LocalPeer { get; }

        /// <summary>Get all discovered peers</summary>
        IEnumerable<PeerInfo> GetPeers();

/// <summary>Get a specific peer by ID</summary>
        PeerInfo? GetPeer(string peerId);

        /// <summary>Send a message to all peers</summary>
        Task BroadcastAsync(PeerMessage message, CancellationToken ct = default);

        /// <summary>Send a message to a specific peer</summary>
        Task SendToAsync(string peerId, PeerMessage message, CancellationToken ct = default);

        /// <summary>Event fired when a new peer is discovered</summary>
        event Action<PeerInfo> PeerDiscovered;

        /// <summary>Event fired when a peer goes offline</summary>
        event Action<PeerInfo> PeerLost;

        /// <summary>Event fired when a message is received from a peer</summary>
        event Action<PeerMessage> MessageReceived;

        /// <summary>Start the discovery service</summary>
        Task StartAsync(CancellationToken ct = default);

 /// <summary>Stop the discovery service</summary>
        Task StopAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Service for managing peer state and synchronization
    /// </summary>
    public interface IPeerStateManager
    {
        /// <summary>Share local service status with peers</summary>
        Task ShareServiceStatusAsync(ServiceStatus status, CancellationToken ct = default);

/// <summary>Get aggregated service status across all peers</summary>
  Task<IEnumerable<ServiceStatus>> GetClusterServiceStatusAsync(CancellationToken ct = default);

        /// <summary>Execute a control command on a specific peer</summary>
        Task<bool> ExecuteRemoteCommandAsync(string peerId, PeerControlCommand command, CancellationToken ct = default);

        /// <summary>Execute a control command on all peers</summary>
        Task<Dictionary<string, bool>> ExecuteClusterCommandAsync(PeerControlCommand command, CancellationToken ct = default);

     /// <summary>Event fired when peer state changes</summary>
    event Action<string, ServiceStatus> RemoteServiceStatusChanged;
    }
}
