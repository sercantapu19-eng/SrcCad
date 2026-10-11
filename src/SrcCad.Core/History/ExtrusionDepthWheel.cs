namespace SrcCad.Core.History;

/// <summary>Accumulates high-resolution wheel input; one Windows detent is 120.</summary>
public sealed class ExtrusionDepthWheel
{
    private int remainder;
    public void Reset() => remainder = 0;
    public double Apply(double depth, int delta)
    {
        remainder += delta;
        var steps = remainder / 120;
        remainder %= 120;
        return depth + steps * 10.0;
    }
}
