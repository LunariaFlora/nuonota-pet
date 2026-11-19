using System;
using System.Windows;
using System.Windows.Media;

namespace NuonotaPet;

// Resolution-independent moonlight, drawn behind the opaque spirit texture.
// It is a separate layer, so light travels around the rigid moon instead of
// deforming the moon-metal with the creature's softer appendages.
public sealed class MoonAura : FrameworkElement
{
    private double phase;
    private readonly Pen orbit;
    private readonly Pen[] fine=new Pen[18];
    private readonly Pen[] glow=new Pen[18];
    private readonly Brush star=new SolidColorBrush(Color.FromArgb(245,255,250,223));
    private readonly Brush haze=new SolidColorBrush(Color.FromArgb(32,190,211,255));
    public MoonAura()
    {
        IsHitTestVisible=false;ClipToBounds=false;
        HorizontalAlignment=HorizontalAlignment.Center;VerticalAlignment=VerticalAlignment.Center;
        orbit=MakePen(Color.FromArgb(48,224,229,253),.45);
        for(int i=0;i<fine.Length;i++)
        {
            double f=(i+1.0)/fine.Length;
            fine[i]=MakePen(Color.FromArgb((byte)(18+212*f*f),255,(byte)(231+20*f),(byte)(200+43*f)),.45+.45*f);
            glow[i]=MakePen(Color.FromArgb((byte)(4+27*f*f),190,211,255),2.8);
        }
        star.Freeze();haze.Freeze();
    }
    private static Pen MakePen(Color color,double width)
    {var pen=new Pen(new SolidColorBrush(color),width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};pen.Freeze();return pen;}
    private Point Orbit(double a,double shift=0)
    {
        double x=(.237+shift)*Math.Cos(a),y=(.226+shift)*Math.Sin(a),tilt=-.15;
        double u=.468+x*Math.Cos(tilt)-y*Math.Sin(tilt),v=.238+x*Math.Sin(tilt)+y*Math.Cos(tilt);
        return new Point((.5+(u-.5)/1.06)*ActualWidth,(.5+(v-.5)/1.06)*ActualHeight);
    }
    public void Animate(double time){phase=time;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        if(ActualWidth<=0)return;
        // A restrained persistent orbit; no filled disk or background glow rectangle.
        var geometry=new StreamGeometry();
        using(var ctx=geometry.Open())
        {
            ctx.BeginFigure(Orbit(0),false,true);
            for(int i=1;i<96;i++)ctx.LineTo(Orbit(i*Math.PI*2/96),true,false);
        }
        geometry.Freeze();dc.DrawGeometry(null,orbit,geometry);
        for(int trail=0;trail<2;trail++)
        {
            double head=phase*.34+trail*Math.PI+1.1;
            double length=trail==0?.95:.6;
            for(int i=0;i<fine.Length;i++)
            {
                double a=head-length+length*i/fine.Length,b=head-length+length*(i+1)/fine.Length;
                Point p=Orbit(a),q=Orbit(b);dc.DrawLine(glow[i],p,q);dc.DrawLine(fine[i],p,q);
            }
            var point=Orbit(head);double radius=trail==0?1.2:.8;
            dc.DrawEllipse(haze,null,point,radius*3.5,radius*3.5);
            dc.DrawEllipse(star,null,point,radius,radius);
            if(trail==0){dc.DrawLine(fine[14],new Point(point.X-2.7,point.Y),new Point(point.X+2.7,point.Y));dc.DrawLine(fine[14],new Point(point.X,point.Y-2.7),new Point(point.X,point.Y+2.7));}
        }
    }
}
