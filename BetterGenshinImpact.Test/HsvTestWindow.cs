using OpenCvSharp;

namespace BetterGenshinImpact.Test;

internal class HsvTestWindow
{
    private const int MaxValueH = 360 / 2;
    static readonly int MaxValue = 255;
    //private const string WindowCaptureName = "Video Capture";
    private const string _windowDetectionName = "Object Detection";
    static int _lowH = 0, _lowS = 0, _lowV = 0;
    static int _highH = MaxValueH, _highS = MaxValue, _highV = MaxValue;

    static void on_low_H_thresh_trackbar(int pos, IntPtr userdata)
    {
        _lowH = Math.Min(_highH - 1, _lowH);
        Cv2.SetTrackbarPos("Low H", _windowDetectionName, _lowH);
    }
    static void on_high_H_thresh_trackbar(int pos, IntPtr userdata)
    {
        _highH = Math.Max(_highH, _lowH + 1);
        Cv2.SetTrackbarPos("High H", _windowDetectionName, _highH);
    }
    static void on_low_S_thresh_trackbar(int pos, IntPtr userdata)
    {
        _lowS = Math.Min(_highS - 1, _lowS);
        Cv2.SetTrackbarPos("Low S", _windowDetectionName, _lowS);
    }
    static void on_high_S_thresh_trackbar(int pos, IntPtr userdata)
    {
        _highS = Math.Max(_highS, _lowS + 1);
        Cv2.SetTrackbarPos("High S", _windowDetectionName, _highS);
    }
    static void on_low_V_thresh_trackbar(int pos, IntPtr userdata)
    {
        _lowV = Math.Min(_highV - 1, _lowV);
        Cv2.SetTrackbarPos("Low V", _windowDetectionName, _lowV);
    }
    static void on_high_V_thresh_trackbar(int pos, IntPtr userdata)
    {
        _highV = Math.Max(_highV, _lowV + 1);
        Cv2.SetTrackbarPos("High V", _windowDetectionName, _highV);
    }

    public void Run()
    {

        //Cv2.NamedWindow(WindowCaptureName);
        Cv2.NamedWindow(_windowDetectionName);
        // Trackbars to set thresholds for HSV values
        Cv2.CreateTrackbar("Low H", _windowDetectionName, ref _lowH, MaxValueH, on_low_H_thresh_trackbar);
        Cv2.CreateTrackbar("High H", _windowDetectionName, ref _highH, MaxValueH, on_high_H_thresh_trackbar);
        Cv2.CreateTrackbar("Low S", _windowDetectionName, ref _lowS, MaxValue, on_low_S_thresh_trackbar);
        Cv2.CreateTrackbar("High S", _windowDetectionName, ref _highS, MaxValue, on_high_S_thresh_trackbar);
        Cv2.CreateTrackbar("Low V", _windowDetectionName, ref _lowV, MaxValue, on_low_V_thresh_trackbar);
        Cv2.CreateTrackbar("High V", _windowDetectionName, ref _highV, MaxValue, on_high_V_thresh_trackbar);
        var frame = Cv2.ImRead(@"E:\HuiTask\更好的原神\自动秘境\视角朝向识别\1_p.png", ImreadModes.Color);
        Mat frameHsv = new Mat();
        // Convert from BGR to HSV colorspace
        Cv2.CvtColor(frame, frameHsv, ColorConversionCodes.BGR2HSV);
        Mat frameThreshold = new Mat();

        while (true)
        {
            // Detect the object based on HSV Range Values
            Cv2.InRange(frameHsv, new Scalar(_lowH, _lowS, _lowV), new Scalar(_highH, _highS, _highV), frameThreshold);
            // Show the frames
            // Cv2.ImShow(WindowCaptureName, frame);
            Cv2.ImShow(_windowDetectionName, frameThreshold);

            char key = (char)Cv2.WaitKey(30);
            if (key == 'q' || key == 27)
            {
                break;
            }
        }
    }
}