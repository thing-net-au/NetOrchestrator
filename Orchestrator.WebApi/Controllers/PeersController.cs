using Microsoft.AspNetCore.Mvc;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Orchestrator.WebApi.Controllers
{
 [ApiController]
    [Route("api/peers")]
    public class PeersController : ControllerBase
    {
  private readonly IPeerDiscoveryService _discovery;
 private readonly IPeerStateManager _stateManager;

        public PeersController(
IPeerDiscoveryService discovery,
       IPeerStateManager stateManager)
  {
      _discovery = discovery;
          _stateManager = stateManager;
 }

        /// <summary>
        /// GET /api/peers
/// List all discovered peers in the network
   /// </summary>
        [HttpGet]
public ActionResult<IEnumerable<PeerInfo>> GetPeers()
{
  var peers = _discovery.GetPeers();
 return Ok(peers);
}

 /// <summary>
        /// GET /api/peers/local
        /// Get information about the local peer
     /// </summary>
[HttpGet("local")]
        public ActionResult<PeerInfo> GetLocalPeer()
 {
   return Ok(_discovery.LocalPeer);
   }

 /// <summary>
     /// GET /api/peers/{peerId}
  /// Get information about a specific peer
   /// </summary>
        [HttpGet("{peerId}")]
        public ActionResult<PeerInfo> GetPeer(string peerId)
  {
   var peer = _discovery.GetPeer(peerId);
    if (peer == null)
     return NotFound();

            return Ok(peer);
  }

        /// <summary>
        /// GET /api/peers/cluster/services
/// Get service status from all peers in the cluster
 /// </summary>
        [HttpGet("cluster/services")]
 public async Task<ActionResult<IEnumerable<ServiceStatus>>> GetClusterServices()
 {
 var statuses = await _stateManager.GetClusterServiceStatusAsync();
 return Ok(statuses);
        }

     /// <summary>
     /// POST /api/peers/{peerId}/command
     /// Execute a control command on a specific peer
        /// </summary>
      [HttpPost("{peerId}/command")]
        public async Task<ActionResult<object>> ExecuteRemoteCommand(
  string peerId,
       [FromBody] PeerControlCommand command)
  {
    var success = await _stateManager.ExecuteRemoteCommandAsync(peerId, command);
     return Ok(new { Success = success, CommandId = command.CommandId });
    }

/// <summary>
        /// POST /api/peers/cluster/command
     /// Execute a control command on all peers in the cluster
   /// </summary>
     [HttpPost("cluster/command")]
 public async Task<ActionResult<object>> ExecuteClusterCommand(
      [FromBody] PeerControlCommand command)
     {
       var results = await _stateManager.ExecuteClusterCommandAsync(command);
      return Ok(new 
       { 
        TotalPeers = results.Count,
 SuccessfulPeers = results.Count(r => r.Value),
 Results = results
   });
        }

        /// <summary>
   /// POST /api/peers/broadcast
        /// Broadcast a message to all peers
  /// </summary>
  [HttpPost("broadcast")]
     public async Task<ActionResult> BroadcastMessage([FromBody] PeerMessage message)
        {
     await _discovery.BroadcastAsync(message);
 return Ok();
}
    }
}
