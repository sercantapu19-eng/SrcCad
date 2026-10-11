using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Core.History;
using SrcCad.Core.Persistence;
using SrcCad.Commands;

internal static class ThreePointToolTests
{
    public static void Run()
    {
        int count=0;void Check(bool ok,string message){if(!ok)throw new Exception("Three point tools: "+message);count++;}
        bool Near(double a,double b)=>Math.Abs(a-b)<1e-6;
        foreach(var plane in StandardPlanes.All)
        {
            var s=new SketchModel(plane);var cmd=new ThreePointRectangleCommand(s);cmd.Begin(new());
            cmd.Pick(new(new(0,0),0,0.001,1));cmd.Pick(new(new(80,60),-1,0.001,1));
            Check(cmd.Preview(new(-18,24)).Count==4&&s.Entities.Count==0,"rectangle preview without mutation");
            cmd.Pick(new(new(-18,24),-1,0.001,1));
            Check(s.TryGetExtrusionProfile(out var loop,out _,out _)&&loop.Count==4,"three point closed rotated rectangle");
            var lines=s.Lines().ToArray();var width=s.AddDimension(SketchDimensionType.LineLength,new(50,100),entityA:lines[0].Id);
            var height=s.AddDimension(SketchDimensionType.LineLength,new(100,50),entityA:lines[1].Id);
            Check(s.SetDimensionValue(height.Id,30)&&s.SetDimensionValue(width.Id,120),"rectangle dimensions drive lengths");
            Check(s.TryGetExtrusionProfile(out _,out _,out _)&&Near(s.Measure(s.Dimension(width.Id)!),120)&&Near(s.Measure(s.Dimension(height.Id)!),30),"rectangle remains valid after edit");
            var axis=s.AxisReference(lines[0].Id);
            Check(axis.IsLine&&axis.Kind==GeometryReferenceKind.Axis&&s.InferDimension(new[]{s.ReferenceForPoint(lines[2].StartPointId),axis},new(30,20))!.Type==SketchDimensionType.PointLineDistance,"axis semantic reference uses common dimension inference");
            var state=PartFile.Deserialize(PartFile.Serialize(new ExtrusionState(plane,s.CaptureSnapshot(),20)));
            var loaded=new SketchModel(plane);loaded.RestoreSnapshot(state.Sketch);
            Check(loaded.SetDimensionValue(width.Id,140)&&loaded.TryGetExtrusionProfile(out _,out _,out _),"rotated rectangle post-open edit");
            var circle=new SketchModel(plane);var pts=new[]{new Point2d(10,0),new Point2d(0,10),new Point2d(-10,0)};
            var ids=pts.Select(p=>circle.CreatePointExact(p)).ToArray();foreach(var id in ids)circle.SetPointFixed(id,true);
            var tool=new ThreePointCircleCommand(circle);tool.Begin(new());
            tool.Pick(new(pts[0],ids[0],0.001,1));tool.Pick(new(pts[1],ids[1],0.001,1));
            Check(tool.Preview(pts[2]).Count==1&&!circle.Circles().Any(),"circle preview without mutation");
            tool.Pick(new(pts[2],ids[2],0.001,1));var c=circle.Circles().Single();
            Check(Near(c.Radius,10)&&Point2d.Distance(circle.Point(c.CenterPointId).Position,new(0,0))<1e-6,"circumcircle geometry");
            Check(circle.Relations.Count==3&&circle.DefinitionInfo().IsFullyDefined,"three snapped points persist constraints and rank");
            var unrelated=circle.AddLine(circle.CreatePointExact(new(200,0)),circle.CreatePointExact(new(200,20)));
            circle.DeleteEntity(unrelated.Id);
            Check(ids.All(circle.Points.ContainsKey)&&circle.DefinitionInfo().IsFullyDefined,"unrelated deletion preserves standalone reference inputs");
            var diameter=circle.AddDimension(SketchDimensionType.CircleDiameter,new(10,10),entityA:c.Id);
            Check(!circle.SetDimensionValue(diameter.Id,30)&&Near(((SketchCircle)circle.Entity(c.Id)!).Radius,10),"conflict with fixed input references rejected");
            var invalid=new SketchModel(plane);var bad=new ThreePointCircleCommand(invalid);bad.Begin(new());
            bad.Pick(new(new(0,0),-1,0.001,1));bad.Pick(new(new(10,0),-1,0.001,1));
            bool rejected=false;try{bad.Pick(new(new(20,0),-1,0.001,1));}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&invalid.Entities.Count==0&&invalid.Points.Count==1&&!bad.HasPendingStep,"collinear third pick leaves clean command/model");
            var large=SketchModel.CircleThrough(new(1e9+10,1e9),new(1e9,1e9+10),new(1e9-10,1e9));
            Check(Near(large.Radius,10),"translated large coordinate circumcircle");
        }
        Console.WriteLine($"THREE POINT TOOLS: {count} PASS (real command geometry, constraints, round-trip)");
    }
}
