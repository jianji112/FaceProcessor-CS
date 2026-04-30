using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FaceProcessor.Models;
using FaceProcessor.Services;
using Microsoft.Win32;
using OpenCvSharp;

namespace FaceProcessor.Views;

public partial class ImageTabView : UserControl
{
	private readonly List<FileItem> _selectedFiles = new();

	private ConfigManager? _configManager;

	private FaceDetector? _detector;

	private ImageProcessor? _processor;

	private bool _eventsBound;

	private bool _isInitialized;

	public ImageTabView()
	{
		InitializeComponent();
	}

	public void Initialize(ConfigManager configManager, FaceDetector? detector)
	{
		if (_isInitialized)
		{
			return;
		}

		_configManager = configManager;
		_detector = detector;
		_processor = new ImageProcessor(detector);
		LoadConfig();
		BindEvents();
		UpdateSelectedCount();
		UpdateOutputDirectoryState();
		_isInitialized = true;
	}

	public void UpdateRuntimeSummary(string detectorSummary)
	{
		DetectorStatusText.Text = detectorSummary;
	}

	private void LoadConfig()
	{
		AppConfig config = EnsureConfigManager().Config;
		ModeCombo.SelectedIndex = Math.Clamp((int)config.ImageMode - 1, 0, ModeCombo.Items.Count - 1);
		ResolutionCombo.SelectedIndex = config.MaxResolution switch
		{
			1024 => 1,
			2048 => 2,
			_ => 0
		};
		FormatCombo.SelectedIndex = config.ExportFormat switch
		{
			"JPG" => 1,
			"WEBP" => 2,
			_ => 0
		};
		ImageFaceCountCombo.SelectedIndex = GetFaceCountComboIndex(config.ImageFaceCountLimit);
		QualitySlider.Value = config.ExportQuality;
		QualityText.Text = $"{config.ExportQuality}%";
		OutputPathTextBox.Text = config.OutputPath;
		CustomPathRadio.IsChecked = !config.UseSubfolder;
		SubfolderRadio.IsChecked = config.UseSubfolder;
		SubfolderNameTextBox.Text = string.IsNullOrWhiteSpace(config.SubfolderName) ? "processed" : config.SubfolderName;
	}

	private void BindEvents()
	{
		if (_eventsBound)
		{
			return;
		}

		_eventsBound = true;

		QualitySlider.ValueChanged += (_, e) =>
		{
			if (_configManager == null)
			{
				return;
			}

			QualityText.Text = $"{(int)e.NewValue}%";
			_configManager.Update(config => config.ExportQuality = (int)e.NewValue);
		};

		ModeCombo.SelectionChanged += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			_configManager.Update(config => config.ImageMode = GetSelectedMode());
		};

		ResolutionCombo.SelectionChanged += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			_configManager.Update(config => config.MaxResolution = ResolutionCombo.SelectedIndex switch
			{
				1 => 1024,
				2 => 2048,
				_ => 0
			});
		};

		FormatCombo.SelectionChanged += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			_configManager.Update(config => config.ExportFormat = FormatCombo.SelectedIndex switch
			{
				1 => "JPG",
				2 => "WEBP",
				_ => "PNG"
			});
		};

		ImageFaceCountCombo.SelectionChanged += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			_configManager.Update(config => config.ImageFaceCountLimit = GetSelectedFaceCountLimit(ImageFaceCountCombo));
		};

		OutputPathTextBox.TextChanged += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			_configManager.Update(config => config.OutputPath = OutputPathTextBox.Text);
			UpdateOutputDirectoryState();
		};

		CustomPathRadio.Checked += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			_configManager.Update(config => config.UseSubfolder = false);
			UpdateOutputDirectoryState();
		};

		SubfolderRadio.Checked += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			_configManager.Update(config => config.UseSubfolder = true);
			UpdateOutputDirectoryState();
		};

		SubfolderNameTextBox.TextChanged += (_, _) =>
		{
			if (_configManager == null)
			{
				return;
			}

			_configManager.Update(config => config.SubfolderName = SubfolderNameTextBox.Text);
			UpdateOutputDirectoryState();
		};
	}

	private void SelectImages_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new()
		{
			Multiselect = true,
			Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
		};
		if (dialog.ShowDialog() == true)
		{
			foreach (string file in dialog.FileNames)
			{
				AddSelectedFile(file);
			}
		}
	}

	private void SelectFolder_Click(object sender, RoutedEventArgs e)
	{
		OpenFolderDialog dialog = new();
		if (dialog.ShowDialog() != true)
		{
			return;
		}

		string[] patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"];
		foreach (string pattern in patterns)
		{
			foreach (string file in Directory.GetFiles(dialog.FolderName, pattern))
			{
				AddSelectedFile(file);
			}
		}
	}

	private void AddSelectedFile(string path)
	{
		if (_selectedFiles.Any(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}

		FileItem item = new(path);
		_selectedFiles.Add(item);
		ImageListBox.Items.Add(item);
		ImageListBox.SelectedItem = item;
		UpdateSelectedCount();
		UpdateOutputDirectoryState();
	}

	private void ClearFiles_Click(object sender, RoutedEventArgs e)
	{
		_selectedFiles.Clear();
		ImageListBox.Items.Clear();
		PreviewImage.Source = null;
		PreviewPlaceholderText.Visibility = Visibility.Visible;
		PreviewModeText.Text = "预览模式：等待选择图片";
		UpdateSelectedCount();
		UpdateOutputDirectoryState();
	}

	private void BrowseOutput_Click(object sender, RoutedEventArgs e)
	{
		OpenFolderDialog dialog = new();
		if (dialog.ShowDialog() == true)
		{
			OutputPathTextBox.Text = dialog.FolderName;
		}
	}

	private void OpenOutput_Click(object sender, RoutedEventArgs e)
	{
		string? targetDirectory = ResolveOutputPreviewDirectory();
		if (string.IsNullOrWhiteSpace(targetDirectory))
		{
			MessageBox.Show("请先设置输出目录，或者先添加待处理图片。", "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}

		try
		{
			Directory.CreateDirectory(targetDirectory);
			Process.Start(new ProcessStartInfo
			{
				FileName = targetDirectory,
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
		if (_selectedFiles.Count == 0)
		{
			MessageBox.Show("请先添加至少一张图片。", "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		ImageProcessor processor = EnsureProcessor();
		ConfigManager configManager = EnsureConfigManager();
		ProcessBtn.IsEnabled = false;
		ProgressBar.Value = 0;
		ProgressText.Text = "0%";
		StatusText.Text = "正在准备任务...";
		List<FileItem> files = _selectedFiles.ToList();
		int total = files.Count;
		ProcessMode mode = GetSelectedMode();
		int maxResolution = configManager.Config.MaxResolution;
		int maxFileSizeMb = configManager.Config.MaxFileSizeMB;
		string format = configManager.Config.ExportFormat;
		int quality = (int)QualitySlider.Value;
		bool useSubfolder = SubfolderRadio.IsChecked == true;
		string subfolderName = GetEffectiveSubfolderName();
		string customOutputRoot = OutputPathTextBox.Text.Trim();
		int maxFacesToProcess = GetSelectedFaceCountLimit(ImageFaceCountCombo);
		int success = 0;
		int failed = 0;
		string? firstError = null;
		Trace.WriteLine($"[ImageTab] Start batch: files={total}, mode={mode}, format={format}, useSubfolder={useSubfolder}");

		await Task.Run(() =>
		{
			for (int index = 0; index < total; index++)
			{
				string inputPath = files[index].Path;
				try
				{
					using Mat original = Cv2.ImRead(inputPath);
					if (original.Empty())
					{
						throw new InvalidOperationException("OpenCV 无法读取这张图片。");
					}

					using Mat workingImage = maxResolution > 0 ? processor.Resize(original, maxResolution) : original.Clone();
					List<FaceRect> faces = _detector?.Detect(workingImage) ?? new List<FaceRect>();
					List<FaceRect> selectedFaces = ImageProcessor.SelectFacesToProcess(faces, maxFacesToProcess);
					string outputDir = ResolveAndEnsureOutputDirectory(inputPath, useSubfolder, subfolderName, customOutputRoot);

					if (mode == ProcessMode.Split)
					{
						(string backgroundPath, string faceOnlyPath) = BuildSeparatedOutputPaths(inputPath, outputDir, format);
						ProcessOptions options = new()
						{
							MaxFacesToProcess = maxFacesToProcess
						};
						var separated = processor.CreateFaceSeparationOutputs(workingImage, selectedFaces, options);
						try
						{
							if (!processor.Save(separated.BackgroundOnly, backgroundPath, format, quality, maxFileSizeMb))
							{
								throw new InvalidOperationException("保存无人脸图片失败：" + backgroundPath);
							}

							if (!processor.Save(separated.FaceOnly, faceOnlyPath, format, quality, maxFileSizeMb))
							{
								throw new InvalidOperationException("保存纯人脸图片失败：" + faceOnlyPath);
							}
						}
						finally
						{
							separated.BackgroundOnly.Dispose();
							separated.FaceOnly.Dispose();
						}
					}
					else
					{
						string outputPath = BuildOutputPath(inputPath, outputDir, format);
						using Mat processed = processor.Process(workingImage, faces, mode, new ProcessOptions
						{
							MaxFacesToProcess = maxFacesToProcess,
							MaxResolution = maxResolution,
							MaxFileSizeMB = maxFileSizeMb,
							Format = format,
							Quality = quality
						});

						if (!processor.Save(processed, outputPath, format, quality, maxFileSizeMb))
						{
							throw new InvalidOperationException("保存输出图片失败：" + outputPath);
						}
					}

					success++;
				}
				catch (Exception ex)
				{
					firstError ??= ex.Message;
					Trace.WriteLine($"[ImageTab] Failed to process {inputPath}: {ex}");
					failed++;
				}

				Dispatcher.Invoke(() =>
				{
					double percent = (index + 1) * 100d / total;
					ProgressBar.Value = percent;
					ProgressText.Text = $"{percent:0}%";
					StatusText.Text = $"正在处理... {index + 1}/{total}";
				});
			}
		});

		StatusText.Text = failed == 0
			? $"处理完成，成功 {success}/{total}"
			: $"处理完成，成功 {success}/{total}，失败 {failed}";

		if (success == 0 && !string.IsNullOrWhiteSpace(firstError))
		{
			MessageBox.Show("图片处理失败：\n" + firstError + "\n\n详细日志见：\n" + LogCollector.LogFilePath, "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		ProcessBtn.IsEnabled = true;
	}

	private async void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (ImageListBox.SelectedItem is not FileItem item)
		{
			return;
		}

		PreviewPlaceholderText.Visibility = Visibility.Visible;
		PreviewPlaceholderText.Text = "正在生成预览...";
		PreviewModeText.Text = "预览模式：生成中";

		try
		{
			PreviewResult result = await Task.Run(() => BuildPreview(item.Path));
			PreviewImage.Source = result.Bitmap;
			PreviewPlaceholderText.Visibility = Visibility.Collapsed;
			PreviewModeText.Text = result.FaceCount > 0
				? $"预览模式：人脸检测 · 已识别 {result.FaceCount} 张脸"
				: "预览模式：人脸检测 · 未识别到人脸";
		}
		catch (Exception ex)
		{
			Trace.WriteLine($"[ImageTab] Preview failed for {item.Path}: {ex}");
			PreviewImage.Source = null;
			PreviewPlaceholderText.Visibility = Visibility.Visible;
			PreviewPlaceholderText.Text = "预览加载失败";
			PreviewModeText.Text = "预览模式：加载失败";
		}
	}

	private PreviewResult BuildPreview(string path)
	{
		using Mat original = Cv2.ImRead(path);
		if (original.Empty())
		{
			throw new InvalidOperationException("无法读取预览图片。");
		}

		using Mat preview = ResizePreview(original, 980, 620);
		List<FaceRect> faces = _detector?.Detect(preview) ?? new List<FaceRect>();
		foreach (FaceRect face in faces)
		{
			OpenCvSharp.Rect rect = new(face.X, face.Y, face.Width, face.Height);
			Cv2.Rectangle(preview, rect, new Scalar(65, 133, 255), 3, LineTypes.AntiAlias);
		}

		BitmapSource bitmap = ToBitmapSource(preview);
		bitmap.Freeze();
		return new PreviewResult(bitmap, faces.Count);
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

	private void UpdateSelectedCount()
	{
		SelectedCountText.Text = $"已选择 {_selectedFiles.Count} 张";
	}

	private void UpdateOutputDirectoryState()
	{
		string? previewDirectory = ResolveOutputPreviewDirectory();
		OutputPreviewText.Text = BuildOutputPreviewLabel(previewDirectory);
		OpenOutputBtn.IsEnabled = !string.IsNullOrWhiteSpace(previewDirectory);
		OpenOutputBtn.ToolTip = string.IsNullOrWhiteSpace(previewDirectory) ? "请先设置输出目录，或者先添加图片。" : previewDirectory;
	}

	private string BuildOutputPreviewLabel(string? previewDirectory)
	{
		if (!string.IsNullOrWhiteSpace(previewDirectory))
		{
			List<string?> sourceDirectories = _selectedFiles.Select(item => Path.GetDirectoryName(item.Path))
				.Where(path => !string.IsNullOrWhiteSpace(path))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			if (SubfolderRadio.IsChecked == true && sourceDirectories.Count > 1)
			{
				return $"{previewDirectory} 等 {sourceDirectories.Count} 个源目录";
			}

			return previewDirectory;
		}

		return SubfolderRadio.IsChecked == true
			? "选择图片后会按源目录自动生成子文件夹。"
			: "未设置输出目录时，会跟随源图片所在目录输出。";
	}

	private string? ResolveOutputPreviewDirectory()
	{
		if (SubfolderRadio.IsChecked == true)
		{
			string? sourceDir = _selectedFiles.Select(item => Path.GetDirectoryName(item.Path))
				.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
			return string.IsNullOrWhiteSpace(sourceDir) ? null : Path.Combine(sourceDir, GetEffectiveSubfolderName());
		}

		string outputRoot = OutputPathTextBox.Text.Trim();
		if (!string.IsNullOrWhiteSpace(outputRoot))
		{
			return outputRoot;
		}

		return _selectedFiles.Select(item => Path.GetDirectoryName(item.Path))
			.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
	}

	private string GetEffectiveSubfolderName()
	{
		return string.IsNullOrWhiteSpace(SubfolderNameTextBox.Text) ? "processed" : SubfolderNameTextBox.Text.Trim();
	}

	private ProcessMode GetSelectedMode()
	{
		return ModeCombo.SelectedIndex switch
		{
			0 => ProcessMode.Mosaic,
			1 => ProcessMode.Blur,
			2 => ProcessMode.BlackMesh,
			3 => ProcessMode.Grid,
			4 => ProcessMode.Split,
			_ => ProcessMode.Mosaic
		};
	}

	private static string ResolveAndEnsureOutputDirectory(string inputPath, bool useSubfolder, string subfolderName, string customOutputRoot)
	{
		string preferredDirectory = useSubfolder
			? Path.Combine(Path.GetDirectoryName(inputPath) ?? string.Empty, subfolderName)
			: string.IsNullOrWhiteSpace(customOutputRoot) ? Path.GetDirectoryName(inputPath) ?? string.Empty : customOutputRoot;
		try
		{
			Directory.CreateDirectory(preferredDirectory);
			return preferredDirectory;
		}
		catch
		{
			string fallback = Path.Combine(Path.GetDirectoryName(inputPath) ?? string.Empty, "processed");
			Directory.CreateDirectory(fallback);
			return fallback;
		}
	}

	private static string BuildOutputPath(string inputPath, string outputDir, string format)
	{
		string extension = GetExtension(format);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputPath);
		string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
		string candidate = Path.Combine(outputDir, fileNameWithoutExtension + "_" + timestamp + extension);
		if (!File.Exists(candidate))
		{
			return candidate;
		}

		int counter = 1;
		while (true)
		{
			string numberedCandidate = Path.Combine(outputDir, $"{fileNameWithoutExtension}_{timestamp}_{counter}{extension}");
			if (!File.Exists(numberedCandidate))
			{
				return numberedCandidate;
			}

			counter++;
		}
	}

	private static (string BackgroundPath, string FaceOnlyPath) BuildSeparatedOutputPaths(string inputPath, string outputDir, string format)
	{
		string extension = GetExtension(format);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputPath);
		string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
		int counter = 0;
		while (true)
		{
			string suffix = counter == 0 ? string.Empty : "_" + counter;
			string backgroundPath = Path.Combine(outputDir, $"{fileNameWithoutExtension}_{timestamp}{suffix}_no_face{extension}");
			string faceOnlyPath = Path.Combine(outputDir, $"{fileNameWithoutExtension}_{timestamp}{suffix}_face_only{extension}");
			if (!File.Exists(backgroundPath) && !File.Exists(faceOnlyPath))
			{
				return (backgroundPath, faceOnlyPath);
			}

			counter++;
		}
	}

	private static string GetExtension(string format)
	{
		return format.ToLowerInvariant() switch
		{
			"jpg" or "jpeg" => ".jpg",
			"webp" => ".webp",
			_ => ".png"
		};
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
		return _configManager ?? throw new InvalidOperationException("ImageTabView 尚未初始化。");
	}

	private ImageProcessor EnsureProcessor()
	{
		return _processor ?? throw new InvalidOperationException("ImageProcessor 尚未初始化。");
	}

	private sealed record FileItem(string Path)
	{
		public string Name => System.IO.Path.GetFileName(Path);
	}

	private sealed record PreviewResult(BitmapSource Bitmap, int FaceCount);
}
