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
/// 
/// *后来又新增了一些访谈内容：
/// 
/// 额 总之就是要求不能把不咬钩的识别成咬钩的 但是咬钩的可以识别成不咬钩的
/// 
/// 然后就可视化一下onehot在不同距离的结果 加一个offset使得模型输出的结果在保证可以predict距离正好的结果的同时距离范围尽可能小
/// </summary>
public class RodNet : Module<Tensor, Tensor, Tensor, Tensor, Tensor>
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

    private Parameter thetaParameter;
    private Parameter bParameter;
    private Parameter dzParameter;
    private Parameter hCoeffParameter;

    public RodNet() : base("RodNet")
    {
        long num_embeddings = RodNet.weight.GetLength(0);
        long embedding_dim = 3;

        this.thetaParameter = new Parameter(torch.randn(num_embeddings, embedding_dim, dtype: ScalarType.Float64));
        this.bParameter = new Parameter(torch.randn(num_embeddings, embedding_dim, dtype: ScalarType.Float64));

        this.dzParameter = new Parameter(torch.zeros(num_embeddings, 1, dtype: ScalarType.Float64));
        this.hCoeffParameter = new Parameter(torch.zeros(num_embeddings, 1, dtype: ScalarType.Float64));

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

    internal static int GetRodState(RodInput input)
    {
        double[] pred = ComputeScores(input);

        return Array.IndexOf(pred, pred.Max());
    }

    public static double[] ComputeScores(RodInput input)
    {
        var (y0, z0, t, u, v, h) = GetRodStatePreProcess(input);

        v -= h * h_coeff[input.fish_label];
        double x, y, dist;

        x = u * (z0 + dz[input.fish_label]) * Math.Sqrt(1 + t * t) / (t - v);
        y = (z0 + dz[input.fish_label]) * (1 + t * v) / (t - v);
        dist = Math.Sqrt(x * x + (y - y0) * (y - y0));

        int fish_label = input.fish_label;

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
        using var _ = no_grad();
        Tensor outputTensor = ComputeScores_Torch(input);

        var max = argmax(outputTensor);
        return (int)max.item<long>();
    }

    public Tensor ComputeScores_Torch(RodInput input)
    {
        using var _ = no_grad();
        this.SetWeightsManually();

        var (y0, z0, t, u, v, h) = GetRodStatePreProcess(input);

        Tensor fishLabel = tensor(new double[] { input.fish_label }, dtype: ScalarType.Int32);
        Tensor uv = tensor(new double[,] { { u, v } }, dtype: ScalarType.Float64);
        Tensor y0z0t = tensor(new double[,] { { y0, z0, t } }, dtype: ScalarType.Float64);
        Tensor h_ = tensor(new double[,] { { h } }, dtype: ScalarType.Float64);

        var logits = forward(fishLabel, uv, y0z0t, h_);
        var output = PostProcess(logits, fishLabel);

        return output;
    }

    /// <summary>
    /// 使用时直接赋值已知权重
    /// </summary>
    public void SetWeightsManually()
    {
        var weightTensor = tensor(RodNet.weight, ScalarType.Float64);
        var biasTensor = tensor(RodNet.bias, ScalarType.Float64);
        var dzTensor = tensor(RodNet.dz, ScalarType.Float64).reshape([RodNet.dz.Length, 1]);
        var h_coeffTensor = tensor(RodNet.h_coeff, ScalarType.Float64).reshape([RodNet.h_coeff.Length, 1]);
        this.thetaParameter = new Parameter(weightTensor);
        this.bParameter = new Parameter(biasTensor);
        this.dzParameter = new Parameter(dzTensor);
        this.hCoeffParameter = new Parameter(h_coeffTensor);
    }

    public override Tensor forward(Tensor fishLabel, Tensor uv, Tensor y0z0t, Tensor h)
    {
        var uvSplit = uv.split([1, 1], dim: 1);
        Tensor u = uvSplit[0];
        Tensor v = uvSplit[1];

        var y0z0tSplit = y0z0t.split([1, 1, 1], dim: 1);
        Tensor y0 = y0z0tSplit[0];
        Tensor z0 = y0z0tSplit[1];
        Tensor t = y0z0tSplit[2];

        v = v - h * hCoeffParameter[fishLabel];

        Tensor x, y, dist;

        var dz = dzParameter[fishLabel];
        x = u * (z0 + dz) * torch.sqrt(1 + t * t) / (t - v);
        y = (z0 + dz) * (1 + t * v) / (t - v);
        dist = torch.sqrt(x * x + (y - y0) * (y - y0));

        Tensor logits = this.thetaParameter[fishLabel] * dist + this.bParameter[fishLabel];

        return logits;
    }

    public Tensor PostProcess(Tensor logits, Tensor fishLabel)
    {
        var x_softmax = torch.nn.functional.softmax(logits, 1);

        Tensor x_offset = tensor(fishLabel.data<int>().Select(l => RodNet.offset[l]).ToArray());

        x_softmax[torch.arange(x_offset.shape[0]), 0] -= x_offset;
        return x_softmax;
    }

    /// <summary>
    /// 根据rod和fish的坐标计算y0z0t、uv、h
    /// </summary>
    /// <param name="input"></param>
    /// <returns>y0, z0, t, u, v, h</returns>
    public static (double, double, double, double, double, double) GetRodStatePreProcess(RodInput input)
    {
        /*
         * 以下为hutaofisher代码中关于部分变量的意义的注释
            # uv: screen coordinate of bbox center of the fish
            # abv0: rod shape and center coordinate in screen
        */
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
        double y0, z0, t;

        y0 = Math.Sqrt(Math.Pow(a, 4) - b * b + a * a * (1 - b * b + v0 * v0)) / (a * a);
        z0 = b / (a * a);
        t = a * a * (y0 * b + v0) / (a * a - b * b);

        return (y0, z0, t, u, v, h);
    }
}