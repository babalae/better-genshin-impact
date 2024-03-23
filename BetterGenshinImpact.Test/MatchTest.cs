using OpenCvSharp;
using OpenCvSharp.Features2D;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Xml;

namespace BetterGenshinImpact.Test;

public class MatchTest
{
    public static void Test()
    {
        var tar = new Mat(@"E:\HuiTask\更好的原神\地图匹配\比较\小地图\Clip_20240323_185641.png", ImreadModes.Color);
        tar = tar.Resize(new Size(tar.Width * 2, tar.Height * 2), 0, 0, InterpolationFlags.Nearest);
        var src = new Mat(@"E:\HuiTask\更好的原神\地图匹配\combined_image.png", ImreadModes.Color);
        var res = MatchPicBySurf(src, tar);

        Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\s1.png", res);
    }

    public static Mat MatchPicBySift(Mat matSrc, Mat matTo)
    {
        Stopwatch sw = new Stopwatch();
        sw.Start();

        Mat matSrcRet = new Mat();
        using Mat matToRet = new Mat();
        KeyPoint[] keyPointsSrc, keyPointsTo;
        using (var sift = SIFT.Create())
        {
            var kpPath = @"E:\HuiTask\更好的原神\地图匹配\sift.kp";
            var kpMatPath = @"E:\HuiTask\更好的原神\地图匹配\sift.mat";
            if (File.Exists(kpPath) && File.Exists(kpMatPath))
            {
                keyPointsSrc = (KeyPoint[])DeserializeObject(File.ReadAllBytes(kpPath));
                GCHandle pinnedArray = GCHandle.Alloc(DeserializeObject(File.ReadAllBytes(kpMatPath)), GCHandleType.Pinned);
                IntPtr pointer = pinnedArray.AddrOfPinnedObject();
                matSrcRet = new Mat(166767, 128, MatType.CV_32FC1, pointer);
            }
            else
            {
                sift.DetectAndCompute(matSrc, null, out keyPointsSrc, matSrcRet);
                byte[] arr = new byte[matSrcRet.Step(0) * matSrcRet.Rows]; // matSrcRet.Total() * matSrcRet.ElemSize()
                Marshal.Copy(matSrcRet.Data, arr, 0, arr.Length);
                File.WriteAllBytes(kpMatPath, SerializeObject(arr));
                File.WriteAllBytes(kpPath, SerializeObject(keyPointsSrc));
            }

            sw.Stop();
            Debug.WriteLine($"大地图kp耗时：{sw.ElapsedMilliseconds}ms.");
            sw.Restart();
            sift.DetectAndCompute(matTo, null, out keyPointsTo, matToRet);
            sw.Stop();
            Debug.WriteLine($"模板kp耗时：{sw.ElapsedMilliseconds}ms.");
            sw.Restart();
        }

        using (var bfMatcher = new OpenCvSharp.BFMatcher())
        {
            var matches = bfMatcher.KnnMatch(matSrcRet, matToRet, k: 2);

            var pointsSrc = new List<Point2f>();
            var pointsDst = new List<Point2f>();
            var goodMatches = new List<DMatch>();
            foreach (DMatch[] items in matches.Where(x => x.Length > 1))
            {
                if (items[0].Distance < 0.5 * items[1].Distance)
                {
                    pointsSrc.Add(keyPointsSrc[items[0].QueryIdx].Pt);
                    pointsDst.Add(keyPointsTo[items[0].TrainIdx].Pt);
                    goodMatches.Add(items[0]);
                    Debug.WriteLine($"{keyPointsSrc[items[0].QueryIdx].Pt.X}, {keyPointsSrc[items[0].QueryIdx].Pt.Y}");
                }
            }

            sw.Stop();
            Debug.WriteLine($"bfMatcher耗时：{sw.ElapsedMilliseconds}ms.");
            sw.Restart();

            var outMat = new Mat();

            // algorithm RANSAC Filter the matched results
            var pSrc = pointsSrc.ConvertAll(Point2fToPoint2d);
            var pDst = pointsDst.ConvertAll(Point2fToPoint2d);
            var outMask = new Mat();
            // If the original matching result is null, Skip the filtering step
            if (pSrc.Count > 0 && pDst.Count > 0)
                Cv2.FindHomography(pSrc, pDst, HomographyMethods.Ransac, mask: outMask);

            sw.Stop();
            Debug.WriteLine($"FindHomography耗时：{sw.ElapsedMilliseconds}ms.");
            sw.Restart();
            // If passed RANSAC After processing, the matching points are more than 10.,Only filters are used. Otherwise, use the original matching point result(When the matching point is too small, it passes through RANSAC After treatment,It is possible to get the result of 0 matching points.).
            if (outMask.Rows > 10)
            {
                byte[] maskBytes = new byte[outMask.Rows * outMask.Cols];
                outMask.GetArray(out maskBytes);
                Cv2.DrawMatches(matSrc, keyPointsSrc, matTo, keyPointsTo, goodMatches, outMat, matchesMask: maskBytes, flags: DrawMatchesFlags.NotDrawSinglePoints);
            }
            else
                Cv2.DrawMatches(matSrc, keyPointsSrc, matTo, keyPointsTo, goodMatches, outMat, flags: DrawMatchesFlags.NotDrawSinglePoints);

            sw.Stop();
            Debug.WriteLine($"绘图耗时：{sw.ElapsedMilliseconds}ms.");
            sw.Restart();
            return outMat;
        }
    }

    //This method may be missed, you may read a lot of blogs, but none of them wrote
    private static Point2d Point2fToPoint2d(Point2f input)
    {
        Point2d p2 = new Point2d(input.X, input.Y);
        return p2;
    }

    public static Mat MatchPicBySurf(Mat matSrc, Mat matTo, double threshold = 400)
    {
        Stopwatch sw = new Stopwatch();
        sw.Start();

        Mat matSrcRet = new Mat();
        using Mat matToRet = new Mat();
        KeyPoint[] keyPointsSrc, keyPointsTo;
        using (var surf = OpenCvSharp.XFeatures2D.SURF.Create(threshold, 4, 3, true, true))
        {
            var kpPath = @"E:\HuiTask\更好的原神\地图匹配\surf.kp";
            var kpMatPath = @"E:\HuiTask\更好的原神\地图匹配\surf.mat";
            if (File.Exists(kpPath) && File.Exists(kpMatPath))
            {
                keyPointsSrc = (KeyPoint[])DeserializeObject(File.ReadAllBytes(kpPath));
                GCHandle pinnedArray = GCHandle.Alloc(DeserializeObject(File.ReadAllBytes(kpMatPath)), GCHandleType.Pinned);
                IntPtr pointer = pinnedArray.AddrOfPinnedObject();
                matSrcRet = new Mat(166767, 128, MatType.CV_32FC1, pointer);
            }
            else
            {
                surf.DetectAndCompute(matSrc, null, out keyPointsSrc, matSrcRet);
                byte[] arr = new byte[matSrcRet.Step(0) * matSrcRet.Rows]; // matSrcRet.Total() * matSrcRet.ElemSize()
                Marshal.Copy(matSrcRet.Data, arr, 0, arr.Length);
                File.WriteAllBytes(kpMatPath, SerializeObject(arr));
                File.WriteAllBytes(kpPath, SerializeObject(keyPointsSrc));
            }
            sw.Stop();
            Debug.WriteLine($"大地图kp耗时：{sw.ElapsedMilliseconds}ms.");
            sw.Restart();

            surf.DetectAndCompute(matTo, null, out keyPointsTo, matToRet);
            sw.Stop();
            Debug.WriteLine($"模板kp耗时：{sw.ElapsedMilliseconds}ms.");
            sw.Restart();
        }

        using (var flnMatcher = new OpenCvSharp.FlannBasedMatcher())
        {
            var matches = flnMatcher.Match(matSrcRet, matToRet);
            //Finding the Minimum and Maximum Distance
            double minDistance = 1000; //Backward approximation
            double maxDistance = 0;
            for (int i = 0; i < matSrcRet.Rows; i++)
            {
                double distance = matches[i].Distance;
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                }

                if (distance < minDistance)
                {
                    minDistance = distance;
                }
            }

            Debug.WriteLine($"max distance : {maxDistance}");
            Debug.WriteLine($"min distance : {minDistance}");

            var pointsSrc = new List<Point2f>();
            var pointsDst = new List<Point2f>();
            //Screening better matching points
            var goodMatches = new List<DMatch>();
            for (int i = 0; i < matSrcRet.Rows; i++)
            {
                double distance = matches[i].Distance;
                if (distance < Math.Max(minDistance * 2, 0.02))
                {
                    pointsSrc.Add(keyPointsSrc[matches[i].QueryIdx].Pt);
                    pointsDst.Add(keyPointsTo[matches[i].TrainIdx].Pt);
                    //Compression of new ones with distances less than ranges DMatch
                    goodMatches.Add(matches[i]);
                }
            }

            sw.Stop();
            Debug.WriteLine($"flnMatcher耗时：{sw.ElapsedMilliseconds}ms.");
            sw.Restart();

            var outMat = new Mat();

            // algorithm RANSAC Filter the matched results
            var pSrc = pointsSrc.ConvertAll(Point2fToPoint2d);
            var pDst = pointsDst.ConvertAll(Point2fToPoint2d);
            var outMask = new Mat();
            // If the original matching result is null, Skip the filtering step
            if (pSrc.Count > 0 && pDst.Count > 0)
                Cv2.FindHomography(pSrc, pDst, HomographyMethods.Ransac, mask: outMask);
            sw.Stop();
            Debug.WriteLine($"FindHomography耗时：{sw.ElapsedMilliseconds}ms.");
            sw.Restart();

            // If passed RANSAC After processing, the matching points are more than 10.,Only filters are used. Otherwise, use the original matching point result(When the matching point is too small, it passes through RANSAC After treatment,It's possible to get the result of 0 matching points.).
            if (outMask.Rows > 10)
            {
                Debug.WriteLine($"使用了Ransac的结果");
                byte[] maskBytes = new byte[outMask.Rows * outMask.Cols];
                outMask.GetArray(out maskBytes);
                Cv2.DrawMatches(matSrc, keyPointsSrc, matTo, keyPointsTo, goodMatches, outMat, matchesMask: maskBytes, flags: DrawMatchesFlags.NotDrawSinglePoints);
            }
            else
                Cv2.DrawMatches(matSrc, keyPointsSrc, matTo, keyPointsTo, goodMatches, outMat, flags: DrawMatchesFlags.NotDrawSinglePoints);

            sw.Stop();
            Debug.WriteLine($"绘图耗时：{sw.ElapsedMilliseconds}ms.");
            return outMat;
        }
    }

    /// <summary>
    /// 序列化
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    private static byte[] Serialize(object obj)
    {
        using var memoryStream = new MemoryStream();
        DataContractSerializer ser = new DataContractSerializer(typeof(object));
        ser.WriteObject(memoryStream, obj);
        var data = memoryStream.ToArray();
        return data;
    }

    /// <summary>
    /// 反序列化
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="data"></param>
    /// <returns></returns>
    private static T Deserialize<T>(byte[] data)
    {
        using var memoryStream = new MemoryStream(data);
        XmlDictionaryReader reader = XmlDictionaryReader.CreateTextReader(memoryStream, new XmlDictionaryReaderQuotas());
        DataContractSerializer ser = new DataContractSerializer(typeof(T));
        var result = (T)ser.ReadObject(reader, true);
        return result;
    }

    [Obsolete("Obsolete")]
    public static byte[] SerializeObject(object obj)
    {
        if (obj == null)
            return null;
        //内存实例
        MemoryStream ms = new MemoryStream();
        //创建序列化的实例
        BinaryFormatter formatter = new BinaryFormatter();
        formatter.Serialize(ms, obj); //序列化对象，写入ms流中
        byte[] bytes = ms.GetBuffer();
        return bytes;
    }

    [Obsolete("Obsolete")]
    public static object DeserializeObject(byte[] bytes)
    {
        object obj = null;
        if (bytes == null)
            return obj;
        //利用传来的byte[]创建一个内存流
        MemoryStream ms = new MemoryStream(bytes);
        ms.Position = 0;
        BinaryFormatter formatter = new BinaryFormatter();
        obj = formatter.Deserialize(ms); //把内存流反序列成对象
        ms.Close();
        return obj;
    }

    static bool WriteRawImage(Mat image, string filename)
    {
        using (FileStream file = new FileStream(filename, FileMode.Create))
        {
            if (file == null || !file.CanWrite)
                return false;

            BinaryWriter writer = new BinaryWriter(file);
            writer.Write(image.Rows);
            writer.Write(image.Cols);
            int depth = image.Depth();
            int type = image.Type();
            int channels = image.Channels();
            writer.Write(depth);
            writer.Write(type);
            writer.Write(channels);
            int sizeInBytes = (int)image.Step() * image.Rows;
            writer.Write(sizeInBytes);
            byte[] arr = new byte[sizeInBytes];
            Marshal.Copy(image.Data, arr, 0, arr.Length);
            writer.Write(arr, 0, sizeInBytes);
        }
        return true;
    }

    static bool ReadRawImage(out Mat image, string filename)
    {
        int rows, cols, data, depth, type, channels;
        image = null;

        using (FileStream file = new FileStream(filename, FileMode.Open))
        {
            if (file == null || !file.CanRead)
                return false;

            try
            {
                BinaryReader reader = new BinaryReader(file);
                rows = reader.ReadInt32();
                cols = reader.ReadInt32();
                depth = reader.ReadInt32();
                type = reader.ReadInt32();
                channels = reader.ReadInt32();
                data = reader.ReadInt32();
                image = new OpenCvSharp.Mat(rows, cols, (OpenCvSharp.MatType)type);
                // reader.Read(image.Data, 0, data);
            }
            catch (Exception)
            {
                return false;
            }
        }

        return true;
    }
}
