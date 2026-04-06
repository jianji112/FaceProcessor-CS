using System.Windows;

namespace FaceProcessor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // 设置异常处理
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"发生错误：{args.Exception.Message}", "错误", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
