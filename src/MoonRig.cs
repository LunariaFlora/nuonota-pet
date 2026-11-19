using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace NuonotaPet;

// Real volumetric model and hierarchical weighted skeleton. The archive has no
// animation clips; motion curves below are reconstructed from recorded gameplay.
public sealed class MoonRig : Viewport3D
{
    sealed class Bone { public string name{get;set;}=""; public int parent{get;set;} public double[] local{get;set;}=Array.Empty<double>(); public double[] inverse{get;set;}=Array.Empty<double>(); }
    sealed class Vertex { public double[] p{get;set;}=Array.Empty<double>(); public double[] uv{get;set;}=Array.Empty<double>(); public double[][] weights{get;set;}=Array.Empty<double[]>(); }
    sealed class Part { public string name{get;set;}=""; public Vertex[] vertices{get;set;}=Array.Empty<Vertex>(); public int[] triangles{get;set;}=Array.Empty<int>(); }
    sealed class Rig { public Bone[] bones{get;set;}=Array.Empty<Bone>(); public Part[] meshes{get;set;}=Array.Empty<Part>(); }
    readonly Rig rig;
    readonly List<(Part data,MeshGeometry3D mesh)> parts=new();
    readonly Matrix3D[] local,inverse,world,skin;
    readonly Model3DGroup model=new();
    readonly AxisAngleRotation3D yaw=new(new Vector3D(0,0,1),0);
    readonly AxisAngleRotation3D roll=new(new Vector3D(0,1,0),0);
    readonly TranslateTransform3D lift=new();
    readonly Model3DGroup effects=new();
    readonly List<(GeometryModel3D model,GeometryModel3D glow,double angle,double radius)> stars=new();
    readonly GeometryModel3D arc=new();
    readonly GeometryModel3D arc2=new();
    double heading,tilt;
    readonly AppendageDynamics secondary=new();
    string lastAction="idle";
    double actionHeading,lastProgress,restBlend;
    readonly SolidColorBrush bodyLight=new(Color.FromRgb(57,55,49));
    readonly Dictionary<MeshGeometry3D,(MeshGeometry3D mesh,Vector3D[] normals)> glowShells=new();
    readonly Dictionary<MeshGeometry3D,int[][]> ribbonSections=new();
    public int VertexCount=>parts.Sum(p=>p.data.vertices.Length);
    public int BoneCount=>rig.bones.Length;
    public double Heading=>heading;
    public void ValidateEarSymmetry()
    {
        foreach(string name in new[]{"Bone_EarA01_","Bone_EarA02_","Bone_EarB01_","Bone_EarC01_","Bone_EarC02_","Bone_EarC03_"})
        {
            int l=Array.FindIndex(rig.bones,b=>b.name==name+"L"),r=Array.FindIndex(rig.bones,b=>b.name==name+"R");
            var a=world[l].Transform(new Point3D());var b=world[r].Transform(new Point3D());
            if(Math.Abs(a.X+b.X)+Math.Abs(a.Y-b.Y)+Math.Abs(a.Z-b.Z)>.003)
                throw new InvalidDataException("Ear mirror mismatch: "+name);
        }
    }
    static Matrix3D Mat(double[] a)=>new(a[0],a[4],a[8],a[12],a[1],a[5],a[9],a[13],a[2],a[6],a[10],a[14],a[3],a[7],a[11],a[15]);
    public MoonRig()
    {
        IsHitTestVisible=false;ClipToBounds=false;
        HorizontalAlignment=HorizontalAlignment.Center;VerticalAlignment=VerticalAlignment.Center;
        Camera=new OrthographicCamera(new Point3D(0,-3,.10),new Vector3D(0,1,0),new Vector3D(0,0,1),1.45);
        rig=JsonSerializer.Deserialize<Rig>(File.ReadAllText(Path.Combine(Program.Root,"assets","model","rig.json")))!;
        local=rig.bones.Select(b=>Mat(b.local)).ToArray();inverse=rig.bones.Select(b=>Mat(b.inverse)).ToArray();
        world=new Matrix3D[rig.bones.Length];skin=new Matrix3D[rig.bones.Length];
        var scene=new Model3DGroup();scene.Children.Add(new AmbientLight(Colors.White));
        var texture=new BitmapImage(new Uri(Path.Combine(Program.Root,"assets","model","Luonnotar","NPC_Others_MoonFairy01_Tex_Diffuse.png")));texture.Freeze();
        var shells=new List<GeometryModel3D>();
        foreach(var part in rig.meshes.OrderBy(p=>p.name=="Body"?0:1))
        {
            var mesh=new MeshGeometry3D{TriangleIndices=new Int32Collection(part.triangles),TextureCoordinates=new PointCollection(part.vertices.Select(v=>new Point(v.uv[0],v.uv[1])))};
            Material material;
            GeometryModel3D? shellVisual=null;
            if(part.name=="Body")
            {
                // Opaque albedo gives a solid body; a restrained emission layer lifts
                // lavender midtones without making the desktop visible through it.
                var m=new MaterialGroup();m.Children.Add(new DiffuseMaterial(new ImageBrush(texture)));
                m.Children.Add(new EmissiveMaterial(bodyLight));material=m;
                var normals=new Vector3D[part.vertices.Length];
                for(int k=0;k<part.triangles.Length;k+=3)
                {
                    int a=part.triangles[k],b=part.triangles[k+1],c=part.triangles[k+2];
                    var p=part.vertices[a].p;var q=part.vertices[b].p;var s=part.vertices[c].p;
                    var n=Vector3D.CrossProduct(new Vector3D(q[0]-p[0],q[1]-p[1],q[2]-p[2]),new Vector3D(s[0]-p[0],s[1]-p[1],s[2]-p[2]));
                    normals[a]+=n;normals[b]+=n;normals[c]+=n;
                }
                for(int k=0;k<normals.Length;k++)if(normals[k].Length>0)normals[k].Normalize();
                var shell=new MeshGeometry3D{TriangleIndices=mesh.TriangleIndices};
                shellVisual=new GeometryModel3D(shell,new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(35,235,240,255))));
                glowShells[mesh]=(shell,normals);
            }
            else if(part.name.StartsWith("Eff_"))
            {
                ribbonSections[mesh]=Enumerable.Range(0,part.vertices.Length)
                    .GroupBy(k=>Math.Round(part.vertices[k].uv[1],4)).Select(g=>g.ToArray()).ToArray();
                var brush=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,0)};
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0,210,220,255),0));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(160,236,239,255),.40));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(235,255,253,218),.52));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(80,215,229,255),.72));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0,210,220,255),1));
                var drawing=new DrawingGroup();drawing.Children.Add(new GeometryDrawing(brush,null,new RectangleGeometry(new Rect(0,0,1,1))));
                var fade=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(0,1)};
                fade.GradientStops.Add(new GradientStop(Colors.Transparent,0));fade.GradientStops.Add(new GradientStop(Colors.White,.20));
                fade.GradientStops.Add(new GradientStop(Colors.White,.73));fade.GradientStops.Add(new GradientStop(Colors.Transparent,1));
                drawing.OpacityMask=fade;material=new DiffuseMaterial(new DrawingBrush(drawing));
            }
            else
            {
                material=new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255,252,218)));
            }
            model.Children.Add(new GeometryModel3D(mesh,material){BackMaterial=material});parts.Add((part,mesh));
            if(shellVisual!=null)shells.Add(shellVisual);
        }
        // Transparent shells must follow opaque geometry, including the moon.
        foreach(var shell in shells)model.Children.Add(shell);
        var transform=new Transform3DGroup();transform.Children.Add(new RotateTransform3D(yaw,new Point3D(0,0,.28)));
        transform.Children.Add(new RotateTransform3D(roll,new Point3D(0,0,.30)));transform.Children.Add(lift);model.Transform=transform;
        arc.Material=arc.BackMaterial=new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(195,255,255,224)));
        arc2.Material=arc2.BackMaterial=new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(125,205,225,255)));
        effects.Children.Add(arc);effects.Children.Add(arc2);
        for(int i=0;i<9;i++)
        {
            var glow=new GeometryModel3D();var glowBrush=new RadialGradientBrush();
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(140,255,250,220),0));
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(35,215,226,255),.35));
            glowBrush.GradientStops.Add(new GradientStop(Colors.Transparent,1));
            glow.Material=glow.BackMaterial=new DiffuseMaterial(glowBrush);effects.Children.Add(glow);
            var g=new GeometryModel3D();var mat=new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(235,255,255,236)));g.Material=g.BackMaterial=mat;
            effects.Children.Add(g);stars.Add((g,glow,i*2.39996,.238+(i%3)*.026));
        }
        model.Children.Add(effects);scene.Children.Add(model);Children.Add(new ModelVisual3D{Content=scene});
        Animate(0,0,false,"idle",0,0,0,false,true);
    }
    static double Smooth(double p)=>p*p*(3-2*p);
    static double Curve(double p,params (double t,double v)[] keys)
    {
        if(p<=keys[0].t)return keys[0].v;
        for(int i=1;i<keys.Length;i++)if(p<=keys[i].t)
        {double u=Smooth((p-keys[i-1].t)/(keys[i].t-keys[i-1].t));return keys[i-1].v+(keys[i].v-keys[i-1].v)*u;}
        return keys[^1].v;
    }
    static double Wrap(double a)=>(a%360+540)%360-180;
    public void Animate(double t,double dt,bool sleeping,string action,double progress,double cursor,double velocity,bool dragging,bool moonlight,double? fixedYaw=null)
    {
        if(lastAction!=action||progress<lastProgress-.05){actionHeading=Wrap(heading);lastAction=action;}
        lastProgress=progress;
        restBlend+=((sleeping?1:0)-restBlend)*(1-Math.Exp(-dt*2.8));
        double envelope=Math.Pow(Math.Sin(Math.Clamp(progress,0,1)*Math.PI),2);
        double greet=action=="happy"?Curve(progress,(0,0),(.14,.15),(.38,1),(.60,.65),(1,0)):0;
        double glance=action=="turn"?Curve(progress,(0,0),(.17,-8),(.46,100),(.62,108),(.9,15),(1,0)):0;
        double turnLead=action=="turn"?Curve(progress,(0,0),(.18,16),(.48,5),(.68,-12),(1,0)):action=="orbit"?Curve(progress,(0,0),(.16,15),(.42,6),(.7,-8),(1,0)):0;
        double flourish=action=="orbit"?Curve(progress,(0,0),(.2,.4),(.45,1),(.7,.65),(1,0)):0;
        double target=dragging?Math.Clamp(velocity*.11,-72,72):action=="walk"?Math.Sign(velocity)*55:cursor*9;
        if(action=="happy")target=actionHeading*(1-Smooth(progress));
        if(action=="turn")target=actionHeading+glance;
        if(action=="orbit")target=actionHeading+Curve(progress,(0,0),(.13,-10),(.34,65),(.58,235),(.83,350),(1,360));
        if(fixedYaw.HasValue)heading=fixedYaw.Value;
        else if(action=="orbit"||action=="turn")heading=target;
        else {heading=Wrap(heading);heading+=Wrap(target-heading)*(1-Math.Exp(-dt*5));}
        yaw.Angle=heading;
        // Fixed angles are offline inspection poses, not real sudden turns.
        if(fixedYaw.HasValue)secondary.Reset(heading);else secondary.Update(dt,velocity,heading);
        double drag=Math.Clamp(velocity/700,-1,1);
        tilt+=(Math.Clamp(-velocity*.018,-16,16)-tilt)*(1-Math.Exp(-dt*7));
        roll.Angle=tilt+(action=="orbit"?Curve(progress,(0,0),(.2,-5),(.42,9),(.68,-6),(1,0)):action=="turn"?Math.Sin(progress*2*Math.PI)*3:0);
        lift.OffsetZ=Math.Sin(t*1.6)*.018+(action=="jump"?.12*envelope:0)+flourish*.065+greet*.009;
        lift.OffsetX=action=="orbit"?Math.Sin(progress*2*Math.PI)*.025:0;
        int shimmer=(int)Math.Round(Math.Sin(t*1.3)*2);
        bodyLight.Color=Color.FromRgb((byte)(57+shimmer),(byte)(55+shimmer),(byte)(49+shimmer));
        for(int i=0;i<rig.bones.Length;i++)
        {
            string n=rig.bones[i].name;double side=n.EndsWith("_L")?1:-1;
            // Axes are expressed in bind-model space. The FBX's left and right
            // local axes are not mirrored, so local +/- angles folded one ear back.
            double opening=0,pitch=0,look=0;double calm=sleeping?.35:1;
            if(n.Contains("Head")){pitch=Math.Sin(t*1.5)*1.0*calm+6*greet+8*restBlend;look=turnLead+(action=="idle"&&!sleeping?cursor*.7:0);}
            if(n.Contains("EarA"))
            {
                opening=side*(n.Contains("01")?9+Math.Sin(t*1.6-.35)*1.5*calm+3*greet:1+Math.Sin(t*1.6-.8)*1.0*calm);
                pitch=(n.Contains("01")?32:10)*restBlend+1.5*greet;
                opening+=secondary.Bend[0]*.10;pitch+=secondary.Depth[0]*.10;look=secondary.Twist[0]*.10;
            }
            if(n.Contains("EarB"))opening=side*Math.Sin(t*1.6-.7)*1.5*calm;
            if(n.Contains("EarC"))
            {
                int segment=n.Contains("03")?3:n.Contains("02")?2:1;
                opening=side*((segment==1?9:segment==2?6:12)+Math.Sin(t*1.6-segment*.5)*2*calm-3*greet-5*flourish);
                pitch=Math.Sin(t*1.6-segment*.55)*1.5*calm+drag*segment*2;
                int link=segment-1;double follow=segment==1?.55:segment==2?.40:.30;
                opening+=secondary.Bend[link]*follow;
                pitch+=secondary.Depth[link]*follow;
                look=secondary.Twist[link]*follow;
            }
            if(n.Contains("ArmA"))opening=side*(Math.Sin(t*1.6-.5)*2*calm-4*greet-6*flourish);
            if(n.Contains("Cape"))pitch=Math.Sin(t*1.6-.8)*2*calm;
            if(n.Contains("BowA")&&!n.EndsWith("_M"))
            {
                int s=int.Parse(n.Substring(n.IndexOf("BowA")+4,2));
                pitch=Math.Sin(t*1.7-s*.55)*2.5*calm+drag*2;
                opening=side*Math.Sin(t*1.35-s*.5)*1.8*calm+flourish*Math.Sin(progress*2*Math.PI-s*.30)*3;
                opening+=secondary.Bend[s-1]*.22;
                pitch+=secondary.Depth[s-1]*.22;
                look=secondary.Twist[s-1]*.22;
            }
            var anim=Matrix3D.Identity;
            anim.Rotate(new Quaternion(inverse[i].Transform(new Vector3D(0,1,0)),opening));
            anim.Rotate(new Quaternion(inverse[i].Transform(new Vector3D(1,0,0)),pitch));
            anim.Rotate(new Quaternion(inverse[i].Transform(new Vector3D(0,0,1)),look));
            world[i]=anim*local[i];int parent=rig.bones[i].parent;if(parent>=0)world[i]*=world[parent];skin[i]=inverse[i]*world[i];
        }
        foreach(var (data,mesh) in parts)
        {
            var pos=new Point3DCollection(data.vertices.Length);
            foreach(var v in data.vertices)
            {
                var p=new Point3D(v.p[0],v.p[1],v.p[2]);double x=0,y=0,z=0,total=0;
                foreach(var w in v.weights){var q=skin[(int)w[0]].Transform(p);x+=q.X*w[1];y+=q.Y*w[1];z+=q.Z*w[1];total+=w[1];}
                pos.Add(total>0?new Point3D(x/total,y/total,z/total):p);
            }
            if(ribbonSections.TryGetValue(mesh,out var sections))
            {
                // Move the strip's centreline, keeping its cross-section and material.
                // The root remains attached; lower sections hang closer together and
                // carry a small travelling bend instead of a straight, wide V.
                double side=data.name.EndsWith("01")?1:-1;
                foreach(var section in sections)
                {
                    double u=Math.Clamp(data.vertices[section[0]].uv[1],0,1);
                    double weight=Smooth(u),cx=section.Average(k=>pos[k].X),cy=section.Average(k=>pos[k].Y);
                    double wave=Math.Sin(t*1.45-u*5.5+side*.30);
                    double dx=-cx*.65*weight+weight*(wave*.016+side*Math.Sin(u*2*Math.PI)*.012);
                    double dy=-(cy-.085)*.52*weight+weight*Math.Sin(t*1.25-u*5.0)*.019;
                    // Apply the shared trailing offset AFTER narrowing, so the
                    // accepted resting width doesn't suppress motion or fan out.
                    int link=Math.Clamp((int)(u*5),0,5);
                    dx-=weight*(secondary.Bend[link]*.0020+secondary.Twist[link]*.0015);
                    dy+=weight*secondary.Depth[link]*.0020;
                    foreach(int k in section){var p=pos[k];pos[k]=new Point3D(p.X+dx,p.Y+dy,p.Z);}
                }
            }
            mesh.Positions=pos;
            if(glowShells.TryGetValue(mesh,out var glow))
            {
                var outer=new Point3DCollection(pos.Count);
                for(int k=0;k<pos.Count;k++)
                {
                    var v=data.vertices[k];var normal=new Vector3D();
                    foreach(var w in v.weights)normal+=skin[(int)w[0]].Transform(glow.normals[k])*w[1];
                    outer.Add(pos[k]+normal*.006);
                }
                glow.mesh.Positions=outer;
            }
        }
        effects.Transform=new ScaleTransform3D(moonlight?1:0,moonlight?1:0,moonlight?1:0);
        arc.Geometry=Trail(t*.60,.250,.65,0);arc2.Geometry=Trail(-t*.41+Math.PI,.266,.80,.025);
        foreach(var star in stars)
        {
            double ang=star.angle+t*.23;double pulse=Math.Pow(.5+.5*Math.Sin(t*2.2+star.angle*2),3);
            double size=.001+.010*pulse;double x=Math.Cos(ang)*star.radius,z=.473+Math.Sin(ang)*star.radius;
            double depth=.225+Math.Sin(ang*2)*.035;star.model.Geometry=Spark(x,depth,z,size);
            double s=.009+.025*pulse;star.glow.Geometry=new MeshGeometry3D{Positions=new Point3DCollection(new[]{new Point3D(x-s,depth+.002,z-s),new Point3D(x+s,depth+.002,z-s),new Point3D(x+s,depth+.002,z+s),new Point3D(x-s,depth+.002,z+s)}),TriangleIndices=new Int32Collection(new[]{0,1,2,0,2,3}),TextureCoordinates=new PointCollection(new[]{new Point(0,1),new Point(1,1),new Point(1,0),new Point(0,0)})};
        }
    }
    static MeshGeometry3D Trail(double start,double radius,double length,double offset)
    {
        var m=new MeshGeometry3D();const int count=36;
        for(int i=0;i<=count;i++)
        {
            double a=start-length+(double)i/count*length,w=.0007+.004*Math.Pow((double)i/count,2);
            foreach(double r in new[]{radius-w,radius+w})m.Positions.Add(new Point3D(Math.Cos(a)*r,.245+offset+Math.Sin(a*2)*.018,.473+Math.Sin(a)*r));
            if(i<count){int k=i*2;foreach(int j in new[]{k,k+1,k+2,k+1,k+3,k+2})m.TriangleIndices.Add(j);}
        }
        return m;
    }
    static MeshGeometry3D Spark(double x,double y,double z,double s)
    {
        var m=new MeshGeometry3D();m.Positions.Add(new Point3D(x,y,z));
        for(int i=0;i<8;i++){double a=i*Math.PI/4,r=i%2==0?s:s*.23;m.Positions.Add(new Point3D(x+Math.Sin(a)*r,y,z+Math.Cos(a)*r));}
        for(int i=0;i<8;i++){m.TriangleIndices.Add(0);m.TriangleIndices.Add(i+1);m.TriangleIndices.Add((i+1)%8+1);}return m;
    }
}
