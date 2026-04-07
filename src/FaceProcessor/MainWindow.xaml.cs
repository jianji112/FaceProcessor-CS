using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FaceProcessor.Models;
using FaceProcessor.Services;
using FaceProcessor.Views;

namespace FaceProcessor;

public partial class MainWindow : Window
{
    private readonly ConfigManager _configManager;
    private readonly FaceDetector? _detector;
    private readonly VideoProcessor _videoProcessor;
    
    private ImageTabView? _imageTab;
    private VideoTabView? _videoTab;
    private readonly DispatcherTimer _signatureTimer;

    public MainWindow()
    {
        InitializeComponent();
        
        _configManager = new ConfigManager();
        
        // 初始化人脸检测器（带日志）
        _detector = InitializeDetector();
        
        _videoProcessor = new VideoProcessor(_detector);
        
        // 初始化标签页
        InitTabs();
        
        // 设置署名定时器：60秒后隐藏
        _signatureTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60)
        };
        _signatureTimer.Tick += (s, e) =>
        {
            SignatureText.Visibility = Visibility.Collapsed;
            _signatureTimer.Stop();
        };
        _signatureTimer.Start();
    }

    private FaceDetector? InitializeDetector()
    {
        // 尝试多个可能的模型路径
        var possiblePaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "yolov8n-face.onnx"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yolov8n-face.onnx"),
            Path.Combine(Directory.GetCurrentDirectory(), "models", "yolov8n-face.onnx"),
            Path.Combine(Directory.GetCurrentDirectory(), "yolov8n-face.onnx")
        };

        foreach (var modelPath in possiblePaths)
        {
            if (File.Exists(modelPath))
            {
                var fileInfo = new FileInfo(modelPath);
                System.Diagnostics.Debug.WriteLine($"[FaceProcessor] 找到模型: {modelPath}, 大小: {fileInfo.Length} bytes");
                
                // 检查文件大小（正常模型应该 > 5MB）
                if (fileInfo.Length < 1_000_000)
                {
                    System.Diagnostics.Debug.WriteLine($"[FaceProcessor] 警告: 模型文件太小，可能损坏");
                    continue;
                }

                try
                {
                    var detector = new FaceDetector(modelPath, 0.5f, _configManager.Config.UseGpu);
                    System.Diagnostics.Debug.WriteLine($"[FaceProcessor] 人脸检测器初始化成功");
                    return detector;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FaceProcessor] 模型加载失败: {ex.Message}");
                    MessageBox.Show($"模型加载失败: {ex.Message}\n\n人脸检测将不可用", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[FaceProcessor] 未找到有效的模型文件");
        MessageBox.Show("未找到人脸检测模型文件 (yolov8n-face.onnx)\n\n人脸检测功能将不可用，请确保 models 文件夹中包含有效的模型文件。", 
            "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        return null;
    }

    private void InitTabs()
    {
        _imageTab = new ImageTabView(_configManager, _detector);
        _videoTab = new VideoTabView(_configManager, _videoProcessor);
        
        ImageTabContainer.Children.Add(_imageTab);
        VideoTabContainer.Children.Add(_videoTab);
        
        // 更新 GPU 状态
        var (available, info) = FaceDetector.GetGpuStatus();
        var statusMessage = _detector != null 
            ? (available ? $"✅ {info}" : "⚠️ CPU 模式")
            : "❌ 模型未加载";
        _videoTab.UpdateGpuStatus(_detector != null && available, statusMessage);
    }
}
