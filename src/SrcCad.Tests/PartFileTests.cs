using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Core.History;
using SrcCad.Core.Persistence;

internal static class PartFileTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool ok,string message){if(!ok)throw new Exception("Part file: "+message);count++;}
        foreach(var plane in StandardPlanes.All)
        {
            var s=new SketchModel(plane);s.AddCornerRectangle(new(0,0),new(100,60),0.001);
            var c=s.AddCircle(s.CreatePointExact(new(30,30)),5);
            var c2=s.AddCircle(s.CreatePointExact(new(70,30)),8);
            var p=s.CreatePointExact(new(10,10));var q=s.CreatePointExact(new(90,10));s.AddLine(p,q,true);
            var independentPoint=s.CreatePointExact(new(17,47));
            var origin=s.ReferenceForPoint(0);var center=s.ReferenceForPoint(c.CenterPointId);
            s.ApplyReferenceRelation(SketchRelationType.Horizontal,new[]{center,s.ReferenceForPoint(c2.CenterPointId)});
            var d=s.AddInferredDimension(s.InferDimension(new[]{origin,center},new(-20,15))!);
            Check(s.SetDimensionValue(d.Id,25),"pre-save reference dimension edit");
            var state=new ExtrusionState(plane,s.CaptureSnapshot(),20);
            var json=PartFile.Serialize(state);var reopened=PartFile.Deserialize(json);
            var loaded=new SketchModel(reopened.Plane);loaded.RestoreSnapshot(reopened.Sketch);
            Check(loaded.Relations.Last().ReferenceA==center&&loaded.Lines().Count(l=>l.Construction)==1,"semantic references and construction round-trip");
            Check(loaded.Point(independentPoint).Position==new Point2d(17,47),
                "independent sketch point survives save/open round-trip");
            Check(loaded.SetDimensionValue(d.Id,35)&&Math.Abs(loaded.Point(c2.CenterPointId).Position.Y-35)<1e-6,"reopened dimensions drive geometry and preserve relation");
            Check(loaded.TryGetExtrusionProfile(out _,out _,out var holes)&&holes.Count==2&&reopened.Depth==20,"reopened extrusion inputs");
            var before=loaded.CaptureSnapshot();loaded.DeleteEntity(c.Id);
            Check(!loaded.Dimensions.Any(x=>x.Id==d.Id)&&!loaded.Relations.Any(r=>r.ReferenceA==center||r.ReferenceB==center),"deleted reference owner cleans consumers");
            loaded.RestoreSnapshot(before);
            Check(loaded.Dimension(d.Id)!.ReferenceB==center,"undo snapshot restores semantics");
            var file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".scpart");
            try{PartFile.Save(file,state);Check(PartFile.Load(file).Sketch.Entities.Count==s.Entities.Count,"real file save/load");}finally{File.Delete(file);}
            void Reject(string bad,string why)
            {
                bool rejected=false;try{PartFile.Deserialize(bad);}catch(Exception ex) when(ex is InvalidDataException or System.Text.Json.JsonException){rejected=true;}
                Check(rejected,why);
            }
            Reject(json.Replace("\"SchemaVersion\": 1","\"SchemaVersion\": 99"),"future schema rejected");
            Reject(json.Replace("\"Units\": \"mm\"","\"Units\": \"inch\""),"unknown unit rejected");
            Reject(json.Replace("\"Depth\": 20","\"Depth\": 0"),"zero depth rejected");
            Reject(json.Replace("\"NextPointId\": "+state.Sketch.NextPointId,"\"NextPointId\": 1"),"ID reuse rejected");
        }
        Console.WriteLine($"PART ROUNDTRIP: {count} PASS (real JSON/file IO and post-open solver; native pending)");
    }
}
