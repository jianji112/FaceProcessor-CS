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

        // 绑定日志集合到 ListBox
        LogListBox.ItemsSource = LogCollector.Instance.Entries;

        // 计数同步
        LogCollector.Instance.Entries.CollectionChanged += (_, _) => UpdateCount();
        UpdateCount();
    }

    private void UpdateCount()
    {
        Dispatcher.Invoke(() => CountText.Text = $"共 {LogCollector.Instance.Entries.Count} 条");
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (LogCollector.Instance.Entries.Count == 0)
        {
            MessageBox.Show("日志为空，无需复制", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sb = new StringBuilder();
        foreach (var entry in LogCollector.Instance.Entries)
            sb.AppendLine(entry.Formatted);

        Clipboard.SetText(sb.ToString());
        MessageBox.Show($"已复制 {LogCollector.Instance.Entries.Count} 条日志", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        if (LogListBox.SelectedItems.Count == 0)
        {
            MessageBox.Show("请先选中要复制的日志行", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sb = new StringBuilder();
        foreach (LogEntry entry in LogListBox.SelectedItems)
            sb.AppendLine(entry.Formatted);

        Clipboard.SetText(sb.ToString());
        MessageBox.Show($"已复制 {LogListBox.SelectedItems.Count} 条日志", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        LogCollector.Instance.Clear();
    }
}
