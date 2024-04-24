using System;
using System.Linq;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoFishing;

/// <summary>
/// copy from https://github.com/myHuTao-qwq/HutaoFisher/blob/master/src/rodnet.cpp
/// </summary>
public class RodNet
{
    const double alpha = 1734.34 / 2.5;

    static readonly double[] dz = { 0.561117562965, 0.637026851288, 0.705579317577,
                                     1.062734463845, 0.949307580751, 1.015620474332,
                                     1.797904203405, 1.513476738412, 1.013873007495,
                                     1.159949954831, 1.353650974146, 1.302893195071 };

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

    static readonly double[] B = { 1.241950004386, 3.051113640564, -3.848898190087 };

    static readonly double[] offset = { 0.4, 0.2, 0.4, 0, 0.3, 0.3,
        0.3, 0.15, 0.5, 0.5, 0.5, 0.5 };

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

    public static int GetRodState(RodInput input)
    {
        double a, b, v0, u, v;

        a = (input.rod_x2 - input.rod_x1) / 2 / alpha;
        b = (input.rod_y2 - input.rod_y1) / 2 / alpha;

        if (a < b)
        {
            double temp = a;
            a = b;
            b = temp;
        }

        v0 = (288 - (input.rod_y1 + input.rod_y2) / 2) / alpha;

        u = (input.fish_x1 + input.fish_x2 - input.rod_x1 - input.rod_x2) / 2 / alpha;
        v = (288 - (input.fish_y1 + input.fish_y2) / 2) / alpha;

        double[] y0z0t = new double[3];
        double[] abv0 = { a, b, v0 };
        double[] init = { 30, 15, 1 };

        bool solveSuccess = NewtonRaphson(F, DfInv, y0z0t, abv0, init, 3, 1000, 1e-6);

        if (!solveSuccess)
        {
            return -1;
        }

        double y0 = y0z0t[0], z0 = y0z0t[1], t = y0z0t[2];
        double x, y, dist;
        x = u * (z0 + dz[input.fish_label]) * Math.Sqrt(1 + t * t) / (t - v);
        y = (z0 + dz[input.fish_label]) * (1 + t * v) / (t - v);
        dist = Math.Sqrt(x * x + (y - y0) * (y - y0));

        double[] logits = new double[3];
        for (int i = 0; i < 3; i++)
        {
            logits[i] = theta[i, 0] * dist + theta[i, 1 + input.fish_label] + B[i];
        }

        double[] pred = new double[3];
        Softmax(pred, logits, 3);
        pred[0] -= offset[input.fish_label];

        return Array.IndexOf(pred, pred.Max());
    }

    public static int GetRodState(Rect rod, Rect fish, int fishTypeIndex)
    {
        RodInput input = new RodInput
        {
            rod_x1 = rod.Left,
            rod_x2 = rod.Right,
            rod_y1 = rod.Top,
            rod_y2 = rod.Bottom,
            fish_x1 = fish.Left,
            fish_x2 = fish.Right,
            fish_y1 = fish.Top,
            fish_y2 = fish.Bottom,
            fish_label = fishTypeIndex
        };
        return GetRodState(input);
    }
}