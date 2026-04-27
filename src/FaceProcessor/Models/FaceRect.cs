namespace FaceProcessor.Models;

public class FaceRect
{
	public int X { get; set; }

	public int Y { get; set; }

	public int Width { get; set; }

	public int Height { get; set; }

	public float Confidence { get; set; }

	public FaceRect(int x, int y, int width, int height, float confidence = 1f)
	{
		X = x;
		Y = y;
		Width = width;
		Height = height;
		Confidence = confidence;
	}
}
