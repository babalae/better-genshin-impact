using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using OpenCvSharp;

namespace BetterGenshinImpact.Test.Simple.AllMap;

public class MapPuzzle
{

    public static List<string> PicWhiteHashList = new List<string>
    {
        "25E23B0D18C2CBEA19D28E5E399D42FA",
        "3B4847FEA7D506EAF3B75D4F4541E867",
        "CCCAF02768432A46AA0001E51DC5991B",
        "F80C609208063B289A05B8A1E0351226",
        "06767779699056515930D0597072AB7D",
        "04F757BD57FBB5FA74A6C9C0634BB122",
        "04C1EDFCD89249209D1DD885DD2F2129",
        "01B8641F5F58BBDE6E10F53D56EA7288",
        "AF56BDE27BDF534317A9FB34E08165C0",
        "05F4EAB8C6BFBADD60B8A5CCDD614F83"
    };

    public static MD5 Md5Service = MD5.Create();

    public static void PutAll()
    {
        // 保存2048大图
        var img2048 = Put(2048);
        Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\拼图结果\5.8\map_2048.png", img2048);
        
        // 保存1024
        var img1024 = Put(1024);
        Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\拼图结果\5.8\map_1024.png", img1024);
        
        // 保存256
        var grayImage = new Mat();
        Cv2.CvtColor(img2048.Resize(new Size(img2048.Width / 8, img2048.Height / 8), 0, 0, InterpolationFlags.Cubic), grayImage, ColorConversionCodes.BGR2GRAY);
        Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\拼图结果\5.8\map_256.png", grayImage);

    }
    
    public static Mat Put(int block = 2048)
    {
        string folderPath = @"E:\HuiTask\更好的原神\地图匹配\地图块素材\UI_Map_最新"; // 图片文件夹路径
        string pattern = @"UI_MapBack_([-+]?\d+)_([-+]?\d+)(.*)";
        var images = Directory.GetFiles(folderPath, "*.png", SearchOption.TopDirectoryOnly); // 获取所有图片文件路径

        // 解析图片位置信息并存储到字典中
        var imageLocations = new Dictionary<(int row, int col), ImgInfo>();
        foreach (var imagePath in images)
        {
            // 获取文件大小
            var fileInfo = new FileInfo(imagePath);

            // 解析图片名称中的行列信息
            var name = Path.GetFileNameWithoutExtension(imagePath);
            var match = Regex.Match(name, pattern);
            int row, col;
            if (match.Success)
            {
                // Debug.WriteLine($"已匹配 ({match.Groups[1].Value}, {match.Groups[2].Value}) {name}");
                row = int.Parse(match.Groups[1].Value);
                col = int.Parse(match.Groups[2].Value);
            }
            else
            {
                // Debug.WriteLine($"未匹配 {name}");
                continue;
            }

            // 排除指定行列的图片
            if ((row, col) == (4, 6) || (row, col) == (5, 6) || (row, col) == (5, 5) || (row, col) == (5, 2) || (row, col) == (5, 1) || (row, col) == (4, 1))
            {
                continue;
            }

            // 读取图片并计算hash值
            Mat img = Cv2.ImRead(imagePath);
            var hashBytes = Md5Service.ComputeHash(File.ReadAllBytes(imagePath));
            var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToUpperInvariant();

            if (img.Width < 8)
            {
                Debug.WriteLine($"太小的不要 ({row}, {col}) {img.Width} {img.Height}  {name}");
                continue;
            }

            // 如果只要城市拼接，取消注释这段
            // if (block == 2048 && img.Width != 2048)
            // {
            //     Debug.WriteLine($"不是2048的不要 ({row}, {col}) {img.Width} {img.Height}  {name}");
            //     continue;
            // }

            // 如果当前位置已经有图片了，保留尺寸较大的图片
            if (imageLocations.ContainsKey((row, col)))
            {
                // 如果当前位置的图片已经被hash锁定，跳过
                if (imageLocations[(row, col)].Locked)
                {
                    Debug.WriteLine($"已锁定 ({row}, {col}) {name}");
                    continue;
                }

                if (img.Width > imageLocations[(row, col)].Img.Width || fileInfo.Length > imageLocations[(row, col)].FileLength || PicWhiteHashList.Contains(hash))
                {
                    imageLocations[(row, col)] = new ImgInfo(img, name, fileInfo.Length, PicWhiteHashList.Contains(hash));
                }
                else
                {
                    Debug.WriteLine($"重复 ({row}, {col}) {img.Width} {img.Height}  {name}");
                }
            }
            else
            {
                imageLocations[(row, col)] = new ImgInfo(img, name, fileInfo.Length, PicWhiteHashList.Contains(hash));
            }
        }

        int minRow = imageLocations.Keys.Min(key => key.row);
        int minCol = imageLocations.Keys.Min(key => key.col);

        // 确定大图的行数和列数
        int maxRow = imageLocations.Keys.Max(key => key.row);
        int maxCol = imageLocations.Keys.Max(key => key.col);

        // 计算大图的总宽度和高度
        var lenCol = maxCol - minCol;
        var lenRow = maxRow - minRow;
        Debug.WriteLine($"列数X: {lenCol+1}, 行数Y: {lenRow+1}");
        int totalWidth = (lenCol + 1) * block;
        int totalHeight = (lenRow + 1) * block;

        // 创建空白大图
        Mat largeImage = new Mat(totalHeight, totalWidth, MatType.CV_8UC3, new Scalar(0, 0, 0));

        // 拼接图片
        int[,] arr = new int[lenRow + 1, lenCol + 1];
        foreach (var location in imageLocations)
        {
            int row = location.Key.row - minRow;
            int col = location.Key.col - minCol;
            Mat img = location.Value.Img;

            arr[row, col] = 1;

            // 计算当前图片在大图中的左上角位置
            int x = (lenCol - col) * block; // 顺序倒过来了，屮
            int y = (lenRow - row) * block; // 顺序倒过来了，屮

            // 将图片粘贴到大图上
            if (img.Width != block || img.Height != block)
            {
                img = img.Resize(new Size(block, block), 0, 0, InterpolationFlags.Nearest);
            }

            // 添加位置标识
            // img.PutText($"{location.Key.row} , {location.Key.col}", new Point(50, 50), HersheyFonts.HersheyComplex, 2, Scalar.Red, 2, LineTypes.Link8);

            img.CopyTo(new Mat(largeImage, new Rect(x, y, img.Width, img.Height)));
        }

        // 康康二维数组的拼接结果
        for (int i = 0; i < arr.GetLength(0); i++)
        {
            for (int j = 0; j < arr.GetLength(1); j++)
            {
                Debug.Write(arr[i, j] + " ");
            }

            Debug.WriteLine("");
        }

        // 地图图片块
        // SaveImagesAs1024X1024(arr, imageLocations, @"E:\HuiTask\更好的原神\地图匹配\有用的素材\5.0\地图块", minRow, minCol);


        // Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\combined_image_small.png", largeImage.Resize(new Size(1400, 1300), 0, 0, InterpolationFlags.Cubic));

        // 释放资源
        return largeImage;
    }

    public static void SaveImagesAs1024X1024(int[,] arr, Dictionary<(int row, int col), ImgInfo> imageLocations, string outputFolder, int minRow, int minCol)
    {
        if (!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
        }

        for (int row = 0; row < arr.GetLength(0); row++)
        {
            for (int col = 0; col < arr.GetLength(1); col++)
            {
                if (arr[row, col] == 1)
                {
                    var key = (row + minRow, col + minCol);
                    if (imageLocations.TryGetValue(key, out var imgInfo))
                    {
                        var img = imgInfo.Img;
                        if (img.Width != 1204 || img.Height != 1204)
                        {
                            img = imgInfo.Img.Resize(new Size(1024, 1024), 0, 0, InterpolationFlags.Nearest);
                        }
                        var outputPath = Path.Combine(outputFolder, $"{row}_{col}.png");
                        Cv2.ImWrite(outputPath, img);
                        img.Dispose();
                    }
                }
            }
        }
    }

    public class ImgInfo
    {
        public Mat Img { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// 文件大小
        /// </summary>
        public long FileLength { get; set; }

        public bool Locked { get; set; }

        public ImgInfo(Mat mat, string name, long fileLength, bool locked = false)
        {
            Img = mat;
            Name = name;
            FileLength = fileLength;
            Locked = locked;
        }
    }
}
