using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using FaceProcessor.Services;

namespace FaceProcessor.Views;

public partial class LogTabView : UserControl
{
	public LogTabView()
	{
		InitializeComponent();
		LogListBox.ItemsSource = LogCollector.Instance.Entries;
		LogCollector.Instance.Entries.CollectionChanged += (_, _) => UpdateCount();
		UpdateCount();
	}

	private void UpdateCount()
	{
		Dispatcher.Invoke(() => CountText.Text = $"{LogCollector.Instance.Entries.Count} 条");
	}

	private void CopyAll_Click(object sender, RoutedEventArgs e)
	{
		if (LogCollector.Instance.Entries.Count == 0)
		{
			MessageBox.Show("当前没有可复制的日志。", "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}

		StringBuilder builder = new();
		foreach (LogEntry entry in LogCollector.Instance.Entries)
		{
			builder.AppendLine(entry.Formatted);
		}

		Clipboard.SetText(builder.ToString());
		MessageBox.Show($"已复制 {LogCollector.Instance.Entries.Count} 条日志。", "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Information);
	}

	private void CopySelected_Click(object sender, RoutedEventArgs e)
	{
		if (LogListBox.SelectedItems.Count == 0)
		{
			MessageBox.Show("请先选择要复制的日志行。", "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		StringBuilder builder = new();
		foreach (LogEntry entry in LogListBox.SelectedItems)
		{
			builder.AppendLine(entry.Formatted);
		}

		Clipboard.SetText(builder.ToString());
		MessageBox.Show($"已复制 {LogListBox.SelectedItems.Count} 条选中日志。", "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Information);
	}

	private void OpenLogFile_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			string logPath = LogCollector.LogFilePath;
			string directory = Path.GetDirectoryName(logPath) ?? AppDomain.CurrentDomain.BaseDirectory;
			Directory.CreateDirectory(directory);
			if (!File.Exists(logPath))
			{
				File.WriteAllText(logPath, string.Empty, Encoding.UTF8);
			}

			Process.Start(new ProcessStartInfo
			{
				FileName = logPath,
				UseShellExecute = true
			});
		}
		catch (System.Exception ex)
		{
			MessageBox.Show("打开日志文件失败：\n" + ex.Message, "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	private void Clear_Click(object sender, RoutedEventArgs e)
	{
		LogCollector.Instance.Clear();
	}
}
