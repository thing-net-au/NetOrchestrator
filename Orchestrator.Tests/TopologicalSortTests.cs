using System;
using System.Collections.Generic;
using System.Linq;
using Orchestrator.Core;
using Xunit;

namespace Orchestrator.Tests
{
    public class TopologicalSortTests
    {
        [Fact]
        public void Sort_NoDependencies_ReturnsSameServices()
        {
            var graph = new Dictionary<string, string[]>
            {
                { "A", Array.Empty<string>() },
                { "B", Array.Empty<string>() },
                { "C", Array.Empty<string>() }
            };
            var result = TopologicalSort.Sort(graph);
            Assert.Equal(3, result.Count);
            Assert.Contains("A", result);
            Assert.Contains("B", result);
            Assert.Contains("C", result);
        }

        [Fact]
        public void Sort_WithDependencies_DependencyComesFirst()
        {
            var graph = new Dictionary<string, string[]>
            {
                { "App", new[] { "Database" } },
                { "Database", Array.Empty<string>() }
            };
            var result = TopologicalSort.Sort(graph);
            Assert.Equal(2, result.Count);
            var dbIdx = result.ToList().IndexOf("Database");
            var appIdx = result.ToList().IndexOf("App");
            Assert.True(dbIdx < appIdx, "Database should start before App.");
        }

        [Fact]
        public void Sort_ChainedDependencies_CorrectOrder()
        {
            var graph = new Dictionary<string, string[]>
            {
                { "C", new[] { "B" } },
                { "B", new[] { "A" } },
                { "A", Array.Empty<string>() }
            };
            var result = TopologicalSort.Sort(graph);
            var list = result.ToList();
            Assert.Equal(0, list.IndexOf("A"));
            Assert.Equal(1, list.IndexOf("B"));
            Assert.Equal(2, list.IndexOf("C"));
        }

        [Fact]
        public void Sort_CircularDependency_ThrowsInvalidOperationException()
        {
            var graph = new Dictionary<string, string[]>
            {
                { "A", new[] { "B" } },
                { "B", new[] { "A" } }
            };
            Assert.Throws<InvalidOperationException>(() => TopologicalSort.Sort(graph));
        }

        [Fact]
        public void Sort_DiamondDependency_AllNodesReturned()
        {
            // A depends on B and C; B depends on D; C depends on D
            var graph = new Dictionary<string, string[]>
            {
                { "A", new[] { "B", "C" } },
                { "B", new[] { "D" } },
                { "C", new[] { "D" } },
                { "D", Array.Empty<string>() }
            };
            var result = TopologicalSort.Sort(graph);
            Assert.Equal(4, result.Count);
            // D must come first
            Assert.Equal(0, result.ToList().IndexOf("D"));
            // A must come last
            Assert.Equal(3, result.ToList().IndexOf("A"));
        }

        [Fact]
        public void Sort_EmptyGraph_ReturnsEmpty()
        {
            var graph = new Dictionary<string, string[]>();
            var result = TopologicalSort.Sort(graph);
            Assert.Empty(result);
        }
    }
}
