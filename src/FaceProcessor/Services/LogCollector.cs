using System.Collections.ObjectModel;
using System.Diagnostics;

namespace FaceProcessor.Services;

/// <summary>全局 Debug.WriteLine 日志收集器（单例）</summary>
public sealed class LogCollector : TraceListener
{
    public static LogCollector Instance { get; } = new();

    /// <summary>日志条目集合，UI 绑定到此</summary>
    public ObservableCollection<LogEntry> Entries { get; } = new();

    /// <summary>最大保留条数，防止内存溢出</summary>
    private const int MaxEntries = 2000;

    private LogCollector() { }

    public override void Write(string? message)
    {
        if (string.IsNullOrEmpty(message)) return;
        Append(message, null);
    }

    public override void WriteLine(string? message)
    {
        if (string.IsNullOrEmpty(message)) return;
        Append(message, null);
    }

    public override void Write(string? message, string? category)
    {
        if (string.IsNullOrEmpty(message)) return;
        Append(message, category);
    }

    public override void WriteLine(string? message, string? category)
    {
        if (string.IsNullOrEmpty(message)) return;
        Append(message, category);
    }

    private void Append(string message, string? category)
    {
        var entry = new LogEntry
        {
            Time = DateTime.Now,
            Category = category,
            Message = message
        };

        // 线程安全地添加
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Entries.Add(entry);

            // 超量时删掉最旧的
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        });
    }

    /// <summary>清空所有日志</summary>
    public void Clear()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(Entries.Clear);
    }

    /// <summary>安装到 Trace 列表（应用启动时调用一次）</summary>
    public static void Install()
    {
        // 移除旧实例（防止重复）
        var existingInTrace = Trace.Listeners.OfType<LogCollector>().FirstOrDefault();
        if (existingInTrace != null)
            Trace.Listeners.Remove(existingInTrace);

        // 添加到 Trace.Listeners（Trace.WriteLine 使用这个）
        // 注意：Debug.Listeners 在 .NET 8/Core 下不存在，只能用 Trace.Listeners
        Trace.Listeners.Add(Instance);

        Trace.WriteLine("[LogCollector] 已安装到 Trace.Listeners");
    }
}

/// <summary>单条日志条目</summary>
public class LogEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string? Category { get; set; }
    public string Message { get; set; } = "";

    public string Formatted =>
        $"[{Time:HH:mm:ss}] {(string.IsNullOrEmpty(Category) ? "" : $"[{Category}] ")}{Message}";
}
