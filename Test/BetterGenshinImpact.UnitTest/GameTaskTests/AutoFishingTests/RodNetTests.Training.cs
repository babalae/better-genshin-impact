using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static TorchSharp.torch.nn;
using static TorchSharp.torch;
using TorchSharp;
using System.Diagnostics;
using System.Collections;
using BetterGenshinImpact.GameTask.AutoFishing;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public partial class RodNetTests
    {
        /// <summary>
        /// RodNet验证，应在数据集上达到一定准确率
        /// </summary>
        [Theory]
        [InlineData(@"..\..\..\Assets\AutoFishing\data_selected.csv")]
        public void Training_AccuracyShouldBeOK(string dataLocation)
        {
            //
            using var _ = no_grad();

            var device =
                torch.cuda.is_available() ? torch.CUDA :
                torch.mps_is_available() ? torch.MPS :
                torch.CPU;
            var loss = CrossEntropyLoss();
            var sut = new RodNet().to((Device)device);
            sut.SetWeightsManually();

            using var test_reader = new CSVReader(Enumerable.Repeat(false, 8).Concat(Enumerable.Repeat(true, 2)), Path.GetFullPath(dataLocation), (Device)device);

            //
            var accuracy = evaluate(test_reader.GetBatches(eval_batch_size), sut, loss);

            //
            Assert.True(accuracy > 0.8);
        }

        /// <summary>
        /// RodNet必须粗略地支持训练
        /// </summary>
        [Fact]
        public void Training_ShouldBeDifferentiable()
        {
            //
            RodInput input = new RodInput();
            var (y0, z0, t, u, v, h) = RodNet.GetRodStatePreProcess(input);

            Tensor fishLabel = tensor(new double[] { input.fish_label }, dtype: ScalarType.Int32);
            Tensor uv = tensor(new double[,] { { u, v } }, dtype: ScalarType.Float64);
            Tensor y0z0t = tensor(new double[,] { { y0, z0, t } }, dtype: ScalarType.Float64);
            Tensor h_ = tensor(new double[,] { { h } }, dtype: ScalarType.Float64);
            RodNet sut = new RodNet();

            //
            Tensor output = sut.forward(fishLabel, uv, y0z0t, h_);
            output.backward([torch.ones_like(output)]);
            //
        }

        #region 训练相关代码
        // 这部分代码改编自TorchSharpExamples的CSharpExamples.TextClassification
        private const long batch_size = 32;
        private const long eval_batch_size = 32;

        internal static RodNet Run(int epochs, int timeout, string dataLocation)
        {
            torch.random.manual_seed(1);

            var device =
                torch.cuda.is_available() ? torch.CUDA :
                torch.mps_is_available() ? torch.MPS :
                torch.CPU;

            Console.WriteLine();
            Console.WriteLine($"\tRunning TextClassification on {device.type.ToString()} for {epochs} epochs, terminating after {TimeSpan.FromSeconds(timeout)}.");
            Console.WriteLine();

            Console.WriteLine($"\tPreparing training and test data...");

            using (var reader = new CSVReader(Enumerable.Repeat(true, 8).Concat(Enumerable.Repeat(false, 2)), dataLocation, (Device)device))
            {
                Console.WriteLine($"\tCreating the model...");
                Console.WriteLine();

                var model = new RodNet().to((Device)device);

                var loss = CrossEntropyLoss();
                var lr = 1e-2;
                var optimizer = torch.optim.SGD(model.parameters(), learningRate: lr);
                var scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(optimizer, epochs, eta_min: 0);

                var totalTime = new Stopwatch();
                totalTime.Start();

                foreach (var epoch in Enumerable.Range(1, epochs))
                {

                    var sw = new Stopwatch();
                    sw.Start();

                    train(epoch, reader.GetBatches(batch_size), model, loss, optimizer);

                    sw.Stop();

                    Console.WriteLine($"\nEnd of epoch: {epoch} | lr: {optimizer.ParamGroups.First().LearningRate:0.0000} | time: {sw.Elapsed.TotalSeconds:0.0}s\n");
                    scheduler.step();

                    if (totalTime.Elapsed.TotalSeconds > timeout) break;
                }

                totalTime.Stop();

                using (var test_reader = new CSVReader(Enumerable.Repeat(false, 8).Concat(Enumerable.Repeat(true, 2)), dataLocation, (Device)device))
                {

                    var sw = new Stopwatch();
                    sw.Start();

                    var accuracy = evaluate(test_reader.GetBatches(eval_batch_size), model, loss);

                    sw.Stop();

                    Console.WriteLine($"\nEnd of training: test accuracy: {accuracy:0.00} | eval time: {sw.Elapsed.TotalSeconds:0.0}s\n");
                    scheduler.step();
                }

                foreach (var (name, param) in model.named_parameters())
                {
                    switch (param.dtype)
                    {
                        case ScalarType.Int64:
                            Console.WriteLine($"参数{name}={String.Join(", ", param.data<long>())}");
                            break;
                        case ScalarType.Float32:
                            Console.WriteLine($"参数{name}={String.Join(", ", param.data<float>())}");
                            break;
                        case ScalarType.Float64:
                            Console.WriteLine($"参数{name}={String.Join(", ", param.data<double>())}");
                            break;
                    }
                }

                return model;
            }

        }

        static void train(int epoch, IEnumerable<(Tensor, Tensor, Tensor, Tensor, Tensor)> train_data, RodNet model, Loss<Tensor, Tensor, Tensor> criterion, torch.optim.Optimizer optimizer)
        {
            model.train();

            double total_acc = 0.0;
            long total_count = 0;
            long log_interval = 1;

            var batch = 0;

            var batch_count = train_data.Count();

            using (var d = torch.NewDisposeScope())
            {
                foreach (var (y0z0t, uv, h, fish_label, success) in train_data)
                {

                    optimizer.zero_grad();

                    using (var predicted_labels = model.forward(fish_label, uv, y0z0t, h))
                    {
                        var loss = criterion.forward(predicted_labels, success.to(ScalarType.Int64));
                        loss.backward();
                        torch.nn.utils.clip_grad_norm_(model.parameters(), 0.1);
                        optimizer.step();

                        total_acc += (predicted_labels.argmax(1) == success).sum().to(torch.CPU).item<long>();
                        total_count += success.size(0);
                    }

                    batch += 1;
                    if (batch % log_interval == 0)
                    {
                        var accuracy = total_acc / total_count;
                        Console.WriteLine($"epoch: {epoch} | batch: {batch} / {batch_count} | accuracy: {accuracy:0.00}");
                    }
                }
            }
        }

        static double evaluate(IEnumerable<(Tensor, Tensor, Tensor, Tensor, Tensor)> test_data, RodNet model, Loss<Tensor, Tensor, Tensor> criterion)
        {
            model.eval();

            double total_acc = 0.0;
            long total_count = 0;

            using (var d = torch.NewDisposeScope())
            {
                foreach (var (y0z0t, uv, h, fish_label, success) in test_data)
                {
                    using (var predicted_labels = model.forward(fish_label, uv, y0z0t, h))
                    {
                        var loss = criterion.forward(predicted_labels, success.to(ScalarType.Int64));

                        total_acc += (predicted_labels.argmax(1) == success).sum().to(torch.CPU).item<long>();
                        total_count += success.size(0);
                    }
                }

                return total_acc / total_count;
            }
        }
        #endregion
    }

    internal class CSVReader : IDisposable
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="takeMask">按长度分组，布尔值代表每组内此序号元素是否被读取。例如8个true2个false就是将近80%进入训练集</param>
        /// <param name="path"></param>
        /// <param name="device"></param>
        public CSVReader(IEnumerable<bool> takeMask, string path, Device device)
        {
            this.takeMask = takeMask.ToArray();
            _path = path;
            _device = device;
        }

        private readonly bool[] takeMask;
        private readonly string _path;
        private readonly Device _device;


        public IEnumerable<Data> Enumerate()
        {
            var all = File.ReadLines(_path).Skip(1);    // 跳过首行列名
            int count = takeMask.Length;
            int maskCount = all.Count() / count;
            for (int i = 0; i < maskCount + 1; i++)
            {
                int lastGroupCount = (i == maskCount) ? (all.Count() % count) : count;
                for (int j = 0; j < lastGroupCount; j++)
                {
                    if (takeMask[j])
                    {
                        yield return ParseLine(all.Skip(i * count + j).First());
                    }
                }
            }
        }

        public IEnumerable<(Tensor, Tensor, Tensor, Tensor, Tensor)> GetBatches(long batch_size)
        {
            // This data set fits in memory, so we will simply load it all and cache it between epochs.

            var inputs = new List<Data>();

            if (_data == null)
            {

                _data = new List<(Tensor, Tensor, Tensor, Tensor, Tensor)>();

                var counter = 0;
                var lines = Enumerate().ToList();
                var left = lines.Count;

                foreach (var line in lines)
                {

                    inputs.Add(line);
                    left -= 1;

                    if (++counter == batch_size || left == 0)
                    {
                        _data.Add(Batchifier(inputs));
                        inputs.Clear();
                        counter = 0;
                    }
                }
            }

            return _data;
        }

        private List<(Tensor, Tensor, Tensor, Tensor, Tensor)> _data;
        private bool disposedValue;

        /// <summary>
        /// 将csv中的数据进行初步转换
        /// </summary>
        /// <param name="input"></param>
        /// <returns>y0z0t、uv、h、fish_label、success张量</returns>
        private (Tensor, Tensor, Tensor, Tensor, Tensor) Batchifier(IEnumerable<Data> input)
        {
            var y0List = new List<double>();
            var z0List = new List<double>();
            var tList = new List<double>();
            var uList = new List<double>();
            var vList = new List<double>();
            var hList = new List<double>();
            var labelList = new List<int>();
            var successList = new List<int>();

            foreach (var line in input)
            {
                int fish_label = line.fish_label;
                int success = line.success;
                RodInput rodInput = new RodInput()
                {
                    rod_x1 = line.rod_x1,
                    rod_x2 = line.rod_x2,
                    rod_y1 = line.rod_y1,
                    rod_y2 = line.rod_y2,
                    fish_x1 = line.fish_x1,
                    fish_x2 = line.fish_x2,
                    fish_y1 = line.fish_y1,
                    fish_y2 = line.fish_y2
                };
                var (y0, z0, t, u, v, h) = RodNet.GetRodStatePreProcess(rodInput);

                y0List.Add(y0);
                z0List.Add(z0);
                tList.Add(t);
                uList.Add(u);
                vList.Add(v);
                hList.Add(h);
                labelList.Add(fish_label);
                successList.Add(success);
            }

            Tensor y0Tensor = tensor(y0List, dtype: ScalarType.Float64).to(_device);
            Tensor z0Tensor = tensor(z0List, dtype: ScalarType.Float64).to(_device);
            Tensor tTensor = tensor(tList, dtype: ScalarType.Float64).to(_device);
            Tensor uTensor = tensor(uList, dtype: ScalarType.Float64).to(_device);
            Tensor vTensor = tensor(vList, dtype: ScalarType.Float64).to(_device);
            Tensor hTensor = tensor(hList, dtype: ScalarType.Float64).to(_device);
            Tensor fish_labelTensor = tensor(labelList, dtype: ScalarType.Int32).to(_device);
            Tensor successTensor = tensor(successList, dtype: ScalarType.Int32).to(_device);

            return (torch.stack([y0Tensor, z0Tensor, tTensor], dim: 1), torch.stack([uTensor, vTensor], dim: 1), hTensor.unsqueeze(1), fish_labelTensor, successTensor);
        }

        /// <summary>
        /// csv的列定义默认为time,bite_time,rod_x1,rod_x2,rod_y1,rod_y2,fish_x1,fish_x2,fish_y1,fish_y2,fish_label,success
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public Data ParseLine(string line)
        {
            var columns = line.Split(",").ToArray();

            return new Data(float.Parse(columns[2]), float.Parse(columns[3]), float.Parse(columns[4]), float.Parse(columns[5]),
                float.Parse(columns[6]), float.Parse(columns[7]), float.Parse(columns[8]), float.Parse(columns[9]),
                int.Parse(columns[10]), int.Parse(columns[11]));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing && _data != null)
                {
                    foreach (var (y0z0t, uv, h, label, success) in _data)
                    {
                        y0z0t.Dispose();
                        uv.Dispose();
                        h.Dispose();
                        label.Dispose();
                        success.Dispose();
                    }
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    internal record Data(float rod_x1, float rod_x2, float rod_y1, float rod_y2, float fish_x1, float fish_x2, float fish_y1, float fish_y2, int fish_label, int success);
}
