using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.ViewModel.Windows.GearTask;
using BetterGenshinImpact.ViewModel.Pages.Component;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;
using Image = SixLabors.ImageSharp.Image;

namespace BetterGenshinImpact.View.Windows.GearTask;

/// <summary>
/// 地图追踪任务选择窗口
/// </summary>
public partial class PathingTaskSelectionWindow : FluentWindow
{
    /// <summary>
    /// ViewModel
    /// </summary>
    public PathingTaskSelectionViewModel ViewModel { get; }

    /// <summary>
    /// 选中的任务
    /// </summary>
    public PathingTaskInfo? SelectedTask { get; private set; }

    /// <summary>
    /// 对话框结果
    /// </summary>
    public bool DialogResult { get; private set; }

    /// <summary>
    /// 添加的任务列表
    /// </summary>
    public List<GearTaskViewModel> AddedTasks { get; private set; } = new();

    /// <summary>
    /// 目录到符号图标的转换器
    /// </summary>
    public static readonly DirectoryToSymbolConverter DirectoryToSymbolConverter = new();

    public PathingTaskSelectionWindow()
    {
        ViewModel = new PathingTaskSelectionViewModel();
        DataContext = ViewModel;
        InitializeComponent();
        
        // 绑定TreeView的选中项变化事件
        TaskTreeView.SelectedItemChanged += OnTreeViewSelectedItemChanged;
        
        // 订阅任务添加事件
        ViewModel.OnTaskAdded += OnTaskAdded;
    }
    
    /// <summary>
    /// 任务添加事件处理
    /// </summary>
    private void OnTaskAdded(List<GearTaskViewModel> tasks)
    {
        AddedTasks.AddRange(tasks);
        
        // 添加任务后自动关闭窗口
        CloseWithResult();
    }
    
    /// <summary>
    /// TreeView选中项变化事件处理
    /// </summary>
    private void OnTreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is PathingTaskInfo selectedTask)
        {
            ViewModel.SelectedTask = selectedTask;
        }
    }

    /// <summary>
    /// 任务添加完成后关闭窗口
    /// </summary>
    public void CloseWithResult()
    {
        SelectedTask = ViewModel.SelectedTask;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

/// <summary>
/// 目录到符号图标的转换器
/// </summary>
public class DirectoryToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isDirectory)
        {
            return isDirectory ? SymbolRegular.Folder24 : SymbolRegular.Document24;
        }
        return SymbolRegular.Document24;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔值到可见性的转换器（支持参数反转）
/// </summary>
public class BooleanToVisibilityParameterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            bool invert = parameter?.ToString() == "Invert";
            if (invert)
                boolValue = !boolValue;
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔值到可见性的反向转换器
/// </summary>
public class BooleanToVisibilityInvertConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 空值到可见性的转换器
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString() == "Invert";
        bool isNull = value == null;
        
        if (invert)
            isNull = !isNull;
            
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 字符串到ImageSource的转换器，处理空字符串情况，支持WebP格式
/// </summary>
public class StringToImageSourceConverter : IValueConverter
{
    private static readonly HttpClient HttpClient = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string url && !string.IsNullOrEmpty(url))
        {
            try
            {
                // 检查是否为WebP格式或其他需要特殊处理的格式
                if (url.Contains(".webp", StringComparison.OrdinalIgnoreCase))
                {
                    return LoadWebPImage(url);
                }
                else
                {
                    // 对于其他格式，使用原有的BitmapImage
                    return new BitmapImage(new Uri(url));
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return null;
            }
        }
        return null;
    }

    private static BitmapSource? LoadWebPImage(string url)
    {
        try
        {
            byte[] imageData;
            
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // 从网络加载
                imageData = HttpClient.GetByteArrayAsync(url).Result;
            }
            else
            {
                // 从本地文件加载
                imageData = File.ReadAllBytes(url);
            }

            using var image = Image.Load<Rgba32>(imageData);
            
            // 转换为WPF可用的BitmapSource
            var bitmap = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null);
            
            bitmap.Lock();
            try
            {
                var backBuffer = bitmap.BackBuffer;
                var stride = bitmap.BackBufferStride;
                
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var pixelRow = accessor.GetRowSpan(y);
                        var targetPtr = backBuffer + y * stride;
                        
                        for (int x = 0; x < pixelRow.Length; x++)
                        {
                            var pixel = pixelRow[x];
                            // 转换RGBA到BGRA
                            var bgra = (pixel.A << 24) | (pixel.R << 16) | (pixel.G << 8) | pixel.B;
                            System.Runtime.InteropServices.Marshal.WriteInt32(targetPtr + x * 4, bgra);
                        }
                    }
                });
            }
            finally
            {
                bitmap.AddDirtyRect(new Int32Rect(0, 0, image.Width, image.Height));
                bitmap.Unlock();
            }
            
            return bitmap;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Failed to load WebP image: {e}");
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}