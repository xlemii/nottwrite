using HandwritingFontCreator.Core.Models;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HandwritingFontCreator.UI;

public partial class MainWindow : Window
{
    private bool _isDrawing;
    private Polyline? _currentLine;

    private readonly List<List<Point>> _strokes = [];
    private List<Point>? _currentStroke;
    private readonly Stack<List<Point>> _redoStack = [];

    private string CurrentTemplate = "Default";

    private string GetTemplateFolder()
    {
        string folder =
            System.IO.Path.Combine(
                @"C:\Users\matys\Desktop\projects\nottwrite\templates",
                CurrentTemplate);

        Directory.CreateDirectory(folder);

        return folder;
    }

    public MainWindow()
    {
        InitializeComponent();
        TemplateComboBox.Items.Add("Default");
        TemplateComboBox.Items.Add("School");
        TemplateComboBox.Items.Add("Fancy");
        TemplateComboBox.Items.Add("Messy");

        TemplateComboBox.SelectedIndex = 0;

        Loaded += (_, _) =>
        {
            DrawNotebookLines();
        };

        for (char c = 'A'; c <= 'Z'; c++)
        {
            LetterComboBox.Items.Add(c.ToString());
        }

        for (char c = 'a'; c <= 'z'; c++)
        {
            LetterComboBox.Items.Add(c.ToString());
        }

        for (char c = '0'; c <= '9'; c++)
        {
            LetterComboBox.Items.Add(c.ToString());
        }

        LetterComboBox.SelectedIndex = 0;
    }

    private string GetSelectedFilePath()
    {
        string letter =
            LetterComboBox.SelectedItem?.ToString()
            ?? "A";

        return System.IO.Path.Combine(
            GetTemplateFolder(),
            $"{letter}.json");
    }

    private void DrawingCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _redoStack.Clear();
        _isDrawing = true;

        _currentLine = new Polyline
        {
            Stroke = Brushes.Black,
            StrokeThickness = 3
        };

        Point point = e.GetPosition(DrawingCanvas);

        _currentStroke = [];
        _currentStroke.Add(point);

        _strokes.Add(_currentStroke);

        _currentLine.Points.Add(point);

        DrawingCanvas.Children.Add(_currentLine);

        DrawingCanvas.CaptureMouse();
    }

    private void DrawingCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing || _currentLine == null)
            return;

        Point point = e.GetPosition(DrawingCanvas);

        _currentStroke?.Add(point);
        _currentLine.Points.Add(point);
    }

    private void DrawingCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDrawing = false;
        DrawingCanvas.ReleaseMouseCapture();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        DrawingCanvas.Children.Clear();

        DrawNotebookLines();

        _strokes.Clear();
        _redoStack.Clear();
    }

    private List<Point> SmoothStroke(List<Point> points)
    {
        if (points.Count < 3)
            return points;

        var result = new List<Point>();

        result.Add(points[0]);

        for (int i = 1; i < points.Count - 1; i++)
        {
            double x =
                (points[i - 1].X +
                 points[i].X +
                 points[i + 1].X) / 3.0;

            double y =
                (points[i - 1].Y +
                 points[i].Y +
                 points[i + 1].Y) / 3.0;

            result.Add(new Point(x, y));
        }

        result.Add(points[^1]);

        return result;
    }
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strokes.Count == 0)
        {
            MessageBox.Show("Najpierw narysuj literę");
            return;
        }

        double minX = double.MaxValue;
        double minY = double.MaxValue;

        double maxX = double.MinValue;
        double maxY = double.MinValue;

        foreach (var stroke in _strokes)
        {
            foreach (var point in stroke)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);

                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        var letter = new StrokeData
        {
            Width = (maxX - minX) + 20,
            Height = (maxY - minY) + 20,
            Baseline = maxY - minY
        };

        foreach (var originalStroke in _strokes)
        {
            var strokePoints = SmoothStroke(originalStroke);
            var stroke = new Stroke();

            foreach (var point in strokePoints)
            {
                stroke.Points.Add(new PointData
                {
                    X = point.X - minX,
                    Y = point.Y - minY
                });
            }

            letter.Strokes.Add(stroke);
        }

        string json = JsonSerializer.Serialize(
            letter,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        Directory.CreateDirectory(GetTemplateFolder());

        string filePath = GetSelectedFilePath();

        File.WriteAllText(filePath, json);

        MessageBox.Show($"Zapisano {System.IO.Path.GetFileName(filePath)}");
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        string filePath = GetSelectedFilePath();

        if (!File.Exists(filePath))
        {
            MessageBox.Show($"Nie znaleziono {System.IO.Path.GetFileName(filePath)}");
            return;
        }

        string json = File.ReadAllText(filePath);

        StrokeData? letter =
            JsonSerializer.Deserialize<StrokeData>(json);

        if (letter == null)
            return;

        DrawingCanvas.Children.Clear();
        DrawNotebookLines();

        foreach (var stroke in letter.Strokes)
        {
            var line = new Polyline
            {
                Stroke = Brushes.Blue,
                StrokeThickness = 3
            };

            foreach (var point in stroke.Points)
            {
                line.Points.Add(
                    new Point(
                        point.X,
                        point.Y));
            }

            DrawingCanvas.Children.Add(line);
        }
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        DrawingCanvas.Children.Clear();
        DrawNotebookLines();
        DrawGeneratedText(DrawingCanvas);
    }

    private void PreviewAlphabetButton_Click(object sender, RoutedEventArgs e)
    {
        DrawingCanvas.Children.Clear();
        DrawNotebookLines();

        double offsetX = 20;
        double offsetY = 20;

        const double targetHeight = 120;

        string characters =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
            "abcdefghijklmnopqrstuvwxyz" +
            "0123456789";

        foreach (char c in characters)
        {
            string filePath = System.IO.Path.Combine(
                GetTemplateFolder(),
                $"{c}.json");

            if (!File.Exists(filePath))
                continue;

            string json = File.ReadAllText(filePath);

            StrokeData? letter =
                JsonSerializer.Deserialize<StrokeData>(json);

            if (letter == null)
                continue;

            double scale = 1;

            if (letter.Height > 0)
            {
                scale = targetHeight / letter.Height;
            }

            foreach (var stroke in letter.Strokes)
            {
                var line = new Polyline
                {
                    Stroke = Brushes.DarkBlue,
                    StrokeThickness = 2
                };

                foreach (var point in stroke.Points)
                {
                    line.Points.Add(
                        new Point(
                            offsetX + (point.X * scale),
                            offsetY + (point.Y * scale)));
                }

                DrawingCanvas.Children.Add(line);
            }

            offsetX += (letter.Width * scale) + 15;

            if (offsetX > 1050)
            {
                offsetX = 20;
                offsetY += 180;
            }
        }
    }

    private void DrawNotebookLines()
    {
        for (int y = 100; y <= 600; y += 100)
        {
            var line = new Line
            {
                X1 = 0,
                X2 = DrawingCanvas.ActualWidth > 0
                    ? DrawingCanvas.ActualWidth
                    : 2000,

                Y1 = y,
                Y2 = y,

                Stroke = Brushes.LightGray,
                StrokeThickness = 1
            };

            DrawingCanvas.Children.Add(line);
        }
    }

    private void ExportPngButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory("exports");

        var exportCanvas = new Canvas
        {
            Width = 3000,
            Height = 800,
            Background = Brushes.Transparent
        };

        DrawGeneratedText(exportCanvas);

        exportCanvas.Measure(
            new Size(
                exportCanvas.Width,
                exportCanvas.Height));

        exportCanvas.Arrange(
            new Rect(
                0,
                0,
                exportCanvas.Width,
                exportCanvas.Height));

        var bitmap = new RenderTargetBitmap(
            (int)exportCanvas.Width,
            (int)exportCanvas.Height,
            96,
            96,
            PixelFormats.Pbgra32);

        bitmap.Render(exportCanvas);

        var encoder = new PngBitmapEncoder();

        encoder.Frames.Add(
            BitmapFrame.Create(bitmap));

        string filePath =
            System.IO.Path.Combine(
                "exports",
                $"{DateTime.Now:yyyyMMdd_HHmmss}.png");

        using (var stream = File.Create(filePath))
        {
            encoder.Save(stream);
        }

        MessageBox.Show(
            $"Zapisano PNG:\n{System.IO.Path.GetFullPath(filePath)}");
    }

    private void DrawGeneratedText(Canvas canvas)
    {
        string text = GenerateTextBox.Text;

        double offsetX = 20;

        const double targetHeight = 180;
        const double baselineY = 250;

        foreach (char character in text)
        {
            string filePath = System.IO.Path.Combine(
                GetTemplateFolder(),
                $"{character}.json");

            if (!File.Exists(filePath))
                continue;

            string json = File.ReadAllText(filePath);

            StrokeData? letter =
                JsonSerializer.Deserialize<StrokeData>(json);

            if (letter == null)
                continue;

            double scale = 1;

            if (letter.Height > 0)
            {
                scale = targetHeight / letter.Height;
            }

            double baselineOffset =
                baselineY - (letter.Baseline * scale);

            foreach (var stroke in letter.Strokes)
            {
                var line = new Polyline
                {
                    Stroke = Brushes.Black,
                    StrokeThickness = 4
                };

                foreach (var point in stroke.Points)
                {
                    line.Points.Add(
                        new Point(
                            offsetX + (point.X * scale),
                            baselineOffset + (point.Y * scale)));
                }

                canvas.Children.Add(line);
            }

            offsetX += (letter.Width * scale) + 20;
        }
    }

    private void TemplateComboBox_SelectionChanged(
    object sender,
    System.Windows.Controls.SelectionChangedEventArgs e)
    {
        CurrentTemplate =
            TemplateComboBox.SelectedItem?.ToString()
            ?? "Default";
    }

    private void RedrawCurrentStrokes()
    {
        DrawingCanvas.Children.Clear();

        DrawNotebookLines();

        foreach (var stroke in _strokes)
        {
            var line = new Polyline
            {
                Stroke = Brushes.Black,
                StrokeThickness = 3
            };

            foreach (var point in stroke)
            {
                line.Points.Add(point);
            }

            DrawingCanvas.Children.Add(line);
        }
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strokes.Count == 0)
            return;

        var lastStroke = _strokes[^1];

        _strokes.RemoveAt(_strokes.Count - 1);

        _redoStack.Push(lastStroke);

        RedrawCurrentStrokes();
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0)
            return;

        var stroke = _redoStack.Pop();

        _strokes.Add(stroke);

        RedrawCurrentStrokes();
    }
}