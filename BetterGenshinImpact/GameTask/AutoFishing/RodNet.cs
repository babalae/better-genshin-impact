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

    static readonly double[] dz = [ 0.561117562965, 0.637026851288, 0.705579317577,
                                     1.062734463845, 0.949307580751, 1.015620474332,
                                     1.797904203405, 1.513476738412, 1.013873007495,
                                     1.159949954831, 1.353650974146, 1.302893195071 ];

    static readonly double[,] theta = {
                                        {-0.262674397633, 0.317025388945, -0.457150765450, 0.174522158281,
                                          -0.957110676932, -0.095339800558, -0.119519564026, -0.139914755291,
                                          -0.580893838475, 0.702302245305, 0.271575851220, 0.708473199472,
                                          0.699108382380},
                                        {-1.062702043060, -0.280779165943, -0.289891597384, 0.220173840594,
                                          0.493463877037, -0.326492366566, 1.215859141832, 1.607133159643,
                                          1.619199133672, 0.356402262447, 0.365385941958, 0.411869019381,
                                          0.224962055122},
                                        {0.460481782256, 0.048180392806, 0.475529271293, -0.150186412126,
                                          0.135512307120, 0.087365984352, -1.317661146364, -1.882438208662,
                                          -1.502483859283, -0.580228373556, -1.005821958682, -1.184199131739,
                                          -1.285988918494}
                                       };

    static readonly double[] B = [1.241950004386, 3.051113640564, -3.848898190087];

    static readonly double[] offset = [ 0.4, 0.2, 0.4, 0, 0.3, 0.3,
        0.3, 0.15, 0.5, 0.5, 0.5, 0.5 ];

    private readonly Module<Tensor, Tensor> layers;

    public RodNet() : base("RodNet")
    {
        var theta = tensor(RodNet.theta, ScalarType.Float64);
        var b = tensor(RodNet.B, ScalarType.Float64);

        RodLayer1 rodLayer1 = new RodLayer1(num_embeddings: theta.shape[0], embedding_dim: theta.shape[1], input_dim: 3, output_dim: 3);
        rodLayer1.SetWeightsManually(theta, b);
        var modules = new List<(string, Module<Tensor, Tensor>)>
        {
            ($"linear", rodLayer1),
            ($"softmax", nn.Softmax(1))
        };

        layers = Sequential(modules);

        RegisterComponents();
    }

    static void F(double[] dst, double[] x, double[] y)
    {
        double y0 = x[0], z0 = x[1], t = x[2];
        double tmp = (y0 + t * z0) * (y0 + t * z0) - 1;
        dst[0] = Math.Sqrt((1 + t * t) / tmp) - y[0];
        dst[1] = (1 + t * t) * z0 / tmp - y[1];
        dst[2] = ((t * t - 1) * y0 * z0 + t * (y0 * y0 - z0 * z0 - 1)) / tmp - y[2];
    }

    static void DfInv(double[] dst, double[] x)
    {
        double y0 = x[0], z0 = x[1], t = x[2];
        double tmp1 = (y0 + t * z0) * (y0 + t * z0) - 1;
        double tmp2 = 1 + t * t;
        dst[0] = (1 - y0 * y0 + z0 * z0) / y0 * Math.Sqrt(tmp1 / tmp2);
        dst[1] = -z0 * (y0 * y0 + t * (t * (1 + z0 * z0) + 2 * y0 * z0)) / tmp2 / y0;
        dst[2] = ((t * t - 1) * y0 * z0 + t * (y0 * y0 - z0 * z0 - 1)) / y0 / tmp2;
        dst[3] = -2 * z0 * Math.Sqrt(tmp1 / tmp2);
        dst[4] = tmp1 / tmp2;
        dst[5] = 0;
        dst[6] = -z0 / y0 * Math.Sqrt(tmp1 / tmp2);
        dst[7] = (y0 + t * z0) * (y0 + t * z0) / y0;
        dst[8] = 1 + t * z0 / y0;
    }

    static bool NewtonRaphson(Action<double[], double[], double[]> f, Action<double[], double[]> dfInv, double[] dst, double[] y,
                               double[] init, int n, int maxIter, double eps)
    {
        double[] fEst = new double[n];
        double[] dfInvMat = new double[n * n];
        double[] x = new double[n];
        double err;

        Array.Copy(init, x, n);

        for (int iter = 0; iter < maxIter; iter++)
        {
            err = 0;
            f(fEst, x, y);
            for (int i = 0; i < n; i++)
            {
                err += Math.Abs(fEst[i]);
            }

            if (err < eps)
            {
                Array.Copy(x, dst, n);
                // printf("Newton-Raphson solver converge after %d steps, err: %lf !\n",
                //        iter, err);
                return true;
            }

            dfInv(dfInvMat, x);

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    x[i] -= dfInvMat[n * i + j] * fEst[j];
                }
            }
        }
        return false;
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
    record NetInput(double dist, int fish_label);
    private static NetInput GeometryProcessing(RodInput input)
    {
        double a, b, v0, u, v;

        a = (input.rod_x2 - input.rod_x1) / 2 / alpha;
        b = (input.rod_y2 - input.rod_y1) / 2 / alpha;

        if (a < b)
        {
            (b, a) = (a, b);
        }

        v0 = (288 - (input.rod_y1 + input.rod_y2) / 2) / alpha;

        u = (input.fish_x1 + input.fish_x2 - input.rod_x1 - input.rod_x2) / 2 / alpha;
        v = (288 - (input.fish_y1 + input.fish_y2) / 2) / alpha;

        double[] y0z0t = new double[3];
        double[] abv0 = [a, b, v0];
        double[] init = [30, 15, 1];

        // todo 处理此种情况，奇怪的是hutao的dev分支已去除牛顿算法，得询问hutao和鸭蛋
        //bool solveSuccess = NewtonRaphson(F, DfInv, y0z0t, abv0, init, 3, 1000, 1e-6);

        //if (!solveSuccess)
        //{
        //    return -1;
        //}

        double y0 = y0z0t[0], z0 = y0z0t[1], t = y0z0t[2];
        double x, y, dist;
        x = u * (z0 + dz[input.fish_label]) * Math.Sqrt(1 + t * t) / (t - v);
        y = (z0 + dz[input.fish_label]) * (1 + t * v) / (t - v);
        dist = Math.Sqrt(x * x + (y - y0) * (y - y0));

        return new NetInput(dist, input.fish_label);
    }

    public static int GetRodState(RodInput input)
    {
        NetInput netInput = GeometryProcessing(input);
        double dist = netInput.dist;
        int fish_label = netInput.fish_label;

        double[] logits = new double[3];
        for (int i = 0; i < 3; i++)
        {
            logits[i] = theta[i, 0] * dist + theta[i, 1 + fish_label] + B[i];
        }

        double[] pred = new double[3];
        Softmax(pred, logits, 3);
        pred[0] -= offset[fish_label];

        return Array.IndexOf(pred, pred.Max());
    }

    public int GetRodState_Torch(RodInput input)
    {
        NetInput netInput = GeometryProcessing(input);
        double dist = netInput.dist;
        int fish_label = netInput.fish_label;

        Tensor inputTensor = cat([tensor(new double[,] { { dist } }, dtype: ScalarType.Float64),
            tensor(new int[,] { {fish_label } }, dtype: ScalarType.Int32)]).T;
        var outputTensor = forward(inputTensor);

        outputTensor[0][0] = outputTensor[0][0] - RodNet.offset[fish_label];

        #region 临时校验
        double[] logits = new double[3];
        for (int i = 0; i < 3; i++)
        {
            logits[i] = theta[i, 0] * dist + theta[i, 1 + fish_label] + B[i];
        }

        double[] pred = new double[3];
        Softmax(pred, logits, 3);

        pred[0] -= offset[fish_label];

        if ((float)pred[0] != (float)outputTensor.data<double>()[0] ||
            (float)pred[1] != (float)outputTensor.data<double>()[1] ||
            (float)pred[2] != (float)outputTensor.data<double>()[2])
        {
            //todo 处理调试时发生的 pred[2] 为NaN 而此时outputTensor.data<double>()[2]为1的情况
            throw new Exception("RodNet新旧方法数值不一致，远超计算精度影响");
        }
        #endregion

        var max = argmax(outputTensor);
        return (int)max.item<long>();
    }

    public override Tensor forward(Tensor input)
    {
        return layers.forward(input);
    }
}

public class RodLayer1 : Module<Tensor, Tensor>
{
    private readonly Embedding embedding;
    private readonly Linear linear;
    public RodLayer1(long num_embeddings, long embedding_dim, long input_dim, long output_dim)
        : base("RodLinear")
    {
        embedding = torch.nn.Embedding(num_embeddings, embedding_dim);
        linear = torch.nn.Linear(input_dim, output_dim);

        RegisterComponents();
    }

    public void SetWeightsManually(Tensor theta, Tensor b)
    {
        var splitTheta = theta.split([1, 12], dim: 1);

        linear.weight.requires_grad = false;
        linear.weight = new Parameter(splitTheta[0]);
        linear.bias.requires_grad = false;
        linear.bias = new Parameter(b);

        embedding.weight = new Parameter(splitTheta[1].T);
    }

    public override Tensor forward(Tensor input)
    {
        var splitInput = input.split([1, 1], dim: 1);
        var dist = splitInput[0];
        var fish_label = splitInput[1].to(ScalarType.Int32).flatten();
        var x_linear = linear.forward(dist);
        //Console.WriteLine(x_linear);
        //Console.WriteLine(String.Join(",", x_linear.data<double>()));
        var x_embed = embedding.forward(fish_label);
        //Console.WriteLine(x_embed);
        //Console.WriteLine(String.Join(",", x_embed.data<double>()));
        var x_combined = x_linear + x_embed;
        return x_combined;
    }
}