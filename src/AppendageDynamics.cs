using System;

namespace NuonotaPet;

// Damped chains retain motion after their driver stops. Later links react to
// earlier links, giving tips a real delay rather than an independent idle sine.
public sealed class AppendageDynamics
{
    public readonly double[] Bend=new double[6],Depth=new double[6],Twist=new double[6];
    readonly double[] bendSpeed=new double[6],depthSpeed=new double[6],twistSpeed=new double[6];
    double previousSpeed,previousYaw;
    bool initialized;
    public void Reset(double yaw=0)
    {
        foreach(var a in new[]{Bend,Depth,Twist,bendSpeed,depthSpeed,twistSpeed})Array.Clear(a);
        previousYaw=yaw;previousSpeed=0;initialized=true;
    }
    public void Update(double dt,double speed,double yaw)
    {
        if(!initialized){previousYaw=yaw;previousSpeed=speed;initialized=true;}
        if(dt<=0)return;
        dt=Math.Min(dt,.08);
        double acceleration=Math.Clamp((speed-previousSpeed)/dt,-4000,4000);
        double turnRate=Math.Clamp(((yaw-previousYaw)%360+540)%360-180,-90,90)/dt;
        previousSpeed=speed;previousYaw=yaw;
        double wind=Math.Clamp(speed*.026+acceleration*.001,-20,20);
        double radians=yaw*Math.PI/180;
        double bend=wind*Math.Cos(radians),depth=wind*Math.Sin(radians);
        double twist=Math.Clamp(-turnRate*.11,-27,27);
        int steps=(int)Math.Ceiling(dt/.008);double h=dt/steps;
        for(int step=0;step<steps;step++)
        {
            // Back-to-front integration avoids frame-rate dependent cascading.
            for(int i=5;i>=0;i--)
            {
                double frequency=13-i*.9;
                Advance(Bend,bendSpeed,i,i==0?bend:Bend[i-1],frequency,h,24);
                Advance(Depth,depthSpeed,i,i==0?depth:Depth[i-1],frequency,h,24);
                Advance(Twist,twistSpeed,i,i==0?twist:Twist[i-1],frequency,h,31);
            }
        }
    }
    static void Advance(double[] value,double[] velocity,int i,double target,double frequency,double dt,double limit)
    {
        velocity[i]+=((target-value[i])*frequency*frequency-2*.64*frequency*velocity[i])*dt;
        value[i]+=velocity[i]*dt;
        if(Math.Abs(value[i])>limit){value[i]=Math.Clamp(value[i],-limit,limit);velocity[i]*=.25;}
    }
}
