using System.Globalization;
using System.Text;
using SrcCad.Core.Geometry;
using SrcCad.Core.Sketch;

namespace SrcCad.Core.Interop;

public static class DxfSketch
{
    private sealed record Pair(int Code,string Value);
    private static readonly CultureInfo Inv=CultureInfo.InvariantCulture;

    private static List<Pair> Pairs(string text)
    {
        var lines=text.Replace("\r","").Split('\n');
        var result=new List<Pair>();
        for(var i=0;i+1<lines.Length;i+=2)
        {
            if(!int.TryParse(lines[i].Trim(),NumberStyles.Integer,Inv,out var code))continue;
            result.Add(new Pair(code,lines[i+1].Trim()));
        }
        return result;
    }

    private static double Number(IEnumerable<Pair> pairs,int code,double fallback=0)
        => double.TryParse(pairs.FirstOrDefault(p=>p.Code==code)?.Value,NumberStyles.Float,Inv,out var value)?value:fallback;

    private static int Integer(IEnumerable<Pair> pairs,int code,int fallback=0)
        => int.TryParse(pairs.FirstOrDefault(p=>p.Code==code)?.Value,NumberStyles.Integer,Inv,out var value)?value:fallback;

    private static double UnitScale(IReadOnlyList<Pair> pairs)
    {
        for(var i=0;i+1<pairs.Count;i++)
            if(pairs[i].Code==9&&pairs[i].Value=="$INSUNITS"&&pairs[i+1].Code==70&&
               int.TryParse(pairs[i+1].Value,out var unit))
                return unit switch
                {
                    1=>25.4,   // inch
                    2=>304.8,  // foot
                    4=>1.0,    // millimetre
                    5=>10.0,   // centimetre
                    6=>1000.0, // metre
                    _=>1.0
                };
        return 1.0;
    }

    private static int Point(SketchModel model,Point2d point,double tolerance)
        => model.CreateOrSnapPoint(point,tolerance);

    public static SketchModel Import(string text,PlaneFrame plane,double snapTolerance=1e-6)
    {
        if(string.IsNullOrWhiteSpace(text))throw new InvalidDataException("DXF is empty.");
        var pairs=Pairs(text);
        var scale=UnitScale(pairs);
        var model=new SketchModel(plane);
        var inEntities=false;

        for(var i=0;i<pairs.Count;)
        {
            if(pairs[i].Code==0&&pairs[i].Value=="SECTION")
            {
                var section=i+1<pairs.Count&&pairs[i+1].Code==2?pairs[i+1].Value:"";
                inEntities=section=="ENTITIES";i+=2;continue;
            }
            if(pairs[i].Code==0&&pairs[i].Value=="ENDSEC"){inEntities=false;i++;continue;}
            if(!inEntities||pairs[i].Code!=0){i++;continue;}

            var type=pairs[i].Value;
            var start=++i;
            while(i<pairs.Count&&pairs[i].Code!=0)i++;
            var entity=pairs.GetRange(start,i-start);

            switch(type)
            {
                case "LINE":
                {
                    var a=new Point2d(Number(entity,10)*scale,Number(entity,20)*scale);
                    var b=new Point2d(Number(entity,11)*scale,Number(entity,21)*scale);
                    if(Point2d.Distance(a,b)>1e-9)
                        model.AddLine(Point(model,a,snapTolerance),Point(model,b,snapTolerance));
                    break;
                }
                case "CIRCLE":
                {
                    var center=new Point2d(Number(entity,10)*scale,Number(entity,20)*scale);
                    var radius=Number(entity,40)*scale;
                    if(radius>1e-9)model.AddCircle(Point(model,center,snapTolerance),radius);
                    break;
                }
                case "ARC":
                {
                    var center=new Point2d(Number(entity,10)*scale,Number(entity,20)*scale);
                    var radius=Number(entity,40)*scale;
                    if(radius<=1e-9)break;
                    var startAngle=Number(entity,50)*Math.PI/180.0;
                    var endAngle=Number(entity,51)*Math.PI/180.0;
                    var a=new Point2d(center.X+radius*Math.Cos(startAngle),center.Y+radius*Math.Sin(startAngle));
                    var b=new Point2d(center.X+radius*Math.Cos(endAngle),center.Y+radius*Math.Sin(endAngle));
                    model.AddArc(Point(model,center,snapTolerance),Point(model,a,snapTolerance),Point(model,b,snapTolerance),false);
                    break;
                }
                case "ELLIPSE":
                {
                    var center=new Point2d(Number(entity,10)*scale,Number(entity,20)*scale);
                    var mx=Number(entity,11)*scale;var my=Number(entity,21)*scale;
                    var major=Math.Sqrt(mx*mx+my*my);
                    var ratio=Math.Abs(Number(entity,40,1));
                    var startParam=Number(entity,41,0);var endParam=Number(entity,42,Math.PI*2);
                    if(major>1e-9&&ratio>1e-9&&Math.Abs((endParam-startParam)-Math.PI*2)<1e-4)
                        model.AddEllipse(Point(model,center,snapTolerance),major,major*ratio,Math.Atan2(my,mx));
                    break;
                }
                case "LWPOLYLINE":
                {
                    var flags=Integer(entity,70);
                    var vertices=new List<Point2d>();
                    for(var p=0;p<entity.Count;p++)
                    {
                        if(entity[p].Code!=10)continue;
                        var x=double.Parse(entity[p].Value,Inv)*scale;
                        double y=0;
                        for(var q=p+1;q<entity.Count&&entity[q].Code!=10;q++)
                            if(entity[q].Code==20){y=double.Parse(entity[q].Value,Inv)*scale;break;}
                        vertices.Add(new Point2d(x,y));
                    }
                    for(var p=0;p+1<vertices.Count;p++)
                        if(Point2d.Distance(vertices[p],vertices[p+1])>1e-9)
                            model.AddLine(Point(model,vertices[p],snapTolerance),Point(model,vertices[p+1],snapTolerance));
                    if((flags&1)!=0&&vertices.Count>2&&Point2d.Distance(vertices[^1],vertices[0])>1e-9)
                        model.AddLine(Point(model,vertices[^1],snapTolerance),Point(model,vertices[0],snapTolerance));
                    break;
                }
            }
        }

        if(model.Entities.Count==0)throw new InvalidDataException("DXF contains no supported LINE/CIRCLE/ARC/ELLIPSE/LWPOLYLINE geometry.");
        return model;
    }

    private static void WritePair(StringBuilder b,int code,string value)
    {
        b.AppendLine(code.ToString(Inv));b.AppendLine(value);
    }
    private static string N(double value)=>value.ToString("G17",Inv);

    public static string Export(SketchModel model)
    {
        if(model is null)throw new ArgumentNullException(nameof(model));
        var b=new StringBuilder();
        WritePair(b,0,"SECTION");WritePair(b,2,"HEADER");WritePair(b,9,"$INSUNITS");WritePair(b,70,"4");WritePair(b,0,"ENDSEC");
        WritePair(b,0,"SECTION");WritePair(b,2,"ENTITIES");

        foreach(var entity in model.Entities.Where(e=>!e.Construction))
        {
            switch(entity)
            {
                case SketchLine line:
                {
                    var a=model.Point(line.StartPointId).Position;var c=model.Point(line.EndPointId).Position;
                    WritePair(b,0,"LINE");WritePair(b,8,"SKETCH");
                    WritePair(b,10,N(a.X));WritePair(b,20,N(a.Y));WritePair(b,30,"0");
                    WritePair(b,11,N(c.X));WritePair(b,21,N(c.Y));WritePair(b,31,"0");
                    break;
                }
                case SketchCircle circle:
                {
                    var c=model.Point(circle.CenterPointId).Position;
                    WritePair(b,0,"CIRCLE");WritePair(b,8,"SKETCH");
                    WritePair(b,10,N(c.X));WritePair(b,20,N(c.Y));WritePair(b,30,"0");WritePair(b,40,N(circle.Radius));
                    break;
                }
                case SketchArc arc:
                {
                    var center=model.Point(arc.CenterPointId).Position;
                    var a=model.Point(arc.StartPointId).Position;var c=model.Point(arc.EndPointId).Position;
                    var radius=Point2d.Distance(center,a);
                    var start=Math.Atan2(a.Y-center.Y,a.X-center.X)*180/Math.PI;
                    var end=Math.Atan2(c.Y-center.Y,c.X-center.X)*180/Math.PI;
                    if(arc.Clockwise)(start,end)=(end,start);
                    if(start<0)start+=360;if(end<0)end+=360;
                    WritePair(b,0,"ARC");WritePair(b,8,"SKETCH");
                    WritePair(b,10,N(center.X));WritePair(b,20,N(center.Y));WritePair(b,30,"0");WritePair(b,40,N(radius));
                    WritePair(b,50,N(start));WritePair(b,51,N(end));
                    break;
                }
                case SketchEllipse ellipse:
                {
                    var c=model.Point(ellipse.CenterPointId).Position;
                    var mx=ellipse.RadiusX*Math.Cos(ellipse.RotationRadians);
                    var my=ellipse.RadiusX*Math.Sin(ellipse.RotationRadians);
                    WritePair(b,0,"ELLIPSE");WritePair(b,8,"SKETCH");
                    WritePair(b,10,N(c.X));WritePair(b,20,N(c.Y));WritePair(b,30,"0");
                    WritePair(b,11,N(mx));WritePair(b,21,N(my));WritePair(b,31,"0");
                    WritePair(b,40,N(ellipse.RadiusY/ellipse.RadiusX));WritePair(b,41,"0");WritePair(b,42,N(Math.PI*2));
                    break;
                }
            }
        }
        WritePair(b,0,"ENDSEC");WritePair(b,0,"EOF");
        return b.ToString();
    }
}
