namespace SrcCad.Solver;

public interface IConstraint
{
    int ResidualCount { get; }
    void Residuals(ReadOnlySpan<double> x, Span<double> residuals);
}

public enum ConstraintState
{
    UnderDefined,
    FullyDefined,
    Redundant,
    Conflicting
}

public sealed record SolveResult(
    ConstraintState State,
    int DegreesOfFreedom,
    double ResidualNorm,
    string? Message);
