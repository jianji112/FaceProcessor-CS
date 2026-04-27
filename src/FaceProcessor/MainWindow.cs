#define TRACE
using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using FaceProcessor.Models;
using FaceProcessor.Services;
using FaceProcessor.Views;

namespace FaceProcessor;

public class MainWindow : Window, IComponentConnector
{
	private readonly ConfigManager _configManager;

	private readonly bool _preferGpu;

	private readonly FaceDetector? _detector;

	private readonly VideoProcessor _videoProcessor;

	private ImageTabView? _imageTab;

	private VideoTabView? _videoTab;

	private LogTabView? _logTab;

	internal TabControl MainTabControl;

	internal Grid ImageTabContainer;

	internal Grid VideoTabContainer;

	internal Grid LogTabContainer;

	private bool _contentLoaded;

	public MainWindow()
	{
		InitializeComponent();
		LogCollector.Install();
		_configManager = new ConfigManager();
		ApplyWindowSettings();
		_preferGpu = true;
		if (!_configManager.Config.UseGpu)
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.UseGpu = true;
			});
		}
		_detector = InitializeDetector();
		_videoProcessor = new VideoProcessor(_detector);
		InitTabs();
		base.Closing += OnWindowClosing;
	}

	private FaceDetector? InitializeDetector()
	{
		string cascadePath = GetPossibleCascadePaths().FirstOrDefault(File.Exists);
		string[] possibleModelPaths = GetPossibleModelPaths();
		foreach (string modelPath in possibleModelPaths)
		{
			if (File.Exists(modelPath))
			{
				try
				{
					FaceDetector result = new FaceDetector(modelPath, 0.5f, _preferGpu, cascadePath);
					Trace.WriteLine("[MainWindow] Detector initialized with model: " + modelPath);
					return result;
				}
				catch (Exception value)
				{
					Trace.WriteLine($"[MainWindow] Failed to initialize model {modelPath}: {value}");
				}
			}
		}
		if (!string.IsNullOrWhiteSpace(cascadePath) && File.Exists(cascadePath))
		{
			try
			{
				FaceDetector result2 = new FaceDetector(null, 0.5f, _preferGpu, cascadePath);
				Trace.WriteLine("[MainWindow] Detector initialized with cascade fallback only: " + cascadePath);
				return result2;
			}
			catch (Exception value2)
			{
				Trace.WriteLine($"[MainWindow] Failed to initialize cascade fallback {cascadePath}: {value2}");
			}
		}
		Trace.WriteLine("[MainWindow] No usable detector backend found");
		MessageBox.Show("未找到可用的人脸检测后端。\n图片和视频处理仍可继续执行，但人脸打码效果会受到影响。\n\n详细信息请查看日志页或 logs/faceprocessor.log。", "警告", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		return null;
	}

	private static string[] GetPossibleModelPaths()
	{
		string baseDir = AppDomain.CurrentDomain.BaseDirectory;
		string currentDir = Directory.GetCurrentDirectory();
		return new string[8]
		{
			Path.Combine(baseDir, "models", "face_detector.caffemodel"),
			Path.Combine(baseDir, "assets", "models", "face_detector.caffemodel"),
			Path.Combine(currentDir, "models", "face_detector.caffemodel"),
			Path.Combine(currentDir, "assets", "models", "face_detector.caffemodel"),
			Path.Combine(baseDir, "models", "yolov8n-face.onnx"),
			Path.Combine(baseDir, "assets", "models", "yolov8n-face.onnx"),
			Path.Combine(currentDir, "models", "yolov8n-face.onnx"),
			Path.Combine(currentDir, "assets", "models", "yolov8n-face.onnx")
		}.Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static string[] GetPossibleCascadePaths()
	{
		string baseDir = AppDomain.CurrentDomain.BaseDirectory;
		string currentDir = Directory.GetCurrentDirectory();
		return new string[4]
		{
			Path.Combine(baseDir, "models", "haarcascade_frontalface_default.xml"),
			Path.Combine(baseDir, "assets", "models", "haarcascade_frontalface_default.xml"),
			Path.Combine(currentDir, "models", "haarcascade_frontalface_default.xml"),
			Path.Combine(currentDir, "assets", "models", "haarcascade_frontalface_default.xml")
		}.Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private void InitTabs()
	{
		_imageTab = new ImageTabView(_configManager, _detector);
		_videoTab = new VideoTabView(_configManager, _videoProcessor);
		_logTab = new LogTabView();
		ImageTabContainer.Children.Add(_imageTab);
		VideoTabContainer.Children.Add(_videoTab);
		LogTabContainer.Children.Add(_logTab);
		bool gpuActive = _detector?.IsGpuActive ?? false;
		FaceDetector detector = _detector;
		string text = ((detector == null) ? "检测器不可用" : ((!detector.IsGpuActive) ? ((!_preferGpu) ? _detector.BackendDescription : ("GPU 不可用，已回退到 " + _detector.BackendDescription)) : ("GPU 已启用：" + _detector.BackendDescription)));
		string statusMessage = text;
		_videoTab.UpdateGpuStatus(gpuActive, statusMessage);
	}

	private void ApplyWindowSettings()
	{
		AppConfig config = _configManager.Config;
		if (config.WindowWidth > 0.0)
		{
			base.Width = Math.Max(base.MinWidth, config.WindowWidth);
		}
		if (config.WindowHeight > 0.0)
		{
			base.Height = Math.Max(base.MinHeight, config.WindowHeight);
		}
		if (HasSavedWindowPosition(config))
		{
			base.WindowStartupLocation = WindowStartupLocation.Manual;
			base.Left = config.WindowLeft.Value;
			base.Top = config.WindowTop.Value;
		}
		if (config.WindowIsMaximized)
		{
			base.Loaded += delegate
			{
				base.WindowState = WindowState.Maximized;
			};
		}
	}

	private static bool HasSavedWindowPosition(AppConfig config)
	{
		if (!config.WindowLeft.HasValue || !config.WindowTop.HasValue)
		{
			return false;
		}
		double left = config.WindowLeft.Value;
		double top = config.WindowTop.Value;
		double maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 160.0;
		double maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 120.0;
		if (left >= SystemParameters.VirtualScreenLeft - 100.0 && top >= SystemParameters.VirtualScreenTop - 100.0 && left <= maxLeft)
		{
			return top <= maxTop;
		}
		return false;
	}

	private void OnWindowClosing(object? sender, CancelEventArgs e)
	{
		Rect bounds = ((base.WindowState == WindowState.Normal) ? new Rect(base.Left, base.Top, base.Width, base.Height) : base.RestoreBounds);
		_configManager.Update(delegate(AppConfig config)
		{
			config.WindowWidth = Math.Max(base.MinWidth, bounds.Width);
			config.WindowHeight = Math.Max(base.MinHeight, bounds.Height);
			config.WindowLeft = bounds.Left;
			config.WindowTop = bounds.Top;
			config.WindowIsMaximized = base.WindowState == WindowState.Maximized;
		});
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "9.0.3.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			BamlComponentLoader.Load(this, "mainwindow.baml");
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "9.0.3.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			MainTabControl = (TabControl)target;
			break;
		case 2:
			ImageTabContainer = (Grid)target;
			break;
		case 3:
			VideoTabContainer = (Grid)target;
			break;
		case 4:
			LogTabContainer = (Grid)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
