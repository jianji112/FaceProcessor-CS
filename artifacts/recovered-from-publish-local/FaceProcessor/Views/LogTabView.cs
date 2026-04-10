using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using FaceProcessor.Services;

namespace FaceProcessor.Views;

public class LogTabView : UserControl, IComponentConnector
{
	internal TextBlock CountText;

	internal ListBox LogListBox;

	private bool _contentLoaded;

	public LogTabView()
	{
		InitializeComponent();
		LogListBox.ItemsSource = LogCollector.Instance.Entries;
		LogCollector.Instance.Entries.CollectionChanged += delegate
		{
			UpdateCount();
		};
		UpdateCount();
	}

	private void UpdateCount()
	{
		base.Dispatcher.Invoke(() => CountText.Text = $"{LogCollector.Instance.Entries.Count} 条");
	}

	private void CopyAll_Click(object sender, RoutedEventArgs e)
	{
		if (LogCollector.Instance.Entries.Count == 0)
		{
			MessageBox.Show("当前没有可复制的日志。", "提示", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		StringBuilder builder = new StringBuilder();
		foreach (LogEntry entry in LogCollector.Instance.Entries)
		{
			builder.AppendLine(entry.Formatted);
		}
		Clipboard.SetText(builder.ToString());
		MessageBox.Show($"已复制 {LogCollector.Instance.Entries.Count} 条日志。", "提示", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	private void CopySelected_Click(object sender, RoutedEventArgs e)
	{
		if (LogListBox.SelectedItems.Count == 0)
		{
			MessageBox.Show("请先选择要复制的日志行。", "提示", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		StringBuilder builder = new StringBuilder();
		foreach (LogEntry entry in LogListBox.SelectedItems)
		{
			builder.AppendLine(entry.Formatted);
		}
		Clipboard.SetText(builder.ToString());
		MessageBox.Show($"已复制 {LogListBox.SelectedItems.Count} 条选中日志。", "提示", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	private void Clear_Click(object sender, RoutedEventArgs e)
	{
		LogCollector.Instance.Clear();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "9.0.3.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocater = new Uri("/FaceProcessor;component/views/logtabview.xaml", UriKind.Relative);
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
			((Button)target).Click += CopyAll_Click;
			break;
		case 2:
			((Button)target).Click += CopySelected_Click;
			break;
		case 3:
			((Button)target).Click += Clear_Click;
			break;
		case 4:
			CountText = (TextBlock)target;
			break;
		case 5:
			LogListBox = (ListBox)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
