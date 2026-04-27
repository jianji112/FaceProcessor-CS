using System;

namespace FaceProcessor.Services;

public class LogEntry
{
	public DateTime Time { get; set; } = DateTime.Now;

	public string? Category { get; set; }

	public string Message { get; set; } = "";

	public string Formatted => $"[{Time:HH:mm:ss}] {(string.IsNullOrWhiteSpace(Category) ? "" : ("[" + Category + "] "))}{Message}";
}
