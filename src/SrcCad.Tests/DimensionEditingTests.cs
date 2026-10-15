using System.Globalization;
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Core.History;
internal static class DimensionEditingTests
{
    public static void Run()
    {
        int count=0;void Check(bool ok,string m){if(!ok)throw new Exception("Dimension edit: "+m);count++;}
        bool Near(double a,double b)=>Math.Abs(a-b)<1e-6;
        foreach(var plane in StandardPlanes.All)
        {
            var s=new SketchModel(plane);var o=s.CreatePointExact(new(0,0));var a=s.CreatePointExact(new(100,0));var b=s.CreatePointExact(new(70.71067811865,70.71067811865));
            s.SetPointFixed(o,true);s.SetPointFixed(a,true);
            var first=s.AddLine(o,a);var second=s.AddLine(o,b);s.AddRelation(SketchRelationType.Horizontal,first.Id);
            var length=s.AddDimension(SketchDimensionType.LineLength,new(80,80),entityA:second.Id);
            var angle=s.AddDimension(SketchDimensionType.Angle,new(50,20),entityA:first.Id,entityB:second.Id);
            Check(s.SetDimensionValue(angle.Id,45),"45 setup");
            var before=s.CaptureSnapshot();var revision=s.Revision;var old=s.Point(b).Position;
            var edit=new DimensionEditSession(s,angle.Id);
            Check(edit.TryCommit("60",out _),"45 to 60 commit");
            Check(Near(s.Measure(s.Dimension(angle.Id)!),60)&&Point2d.Distance(old,s.Point(b).Position)>1,"angle drives real geometry");
            Check(Near(s.Point(a).Position.Y,0)&&Near(s.Measure(s.Dimension(length.Id)!),100)&&!s.DefinitionInfo().IsConflicting,"horizontal shared vertex and length preserved");
            Check(s.Revision>revision,"successful commit advances revision");
            var after=s.CaptureSnapshot();var history=new TransactionalHistory<SketchSnapshot>();history.Record(before,after);
            Check(history.TryUndo(x=>{s.RestoreSnapshot(x,true);return true;})&&Near(s.Measure(s.Dimension(angle.Id)!),45),"undo angle and geometry");
            Check(history.TryRedo(x=>{s.RestoreSnapshot(x,true);return true;})&&Near(s.Measure(s.Dimension(angle.Id)!),60),"redo angle and geometry");
            foreach(var input in new[]{"","NaN","Infinity","-1","0","180","181","1,234.5"})
            {
                var snapshot=s.CaptureSnapshot();var r=s.Revision;
                Check(!new DimensionEditSession(s,angle.Id).TryCommit(input,out _)&&s.Revision==r&&s.Points.SequenceEqual(snapshot.Points)&&s.Dimensions.SequenceEqual(snapshot.Dimensions),"invalid input atomic: "+input);
            }
            foreach(var input in new[]{"45,5","45.5","0.001","179.999","60"})
                Check(new DimensionEditSession(s,angle.Id).TryCommit(input,out _)&&!s.DefinitionInfo().IsConflicting,"repeat/near-boundary edit "+input);
            var cancelSnapshot=s.CaptureSnapshot();_ = new DimensionEditSession(s,angle.Id);
            Check(s.Points.SequenceEqual(cancelSnapshot.Points)&&s.Dimensions.SequenceEqual(cancelSnapshot.Dimensions),"uncommitted session cancels without mutation");
            var rev=s.Revision;
            Check(!new DimensionEditSession(s,angle.Id).TryCommit("70",out _,_=>false)&&s.Revision==rev&&Near(s.Measure(s.Dimension(angle.Id)!),60),"dependency rejection rolls back candidate");
            Check(new DimensionEditSession(s,length.Id).TryCommit("120",out _)&&Near(s.Measure(s.Dimension(length.Id)!),120)&&Near(s.Measure(s.Dimension(angle.Id)!),60),"linear uses same editor and preserves angle");
            var circle=s.AddCircle(s.CreatePointExact(new(-30,-30)),8);var dia=s.AddDimension(SketchDimensionType.CircleDiameter,new(-70,-60),entityA:circle.Id);
            Check(new DimensionEditSession(s,dia.Id).TryCommit("20",out _)&&Near(((SketchCircle)s.Entity(circle.Id)!).Radius,10),"diameter uses same editor");
        }
        foreach(var type in Enum.GetValues<SketchDimensionType>().Where(t=>t!=SketchDimensionType.Angle))
        {
            var s=new SketchModel(StandardPlanes.Front);SketchDimension d;
            var p=s.CreatePointExact(new(30,40));
            if(type is SketchDimensionType.CircleDiameter or SketchDimensionType.CircleRadius)
            {
                var c=s.AddCircle(p,10);
                d=s.AddDimension(type,new(50,50),entityA:c.Id);
            }
            else if(type==SketchDimensionType.ArcRadius)
            {
                var start=s.CreatePointExact(new(40,40));
                var end=s.CreatePointExact(new(30,50));
                var arc=s.AddArc(p,start,end);
                d=s.AddDimension(type,new(50,50),entityA:arc.Id);
            }
            else if(type is SketchDimensionType.EllipseAxis1Diameter or SketchDimensionType.EllipseAxis2Diameter)
            {
                var ellipse=s.AddEllipse(p,12,6,Math.PI/8);
                d=s.AddDimension(type,new(50,50),entityA:ellipse.Id);
            }
            else if(type==SketchDimensionType.LineLength)
            {
                var l=s.AddLine(0,p);
                d=s.AddDimension(type,new(50,50),entityA:l.Id);
            }
            else if(type is SketchDimensionType.PointLineDistance or SketchDimensionType.ParallelLineDistance)
            {
                var a=s.CreatePointExact(new(-100,0));var b=s.CreatePointExact(new(100,0));s.SetPointFixed(a,true);s.SetPointFixed(b,true);var l=s.AddLine(a,b);
                GeometryReference target=s.ReferenceForPoint(p);
                if(type==SketchDimensionType.ParallelLineDistance)target=s.ReferenceForEntity(s.AddLine(p,s.CreatePointExact(new(80,40))).Id);
                d=s.AddInferredDimension(s.InferDimension(new[]{target,s.ReferenceForEntity(l.Id)},new(50,50))!);
            }
            else
            {
                d=s.AddDimension(type,new(50,50),pointA:0,pointB:p);
            }
            var value=d.Value*1.2;
            Check(value>1e-9&&new DimensionEditSession(s,d.Id).TryCommit(value.ToString(CultureInfo.InvariantCulture),out _)&&
                  Near(s.Measure(s.Dimension(d.Id)!),value),"shared editor audit: "+type);
        }
        var saved=CultureInfo.CurrentCulture;
        try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("tr-TR");Check(DimensionNumbers.TryParse("45,5",SketchDimensionType.Angle,out var comma,out _)&&DimensionNumbers.TryParse("45.5",SketchDimensionType.Angle,out var dot,out _)&&comma==dot,"Turkish decimal policy");}
        finally{CultureInfo.CurrentCulture=saved;}
        Console.WriteLine($"DIMENSION EDITING: {count} PASS (shared transaction, solver, revision, locale; Windows pending)");
    }
}
