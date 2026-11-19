using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace NuonotaPet;

// A continuous deformation rig, not frame duplication. Texture identity stays fixed
// while wings, ears, robe and lower ribbons follow distinct smooth motion curves.
public sealed class MoonMesh : Viewport3D
{
    private const int Columns=14, Rows=20;
    private readonly MeshGeometry3D mesh = new();
    private readonly ImageBrush texture = new() { Stretch=Stretch.Fill };
    private readonly Point3D[] rest = new Point3D[(Columns+1)*(Rows+1)];
    private BitmapSource? current;
    public MoonMesh()
    {
        IsHitTestVisible=false;ClipToBounds=false;
        RenderOptions.SetEdgeMode(this,EdgeMode.Aliased);
        HorizontalAlignment=HorizontalAlignment.Center;VerticalAlignment=VerticalAlignment.Center;
        Camera=new OrthographicCamera(new Point3D(0,0,5),new Vector3D(0,0,-1),new Vector3D(0,1,0),2.12);
        var uv=new PointCollection();var triangles=new Int32Collection();var positions=new Point3DCollection();
        for(int y=0;y<=Rows;y++)for(int x=0;x<=Columns;x++)
        {
            var p=new Point3D((double)x/Columns*2-1,1-(double)y/Rows*2,0);
            rest[y*(Columns+1)+x]=p;positions.Add(p);uv.Add(new Point((double)x/Columns,(double)y/Rows));
        }
        for(int y=0;y<Rows;y++)for(int x=0;x<Columns;x++)
        {int a=y*(Columns+1)+x,b=a+1,c=a+Columns+1,d=c+1;triangles.Add(a);triangles.Add(c);triangles.Add(b);triangles.Add(b);triangles.Add(c);triangles.Add(d);}
        mesh.Positions=positions;mesh.TextureCoordinates=uv;mesh.TriangleIndices=triangles;
        // Diffuse + neutral ambient light preserves texture alpha and occludes the
        // halo correctly. Emissive-only material can look additive/see-through.
        var material=new DiffuseMaterial(texture);
        var scene=new Model3DGroup();scene.Children.Add(new AmbientLight(Colors.White));
        scene.Children.Add(new GeometryModel3D(mesh,material){BackMaterial=material});
        Children.Add(new ModelVisual3D{Content=scene});
    }
    public void SetTexture(BitmapSource source){if(ReferenceEquals(current,source))return;current=source;texture.ImageSource=source;}
    private static double Bell(double v,double center,double width)=>Math.Exp(-Math.Pow((v-center)/width,2));
    public void Animate(double t,bool sleeping,string action,double cursor,double dragVelocity)
    {
        double energy=sleeping?.45:action=="happy"?1.55:1;
        var positions=new Point3DCollection(rest.Length);
        foreach(var p in rest)
        {
            double y=(1-p.Y)/2,side=Math.Sign(p.X),edge=Math.Abs(p.X);
            double wing=Bell(y,.62,.18)*Math.Clamp((edge-.10)/.5,0,1);
            double ear=Bell(y,.31,.11)*Math.Clamp((edge-.10)/.5,0,1);
            double hem=Bell(y,.61,.10);
            double ribbon=Math.Clamp((y-.64)/.32,0,1)*Math.Clamp(1-edge/.28,0,1);
            double dx=side*wing*Math.Sin(t*2.4+side*.3)*.035*energy;
            double dy=wing*Math.Sin(t*2.4+side*.3)*.045*energy;
            dx+=Math.Sin(t*1.9-y*3)*hem*.012*energy;
            // Travelling waves: the root stays attached while increasingly delayed motion
            // propagates toward each narrow tail. Not a stiff curved strip bobbing as one.
            dx+=Math.Sin(t*1.65-y*8+side*.5)*ribbon*ribbon*.043*energy;
            dy+=Math.Cos(t*1.65-y*8+side*.5)*ribbon*.009*energy;
            dy+=ear*Math.Sin(t*1.7+side*.4)*.013*energy;
            dx+=cursor*Bell(y,.39,.2)*.006;
            dx-=Math.Clamp(dragVelocity/1600,-.05,.05)*Math.Pow(y,2);
            // Keep the exposed crescent area stable while the softer appendages move.
            if((p.X<-.24&&y<.43)||y<.11){dx=0;dy=0;}
            positions.Add(new Point3D(p.X+dx,p.Y+dy,0));
        }
        mesh.Positions=positions;
    }
}
