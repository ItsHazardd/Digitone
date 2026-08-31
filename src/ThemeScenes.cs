using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Digitone
{
    // Original vector scenes plus the user-supplied, deterministically processed sanctuary plant.
    internal static class ThemeScenes
    {
        private static ImageSource sanctuaryPlant;
        internal delegate void Animate(IAnimatable target,DependencyProperty property,double from,double to,double seconds);
        private static Brush Ink(string hex) { var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        private static T Place<T>(Canvas c,T item,double x,double y) where T:FrameworkElement { Canvas.SetLeft(item,x); Canvas.SetTop(item,y); c.Children.Add(item); return item; }
        private static Rectangle Box(Canvas c,double x,double y,double w,double h,string fill) { return Place(c,new Rectangle { Width=w,Height=h,Fill=Ink(fill) },x,y); }
        private static Path Shape(Canvas c,string data,string fill,string stroke,double width=1) { var p=new Path { Data=Geometry.Parse(data),Fill=fill==null?null:Ink(fill),Stroke=stroke==null?null:Ink(stroke),StrokeThickness=width }; c.Children.Add(p); return p; }
        private static void Drift(UIElement e,Animate animate,double dx,double dy,double seconds)
        {
            var t=new TranslateTransform(); e.RenderTransform=t;
            if(animate!=null) { if(dx!=0) animate(t,TranslateTransform.XProperty,-dx,dx,seconds); if(dy!=0) animate(t,TranslateTransform.YProperty,-dy,dy,seconds+2); }
        }
        private static ImageSource SanctuaryPlant()
        {
            if(sanctuaryPlant!=null)return sanctuaryPlant;
            using(System.IO.Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("SanctuaryPlant.png"))
            {
                if(stream==null)return null;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();sanctuaryPlant=image;return image;
            }
        }
        private static Canvas Block()
        {
            var c=new Canvas { Width=160,Height=170 };
            Shape(c,"M0,40 L80,0 160,40 80,80 Z","#79AF41","#24381E",2);
            Shape(c,"M0,40 L80,80 80,165 0,125 Z","#705035","#30251E",2);
            Shape(c,"M80,80 L160,40 160,125 80,165 Z","#4C3629","#30251E",2);
            Shape(c,"M0,40 L80,80 80,102 63,94 63,107 46,98 46,84 28,75 28,86 12,78 12,63 0,58 Z","#4F8A35",null);
            Shape(c,"M80,80 L160,40 160,60 144,68 144,82 126,90 126,78 106,88 106,105 80,118 Z","#3E722B",null);
            for(int i=0;i<12;i++) { double x=10+(i*23)%58,y=78+(i*17)%40; Shape(c,String.Format(System.Globalization.CultureInfo.InvariantCulture,"M{0},{1} l12,6 0,9 -12,-6 Z",x,y),i%2==0?"#8B6945":"#523A29",null); }
            Shape(c,"M25,38 l18,-9 12,6 -18,9 Z M85,23 l20,10 -14,7 -20,-10 Z M75,54 l15,-8 16,8 -15,8 Z","#95C555",null);
            return c;
        }
        private static Canvas Card(int index)
        {
            var c=new Canvas { Width=124,Height=184 };
            var outer=Place(c,new Border { Width=124,Height=184,CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(3),BorderBrush=Ink("#C39B57"),Background=Ink("#322B32") },0,0);
            Box(c,10,18,104,92,index==0?"#723C3A":index==1?"#345E66":"#5C467A");
            if(index==0) Shape(c,"M32,94 L76,32 87,29 85,42 43,103 Z M26,88 L52,107 M34,103 L23,118","#DBD3B6","#E8BB69",3);
            else if(index==1) Shape(c,"M33,42 Q62,30 93,42 L89,78 Q79,101 62,110 Q41,101 35,78 Z M62,44 L62,94","#7799A1","#D8C991",3);
            else Shape(c,"M62,31 L73,60 96,70 72,80 62,110 51,80 28,70 51,60 Z","#D4A7E4","#F1DFBA",2);
            Place(c,new Border { Width=104,Height=45,Background=Ink("#D3BC8E"),CornerRadius=new CornerRadius(4) },10,128);
            for(int i=0;i<3;i++) Box(c,24,139+i*9,76-i*11,2,"#796442");
            Place(c,new Ellipse { Width=27,Height=27,Fill=Ink("#54748A"),Stroke=Ink("#E8C57D"),StrokeThickness=2 },-8,-7);
            Place(c,new TextBlock { Text=(index+1).ToString(),FontFamily=new FontFamily("Georgia"),FontSize=19,Foreground=Ink("#FFF1CC") },0,-7);
            c.RenderTransform=new RotateTransform((index-1)*15,62,184); return c;
        }
        internal static Canvas Build(string theme,double height,Animate animate=null,bool light=false,bool includePlant=false,bool neon=false)
        {
            var c=new Canvas { Width=1000,Height=height,ClipToBounds=true };
            if(theme=="Sanctuary")
            {
                Box(c,0,0,1000,height,light?"#BFE5BA":"#102B29");
                if(light)
                {
                    var sun=Place(c,new Ellipse{Name="SanctuarySun",Width=138,Height=138,Fill=Ink("#F4D77E"),Opacity=.92},80,42);Drift(sun,animate,5,8,10);
                    var haze=new Canvas{Opacity=.28};c.Children.Add(haze);for(int i=0;i<4;i++)Place(haze,new Ellipse{Width=240-i*25,Height=62,Fill=Ink("#F1F1D0")},70+i*250,125+(i%2)*52);Drift(haze,animate,34,0,24);
                }
                else
                {
                    var moon=Place(c,new Ellipse{Width=105,Height=105,Fill=Ink("#D7E7C0"),Opacity=.9},105,58);Place(c,new Ellipse{Width=92,Height=92,Fill=Ink("#102B29")},139,45);Drift(moon,animate,4,7,11);
                    var stars=new Canvas{Name="SanctuaryStars"};if(neon)stars.Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=(Color)ColorConverter.ConvertFromString("#B8F39A"),BlurRadius=13,ShadowDepth=0,Opacity=.9,RenderingBias=System.Windows.Media.Effects.RenderingBias.Performance};c.Children.Add(stars);for(int i=0;i<26;i++){double size=2+(i%4);Place(stars,new Ellipse{Width=size,Height=size,Fill=Ink(i%5==0?"#C9B8FF":"#D6ECCB"),Opacity=.45+(i%3)*.18},25+(i*83)%950,24+(i*67)%Math.Max(180,height*.62));}Drift(stars,animate,6,-12,16);
                }
                Shape(c,String.Format(System.Globalization.CultureInfo.InvariantCulture,"M0,{0} Q170,{1} 340,{0} T680,{2} T1000,{0} L1000,{3} L0,{3} Z",height*.68,height*.51,height*.58,height),light?"#598F54":"#214E3B",null);
                var shrubs=new Canvas{Name="SanctuaryShrubs"};c.Children.Add(shrubs);for(int i=0;i<18;i++){double radius=90+(i%4)*24,x=-35+i*62,y=height-radius*.95-(i%3)*18;Place(shrubs,new Ellipse{Width=radius,Height=radius*.68,Fill=Ink(i%3==0?(light?"#4F8D4B":"#1C5B3D"):i%3==1?(light?"#6AA75C":"#28704A"):(light?"#3F7747":"#174A38")),Stroke=Ink(light?"#356D3A":"#2B7D55"),StrokeThickness=1.2},x,y);}
                var vines=new Canvas{Opacity=.76};c.Children.Add(vines);for(int i=0;i<5;i++){double x=18+i*220;var vine=Shape(vines,String.Format(System.Globalization.CultureInfo.InvariantCulture,"M{0},0 C{1},{2} {3},{4} {5},{6}",x,x+65,height*.18,x-45,height*.42,x+32,height*.72),null,light?"#397840":"#3B8D5A",3);for(int j=0;j<4;j++){var leaf=Place(vines,new Ellipse{Width=20,Height=10,Fill=Ink(j%2==0?(light?"#69A954":"#4D9B61"):(light?"#4B8D49":"#337C52"))},x+((j%2==0)?18:-7),70+j*height*.13);leaf.RenderTransform=new RotateTransform(j%2==0?28:-28,10,5);}}Drift(vines,animate,8,12,13);
                var fireflies=new Canvas();c.Children.Add(fireflies);for(int i=0;i<11;i++)Place(fireflies,new Ellipse{Width=5+i%3,Height=5+i%3,Fill=Ink(light?"#F3DD8D":"#B8F39A"),Opacity=.42+(i%4)*.12},65+i*86,170+(i*79)%Math.Max(200,height-260));Drift(fireflies,animate,16,-24,9);
                if(includePlant)
                {
                    var source=SanctuaryPlant();if(source!=null){var plant=new Image{Name="SanctuaryPlant",Source=source,Width=150,Height=150,Stretch=Stretch.Uniform,RenderTransformOrigin=new Point(.5,.05),Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=(Color)ColorConverter.ConvertFromString(light?"#55764A":"#83D59A"),BlurRadius=12,ShadowDepth=2,Opacity=.55}};var transforms=new TransformGroup();var sway=new RotateTransform();var floatMove=new TranslateTransform();transforms.Children.Add(sway);transforms.Children.Add(floatMove);plant.RenderTransform=transforms;Place(c,plant,760,5);if(animate!=null){animate(sway,RotateTransform.AngleProperty,-1.8,1.8,5.5);animate(floatMove,TranslateTransform.YProperty,-5,5,4.5);}}
                }
            }
            else if(theme=="Overworld")
            {
                for(int i=0;i<3;i++) { var block=Place(c,Block(),i==0?15:i==1?820:650,i==0?height*.42:i==1?height*.1:height*.8); block.Opacity=.9; Drift(block,animate,0,8+i*2,5+i); }
                var clouds=new Canvas(); c.Children.Add(clouds);
                for(int i=0;i<4;i++) { double x=80+i*245,y=30+(i%2)*55; Box(clouds,x,y,140,19,"#73908B"); Box(clouds,x+28,y-17,65,17,"#73908B"); }
                clouds.Opacity=.35; Drift(clouds,animate,38,0,22);
                for(int i=0;i<18;i++) { double x=i*60,y=height-35-(i%5)*10; Box(c,x,y,60,height-y,"#3E3327"); Box(c,x,y,60,9,"#578339"); }
                var sparks=new Canvas(); c.Children.Add(sparks); for(int i=0;i<9;i++) Box(sparks,60+i*111,140+(i*89)%350,4,4,"#B1C984"); Drift(sparks,animate,8,18,8);
            }
            else if(theme=="Spire")
            {
                var route=new Canvas { Opacity=.5 }; c.Children.Add(route);
                var line=Shape(route,"M780,580 C690,500 860,460 790,390 S670,300 790,210 S920,100 820,20",null,"#B18B5A",3); line.StrokeDashArray=new DoubleCollection { 2,5 };
                if(animate!=null) animate(line,System.Windows.Shapes.Shape.StrokeDashOffsetProperty,0,14,8);
                for(int i=0;i<5;i++) { double x=770+(i%2)*55,y=70+i*104; Place(route,new Ellipse { Width=23,Height=23,Fill=Ink("#242027"),Stroke=Ink("#C7A667"),StrokeThickness=2 },x,y); }
                var hand=new Canvas { Width=360,Height=240 }; for(int i=0;i<3;i++) Place(hand,Card(i),i*80,Math.Abs(i-1)*12); Place(c,hand,25,height*.58); Drift(hand,animate,0,10,6);
                for(int i=0;i<2;i++) { var orb=Place(c,new Ellipse { Width=54,Height=54,Fill=new RadialGradientBrush((Color)ColorConverter.ConvertFromString("#E1C586"),(Color)ColorConverter.ConvertFromString("#644B79")),Stroke=Ink("#BC9554"),StrokeThickness=3 },i==0?80:900,i==0?50:height*.65); Drift(orb,animate,6,14,4+i*2); }
                var embers=new Canvas(); c.Children.Add(embers); for(int i=0;i<12;i++) Place(embers,new Ellipse { Width=3+i%3,Height=3+i%3,Fill=Ink("#DDB571") },40+i*83,90+(i*67)%450); Drift(embers,animate,10,-30,9);
            }
            else if(theme=="Midnight")
            {
                var ribbons=new Canvas { Opacity=.6 }; c.Children.Add(ribbons);
                Shape(ribbons,"M-100,400 C180,40 580,740 1100,120 L1100,210 C550,850 200,190 -100,520 Z","#1265C8",null);
                Shape(ribbons,"M-100,440 C250,90 580,800 1100,140",null,"#39B8E4",3); Drift(ribbons,animate,30,22,10);
                var bubbles=new Canvas(); c.Children.Add(bubbles);
                for(int i=0;i<12;i++) { double r=8+(i%4)*11; Place(bubbles,new Ellipse { Width=r,Height=r,Stroke=Ink("#65D9EC"),StrokeThickness=1,Fill=Ink("#133D79"),Opacity=.35+(i%3)*.1 },25+i*86,35+(i*73)%(height-70)); }
                Drift(bubbles,animate,12,-40,12);
                var streaks=new Canvas { Opacity=.35 }; c.Children.Add(streaks); for(int i=0;i<4;i++) { var p=Shape(streaks,"M0,0 L160,-110 162,-105 2,5 Z","#9BEEFF",null); Canvas.SetLeft(p,60+i*280); Canvas.SetTop(p,110+(i%2)*height*.5); } Drift(streaks,animate,22,-16,6);
            }
            else
            {
                var rays=new Canvas { Opacity=.45 }; c.Children.Add(rays);
                for(int i=0;i<6;i++) { var p=Shape(rays,"M0,0 L-120,-900 80,-900 Z",i%2==0?"#CB3933":"#BD945E",null); p.RenderTransform=new RotateTransform(-75+i*30); Canvas.SetLeft(p,120); Canvas.SetTop(p,height*.8); }
                Drift(rays,animate,12,6,14);
                var poster=new Canvas { Width=180,Height=180 }; Shape(poster,"M90,0 L111,58 177,60 125,100 143,164 90,126 37,164 55,100 3,60 69,58 Z","#E4BD72",null); Place(c,poster,770,35); Drift(poster,animate,0,12,7);
                var bars=new Canvas(); c.Children.Add(bars); for(int i=0;i<3;i++) { var p=Shape(bars,"M0,0 L250,-70 260,-40 10,30 Z","#AD3034",null); Canvas.SetLeft(p,20+i*310); Canvas.SetTop(p,height-65); } Drift(bars,animate,25,0,9);
            }
            return c;
        }
    }
}

