using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Commands;

internal static class TrimInteractionTests
{
    public static void Run()
    {
        int count=0;void Check(bool ok,string name){if(!ok)throw new Exception("Trim interaction: "+name);count++;}
        bool Near(double x,double y)=>Math.Abs(x-y)<1e-7;
        foreach(var plane in StandardPlanes.All)
        {
            var s=new SketchModel(plane);
            SketchLine Line(double x,double y,double u,double v,bool construction=false)=>s.AddLine(s.CreatePointExact(new(x,y)),s.CreatePointExact(new(u,v)),construction);
            var target=Line(-100,0,100,0,true);var left=Line(-40,-30,-40,30);var right=Line(60,-30,60,30);
            var dim=s.AddDimension(SketchDimensionType.LineLength,new(0,20),entityA:target.Id);
            var before=s.CaptureSnapshot();var command=new TrimSketchCommand(s,0.1);int changes=0;
            command.Begin(new CommandContext{ModelChanged=()=>changes++});
            var preview=(SketchPreviewLine)command.Preview(new(0,0)).Single();
            Check(preview.A==new Point2d(-40,0)&&preview.B==new Point2d(60,0),"hover only between finite line intersections");
            Check(s.Entities.SequenceEqual(before.Entities)&&s.Points.OrderBy(p=>p.Key).SequenceEqual(before.Points.OrderBy(p=>p.Key))&&changes==0,"hover does not mutate model/history");
            command.Pick(new SketchPick(new(0,0),-1,0.1,1));
            Check(changes==1&&s.Entity(target.Id) is null,"pick commits actual trim once");
            Check(s.Dimension(dim.Id) is null&&s.Entity(left.Id) is not null&&s.Entity(right.Id) is not null,"dependent dimension cleaned, cutters unchanged");
            var retained=s.Lines().Where(l=>l.Construction).ToArray();
            Check(retained.Length==2&&Near(retained.Sum(l=>Point2d.Distance(s.Point(l.StartPointId).Position,s.Point(l.EndPointId).Position)),100),"two surviving pieces retain construction semantics");
            var after=s.CaptureSnapshot();s.RestoreSnapshot(before);
            Check(s.Entity(target.Id) is not null&&s.Dimension(dim.Id) is not null,"undo restores full entity and dimension");
            s.RestoreSnapshot(after);Check(s.Entity(target.Id) is null&&s.Dimension(dim.Id) is null,"redo restores trimmed topology");
            var circle=new SketchModel(plane);var a=circle.CreatePointExact(new(-20,0));var b=circle.CreatePointExact(new(20,0));circle.AddLine(a,b);
            var c=circle.AddCircle(circle.CreatePointExact(new(0,0)),10);
            var cut=circle.PreviewLineTrim(new(5,0),0.1)!;
            Check(Near(cut.Start.X,-10)&&Near(cut.End.X,10),"circle intersections bound the removed line span");
            Check(circle.TrimLineAt(new(5,0),0.1)&&circle.Lines().Count()==2&&circle.Entity(c.Id)==c,"line trimmed against real circle without changing circle");
            Check(circle.PreviewLineTrim(new(0,10),0.1) is null,"unsupported circle trimming rejected, no fake geometry");
            var lone=new SketchModel(plane);lone.AddLine(lone.CreatePointExact(new(1,1)),lone.CreatePointExact(new(101,1.1)));
            Check(lone.TrimLineAt(new(50,1.049),0.1)&&lone.Entities.Count==0,"unbounded segment trim removes full line");
            var invalidBefore=s.CaptureSnapshot();Check(!s.TrimLineAt(new(999,999),0.1)&&s.Entities.SequenceEqual(invalidBefore.Entities),"blank pick no mutation");
        }
        Console.WriteLine($"TRIM INTERACTION: {count} PASS (shared hover/commit command geometry; Windows HWND pending)");
    }
}
