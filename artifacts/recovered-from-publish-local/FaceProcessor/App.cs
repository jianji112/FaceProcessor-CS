#define TRACE
using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using FaceProcessor.Services;

namespace FaceProcessor;

public class App : Application
{
	private bool _contentLoaded;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		LogCollector.Install();
		base.DispatcherUnhandledException += delegate(object _, DispatcherUnhandledExceptionEventArgs args)
		{
			Trace.WriteLine($"[App] Unhandled exception: {args.Exception}");
			MessageBox.Show("发生未处理异常：" + args.Exception.Message + "\n\n详细信息请查看日志页或 " + LogCollector.LogFilePath, "错误", MessageBoxButton.OK, MessageBoxImage.Hand);
			args.Handled = true;
		};
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "9.0.3.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			base.StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
			Uri resourceLocater = new Uri("/FaceProcessor;component/app.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocater);
		}
	}

	[STAThread]
	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "9.0.3.0")]
	public static void Main()
	{
		App app = new App();
		app.InitializeComponent();
		app.Run();
	}
}
