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
        
        // 初始化人脸检测器
        try
        {
            var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "yolov8n-face.onnx");
            if (File.Exists(modelPath))
            {
                _detector = new FaceDetector(modelPath, 0.5f, _configManager.Config.UseGpu);
            }
        }
        catch { }
        
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

    private void InitTabs()
    {
        _imageTab = new ImageTabView(_configManager, _detector);
        _videoTab = new VideoTabView(_configManager, _videoProcessor);
        
        ImageTabContainer.Children.Add(_imageTab);
        VideoTabContainer.Children.Add(_videoTab);
        
        // 更新 GPU 状态
        var (available, info) = FaceDetector.GetGpuStatus();
        _videoTab.UpdateGpuStatus(_detector != null && available, 
            _detector != null ? info : "⚠️ 未加载模型");
    }
}
