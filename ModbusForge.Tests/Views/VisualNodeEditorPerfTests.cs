using System.Collections.Generic;
using System.Linq;
using ModbusForge.Models;
using ModbusForge.Views;
using Xunit;

namespace ModbusForge.Tests.Views
{
    public class VisualNodeEditorPerfTests
    {
        [Fact]
        public void GetConnectionsForNode_ShouldOnlyReturnTargetedConnections()
        {
            // Arrange: create a graph with 50 nodes and 100 connections
            var connections = new List<NodeConnection>();

            // Central node we will "drag"
            var targetNodeId = "Node-0";

            // Add 10 connections tied to the target node
            for (int i = 1; i <= 10; i++)
            {
                var conn1 = new NodeConnection(targetNodeId, $"Node-{i}");
                conn1.Id = $"Conn-Target-Src-{i}";
                connections.Add(conn1);

                var conn2 = new NodeConnection($"Node-{i + 10}", targetNodeId);
                conn2.Id = $"Conn-Target-Dst-{i}";
                connections.Add(conn2);
            }

            // Add 80 background connections not tied to the target node
            for (int i = 1; i <= 80; i++)
            {
                var conn = new NodeConnection($"Node-Bg-{i}", $"Node-Bg-{i+1}");
                conn.Id = $"Conn-Bg-{i}";
                connections.Add(conn);
            }

            Assert.Equal(100, connections.Count);

            // Simulate 100 mouse move events dragging targetNodeId
            int operationsProcessed = 0;
            int simulatedFrames = 100;

            for (int frame = 0; frame < simulatedFrames; frame++)
            {
                // Act: get the localized connections for this node
                var nodeConnections = VisualNodeEditor.GetConnectionsForNode(connections, targetNodeId).ToList();

                // Assert per-frame expectations
                Assert.Equal(20, nodeConnections.Count);
                operationsProcessed += nodeConnections.Count;

                foreach(var c in nodeConnections)
                {
                    Assert.True(c.SourceNodeId == targetNodeId || c.TargetNodeId == targetNodeId);
                }
            }

            // Overall assert: Before this fix, operations would have been 100 * 100 = 10,000
            // After this fix, it should strictly be 20 * 100 = 2,000
            Assert.Equal(2000, operationsProcessed);
        }
    }
}
