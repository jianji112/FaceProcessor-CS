#define TRACE
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using FaceProcessor.Models;
using FaceProcessor.Services;
using Microsoft.Win32;
using OpenCvSharp;

namespace FaceProcessor.Views;

public class ImageTabView : UserControl, IComponentConnector
{
	private readonly ConfigManager _configManager;

	private readonly FaceDetector? _detector;

	private readonly ImageProcessor _processor;

	private readonly List<string> _selectedFiles = new List<string>();

	internal ListBox ImageListBox;

	internal TextBlock SelectedCountText;

	internal ProgressBar ProgressBar;

	internal TextBlock StatusText;

	internal TextBlock OutputPreviewText;

	internal Button ProcessBtn;

	internal Button OpenOutputBtn;

	internal ComboBox ModeCombo;

	internal ComboBox ResolutionCombo;

	internal ComboBox FormatCombo;

	internal ComboBox ImageFaceCountCombo;

	internal Slider QualitySlider;

	internal TextBlock QualityText;

	internal RadioButton CustomPathRadio;

	internal RadioButton SubfolderRadio;

	internal TextBox OutputPathTextBox;

	internal TextBox SubfolderNameTextBox;

	private bool _contentLoaded;

	public ImageTabView(ConfigManager configManager, FaceDetector? detector)
	{
		InitializeComponent();
		_configManager = configManager;
		_detector = detector;
		_processor = new ImageProcessor(detector);
		LoadConfig();
		BindEvents();
		UpdateSelectedCount();
		UpdateOutputDirectoryState();
	}

	private void LoadConfig()
	{
		AppConfig config = _configManager.Config;
		ModeCombo.SelectedIndex = (int)config.ImageMode;
		ComboBox resolutionCombo = ResolutionCombo;
		resolutionCombo.SelectedIndex = config.MaxResolution switch
		{
			1024 => 1, 
			2048 => 2, 
			_ => 0, 
		};
		resolutionCombo = FormatCombo;
		string exportFormat = config.ExportFormat;
		int selectedIndex = ((exportFormat == "JPG") ? 1 : ((exportFormat == "WEBP") ? 2 : 0));
		resolutionCombo.SelectedIndex = selectedIndex;
		ImageFaceCountCombo.SelectedIndex = GetFaceCountComboIndex(config.ImageFaceCountLimit);
		QualitySlider.Value = config.ExportQuality;
		QualityText.Text = $"{config.ExportQuality}%";
		OutputPathTextBox.Text = config.OutputPath;
		CustomPathRadio.IsChecked = !config.UseSubfolder;
		SubfolderRadio.IsChecked = config.UseSubfolder;
		SubfolderNameTextBox.Text = (string.IsNullOrWhiteSpace(config.SubfolderName) ? "processed" : config.SubfolderName);
	}

	private void BindEvents()
	{
		QualitySlider.ValueChanged += delegate(object _, RoutedPropertyChangedEventArgs<double> e)
		{
			QualityText.Text = $"{(int)e.NewValue}%";
			_configManager.Update(delegate(AppConfig config)
			{
				config.ExportQuality = (int)e.NewValue;
			});
		};
		ModeCombo.SelectionChanged += delegate
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.ImageMode = (ProcessMode)ModeCombo.SelectedIndex;
			});
		};
		ResolutionCombo.SelectionChanged += delegate
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.MaxResolution = ResolutionCombo.SelectedIndex switch
				{
					1 => 1024, 
					2 => 2048, 
					_ => 0, 
				};
			});
		};
		FormatCombo.SelectionChanged += delegate
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.ExportFormat = FormatCombo.SelectedIndex switch
				{
					1 => "JPG", 
					2 => "WEBP", 
					_ => "PNG", 
				};
			});
		};
		ImageFaceCountCombo.SelectionChanged += delegate
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.ImageFaceCountLimit = GetSelectedFaceCountLimit(ImageFaceCountCombo);
			});
		};
		OutputPathTextBox.TextChanged += delegate
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.OutputPath = OutputPathTextBox.Text;
			});
			UpdateOutputDirectoryState();
		};
		CustomPathRadio.Checked += delegate
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.UseSubfolder = false;
			});
			UpdateOutputDirectoryState();
		};
		SubfolderRadio.Checked += delegate
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.UseSubfolder = true;
			});
			UpdateOutputDirectoryState();
		};
		SubfolderNameTextBox.TextChanged += delegate
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.SubfolderName = SubfolderNameTextBox.Text;
			});
			UpdateOutputDirectoryState();
		};
	}

	private void SelectImages_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Multiselect = true,
			Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
		};
		if (dialog.ShowDialog() == true)
		{
			string[] fileNames = dialog.FileNames;
			foreach (string file in fileNames)
			{
				AddSelectedFile(file);
			}
		}
	}

	private void SelectFolder_Click(object sender, RoutedEventArgs e)
	{
		OpenFolderDialog dialog = new OpenFolderDialog();
		if (dialog.ShowDialog() != true)
		{
			return;
		}
		string[] array = new string[5] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" };
		foreach (string pattern in array)
		{
			string[] files = Directory.GetFiles(dialog.FolderName, pattern);
			foreach (string file in files)
			{
				AddSelectedFile(file);
			}
		}
	}

	private void AddSelectedFile(string path)
	{
		if (!_selectedFiles.Contains(path))
		{
			_selectedFiles.Add(path);
			ImageListBox.Items.Add(Path.GetFileName(path));
			UpdateSelectedCount();
			UpdateOutputDirectoryState();
		}
	}

	private void ClearFiles_Click(object sender, RoutedEventArgs e)
	{
		_selectedFiles.Clear();
		ImageListBox.Items.Clear();
		UpdateSelectedCount();
		UpdateOutputDirectoryState();
	}

	private void BrowseOutput_Click(object sender, RoutedEventArgs e)
	{
		OpenFolderDialog dialog = new OpenFolderDialog();
		if (dialog.ShowDialog() == true)
		{
			OutputPathTextBox.Text = dialog.FolderName;
		}
	}

	private void OpenOutput_Click(object sender, RoutedEventArgs e)
	{
		string targetDirectory = ResolveOutputPreviewDirectory();
		if (string.IsNullOrWhiteSpace(targetDirectory))
		{
			MessageBox.Show("请先选择输出目录，或先添加待处理图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Asterisk);
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
			MessageBox.Show("打开输出目录失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private async void Process_Click(object sender, RoutedEventArgs e)
	{
		if (_selectedFiles.Count == 0)
		{
			MessageBox.Show("请先添加至少一张图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		ProcessBtn.IsEnabled = false;
		ProgressBar.Value = 0.0;
		StatusText.Text = "正在准备任务...";
		List<string> selectedFiles = _selectedFiles.ToList();
		int total = selectedFiles.Count;
		ProcessMode mode = (ProcessMode)ModeCombo.SelectedIndex;
		int maxResolution = _configManager.Config.MaxResolution;
		int maxFileSizeMb = _configManager.Config.MaxFileSizeMB;
		string format = _configManager.Config.ExportFormat;
		int quality = (int)QualitySlider.Value;
		bool useSubfolder = SubfolderRadio.IsChecked == true;
		string subfolderName = GetEffectiveSubfolderName();
		string customOutputRoot = OutputPathTextBox.Text.Trim();
		int maxFacesToProcess = GetSelectedFaceCountLimit(ImageFaceCountCombo);
		int success = 0;
		int failed = 0;
		string firstError = null;
		Trace.WriteLine($"[ImageTab] Start batch: files={total}, mode={mode}, format={format}, useSubfolder={useSubfolder}");
		await Task.Run(delegate
		{
			int i;
			for (i = 0; i < total; i++)
			{
				string text = selectedFiles[i];
				try
				{
					using Mat mat = Cv2.ImRead(text);
					if (mat.Empty())
					{
						throw new InvalidOperationException("OpenCV 无法读取该图片。");
					}
					using Mat image = ((maxResolution > 0) ? _processor.Resize(mat, maxResolution) : mat.Clone());
					List<FaceRect> list = _detector?.Detect(image) ?? new List<FaceRect>();
					List<FaceRect> list2 = ImageProcessor.SelectFacesToProcess(list, maxFacesToProcess);
					Trace.WriteLine($"[ImageTab] {Path.GetFileName(text)} faces={list.Count}, selected={list2.Count}");
					string outputDir = ResolveAndEnsureOutputDirectory(text, useSubfolder, subfolderName, customOutputRoot);
					if (mode == ProcessMode.Split)
					{
						(string BackgroundPath, string FaceOnlyPath) tuple = BuildSeparatedOutputPaths(text, outputDir, format);
						string item = tuple.BackgroundPath;
						string item2 = tuple.FaceOnlyPath;
						ProcessOptions options = new ProcessOptions
						{
							MaxFacesToProcess = maxFacesToProcess
						};
						var (mat2, mat3) = _processor.CreateFaceSeparationOutputs(image, list2, options);
						try
						{
							if (!_processor.Save(mat2, item, format, quality, maxFileSizeMb))
							{
								throw new InvalidOperationException("保存无人脸图片失败：" + item);
							}
							if (!_processor.Save(mat3, item2, format, quality, maxFileSizeMb))
							{
								throw new InvalidOperationException("保存纯人脸图片失败：" + item2);
							}
						}
						finally
						{
							mat2.Dispose();
							mat3.Dispose();
						}
						Trace.WriteLine("[ImageTab] Saved separation: " + item + ", " + item2);
					}
					else
					{
						string text2 = BuildOutputPath(text, outputDir, format);
						using Mat image2 = _processor.Process(image, list, mode, new ProcessOptions
						{
							MaxFacesToProcess = maxFacesToProcess,
							MaxResolution = maxResolution,
							MaxFileSizeMB = maxFileSizeMb,
							Format = format,
							Quality = quality
						});
						if (!_processor.Save(image2, text2, format, quality, maxFileSizeMb))
						{
							throw new InvalidOperationException("保存输出图片失败：" + text2);
						}
						Trace.WriteLine("[ImageTab] Saved: " + text2);
					}
					success++;
				}
				catch (Exception ex)
				{
					if (firstError == null)
					{
						firstError = ex.Message;
					}
					Trace.WriteLine($"[ImageTab] Failed to process {text}: {ex}");
					failed++;
				}
				base.Dispatcher.Invoke(delegate
				{
					ProgressBar.Value = (double)(i + 1) * 100.0 / (double)total;
					StatusText.Text = $"正在处理... {i + 1}/{total}";
				});
			}
		});
		StatusText.Text = ((failed == 0) ? $"处理完成，成功 {success}/{total}。" : $"处理完成，成功 {success}/{total}，失败 {failed}。");
		if (success == 0 && !string.IsNullOrWhiteSpace(firstError))
		{
			MessageBox.Show("图片处理失败：" + firstError + "\n\n详细信息请查看日志页或 " + LogCollector.LogFilePath, "处理失败", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		ProcessBtn.IsEnabled = true;
	}

	private static string ResolveAndEnsureOutputDirectory(string inputPath, bool useSubfolder, string subfolderName, string customOutputRoot)
	{
		string preferredDirectory = (useSubfolder ? Path.Combine(Path.GetDirectoryName(inputPath), subfolderName) : (string.IsNullOrWhiteSpace(customOutputRoot) ? Path.GetDirectoryName(inputPath) : customOutputRoot));
		try
		{
			Directory.CreateDirectory(preferredDirectory);
			return preferredDirectory;
		}
		catch
		{
			string text = Path.Combine(Path.GetDirectoryName(inputPath), "processed");
			Directory.CreateDirectory(text);
			return text;
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
		string numberedCandidate;
		while (true)
		{
			numberedCandidate = Path.Combine(outputDir, $"{fileNameWithoutExtension}_{timestamp}_{counter}{extension}");
			if (!File.Exists(numberedCandidate))
			{
				break;
			}
			counter++;
		}
		return numberedCandidate;
	}

	private static (string BackgroundPath, string FaceOnlyPath) BuildSeparatedOutputPaths(string inputPath, string outputDir, string format)
	{
		string extension = GetExtension(format);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputPath);
		string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
		int counter = 0;
		string backgroundPath;
		string faceOnlyPath;
		while (true)
		{
			string duplicateSuffix = ((counter == 0) ? string.Empty : $"_{counter}");
			backgroundPath = Path.Combine(outputDir, $"{fileNameWithoutExtension}_{timestamp}{duplicateSuffix}_no_face{extension}");
			faceOnlyPath = Path.Combine(outputDir, $"{fileNameWithoutExtension}_{timestamp}{duplicateSuffix}_face_only{extension}");
			if (!File.Exists(backgroundPath) && !File.Exists(faceOnlyPath))
			{
				break;
			}
			counter++;
		}
		return (BackgroundPath: backgroundPath, FaceOnlyPath: faceOnlyPath);
	}

	private static string GetExtension(string format)
	{
		switch (format.ToLowerInvariant())
		{
		case "jpg":
		case "jpeg":
			return ".jpg";
		case "webp":
			return ".webp";
		default:
			return ".png";
		}
	}

	private void UpdateSelectedCount()
	{
		SelectedCountText.Text = $"{_selectedFiles.Count} 张";
	}

	private void UpdateOutputDirectoryState()
	{
		string previewDirectory = ResolveOutputPreviewDirectory();
		OutputPreviewText.Text = BuildOutputPreviewLabel(previewDirectory);
		OpenOutputBtn.IsEnabled = !string.IsNullOrWhiteSpace(previewDirectory);
		OpenOutputBtn.ToolTip = (string.IsNullOrWhiteSpace(previewDirectory) ? "请先选择输出目录，或先添加待处理图片。" : previewDirectory);
	}

	private string BuildOutputPreviewLabel(string? previewDirectory)
	{
		if (!string.IsNullOrWhiteSpace(previewDirectory))
		{
			List<string> sourceDirectories = (from path in _selectedFiles.Select(Path.GetDirectoryName)
				where !string.IsNullOrWhiteSpace(path)
				select path).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
			if (SubfolderRadio.IsChecked == true && sourceDirectories.Count > 1)
			{
				return $"{previewDirectory} 等 {sourceDirectories.Count} 个源目录";
			}
			return previewDirectory;
		}
		if (SubfolderRadio.IsChecked != true)
		{
			return "未设置自定义输出目录时，将跟随源图片目录输出。";
		}
		return "选择图片后会按源目录自动生成子文件夹。";
	}

	private string? ResolveOutputPreviewDirectory()
	{
		if (SubfolderRadio.IsChecked == true)
		{
			string firstSourceDirectory = _selectedFiles.Select(Path.GetDirectoryName).FirstOrDefault((string path) => !string.IsNullOrWhiteSpace(path));
			if (string.IsNullOrWhiteSpace(firstSourceDirectory))
			{
				return null;
			}
			return Path.Combine(firstSourceDirectory, GetEffectiveSubfolderName());
		}
		string customOutputRoot = OutputPathTextBox.Text.Trim();
		if (!string.IsNullOrWhiteSpace(customOutputRoot))
		{
			return customOutputRoot;
		}
		return _selectedFiles.Select(Path.GetDirectoryName).FirstOrDefault((string path) => !string.IsNullOrWhiteSpace(path));
	}

	private string GetEffectiveSubfolderName()
	{
		if (!string.IsNullOrWhiteSpace(SubfolderNameTextBox.Text))
		{
			return SubfolderNameTextBox.Text.Trim();
		}
		return "processed";
	}

	private static int GetFaceCountComboIndex(int maxFacesToProcess)
	{
		if (maxFacesToProcess > 0)
		{
			return maxFacesToProcess switch
			{
				2 => 1, 
				3 => 2, 
				_ => 0, 
			};
		}
		return 3;
	}

	private static int GetSelectedFaceCountLimit(ComboBox comboBox)
	{
		return comboBox.SelectedIndex switch
		{
			1 => 2, 
			2 => 3, 
			3 => 0, 
			_ => 1, 
		};
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "9.0.3.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/FaceProcessor;component/views/imagetabview.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocater);
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
			((Button)target).Click += SelectImages_Click;
			break;
		case 2:
			((Button)target).Click += SelectFolder_Click;
			break;
		case 3:
			((Button)target).Click += ClearFiles_Click;
			break;
		case 4:
			ImageListBox = (ListBox)target;
			break;
		case 5:
			SelectedCountText = (TextBlock)target;
			break;
		case 6:
			ProgressBar = (ProgressBar)target;
			break;
		case 7:
			StatusText = (TextBlock)target;
			break;
		case 8:
			OutputPreviewText = (TextBlock)target;
			break;
		case 9:
			ProcessBtn = (Button)target;
			ProcessBtn.Click += Process_Click;
			break;
		case 10:
			OpenOutputBtn = (Button)target;
			OpenOutputBtn.Click += OpenOutput_Click;
			break;
		case 11:
			ModeCombo = (ComboBox)target;
			break;
		case 12:
			ResolutionCombo = (ComboBox)target;
			break;
		case 13:
			FormatCombo = (ComboBox)target;
			break;
		case 14:
			ImageFaceCountCombo = (ComboBox)target;
			break;
		case 15:
			QualitySlider = (Slider)target;
			break;
		case 16:
			QualityText = (TextBlock)target;
			break;
		case 17:
			CustomPathRadio = (RadioButton)target;
			break;
		case 18:
			SubfolderRadio = (RadioButton)target;
			break;
		case 19:
			OutputPathTextBox = (TextBox)target;
			break;
		case 20:
			((Button)target).Click += BrowseOutput_Click;
			break;
		case 21:
			SubfolderNameTextBox = (TextBox)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
