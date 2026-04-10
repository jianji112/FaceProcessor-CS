#define TRACE
using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using FaceProcessor.Models;
using FaceProcessor.Services;
using Microsoft.Win32;

namespace FaceProcessor.Views;

public class VideoTabView : UserControl, IComponentConnector
{
	private readonly ConfigManager _configManager;

	private readonly VideoProcessor _processor;

	private CancellationTokenSource? _cts;

	internal TextBox VideoPathTextBox;

	internal TextBlock GpuStatusText;

	internal Slider FaceThresholdSlider;

	internal TextBlock FaceThresholdText;

	internal ComboBox ModeCombo;

	internal TextBox BlockSizeTextBox;

	internal TextBox BlurStrengthTextBox;

	internal ComboBox VideoFaceCountCombo;

	internal ComboBox OutputResolutionCombo;

	internal CheckBox KeepAudioCheckBox;

	internal TextBox OutputPathTextBox;

	internal Button OpenOutputDirectoryBtn;

	internal ProgressBar ProgressBar;

	internal TextBlock StatusText;

	internal TextBlock ProgressText;

	internal Button ProcessBtn;

	internal Button CancelBtn;

	private bool _contentLoaded;

	public VideoTabView(ConfigManager configManager, VideoProcessor processor)
	{
		InitializeComponent();
		_configManager = configManager;
		_processor = processor;
		LoadConfig();
		BindEvents();
		UpdateOutputDirectoryState();
	}

	private void LoadConfig()
	{
		AppConfig config = _configManager.Config;
		ModeCombo.SelectedIndex = ((config.VideoMode == ProcessMode.Blur) ? 1 : 0);
		BlockSizeTextBox.Text = config.VideoBlockSize.ToString();
		BlurStrengthTextBox.Text = config.VideoBlurStrength.ToString();
		FaceThresholdSlider.Value = config.VideoFaceThreshold;
		FaceThresholdText.Text = config.VideoFaceThreshold.ToString("0.00");
		KeepAudioCheckBox.IsChecked = config.VideoKeepAudio;
		VideoFaceCountCombo.SelectedIndex = GetFaceCountComboIndex(config.VideoFaceCountLimit);
		ComboBox outputResolutionCombo = OutputResolutionCombo;
		outputResolutionCombo.SelectedIndex = config.VideoOutputResolution switch
		{
			VideoOutputResolution.P1080 => 1, 
			VideoOutputResolution.P720 => 2, 
			_ => 0, 
		};
	}

	private void BindEvents()
	{
		ModeCombo.SelectionChanged += delegate
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.VideoMode = ((ModeCombo.SelectedIndex == 0) ? ProcessMode.Mosaic : ProcessMode.Blur);
			});
		};
		BlockSizeTextBox.TextChanged += delegate
		{
			if (int.TryParse(BlockSizeTextBox.Text, out var value))
			{
				_configManager.Update(delegate(AppConfig config)
				{
					config.VideoBlockSize = value;
				});
			}
		};
		BlurStrengthTextBox.TextChanged += delegate
		{
			if (int.TryParse(BlurStrengthTextBox.Text, out var value))
			{
				_configManager.Update(delegate(AppConfig config)
				{
					config.VideoBlurStrength = value;
				});
			}
		};
		FaceThresholdSlider.ValueChanged += delegate
		{
			FaceThresholdText.Text = FaceThresholdSlider.Value.ToString("0.00");
			_configManager.Update(delegate(AppConfig config)
			{
				config.VideoFaceThreshold = FaceThresholdSlider.Value;
			});
		};
		KeepAudioCheckBox.Checked += delegate
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.VideoKeepAudio = true;
			});
		};
		KeepAudioCheckBox.Unchecked += delegate
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.VideoKeepAudio = false;
			});
		};
		VideoFaceCountCombo.SelectionChanged += delegate
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.VideoFaceCountLimit = GetSelectedFaceCountLimit(VideoFaceCountCombo);
			});
		};
		OutputResolutionCombo.SelectionChanged += delegate
		{
			_configManager.Update(delegate(AppConfig config)
			{
				config.VideoOutputResolution = OutputResolutionCombo.SelectedIndex switch
				{
					1 => VideoOutputResolution.P1080, 
					2 => VideoOutputResolution.P720, 
					_ => VideoOutputResolution.Original, 
				};
			});
		};
		OutputPathTextBox.TextChanged += delegate
		{
			UpdateOutputDirectoryState();
		};
	}

	public void UpdateGpuStatus(bool available, string message)
	{
		base.Dispatcher.Invoke(delegate
		{
			GpuStatusText.Text = message;
			GpuStatusText.Foreground = (available ? ((Brush)FindResource("SuccessBrush")) : ((Brush)FindResource("WarningBrush")));
		});
	}

	private void SelectVideo_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = "Videos|*.mp4;*.avi;*.mov;*.mkv;*.flv;*.wmv"
		};
		if (dialog.ShowDialog() == true)
		{
			VideoPathTextBox.Text = dialog.FileName;
			string directory = Path.GetDirectoryName(dialog.FileName);
			string name = Path.GetFileNameWithoutExtension(dialog.FileName);
			OutputPathTextBox.Text = Path.Combine(directory, name + "_processed.mp4");
		}
	}

	private void BrowseOutput_Click(object sender, RoutedEventArgs e)
	{
		OpenFolderDialog dialog = new OpenFolderDialog();
		if (dialog.ShowDialog() == true)
		{
			string name = (string.IsNullOrWhiteSpace(VideoPathTextBox.Text) ? "output" : Path.GetFileNameWithoutExtension(VideoPathTextBox.Text));
			OutputPathTextBox.Text = Path.Combine(dialog.FolderName, name + "_processed.mp4");
		}
	}

	private void OpenOutputDirectory_Click(object sender, RoutedEventArgs e)
	{
		string outputDirectory = ResolveOutputDirectory();
		if (string.IsNullOrWhiteSpace(outputDirectory))
		{
			MessageBox.Show("请先设置输出文件路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Asterisk);
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
			MessageBox.Show("打开输出目录失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private async void Process_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(VideoPathTextBox.Text) || !File.Exists(VideoPathTextBox.Text))
		{
			MessageBox.Show("请先选择源视频。", "提示", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (string.IsNullOrWhiteSpace(OutputPathTextBox.Text))
		{
			MessageBox.Show("请先选择输出路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		ProcessBtn.Visibility = Visibility.Collapsed;
		CancelBtn.Visibility = Visibility.Visible;
		CancelBtn.IsEnabled = true;
		ProgressBar.Value = 0.0;
		ProgressText.Text = "0%";
		StatusText.Text = "正在初始化...";
		_processor.Reset();
		_cts = new CancellationTokenSource();
		Progress<double> progress = new Progress<double>(delegate(double value)
		{
			ProgressBar.Value = ((value < 0.0) ? 0.0 : (value * 100.0));
			ProgressText.Text = ((value < 0.0) ? "已停止" : $"{(int)(value * 100.0)}%");
			if (value < 0.0)
			{
				StatusText.Text = "已取消";
			}
			else if (value >= 0.95)
			{
				StatusText.Text = "正在合并音频...";
			}
			else if (value > 0.0)
			{
				StatusText.Text = "正在处理视频...";
			}
		});
		ProcessOptions processOptions = new ProcessOptions();
		processOptions.BlockSize = (int.TryParse(BlockSizeTextBox.Text, out var blockSize) ? blockSize : 0);
		processOptions.BlurStrength = (int.TryParse(BlurStrengthTextBox.Text, out var blurStrength) ? blurStrength : 99);
		processOptions.FaceThreshold = (float)FaceThresholdSlider.Value;
		processOptions.MaxFacesToProcess = GetSelectedFaceCountLimit(VideoFaceCountCombo);
		processOptions.KeepAudio = KeepAudioCheckBox.IsChecked == true;
		ProcessOptions processOptions2 = processOptions;
		processOptions2.OutputResolution = OutputResolutionCombo.SelectedIndex switch
		{
			1 => VideoOutputResolution.P1080, 
			2 => VideoOutputResolution.P720, 
			_ => VideoOutputResolution.Original, 
		};
		ProcessOptions options = processOptions;
		ProcessMode mode = ((ModeCombo.SelectedIndex == 0) ? ProcessMode.Mosaic : ProcessMode.Blur);
		bool success = false;
		bool cancelled = false;
		try
		{
			success = await _processor.ProcessVideoAsync(VideoPathTextBox.Text, OutputPathTextBox.Text, mode, options, progress, _cts.Token);
			cancelled = _cts.Token.IsCancellationRequested;
		}
		catch (OperationCanceledException)
		{
			cancelled = true;
		}
		catch (Exception ex2)
		{
			Trace.WriteLine($"[VideoTab] Unexpected processing error: {ex2}");
			StatusText.Text = "错误：" + ex2.Message;
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
		_processor.Cancel();
		StatusText.Text = "正在取消...";
		CancelBtn.IsEnabled = false;
	}

	private void UpdateOutputDirectoryState()
	{
		string outputDirectory = ResolveOutputDirectory();
		OpenOutputDirectoryBtn.IsEnabled = !string.IsNullOrWhiteSpace(outputDirectory);
		OpenOutputDirectoryBtn.ToolTip = (string.IsNullOrWhiteSpace(outputDirectory) ? "请先设置输出文件路径。" : outputDirectory);
	}

	private string? ResolveOutputDirectory()
	{
		string outputPath = OutputPathTextBox.Text.Trim();
		if (!string.IsNullOrWhiteSpace(outputPath))
		{
			string directory = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				return directory;
			}
			if (!Path.HasExtension(outputPath))
			{
				return outputPath;
			}
		}
		if (!string.IsNullOrWhiteSpace(VideoPathTextBox.Text))
		{
			return Path.GetDirectoryName(VideoPathTextBox.Text);
		}
		return null;
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
			Uri resourceLocater = new Uri("/FaceProcessor;component/views/videotabview.xaml", UriKind.Relative);
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
			VideoPathTextBox = (TextBox)target;
			break;
		case 2:
			((Button)target).Click += SelectVideo_Click;
			break;
		case 3:
			GpuStatusText = (TextBlock)target;
			break;
		case 4:
			FaceThresholdSlider = (Slider)target;
			break;
		case 5:
			FaceThresholdText = (TextBlock)target;
			break;
		case 6:
			ModeCombo = (ComboBox)target;
			break;
		case 7:
			BlockSizeTextBox = (TextBox)target;
			break;
		case 8:
			BlurStrengthTextBox = (TextBox)target;
			break;
		case 9:
			VideoFaceCountCombo = (ComboBox)target;
			break;
		case 10:
			OutputResolutionCombo = (ComboBox)target;
			break;
		case 11:
			KeepAudioCheckBox = (CheckBox)target;
			break;
		case 12:
			OutputPathTextBox = (TextBox)target;
			break;
		case 13:
			((Button)target).Click += BrowseOutput_Click;
			break;
		case 14:
			OpenOutputDirectoryBtn = (Button)target;
			OpenOutputDirectoryBtn.Click += OpenOutputDirectory_Click;
			break;
		case 15:
			ProgressBar = (ProgressBar)target;
			break;
		case 16:
			StatusText = (TextBlock)target;
			break;
		case 17:
			ProgressText = (TextBlock)target;
			break;
		case 18:
			ProcessBtn = (Button)target;
			ProcessBtn.Click += Process_Click;
			break;
		case 19:
			CancelBtn = (Button)target;
			CancelBtn.Click += Cancel_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
