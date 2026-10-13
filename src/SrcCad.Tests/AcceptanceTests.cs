using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
using SrcCad.Commands;

internal static class AcceptanceTests
{
    private static int count, failures;
    private static void Require(bool value) { if (!value) throw new Exception("Beklenen geometri/durum sağlanmadı."); }
    private static bool Near(double a, double b) => double.IsFinite(a) && Math.Abs(a-b) < 1e-6;
    private static SketchModel New() => new(StandardPlanes.Front);
    private static SketchLine Line(SketchModel s, double x=0, double y=0, double dx=30, double dy=40)
        => s.AddLine(s.CreatePointExact(new(x,y)),s.CreatePointExact(new(x+dx,y+dy)));
    private static void Reject(Action a) { try { a(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; } throw new Exception("Geçersiz girdi kabul edildi."); }
    private static void Test(string name, Action action)
    {
        count++;
        try { action(); Console.WriteLine($"SM-{count:000} PASS {name}"); }
        catch (Exception ex) { failures++; Console.WriteLine($"SM-{count:000} FAIL {name}: {ex.Message}"); }
    }
    private static string State(SketchModel s) => System.Text.Json.JsonSerializer.Serialize(s.CaptureSnapshot());
    private static void Polygon(SketchModel s, params Point2d[] p)
    {
        var ids=p.Select(s.CreatePointExact).ToArray();
        for(int i=0;i<ids.Length;i++) s.AddLine(ids[i],ids[(i+1)%ids.Length]);
    }
    private static SketchModel Plate()
    {
        var s=New(); s.AddCornerRectangle(new(-100,-100),new(100,100),0.001);return s;
    }
    public static void Run()
    {
        foreach(var plane in StandardPlanes.All)
        {
            Test(plane.Name+" düzlem dönüşümü",()=>{var p=plane.WorldToSketch(plane.SketchToWorld(12,-34));Require(Near(p.X,12)&&Near(p.Y,-34));});
            Test(plane.Name+" sağ elli birim eksenler",()=>Require(Near(plane.U.Length,1)&&Near(plane.V.Length,1)&&(Vector3d.Cross(plane.U,plane.V)-plane.Normal).Length<1e-9));
        }
        Test("NaN nokta reddi",()=>Reject(()=>New().CreatePointExact(new(double.NaN,0))));
        Test("Sonsuz nokta reddi",()=>Reject(()=>New().CreatePointExact(new(0,double.PositiveInfinity))));
        Test("Aynı koordinatta sıfır çizgi reddi",()=>Reject(()=>Line(New(),dx:0,dy:0)));
        Test("Eksik noktalı çizgi reddi",()=>Reject(()=>New().AddLine(12,13)));
        Test("NaN yarıçap reddi",()=>Reject(()=>New().AddCircle(0,double.NaN)));
        Test("Eksik merkezli daire reddi",()=>Reject(()=>New().AddCircle(999,10)));
        foreach(var type in new[]{SketchRelationType.Horizontal,SketchRelationType.Vertical})
            Test(type+" gerçek geometri",()=>{var s=New();var l=Line(s);Require(s.ApplyRelationToSelection(type,new[]{l.Id},Array.Empty<int>()));var a=s.Point(l.StartPointId).Position;var b=s.Point(l.EndPointId).Position;Require(type==SketchRelationType.Horizontal?Near(a.Y,b.Y):Near(a.X,b.X));});
        Test("Çakışık iki nokta",()=>{var s=New();var l=Line(s);Require(s.ApplyRelationToSelection(SketchRelationType.Coincident,Array.Empty<int>(),new[]{0,l.StartPointId}));Require(s.Point(l.StartPointId).Position==new Point2d(0,0));});
        foreach(var type in new[]{SketchRelationType.Parallel,SketchRelationType.Perpendicular,SketchRelationType.Equal,SketchRelationType.Collinear})
            Test(type+" iki çizgi",()=>{var s=New();var a=Line(s,dx:50,dy:0);var b=Line(s,0,80);Require(s.ApplyRelationToSelection(type,new[]{a.Id,b.Id},Array.Empty<int>()));var p=s.Point(b.StartPointId).Position;var q=s.Point(b.EndPointId).Position;Require(type switch {SketchRelationType.Parallel=>Near(p.Y,q.Y),SketchRelationType.Perpendicular=>Near(p.X,q.X),SketchRelationType.Equal=>Near(Point2d.Distance(p,q),50),_=>Near(p.Y,0)&&Near(q.Y,0)});});
        Test("Eşmerkezli iki daire",()=>{var s=New();var a=s.AddCircle(0,10);var c=s.CreatePointExact(new(40,60));var b=s.AddCircle(c,20);Require(s.ApplyRelationToSelection(SketchRelationType.Concentric,new[]{a.Id,b.Id},Array.Empty<int>()));Require(s.Point(c).Position==new Point2d(0,0));});
        Test("Sabitle ve sürüklemeyi engelle",()=>{var s=New();var l=Line(s);s.ApplyRelationToSelection(SketchRelationType.Fixed,Array.Empty<int>(),new[]{l.EndPointId});var p=s.Point(l.EndPointId).Position;s.SetPointPosition(l.EndPointId,new(999,999));Require(s.Point(l.EndPointId).Position==p);
            var r=New(); var ids=r.AddCenterRectangle(new(0,0),new(20,10)); var def=r.CenterRectangles[0];
            r.SetPointFixed(def.P0,true);var fixedPosition=r.Point(def.P0).Position;
            var d=r.AddDimension(SketchDimensionType.LineLength,new(2,2),entityA:ids[0]);var before=State(r);
            Require(!r.SetDimensionValue(d.Id,80)&&State(r)==before&&r.Point(def.P0).Position==fixedPosition);
            Require(!r.TryMovePoint(def.P0,new(999,999)));
        });
        foreach(var type in new[]{SketchRelationType.Parallel,SketchRelationType.Perpendicular,SketchRelationType.Collinear})
            Test(type+" daire seçimi reddi",()=>{var s=New();var a=s.AddCircle(0,10);var b=s.AddCircle(0,20);var before=State(s);Require(!s.ApplyRelationToSelection(type,new[]{a.Id,b.Id},Array.Empty<int>())&&State(s)==before);});
        Test("Eşmerkezli çizgi seçimi reddi",()=>{var s=New();var a=Line(s);var b=Line(s,0,80);Require(!s.ApplyRelationToSelection(SketchRelationType.Concentric,new[]{a.Id,b.Id},Array.Empty<int>()));});
        Test("Eşit karma geometri reddi",()=>{var s=New();var a=Line(s);var b=s.AddCircle(0,20);Require(!s.ApplyRelationToSelection(SketchRelationType.Equal,new[]{a.Id,b.Id},Array.Empty<int>()));});
        Test("Aynı objeye ilişki reddi",()=>{var s=New();var a=Line(s);Require(!s.ApplyRelationToSelection(SketchRelationType.Parallel,new[]{a.Id,a.Id},Array.Empty<int>()));});
        Test("Eksik noktaya sabitle reddi",()=>{var s=New();Require(!s.ApplyRelationToSelection(SketchRelationType.Fixed,Array.Empty<int>(),new[]{999}));});
        foreach(var type in new[]{SketchDimensionType.LineLength,SketchDimensionType.PointDistance,SketchDimensionType.HorizontalDistance,SketchDimensionType.VerticalDistance})
            Test(type+" sürücü ölçü",()=>{var s=New();var l=Line(s);var d=s.AddDimension(type,new(10,10),entityA:l.Id,pointA:l.StartPointId,pointB:l.EndPointId);Require(s.SetDimensionValue(d.Id,75)&&Near(s.Measure(s.Dimension(d.Id)!),75));});
        Test("Çap sürücü ölçüsü",()=>{var s=New();var c=s.AddCircle(0,10);var d=s.AddDimension(SketchDimensionType.CircleDiameter,new(10,10),entityA:c.Id);Require(s.SetDimensionValue(d.Id,60)&&Near(((SketchCircle)s.Entity(c.Id)!).Radius,30));});
        Test("Ters yönlü ikinci çizgi açısı",()=>{var s=New();var a=s.AddLine(0,s.CreatePointExact(new(100,0)));var b=s.AddLine(s.CreatePointExact(new(0,80)),0);var d=s.AddDimension(SketchDimensionType.Angle,new(10,10),entityA:a.Id,entityB:b.Id);Require(s.SetDimensionValue(d.Id,30)&&Near(s.Measure(s.Dimension(d.Id)!),30));});
        foreach(var value in new[]{double.NaN,double.PositiveInfinity,-1.0,0.0})
            Test("Geçersiz ölçü reddi "+value,()=>{var s=New();var l=Line(s);var d=s.AddDimension(SketchDimensionType.LineLength,new(1,1),entityA:l.Id);var before=State(s);Require(!s.SetDimensionValue(d.Id,value)&&State(s)==before);});
        Test("Çakışan ölçü atomik geri alma",()=>{var s=New();var l=Line(s);var a=s.AddDimension(SketchDimensionType.LineLength,new(1,1),entityA:l.Id);var b=s.AddDimension(SketchDimensionType.LineLength,new(2,2),entityA:l.Id);var before=State(s);Require(!s.SetDimensionValue(a.Id,80)&&State(s)==before&&Near(s.Dimension(b.Id)!.Value,50));});
        Test("180 üstü açı reddi",()=>{var s=New();var a=Line(s);var b=Line(s,0,80);var d=s.AddDimension(SketchDimensionType.Angle,new(1,1),entityA:a.Id,entityB:b.Id);var before=State(s);Require(!s.SetDimensionValue(d.Id,220)&&State(s)==before);});
        Test("Snapshot tam geri yükleme",()=>{var s=Plate();var before=State(s);var snap=s.CaptureSnapshot();s.AddCircle(0,10);s.RestoreSnapshot(snap);Require(State(s)==before);});
        Test("Snapshot yineleme",()=>{var s=Plate();var a=s.CaptureSnapshot();s.AddCircle(0,10);var expected=State(s);var b=s.CaptureSnapshot();s.RestoreSnapshot(a);s.RestoreSnapshot(b);Require(State(s)==expected);});
        Test("Silinen nokta ölçüsünü temizle",()=>{var s=New();var l=Line(s);s.AddDimension(SketchDimensionType.PointDistance,new(1,1),pointA:l.StartPointId,pointB:l.EndPointId);s.DeleteEntity(l.Id);Require(s.Dimensions.Count==0);});
        Test("Origin silinemez",()=>{var s=New();s.DeletePoint(0);Require(s.Point(0).Fixed);});
        Test("Ölçü etiketini taşımak geometriyi korur",()=>{var s=New();var l=Line(s);var d=s.AddDimension(SketchDimensionType.LineLength,new(1,1),entityA:l.Id);var p=s.Point(l.EndPointId).Position;s.SetDimensionLabel(d.Id,new(300,400));Require(s.Point(l.EndPointId).Position==p&&Near(s.Dimension(d.Id)!.Value,50));});
        Test("Tek dikdörtgen konturu",()=>Require(Plate().TryGetExtrusionProfile(out _,out _,out _)));
        Test("Elips kapalı ekstrüzyon profili",()=>{var s=New();s.AddEllipse(0,30,15,Math.PI/8);Require(s.TryGetExtrusionProfile(out var outer,out var holes,out var circles)&&outer.Count>=48&&holes.Count==0&&circles.Count==0);});
        Test("Slot çizgi-yay ekstrüzyon profili",()=>{var s=New();var cmd=new StraightSlotSketchCommand(s);cmd.Pick(new(new(-30,0),-1,.001,1));cmd.Pick(new(new(30,0),-1,.001,1));cmd.Pick(new(new(30,10),-1,.001,1));Require(s.Arcs(false).Count()==2&&s.TryGetExtrusionProfile(out _,out _,out _));});
        Test("Üç dairesel delik",()=>{var s=Plate();foreach(var x in new[]{-60.0,0,60})s.AddCircle(s.CreatePointExact(new(x,0)),7.5);Require(s.TryGetExtrusionProfile(out _,out _,out var h)&&h.Count==3);});
        Test("Poligon delik",()=>{var s=Plate();s.AddCornerRectangle(new(-20,-20),new(20,20),0.001);Require(s.TryGetExtrusionProfile(out _,out var h,out _)&&h.Count==1);});
        Test("Kendini kesen kontur reddi",()=>{var s=New();Polygon(s,new(0,0),new(100,100),new(0,100),new(100,0));Require(!s.TryGetExtrusionProfile(out _,out _,out _));});
        Test("Sıfır alan konturu reddi",()=>{var s=New();Polygon(s,new(0,0),new(10,0),new(20,0));Require(!s.TryGetExtrusionProfile(out _,out _,out _));});
        Test("Çakışan poligon delikler reddi",()=>{var s=Plate();s.AddCornerRectangle(new(-50,-50),new(10,10),0.001);s.AddCornerRectangle(new(-10,-10),new(50,50),0.001);Require(!s.TryGetExtrusionProfile(out _,out _,out _));});
        Test("Daire poligon deliğini kesemez",()=>{var s=Plate();s.AddCornerRectangle(new(-20,-20),new(20,20),0.001);s.AddCircle(s.CreatePointExact(new(25,0)),10);Require(!s.TryGetExtrusionProfile(out _,out _,out _));});
        Test("Dış sınıra teğet delik reddi",()=>{var s=Plate();s.AddCircle(s.CreatePointExact(new(90,0)),10);Require(!s.TryGetExtrusionProfile(out _,out _,out _));});
        Test("Açık zincir reddi",()=>{var s=New();Line(s);Require(!s.TryGetExtrusionProfile(out _,out _,out _));});
        Test("Kontur yönlerini normalize et",()=>{var s=New();Polygon(s,new(-100,-100),new(-100,100),new(100,100),new(100,-100));Polygon(s,new(-20,-20),new(-20,20),new(20,20),new(20,-20));Require(s.TryGetExtrusionProfile(out var o,out var h,out _));double Area(List<Point2d> p)=>p.Select((a,i)=>a.X*p[(i+1)%p.Count].Y-p[(i+1)%p.Count].X*a.Y).Sum();Require(Area(o)>0&&Area(h[0])>0);});
        Test("Nokta komutu kalıcı sketch noktası üretir",()=>{var s=New();var c=new PointSketchCommand(s);var before=s.Points.Count;c.Pick(new(new(25,35),-1,0.01,1));Require(s.Points.Count==before+1&&s.Points.Values.Any(p=>p.Position==new Point2d(25,35)));});
        Test("Nokta komutu snap edilen noktayı çoğaltmaz",()=>{var s=New();var id=s.CreatePointExact(new(10,10));var c=new PointSketchCommand(s);var before=s.Points.Count;c.Pick(new(new(10,10),id,0.01,1));Require(s.Points.Count==before);});
        Test("Çizgi komutu geometri üretir",()=>{var s=New();var c=new LineSketchCommand(s);c.Pick(new(new(0,0),0,0.01,1));c.Pick(new(new(30,20),-1,0.01,1));Require(s.Lines().Count()==1);});
        Test("Dikdörtgen iptali geometri üretmez",()=>{var s=New();var c=new CornerRectangleSketchCommand(s);c.Pick(new(new(1,1),-1,0.01,1));c.CancelStep();Require(!c.HasPendingStep&&s.Entities.Count==0);});
        Test("Daire komutu geometri üretir",()=>{var s=New();var c=new CircleSketchCommand(s);c.Pick(new(new(0,0),0,0.01,1));c.Pick(new(new(10,0),-1,0.01,1));Require(s.TryGetSingleCircle(out var circle)&&Near(circle.Radius,10));});
        Test("3 nokta yay geometri üretir",()=>{var s=New();var c=new ThreePointArcCommand(s);c.Pick(new(new(10,0),-1,0.01,1));c.Pick(new(new(0,10),-1,0.01,1));c.Pick(new(new(-10,0),-1,0.01,1));var arc=s.Arcs(false).Single();var pts=s.SampleArc(arc,12);Require(pts.Count>=3&&pts.Any(p=>p.Y>9));});
        Test("Merkez dikdörtgen komutu",()=>{var s=New();var c=new CenterRectangleSketchCommand(s);c.Pick(new(new(0,0),0,0.01,1));c.Pick(new(new(20,10),-1,0.01,1));Require(s.Lines(false).Count()==4&&s.Lines().Count()==6&&s.CenterRectangles.Count==1);});
        Test("Önizleme modeli değiştirmez",()=>{var s=New();var c=new CircleSketchCommand(s);c.Pick(new(new(0,0),0,0.01,1));var before=State(s);Require(c.Preview(new(20,20)).Count==1&&State(s)==before);});
        Console.WriteLine($"ACCEPTANCE: {count-failures}/{count} PASS, {failures} FAIL");
        if(failures>0) throw new Exception($"{failures} acceptance failures");
    }
}
