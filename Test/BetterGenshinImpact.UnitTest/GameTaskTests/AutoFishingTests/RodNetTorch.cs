using BetterGenshinImpact.GameTask.AutoFishing;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    /// <summary>
    /// 使用TorchSharp复现胡桃的RodNet
    /// </summary>
    internal class RodNetTorch : RodNet
    {
        internal sealed class TorchNet : Module<Tensor, Tensor, Tensor, Tensor, Tensor>
        {
            private Parameter thetaParameter;
            private Parameter bParameter;
            private Parameter dzParameter;
            private Parameter hCoeffParameter;

            public TorchNet(RodNet rodNet) : base("RodNet")
            {
                long num_embeddings = RodNet.weight.GetLength(0);
                long embedding_dim = 3;

                this.thetaParameter = new Parameter(torch.randn(num_embeddings, embedding_dim, dtype: ScalarType.Float64));
                this.bParameter = new Parameter(torch.randn(num_embeddings, embedding_dim, dtype: ScalarType.Float64));

                this.dzParameter = new Parameter(torch.zeros(num_embeddings, 1, dtype: ScalarType.Float64));
                this.hCoeffParameter = new Parameter(torch.zeros(num_embeddings, 1, dtype: ScalarType.Float64));

                RegisterComponents();
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

                Tensor logits = thetaParameter[fishLabel] * dist + bParameter[fishLabel];

                return logits;
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
        }

        internal readonly TorchNet net;

        public RodNetTorch()
        {
            net = new TorchNet(this);
        }

        public new Tensor ComputeScores(RodInput input)
        {
            using var _ = no_grad();
            this.net.SetWeightsManually();

            var (y0, z0, t, u, v, h) = GetRodStatePreProcess(input);

            Tensor fishLabel = tensor(new double[] { input.fish_label }, dtype: ScalarType.Int32);
            Tensor uv = tensor(new double[,] { { u, v } }, dtype: ScalarType.Float64);
            Tensor y0z0t = tensor(new double[,] { { y0, z0, t } }, dtype: ScalarType.Float64);
            Tensor h_ = tensor(new double[,] { { h } }, dtype: ScalarType.Float64);

            var logits = this.net.forward(fishLabel, uv, y0z0t, h_);
            var output = PostProcess(logits, fishLabel);

            return output;
        }

        public override int GetRodState(RodInput input)
        {
            using var _ = no_grad();
            Tensor outputTensor = ComputeScores(input);

            var max = argmax(outputTensor);
            return (int)max.item<long>();
        }

        public Tensor PostProcess(Tensor logits, Tensor fishLabel)
        {
            var x_softmax = torch.nn.functional.softmax(logits, 1);

            Tensor x_offset = tensor(fishLabel.data<int>().Select(l => RodNet.offset[l]).ToArray());

            x_softmax[torch.arange(x_offset.shape[0]), 0] -= x_offset;
            return x_softmax;
        }
    }
}
