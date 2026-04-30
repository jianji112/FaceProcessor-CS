using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using FaceProcessor.Models;
using FaceProcessor.Services;

namespace FaceProcessor;

public partial class MainWindow : Window
{
	private readonly ConfigManager _configManager;

	private readonly FaceDetector? _detector;

	private readonly VideoProcessor _videoProcessor;

	public MainWindow()
	{
		InitializeComponent();
		TryLoadWindowIcon();
		_configManager = new ConfigManager();
		ApplyWindowSettings();
		EnsureGpuPreference();
		_detector = InitializeDetector();
		_videoProcessor = new VideoProcessor(_detector);
		ImageTab.Initialize(_configManager, _detector);
		VideoTab.Initialize(_configManager, _videoProcessor, _detector);
		UpdateRuntimeBadges();
		Closing += OnWindowClosing;
	}

	private void TryLoadWindowIcon()
	{
		string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.png");
		if (!File.Exists(iconPath))
		{
			return;
		}

		try
		{
			Icon = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
		}
		catch (Exception ex)
		{
			Trace.WriteLine("[MainWindow] Failed to load window icon: " + ex.Message);
		}
	}

	private void EnsureGpuPreference()
	{
		if (_configManager.Config.UseGpu)
		{
			return;
		}

		_configManager.Update(config => config.UseGpu = true);
	}

	private FaceDetector? InitializeDetector()
	{
		string? cascadePath = GetPossibleCascadePaths().FirstOrDefault(File.Exists);
		foreach (string modelPath in GetPossibleModelPaths())
		{
			if (!File.Exists(modelPath))
			{
				continue;
			}

			try
			{
				FaceDetector detector = new FaceDetector(modelPath, 0.5f, _configManager.Config.UseGpu, cascadePath);
				Trace.WriteLine("[MainWindow] Detector initialized with model: " + modelPath);
				return detector;
			}
			catch (Exception ex)
			{
				Trace.WriteLine($"[MainWindow] Failed to initialize model {modelPath}: {ex}");
			}
		}

		if (!string.IsNullOrWhiteSpace(cascadePath) && File.Exists(cascadePath))
		{
			try
			{
				FaceDetector detector = new FaceDetector(null, 0.5f, _configManager.Config.UseGpu, cascadePath);
				Trace.WriteLine("[MainWindow] Detector initialized with cascade fallback only: " + cascadePath);
				return detector;
			}
			catch (Exception ex2)
			{
				Trace.WriteLine($"[MainWindow] Failed to initialize cascade fallback {cascadePath}: {ex2}");
			}
		}

		Trace.WriteLine("[MainWindow] No usable detector backend found.");
		MessageBox.Show("未找到可用的人脸检测后端。\n图片与视频仍可打开，但人脸处理功能会不可用。\n\n详细日志见：\n" + LogCollector.LogFilePath, "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Warning);
		return null;
	}

	private void UpdateRuntimeBadges()
	{
		string detectorSummary = _detector == null ? "检测后端：不可用" : (_detector.IsGpuActive ? $"检测后端：GPU · {_detector.BackendDescription}" : $"检测后端：CPU 回退 · {_detector.BackendDescription}");
		string encoderSummary = "编码后端：" + VideoProcessor.GetEncodingCapabilitySummary();
		DetectorBadgeText.Text = detectorSummary;
		EncoderBadgeText.Text = encoderSummary;
		VideoTab.UpdateRuntimeSummary(detectorSummary, encoderSummary, _detector?.IsGpuActive == true);
		ImageTab.UpdateRuntimeSummary(detectorSummary);
	}

	private static string[] GetPossibleModelPaths()
	{
		string baseDir = AppDomain.CurrentDomain.BaseDirectory;
		string currentDir = Directory.GetCurrentDirectory();
		return new[]
		{
			Path.Combine(baseDir, "models", "yolov8n-face.onnx"),
			Path.Combine(baseDir, "assets", "models", "yolov8n-face.onnx"),
			Path.Combine(currentDir, "models", "yolov8n-face.onnx"),
			Path.Combine(currentDir, "assets", "models", "yolov8n-face.onnx"),
			Path.Combine(baseDir, "models", "face_detector.caffemodel"),
			Path.Combine(baseDir, "assets", "models", "face_detector.caffemodel"),
			Path.Combine(currentDir, "models", "face_detector.caffemodel"),
			Path.Combine(currentDir, "assets", "models", "face_detector.caffemodel")
		}.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static string[] GetPossibleCascadePaths()
	{
		string baseDir = AppDomain.CurrentDomain.BaseDirectory;
		string currentDir = Directory.GetCurrentDirectory();
		return new[]
		{
			Path.Combine(baseDir, "models", "haarcascade_frontalface_default.xml"),
			Path.Combine(baseDir, "assets", "models", "haarcascade_frontalface_default.xml"),
			Path.Combine(currentDir, "models", "haarcascade_frontalface_default.xml"),
			Path.Combine(currentDir, "assets", "models", "haarcascade_frontalface_default.xml")
		}.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private void ApplyWindowSettings()
	{
		AppConfig config = _configManager.Config;
		if (config.WindowWidth > 0)
		{
			Width = Math.Max(MinWidth, config.WindowWidth);
		}

		if (config.WindowHeight > 0)
		{
			Height = Math.Max(MinHeight, config.WindowHeight);
		}

		if (HasSavedWindowPosition(config))
		{
			WindowStartupLocation = WindowStartupLocation.Manual;
			Left = config.WindowLeft!.Value;
			Top = config.WindowTop!.Value;
		}

		if (config.WindowIsMaximized)
		{
			Loaded += (_, _) => WindowState = WindowState.Maximized;
		}
	}

	private static bool HasSavedWindowPosition(AppConfig config)
	{
		if (!config.WindowLeft.HasValue || !config.WindowTop.HasValue)
		{
			return false;
		}

		double maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 160;
		double maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 120;
		return config.WindowLeft.Value >= SystemParameters.VirtualScreenLeft - 100
			&& config.WindowTop.Value >= SystemParameters.VirtualScreenTop - 100
			&& config.WindowLeft.Value <= maxLeft
			&& config.WindowTop.Value <= maxTop;
	}

	private void OnWindowClosing(object? sender, CancelEventArgs e)
	{
		Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
		_configManager.Update(config =>
		{
			config.WindowWidth = Math.Max(MinWidth, bounds.Width);
			config.WindowHeight = Math.Max(MinHeight, bounds.Height);
			config.WindowLeft = bounds.Left;
			config.WindowTop = bounds.Top;
			config.WindowIsMaximized = WindowState == WindowState.Maximized;
		});

		_detector?.Dispose();
	}
}
