namespace F1Dash.Core.Track;

public sealed record Point(double X, double Y);

public sealed record CornerMarker(int Number, double X, double Y, double LabelX, double LabelY);

public sealed record MarshalSector(int Number, string Path);

public sealed record ViewBox(double X, double Y, double Width, double Height)
{
    public override string ToString() =>
        $"{X:0} {Y:0} {Width:0} {Height:0}";
}

/// <summary>
/// A circuit, already transformed into screen space. The client renders this
/// directly with no maths of its own.
/// </summary>
public sealed record TrackGeometry(
    int CircuitKey,
    string CircuitName,
    int Year,
    double Rotation,
    string Path,
    ViewBox ViewBox,
    IReadOnlyList<CornerMarker> Corners,
    IReadOnlyList<MarshalSector> MarshalSectors,
    Point StartFinish);
