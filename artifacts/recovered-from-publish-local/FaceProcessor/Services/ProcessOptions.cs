using FaceProcessor.Models;

namespace FaceProcessor.Services;

public class ProcessOptions
{
	public int BlockSize { get; set; }

	public int BlurStrength { get; set; } = 99;

	public float FaceThreshold { get; set; } = 0.5f;

	public int MaxFacesToProcess { get; set; } = 1;

	public VideoOutputResolution OutputResolution { get; set; }

	public int MaxResolution { get; set; }

	public int MaxFileSizeMB { get; set; }

	public string OutputPath { get; set; } = "";

	public string Format { get; set; } = "PNG";

	public int Quality { get; set; } = 95;

	public bool KeepAudio { get; set; } = true;
}
