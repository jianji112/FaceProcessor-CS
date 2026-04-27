#define TRACE
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace FaceProcessor.Services;

public sealed class LogCollector : TraceListener
{
	private const int MaxEntries = 2000;

	private static readonly object FileLock = new object();

	public static LogCollector Instance { get; } = new LogCollector();

	public ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();

	public static string LogFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "faceprocessor.log");

	private LogCollector()
	{
	}

	public override void Write(string? message)
	{
		if (!string.IsNullOrWhiteSpace(message))
		{
			Append(message, null);
		}
	}

	public override void WriteLine(string? message)
	{
		if (!string.IsNullOrWhiteSpace(message))
		{
			Append(message, null);
		}
	}

	public override void Write(string? message, string? category)
	{
		if (!string.IsNullOrWhiteSpace(message))
		{
			Append(message, category);
		}
	}

	public override void WriteLine(string? message, string? category)
	{
		if (!string.IsNullOrWhiteSpace(message))
		{
			Append(message, category);
		}
	}

	private void Append(string message, string? category)
	{
		LogEntry entry = new LogEntry
		{
			Time = DateTime.Now,
			Category = category,
			Message = message
		};
		WriteToFile(entry.Formatted);
		(Application.Current?.Dispatcher)?.BeginInvoke((Action)delegate
		{
			Entries.Add(entry);
			while (Entries.Count > 2000)
			{
				Entries.RemoveAt(0);
			}
		});
	}

	private static void WriteToFile(string line)
	{
		try
		{
			string directory = Path.GetDirectoryName(LogFilePath);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}
			lock (FileLock)
			{
				File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
			}
		}
		catch
		{
		}
	}

	public void Clear()
	{
		Application.Current?.Dispatcher.Invoke(Entries.Clear);
	}

	public static void Install()
	{
		LogCollector existing = Trace.Listeners.OfType<LogCollector>().FirstOrDefault();
		if (existing != null)
		{
			Trace.Listeners.Remove(existing);
		}
		Trace.AutoFlush = true;
		Trace.Listeners.Add(Instance);
		Trace.WriteLine("[LogCollector] Installed. Log file: " + LogFilePath);
	}
}
