using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using System.Text.Json;

namespace BetterGenshinImpact.UnitTest.AutoPathing.Telemetry;

public class RouteGraphOverrideTests
{
    [Fact]
    public void Apply_DisablesBadEdgeAndAddsReviewedConnector()
    {
        var graph = CreateGraph();
        var patch = new RouteGraphOverridePatch
        {
            Id = "fix-road-001",
            BaseGraphId = graph.GraphId,
            Author = "tester",
            Reason = "remove invalid snap",
            Operations =
            [
                new RouteGraphOverrideOperation { Type = RouteGraphOverrideOperationType.DisableEdge, EdgeId = "edge" },
                new RouteGraphOverrideOperation
                {
                    Type = RouteGraphOverrideOperationType.AddEdge,
                    Edge = new RouteNavigationEdge
                    {
                        EdgeId = "manual-edge",
                        FromNodeId = "a",
                        ToNodeId = "b",
                        MapName = "Teyvat",
                        MoveMode = MoveModeEnum.Walk.Code,
                        ReviewStatus = GraphReviewStatus.Verified,
                        SourceKind = "manual-override"
                    }
                }
            ]
        };

        var result = new RouteGraphOverrideApplier().Apply(graph, [patch]);

        Assert.True(result.Succeeded);
        Assert.Equal(GraphReviewStatus.Disabled, graph.Edges.Single(edge => edge.EdgeId == "edge").ReviewStatus);
        Assert.Equal(RouteHealthStatus.Disabled, graph.Edges.Single(edge => edge.EdgeId == "edge").HealthStatus);
        Assert.Equal(GraphReviewStatus.Verified, graph.Edges.Single(edge => edge.EdgeId == "manual-edge").ReviewStatus);
        Assert.Equal(["fix-road-001"], result.AppliedPatchIds);
    }

    [Fact]
    public void Apply_IsolatesPatchWhenBaseGraphDoesNotMatch()
    {
        var graph = CreateGraph();
        var patch = new RouteGraphOverridePatch
        {
            Id = "stale-patch",
            BaseGraphId = "different-graph",
            Operations =
            [
                new RouteGraphOverrideOperation { Type = RouteGraphOverrideOperationType.DisableEdge, EdgeId = "edge" }
            ]
        };

        var result = new RouteGraphOverrideApplier().Apply(graph, [patch]);

        Assert.True(result.Succeeded);
        Assert.Empty(result.AppliedPatchIds);
        Assert.Equal(["stale-patch"], result.IsolatedPatchIds);
        Assert.Equal(GraphReviewStatus.Unreviewed, Assert.Single(graph.Edges).ReviewStatus);
    }

    [Fact]
    public void Apply_UsesDeterministicPatchOrder()
    {
        var graph = CreateGraph();
        var restore = new RouteGraphOverridePatch
        {
            Id = "02-restore",
            SourceFileName = "02-restore.json",
            Operations =
            [
                new RouteGraphOverrideOperation { Type = RouteGraphOverrideOperationType.RestoreEdge, EdgeId = "edge" }
            ]
        };
        var disable = new RouteGraphOverridePatch
        {
            Id = "01-disable",
            SourceFileName = "01-disable.json",
            Operations =
            [
                new RouteGraphOverrideOperation { Type = RouteGraphOverrideOperationType.DisableEdge, EdgeId = "edge" }
            ]
        };

        var result = new RouteGraphOverrideApplier().Apply(graph, [restore, disable]);

        Assert.True(result.Succeeded);
        Assert.Equal(GraphReviewStatus.Unreviewed, Assert.Single(graph.Edges).ReviewStatus);
        Assert.Equal(["01-disable", "02-restore"], result.AppliedPatchIds);
    }

    [Fact]
    public void Provider_AppliesSavedOverridesAndReloadsWhenPatchChanges()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var graph = CreateGraph();
            File.WriteAllText(
                Path.Combine(directory, RouteNavigationGraphBuilder.GraphFileName),
                JsonSerializer.Serialize(graph));
            var store = new RouteGraphOverrideStore(directory);
            var patch = new RouteGraphOverridePatch
            {
                Id = "disable-edge",
                BaseGraphId = graph.GraphId,
                Operations =
                [
                    new RouteGraphOverrideOperation
                    {
                        Type = RouteGraphOverrideOperationType.DisableEdge,
                        EdgeId = "edge"
                    }
                ]
            };
            store.Save(patch, "01-disable.json");
            var provider = new RouteNavigationGraphProvider(directory);

            Assert.True(provider.TryGetSnapshot(out var snapshot, out var status));
            Assert.Equal(RouteNavigationGraphLoadStatus.Loaded, status);
            Assert.Equal(GraphReviewStatus.Disabled, Assert.Single(snapshot.Edges).ReviewStatus);
            Assert.Equal(["disable-edge"], provider.LastOverrideApplyResult.AppliedPatchIds);

            patch.Operations[0].Type = RouteGraphOverrideOperationType.RestoreEdge;
            store.Save(patch, "01-disable.json");
            Assert.True(provider.TryGetSnapshot(out snapshot, out status));
            Assert.Equal(GraphReviewStatus.Unreviewed, Assert.Single(snapshot.Edges).ReviewStatus);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Provider_LoadsLegacyGraphWhenGeneratedGraphDoesNotExist()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(directory, RouteNavigationGraphBuilder.LegacyGraphFileName),
                JsonSerializer.Serialize(CreateGraph()));
            var provider = new RouteNavigationGraphProvider(directory);

            Assert.True(provider.TryGetSnapshot(out var snapshot, out var status));
            Assert.Equal(RouteNavigationGraphLoadStatus.Loaded, status);
            Assert.Equal(2, snapshot.Nodes.Count);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Save_RejectsStructurallyInvalidPatchBeforeWritingJson()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var store = new RouteGraphOverrideStore(directory);
            var patch = new RouteGraphOverridePatch
            {
                Id = "invalid-add-edge",
                Operations =
                [
                    new RouteGraphOverrideOperation
                    {
                        Type = RouteGraphOverrideOperationType.AddEdge,
                        Edge = null
                    }
                ]
            };

            var exception = Assert.Throws<InvalidOperationException>(() => store.Save(patch));

            Assert.Contains("addEdge", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(store.DirectoryPath) && Directory.EnumerateFiles(store.DirectoryPath, "*.json").Any());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Provider_ExposesActualJsonErrorAndFilePath()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, RouteNavigationGraphBuilder.GraphFileName), "{");
            var provider = new RouteNavigationGraphProvider(directory);

            Assert.False(provider.TryGetSnapshot(out _, out var status));
            Assert.Equal(RouteNavigationGraphLoadStatus.Invalid, status);
            Assert.Contains(RouteNavigationGraphBuilder.GraphFileName, provider.LastLoadError);
            Assert.Contains("JSON", provider.LastLoadError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Provider_WhenOverrideCannotBeRead_ReturnsInvalidInsteadOfThrowing()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(directory, RouteNavigationGraphBuilder.GraphFileName),
                JsonSerializer.Serialize(CreateGraph()));
            var provider = new RouteNavigationGraphProvider(directory);
            Directory.CreateDirectory(provider.OverrideDirectoryPath);
            var overridePath = Path.Combine(provider.OverrideDirectoryPath, "locked.json");
            File.WriteAllText(overridePath, "{}");

            bool succeeded = false;
            var status = default(RouteNavigationGraphLoadStatus);
            Exception? exception;
            using (new FileStream(overridePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                exception = Record.Exception(() =>
                    succeeded = provider.TryGetSnapshot(out _, out status));
            }

            Assert.Null(exception);
            Assert.False(succeeded);
            Assert.Equal(RouteNavigationGraphLoadStatus.Invalid, status);
            Assert.Contains(RouteNavigationGraphBuilder.GraphFileName, provider.LastLoadError);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Apply_CanEditNodeTypeTeleportAssociationAndDeleteConnectedNode()
    {
        var graph = CreateGraph();
        var patch = new RouteGraphOverridePatch
        {
            Id = "edit-node",
            Operations =
            [
                new RouteGraphOverrideOperation
                {
                    Type = RouteGraphOverrideOperationType.SetNodeType,
                    NodeId = "a",
                    NodeType = "path"
                },
                new RouteGraphOverrideOperation
                {
                    Type = RouteGraphOverrideOperationType.AssociateTeleport,
                    NodeId = "a",
                    TeleportAnchorId = "tp-1"
                },
                new RouteGraphOverrideOperation
                {
                    Type = RouteGraphOverrideOperationType.DeleteNode,
                    NodeId = "b"
                }
            ]
        };

        var result = new RouteGraphOverrideApplier().Apply(graph, [patch]);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Errors));
        Assert.Contains("tp-1", Assert.Single(graph.Nodes).AnchorIds);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void Apply_IsolatesPatchWhoseLaterOperationTargetsEdgeRemovedByNodeDelete()
    {
        var graph = CreateGraph();
        var patch = new RouteGraphOverridePatch
        {
            Id = "invalid-operation-order",
            Operations =
            [
                new RouteGraphOverrideOperation
                {
                    Type = RouteGraphOverrideOperationType.AddEdge,
                    Edge = new RouteNavigationEdge
                    {
                        EdgeId = "new-edge",
                        FromNodeId = "a",
                        ToNodeId = "b",
                        MapName = "Teyvat"
                    }
                },
                new RouteGraphOverrideOperation { Type = RouteGraphOverrideOperationType.DeleteNode, NodeId = "b" },
                new RouteGraphOverrideOperation
                {
                    Type = RouteGraphOverrideOperationType.SetEdgeReview,
                    EdgeId = "new-edge",
                    ReviewStatus = GraphReviewStatus.Verified
                }
            ]
        };

        var result = new RouteGraphOverrideApplier().Apply(graph, [patch]);

        Assert.False(result.Succeeded);
        Assert.Equal(["invalid-operation-order"], result.IsolatedPatchIds);
        Assert.Equal(2, graph.Nodes.Count);
        Assert.Single(graph.Edges);
    }

    [Fact]
    public void MoveNode_UpdatesIncomingOutgoingSelfLoopAndMalformedEdgeEndpoints()
    {
        var graph = new RouteNavigationGraph
        {
            Nodes =
            [
                new RouteNavigationNode { NodeId = "a", MapName = "Teyvat", X = 0, Y = 0 },
                new RouteNavigationNode { NodeId = "b", MapName = "Teyvat", X = 10, Y = 0 },
                new RouteNavigationNode { NodeId = "c", MapName = "Teyvat", X = 20, Y = 0 }
            ],
            Edges =
            [
                CreateEdge("incoming", "b", "a", 10, 0, 0, 0),
                CreateEdge("outgoing", "a", "c", 0, 0, 20, 0),
                CreateEdge("self", "a", "a", 0, 0, 0, 0),
                new RouteNavigationEdge
                {
                    EdgeId = "malformed",
                    FromNodeId = "a",
                    ToNodeId = "b",
                    MapName = "Teyvat",
                    Points = [new TelemetryPoint2D { X = 0, Y = 0 }]
                }
            ]
        };

        Assert.True(RouteGraphMutationService.MoveNode(graph, "a", 5, 6));

        Assert.Equal((5f, 6f), Endpoint(graph, "incoming", last: true));
        Assert.Equal((5f, 6f), Endpoint(graph, "outgoing", last: false));
        Assert.Equal((5f, 6f), Endpoint(graph, "self", last: false));
        Assert.Equal((5f, 6f), Endpoint(graph, "self", last: true));
        Assert.Equal((5f, 6f), Endpoint(graph, "malformed", last: false));
        Assert.Equal((10f, 0f), Endpoint(graph, "malformed", last: true));
    }

    [Fact]
    public void GraphIdentity_IgnoresReviewStateButChangesWithGeneratedGeometry()
    {
        var first = CreateGraph();
        first.Edges[0].Points =
        [
            new TelemetryPoint2D { X = 0, Y = 0 },
            new TelemetryPoint2D { X = 10, Y = 0 }
        ];
        var sameTopology = CreateGraph();
        sameTopology.GeneratedAtUtc = DateTime.UtcNow.AddDays(1);
        sameTopology.Edges[0].ReviewStatus = GraphReviewStatus.Verified;
        sameTopology.Edges[0].Points =
        [
            new TelemetryPoint2D { X = 0, Y = 0 },
            new TelemetryPoint2D { X = 10, Y = 0 }
        ];
        var changedGeometry = CreateGraph();
        changedGeometry.Edges[0].Points =
        [
            new TelemetryPoint2D { X = 0, Y = 0 },
            new TelemetryPoint2D { X = 5, Y = 4 },
            new TelemetryPoint2D { X = 10, Y = 0 }
        ];

        var firstId = RouteNavigationGraphIdentity.Compute(first);

        Assert.Equal(firstId, RouteNavigationGraphIdentity.Compute(sameTopology));
        Assert.NotEqual(firstId, RouteNavigationGraphIdentity.Compute(changedGeometry));
    }

    private static RouteNavigationGraph CreateGraph()
    {
        return new RouteNavigationGraph
        {
            SchemaVersion = 3,
            GraphId = "graph-1",
            Nodes =
            [
                new RouteNavigationNode { NodeId = "a", MapName = "Teyvat", X = 0, Y = 0 },
                new RouteNavigationNode { NodeId = "b", MapName = "Teyvat", X = 10, Y = 0 }
            ],
            Edges =
            [
                new RouteNavigationEdge
                {
                    EdgeId = "edge",
                    FromNodeId = "a",
                    ToNodeId = "b",
                    MapName = "Teyvat",
                    MoveMode = MoveModeEnum.Walk.Code,
                    ReviewStatus = GraphReviewStatus.Unreviewed
                }
            ]
        };
    }

    private static RouteNavigationEdge CreateEdge(
        string edgeId,
        string fromNodeId,
        string toNodeId,
        float fromX,
        float fromY,
        float toX,
        float toY)
    {
        return new RouteNavigationEdge
        {
            EdgeId = edgeId,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            MapName = "Teyvat",
            Points =
            [
                new TelemetryPoint2D { X = fromX, Y = fromY },
                new TelemetryPoint2D { X = toX, Y = toY }
            ]
        };
    }

    private static (float X, float Y) Endpoint(RouteNavigationGraph graph, string edgeId, bool last)
    {
        var points = graph.Edges.Single(edge => edge.EdgeId == edgeId).Points;
        var point = last ? points[^1] : points[0];
        return (point.X, point.Y);
    }
}
