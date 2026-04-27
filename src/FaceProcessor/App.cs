#define TRACE
using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FaceProcessor.Services;

namespace FaceProcessor;

public class App : Application
{
	private bool _contentLoaded;

	private void InstallGlobalExceptionHandling()
	{
		LogCollector.Install();
		base.DispatcherUnhandledException += delegate(object _, DispatcherUnhandledExceptionEventArgs args)
		{
			Trace.WriteLine($"[App] Unhandled exception: {args.Exception}");
			MessageBox.Show("Unhandled exception: " + args.Exception.Message + "\n\nSee log: " + LogCollector.LogFilePath, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
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
			string themePath = Path.Combine(AppContext.BaseDirectory, "Themes", "StudioTheme.xaml");
			Resources.MergedDictionaries.Add(new ResourceDictionary
			{
				Source = new Uri(themePath, UriKind.Absolute)
			});
		}
	}

	[STAThread]
	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "9.0.3.0")]
	public static void Main()
	{
		App app = new App();
		app.InstallGlobalExceptionHandling();
		app.InitializeComponent();
		try
		{
			app.Run(new MainWindow());
		}
		catch (Exception ex)
		{
			Trace.WriteLine($"[App] Startup failure: {ex}");
			MessageBox.Show("Application failed to start: " + ex.Message + "\n\nSee log: " + LogCollector.LogFilePath, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}
}
