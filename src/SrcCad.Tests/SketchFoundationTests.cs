using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Commands;

internal static class SketchFoundationTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool ok,string message) { if(!ok)throw new Exception("Foundation: "+message);count++; }
        bool Near(double a,double b)=>Math.Abs(a-b)<1e-6;
        foreach(var plane in StandardPlanes.All)
        {
            var s=new SketchModel(plane);
            var rectangle=s.AddCornerRectangle(new(20,30),new(120,90),0.001);
            var p=s.CreatePointExact(new(40,50));var q=s.CreatePointExact(new(90,60));
            var c1=s.AddCircle(p,5);var c2=s.AddCircle(q,8);
            var center1=s.HitReference(new(40,50),0.01)!;var center2=s.HitReference(new(90,60),0.01)!;
            var origin=s.HitReference(new(0,0),0.01)!;
            Check(center1.Kind==GeometryReferenceKind.CenterPoint && s.HitReference(new(45,50),0.01)!.Kind==GeometryReferenceKind.Circle,"center/body semantic selection");
            Check(origin.Kind==GeometryReferenceKind.Origin,"Origin first-class hit");
            Check(s.ApplyReferenceRelation(SketchRelationType.Horizontal,new[]{center1,center2}),"center-center horizontal from selection");
            Check(Near(s.Point(p).Position.Y,s.Point(q).Position.Y),"horizontal moves geometry");
            SketchDimension Drive(GeometryReference a,GeometryReference b,Point2d label,double value)
            {
                var session=new SmartDimensionSession();session.Preselect(new[]{a,b});
                var preview=session.Preview(s,label)!;var dim=s.AddInferredDimension(preview);
                Check(s.SetDimensionValue(dim.Id,value),"driving "+preview.Type);
                return dim;
            }
            var corner=s.ReferenceForPoint(s.FindPointNear(new(20,30),0.001));
            // Actual pick -> second pick -> placement path, not direct AddDimension.
            var picks=new SmartDimensionSession();
            Check(!picks.Pick(s,new(0,0),0.01)&&!picks.Pick(s,new(20,30),0.01)&&picks.Pick(s,new(10,-20),0.01),"Origin/corner click workflow");
            var x=s.AddInferredDimension(picks.Preview(s,new(10,-20))!);
            Check(x.Type==SketchDimensionType.HorizontalDistance&&s.SetDimensionValue(x.Id,20),"Origin X through shared session");
            Drive(origin,corner,new(-20,15),30);
            foreach(var id in new[]{rectangle[0],rectangle[1]})
            {
                var d=s.AddInferredDimension(s.InferDimension(new[]{s.ReferenceForEntity(id)},new(150,100))!);
                Check(s.SetDimensionValue(d.Id,id==rectangle[0]?100:60),"rectangle dimensions");
            }
            foreach(var c in new[]{c1,c2})
            {
                var d=s.AddInferredDimension(s.InferDimension(new[]{s.ReferenceForEntity(c.Id)},new(150,100))!);
                Check(s.SetDimensionValue(d.Id,c.Id==c1.Id?10:16),"diameter drives radius");
            }
            Drive(origin,center1,new(20,-20),40);
            var cy=Drive(origin,center1,new(-20,25),50);
            Drive(center1,center2,new(65,110),50);
            Check(s.DefinitionInfo().IsFullyDefined&&s.DefinitionInfo().DegreesOfFreedom==0&&!s.Points.Values.Any(v=>v.Id!=0&&v.Fixed),"rectangle + two circles fully defined without Fix");
            Check(s.SetDimensionValue(cy.Id,55)&&Near(s.Point(p).Position.Y,55)&&Near(s.Point(q).Position.Y,55),"alignment survives subsequent driving edit");
            var snapshot=s.CaptureSnapshot();
            var conflict=s.AddInferredDimension(s.InferDimension(new[]{origin,center1},new(-30,25))!);
            Check(!s.SetDimensionValue(conflict.Id,70)&&Near(s.Point(p).Position.Y,55),"conflicting dimension rolls back geometry");
            s.RestoreSnapshot(snapshot);
            s.DeleteDimension(cy.Id);
            Check(s.DefinitionInfo().DegreesOfFreedom==1&&!s.DefinitionInfo().IsFullyDefined,"rank detects released DOF");
            var v=new SketchModel(plane);var a=v.CreatePointExact(new(20,30));var b=v.CreatePointExact(new(60,70));v.AddCircle(a,5);v.AddCircle(b,5);
            Check(v.ApplyReferenceRelation(SketchRelationType.Vertical,new[]{v.ReferenceForPoint(a),v.ReferenceForPoint(b)})&&Near(v.Point(a).Position.X,v.Point(b).Position.X),"center-center vertical");
            foreach(bool reverse in new[]{false,true})
            foreach(bool vertical in new[]{false,true})
            {
                var m=new SketchModel(plane);
                var la=m.CreatePointExact(new(10,10));var lb=m.CreatePointExact(vertical?new(10,100):new(100,10));
                m.SetPointFixed(la,true);m.SetPointFixed(lb,true);
                var edge=m.AddLine(la,lb);var cp=m.CreatePointExact(new(40,40));m.AddCircle(cp,5);
                var r1=m.ReferenceForPoint(cp);var r2=m.ReferenceForEntity(edge.Id);
                var session=new SmartDimensionSession();
                session.Pick(m,reverse?new(10,10):new(40,40),0.01); // endpoint tests separately; body must be body
                session.Reset();
                var body=vertical?new Point2d(10,55):new Point2d(55,10);
                Check(!session.Pick(m,reverse?body:new(40,40),0.01)&&!session.Pick(m,reverse?new(40,40):body,0.01),"center/line selection both orders");
                var d=m.AddInferredDimension(session.Preview(m,new(150,150))!);
                Check(d.Type==SketchDimensionType.PointLineDistance&&m.SetDimensionValue(d.Id,25),"point-line driving solver");
                Check(Near(vertical?m.Point(cp).Position.X:m.Point(cp).Position.Y,35)&&Near(((SketchCircle)m.Entity(edge.Id+1)!).Radius,5),"line distance drives center and preserves radius");
            }
            var construction=new SketchModel(plane);construction.AddCornerRectangle(new(0,0),new(100,60),0.001);
            var command=new LineSketchCommand(construction,true);command.Begin(new());
            command.Pick(new(new(10,20),-1,0.001,1));command.Pick(new(new(80,20),-1,0.001,2));
            var axis=construction.Lines().Single(l=>l.Construction);
            Check(construction.ReferenceForEntity(axis.Id).Kind==GeometryReferenceKind.ConstructionLine&&construction.HitReference(new(45,20),0.01)!.Kind==GeometryReferenceKind.ConstructionLine,"construction command + body selection");
            Check(construction.TryGetExtrusionProfile(out var loop,out _,out _)&&loop.Count==4,"construction excluded from boundary");
            var midpoint=new GeometryReference(GeometryReferenceKind.Midpoint,axis.Id);
            Check(construction.ResolvePoint(midpoint)==new Point2d(45,20),"midpoint derived from endpoints");
        }

        {
            var s=new SketchModel(StandardPlanes.Front);
            var p0=s.CreatePointExact(new(0,0));var p1=s.CreatePointExact(new(20,0));var line=s.AddLine(p0,p1);
            var offset=s.OffsetEntities(new[]{line.Id},5);
            Check(offset.Count==1&&s.Entity(offset[0]) is SketchLine ol&&Near(s.Point(ol.StartPointId).Position.Y,5),"line offset creates parallel geometry");
            var a0=s.CreatePointExact(new(0,-10));var a1=s.CreatePointExact(new(0,30));var axis=s.AddLine(a0,a1,true);
            var mirrored=s.MirrorEntities(new[]{line.Id},axis.Id);
            Check(mirrored.Count==1&&s.Entity(mirrored[0]) is SketchLine ml&&Near(s.Point(ml.EndPointId).Position.X,-20),"mirror across construction line");
            var linear=s.LinearPatternEntities(new[]{line.Id},1,0,30,3);
            Check(linear.Count==2&&s.Entity(linear[^1]) is SketchLine ll&&Near(s.Point(ll.StartPointId).Position.X,60),"linear sketch pattern");
            var circular=s.CircularPatternEntities(new[]{line.Id},new Point2d(0,0),360,4);
            Check(circular.Count==3,"circular sketch pattern");
        }

        {
            var s=new SketchModel(StandardPlanes.Front);
            var c=s.CreatePointExact(new(0,0));
            var a=s.CreatePointExact(new(10,0));
            var b=s.CreatePointExact(new(0,10));
            var arc=s.AddArc(c,a,b,false);
            Check(s.Arcs(false).Single().Id==arc.Id&&s.FindEntityNear(new(7.071,7.071),0.02)==arc.Id,
                "parametric arc selection");
            Check(s.SampleArc(arc,8).Count==9,"arc sampling preserves endpoints");
        }

        {
            var s=new SketchModel(StandardPlanes.Front);
            var command=new StraightSlotSketchCommand(s);command.Begin(new());
            command.Pick(new(new(-20,0),-1,0.001,1));
            command.Pick(new(new(20,0),-1,0.001,1));
            command.Pick(new(new(0,5),-1,0.001,1));
            Check(s.Lines(false).Count()==2&&s.Arcs(false).Count()==2,"slot creates two lines and two true arcs");
            Check(s.TryGetExtrusionProfile(out var slotLoop,out _,out _)&&slotLoop.Count>20,
                "arc slot is accepted as closed extrusion profile");
        }

        {
            var s=new SketchModel(StandardPlanes.Front);
            var a=s.CreatePointExact(new(0,0));var b=s.CreatePointExact(new(10,0));var target=s.AddLine(a,b);
            var c=s.CreatePointExact(new(20,-10));var d=s.CreatePointExact(new(20,10));s.AddLine(c,d);
            Check(s.ExtendLinesToNearest(new[]{target.Id})==1&&Near(s.Point(b).Position.X,20),"extend line to nearest boundary");
        }

        Console.WriteLine($"SKETCH FOUNDATION: {count} PASS (shared selection, solver, dimension, rank; Windows UI pending)");
    }
}
