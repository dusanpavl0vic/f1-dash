namespace F1Dash.Core.Track;

/// <summary>
/// The rotate-and-flip that puts the circuit outline and the live car positions
/// into the same screen space (docs/10).
///
/// Both the server (drawing the outline) and the client (placing car dots) must
/// use identical maths, so the transform is expressed once, here, and mirrored
/// in web/src/features/track-map/lib/toScreen.ts against a shared fixture.
/// </summary>
public readonly record struct Transform(double RotationDegrees, bool FlipY = true)
{
    public (double X, double Y) Apply(double x, double y)
    {
        var a = RotationDegrees * Math.PI / 180.0;
        var rx = (x * Math.Cos(a)) + (y * Math.Sin(a));
        var ry = (-x * Math.Sin(a)) + (y * Math.Cos(a));

        // F1 coordinates have Y increasing upward; SVG has Y increasing
        // downward. Without the flip the circuit renders mirrored.
        return (rx, FlipY ? -ry : ry);
    }
}
