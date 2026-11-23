using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Core.Persistence;
using SrcCad.Core.History;

internal static class ParallelDimensionTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool ok,string message) {if(!ok)throw new Exception("Parallel dimension: "+message);count++;}
        bool Near(double a,double b)=>Math.Abs(a-b)<1e-6;
        foreach(var plane in StandardPlanes.All)
        foreach(var angle in new[]{0.0,Math.PI/2,0.63})
        foreach(var reverse in new[]{false,true})
        {
            var s=new SketchModel(plane);
            Point2d P(double x,double y)=>new(40+x*Math.Cos(angle)-y*Math.Sin(angle),70+x*Math.Sin(angle)+y*Math.Cos(angle));
            var a=s.CreatePointExact(P(0,0));var b=s.CreatePointExact(P(100,0));
            var c=s.CreatePointExact(P(20,20));var e=s.CreatePointExact(P(80,20));
            s.SetPointFixed(a,true);s.SetPointFixed(b,true);
            var carrier=s.AddLine(a,b,true);var moving=s.AddLine(e,c);
            var session=new SmartDimensionSession();
            Check(!session.Pick(s,P(50,reverse?20:0),0.1)&&!session.Pick(s,P(50,reverse?0:20),0.1),"body selection, reversed construction/line pair");
            var candidate=session.Preview(s,P(50,40))!;
            Check(candidate.Type==SketchDimensionType.ParallelLineDistance&&Near(candidate.Value,20),"parallel rather than 180-degree angle inference");
            var beforeDof=s.DefinitionInfo().DegreesOfFreedom;
            var d=s.AddInferredDimension(candidate);
            Check(s.DefinitionInfo().DegreesOfFreedom==beforeDof-2,"distance and parallelism contribute independent equations");
            Check(s.SetDimensionValue(d.Id,35),"edit succeeds");
            double Height(int id) {var p=s.Point(id).Position;return -(p.X-40)*Math.Sin(angle)+(p.Y-70)*Math.Cos(angle);}
            Check(Near(Height(c),35)&&Near(Height(e),35),"both endpoints driven, antiparallel orientation retained");
            var edited=s.CaptureSnapshot();
            Check(s.SetDimensionValue(d.Id,0)&&Near(Height(c),0)&&Near(Height(e),0),"zero distance is collinearity");
            Check(s.SetDimensionValue(d.Id,12)&&Near(Height(c),12)&&Near(Height(e),12),"zero dimension preserves side when increased");
            s.RestoreSnapshot(edited);
            Check(Near(s.Measure(s.Dimension(d.Id)!),35)&&Near(Height(c),35),"snapshot undo restores value and geometry");
            var copy=new SketchModel(plane);copy.RestoreSnapshot(PartFile.Deserialize(PartFile.Serialize(new ExtrusionState(plane,s.CaptureSnapshot(),null))).Sketch);
            Check(copy.SetDimensionValue(d.Id,45)&&Near(copy.Measure(copy.Dimension(d.Id)!),45)&&!copy.DefinitionInfo().IsConflicting,"round-trip retains driving and parallel equations");
            s.SetPointFixed(c,true);s.SetPointFixed(e,true);
            var json=PartFile.Serialize(new ExtrusionState(plane,s.CaptureSnapshot(),null));
            Check(!s.SetDimensionValue(d.Id,70)&&json==PartFile.Serialize(new ExtrusionState(plane,s.CaptureSnapshot(),null)),"conflicting edit rolls back all model state");
            s.DeleteEntity(moving.Id);
            Check(s.Dimensions.Count==0,"deleting referenced line removes dependent dimension");
        }
        foreach(var plane in StandardPlanes.All)
        {
            var profile=new SketchModel(plane);
            var ids=profile.AddThreePointRectangle(new(0,0),new(80,60),new(-18,24),0.001);
            var width=profile.AddDimension(SketchDimensionType.LineLength,new(50,100),entityA:ids[0]);
            var distance=profile.AddInferredDimension(profile.InferDimension(new[]{profile.ReferenceForEntity(ids[0]),profile.ReferenceForEntity(ids[2])},new(-50,50))!);
            Check(profile.SetDimensionValue(width.Id,100)&&profile.SetDimensionValue(distance.Id,45),"closed rotated profile driving distance");
            Check(profile.TryGetExtrusionProfile(out var loop,out _,out _)&&Near(Math.Abs(loop.Select((p,i)=>p.X*loop[(i+1)%loop.Count].Y-p.Y*loop[(i+1)%loop.Count].X).Sum())/2,4500),"actual closed profile area changes to 4500");
        }
        var angled=new SketchModel(StandardPlanes.Front);
        var l=angled.AddLine(angled.CreatePointExact(new(10,10)),angled.CreatePointExact(new(90,10)));
        var k=angled.AddLine(angled.CreatePointExact(new(10,40)),angled.CreatePointExact(new(70,90)));
        Check(angled.InferDimension(new[]{angled.ReferenceForEntity(l.Id),angled.ReferenceForEntity(k.Id)},new(60,30))!.Type==SketchDimensionType.Angle,"nonparallel pair retains angular dimension");
        Console.WriteLine($"PARALLEL DIMENSIONS: {count} PASS (shared selection, solver, DOF, persistence, rollback)");
    }
}
