namespace HandwritingFontCreator.Core.Models;

public class StrokeData
{
    public double Width { get; set; }

    public double Height { get; set; }

    public double Baseline { get; set; }

    public List<Stroke> Strokes { get; set; } = [];
}

public class Stroke
{
    public List<PointData> Points { get; set; } = [];
}

public class PointData
{
    public double X { get; set; }
    public double Y { get; set; }
}