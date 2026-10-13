using SrcCad.Core.Geometry;

namespace SrcCad.Core.Sketch;

public sealed partial class SketchModel
{
    private double[] ReferenceRelationResidual(SketchRelation r)
    {
        if(r.Type==SketchRelationType.Symmetric)
        {
            var p=ResolvePoint(r.ReferenceA!);
            var q=ResolvePoint(r.ReferenceB!);
            var axis=(SketchLine)Entity(r.ReferenceC!.Id)!;
            var a=Point(axis.StartPointId).Position;var b=Point(axis.EndPointId).Position;
            var mid=new Point2d((p.X+q.X)/2,(p.Y+q.Y)/2);
            var dx=b.X-a.X;var dy=b.Y-a.Y;
            var length=Math.Max(1e-12,Point2d.Distance(a,b));
            return new[]{
                SignedLineDistance(mid,a,b),
                ((q.X-p.X)*dx+(q.Y-p.Y)*dy)/length
            };
        }
        if(r.Type==SketchRelationType.Tangent)
        {
            var circleB=(SketchCircle)Entity(r.ReferenceB!.Id)!;
            if(r.ReferenceA!.IsLine)
            {
                var line=(SketchLine)Entity(r.ReferenceA.Id)!;
                return new[]{SignedLineDistance(Point(circleB.CenterPointId).Position,Point(line.StartPointId).Position,Point(line.EndPointId).Position)-r.Sense*circleB.Radius};
            }
            var c=(SketchCircle)Entity(r.ReferenceA.Id)!;
            var target=r.InternalTangency?Math.Abs(c.Radius-circleB.Radius):c.Radius+circleB.Radius;
            return new[]{Point2d.Distance(Point(c.CenterPointId).Position,Point(circleB.CenterPointId).Position)-target};
        }
        var a=ResolvePoint(r.ReferenceA!);
        if(r.ReferenceB!.Kind==GeometryReferenceKind.Circle)
        {
            var circle=(SketchCircle)Entity(r.ReferenceB.Id)!;
            return new[]{Point2d.Distance(a,Point(circle.CenterPointId).Position)-circle.Radius};
        }
        if(r.ReferenceB!.IsLine)
        {
            var line=(SketchLine)Entity(r.ReferenceB.Id)!;
            var p=Point(line.StartPointId).Position;var q=Point(line.EndPointId).Position;
            return r.Type==SketchRelationType.Midpoint?new[]{a.X-(p.X+q.X)/2,a.Y-(p.Y+q.Y)/2}:new[]{SignedLineDistance(a,p,q)};
        }
        var b=ResolvePoint(r.ReferenceB!);
        return r.Type switch
        {
            SketchRelationType.Horizontal => new[]{b.Y-a.Y},
            SketchRelationType.Vertical => new[]{b.X-a.X},
            SketchRelationType.Coincident => new[]{b.X-a.X,b.Y-a.Y},
            _ => new[]{double.NaN}
        };
    }
    private IEnumerable<double> ReferenceDimensionResiduals(SketchDimension d)
    {
        yield return SignedReferenceMeasure(d)-d.Sense*d.Value;
        if(d.Type==SketchDimensionType.ParallelLineDistance)
        {
            var a=(SketchLine)Entity(d.ReferenceA!.Id)!;
            var b=(SketchLine)Entity(d.ReferenceB!.Id)!;
            yield return SignedLineDistance(Point(a.EndPointId).Position,
                Point(b.StartPointId).Position,Point(b.EndPointId).Position)-d.Sense*d.Value;
        }
    }
    private double[] ConstraintResiduals()
    {
        var values=new List<double>();
        Point2d A(SketchLine l)=>Point(l.StartPointId).Position;
        Point2d B(SketchLine l)=>Point(l.EndPointId).Position;
        double Len(SketchLine l)=>Point2d.Distance(A(l),B(l));
        foreach(var r in Relations)
        {
            if(r.ReferenceA is not null) {values.AddRange(ReferenceRelationResidual(r));continue;}
            var a=Entity(r.EntityA) as SketchLine;var b=Entity(r.EntityB) as SketchLine;
            switch(r.Type)
            {
                case SketchRelationType.Fixed: break; // fixed parameters are eliminated
                case SketchRelationType.Horizontal: values.Add(B(a!).Y-A(a!).Y);break;
                case SketchRelationType.Vertical: values.Add(B(a!).X-A(a!).X);break;
                case SketchRelationType.Coincident:
                    var p=Point(r.PointA).Position;var q=Point(r.PointB).Position;
                    values.Add(q.X-p.X);values.Add(q.Y-p.Y);break;
                case SketchRelationType.Concentric:
                    var ca=(SketchCircle)Entity(r.EntityA)!;var cb=(SketchCircle)Entity(r.EntityB)!;
                    p=Point(ca.CenterPointId).Position;q=Point(cb.CenterPointId).Position;
                    values.Add(q.X-p.X);values.Add(q.Y-p.Y);break;
                case SketchRelationType.Equal:
                    values.Add(a is not null && b is not null?Len(a)-Len(b):((SketchCircle)Entity(r.EntityA)!).Radius-((SketchCircle)Entity(r.EntityB)!).Radius);break;
                case SketchRelationType.Collinear:
                    values.Add(SignedLineDistance(A(b!),A(a!),B(a!)));
                    values.Add(SignedLineDistance(B(b!),A(a!),B(a!)));break;
                case SketchRelationType.Parallel:
                case SketchRelationType.Perpendicular:
                    var ax=B(a!).X-A(a!).X;var ay=B(a!).Y-A(a!).Y;
                    var bx=B(b!).X-A(b!).X;var by=B(b!).Y-A(b!).Y;
                    values.Add((r.Type==SketchRelationType.Parallel?ax*by-ay*bx:ax*bx+ay*by)/Math.Max(1e-12,Len(a!)*Len(b!)));break;
            }
        }
        foreach(var d in Dimensions.Where(d=>d.Driving))
            if(d.ReferenceA is null) values.Add(Measure(d)-d.Value);
            else values.AddRange(ReferenceDimensionResiduals(d));
        foreach(var r in CenterRectangles)
        {
            var c=Point(r.CenterPointId).Position;
            foreach(var pair in new[]{(r.P0,r.P2),(r.P1,r.P3)})
            {
                var a=Point(pair.Item1).Position;var b=Point(pair.Item2).Position;
                values.Add((a.X+b.X)/2-c.X);values.Add((a.Y+b.Y)/2-c.Y);
            }
        }
        return values.ToArray();
    }
    private static double SignedLineDistance(Point2d p,Point2d a,Point2d b)
        => ((b.X-a.X)*(p.Y-a.Y)-(b.Y-a.Y)*(p.X-a.X))/Math.Max(1e-12,Point2d.Distance(a,b));
    private List<(int Id,int Axis)> SolverVariables()
    {
        var variables=new List<(int,int)>();
        foreach(var p in Points.Values.OrderBy(p=>p.Id).Where(p=>!p.Fixed)) {variables.Add((p.Id,0));variables.Add((p.Id,1));}
        foreach(var c in Circles().OrderBy(c=>c.Id)) variables.Add((c.Id,2));
        return variables;
    }
    private double Variable((int Id,int Axis) v) => v.Axis==2?((SketchCircle)Entity(v.Id)!).Radius:v.Axis==0?Point(v.Id).Position.X:Point(v.Id).Position.Y;
    private void SetVariable((int Id,int Axis) v,double value)
    {
        if(v.Axis==2) {var i=Entities.FindIndex(e=>e.Id==v.Id);Entities[i]=((SketchCircle)Entities[i]) with {Radius=value};}
        else {var p=Point(v.Id);Points[v.Id]=p with {Position=v.Axis==0?new(value,p.Position.Y):new(p.Position.X,value)};}
    }
    private double[,] Jacobian(List<(int Id,int Axis)> vars,double[] residual)
    {
        var j=new double[residual.Length,vars.Count];
        for(int c=0;c<vars.Count;c++)
        {
            var value=Variable(vars[c]);var h=1e-6*Math.Max(1,Math.Abs(value));
            SetVariable(vars[c],value+h);var plus=ConstraintResiduals();
            SetVariable(vars[c],value-h);var minus=ConstraintResiduals();
            SetVariable(vars[c],value);
            for(int r=0;r<residual.Length;r++) j[r,c]=(plus[r]-minus[r])/(2*h);
        }
        return j;
    }
    private bool SolveNumerically()
    {
        var before=CaptureSnapshot();var vars=SolverVariables();double lambda=1e-5;
        for(int iteration=0;iteration<100;iteration++)
        {
            var r=ConstraintResiduals();
            if(r.All(v=>double.IsFinite(v)&&Math.Abs(v)<1e-8) && Circles().All(c=>c.Radius>1e-9)) return true;
            if(vars.Count==0 || r.Any(v=>!double.IsFinite(v))) break;
            var j=Jacobian(vars,r);var n=vars.Count;var a=new double[n,n+1];
            for(int x=0;x<n;x++)
            {
                for(int y=0;y<n;y++) for(int k=0;k<r.Length;k++) a[x,y]+=j[k,x]*j[k,y];
                a[x,x]+=lambda;
                for(int k=0;k<r.Length;k++) a[x,n]-=j[k,x]*r[k];
            }
            var step=SolveLinear(a,n);var original=vars.Select(Variable).ToArray();
            for(int x=0;x<n;x++) SetVariable(vars[x],original[x]+step[x]);
            var next=ConstraintResiduals();
            if(next.All(double.IsFinite) && Circles().All(c=>c.Radius>1e-9) && next.Sum(v=>v*v)<r.Sum(v=>v*v)) lambda=Math.Max(1e-12,lambda/4);
            else {for(int x=0;x<n;x++) SetVariable(vars[x],original[x]);lambda*=10;if(lambda>1e12)break;}
        }
        RestoreSnapshot(before);return false;
    }
    private static double[] SolveLinear(double[,] a,int n)
    {
        for(int c=0;c<n;c++)
        {
            int pivot=c;for(int r=c+1;r<n;r++) if(Math.Abs(a[r,c])>Math.Abs(a[pivot,c])) pivot=r;
            for(int k=c;k<=n;k++) (a[c,k],a[pivot,k])=(a[pivot,k],a[c,k]);
            if(Math.Abs(a[c,c])<1e-20) continue;
            var scale=a[c,c];for(int k=c;k<=n;k++) a[c,k]/=scale;
            for(int r=0;r<n;r++) if(r!=c) {var f=a[r,c];for(int k=c;k<=n;k++)a[r,k]-=f*a[c,k];}
        }
        return Enumerable.Range(0,n).Select(i=>a[i,n]).ToArray();
    }
    private static int Rank(double[,] a)
    {
        int rows=a.GetLength(0),cols=a.GetLength(1),rank=0;
        // Scale each residual row; rank must not depend on length vs angle units.
        for(int r=0;r<rows;r++) {double max=0;for(int c=0;c<cols;c++)max=Math.Max(max,Math.Abs(a[r,c]));if(max>1e-12)for(int c=0;c<cols;c++)a[r,c]/=max;}
        for(int c=0;c<cols && rank<rows;c++)
        {
            int pivot=rank;for(int r=rank+1;r<rows;r++) if(Math.Abs(a[r,c])>Math.Abs(a[pivot,c])) pivot=r;
            if(Math.Abs(a[pivot,c])<1e-7) continue;
            for(int k=c;k<cols;k++) (a[rank,k],a[pivot,k])=(a[pivot,k],a[rank,k]);
            var scale=a[rank,c];for(int k=c;k<cols;k++)a[rank,k]/=scale;
            for(int r=rank+1;r<rows;r++) {var f=a[r,c];for(int k=c;k<cols;k++)a[r,k]-=f*a[rank,k];}
            rank++;
        }
        return rank;
    }
    private double[] RelationDiagnosticResiduals(SketchRelation r)
    {
        if(r.ReferenceA is not null)return ReferenceRelationResidual(r);
        Point2d A(SketchLine l)=>Point(l.StartPointId).Position;
        Point2d B(SketchLine l)=>Point(l.EndPointId).Position;
        double Len(SketchLine l)=>Point2d.Distance(A(l),B(l));
        var a=Entity(r.EntityA) as SketchLine;var b=Entity(r.EntityB) as SketchLine;
        return r.Type switch
        {
            SketchRelationType.Fixed=>Array.Empty<double>(),
            SketchRelationType.Horizontal=>new[]{B(a!).Y-A(a!).Y},
            SketchRelationType.Vertical=>new[]{B(a!).X-A(a!).X},
            SketchRelationType.Coincident=>new[]{
                Point(r.PointB).Position.X-Point(r.PointA).Position.X,
                Point(r.PointB).Position.Y-Point(r.PointA).Position.Y},
            SketchRelationType.Concentric=>new[]{
                Point(((SketchCircle)Entity(r.EntityB)!).CenterPointId).Position.X-Point(((SketchCircle)Entity(r.EntityA)!).CenterPointId).Position.X,
                Point(((SketchCircle)Entity(r.EntityB)!).CenterPointId).Position.Y-Point(((SketchCircle)Entity(r.EntityA)!).CenterPointId).Position.Y},
            SketchRelationType.Equal=>new[]{a is not null&&b is not null
                ?Len(a)-Len(b)
                :((SketchCircle)Entity(r.EntityA)!).Radius-((SketchCircle)Entity(r.EntityB)!).Radius},
            SketchRelationType.Collinear=>new[]{SignedLineDistance(A(b!),A(a!),B(a!)),SignedLineDistance(B(b!),A(a!),B(a!))},
            SketchRelationType.Parallel or SketchRelationType.Perpendicular=>new[]{
                (r.Type==SketchRelationType.Parallel
                    ?(B(a!).X-A(a!).X)*(B(b!).Y-A(b!).Y)-(B(a!).Y-A(a!).Y)*(B(b!).X-A(b!).X)
                    :(B(a!).X-A(a!).X)*(B(b!).X-A(b!).X)+(B(a!).Y-A(a!).Y)*(B(b!).Y-A(b!).Y))/
                Math.Max(1e-12,Len(a!)*Len(b!))},
            _=>Array.Empty<double>()
        };
    }

    private double[] DimensionDiagnosticResiduals(SketchDimension d)
        => d.ReferenceA is null
            ?new[]{Measure(d)-d.Value}
            :ReferenceDimensionResiduals(d).ToArray();

    public SketchDefinitionInfo SolverDefinition()
    {
        var vars=SolverVariables();var residual=ConstraintResiduals();
        if(!ConstraintsSatisfied())
        {
            const double tolerance=1e-6;
            var badRelations=Relations.Where(r=>RelationDiagnosticResiduals(r).Any(v=>!double.IsFinite(v)||Math.Abs(v)>tolerance))
                .Select(r=>$"İlişki #{r.Id}").ToArray();
            var badDimensions=Dimensions.Where(d=>d.Driving&&DimensionDiagnosticResiduals(d).Any(v=>!double.IsFinite(v)||Math.Abs(v)>tolerance))
                .Select(d=>$"Ölçü #{d.Id}").ToArray();
            var details=badRelations.Concat(badDimensions).Take(8).ToArray();
            var text=details.Length==0?"Çakışan ilişkiler / ölçüler":"Çakışma: "+string.Join(", ",details);
            return new(vars.Count,false,true,text);
        }
        var rank=Rank(Jacobian(vars,residual));var dof=vars.Count-rank;
        var redundant=Math.Max(0,residual.Length-rank);
        var text=dof==0?"Tam Tanımlı (0 DOF)":$"Eksik Tanımlı ({dof} DOF)";
        if(redundant>0)text+=$" • {redundant} bağımlı denklem";
        return new(dof,dof==0,false,text,redundant);
    }
}
