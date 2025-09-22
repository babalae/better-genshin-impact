using OpenCvSharp;

namespace BetterGenshinImpact.Test.Cv;

public class ThresholdWindow
{
    private Mat? _originalImage;
    private Mat? _grayImage;
    private int _currentThreshold = 160;
    
    
    public static void Test()
    {
        var window = new ThresholdWindow();
        window.ShowThresholdAdjuster(@"E:\HuiTask\更好的原神\自动拾取\pick_ocr_ori_20250915011455192.png");
    }
    
    /// <summary>
    /// 对指定图片进行二值化阈值拉条调整
    /// </summary>
    /// <param name="imagePath">图片路径</param>
    public void ShowThresholdAdjuster(string imagePath)
    {
        // 加载原始图像
        _originalImage = Cv2.ImRead(imagePath);
        if (_originalImage.Empty())
        {
            throw new ArgumentException("无法加载图像文件");
        }
        
        // 转换为灰度图像
        _grayImage = new Mat();
        Cv2.CvtColor(_originalImage, _grayImage, ColorConversionCodes.BGR2GRAY);
        
        // 创建窗口
        const string windowName = "Threshold Adjuster";
        const string trackbarName = "Threshold";
        
        Cv2.NamedWindow(windowName, WindowFlags.AutoSize);
        
        // 创建拉条，范围0-255
        Cv2.CreateTrackbar(trackbarName, windowName, ref _currentThreshold, 255, OnThresholdChanged);
        
        // 初始显示
        UpdateThresholdImage(windowName);
        
        // 等待用户按键
        Console.WriteLine("按任意键关闭窗口...");
        Cv2.WaitKey(0);
        
        // 清理资源
        Cv2.DestroyAllWindows();
        _originalImage?.Dispose();
        _grayImage?.Dispose();
    }
    
    /// <summary>
    /// 阈值变化回调函数
    /// </summary>
    /// <param name="value">阈值</param>
    /// <param name="userdata">用户数据指针（未使用）</param>
    private void OnThresholdChanged(int value, IntPtr userdata)
    {
        _currentThreshold = value;
        UpdateThresholdImage("Threshold Adjuster");
    }
    
    /// <summary>
    /// 更新二值化图像显示
    /// </summary>
    /// <param name="windowName">窗口名称</param>
    private void UpdateThresholdImage(string windowName)
    {
        if (_grayImage == null) return;
        
        using var thresholdImage = new Mat();
        Cv2.Threshold(_grayImage, thresholdImage, _currentThreshold, 255, ThresholdTypes.Binary);
        
        Cv2.ImShow(windowName, thresholdImage);
    }
}