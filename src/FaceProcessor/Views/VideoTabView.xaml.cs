using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FaceProcessor.Models;
using FaceProcessor.Services;
using Microsoft.Win32;
using OpenCvSharp;

namespace FaceProcessor.Views;

public partial class VideoTabView : UserControl
{
	private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".mp4",
		".avi",
		".mov",
		".mkv",
		".flv",
		".wmv"
	};

	private ConfigManager? _configManager;

	private VideoProcessor? _processor;

	private FaceDetector? _detector;

	private CancellationTokenSource? _cts;

	private bool _eventsBound;

	private bool _isInitialized;

	public VideoTabView()
	{
		InitializeComponent();
	}

	public void Initialize(ConfigManager configManager, VideoProcessor processor, FaceDetector? detector)
	{
		if (_isInitialized)
		{
			return;
		}

		_configManager = configManager;
		_processor = processor;
		_detector = detector;
		LoadConfig();
		BindEvents();
		UpdateOutputDirectoryState();
		_isInitialized = true;
	}

	public void UpdateRuntimeSummary(string detectorSummary, string encoderSummary, bool gpuActive)
	{
		GpuStatusText.Text = detectorSummary;
		GpuStatusText.Foreground = gpuActive ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("WarningBrush");
		EngineSummaryText.Text = encoderSummary;
	}

	private void LoadConfig()
	{
		AppConfig config = EnsureConfigManager().Config;
		ModeCombo.SelectedIndex = config.VideoMode == ProcessMode.Blur ? 1 : 0;
		BlockSizeTextBox.Text = config.VideoBlockSize.ToString();
		BlurStrengthTextBox.Text = config.VideoBlurStrength.ToString();
		FaceThresholdSlider.Value = config.VideoFaceThreshold;
		FaceThresholdText.Text = config.VideoFaceThreshold.ToString("0.00");
		KeepAudioCheckBox.IsChecked = config.VideoKeepAudio;
		VideoFaceCountCombo.SelectedIndex = GetFaceCountComboIndex(config.VideoFaceCountLimit);
		OutputResolutionCombo.SelectedIndex = config.VideoOutputResolution switch
		{
			VideoOutputResolution.P1080 => 1,
			VideoOutputResolution.P720 => 2,
			_ => 0
		};
	}

	private void BindEvents()
	{
		if (_eventsBound)
		{
			return;
		}

		_eventsBound = true;

		ModeCombo.SelectionChanged += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			_configManager.Update(config => config.VideoMode = ModeCombo.SelectedIndex == 0 ? ProcessMode.Mosaic : ProcessMode.Blur);
		};

		BlockSizeTextBox.TextChanged += (_, _) =>
		{
			if (_configManager == null || !int.TryParse(BlockSizeTextBox.Text, out int value))
			{
				return;
			}

			_configManager.Update(config => config.VideoBlockSize = value);
		};

		BlurStrengthTextBox.TextChanged += (_, _) =>
		{
			if (_configManager == null || !int.TryParse(BlurStrengthTextBox.Text, out int value))
			{
				return;
			}

			_configManager.Update(config => config.VideoBlurStrength = value);
		};

		FaceThresholdSlider.ValueChanged += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			FaceThresholdText.Text = FaceThresholdSlider.Value.ToString("0.00");
			_configManager.Update(config => config.VideoFaceThreshold = FaceThresholdSlider.Value);
		};

		KeepAudioCheckBox.Checked += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			_configManager.Update(config => config.VideoKeepAudio = true);
		};

		KeepAudioCheckBox.Unchecked += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			_configManager.Update(config => config.VideoKeepAudio = false);
		};

		VideoFaceCountCombo.SelectionChanged += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			_configManager.Update(config => config.VideoFaceCountLimit = GetSelectedFaceCountLimit(VideoFaceCountCombo));
		};

		OutputResolutionCombo.SelectionChanged += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			_configManager.Update(config => config.VideoOutputResolution = OutputResolutionCombo.SelectedIndex switch
			{
				1 => VideoOutputResolution.P1080,
				2 => VideoOutputResolution.P720,
				_ => VideoOutputResolution.Original
			});
		};

		OutputPathTextBox.TextChanged += (_, _) => UpdateOutputDirectoryState();
	}

	private async void SelectVideo_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new()
		{
			Filter = "视频|*.mp4;*.avi;*.mov;*.mkv;*.flv;*.wmv"
		};
		if (dialog.ShowDialog() != true)
		{
			return;
		}

		await LoadSelectedVideoAsync(dialog.FileName);
	}

	private async Task LoadSelectedVideoAsync(string videoPath)
	{
		VideoPathTextBox.Text = videoPath;
		string directory = Path.GetDirectoryName(videoPath) ?? string.Empty;
		string name = Path.GetFileNameWithoutExtension(videoPath);
		OutputPathTextBox.Text = Path.Combine(directory, name + "_processed.mp4");
		UpdateOutputDirectoryState();
		await LoadVideoPreviewAsync(videoPath);
	}

	private void RootDragEnter(object sender, DragEventArgs e)
	{
		UpdateDropState(e);
	}

	private void RootDragOver(object sender, DragEventArgs e)
	{
		UpdateDropState(e);
	}

	private void RootDragLeave(object sender, DragEventArgs e)
	{
		System.Windows.Point position = e.GetPosition(RootGrid);
		if (position.X < 0 || position.Y < 0 || position.X > RootGrid.ActualWidth || position.Y > RootGrid.ActualHeight)
		{
			SetDropOverlayVisible(false);
		}
	}

	private async void RootDrop(object sender, DragEventArgs e)
	{
		if (TryResolveDroppedVideo(e, out string? videoPath))
		{
			await LoadSelectedVideoAsync(videoPath);
		}
		else if (TryGetDroppedPaths(e, out _))
		{
			MessageBox.Show("\u672A\u53D1\u73B0\u53EF\u5BFC\u5165\u7684\u89C6\u9891\u6587\u4EF6\u3002", "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Information);
		}

		SetDropOverlayVisible(false);
		e.Handled = true;
	}

	private void UpdateDropState(DragEventArgs e)
	{
		bool canAccept = TryResolveDroppedVideo(e, out _);
		e.Effects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;
		SetDropOverlayVisible(canAccept);
		e.Handled = true;
	}

	private static bool TryResolveDroppedVideo(DragEventArgs e, out string? videoPath)
	{
		videoPath = null;
		if (!TryGetDroppedPaths(e, out string[] paths))
		{
			return false;
		}

		videoPath = FindFirstSupportedVideo(paths);
		return !string.IsNullOrWhiteSpace(videoPath);
	}

	private static bool TryGetDroppedPaths(DragEventArgs e, out string[] paths)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop)
			&& e.Data.GetData(DataFormats.FileDrop) is string[] droppedPaths
			&& droppedPaths.Length > 0)
		{
			paths = droppedPaths;
			return true;
		}

		paths = [];
		return false;
	}

	private static string? FindFirstSupportedVideo(IEnumerable<string> paths)
	{
		foreach (string path in paths)
		{
			if (File.Exists(path) && IsSupportedVideoFile(path))
			{
				return path;
			}

			if (!Directory.Exists(path))
			{
				continue;
			}

			IEnumerable<string> files;
			try
			{
				files = Directory.EnumerateFiles(path);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				continue;
			}

			string? firstVideo = files.FirstOrDefault(IsSupportedVideoFile);
			if (!string.IsNullOrWhiteSpace(firstVideo))
			{
				return firstVideo;
			}
		}

		return null;
	}

	private static bool IsSupportedVideoFile(string path)
	{
		return SupportedVideoExtensions.Contains(Path.GetExtension(path));
	}

	private void SetDropOverlayVisible(bool isVisible)
	{
		DropOverlay.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
	}

	private void BrowseOutput_Click(object sender, RoutedEventArgs e)
	{
		OpenFolderDialog dialog = new();
		if (dialog.ShowDialog() == true)
		{
			string name = string.IsNullOrWhiteSpace(VideoPathTextBox.Text) ? "output" : Path.GetFileNameWithoutExtension(VideoPathTextBox.Text);
			OutputPathTextBox.Text = Path.Combine(dialog.FolderName, name + "_processed.mp4");
		}
	}

	private void OpenOutputDirectory_Click(object sender, RoutedEventArgs e)
	{
		string? outputDirectory = ResolveOutputDirectory();
		if (string.IsNullOrWhiteSpace(outputDirectory))
		{
			MessageBox.Show("请先设置输出路径。", "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}

		try
		{
			Directory.CreateDirectory(outputDirectory);
			Process.Start(new ProcessStartInfo
			{
				FileName = outputDirectory,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show("打开输出目录失败：\n" + ex.Message, "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	private async void Process_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(VideoPathTextBox.Text) || !File.Exists(VideoPathTextBox.Text))
		{
			MessageBox.Show("请先选择源视频。", "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		if (string.IsNullOrWhiteSpace(OutputPathTextBox.Text))
		{
			MessageBox.Show("请先设置输出路径。", "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		VideoProcessor processor = EnsureProcessor();
		ProcessBtn.Visibility = Visibility.Collapsed;
		CancelBtn.Visibility = Visibility.Visible;
		CancelBtn.IsEnabled = true;
		ProgressBar.Value = 0;
		ProgressText.Text = "0%";
		StatusText.Text = "正在初始化...";
		processor.Reset();
		_cts = new CancellationTokenSource();

		Progress<double> progress = new(value =>
		{
			ProgressBar.Value = value < 0 ? 0 : value * 100;
			ProgressText.Text = value < 0 ? "已停止" : $"{value * 100:0}%";
			if (value < 0)
			{
				StatusText.Text = "已取消";
			}
			else if (value >= 0.95)
			{
				StatusText.Text = "正在合并音频...";
			}
			else if (value > 0)
			{
				StatusText.Text = "正在处理视频...";
			}
		});

		ProcessOptions options = new()
		{
			BlockSize = int.TryParse(BlockSizeTextBox.Text, out int blockSize) ? blockSize : 0,
			BlurStrength = int.TryParse(BlurStrengthTextBox.Text, out int blurStrength) ? blurStrength : 99,
			FaceThreshold = (float)FaceThresholdSlider.Value,
			MaxFacesToProcess = GetSelectedFaceCountLimit(VideoFaceCountCombo),
			KeepAudio = KeepAudioCheckBox.IsChecked == true,
			OutputResolution = OutputResolutionCombo.SelectedIndex switch
			{
				1 => VideoOutputResolution.P1080,
				2 => VideoOutputResolution.P720,
				_ => VideoOutputResolution.Original
			}
		};
		ProcessMode mode = ModeCombo.SelectedIndex == 0 ? ProcessMode.Mosaic : ProcessMode.Blur;

		bool success = false;
		bool cancelled = false;
		try
		{
			success = await processor.ProcessVideoAsync(VideoPathTextBox.Text, OutputPathTextBox.Text, mode, options, progress, _cts.Token);
			cancelled = _cts.Token.IsCancellationRequested;
			if (!string.IsNullOrWhiteSpace(processor.LastEncodingBackend))
			{
				EngineSummaryText.Text = "编码后端：" + processor.LastEncodingBackend;
			}
		}
		catch (OperationCanceledException)
		{
			cancelled = true;
		}
		catch (Exception ex)
		{
			Trace.WriteLine($"[VideoTab] Unexpected processing error: {ex}");
			StatusText.Text = "错误：" + ex.Message;
		}

		ProcessBtn.Visibility = Visibility.Visible;
		CancelBtn.Visibility = Visibility.Collapsed;

		if (cancelled)
		{
			StatusText.Text = "已取消";
		}
		else if (success)
		{
			StatusText.Text = "处理完成";
		}
		else
		{
			StatusText.Text = "处理失败";
		}
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		_cts?.Cancel();
		_processor?.Cancel();
		StatusText.Text = "正在取消...";
		CancelBtn.IsEnabled = false;
	}

	private async Task LoadVideoPreviewAsync(string videoPath)
	{
		PreviewPlaceholderText.Visibility = Visibility.Visible;
		PreviewPlaceholderText.Text = "正在读取首帧...";
		PreviewSummaryText.Text = "正在分析视频";

		try
		{
			VideoPreviewResult result = await Task.Run(() => BuildPreview(videoPath));
			PreviewImage.Source = result.Bitmap;
			PreviewPlaceholderText.Visibility = Visibility.Collapsed;
			PreviewSummaryText.Text = $"预览帧：{result.Width} × {result.Height}";
			VideoInfoText.Text = result.Summary;
		}
		catch (Exception ex)
		{
			Trace.WriteLine($"[VideoTab] Failed to load preview for {videoPath}: {ex}");
			PreviewImage.Source = null;
			PreviewPlaceholderText.Visibility = Visibility.Visible;
			PreviewPlaceholderText.Text = "视频预览加载失败";
			PreviewSummaryText.Text = "无法读取预览帧";
			VideoInfoText.Text = "无法读取视频信息。";
		}
	}

	private VideoPreviewResult BuildPreview(string videoPath)
	{
		using VideoCapture capture = new(videoPath);
		if (!capture.IsOpened())
		{
			throw new InvalidOperationException("无法打开视频文件。");
		}

		using Mat frame = new();
		if (!capture.Read(frame) || frame.Empty())
		{
			throw new InvalidOperationException("无法读取视频首帧。");
		}

		double fps = capture.Get(VideoCaptureProperties.Fps);
		double frames = capture.Get(VideoCaptureProperties.FrameCount);
		double seconds = fps > 0 ? frames / fps : 0;

		using Mat preview = ResizePreview(frame, 980, 620);
		List<FaceRect> faces = _detector?.Detect(preview) ?? [];
		foreach (FaceRect face in faces)
		{
			Cv2.Rectangle(preview, new OpenCvSharp.Rect(face.X, face.Y, face.Width, face.Height), new Scalar(65, 133, 255), 3, LineTypes.AntiAlias);
		}

		BitmapSource bitmap = ToBitmapSource(preview);
		bitmap.Freeze();

		string summary = $"分辨率：{frame.Width} × {frame.Height}\n帧率：{(fps > 0 ? fps.ToString("0.##") : "未知")} fps\n时长：{TimeSpan.FromSeconds(seconds):hh\\:mm\\:ss}\n首帧检测到人脸：{faces.Count} 张";
		return new VideoPreviewResult(bitmap, frame.Width, frame.Height, summary);
	}

	private static Mat ResizePreview(Mat original, int maxWidth, int maxHeight)
	{
		double scale = Math.Min((double)maxWidth / original.Width, (double)maxHeight / original.Height);
		if (scale >= 1)
		{
			return original.Clone();
		}

		Mat resized = new();
		Cv2.Resize(original, resized, new OpenCvSharp.Size((int)(original.Width * scale), (int)(original.Height * scale)), 0, 0, InterpolationFlags.Area);
		return resized;
	}

	private static BitmapSource ToBitmapSource(Mat image)
	{
		Cv2.ImEncode(".png", image, out byte[] buffer);
		using MemoryStream stream = new(buffer);
		BitmapImage bitmap = new();
		bitmap.BeginInit();
		bitmap.CacheOption = BitmapCacheOption.OnLoad;
		bitmap.StreamSource = stream;
		bitmap.EndInit();
		return bitmap;
	}

	private void UpdateOutputDirectoryState()
	{
		string? outputDirectory = ResolveOutputDirectory();
		OpenOutputDirectoryBtn.IsEnabled = !string.IsNullOrWhiteSpace(outputDirectory);
		OpenOutputDirectoryBtn.ToolTip = string.IsNullOrWhiteSpace(outputDirectory) ? "请先设置输出路径。" : outputDirectory;
	}

	private string? ResolveOutputDirectory()
	{
		string outputPath = OutputPathTextBox.Text.Trim();
		if (!string.IsNullOrWhiteSpace(outputPath))
		{
			string? directory = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				return directory;
			}

			if (!Path.HasExtension(outputPath))
			{
				return outputPath;
			}
		}

		return string.IsNullOrWhiteSpace(VideoPathTextBox.Text) ? null : Path.GetDirectoryName(VideoPathTextBox.Text);
	}

	private static int GetFaceCountComboIndex(int maxFacesToProcess)
	{
		return maxFacesToProcess switch
		{
			2 => 1,
			3 => 2,
			0 => 3,
			_ => 0
		};
	}

	private static int GetSelectedFaceCountLimit(ComboBox comboBox)
	{
		return comboBox.SelectedIndex switch
		{
			1 => 2,
			2 => 3,
			3 => 0,
			_ => 1
		};
	}

	private ConfigManager EnsureConfigManager()
	{
		return _configManager ?? throw new InvalidOperationException("VideoTabView 尚未初始化。");
	}

	private VideoProcessor EnsureProcessor()
	{
		return _processor ?? throw new InvalidOperationException("VideoProcessor 尚未初始化。");
	}

	private sealed record VideoPreviewResult(BitmapSource Bitmap, int Width, int Height, string Summary);
}
