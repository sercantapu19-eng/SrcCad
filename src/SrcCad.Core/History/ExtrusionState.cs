using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;

namespace SrcCad.Core.History;

/// <summary>Depth null means the sketch exists but no extrusion feature exists.</summary>
public sealed record ExtrusionState(PlaneFrame Plane, SketchSnapshot Sketch, double? Depth);
