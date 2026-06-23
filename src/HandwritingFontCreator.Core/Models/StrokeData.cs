namespace HandwritingFontCreator.Core.Models;

public class StrokeData
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double Baseline { get; set; }
    public string? Color { get; set; }   // hex e.g. "#E8E8E8"; null = use editor default

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
    public double Pressure { get; set; } = 1.0;   // 0..~1.5; 1.0 = uniform (mouse)
}