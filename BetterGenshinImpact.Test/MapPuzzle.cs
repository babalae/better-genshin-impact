using OpenCvSharp;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.Test;

public class MapPuzzle
{
    public static readonly int block = 1024;

    public static void Put()
    {
        string folderPath = @"E:\HuiTask\更好的原神\地图匹配\Map"; // 图片文件夹路径
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
                row = int.Parse(match.Groups[1].Value);
                col = int.Parse(match.Groups[2].Value);
            }
            else
            {
                continue;
            }

            Mat img = Cv2.ImRead(imagePath);

            // 如果当前位置已经有图片了，保留尺寸较大的图片
            if (imageLocations.ContainsKey((row, col)))
            {
                if (img.Width > imageLocations[(row, col)].Img.Width || fileInfo.Length > imageLocations[(row, col)].FileLength)
                {
                    imageLocations[(row, col)] = new ImgInfo(img, name, fileInfo.Length);
                }
                else
                {
                    Debug.WriteLine($"重复 ({row}, {col}) {img.Width} {img.Height}  {name}");
                }
            }
            else
            {
                imageLocations[(row, col)] = new ImgInfo(img, name, fileInfo.Length);
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
        int totalWidth = (lenCol + 1) * block; // referenceImage是你参考的单张图片的宽度
        int totalHeight = (lenRow + 1) * block; // referenceImage是你参考的单张图片的高度

        // 创建空白大图
        Mat largeImage = new Mat(totalHeight, totalWidth, MatType.CV_8UC3, new Scalar(255, 255, 255));

        // 拼接图片
        int[,] arr = new int[lenRow + 1, lenCol + 1];
        foreach (var location in imageLocations)
        {
            int row = location.Key.row - minRow;
            int col = location.Key.col - minCol;
            Mat img = location.Value.Img;

            arr[row, col] = 1;

            // 计算当前图片在大图中的左上角位置
            int x = (lenCol - col) * block;
            int y = (lenRow - row) * block;

            // 将图片粘贴到大图上
            if (img.Width != block || img.Height != block)
            {
                img = img.Resize(new Size(block, block));
            }

            img.CopyTo(new Mat(largeImage, new Rect(x, y, img.Width, img.Height)));
        }

        for (int i = 0; i < arr.GetLength(0); i++)
        {
            for (int j = 0; j < arr.GetLength(1); j++)
            {
                Debug.Write(arr[i, j] + " ");
            }

            Debug.WriteLine("");
        }

        // 保存大图
        Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\combined_image.png", largeImage);

        // 释放资源
        largeImage.Dispose();
        foreach (var img in imageLocations.Values)
        {
            img.Img.Dispose();
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

        public ImgInfo(Mat mat, string name, long fileLength)
        {
            this.Img = mat;
            this.Name = name;
            this.FileLength = fileLength;
        }
    }
}
