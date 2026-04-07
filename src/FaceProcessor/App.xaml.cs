using System.Windows;
using FaceProcessor.Services;

namespace FaceProcessor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 安装日志收集器（修复Bug 3：确保Debug.WriteLine和Trace.WriteLine都能被收集）
        LogCollector.Install();

        // 设置异常处理
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"发生错误：{args.Exception.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
