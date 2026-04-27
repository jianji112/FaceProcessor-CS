using System;

namespace FaceProcessor.Services;

public class VideoInfo
{
	public TimeSpan Duration { get; set; }

	public int Width { get; set; }

	public int Height { get; set; }

	public double FrameRate { get; set; }

	public bool HasAudio { get; set; }

	public string AudioCodec { get; set; } = "";

	public string VideoCodec { get; set; } = "";
}
