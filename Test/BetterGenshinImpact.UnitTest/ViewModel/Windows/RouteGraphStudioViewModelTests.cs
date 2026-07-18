using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using BetterGenshinImpact.ViewModel.Windows;

namespace BetterGenshinImpact.UnitTest.ViewModel.Windows;

public sealed class RouteGraphStudioViewModelTests
{
    [Fact]
    public void FinishPathDrawing_CreatesPatchNodesAndBidirectionalEdges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bgi-graph-studio-" + Guid.NewGuid().ToString("N"));
        var viewModel = new RouteGraphStudioViewModel(directory, "Teyvat")
        {
            AddBidirectionalEdge = true,
            DrawSnapDistance = 2
        };

        viewModel.StartPathDrawingCommand.Execute(null);
        viewModel.AddDrawPathPointCommand.Execute(new RouteGraphPoint(100, 100));
        viewModel.AddDrawPathPointCommand.Execute(new RouteGraphPoint(110, 100));
        viewModel.FinishPathDrawingCommand.Execute(null);

        Assert.False(viewModel.IsPathDrawing);
        Assert.Empty(viewModel.DraftPathPoints);
        Assert.Equal(2, viewModel.VisibleNodes.Count);
        Assert.Equal(2, viewModel.VisibleEdges.Count);
        Assert.Equal(4, viewModel.PendingOperationCount);
        Assert.All(viewModel.VisibleNodes, node => Assert.Equal("path", node.NodeType));
        Assert.Contains(viewModel.VisibleEdges, edge => edge.FromNodeId == viewModel.VisibleNodes[0].NodeId);
        Assert.Contains(viewModel.VisibleEdges, edge => edge.ToNodeId == viewModel.VisibleNodes[0].NodeId);
    }
}
