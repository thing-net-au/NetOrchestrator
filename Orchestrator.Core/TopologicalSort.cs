using System;
using System.Collections.Generic;
using System.Linq;

namespace Orchestrator.Core
{
    /// <summary>
    /// Provides Kahn's algorithm for topological ordering of service dependency graphs.
    /// </summary>
    public static class TopologicalSort
    {
        /// <summary>
        /// Returns service names sorted so every dependency comes before the service that depends on it.
        /// </summary>
        /// <param name="graph">Map from service name to the names of its dependencies.</param>
        /// <exception cref="InvalidOperationException">Thrown when a circular dependency is detected.</exception>
        public static IReadOnlyList<string> Sort(IDictionary<string, string[]> graph)
        {
            // Build in-degree counts and adjacency lists (dependency -> dependents)
            var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in graph.Keys)
            {
                if (!inDegree.ContainsKey(node)) inDegree[node] = 0;
                if (!dependents.ContainsKey(node)) dependents[node] = new List<string>();
            }

            foreach (var (service, deps) in graph)
            {
                foreach (var dep in deps)
                {
                    if (!inDegree.ContainsKey(dep)) inDegree[dep] = 0;
                    if (!dependents.ContainsKey(dep)) dependents[dep] = new List<string>();

                    inDegree[service] = inDegree.GetValueOrDefault(service, 0) + 1;
                    dependents[dep].Add(service);
                }
            }

            // Start with nodes that have no dependencies
            var queue = new Queue<string>(
                inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key).OrderBy(n => n));
            var result = new List<string>(graph.Count);

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                result.Add(node);

                foreach (var dependent in dependents[node].OrderBy(n => n))
                {
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0)
                        queue.Enqueue(dependent);
                }
            }

            if (result.Count != inDegree.Count)
            {
                var cycle = string.Join(", ", inDegree
                    .Where(kv => kv.Value > 0)
                    .Select(kv => kv.Key));
                throw new InvalidOperationException(
                    $"Circular dependency detected among services: {cycle}");
            }

            return result;
        }
    }
}
