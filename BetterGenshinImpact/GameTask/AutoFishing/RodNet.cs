using System;
using System.Linq;
using static TorchSharp.torch.nn;
using static TorchSharp.torch;
using TorchSharp.Modules;
using TorchSharp;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFishing;

/// <summary>
/// copy from https://github.com/myHuTao-qwq/HutaoFisher/blob/master/src/rodnet.cpp
/// 
/// 以下是hutaofisher的访谈：
/// 
/// 修改我的qq昵称: 03-20 00:22:48
/// 有任何问题可以问我 只要我在工位而不是在实验室里跟XXXX斗智斗勇就回答你
/// 
/// 额 我的算法有几个感觉挺符合直觉的假设
/// 
/// 就是 你抛竿的时候鱼是否会咬钩取决于鱼饵落点到鱼的距离
/// 
/// 这里的距离是鱼饵落点的(x, y, z)坐标与鱼的(x, y, z)坐标之差在xy平面(水平面)上的投影
/// 
/// 现在问题转化成了如何估计这两个坐标
/// 
/// 这里我们先约定x轴就是水平面上与屏幕长边平行的线 y轴就是屏幕短边的对于 剩下一个z就是游戏里面往天上的方向
/// 
/// 然后经验告诉我鱼饵落点环的x几乎为0
/// 
/// 剩下我们可以用鱼饵落点环的长和宽算出y和z
/// 
/// 再近似鱼的bouding box的中心就是鱼计算咬钩的那个点 同时近似一个大类里面的所有鱼的z是相同的(这个参数可学习)  于是我们也可以计算出鱼的xyz
/// 
/// 最后一步就是把对应的投影距离算出来 然后线性回归一下得到太近 刚好 太远三个类
/// 
/// tmd 今天我意识到 XXXX可不就是XXXX
/// 
/// 哦 到这一步以后剩下的就很弱智了 远了挪近一点 近了挪远一点 调调参差不多得了
/// </summary>
public class RodNet : Module<Tensor, Tensor>
{
    const double alpha = 1734.34 / 2.5;
    // fitted parameters
    static readonly double[] dz = {1.0307939, 1.5887239,  1.4377865, 0.8548809,
                                   1.8640924, -0.1687729, 1.8621461, 0.7167622,
                                   1.7071064, 1.8727832,  0.5531539};
    static readonly double[] h_coeff = {0.5840698,  0.8029298,  0.6090596,
                                        -0.1390072, 0.7214464,  -0.6076725,
                                        0.3286690,  -0.2991239, 0.6072225,
                                        0.7662407,  -0.3689651};
    static readonly double[,] weight = {{0.7779633, -1.7124480, 2.7366412},
                                          {-0.0381155, -1.6536976, 3.5904298},
                                          {0.1947731, -0.0445049, 0.8416666},
                                          {-0.0331017, -1.3641578, 1.2834741},
                                          {1.0268835, -1.6553984, 2.9930501},
                                          {0.0108103, -0.8515291, 1.0032536},
                                          {-0.0746362, -0.9677668, 0.7450780},
                                          {0.7382144, -9.5275803, 2.6134675},
                                          {-0.3597502, -1.7422760, 1.4354013},
                                          {-0.0578425, -2.0274212, 1.7173727},
                                          {-0.1225260, -1.0630554, 1.2958838}};
    static readonly double[,] bias = {{3.1733532, 9.3601589, -11.0612173},
                                        {6.4961057, 11.2683334, -13.7752209},
                                        {2.3662698, 2.4709859, -2.5402584},
                                        {2.4701204, 8.5112562, -7.6070199},
                                        {0.9597272, 8.9189463, -11.9037018},
                                        {2.1239815, 5.8446727, -5.7748013},
                                        {2.1403685, 5.5432696, -4.0048418},
                                        {-9.0128260, 28.4402637, -24.2205143},
                                        {5.2072763, 8.6428480, -9.2946615},
                                        {4.9253063, 11.4634714, -9.4336052},
                                        {5.2460732, 7.7711511, -7.5998945}};


    static readonly double[] offset = { 0.8, 0.4, 0.35, 0.35, 0.6, 0.3, 0.3, 0.8, 0.8, 0.8, 0.8 };

    private readonly Module<Tensor, Tensor> layers;

    public RodNet() : base("RodNet")
    {
        var weight = tensor(RodNet.weight, ScalarType.Float64);
        var bias = tensor(RodNet.bias, ScalarType.Float64);

        RodLayer1 rodLayer1 = new RodLayer1(num_embeddings: weight.shape[0], embedding_dim: weight.shape[1], input_dim: 3, output_dim: 3);
        rodLayer1.SetWeightsManually(weight, bias);

        var modules = new List<(string, Module<Tensor, Tensor>)>
        {
            ($"rodLayer1", rodLayer1),
            ($"softmax", nn.Softmax(1))
        };

        layers = Sequential(modules);

        RegisterComponents();
    }

    static void Softmax(double[] dst, double[] x, int n)
    {
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            dst[i] = Math.Exp(x[i]);
            sum += dst[i];
        }
        for (int i = 0; i < n; i++)
        {
            dst[i] /= sum;
        }
    }
    public record NetInput(double dist, int fish_label);
    public static NetInput? GeometryProcessing(RodInput input)
    {
        double a, b, v0, u, v, h;

        a = (input.rod_x2 - input.rod_x1) / 2 / alpha;
        b = (input.rod_y2 - input.rod_y1) / 2 / alpha;
        h = (input.fish_y2 - input.fish_y1) / 2 / alpha;

        if (a < b)
        {
            b = Math.Sqrt(a * b);
            a = b + 1e-6;
        }

        v0 = (288 - (input.rod_y1 + input.rod_y2) / 2) / alpha;

        u = (input.fish_x1 + input.fish_x2 - input.rod_x1 - input.rod_x2) / 2 / alpha;
        v = (288 - (input.fish_y1 + input.fish_y2) / 2) / alpha;
        v -= h * h_coeff[input.fish_label];

        double y0, z0, t;
        double x, y, dist;

        y0 = Math.Sqrt(Math.Pow(a, 4) - b * b + a * a * (1 - b * b + v0 * v0)) / (a * a);
        z0 = b / (a * a);
        t = a * a * (y0 * b + v0) / (a * a - b * b);

        x = u * (z0 + dz[input.fish_label]) * Math.Sqrt(1 + t * t) / (t - v);
        y = (z0 + dz[input.fish_label]) * (1 + t * v) / (t - v);
        dist = Math.Sqrt(x * x + (y - y0) * (y - y0));

        return new NetInput(dist, input.fish_label);
    }

    internal static int GetRodState(RodInput input)
    {
        NetInput? netInput = GeometryProcessing(input);
        if (netInput is null)
        {
            return -1;
        }

        double[] pred = ComputeScores(netInput);

        return Array.IndexOf(pred, pred.Max());
    }

    public static double[] ComputeScores(NetInput netInput)
    {
        double dist = netInput.dist;
        int fish_label = netInput.fish_label;

        double[] logits = new double[3];
        for (int i = 0; i < 3; i++)
        {
            logits[i] = weight[fish_label, i] * dist + bias[fish_label, i];
        }

        double[] pred = new double[3];
        Softmax(pred, logits, 3);
        pred[0] -= offset[fish_label]; // to make the prediction more precise when deployed

        return pred;
    }

    internal int GetRodState_Torch(RodInput input)
    {
        NetInput? netInput = GeometryProcessing(input);
        if (netInput is null)
        {
            return -1;
        }

        Tensor outputTensor = ComputeScores_Torch(netInput);

        var max = argmax(outputTensor);
        return (int)max.item<long>();
    }

    public Tensor ComputeScores_Torch(NetInput netInput)
    {
        double dist = netInput.dist;
        int fish_label = netInput.fish_label;

        Tensor inputTensor = cat([tensor(new double[,] { { dist } }, dtype: ScalarType.Float64),
            tensor(new int[,] { {fish_label } }, dtype: ScalarType.Int32)]).T;
        var outputTensor = forward(inputTensor);

        outputTensor[0][0] = outputTensor[0][0] - RodNet.offset[fish_label];

        return outputTensor;
    }

    public override Tensor forward(Tensor input)
    {
        return layers.forward(input);
    }
}

public class RodLayer1 : Module<Tensor, Tensor>
{
    private readonly Embedding embedding1;
    private readonly Embedding embedding2;
    private readonly Linear linear;
    public RodLayer1(long num_embeddings, long embedding_dim, long input_dim, long output_dim)
        : base("RodLinear")
    {
        embedding1 = torch.nn.Embedding(num_embeddings, embedding_dim);
        embedding2 = torch.nn.Embedding(num_embeddings, embedding_dim);
        linear = torch.nn.Linear(input_dim, output_dim);

        RegisterComponents();
    }

    public void SetWeightsManually(Tensor weight, Tensor bias)
    {
        embedding1.weight = new Parameter(weight);
        embedding2.weight = new Parameter(bias);
    }

    public override Tensor forward(Tensor input)
    {
        var splitInput = input.split([1, 1], dim: 1);
        var dist = splitInput[0];
        var fish_label = splitInput[1].to(ScalarType.Int32).flatten();

        var embed1 = embedding1.forward(fish_label);
        //Console.WriteLine(String.Join(",", embed1.data<double>()));
        var embed2 = embedding2.forward(fish_label);
        //Console.WriteLine(String.Join(",", embed2.data<double>()));

        linear.weight = new Parameter(embed1.T);
        linear.bias = new Parameter(embed2);

        return linear.forward(dist);
    }
}