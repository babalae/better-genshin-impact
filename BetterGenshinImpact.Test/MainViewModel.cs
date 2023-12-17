using OxyPlot;
using OxyPlot.Series;
using OxyPlot.Wpf;

namespace BetterGenshinImpact.Test;

internal class MainViewModel
{

    public PlotModel LeftModel { get; private set; }
    public PlotModel RightModel { get; private set; }
    public PlotModel AllModel { get; private set; }

    public MainViewModel()
    {
        var data = CameraOrientationTest.Test1();

        LeftModel = BuildModel(data.Item1,"左");
        RightModel = BuildModel(data.Item2, "右(左移90度后)");
        AllModel = BuildModel(data.Item3, "乘积");
    }

    public PlotModel BuildModel(int[] data, string name)
    {
        // 创建折线图
        var series = new LineSeries();
        for (int i = 0; i < data.Length; i++)
        {
            series.Points.Add(new DataPoint(i, data[i]));
        }

        // 创建绘图模型并添加折线图
        var plotModel = new PlotModel();
        plotModel.Series.Add(series);

        // 设置图表标题
        plotModel.Title = name;
        return plotModel;
    }

}