using System;
using System.Windows;
using System.Windows.Media;

namespace Digitone
{
    internal sealed class AudioScene : FrameworkElement
    {
        private double[] samples;
        private readonly double[] levels = new double[3];
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private double lastFrameTime;
        internal double[] BandLevels { get { return (double[])levels.Clone(); } }
        internal string Mode = "Spectrum";
        internal Brush Accent = Brushes.White;
        internal bool Rainbow;
        internal int RenderCount;
        private static readonly Color[] PrideColors={Color.FromRgb(226,58,50),Color.FromRgb(243,152,45),Color.FromRgb(255,223,59),Color.FromRgb(85,189,89),Color.FromRgb(74,146,237),Color.FromRgb(155,77,202)};
        private static readonly Brush[] PrideBrushes=CreatePrideBrushes();
        private readonly TranslateTransform rainbowShift=new TranslateTransform();
        private readonly LinearGradientBrush rainbowBrush;
        private static readonly double[,] Cos = new double[32,384], Sin = new double[32,384];
        static AudioScene()
        {
            for (int b=0;b<32;b++) for (int i=0;i<384;i++)
            {
                double phase = 2*Math.PI*(40*Math.Pow(90,b/31.0))*i/8000;
                double window = .5-.5*Math.Cos(2*Math.PI*i/383);
                Cos[b,i] = Math.Cos(phase)*window; Sin[b,i] = Math.Sin(phase)*window;
            }
        }
        internal AudioScene()
        {
            rainbowBrush=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(.5,0),SpreadMethod=GradientSpreadMethod.Repeat,RelativeTransform=rainbowShift};for(int i=0;i<PrideColors.Length;i++)rainbowBrush.GradientStops.Add(new GradientStop(PrideColors[i],i/(double)(PrideColors.Length-1)));
        }
        private static Brush[] CreatePrideBrushes(){var brushes=new Brush[PrideColors.Length];for(int i=0;i<brushes.Length;i++){var brush=new SolidColorBrush(PrideColors[i]);brush.Freeze();brushes[i]=brush;}return brushes;}
        internal void SetFrame(double[] frame, double[] bands = null)
        {
            samples = frame;
            double now=clock.Elapsed.TotalSeconds,dt=Math.Min(.1,Math.Max(.001,now-lastFrameTime)); lastFrameTime=now;
            for(int i=0;i<3;i++)
            {
                double target=frame==null || bands==null?0:Math.Min(1,Math.Sqrt(Math.Max(0,bands[i]))*1.6);
                if(frame==null) levels[i]=0;
                else levels[i]+=(target-levels[i])*(1-Math.Exp(-dt/(target>levels[i]?.045:.16)));
            }
            InvalidateVisual();
        }
        private static StreamGeometry Line(Point[] points, bool close)
        {
            var shape = new StreamGeometry();
            using(var g=shape.Open()) { g.BeginFigure(points[0],false,close); for(int i=1;i<points.Length;i++) g.LineTo(points[i],true,false); }
            shape.Freeze(); return shape;
        }
        private Brush MovingRainbow(){rainbowShift.X=(clock.Elapsed.TotalSeconds*.22)%1;return rainbowBrush;}
        private Brush PrideBand(int index){return PrideBrushes[(index+(int)(clock.Elapsed.TotalSeconds*7))%PrideBrushes.Length];}
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); RenderCount++;
            double w=ActualWidth,h=ActualHeight; if(w<1 || h<1) return;
            dc.PushClip(new RectangleGeometry(new Rect(0,0,w,h)));
            Brush liveAccent=Rainbow?MovingRainbow():Accent;var pen = new Pen(liveAccent,2); double energy=0;
            if(samples!=null) { foreach(double n in samples) energy+=n*n; energy=Math.Min(1,Math.Sqrt(energy/samples.Length)*3); }
            if(Mode=="Spectrum")
            {
                double baseline=Math.Max(2,h-28);
                for(int b=0;b<32;b++)
                {
                    double real=0,imag=0;
                    if(samples!=null) for(int i=0;i<Math.Min(384,samples.Length);i++) { real+=samples[i]*Cos[b,i]; imag+=samples[i]*Sin[b,i]; }
                    double level=Math.Min(1,Math.Sqrt(real*real+imag*imag)/384*12);
                    double bar=Math.Max(2,level*baseline*.85),step=w/32;
                    dc.DrawRoundedRectangle(Rainbow?PrideBand(b/5):Accent,null,new Rect(b*step+step*.14,baseline-bar,step*.72,bar),2,2);
                }
                SpectrumAxis(dc,w,h,baseline);
            }
            else if(Mode=="Radial")
            {
                var points=new Point[192]; double radius=Math.Min(w,h)*.28;
                for(int i=0;i<points.Length;i++) { double a=i*2*Math.PI/points.Length; double n=samples==null?0:samples[i*samples.Length/points.Length]; double r=radius+n*radius*.65; points[i]=new Point(w/2+Math.Cos(a)*r,h/2+Math.Sin(a)*r); }
                dc.DrawGeometry(null,pen,Line(points,true));
                dc.PushOpacity(.2); dc.DrawEllipse(null,new Pen(liveAccent,1),new Point(w/2,h/2),radius*.78,radius*.78); dc.Pop();
            }
            else if(Mode=="Prism")
            {
                double span=Math.Max(1,Math.Min(w,h-38));
                for(int i=2;i>=0;i--) { double radius=span*(.12+i*.095+levels[i]*.07); dc.PushOpacity(.9-i*.18); dc.DrawGeometry(null,new Pen(Rainbow?PrideBand(i*2):Accent,2),Diamond(w/2,(h-30)/2,radius)); dc.Pop(); }
                dc.PushOpacity(.12+energy*.16); dc.DrawGeometry(liveAccent,null,Diamond(w/2,(h-30)/2,span*.06)); dc.Pop();
            }
            else
            {
                bool stacked=h>w*.95;
                double cellWidth=stacked?w:w/3, cellHeight=stacked?h/3:h;
                string[] names={ "BASS", "MIDS", "TREBLE" };
                string[] ranges={ "20-250 Hz", "250-4,000 Hz", "4,000-20,000 Hz" };
                for(int i=0;i<3;i++)
                {
                    double x=stacked?w/2:w*(i+.5)/3, top=stacked?cellHeight*i:0;
                    double y=top+(cellHeight-42)/2;
                    double room=Math.Max(1,Math.Min(cellWidth*.42,(cellHeight-52)*.45));
                    double radius=room*(.55+levels[i]*.36);
                    Brush bandAccent=Rainbow?PrideBand(i*2):Accent;
                    dc.PushOpacity(.18); dc.DrawEllipse(null,new Pen(bandAccent,1),new Point(x,y),room,room); dc.Pop();
                    dc.DrawEllipse(null,new Pen(bandAccent,2.5),new Point(x,y),radius,radius);
                    dc.PushOpacity(.24+levels[i]*.5); dc.DrawEllipse(null,new Pen(bandAccent,1),new Point(x,y),radius*.78,radius*.78); dc.Pop();
                    dc.PushOpacity(.12+levels[i]*.3); dc.DrawGeometry(bandAccent,null,Diamond(x,y,room*.18)); dc.Pop();
                    var label=new FormattedText(names[i]+"\n"+ranges[i],System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,bandAccent,VisualTreeHelper.GetDpi(this).PixelsPerDip) { TextAlignment=TextAlignment.Center };
                    dc.DrawText(label,new Point(x,top+cellHeight-35));
                }
            }
            dc.Pop();
        }
        private void SpectrumAxis(DrawingContext dc,double w,double h,double baseline)
        {
            double[] frequencies={40,100,250,500,1000,2000,3600};
            string[] labels={"40 Hz","100","250","500","1k","2k","3.6k Hz"};
            dc.PushOpacity(.7);
            dc.DrawLine(new Pen(Accent,1),new Point(0,baseline),new Point(w,baseline));
            for(int i=0;i<frequencies.Length;i++)
            {
                double x=(.5+31*Math.Log(frequencies[i]/40)/Math.Log(90))*w/32;
                var text=new FormattedText(labels[i],System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,Accent,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawLine(new Pen(Accent,1),new Point(x,baseline),new Point(x,baseline+4));
                dc.DrawText(text,new Point(Math.Max(0,Math.Min(w-text.Width,x-text.Width/2)),Math.Max(0,h-19)));
            }
            dc.Pop();
        }
        private static Geometry Diamond(double x,double y,double d)
        {
            var g=new StreamGeometry(); using(var c=g.Open()) { c.BeginFigure(new Point(x,y-d),true,true); c.LineTo(new Point(x+d,y),true,false); c.LineTo(new Point(x,y+d),true,false); c.LineTo(new Point(x-d,y),true,false); } g.Freeze(); return g;
        }
    }
}
