using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FaceProcessor.Services;

namespace FaceProcessor;

public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		LogCollector.Install();
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		LoadThemeResources();
		base.OnStartup(e);
		MainWindow = new MainWindow();
		MainWindow.Show();
	}

	private void LoadThemeResources()
	{
		string themePath = Path.Combine(AppContext.BaseDirectory, "Themes", "StudioTheme.xaml");
		Resources.MergedDictionaries.Add(new ResourceDictionary
		{
			Source = new Uri(themePath, UriKind.Absolute)
		});
	}

	private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		Trace.WriteLine($"[App] Unhandled exception: {e.Exception}");
		MessageBox.Show("程序发生未处理异常：\n" + e.Exception.Message + "\n\n详细日志见：\n" + LogCollector.LogFilePath, "FaceProcessor", MessageBoxButton.OK, MessageBoxImage.Error);
		e.Handled = true;
	}
}
