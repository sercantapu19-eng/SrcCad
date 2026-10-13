using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Core.Persistence;
using SrcCad.Core.History;

internal static class GeneralConstraintTests
{
    public static void Run()
    {
        int count=0;void Check(bool ok,string message){if(!ok)throw new Exception("General constraints: "+message);count++;}
        bool Near(double x,double y)=>Math.Abs(x-y)<1e-6;
        foreach(var plane in StandardPlanes.All)
        {
            var s=new SketchModel(plane);var a=s.CreatePointExact(new(10,10));var b=s.CreatePointExact(new(110,10));s.SetPointFixed(a,true);
            var line=s.AddLine(a,b,true);s.AddRelation(SketchRelationType.Horizontal,line.Id);
            var center=s.CreatePointExact(new(50,30));var circle=s.AddCircle(center,5);
            Check(s.ApplyReferenceRelation(SketchRelationType.Midpoint,new[]{s.ReferenceForEntity(line.Id),s.ReferenceForPoint(center)}),"point-like line midpoint reversed selection");
            Check(Near(s.Point(center).Position.X,(s.Point(a).Position.X+s.Point(b).Position.X)/2)&&Near(s.Point(center).Position.Y,10),"midpoint actual geometry");
            var length=s.AddInferredDimension(s.InferDimension(new[]{s.ReferenceForEntity(line.Id)},new(50,50))!);
            Check(s.SetDimensionValue(length.Id,140)&&Near(s.Point(center).Position.X,80),"midpoint follows subsequent line edit");
            var snapshot=s.CaptureSnapshot();
            var diameter=s.AddInferredDimension(s.InferDimension(new[]{s.ReferenceForEntity(circle.Id)},new(60,20))!);
            Check(s.SetDimensionValue(diameter.Id,10),"radius preserved by dimension");
            Check(!s.ApplyReferenceRelation(SketchRelationType.Tangent,new[]{s.ReferenceForEntity(line.Id),s.ReferenceForEntity(circle.Id)})&&Near(s.Point(center).Position.Y,10),"incompatible midpoint and tangent rejected atomically");
            s.RestoreSnapshot(snapshot);s.RemoveRelation(s.Relations.Last().Id);
            diameter=s.AddInferredDimension(s.InferDimension(new[]{s.ReferenceForEntity(circle.Id)},new(60,20))!);
            s.SetDimensionValue(diameter.Id,10);
            Check(s.ApplyReferenceRelation(SketchRelationType.Tangent,new[]{s.ReferenceForEntity(circle.Id),s.ReferenceForEntity(line.Id)}),"line-circle tangent from zero initial distance");
            Check(Near(Math.Abs(s.Point(center).Position.Y-10),5),"tangent center clearance");
            Check(s.SetDimensionValue(diameter.Id,16)&&Near(Math.Abs(s.Point(center).Position.Y-10),8),"tangent survives diameter edit");
            var loaded=PartFile.Deserialize(PartFile.Serialize(new ExtrusionState(plane,s.CaptureSnapshot(),null)));
            var copy=new SketchModel(plane);copy.RestoreSnapshot(loaded.Sketch);
            Check(copy.SetDimensionValue(diameter.Id,20)&&Near(Math.Abs(copy.Point(center).Position.Y-10),10),"tangent semantic round-trip");
            var t=new SketchModel(plane);var p=t.CreatePointExact(new(10,10));var q=t.CreatePointExact(new(35,10));
            var c=t.AddCircle(p,5);var d=t.AddCircle(q,5);
            foreach(var e in new[]{c,d}) {var dim=t.AddDimension(SketchDimensionType.CircleDiameter,new(0,0),entityA:e.Id);t.SetDimensionValue(dim.Id,10);}
            Check(t.ApplyReferenceRelation(SketchRelationType.Tangent,new[]{t.ReferenceForEntity(c.Id),t.ReferenceForEntity(d.Id)})&&Near(Point2d.Distance(t.Point(p).Position,t.Point(q).Position),10),"external circle tangent");
            int n=t.Relations.Count;Check(t.ApplyReferenceRelation(SketchRelationType.Tangent,new[]{t.ReferenceForEntity(d.Id),t.ReferenceForEntity(c.Id)})&&t.Relations.Count==n,"reversed duplicate does not accumulate");

            var internalTangent=new SketchModel(plane);
            var outerCenter=internalTangent.CreatePointExact(new(0,0));
            var innerCenter=internalTangent.CreatePointExact(new(12,0));
            var outerCircle=internalTangent.AddCircle(outerCenter,20);
            var innerCircle=internalTangent.AddCircle(innerCenter,8);
            internalTangent.SetPointFixed(outerCenter,true);
            Check(internalTangent.ApplyReferenceRelation(SketchRelationType.Tangent,
                new[]{internalTangent.ReferenceForEntity(outerCircle.Id),internalTangent.ReferenceForEntity(innerCircle.Id)}),
                "internal circle tangent relation applies");
            Check(internalTangent.Relations.Last().InternalTangency&&
                  Near(Point2d.Distance(internalTangent.Point(outerCenter).Position,internalTangent.Point(innerCenter).Position),12),
                "internal tangent uses radius difference");

            var symmetric=new SketchModel(plane);
            var ax0=symmetric.CreatePointExact(new(0,-100));var ax1=symmetric.CreatePointExact(new(0,100));
            symmetric.SetPointFixed(ax0,true);symmetric.SetPointFixed(ax1,true);
            var axis=symmetric.AddLine(ax0,ax1,true);
            var sp=symmetric.CreatePointExact(new(-30,10));var sq=symmetric.CreatePointExact(new(15,40));
            Check(symmetric.ApplyReferenceRelation(SketchRelationType.Symmetric,
                new[]{symmetric.ReferenceForPoint(sp),symmetric.ReferenceForPoint(sq),symmetric.ReferenceForEntity(axis.Id)}),
                "point symmetry about construction centerline applies");
            var spa=symmetric.Point(sp).Position;var spb=symmetric.Point(sq).Position;
            Check(Near((spa.X+spb.X)/2,0)&&Near(spa.Y,spb.Y),
                "symmetric points mirror around vertical centerline");
            var symmetricRoundtrip=PartFile.Deserialize(PartFile.Serialize(new ExtrusionState(plane,symmetric.CaptureSnapshot(),null)));
            var symmetricCopy=new SketchModel(plane);symmetricCopy.RestoreSnapshot(symmetricRoundtrip.Sketch);
            var symmetricRelation=symmetricCopy.Relations.Single(r=>r.Type==SketchRelationType.Symmetric);
            Check(symmetricRelation.ReferenceC?.Id==axis.Id&&symmetricCopy.ConstraintsSatisfied(),
                "symmetric third reference survives save/open");

            var u=new SketchModel(plane);p=u.CreatePointExact(new(5,5));q=u.CreatePointExact(new(30,20));var edge=u.AddLine(p,q);
            Check(u.ApplyReferenceRelation(SketchRelationType.Coincident,new[]{u.ReferenceForPoint(0),u.ReferenceForEntity(edge.Id)}),"Origin coincident to arbitrary line");
            var pa=u.Point(p).Position;var pb=u.Point(q).Position;
            Check(Near(pa.X*pb.Y-pa.Y*pb.X,0),"Origin lies on line support");
            var oblique=new SketchModel(plane);
            var oa=oblique.CreatePointExact(new(10,10));var ob=oblique.CreatePointExact(new(60,60));
            oblique.SetPointFixed(oa,true);oblique.SetPointFixed(ob,true);
            var ol=oblique.AddLine(oa,ob);var op=oblique.CreatePointExact(new(40,20));oblique.AddCircle(op,5);
            var od=oblique.AddInferredDimension(oblique.InferDimension(new[]{oblique.ReferenceForPoint(op),oblique.ReferenceForEntity(ol.Id)},new(70,20))!);
            Check(oblique.SetDimensionValue(od.Id,25)&&Near(Math.Abs(oblique.Point(op).Position.Y-oblique.Point(op).Position.X)/Math.Sqrt(2),25),"oblique point-line true perpendicular distance");
            var freedom=oblique.DefinitionInfo().DegreesOfFreedom;
            oblique.AddInferredDimension(oblique.Dimension(od.Id)!);
            Check(oblique.DefinitionInfo().DegreesOfFreedom==freedom&&oblique.DefinitionInfo().RedundantEquationCount>=1,"redundant equation does not consume extra DOF");
            var zero=new SketchModel(plane);var zp=zero.CreatePointExact(new(0,20));zero.AddCircle(zp,5);
            var zd=zero.AddInferredDimension(zero.InferDimension(new[]{zero.ReferenceForPoint(0),zero.ReferenceForPoint(zp)},new(0,40))!);
            Check(zd.Type==SketchDimensionType.HorizontalDistance&&zero.SetDimensionValue(zd.Id,0),"zero X dimension on aligned points");
            Check(zero.SetDimensionValue(zd.Id,15)&&Near(zero.Point(zp).Position.X,15),"zero axis dimension can drive away from axis");

            var conflict=new SketchModel(plane);
            var cf0=conflict.CreatePointExact(new(0,0));var cf1=conflict.CreatePointExact(new(20,0));
            conflict.SetPointFixed(cf0,true);conflict.SetPointFixed(cf1,true);
            var cfLine=conflict.AddLine(cf0,cf1);
            var cfDim=conflict.AddDimension(SketchDimensionType.LineLength,new(10,10),entityA:cfLine.Id);
            var cfSnap=conflict.CaptureSnapshot();
            conflict.RestoreSnapshot(cfSnap with{Dimensions=cfSnap.Dimensions.Select(d=>d.Id==cfDim.Id?d with{Value=25}:d).ToArray()});
            var cfInfo=conflict.DefinitionInfo();
            Check(cfInfo.IsConflicting&&cfInfo.Text.Contains($"Ölçü #{cfDim.Id}",StringComparison.Ordinal),
                "conflict diagnostic names the offending dimension id");
        }
        Console.WriteLine($"GENERAL CONSTRAINTS: {count} PASS (midpoint, point-line, tangency, persistence, conflict ids)");
    }
}
