using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace NuonotaPet;

public sealed class Preferences
{
    public string Skin { get; set; } = "original";
    public double Size { get; set; } = 230;
    public double X { get; set; } = double.MinValue;
    public double Y { get; set; } = double.MinValue;
    public bool Wander { get; set; } = false;
    public bool Follow { get; set; } = true;
    public bool Topmost { get; set; } = true;
    public bool Paused { get; set; } = false;
    public int FrameRate { get; set; } = 60;
    public int AppearanceRevision { get; set; } = 0;
    public bool Moonlight { get; set; } = true;
    public bool IdleActions { get; set; } = true;
}

public static class Program
{
    public static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    public static readonly string Data = Path.Combine(Root, "data");
    [STAThread]
    public static void Main(string[] args)
    {
        Directory.CreateDirectory(Data);
        if (args.Contains("--self-test")) { PetWindow.SelfTest(); return; }
        if(args.Contains("--motion-test"))
        {
            var report=Path.Combine(Data,"motion-test.txt");File.WriteAllText(report,"RUNNING");
            try{PetWindow.MotionTest();}catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}return;
        }
        if (args.Contains("--render-test")) { PetWindow.RenderTest(); return; }
        if (args.Contains("--rig-test"))
        {
            var report=Path.Combine(Data,"rig-test.txt");File.WriteAllText(report,"RUNNING");
            try{PetWindow.RigTest();}catch(Exception ex){File.WriteAllText(report,"FAIL: "+ex);Environment.ExitCode=1;}
            return;
        }
        using var mutex = new Mutex(true, "Local\\NuonotaPet.Standalone", out bool first);
        if (!first) return;
        try
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (_, e) =>
            {
                File.AppendAllText(Path.Combine(Data,"error.log"), DateTime.Now+" "+e.Exception+Environment.NewLine);
            };
            app.Run(new PetWindow());
        }
        catch (Exception e)
        {
            File.AppendAllText(Path.Combine(Data,"error.log"), DateTime.Now+" "+e+Environment.NewLine);
            System.Windows.MessageBox.Show("桌宠未能启动。请保留 data/error.log 以便排查。\n"+e.Message,"努昂诺塔");
        }
    }
}

public sealed class PetWindow : Window
{
    private readonly Preferences prefs;
    private readonly System.Windows.Controls.Image sprite = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly Grid body = new();
    private readonly MoonMesh moon = new();
    private readonly MoonAura aura = new();
    private readonly MoonRig rig = new();
    private readonly TranslateTransform floating = new();
    private readonly RotateTransform lean = new();
    private readonly ScaleTransform breathe = new(1,1);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Random random = new();
    private readonly Forms.NotifyIcon tray;
    private readonly ContextMenu menu = new();
    private readonly BitmapSource[][] original;
    private BitmapSource[]? hd;
    private bool dragging, moved, sleeping, passThrough;
    private Point down, start;
    private double lastTick, phase, frameTime, nextBehavior=8, stateEnd, drift;
    private int row, frame;
    private string action = "idle";
    private readonly TextBlock bubble = new() { Text="", Foreground=Brushes.White, FontSize=13, TextAlignment=TextAlignment.Center, Margin=new Thickness(10,4,10,4) };
    private readonly Border bubbleBox;
    private double bubbleEnd;
    private double velocityX,previousLeft,renderStart;
    private double nextRender;
    private int renderCount;
    private static readonly int[] Counts = {6,8,8,4,5,8,6,6,6,8,8};
    private static readonly int[][] Durations = {
        new[]{280,110,110,140,140,320},new[]{120,120,120,120,120,120,120,220},new[]{120,120,120,120,120,120,120,220},
        new[]{140,140,140,280},new[]{140,140,140,140,280},new[]{140,140,140,140,140,140,140,240},
        new[]{150,150,150,150,150,260},new[]{120,120,120,120,120,220},new[]{150,150,150,150,150,280},
        new[]{120,120,120,120,120,120,120,120},new[]{120,120,120,120,120,120,120,120}
    };
    private static BitmapSource Load(string path)
    {
        var b = new BitmapImage(); b.BeginInit(); b.CacheOption=BitmapCacheOption.OnLoad;
        b.UriSource=new Uri(path); b.EndInit(); b.Freeze(); return b;
    }
    private static BitmapSource Crop(BitmapSource image,int x,int y,int w,int h)
    { var b=new CroppedBitmap(image,new Int32Rect(x,y,w,h));b.Freeze();return b; }
    private static BitmapSource[][] LoadOriginal()
    {
        var sheet=Load(Path.Combine(Program.Root,"assets","original","spritesheet.png"));
        if(sheet.PixelWidth!=1536 || sheet.PixelHeight!=2288) throw new InvalidDataException("原版图集尺寸不正确");
        return Enumerable.Range(0,11).Select(r=>Enumerable.Range(0,Counts[r]).Select(c=>Crop(sheet,c*192,r*208,192,208)).ToArray()).ToArray();
    }
    public PetWindow()
    {
        try { prefs=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(Program.Data,"settings.json")))??new(); }
        catch { prefs=new(); }
        prefs.Size=double.IsFinite(prefs.Size)?Math.Clamp(prefs.Size,140,640):360;
        prefs.FrameRate=prefs.FrameRate==30?30:60;
        original=LoadOriginal();
        var faithfulPath=Path.Combine(Program.Root,"assets","moon-spirit","idle.png");
        var hdPath=Path.Combine(Program.Root,"assets","hd","expressions.png");
        if(File.Exists(faithfulPath))
        {
            var spirit=Load(faithfulPath);hd=new[]{spirit,spirit,spirit,spirit};
            if(prefs.AppearanceRevision<2){prefs.Skin="hd";prefs.Size=380;prefs.X=double.MinValue;prefs.Y=double.MinValue;prefs.AppearanceRevision=2;}
        }
        else if(File.Exists(hdPath))
        {
            var sheet=Load(hdPath); int w=sheet.PixelWidth/2,h=sheet.PixelHeight/2;
            hd=Enumerable.Range(0,4).Select(i=>Crop(sheet,(i%2)*w,(i/2)*h,w,h)).ToArray();
        }
        if(prefs.Skin!="original" && hd==null)prefs.Skin="original";
        Title="努昂诺塔 · 独立桌宠"; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true; Background=Brushes.Transparent; ShowInTaskbar=false; ShowActivated=false;
        Topmost=prefs.Topmost; Width=prefs.Size; Height=prefs.Size*1.15+36;
        var layout=new Grid { Background=Brushes.Transparent };
        body.Background=Brushes.Transparent;
        body.Margin=new Thickness(8,30,8,10); body.RenderTransformOrigin=new Point(.5,.65);
        var group=new TransformGroup();group.Children.Add(breathe);group.Children.Add(lean);group.Children.Add(floating);
        body.RenderTransform=group;body.Children.Add(sprite);body.Children.Add(aura);body.Children.Add(moon);body.Children.Add(rig);layout.Children.Add(body);
        body.SizeChanged+=(_,_)=>{moon.Width=body.ActualWidth;moon.Height=body.ActualWidth;aura.Width=body.ActualWidth;aura.Height=body.ActualWidth;rig.Width=body.ActualWidth;rig.Height=body.ActualHeight;};
        // The image itself receives pointer input only on nontransparent character pixels (native hit test below).
        bubbleBox=new Border{Background=new SolidColorBrush(Color.FromArgb(220,38,39,61)),CornerRadius=new CornerRadius(12),
            HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Child=bubble,Visibility=Visibility.Collapsed,IsHitTestVisible=false};
        layout.Children.Add(bubbleBox);Content=layout;ContextMenu=menu;
        RenderOptions.SetBitmapScalingMode(sprite,BitmapScalingMode.HighQuality);
        tray=new Forms.NotifyIcon { Icon=System.Drawing.SystemIcons.Application,Text="努昂诺塔 · 右键打开菜单",Visible=true };
        tray.MouseClick+=(_,e)=>Dispatcher.Invoke(()=>
        {if(e.Button==Forms.MouseButtons.Right){BuildTrayMenu();tray.ContextMenuStrip?.Show(Forms.Cursor.Position);} else {Show();ResetPosition();}});
        MouseLeftButtonDown+=OnDown;MouseMove+=OnMove;MouseLeftButtonUp+=OnUp;
        MouseWheel+=(_,e)=>SetSize(prefs.Size+(e.Delta>0?20:-20));
        ContextMenuOpening+=(_,_)=>BuildMenu();
        SourceInitialized+=(_,_)=>
        {
            var source=(HwndSource)PresentationSource.FromVisual(this);source.AddHook(Hook);
            var hwnd=source.Handle;long style=GetWindowLongPtr(hwnd,-20).ToInt64();
            SetWindowLongPtr(hwnd,-20,new IntPtr(style|0x08000000|0x00000080));
        };
        Loaded+=(_,_)=>{if(prefs.X==double.MinValue || !double.IsFinite(prefs.X)||!double.IsFinite(prefs.Y))ResetPosition();else {Left=prefs.X;Top=prefs.Y;ClampPosition();}previousLeft=Left;BuildMenu();Tick();CompositionTarget.Rendering+=Render;};
        Closed+=(_,_)=>{CompositionTarget.Rendering-=Render;Save();tray.Visible=false;tray.Dispose();};
    }
    private void OnDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ClickCount==2){Interact();return;}
        sleeping=false;SetAction("idle",0);dragging=true;moved=false;down=PointToScreen(e.GetPosition(this));start=new Point(Left,Top);CaptureMouse();
    }
    private void OnMove(object sender,System.Windows.Input.MouseEventArgs e)
    {
        if(!dragging)return;
        var p=PointToScreen(e.GetPosition(this));var dpi=VisualTreeHelper.GetDpi(this);
        double dx=(p.X-down.X)/dpi.DpiScaleX,dy=(p.Y-down.Y)/dpi.DpiScaleY;
        if(Math.Abs(dx)+Math.Abs(dy)>4)moved=true;
        Left=start.X+dx;Top=start.Y+dy;
    }
    private void OnUp(object sender,MouseButtonEventArgs e)
    {
        if(!dragging)return;dragging=false;ReleaseMouseCapture();ClampPosition();Save();
        if(!moved)Interact();
    }
    private void Interact(){sleeping=false;SetAction("happy",3);}
    private void SetAction(string name,double seconds=3)
    {action=name;row=name=="happy"?3:name=="jump"?4:name=="walk"?(drift>=0?1:2):0;frame=0;frameTime=0;stateEnd=clock.Elapsed.TotalSeconds+seconds;}
    private void Render(object? sender,EventArgs e)
    {
        var now=clock.Elapsed.TotalSeconds;
        if(now+.001<nextRender)return;
        nextRender=Math.Max(nextRender+1.0/prefs.FrameRate,now-.04);
        Tick();
        if(now<2)return;
        if(renderStart==0)renderStart=now;
        renderCount++;
        if(now-renderStart>=12)
        {
            File.WriteAllText(Path.Combine(Program.Data,"performance.json"),JsonSerializer.Serialize(new{targetFps=prefs.FrameRate,observedUpdateFps=Math.Round(renderCount/(now-renderStart),1),seconds=Math.Round(now-renderStart,2),skin=prefs.Skin,meshVertices=rig.VertexCount,bones=rig.BoneCount,note="Local update-loop measurement, not a GPU present-time trace"}));
            renderStart=now;renderCount=0;
        }
    }
    private void Tick()
    {
        double now=clock.Elapsed.TotalSeconds,dt=Math.Clamp(now-lastTick,0,.1);lastTick=now;
        if(now>bubbleEnd)bubbleBox.Visibility=Visibility.Collapsed;
        var source=CurrentFrame();sprite.Source=source;
        bool useMesh=prefs.Skin=="hd"&&hd!=null;
        moon.Visibility=Visibility.Hidden;sprite.Opacity=useMesh?0:1;
        aura.Visibility=Visibility.Hidden;rig.Visibility=useMesh?Visibility.Visible:Visibility.Hidden;
        if(useMesh)moon.SetTexture(source);
        if(prefs.Paused)return;
        phase+=dt;
        if(action!="idle" && now>stateEnd)SetAction("idle",0);
        if(!dragging&&!sleeping&&!IsMouseOver&&!menu.IsOpen&&now>nextBehavior&&action=="idle")
        {
            if(prefs.Wander&&random.Next(3)==0){drift=random.Next(2)==0?-1:1;SetAction("walk",random.Next(3,7));}
            else if(prefs.IdleActions){int choice=random.Next(3);SetAction(choice==0?"happy":choice==1?"turn":"jump",choice==0?3:choice==1?4:2);}
            nextBehavior=now+random.Next(18,36);
        }
        if(action=="walk"&&!dragging&&!sleeping&&prefs.Wander&&!IsMouseOver&&!menu.IsOpen)
        {double before=Left;Left+=drift*35*dt;ClampPosition();if(Math.Abs(Left-before)<.01){drift=-drift;row=drift>0?1:2;}}
        frameTime+=dt*1000;
        if(frameTime>=Durations[row][frame]){frameTime-=Durations[row][frame];frame=(frame+1)%Counts[row];}
        floating.Y=Math.Sin(phase*1.7)*(sleeping?1.5:3);
        floating.X=0;
        moon.Opacity=1;
        breathe.ScaleX=1+Math.Sin(phase*1.7)*.008;breathe.ScaleY=1-Math.Sin(phase*1.7)*.006;
        if(action=="jump")
        {
            double progress=Math.Clamp((now-(stateEnd-2))/2,0,1);
            // Weightless ascent and return: this spirit has light ribbons, not jumping legs.
            floating.Y-=Math.Pow(Math.Sin(progress*Math.PI),2)*48;
        }
        if(action=="orbit"){double p=Math.Clamp((now-(stateEnd-4))/4,0,1);floating.X=22*Math.Sin(p*2*Math.PI);floating.Y-=12*(1-Math.Cos(p*2*Math.PI));}
        double target=0;
        if(prefs.Follow&&!sleeping&&!dragging){var p=Forms.Cursor.Position;var center=PointToScreen(new Point(Width/2,Height/2));target=Math.Clamp((p.X-center.X)/170,-2.5,2.5);}
        if(action=="happy")target-=4*Math.Sin(Math.Clamp((now-(stateEnd-3))/3,0,1)*Math.PI);
        lean.Angle+=(target-lean.Angle)*Math.Min(1,dt*5);
        velocityX+=((Left-previousLeft)/Math.Max(dt,.001)-velocityX)*Math.Min(1,dt*8);previousLeft=Left;
        if(useMesh)
        {
            floating.X=0;floating.Y=0;lean.Angle=0;breathe.ScaleX=breathe.ScaleY=1;
            double duration=action=="orbit"?5:action=="turn"?4:action=="jump"?2:3;
            double progress=Math.Clamp((now-(stateEnd-duration))/duration,0,1);
            rig.Animate(phase,dt,sleeping,action,progress,target,velocityX,dragging,prefs.Moonlight);
        }
        sprite.Source=CurrentFrame();
    }
    private BitmapSource CurrentFrame()
    {
        if(prefs.Skin=="hd"&&hd!=null)return hd[sleeping?2:action=="happy"?1:action=="jump"?3:0];
        return original[sleeping?6:row][sleeping?0:frame];
    }
    private void Say(string text,double seconds){bubble.Text=text;bubbleBox.Visibility=Visibility.Visible;bubbleEnd=clock.Elapsed.TotalSeconds+seconds;}
    private void SetSize(double size){double bottom=Top+Height; prefs.Size=Math.Clamp(size,140,640);Width=prefs.Size;Height=prefs.Size*1.15+36;Top=bottom-Height;ClampPosition();Save();}
    private Rect WorkArea()
    {
        var handle=new WindowInteropHelper(this).Handle;
        var screen=Forms.Screen.FromHandle(handle).WorkingArea;
        var source=PresentationSource.FromVisual(this);var transform=source?.CompositionTarget?.TransformFromDevice??Matrix.Identity;
        return new Rect(transform.Transform(new Point(screen.Left,screen.Top)),transform.Transform(new Point(screen.Right,screen.Bottom)));
    }
    private void ResetPosition(){var a=WorkArea();Left=a.Left+35;Top=a.Bottom-Height-20;Save();}
    private void ClampPosition(){var a=WorkArea();Left=Math.Clamp(Left,a.Left,Math.Max(a.Left,a.Right-Width));Top=Math.Clamp(Top,a.Top,Math.Max(a.Top,a.Bottom-Height));}
    private void Save()
    {
        prefs.X=Left;prefs.Y=Top;
        try{var path=Path.Combine(Program.Data,"settings.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(prefs,new JsonSerializerOptions{WriteIndented=true}));File.Move(path+".tmp",path,true);}catch(IOException){}
    }
    private void TogglePass(){passThrough=!passThrough;Say(passThrough?"穿透已开启 · 托盘可关闭":"现在可以拖动我啦",3);BuildMenu();}
    private void BuildMenu()
    {
        menu.Items.Clear();
        Add("努昂诺塔  ☾",()=>{},false,false);
        menu.Items.Add(new Separator());
        Add("低头回应（单击）",Interact);Add("轻轻浮升",()=>{sleeping=false;SetAction("jump",2);});
        Add("侧身回望",()=>{sleeping=false;SetAction("turn",4);});
        Add("轻盈回旋",()=>{sleeping=false;SetAction("orbit",5);});
        Add("垂耳休息 / 醒来",()=>{sleeping=!sleeping;SetAction("idle",0);},true,sleeping);
        Add("自由游走",()=>{prefs.Wander=!prefs.Wander;SetAction("idle",0);nextBehavior=clock.Elapsed.TotalSeconds+1;Save();},true,prefs.Wander);
        Add("偶尔做小动作",()=>{prefs.IdleActions=!prefs.IdleActions;Save();},true,prefs.IdleActions);
        Add("随鼠标转向",()=>{prefs.Follow=!prefs.Follow;Save();},true,prefs.Follow);
        Add("月轮流光",()=>{prefs.Moonlight=!prefs.Moonlight;Save();},true,prefs.Moonlight);
        Add("暂停动画",()=>{prefs.Paused=!prefs.Paused;Save();},true,prefs.Paused);
        Add("省电模式 · 30 FPS",()=>{prefs.FrameRate=prefs.FrameRate==30?60:30;Save();},true,prefs.FrameRate==30);
        menu.Items.Add(new Separator());
        var skins=new MenuItem{Header="切换外观"};
        foreach(var item in new[]{("原版 · 动画图集","original"),("月灵 · 三维骨骼版","hd")})
        {var m=new MenuItem{Header=item.Item1,IsCheckable=true,IsChecked=prefs.Skin==item.Item2,IsEnabled=item.Item2=="original"||hd!=null};m.Click+=(_,_)=>{prefs.Skin=item.Item2;Save();};skins.Items.Add(m);}
        menu.Items.Add(skins);
        var sizes=new MenuItem{Header="大小（也可滚轮调整）"};
        foreach(int s in new[]{200,280,380,480,600}){var m=new MenuItem{Header=s+"（随屏幕缩放）"};m.Click+=(_,_)=>SetSize(s);sizes.Items.Add(m);}menu.Items.Add(sizes);
        Add("始终置顶",()=>{prefs.Topmost=!prefs.Topmost;Topmost=prefs.Topmost;Save();},true,prefs.Topmost);
        Add("鼠标穿透（托盘可恢复）",TogglePass,true,passThrough);
        Add("回到屏幕左下角",ResetPosition);
        menu.Items.Add(new Separator());
        Add("打开素材文件夹",()=>Process.Start(new ProcessStartInfo("explorer.exe",Path.Combine(Program.Root,"assets")){UseShellExecute=true}));
        Add("使用说明",()=>Process.Start(new ProcessStartInfo(Path.Combine(Program.Root,"使用说明.txt")){UseShellExecute=true}));
        Add("退出",Close);
    }
    private void Add(string title,Action run,bool check=false,bool isChecked=false){var m=new MenuItem{Header=title,IsCheckable=check,IsChecked=isChecked};m.Click+=(_,_)=>run();menu.Items.Add(m);}
    private void BuildTrayMenu()
    {
        tray.ContextMenuStrip?.Dispose();var m=new Forms.ContextMenuStrip();
        m.Items.Add("显示桌宠 / 回到屏幕",null,(_,_)=>{Show();ResetPosition();});
        m.Items.Add(passThrough?"关闭鼠标穿透":"开启鼠标穿透",null,(_,_)=>TogglePass());
        m.Items.Add(prefs.Paused?"继续动画":"暂停动画",null,(_,_)=>{prefs.Paused=!prefs.Paused;Save();});
        m.Items.Add("原版外观",null,(_,_)=>{prefs.Skin="original";Save();});
        if(hd!=null)m.Items.Add("原作月灵版",null,(_,_)=>{prefs.Skin="hd";Save();});
        m.Items.Add("退出",null,(_,_)=>Close());tray.ContextMenuStrip=m;
    }
    private IntPtr Hook(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(msg==0x0084) // WM_NCHITTEST: avoid blocking clicks through the transparent canvas.
        {
            if(passThrough){handled=true;return new IntPtr(-1);}
            int packed=unchecked((int)lParam.ToInt64());var p=PointFromScreen(new Point((short)(packed&0xffff),(short)(packed>>16)));
            if(prefs.Skin=="hd")
            {
                bool hit=false;
                VisualTreeHelper.HitTest(rig,null,result=>{if(result is System.Windows.Media.Media3D.RayMeshGeometry3DHitTestResult)hit=true;return HitTestResultBehavior.Stop;},new PointHitTestParameters(TranslatePoint(p,rig)));
                handled=true;return new IntPtr(hit?1:-1);
            }
            var local=TranslatePoint(p,sprite);var b=sprite.Source as BitmapSource;
            if(b!=null&&sprite.ActualWidth>0&&sprite.ActualHeight>0)
            {
                double scale=Math.Min(sprite.ActualWidth/b.PixelWidth,sprite.ActualHeight/b.PixelHeight);
                int x=(int)Math.Floor((local.X-(sprite.ActualWidth-b.PixelWidth*scale)/2)/scale);
                int y=(int)Math.Floor((local.Y-(sprite.ActualHeight-b.PixelHeight*scale)/2)/scale);
                bool transparent=x<0||y<0||x>=b.PixelWidth||y>=b.PixelHeight;
                if(!transparent && (b.Format==PixelFormats.Bgra32||b.Format==PixelFormats.Pbgra32))
                {byte[] px=new byte[4];b.CopyPixels(new Int32Rect(x,y,1,1),px,4,0);transparent=px[3]<18;}
                if(transparent){handled=true;return new IntPtr(-1);}
                handled=true;return new IntPtr(1);
            }
        }
        return IntPtr.Zero;
    }
    public static void SelfTest()
    {
        var frames=LoadOriginal();int total=frames.Sum(a=>a.Length);
        if(total!=73)throw new Exception("Unexpected frame count: "+total);
        foreach(var row in frames)foreach(var frame in row)if(frame.PixelWidth!=192||frame.PixelHeight!=208)throw new Exception("Invalid frame");
        var hdPath=Path.Combine(Program.Root,"assets","hd","expressions.png");
        string extra="HD not present";
        if(File.Exists(hdPath)){var b=Load(hdPath);if(b.PixelWidth%2!=0||b.PixelHeight%2!=0)throw new Exception("Invalid HD grid");extra="HD "+b.PixelWidth+"x"+b.PixelHeight;}
        var faithfulPath=Path.Combine(Program.Root,"assets","moon-spirit","idle.png");
        if(File.Exists(faithfulPath)){var b=Load(faithfulPath);if(b.PixelWidth<512||b.PixelHeight<512)throw new Exception("Spirit texture is too small");extra="Original-reference spirit "+b.PixelWidth+"x"+b.PixelHeight;}
        File.WriteAllText(Path.Combine(Program.Data,"self-test.txt"),"PASS: 73 original frames; dimensions valid; "+extra+"; "+DateTime.Now);
    }
    public static void RenderTest()
    {
        // No desktop window or input: independently verify the production mesh renderer.
        var path=Path.Combine(Program.Root,"assets","moon-spirit","idle.png");
        BitmapSource source;
        if(File.Exists(path))source=Load(path);
        else{var sheet=Load(Path.Combine(Program.Root,"assets","hd","expressions.png"));source=Crop(sheet,0,0,sheet.PixelWidth/2,sheet.PixelHeight/2);}
        var rig=new MoonMesh{Width=480,Height=480};rig.SetTexture(source);
        var aura=new MoonAura{Width=480,Height=480};
        var layout=new Grid{Width=480,Height=480,Background=new SolidColorBrush(Color.FromRgb(27,32,49))};layout.Children.Add(aura);layout.Children.Add(rig);
        layout.Measure(new Size(480,480));layout.Arrange(new Rect(0,0,480,480));layout.UpdateLayout();
        var stopwatch=Stopwatch.StartNew();
        for(int i=0;i<3;i++)
        {
            rig.Animate(i*.45,false,"idle",0,0);aura.Animate(i*.45);layout.UpdateLayout();
            var bitmap=new RenderTargetBitmap(480,480,96,96,PixelFormats.Pbgra32);bitmap.Render(layout);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output=File.Create(Path.Combine(Program.Data,"render-"+i+".png"));encoder.Save(output);
        }
        layout.Background=null;layout.UpdateLayout();
        var alphaTest=new RenderTargetBitmap(480,480,96,96,PixelFormats.Pbgra32);alphaTest.Render(layout);
        var pixel=new byte[4];alphaTest.CopyPixels(new Int32Rect(253,172,1,1),pixel,4,0);
        if(pixel[3]<240)throw new InvalidDataException("Body opacity test failed: alpha="+pixel[3]);
        File.WriteAllText(Path.Combine(Program.Data,"render-test.txt"),"PASS: three distinct production mesh frames; opaque body alpha="+pixel[3]+"/255; offscreen rendering in "+stopwatch.ElapsedMilliseconds+" ms.");
    }
    public static void MotionTest()
    {
        var chain=new AppendageDynamics();chain.Reset();
        for(int i=0;i<12;i++)chain.Update(1.0/60,450,0);
        if(Math.Abs(chain.Bend[0])<Math.Abs(chain.Bend[5])+1)throw new Exception("Tips must lag behind roots");
        for(int i=0;i<60;i++)chain.Update(1.0/60,450,0);
        double before=chain.Bend[5];chain.Update(1.0/60,-450,0);
        if(before<5||chain.Bend[5]<0)throw new Exception("Reversal must retain tip inertia");
        for(int i=0;i<120;i++)chain.Update(1.0/60,-450,0);
        chain.Update(1.0/60,0,0);double released=chain.Bend[5];
        if(released>-5)throw new Exception("Release must retain secondary motion");
        for(int i=0;i<360;i++)chain.Update(1.0/60,0,0);
        if(chain.Bend.Any(v=>Math.Abs(v)>.1))throw new Exception("Chain did not settle");
        for(int i=0;i<90;i++)chain.Update(1.0/60,0,i*3);
        if(Math.Abs(chain.Twist[5])<10)throw new Exception("Spin must drive trailing twist");
        var rig=new MoonRig{Width=640,Height=720};
        var layout=new Grid{Width=640,Height=720,Background=new SolidColorBrush(Color.FromRgb(28,33,49))};layout.Children.Add(rig);
        layout.Measure(new Size(640,720));layout.Arrange(new Rect(0,0,640,720));layout.UpdateLayout();
        var folder=Path.Combine(Program.Data,"drag-review");Directory.CreateDirectory(folder);double speed=0;
        for(int i=0;i<420;i++)
        {
            double t=i/60.0,target=t<.5?0:t<1.5?450:t<2.5?-450:t<3.5?300:0;
            speed+=(target-speed)*8/60.0;
            rig.Animate(t,1.0/60,false,"idle",0,0,speed,t>=.5&&t<3.5,true);layout.UpdateLayout();
            if(i%3!=0)continue;
            var b=new RenderTargetBitmap(640,720,96,96,PixelFormats.Pbgra32);b.Render(layout);
            var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(b));using var file=File.Create(Path.Combine(folder,$"frame-{i/3:D3}.png"));e.Save(file);
        }
        File.WriteAllText(Path.Combine(Program.Data,"motion-test.txt"),$"PASS: tip delay, reversal inertia, release persistence ({released:F1} deg), settling and spin-driven twist; 7-second production-rig drag sequence rendered.");
    }
    public static void RigTest()
    {
        var rig=new MoonRig{Width=640,Height=720};
        var layout=new Grid{Width=640,Height=720,Background=new SolidColorBrush(Color.FromRgb(28,33,49))};layout.Children.Add(rig);
        layout.Measure(new Size(640,720));layout.Arrange(new Rect(0,0,640,720));layout.UpdateLayout();
        foreach(int angle in new[]{0,45,90,180,270})
        {
            rig.Animate(1,.016,false,"idle",0,0,0,false,true,angle);layout.UpdateLayout();
            var bitmap=new RenderTargetBitmap(640,720,96,96,PixelFormats.Pbgra32);bitmap.Render(layout);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(Program.Data,"rig-"+angle+".png"));encoder.Save(output);
        }
        rig.Animate(1,.016,false,"idle",0,0,0,false,true,0);layout.UpdateLayout();
        for(int i=0;i<=60;i++){rig.Animate(i*.05,.05,false,"happy",i/60.0,0,0,false,true,0);rig.ValidateEarSymmetry();}
        rig.Animate(1,.016,false,"idle",0,0,0,false,true,0);layout.UpdateLayout();
        bool hit=false;VisualTreeHelper.HitTest(rig,null,result=>{if(result is System.Windows.Media.Media3D.RayMeshGeometry3DHitTestResult)hit=true;return HitTestResultBehavior.Stop;},new PointHitTestParameters(new Point(320,265)));
        if(!hit)throw new InvalidDataException("3D pointer hit test failed");
        layout.Background=null;layout.UpdateLayout();var alpha=new RenderTargetBitmap(640,720,96,96,PixelFormats.Pbgra32);alpha.Render(layout);
        var px=new byte[4];alpha.CopyPixels(new Int32Rect(320,265,1,1),px,4,0);if(px[3]<250)throw new InvalidDataException("3D body must be opaque");
        var frames=Path.Combine(Program.Data,"orbit-review");Directory.CreateDirectory(frames);
        layout.Background=new SolidColorBrush(Color.FromRgb(28,33,49));
        for(int i=0;i<90;i++)
        {
            rig.Animate(i/18.0,1.0/18,false,"orbit",i/89.0,0,0,false,true);layout.UpdateLayout();
            var b=new RenderTargetBitmap(640,720,96,96,PixelFormats.Pbgra32);b.Render(layout);
            var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(b));using var file=File.Create(Path.Combine(frames,$"frame-{i:D3}.png"));e.Save(file);
        }
        foreach(string action in new[]{"happy","turn","sleep"})
        {
            rig.Animate(0,.016,false,"idle",0,0,0,false,true,0);
            var folder=Path.Combine(Program.Data,action+"-review");Directory.CreateDirectory(folder);
            for(int i=0;i<72;i++)
            {
                rig.Animate(i/18.0,1.0/18,action=="sleep",action,i/71.0,0,0,false,true);layout.UpdateLayout();
                var b=new RenderTargetBitmap(640,720,96,96,PixelFormats.Pbgra32);b.Render(layout);
                var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(b));using var file=File.Create(Path.Combine(folder,$"frame-{i:D3}.png"));e.Save(file);
            }
        }
        var timer=Stopwatch.StartNew();for(int i=0;i<120;i++)rig.Animate(i/60.0,1.0/60,false,"idle",0,0,0,false,true);
        File.WriteAllText(Path.Combine(Program.Data,"rig-test.txt"),$"PASS: {rig.BoneCount} bones; {rig.VertexCount} vertices; five orientations, greeting, glance and flourish rendered; 61 greeting ear symmetry checks; ray hit test passed; body alpha={px[3]}; CPU animation updates {timer.ElapsedMilliseconds/120.0:F2} ms/frame");
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
}
