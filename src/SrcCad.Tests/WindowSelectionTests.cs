using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;
internal static class WindowSelectionTests
{
    public static void Run()
    {
        int n=0;void Check(bool ok,string m){if(!ok)throw new Exception(m);n++;}
        foreach(var plane in StandardPlanes.All)
        {
            var s=new SketchModel(plane);var line=s.AddLine(s.CreatePointExact(new(-50,5)),s.CreatePointExact(new(50,5)));
            var circle=s.AddCircle(s.CreatePointExact(new(0,0)),3);
            Point2d[] box={new(-10,-10),new(10,-10),new(10,10),new(-10,10)};
            Check(!s.SelectWindow(box,false).Entities.Contains(line.Id),"window rejects partial line");
            Check(s.SelectWindow(box,true).Entities.Contains(line.Id),"crossing picks partial line");
            Check(s.SelectWindow(box,false).Entities.Contains(circle.Id),"window includes complete circle");
            Check(s.SelectWindow(box,true).Entities.Contains(circle.Id),"crossing includes complete circle");
            var cut=new[]{new Point2d(2,-1),new Point2d(4,-1),new Point2d(4,1),new Point2d(2,1)};
            Check(s.SelectWindow(cut,true).Entities.Contains(circle.Id)&&!s.SelectWindow(cut,false).Entities.Contains(circle.Id),"circle boundary crossing");
            Check(s.SelectWindow(box,false).Points.Contains(circle.CenterPointId),"centers participate in window selection");
        }
        Console.WriteLine($"WINDOW SELECTION: {n} PASS (geometry; Windows drag routing pending)");
    }
}
