using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Windows;using System.Windows.Controls;using System.Windows.Controls.Primitives;using System.Windows.Input;using System.Windows.Interop;using System.Windows.Media;using System.Windows.Media.Imaging;using System.Windows.Media.Animation;using System.Windows.Threading;using System.Web.Script.Serialization;
namespace DesktopBox {
public sealed class ScreenshotItem {public string Id,Hash,Created;public int Width,Height;}
public sealed class ScreenshotData {public bool Visible=true,Capture=false;public int CaptureModeVersion;public double X=-99999,Y=-99999;public List<ScreenshotItem> Items=new List<ScreenshotItem>();}
public sealed class ScreenshotStore {
 public readonly string Root;public ScreenshotData Data=new ScreenshotData();public string Error="";readonly JavaScriptSerializer json=new JavaScriptSerializer();
 public ScreenshotStore(string parent){Root=Path.Combine(parent,"screenshot-line");Directory.CreateDirectory(Root);string file=Path.Combine(Root,"line.json");if(File.Exists(file)){try{Data=json.Deserialize<ScreenshotData>(File.ReadAllText(file));if(Data==null||Data.Items==null)throw new InvalidDataException();}catch{Error="截图记录读取失败，原图片仍保留在保存文件夹";try{Data=json.Deserialize<ScreenshotData>(File.ReadAllText(file+".bak"));}catch{Data=new ScreenshotData();}}}if(Data==null||Data.Items==null)Data=new ScreenshotData();bool migrate=Data.Capture||Data.CaptureModeVersion!=2;Data.Capture=false;Data.CaptureModeVersion=2;if(Double.IsNaN(Data.X)||Double.IsInfinity(Data.X))Data.X=-99999;if(Double.IsNaN(Data.Y)||Double.IsInfinity(Data.Y))Data.Y=-99999;Data.Items=Data.Items.Where(i=>i!=null&&ValidId(i.Id)&&File.Exists(PathFor(i))).GroupBy(i=>i.Id).Select(g=>g.First()).ToList();if(migrate)try{Save();}catch(Exception ex){Error="截图线设置未保存："+ex.Message;}}
 static bool ValidId(string id){Guid parsed;return Guid.TryParseExact(id,"N",out parsed);}
 public string PathFor(ScreenshotItem item){if(item==null||!ValidId(item.Id))throw new InvalidDataException("截图标识无效");return Path.Combine(Root,item.Id+".png");}
 public void Save(){string file=Path.Combine(Root,"line.json"),temp=file+".tmp";File.WriteAllText(temp,json.Serialize(Data),System.Text.Encoding.UTF8);if(!File.Exists(file))File.Move(temp,file);else{try{File.Replace(temp,file,file+".bak");}catch(IOException){File.Copy(file,file+".bak",true);File.Copy(temp,file,true);}catch(UnauthorizedAccessException){File.Copy(file,file+".bak",true);File.Copy(temp,file,true);}}}
 public ScreenshotItem Add(BitmapSource source,out bool added){added=false;if(source==null||source.PixelWidth<1||source.PixelHeight<1||(long)source.PixelWidth*source.PixelHeight>64000000)throw new InvalidDataException("截图尺寸无效或过大");var normal=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);normal.Freeze();byte[] png;using(var memory=new MemoryStream()){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(normal));encoder.Save(memory);png=memory.ToArray();}string hash=ImageHash(source);var old=Data.Items.FirstOrDefault(i=>i.Hash==hash);if(old!=null)return old;var item=new ScreenshotItem{Id=Guid.NewGuid().ToString("N"),Hash=hash,Created=DateTimeOffset.Now.ToString("o"),Width=source.PixelWidth,Height=source.PixelHeight};File.WriteAllBytes(PathFor(item),png);Data.Items.Add(item);try{Save();added=true;return item;}catch{Data.Items.Remove(item);throw;}}
 public static string ImageHash(BitmapSource source){var normal=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);normal.Freeze();int stride=checked(source.PixelWidth*4);var pixels=new byte[checked(stride*source.PixelHeight+8)];Buffer.BlockCopy(BitConverter.GetBytes(source.PixelWidth),0,pixels,0,4);Buffer.BlockCopy(BitConverter.GetBytes(source.PixelHeight),0,pixels,4,4);normal.CopyPixels(pixels,stride,8);string hash;using(var sha=System.Security.Cryptography.SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(pixels)).Replace("-","");return hash;}
 public bool Move(string id,string target,bool after){
  int from=Data.Items.FindIndex(item=>item.Id==id),to=Data.Items.FindIndex(item=>item.Id==target);
  if(from<0||to<0||from==to)return false;
  return MoveTo(id,to+(after?1:0)-(from<to?1:0));
 }
 public bool MoveTo(string id,int index){
  int from=Data.Items.FindIndex(item=>item.Id==id);
  if(from<0||index<0||index>=Data.Items.Count||index==from)return false;
  var before=Data.Items.ToList();
  var moving=Data.Items[from];
  Data.Items.RemoveAt(from);
  Data.Items.Insert(index,moving);
  try{Save();return true;}catch{Data.Items=before;throw;}
 }
 public BitmapSource Image(ScreenshotItem item,bool thumbnail){using(var stream=File.OpenRead(PathFor(item))){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;if(thumbnail){double ratio=Math.Min(1,Math.Min(256.0/item.Width,192.0/item.Height));image.DecodePixelWidth=Math.Max(1,(int)Math.Ceiling(item.Width*ratio));}image.StreamSource=stream;image.EndInit();image.Freeze();return image;}}
 public RecycleEntry.Result Recycle(ScreenshotItem item,IntPtr owner){if(!Data.Items.Contains(item))throw new InvalidOperationException("截图已移除");var result=RecycleEntry.Send(PathFor(item),owner);Data.Items.Remove(item);Save();return result;}
}
internal sealed class TileSortPreview {
 readonly Panel tiles;
 readonly Canvas overlay;
 readonly string ghostTag;
 readonly Func<Button,string> identity;
 Button source;
 Image ghost;
 Point grip;
 double sourceOpacity;
 Button[] shifted;
 int destination=-1;

 internal TileSortPreview(Panel tiles,Grid host,string ghostTag,Func<Button,string> identity){
  this.tiles=tiles;this.ghostTag=ghostTag;this.identity=identity;
  overlay=new Canvas{IsHitTestVisible=false};
  Grid.SetRowSpan(overlay,Math.Max(1,host.RowDefinitions.Count));
  Grid.SetColumnSpan(overlay,Math.Max(1,host.ColumnDefinitions.Count));
  Panel.SetZIndex(overlay,30);host.Children.Add(overlay);
 }
 // The direct child's layout offset stays fixed while its RenderTransform animates.
 internal static Rect Bounds(Button button){return new Rect((Point)VisualTreeHelper.GetOffset(button),button.RenderSize);}

 internal void Start(Button button,Point anchor){
  source=button;grip=anchor;sourceOpacity=button.Opacity;
  ghost=TileDragMotion.CreateGhost(button,ghostTag);
  overlay.Children.Add(ghost);
  Position(button.TranslatePoint(grip,overlay));
  button.Opacity=TileDragMotion.PreviewOpacity;
 }
 internal DragDropEffects Run(Button button,Point anchor,IDataObject payload,DragDropEffects effects){
  Start(button,anchor);
  GiveFeedbackEventHandler feedback=(sender,e)=>{
   if(!overlay.IsVisible||PresentationSource.FromVisual(overlay)==null)return;
   var pointer=System.Windows.Forms.Cursor.Position;
   Position(overlay.PointFromScreen(new Point(pointer.X,pointer.Y)));
  };
  button.GiveFeedback+=feedback;
  try{feedback(button,null);return DragDrop.DoDragDrop(button,payload,effects);}
  finally{button.GiveFeedback-=feedback;}
 }
 void Position(Point pointer){
  Canvas.SetLeft(ghost,pointer.X-grip.X);Canvas.SetTop(ghost,pointer.Y-grip.Y);
 }
 internal void Preview(Button[] buttons,int slot,Point pointer){
  if(source==null)return;
  Position(tiles.TranslatePoint(pointer,overlay));
  int from=Array.IndexOf(buttons,source);
  if(from<0){Reset();return;}
  int to=Math.Max(0,Math.Min(buttons.Length-1,slot-(slot>from?1:0)));
  if(destination==to&&shifted!=null&&shifted.SequenceEqual(buttons))return;
  shifted=buttons;destination=to;
  var positions=buttons.Select(button=>Bounds(button).Location).ToArray();
  for(int index=0;index<buttons.Length;index++){
   int next=index;
   if(index==from)next=to;
   else if(from<index&&index<=to)next--;
   else if(to<=index&&index<from)next++;
   TileDragMotion.Slide(buttons[index],positions[next]-positions[index]);
  }
 }
 internal void Reset(){
  if(shifted==null)return;
  foreach(var button in shifted)TileDragMotion.Slide(button,new Vector());
  shifted=null;destination=-1;
 }
 internal void Finish(Action rebuild,bool committed){
  if(source==null)return;
  var previous=new Dictionary<string,Point>(StringComparer.OrdinalIgnoreCase);
  if(rebuild!=null&&committed){
   foreach(var button in tiles.Children.OfType<Button>()){
    string id=identity(button);
    if(id!=null)previous[id]=button.TranslatePoint(new Point(),tiles);
   }
   previous[identity(source)]=overlay.TranslatePoint(new Point(Canvas.GetLeft(ghost),Canvas.GetTop(ghost)),tiles);
  }
  source.Opacity=sourceOpacity;overlay.Children.Remove(ghost);
  source=null;ghost=null;
  Reset();
  if(rebuild==null)return;
  rebuild();
  if(previous.Count==0)return;
  tiles.UpdateLayout();
  foreach(var button in tiles.Children.OfType<Button>()){
   string id=identity(button);Point old;
   if(id==null||!previous.TryGetValue(id,out old))continue;
   var delta=old-Bounds(button).Location;
   button.RenderTransform=new TranslateTransform(delta.X,delta.Y);
   TileDragMotion.Slide(button,new Vector());
  }
 }
}
internal static class TileDragMotion {
 internal const double PreviewOpacity=.45;
 internal static Image CreateGhost(FrameworkElement card,string tag){
  var snapshot=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(card.ActualWidth)),
   Math.Max(1,(int)Math.Ceiling(card.ActualHeight)),96,96,PixelFormats.Pbgra32);
  snapshot.Render(card);
  snapshot.Freeze();
  return new Image{Tag=tag,Source=snapshot,Width=card.ActualWidth,Height=card.ActualHeight,
   Opacity=.82,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new ScaleTransform(1.06,1.06),
   Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=18,ShadowDepth=4,Opacity=.5}};
 }
 internal static void Slide(FrameworkElement card,Vector destination){
  var shift=card.RenderTransform as TranslateTransform;
  if(shift==null){shift=new TranslateTransform();card.RenderTransform=shift;}
  if(shift.X.Equals(destination.X)&&shift.Y.Equals(destination.Y)&&!shift.HasAnimatedProperties)return;
  AnimateAxis(shift,TranslateTransform.XProperty,shift.X,destination.X);
  AnimateAxis(shift,TranslateTransform.YProperty,shift.Y,destination.Y);
 }
 static void AnimateAxis(TranslateTransform shift,DependencyProperty axis,double from,double to){
  shift.BeginAnimation(axis,null);
  shift.SetValue(axis,to);
  if(Math.Abs(from-to)>.25)shift.BeginAnimation(axis,new DoubleAnimation(from,to,
   new Duration(TimeSpan.FromMilliseconds(180))){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
 }
}
public sealed class ScreenshotClip:FrameworkElement {
 public ScreenshotClip(){Width=16;Height=31;IsHitTestVisible=false;}
 protected override void OnRender(DrawingContext dc){base.OnRender(dc);var stroke=new Pen(new SolidColorBrush(Color.FromArgb(180,70,88,99)),.7);dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(247,246,226),Color.FromRgb(176,184,169),0),stroke,new Rect(3,1,10,29),2,2);dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(127,143,134)),1),new Point(8,3),new Point(8,27));dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(223,230,215)),null,new Rect(1,13,14,5),1,1);dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(113,131,128)),null,new Point(8,15.5),1.7,1.7);}
}
public sealed class ScreenshotLine:Window {
 bool opening;string openedId,openRoute,openedBrowsePath;string[] openedBrowseOrder;int wheelDelta;
 public static string RuntimeVersion{get{return typeof(ScreenshotLine).Assembly.GetName().Version.ToString(3);}}
 void Wheel(object sender,MouseWheelEventArgs e){if(PageCount<=1||e.Delta==0)return;ScrollPages(e.Delta);e.Handled=true;}
 public void ScrollPages(int delta){if(delta==0)return;if(PageCount<=1){wheelDelta=0;return;}if(Math.Sign(wheelDelta)!=Math.Sign(delta))wheelDelta=0;wheelDelta+=delta;int steps=wheelDelta/120;if(steps==0)return;wheelDelta%=120;SelectPage(CurrentPage-steps);}
 const string OwnFormat="rushbin.screenshot-line.source.v1",SortFormat="rushbin.screenshot-line.sort.v1";readonly string token=Guid.NewGuid().ToString("N");public readonly ScreenshotStore Store;readonly UniformGrid cards;readonly StackPanel pager;readonly DispatcherTimer pageHover;int hoverPage=-1;bool pageRender,resizeQueued;ScreenshotPinNotice notice;public int CurrentPage{get;private set;}public int PageCapacity{get;private set;}public int PageColumns{get;private set;}public int PageRows{get;private set;}public int PageCount{get{return Math.Max(1,(Store.Data.Items.Count+Math.Max(1,PageCapacity)-1)/Math.Max(1,PageCapacity));}}readonly TextBlock title,message,empty;readonly DispatcherTimer retry;HwndSource source;IntPtr handle;bool closed,sorting,renderPending,dragReordered,moving,pinRequested,manualEmpty;byte[] latestPng;int latestWidth,latestHeight;string latestHash;uint ignoredDeletedSequence;bool ownsF4;public bool F4Registered{get{return ownsF4||(Program.Files!=null&&Program.Files.F4Registered);}}public int PinsRequested;uint pending,seen;int attempts;public bool ListenerReady;public string Error="";public int ClipboardEvents,Imported;readonly Canvas dragOverlay;
 readonly Border sortGap;
 Image dragImage;
 Point dragGrip;
 string draggedId;
 int sortIndex=-1;
 [DllImport("user32.dll",SetLastError=true)]static extern bool AddClipboardFormatListener(IntPtr window);
 [DllImport("user32.dll")]static extern bool RemoveClipboardFormatListener(IntPtr window);
 [DllImport("user32.dll")]static extern uint GetClipboardSequenceNumber();
 [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr window,int id);
 public ScreenshotLine(string parent){Store=new ScreenshotStore(parent);Title="rushbin 截图晾衣线";Height=190;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=new SolidColorBrush(Color.FromArgb(1,0,0,0));Foreground=Brushes.White;FontFamily=new FontFamily("Microsoft YaHei UI");ShowInTaskbar=Program.ShotsCheck;Topmost=Program.ShotsCheck;ShowActivated=false;FocusVisualStyle=null;
  var layout=new Grid{Margin=new Thickness(8,0,8,0),Background=Brushes.Transparent};Content=layout;PreviewMouseWheel+=Wheel;var heading=new DockPanel{Height=27,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(7,0,7,0)};layout.Children.Add(heading);var more=WindowPreview.PlainButton("···");more.Width=29;more.ToolTip="截图线设置";DockPanel.SetDock(more,Dock.Right);heading.Children.Add(more);more.Click+=(o,e)=>{var menu=Menu();menu.PlacementTarget=more;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;};title=new TextBlock{Text="截图晾衣架",FontSize=11,Opacity=.78,VerticalAlignment=VerticalAlignment.Center};heading.Children.Add(title);title.Cursor=Cursors.SizeAll;title.Background=Brushes.Transparent;title.ToolTip=SteamEdition.Enabled?"拖动标题或绳子移动":"拖动标题或绳子移动 · 按F4挂上最近截图";title.MouseLeftButtonDown+=MoveLine;var pin=WindowPreview.PlainButton(SteamEdition.Enabled?"挂上截图":"挂上截图 F4");pin.FontSize=11;pin.Width=160;DockPanel.SetDock(pin,Dock.Right);heading.Children.Insert(1,pin);pin.Click+=(o,e)=>PinLatest();
  var rope=new System.Windows.Shapes.Path{Data=Geometry.Parse("M 5,5 C 220,27 780,27 995,5"),Stroke=new LinearGradientBrush(Color.FromArgb(180,241,250,251),Color.FromArgb(230,255,250,231),0),StrokeThickness=1.4,Stretch=Stretch.Fill,Height=17,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,30,0,0),IsHitTestVisible=true,Cursor=Cursors.SizeAll};rope.MouseLeftButtonDown+=MoveLine;rope.Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,BlurRadius=2,Opacity=.3,ShadowDepth=1};layout.Children.Add(rope);
  cards=new UniformGrid{AllowDrop=true,Rows=1,Columns=1};var viewport=new Border{Margin=new Thickness(12,44,12,32),ClipToBounds=true};var cardArea=new Grid{ClipToBounds=true};
  viewport.Child=cardArea;
  var gapLayer=new Canvas{IsHitTestVisible=false};
  sortGap=new Border{Tag="screenshot-sort-gap",CornerRadius=new CornerRadius(10),
   BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(125,202,242,237)),
   Background=new SolidColorBrush(Color.FromArgb(32,173,223,220)),Visibility=Visibility.Collapsed};
  gapLayer.Children.Add(sortGap);
  cardArea.Children.Add(gapLayer);
  cardArea.Children.Add(cards);
  layout.Children.Add(viewport);
  dragOverlay=new Canvas{IsHitTestVisible=false};
  Panel.SetZIndex(dragOverlay,20);
  layout.Children.Add(dragOverlay);
  pager=new StackPanel{Tag="screenshot-pages",Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Height=24,Margin=new Thickness(0,0,0,3)};layout.Children.Add(pager);pageHover=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(450)};pageHover.Tick+=(o,e)=>{pageHover.Stop();if(hoverPage>=0)SelectPage(hoverPage);};empty=new TextBlock{Text=SteamEdition.Enabled?"拖入图片，或点击「挂上截图」读取剪贴板。":"拖入图片，或按 F4 把最近截图夹上来",FontSize=12,Opacity=.5,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,70,0,0),IsHitTestVisible=false};layout.Children.Add(empty);message=new TextBlock{FontSize=10,Opacity=.7,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,8,0),TextTrimming=TextTrimming.CharacterEllipsis};layout.Children.Add(message);
  AllowDrop=true;PreviewDragOver+=(o,e)=>{if(DragId(e)!=null){
    if(PreviewSortAt(e.GetPosition(cards))){e.Effects=DragDropEffects.Copy;e.Handled=true;}
    else SuspendSortPreview();
    return;
   }
   var effect=IncomingEffect(e.Data,e.AllowedEffects);e.Effects=effect;e.Handled=true;};PreviewDrop+=(o,e)=>{
   string id=DragId(e);
   if(id!=null){
    if(PreviewSortAt(e.GetPosition(cards))){
     CommitSort(id,sortIndex);
     e.Effects=DragDropEffects.Copy;e.Handled=true;
    }
    return;
   }var effect=IncomingEffect(e.Data,e.AllowedEffects);if(effect==DragDropEffects.None){e.Effects=effect;e.Handled=true;return;}try{int count=ImportImages(e.Data);e.Effects=count>0?effect:DragDropEffects.None;}catch(Exception ex){Error=ex.Message;message.Text="图片导入失败："+ex.Message;e.Effects=DragDropEffects.None;}e.Handled=true;};
  PreviewDragLeave+=(o,e)=>{
   if(!sorting)return;
   var mouse=System.Windows.Forms.Cursor.Position;
   var local=PointFromScreen(new Point(mouse.X,mouse.Y));
   if(!new Rect(RenderSize).Contains(local)){SuspendSortPreview();pageHover.Stop();hoverPage=-1;}
  };
  ComponentSizer.Attach(this,"shots",280,150);SizeChanged+=(o,e)=>{if(!resizeQueued){resizeQueued=true;Dispatcher.BeginInvoke(new Action(()=>{resizeQueued=false;if(closed)return;if(sorting){renderPending=true;return;}if(UpdatePageGeometry())Render();}),DispatcherPriority.Render);}};retry=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(90)};retry.Tick+=(o,e)=>{retry.Stop();ReadClipboard();};SourceInitialized+=(o,e)=>{handle=new WindowInteropHelper(this).Handle;source=HwndSource.FromHwnd(handle);source.AddHook(Hook);if(!Program.ShotsCheck){Native.PreviewStyle(this);Native.DesktopWidget(this);}pending=GetClipboardSequenceNumber();seen=0;if(!Program.Testing&&!SteamEdition.Enabled){ListenerReady=AddClipboardFormatListener(handle);ownsF4=Program.ShotsCheck&&RegisterHotKey(handle,4,0x4000,0x73);if(Program.ShotsCheck&&!ownsF4)message.Text="F4已被占用，可点击“挂上截图”";if(!ListenerReady)Error=new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;Dispatcher.BeginInvoke(new Action(ReadClipboard),DispatcherPriority.Background);}Record();};Loaded+=(o,e)=>{Place();Render();};IsVisibleChanged+=(o,e)=>Record();Closed+=(o,e)=>{closed=true;retry.Stop();pageHover.Stop();if(notice!=null)notice.Close();if(ListenerReady)RemoveClipboardFormatListener(handle);if(ownsF4){UnregisterHotKey(handle,4);ownsF4=false;}if(source!=null&&!source.IsDisposed)source.RemoveHook(Hook);ListenerReady=false;};MouseRightButtonUp+=(o,e)=>{if(e.Handled)return;var menu=Menu();menu.PlacementTarget=this;menu.IsOpen=true;e.Handled=true;};Application.Current.Exit+=(o,e)=>Shutdown();Render();
 }
 IntPtr Hook(IntPtr h,int msg,IntPtr wp,IntPtr lp,ref bool handled){if(msg==0x312&&wp.ToInt32()==4&&ownsF4){PinLatest();handled=true;return IntPtr.Zero;}if(msg==0x20a&&!closed){int delta=unchecked((short)((wp.ToInt64()>>16)&0xffff));if(PageCount>1&&delta!=0){ScrollPages(delta);handled=true;return IntPtr.Zero;}}if(msg==0x31d&&!closed){ClipboardEvents++;pending=GetClipboardSequenceNumber();attempts=0;retry.Stop();ReadClipboard();}if(msg==0x21&&!Program.ShotsCheck){handled=true;return new IntPtr(3);}if(msg==0x7e||msg==0x2e0)Dispatcher.BeginInvoke(new Action(Place));return IntPtr.Zero;}
 void ReadClipboard(){if(closed)return;if(pending==seen){FinishPin();return;}try{uint sequence=GetClipboardSequenceNumber();if(sequence!=pending){pending=sequence;attempts=0;}if(sequence==ignoredDeletedSequence&&ignoredDeletedSequence!=0){seen=pending;FinishPin();Record();return;}if(Clipboard.ContainsData(OwnFormat)&&Object.Equals(Clipboard.GetData(OwnFormat),token)){seen=pending;FinishPin();Record();return;}BitmapSource bitmap=null;if(Clipboard.ContainsData("PNG")){object value=Clipboard.GetData("PNG");var stream=value as Stream;var bytes=value as byte[];if(stream!=null||bytes!=null){byte[] buffer;if(bytes!=null)buffer=bytes;else{if(stream.CanSeek)stream.Position=0;using(var copy=new MemoryStream()){stream.CopyTo(copy);buffer=copy.ToArray();}}using(var data=new MemoryStream(buffer)){var decoder=new PngBitmapDecoder(data,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);bitmap=decoder.Frames[0];bitmap.Freeze();}}}if(bitmap==null&&Clipboard.ContainsImage())bitmap=ClipboardBitmap(Clipboard.GetImage(),false);if(bitmap!=null){bitmap.Freeze();Remember(bitmap);}seen=pending;Error="";FinishPin();Record();}catch(ExternalException ex){if(++attempts<8)retry.Start();else{Error="剪贴板暂时被占用："+ex.Message;pinRequested=false;message.Text=SteamEdition.Enabled?"剪贴板暂被占用，请稍后重试。":"剪贴板暂被占用，请稍后再按F4";Record();}}catch(Exception ex){seen=pending;pinRequested=false;Error=ex.Message;message.Text="这张截图未读取："+ex.Message;Record();}}
 public ScreenshotItem Accept(BitmapSource bitmap){bool added;var item=Store.Add(bitmap,out added);if(added)Imported++;manualEmpty=false;UpdatePageGeometry();CurrentPage=Store.Data.Items.IndexOf(item)/Math.Max(1,PageCapacity);Render();return item;}
 public void Start(){new WindowInteropHelper(this).EnsureHandle();if(Store.Data.Visible&&Store.Data.Items.Count>0)Show();Place();Record();}
 public void MoveLine(object sender,MouseButtonEventArgs e){if(e.ChangedButton!=MouseButton.Left||e.ClickCount!=1)return;try{moving=true;DragMove();SavePosition();e.Handled=true;}catch(InvalidOperationException){}finally{moving=false;}}
 public void SavePosition(){ClampPosition();Store.Data.X=Left;Store.Data.Y=Top;Safe(Store.Save);Record();}
 void ClampPosition(){var screen=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);var presentation=PresentationSource.FromVisual(this);double scale=presentation==null?1:presentation.CompositionTarget.TransformToDevice.M11;var area=screen.WorkingArea;Width=Math.Min(Width,Math.Max(280,area.Width/scale-24));Left=Math.Max(area.Left/scale,Math.Min(Left,area.Right/scale-Width));Top=Math.Max(area.Top/scale,Math.Min(Top,area.Bottom/scale-Height));}
 public void Place(){if(closed||moving||ComponentSizer.Busy(this))return;Height=ComponentSizer.Height("shots",190);var screens=System.Windows.Forms.Screen.AllScreens;var screen=screens[Math.Max(0,Math.Min(screens.Length-1,Program.S.Data.Screen))];var presentation=PresentationSource.FromVisual(this);double scale=presentation==null?1:presentation.CompositionTarget.TransformToDevice.M11;if(Store.Data.X!=-99999&&Store.Data.Y!=-99999)screen=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)Math.Round(Store.Data.X*scale),(int)Math.Round(Store.Data.Y*scale)));var area=screen.WorkingArea;double available=area.Width/scale;double inset=Math.Min(390,Math.Max(16,available-480));Width=Math.Min(available-24,ComponentSizer.Width("shots",Math.Min(1120,Math.Max(280,available-inset-20))));Left=Store.Data.X==-99999?area.Left/scale+inset+(available-inset-20-Width)/2:Store.Data.X;Top=Store.Data.Y==-99999?area.Top/scale+12:Store.Data.Y;ClampPosition();if(!Program.ShotsCheck){if(!Held&&!WidgetLayers.Raised(this))Topmost=false;Native.DesktopWidget(this);}Record();}
 public int RememberedBytes{get{return latestPng==null?0:latestPng.Length;}}
 public void Remember(BitmapSource image){if(image==null)return;image.Freeze();string hash=ScreenshotStore.ImageHash(image);if(latestPng!=null&&latestHash==hash)return;byte[] encoded;using(var memory=new MemoryStream()){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(memory);encoded=memory.ToArray();}latestHash=hash;latestPng=encoded;latestWidth=image.PixelWidth;latestHeight=image.PixelHeight;Record();}
 public ScreenshotItem PinRemembered(){if(latestPng==null)throw new InvalidOperationException(SteamEdition.Enabled?"请先截取图片并放入剪贴板。":"请先截图再按F4");using(var stream=new MemoryStream(latestPng,false)){var decoder=new PngBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var bitmap=decoder.Frames[0];bitmap.Freeze();return Accept(bitmap);}}
 public void PinLatest(){if(closed)return;if(SteamEdition.Enabled){latestPng=null;latestHash=null;}PinsRequested++;pinRequested=true;pending=GetClipboardSequenceNumber();seen=0;attempts=0;retry.Stop();ReadClipboard();}
 void FinishPin(){if(!pinRequested)return;pinRequested=false;if(latestPng==null){message.Text=SteamEdition.Enabled?"剪贴板没有可挂上的图片。":"还没有读取到截图，请先截图再按F4";Record();return;}Safe(PinRememberedWithNotice);Record();}
 void Safe(Action action){try{action();}catch(Exception ex){Error=ex.Message;message.Text=ex.Message;Record();}}
 public static BitmapSource ClipboardBitmap(BitmapSource image,bool explicitPng){if(image==null||explicitPng)return image;if((long)image.PixelWidth*image.PixelHeight>64000000)throw new InvalidDataException("截图尺寸过大");var normal=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);int stride=checked(image.PixelWidth*4);var pixels=new byte[checked(stride*image.PixelHeight)];normal.CopyPixels(pixels,stride,0);bool hasAlpha=false;for(int n=3;n<pixels.Length;n+=4)if(pixels[n]!=0){hasAlpha=true;break;}if(hasAlpha){normal.Freeze();return normal;}for(int n=3;n<pixels.Length;n+=4)pixels[n]=255;var result=BitmapSource.Create(image.PixelWidth,image.PixelHeight,image.DpiX>0?image.DpiX:96,image.DpiY>0?image.DpiY:96,PixelFormats.Bgra32,null,pixels,stride);result.Freeze();return result;}
 public void Copy(ScreenshotItem item){var image=Store.Image(item,false);var data=new DataObject();data.SetImage(image);data.SetData("PNG",new MemoryStream(File.ReadAllBytes(Store.PathFor(item))));data.SetData(OwnFormat,token);Clipboard.SetDataObject(data,true);message.Text="已复制，可粘贴";Record();}
 public void Open(ScreenshotItem item){if(closed||opening||!Store.Data.Items.Contains(item))return;opening=true;openedId=item.Id;try{var request=NativeScreenshotOpen.CreateRequest(Store,item);openRoute=request.Route;openedBrowsePath=request.FilePath;openedBrowseOrder=request.OrderedIds;System.Diagnostics.Process.Start(request.StartInfo);Error="";message.Text="已交给系统看图应用";}catch(Exception ex){Error="系统看图启动失败："+ex.Message;message.Text=Error;}finally{opening=false;Record();}}
 void Export(ScreenshotItem item){var dialog=new Microsoft.Win32.SaveFileDialog{Filter="PNG 图片|*.png",FileName="截图-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".png",OverwritePrompt=true};if(dialog.ShowDialog(this)==true&&!String.Equals(Path.GetFullPath(dialog.FileName),Store.PathFor(item),StringComparison.OrdinalIgnoreCase))File.Copy(Store.PathFor(item),dialog.FileName,true);}
 public void Remove(ScreenshotItem item){bool isLatest=latestPng!=null&&latestHash==item.Hash;Store.Recycle(item,handle);if(isLatest){latestPng=null;latestWidth=latestHeight=0;latestHash=null;pinRequested=false;ignoredDeletedSequence=GetClipboardSequenceNumber();}foreach(var thumbnail in AppearanceProbe.Visuals<Image>(cards))thumbnail.Source=null;Render();message.Text="截图已移到回收站";Record();}
 ContextMenu ItemMenu(ScreenshotItem item){var menu=new ContextMenu();AddMenu(menu,"复制截图",()=>Copy(item));AddMenu(menu,"用系统看图打开",()=>Open(item));AddMenu(menu,"另存为 PNG…",()=>Export(item));menu.Items.Add(new Separator());AddMenu(menu,"移到回收站",()=>Remove(item));return menu;}
 void AddMenu(ContextMenu menu,string text,Action action){var entry=new MenuItem{Header=text};entry.Click+=(o,e)=>Safe(action);menu.Items.Add(entry);}
 public MenuItem VisibilityMenu(){var item=new MenuItem{Header="显示截图晾衣线",IsCheckable=true,IsChecked=Store.Data.Visible};item.Click+=(o,e)=>Safe(()=>{Store.Data.Visible=item.IsChecked;Store.Save();SetVisiblePreference(Store.Data.Visible);});return item;}
 public ContextMenu Menu(){var menu=new ContextMenu();AddMenu(menu,SteamEdition.Enabled?"挂上剪贴板图片":"挂上最近截图  F4",PinLatest);menu.Items.Add(VisibilityMenu());AddMenu(menu,"恢复到桌面上方",()=>{Store.Data.X=Store.Data.Y=-99999;Store.Save();Place();});AddMenu(menu,"打开截图保存文件夹",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Store.Root){UseShellExecute=true}));return menu;}
 public bool Held{get;private set;}
 public void SetHeld(bool value){if(closed||Held==value)return;if(value&&(!Store.Data.Visible||(Store.Data.Items.Count==0&&!manualEmpty)))return;Held=value;if(value){if(!IsVisible){Show();Place();}WidgetLayers.RaiseGroup(this);}else WidgetLayers.Desktop(this);Record();}
 public void PrepareManualShow(){manualEmpty=Store.Data.Items.Count==0;}
 public void SetVisiblePreference(bool visible){if(!visible){manualEmpty=false;SetHeld(false);}Store.Data.Visible=visible;Store.Save();if(Program.Components!=null)Program.Components.Observe("shots",visible);if(visible&&(Store.Data.Items.Count>0||manualEmpty)){Show();Place();}else Hide();Record();}
 public void Render(){if(!Program.Testing&&!Program.ShotsCheck){if(Store.Data.Items.Count==0&&!manualEmpty)Hide();else if(Store.Data.Visible&&!IsVisible)Show();}if(sorting&&!pageRender){renderPending=true;return;}UpdatePageGeometry();foreach(var image in AppearanceProbe.Visuals<Image>(cards))image.Source=null;cards.Children.Clear();RenderPager();empty.Visibility=Store.Data.Items.Count==0?Visibility.Visible:Visibility.Collapsed;if(Store.Error!="")message.Text=Store.Error;foreach(var item in Store.Data.Items.Skip(CurrentPage*Math.Max(1,PageCapacity)).Take(Math.Max(1,PageCapacity))){var shot=item;var layout=new Grid{Width=146,Height=143};var paper=new Border{Background=new SolidColorBrush(Color.FromArgb(225,246,247,242)),BorderBrush=new SolidColorBrush(Color.FromArgb(160,255,255,255)),BorderThickness=new Thickness(1),Padding=new Thickness(5),Height=96,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(2,24,2,0),CornerRadius=new CornerRadius(3)};paper.Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,BlurRadius=8,ShadowDepth=3,Opacity=.3};try{paper.Child=new Image{Source=Store.Image(item,true),Stretch=Stretch.Uniform};}catch{paper.Child=new TextBlock{Text="预览不可用",Foreground=Brushes.Gray,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};}layout.Children.Add(paper);layout.Children.Add(new ScreenshotClip{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top});DateTimeOffset time;DateTimeOffset.TryParse(item.Created,out time);layout.Children.Add(new TextBlock{Text=time.ToLocalTime().ToString("HH:mm")+" · "+item.Width+" × "+item.Height,FontSize=9,Opacity=.62,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,5)});var button=WindowPreview.PlainButton(layout);button.Padding=new Thickness(1);button.Margin=new Thickness(3,0,3,0);button.Tag=item.Id;button.ToolTip="单击复制 · 双击系统看图 · 线内排序 · 向外拖放图片";System.Windows.Automation.AutomationProperties.SetName(button,"截图 "+time.ToLocalTime().ToString("HH:mm:ss")+" "+item.Width+"乘"+item.Height);button.ContextMenu=ItemMenu(shot);var gesture=new EntryGesture();bool armed=false;button.PreviewMouseLeftButtonDown+=(o,e)=>{gesture.Press(e.GetPosition(button),e.ClickCount);armed=e.ClickCount==1;};button.PreviewMouseLeftButtonUp+=(o,e)=>armed=false;button.Click+=(o,e)=>{if(!sorting&&gesture.Click())Safe(()=>Copy(shot));};button.MouseDoubleClick+=(o,e)=>{if(e.ChangedButton==MouseButton.Left){Safe(()=>Open(shot));e.Handled=true;}};button.AddHandler(UIElement.PreviewMouseMoveEvent,new MouseEventHandler((o,e)=>{
    if(e.LeftButton!=MouseButtonState.Pressed||!armed||!gesture.Move(e.GetPosition(button)))return;
    armed=false;e.Handled=true;button.ReleaseMouseCapture();
    var card=cards.Children.OfType<Viewbox>().FirstOrDefault(view=>Object.Equals(view.Tag,shot.Id));
    if(card==null)return;
    Point grip=e.GetPosition(card),position=e.GetPosition(cards);
    DragDropEffects result=DragDropEffects.None;
    Safe(()=>{
     var data=DragData(shot);
     BeginSort(shot.Id,card,grip);
     PreviewSortAt(position);
     try{result=DragDrop.DoDragDrop(button,data,DragDropEffects.Copy);}
     finally{FinishSort();}
    });
    gesture.Cancel();
    try{DeferredDiagnostics.Write(Path.Combine(Program.S.Root,"screenshot-line-drag.json"),new JavaScriptSerializer().Serialize(new{
     time=DateTimeOffset.Now.ToString("o"),id=shot.Id,result=result.ToString(),internalReorder=dragReordered,
     sourceRetained=File.Exists(Store.PathFor(shot)),formats=new[]{"FileDrop","Bitmap","PNG"}
    }));}catch{}
   }),true);button.MouseEnter+=(o,e)=>paper.RenderTransform=new TranslateTransform(0,-2);button.MouseLeave+=(o,e)=>paper.RenderTransform=Transform.Identity;var host=new Grid{Width=154,Height=145};host.Children.Add(button);var remove=WindowPreview.PlainButton("×",true);remove.Tag="screenshot-delete";remove.Width=22;remove.Height=22;remove.FontSize=16;remove.Padding=new Thickness(0);remove.Margin=new Thickness(0,22,5,0);remove.VerticalAlignment=VerticalAlignment.Top;remove.HorizontalAlignment=HorizontalAlignment.Right;var deleteFill=new SolidColorBrush(Color.FromArgb(215,26,39,49));remove.Background=deleteFill;remove.MouseLeave+=(o,e)=>remove.Background=deleteFill;remove.ToolTip="删除截图（移到回收站）";System.Windows.Automation.AutomationProperties.SetName(remove,"删除截图 "+time.ToLocalTime().ToString("HH:mm:ss"));remove.Click+=(o,e)=>{e.Handled=true;Safe(()=>Remove(shot));};Panel.SetZIndex(remove,5);host.Children.Add(remove);cards.Children.Add(new Viewbox{Tag=shot.Id,Opacity=sorting&&shot.Id==draggedId?TileDragMotion.PreviewOpacity:1,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Child=host,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch});}Record();}
 public bool UpdatePageGeometry(){double width=ActualWidth>0?ActualWidth:Width,height=ActualHeight>0?ActualHeight:Height;int columns=Math.Max(1,(int)Math.Floor(Math.Max(1,width-40)/154)),rows=Math.Max(1,(int)Math.Floor(Math.Max(1,height-80)/145));int first=CurrentPage*Math.Max(1,PageCapacity);bool changed=columns!=PageColumns||rows!=PageRows;PageColumns=columns;PageRows=rows;PageCapacity=columns*rows;cards.Columns=columns;cards.Rows=rows;if(changed)CurrentPage=first/PageCapacity;CurrentPage=Math.Max(0,Math.Min(CurrentPage,PageCount-1));return changed;}
 public void SelectPage(int index){index=Math.Max(0,Math.Min(index,PageCount-1));if(index==CurrentPage)return;if(sorting)SuspendSortPreview();CurrentPage=index;pageRender=true;try{Render();}finally{pageRender=false;}}
 void RenderPager(){pageHover.Stop();hoverPage=-1;pager.Children.Clear();pager.Visibility=PageCount>1?Visibility.Visible:Visibility.Collapsed;for(int index=0;index<PageCount;index++){int page=index;var dot=new System.Windows.Shapes.Ellipse{Width=7,Height=7,Fill=new SolidColorBrush(page==CurrentPage?Color.FromRgb(100,106,114):Color.FromRgb(190,195,201)),IsHitTestVisible=false};var button=WindowPreview.PlainButton(dot);button.Tag="screenshot-page:"+page;button.Width=18;button.Height=22;button.Padding=new Thickness(4);button.Margin=new Thickness(0);button.ToolTip="第 "+(page+1)+" 页";System.Windows.Automation.AutomationProperties.SetName(button,"截图第 "+(page+1)+" 页"+(page==CurrentPage?"，当前页":""));button.Click+=(o,e)=>SelectPage(page);button.AllowDrop=true;button.PreviewDragOver+=(o,e)=>{if(DragId(e)==null)return;if(page!=CurrentPage&&hoverPage!=page){hoverPage=page;pageHover.Stop();pageHover.Start();}e.Effects=DragDropEffects.Copy;e.Handled=true;};button.DragLeave+=(o,e)=>{pageHover.Stop();hoverPage=-1;};button.Drop+=(o,e)=>{string id=DragId(e);if(id==null)return;var first=Store.Data.Items.Skip(page*PageCapacity).FirstOrDefault();if(first!=null)CommitSort(id,page*PageCapacity);pageHover.Stop();SelectPage(page);renderPending=true;e.Effects=DragDropEffects.Copy;e.Handled=true;};pager.Children.Add(button);}}
 public void PinRememberedWithNotice(){PinRemembered();if(!Store.Data.Visible)SetVisiblePreference(true);else if(!IsVisible&&Store.Data.Items.Count>0){Show();Place();}message.Text="";ShowPinNotice();}
 public void ShowPinNotice(){if(notice==null){notice=new ScreenshotPinNotice();notice.Closed+=(o,e)=>{notice=null;Record();};}notice.Reveal();Record();}
 public bool PinNoticeVisible{get{return notice!=null&&notice.IsVisible;}}
 public void Capture(string path){UpdateLayout();var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(ActualWidth)),Math.Max(1,(int)Math.Ceiling(ActualHeight)),96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);}
 static readonly HashSet<string> ImageExtensions=new HashSet<string>(new[]{".png",".jpg",".jpeg",".jfif",".bmp",".gif",".tif",".tiff",".ico",".webp",".heic",".heif"},StringComparer.OrdinalIgnoreCase);
 public DragDropEffects IncomingEffect(IDataObject data,DragDropEffects allowed){if(data==null)return DragDropEffects.None;var selected=(allowed&DragDropEffects.Copy)!=0?DragDropEffects.Copy:((allowed&DragDropEffects.Link)!=0?DragDropEffects.Link:DragDropEffects.None);if(selected==DragDropEffects.None)return selected;try{if(data.GetDataPresent(DataFormats.FileDrop)){var paths=data.GetData(DataFormats.FileDrop) as string[];return paths!=null&&paths.Any(path=>File.Exists(path)&&ImageExtensions.Contains(Path.GetExtension(path)))?selected:DragDropEffects.None;}if(data.GetDataPresent("PNG")||data.GetDataPresent(DataFormats.Bitmap))return selected;}catch{}return DragDropEffects.None;}
 static BitmapSource DecodeIncoming(Stream stream){var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var bitmap=decoder.Frames[0];if(bitmap.PixelWidth<1||bitmap.PixelHeight<1||(long)bitmap.PixelWidth*bitmap.PixelHeight>64000000)throw new InvalidDataException("图片尺寸过大或无效");bitmap.Freeze();return bitmap;}
 public int ImportImages(IDataObject data){if(data==null)return 0;int accepted=0,failed=0;if(data.GetDataPresent(DataFormats.FileDrop)){var paths=data.GetData(DataFormats.FileDrop) as string[]??new string[0];foreach(string path in paths){if(!File.Exists(path)||!ImageExtensions.Contains(Path.GetExtension(path))){failed++;continue;}try{using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){Accept(DecodeIncoming(stream));accepted++;}}catch{failed++;}}}else{BitmapSource image=null;if(data.GetDataPresent("PNG")){object value=data.GetData("PNG");var bytes=value as byte[];var stream=value as Stream;if(bytes!=null){using(var input=new MemoryStream(bytes,false))image=DecodeIncoming(input);}else if(stream!=null){if(stream.CanSeek)stream.Position=0;image=DecodeIncoming(stream);}}if(image==null&&data.GetDataPresent(DataFormats.Bitmap))image=data.GetData(DataFormats.Bitmap) as BitmapSource;if(image!=null){Accept(image);accepted++;}}message.Text=accepted>0?"已加入 "+accepted+" 张图片"+(failed>0?" · 跳过 "+failed+" 项":""):"没有可导入的图片";Record();return accepted;}
 public DataObject DragData(ScreenshotItem item){var data=new DataObject();data.SetData(SortFormat,token+":"+item.Id);data.SetData(DataFormats.FileDrop,new[]{Store.PathFor(item)});data.SetImage(Store.Image(item,false));data.SetData("PNG",new MemoryStream(File.ReadAllBytes(Store.PathFor(item))));data.SetData("Preferred DropEffect",new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));return data;}
 string DragId(DragEventArgs args){if(!sorting||!args.Data.GetDataPresent(SortFormat))return null;string value=args.Data.GetData(SortFormat) as string;if(value==null||!value.StartsWith(token+":",StringComparison.Ordinal))return null;string id=value.Substring(token.Length+1);return Store.Data.Items.Any(i=>i.Id==id)?id:null;}
 // Keep slot geometry fixed while cards animate. Pointer hit testing never follows a moving card.
 Rect SortSlot(int index){
  double width=cards.ActualWidth/PageColumns,height=cards.ActualHeight/PageRows;
  if(index<0)return new Rect(0,-height,width,height);
  return new Rect(index%PageColumns*width,index/PageColumns*height,width,height);
 }
 internal void BeginSort(string id,Viewbox card,Point grip){
  dragImage=TileDragMotion.CreateGhost(card,"screenshot-drag-image");
  dragOverlay.Children.Add(dragImage);
  sorting=true;dragReordered=false;draggedId=id;dragGrip=grip;sortIndex=-1;
  card.Opacity=TileDragMotion.PreviewOpacity;
 }
 internal bool PreviewSortAt(Point pointer){
  if(!sorting||cards.ActualWidth<=0||cards.ActualHeight<=0||
    !new Rect(cards.RenderSize).Contains(pointer)||Store.Data.Items.Count==0)return false;
  var origin=cards.TranslatePoint(pointer,dragOverlay)-new Vector(dragGrip.X,dragGrip.Y);
  Canvas.SetLeft(dragImage,origin.X);Canvas.SetTop(dragImage,origin.Y);dragImage.Visibility=Visibility.Visible;
  int start=CurrentPage*PageCapacity;
  int col=Math.Min(PageColumns-1,(int)(pointer.X/(cards.ActualWidth/PageColumns)));
  int row=Math.Min(PageRows-1,(int)(pointer.Y/(cards.ActualHeight/PageRows)));
  int index=Math.Min(Store.Data.Items.Count-1,start+row*PageColumns+col);
  if(sortIndex>=start&&sortIndex<start+PageCapacity){
   var previous=SortSlot(sortIndex-start);previous.Inflate(6,6);
   if(previous.Contains(pointer))index=sortIndex;
  }
  if(sortIndex==index&&sortGap.Visibility==Visibility.Visible)return true;
  sortIndex=index;
  var gap=SortSlot(index-start);
  Canvas.SetLeft(sortGap,gap.X+8);Canvas.SetTop(sortGap,gap.Y+18);
  sortGap.Width=Math.Max(1,gap.Width-16);sortGap.Height=Math.Max(1,gap.Height-23);
  sortGap.Visibility=Visibility.Visible;
  bool sourceOnPage=cards.Children.OfType<Viewbox>().Any(card=>Object.Equals(card.Tag,draggedId));
  // The origin card is unavailable when hovering a different page.
  sortGap.Child=sourceOnPage?null:new Image{Source=dragImage.Source,Opacity=TileDragMotion.PreviewOpacity,Stretch=Stretch.Uniform};
  int from=Store.Data.Items.FindIndex(item=>item.Id==draggedId);
  var positions=Store.Data.Items.Select((item,slot)=>new{item.Id,slot}).ToDictionary(x=>x.Id,x=>x.slot);
  for(int i=0;i<cards.Children.Count;i++){
   var card=(Viewbox)cards.Children[i];
   int current;
   if(!positions.TryGetValue((string)card.Tag,out current))continue;
   int final=current;
   if(current==from)final=index;
   else if(from<current&&current<=index)final--;
   else if(index<=current&&current<from)final++;
   TileDragMotion.Slide(card,SortSlot(final-start).Location-SortSlot(i).Location);
  }
  return true;
 }
 void SuspendSortPreview(){
  if(sortIndex<0&&(dragImage==null||dragImage.Visibility!=Visibility.Visible))return;
  sortIndex=-1;sortGap.Visibility=Visibility.Collapsed;sortGap.Child=null;
  if(dragImage!=null)dragImage.Visibility=Visibility.Collapsed;
  foreach(var card in cards.Children.OfType<Viewbox>())TileDragMotion.Slide(card,new Vector());
 }
 internal void CommitSort(string id,int index){
  Safe(()=>dragReordered=Store.MoveTo(id,index));
 }
 internal void FinishSort(){
  var visiblePositions=new Dictionary<string,Point>();
  if(!closed&&IsVisible){
   for(int i=0;i<cards.Children.Count;i++){
    var card=(Viewbox)cards.Children[i];
    var shift=card.RenderTransform as TranslateTransform;
    var slot=SortSlot(i).Location;
    if(shift!=null)slot+=new Vector(shift.X,shift.Y);
    visiblePositions[(string)card.Tag]=slot;
   }
   if(dragImage!=null&&dragImage.Visibility==Visibility.Visible&&draggedId!=null)
    visiblePositions[draggedId]=dragOverlay.TranslatePoint(new Point(Canvas.GetLeft(dragImage),Canvas.GetTop(dragImage)),cards);
  }
  sorting=false;sortIndex=-1;sortGap.Visibility=Visibility.Collapsed;sortGap.Child=null;
  if(dragImage!=null)dragOverlay.Children.Remove(dragImage);
  dragImage=null;draggedId=null;
  pageHover.Stop();hoverPage=-1;renderPending=false;
  if(closed)return;
  Render();
  if(!IsVisible||visiblePositions.Count==0)return;
  UpdateLayout();
  for(int i=0;i<cards.Children.Count;i++){
   var card=(Viewbox)cards.Children[i];Point before;
   if(!visiblePositions.TryGetValue((string)card.Tag,out before))continue;
   var delta=before-SortSlot(i).Location;
   card.RenderTransform=new TranslateTransform(delta.X,delta.Y);
   TileDragMotion.Slide(card,new Vector());
  }
 }
 public void Record(){try{File.WriteAllText(Path.Combine(Program.S.Root,"screenshot-line-state.json"),new JavaScriptSerializer().Serialize(new{time=DateTimeOffset.Now.ToString("o"),version=RuntimeVersion,previewMode="system-default-ordered-snapshot",processId=System.Diagnostics.Process.GetCurrentProcess().Id,executable=System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName,openingNativeViewer=opening,openedId,openRoute,openedBrowsePath,openedBrowseOrder,ListenerReady,Error,ClipboardEvents,Imported,count=Store.Data.Items.Count,page=CurrentPage+1,pages=PageCount,capacity=PageCapacity,columns=PageColumns,rows=PageRows,visibleIds=Store.Data.Items.Skip(CurrentPage*Math.Max(1,PageCapacity)).Take(Math.Max(1,PageCapacity)).Select(item=>item.Id).ToArray(),noticeVisible=notice!=null&&notice.IsVisible,order=Store.Data.Items.Select(i=>i.Id).ToArray(),capture=false,mode="F4-manual",F4Registered,PinsRequested,hasLatest=latestPng!=null,latestEncodedBytes=latestPng==null?0:latestPng.Length,ignoredDeletedClipboardSequence=ignoredDeletedSequence,latestWidth,latestHeight,position=new{Store.Data.X,Store.Data.Y},bounds=new{Left,Top,Width,Height},visible=IsVisible,preference=Store.Data.Visible,manualEmpty,sorting,renderPending,window=Native.WidgetInfo(this),storage=Store.Root,scope="Clipboard images and explicitly dropped image copies stored locally; no clipboard text or audio"}),System.Text.Encoding.UTF8);}catch{}}
 public void Shutdown(){if(closed)return;closed=true;retry.Stop();pageHover.Stop();if(notice!=null)notice.Close();if(ListenerReady)RemoveClipboardFormatListener(handle);if(ownsF4){UnregisterHotKey(handle,4);ownsF4=false;}ListenerReady=false;Close();}
}
public static class ScreenshotBrowseSnapshot {
 public sealed class Result {public string DirectoryPath,SelectedPath;public string[] OrderedIds,Files;public bool Reused;}
 public sealed class CacheRecord {public int Version=1;public string Folder;public string[] Ids,Names,Hashes;public long[] Lengths,WriteTicks;}
 static string Digest(string text){using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
 static string FileDigest(string path){using(var input=File.OpenRead(path))using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");}
 static void Plain(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("浏览目录不能是链接："+path);}
 static Result ResultFor(string folder,string[] ids,string[] names,int selected,bool reused){var files=names.Select(name=>Path.Combine(folder,name)).ToArray();return new Result{DirectoryPath=folder,SelectedPath=files[selected],OrderedIds=ids,Files=files,Reused=reused};}
 public static Result Prepare(ScreenshotStore store,ScreenshotItem selected){
  if(store==null||selected==null)throw new ArgumentNullException();
  var items=store.Data.Items.ToArray();int selectedIndex=Array.FindIndex(items,item=>item.Id==selected.Id);
  if(selectedIndex<0)throw new InvalidOperationException("截图已不在组件中");
  var ids=items.Select(item=>item.Id).ToArray();if(ids.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=ids.Length)throw new InvalidDataException("截图清单存在重复项");
  var sources=items.Select(item=>store.PathFor(item)).ToArray();var info=sources.Select(path=>new FileInfo(path)).ToArray();
  if(info.Any(file=>!file.Exists))throw new FileNotFoundException("组件中的截图文件不存在，请重新检查截图清单");
  foreach(string path in sources)Plain(path);
  var lengths=info.Select(file=>file.Length).ToArray();var stamps=info.Select(file=>file.LastWriteTimeUtc.Ticks).ToArray();
  string signature=Digest(String.Join("\n",ids.Select((id,index)=>id+"|"+lengths[index]+"|"+stamps[index])));
  string cache=Path.Combine(store.Root,"native-view");Directory.CreateDirectory(cache);Plain(cache);
  var names=ids.Select((id,index)=>(index+1).ToString("D8")+"_"+id+".png").ToArray();
  string pointer=Path.Combine(cache,signature+".json"),prefix=signature.Substring(0,16)+"-",legacyPrefix=signature+"-";var json=new JavaScriptSerializer();
  try{
   if(File.Exists(pointer)){Plain(pointer);var record=json.Deserialize<CacheRecord>(File.ReadAllText(pointer));Guid suffix;
    bool validFolder=record!=null&&record.Folder!=null&&((record.Folder.StartsWith(prefix,StringComparison.Ordinal)&&Guid.TryParseExact(record.Folder.Substring(prefix.Length),"N",out suffix))||(record.Folder.StartsWith(legacyPrefix,StringComparison.Ordinal)&&Guid.TryParseExact(record.Folder.Substring(legacyPrefix.Length),"N",out suffix)));
    if(record!=null&&record.Version==1&&validFolder&&record.Ids!=null&&record.Names!=null&&record.Hashes!=null&&record.Lengths!=null&&record.WriteTicks!=null&&record.Ids.SequenceEqual(ids)&&record.Names.SequenceEqual(names)&&record.Hashes.Length==ids.Length&&record.Lengths.SequenceEqual(lengths)&&record.WriteTicks.Length==ids.Length){
     string folder=Path.Combine(cache,record.Folder);Plain(folder);
     var entries=Directory.GetFileSystemEntries(folder);
     if(entries.Length==names.Length&&entries.Select(path=>Path.GetFileName(path)).OrderBy(name=>name,StringComparer.Ordinal).SequenceEqual(names.OrderBy(name=>name,StringComparer.Ordinal))){
      bool valid=true;for(int index=0;index<names.Length;index++){string path=Path.Combine(folder,names[index]);Plain(path);var file=new FileInfo(path);if(!file.Exists||file.Length!=lengths[index]||file.LastWriteTimeUtc.Ticks!=record.WriteTicks[index]||FileDigest(path)!=record.Hashes[index]){valid=false;break;}}
      if(valid)return ResultFor(folder,ids,names,selectedIndex,true);
     }
    }
   }
  }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
  // The full digest remains in the index; shorten only the generated directory to leave room for valid deep portable paths.
  string folderName=prefix+Guid.NewGuid().ToString("N"),destination=Path.Combine(cache,folderName);Directory.CreateDirectory(destination);
  var hashes=new string[ids.Length];var copiedStamps=new long[ids.Length];DateTime baseline=DateTime.UtcNow.AddSeconds(-ids.Length);
  for(int index=0;index<names.Length;index++){
   string target=Path.Combine(destination,names[index]);File.Copy(sources[index],target,false);
   var after=new FileInfo(sources[index]);if(after.Length!=lengths[index]||after.LastWriteTimeUtc.Ticks!=stamps[index])throw new IOException("图片在准备浏览时发生变化，请重试");
   File.SetCreationTimeUtc(target,baseline.AddSeconds(index));File.SetLastWriteTimeUtc(target,baseline.AddSeconds(index));
   hashes[index]=FileDigest(target);copiedStamps[index]=File.GetLastWriteTimeUtc(target).Ticks;
  }
  var saved=new CacheRecord{Folder=folderName,Ids=ids,Names=names,Hashes=hashes,Lengths=lengths,WriteTicks=copiedStamps};
  // A dirty/edited old set is retained. Only a complete clean set becomes the current cache.
  if(!File.Exists(pointer)||(File.GetAttributes(pointer)&FileAttributes.ReparsePoint)==0)File.WriteAllText(pointer,json.Serialize(saved),System.Text.Encoding.UTF8);
  return ResultFor(destination,ids,names,selectedIndex,false);
 }
}
public static class NativeScreenshotOpen {
 public sealed class Request {public string FilePath,DirectoryPath,Route;public string[] OrderedIds;public System.Diagnostics.ProcessStartInfo StartInfo;}
 public static Request CreateRequest(ScreenshotStore store,ScreenshotItem selected){var snapshot=ScreenshotBrowseSnapshot.Prepare(store,selected);var request=CreateRequest(snapshot.SelectedPath);request.OrderedIds=snapshot.OrderedIds;return request;}
 public static string PhotosViewerUri(string path){return "ms-photos:viewer?fileName="+Uri.EscapeDataString(Path.GetFullPath(path));}
 public static bool PhotosIsDefault(string extension){try{using(var choice=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\"+extension+@"\UserChoice")){string progId=choice==null?null:choice.GetValue("ProgId") as string;if(String.IsNullOrEmpty(progId))return false;using(var application=Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(progId+@"\Application")){string id=application==null?null:application.GetValue("AppUserModelID") as string;return id!=null&&id.StartsWith("Microsoft.Windows.Photos_",StringComparison.OrdinalIgnoreCase);}}}catch{return false;}}
 public static Request CreateRequest(string path){string fullPath=Path.GetFullPath(path);if(!File.Exists(fullPath))throw new FileNotFoundException("截图文件不存在",fullPath);bool photos=PhotosIsDefault(Path.GetExtension(fullPath));string directory=Path.GetDirectoryName(fullPath);return new Request{FilePath=fullPath,DirectoryPath=directory,Route=photos?"photos-viewer-uri":"system-default-shell",StartInfo=new System.Diagnostics.ProcessStartInfo(photos?PhotosViewerUri(fullPath):fullPath){UseShellExecute=true,WorkingDirectory=directory}};}
}
public static class ScreenshotDropTests {
 public static void Run(Action<string,bool> check){string root=Path.Combine(Program.S.Root,"截图拖入隔离"),files=Path.Combine(root,"原图");Directory.CreateDirectory(files);string png=Path.Combine(files,"图片.png"),jpg=Path.Combine(files,"照片.jpg"),text=Path.Combine(files,"文档.txt");var source=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{20,30,40,255,90,80,70,255,60,50,40,255,100,110,120,255},8);var p=new PngBitmapEncoder();p.Frames.Add(BitmapFrame.Create(source));using(var stream=File.Create(png))p.Save(stream);var j=new JpegBitmapEncoder();j.Frames.Add(BitmapFrame.Create(source));using(var stream=File.Create(jpg))j.Save(stream);File.WriteAllText(text,"nonimage");byte[] pngBefore=File.ReadAllBytes(png),jpgBefore=File.ReadAllBytes(jpg);var line=new ScreenshotLine(root);try{line.Start();check("空截图架默认隐藏",!line.IsVisible);line.PrepareManualShow();line.SetVisiblePreference(true);check("手动显示空截图架可拖入",line.IsVisible&&line.AllowDrop);var data=new DataObject();data.SetData(DataFormats.FileDrop,new[]{png,jpg,text});check("外部图片接受复制且主面板链接效果可用",line.IncomingEffect(data,DragDropEffects.Copy)==DragDropEffects.Copy&&line.IncomingEffect(data,DragDropEffects.Link|DragDropEffects.Move)==DragDropEffects.Link&&line.IncomingEffect(data,DragDropEffects.Move)==DragDropEffects.None);int count=line.ImportImages(data);check("多张拖入图片生成独立截图副本并保留原图",count==2&&line.Store.Data.Items.Count==2&&File.ReadAllBytes(png).SequenceEqual(pngBefore)&&File.ReadAllBytes(jpg).SequenceEqual(jpgBefore));check("图片副本保存后可显示且像素尺寸正确",line.IsVisible&&line.Store.Data.Items.All(item=>File.Exists(line.Store.PathFor(item))&&item.Width==2&&item.Height==2));int before=line.Store.Data.Items.Count;line.ImportImages(data);check("重复拖入图片不重复存储",line.Store.Data.Items.Count==before);var imageOnly=new DataObject();imageOnly.SetImage(BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{5,6,7,255},4));check("原生位图拖入支持复制",line.IncomingEffect(imageOnly,DragDropEffects.Copy)==DragDropEffects.Copy&&line.ImportImages(imageOnly)==1&&line.Store.Data.Items.Count==before+1);var invalid=new DataObject();invalid.SetData(DataFormats.FileDrop,new[]{text});check("非图片文件不接受为图片拖入",line.IncomingEffect(invalid,DragDropEffects.Copy)==DragDropEffects.None);line.SetVisiblePreference(false);check("手动关闭保留已导入图片和原文件",!line.IsVisible&&line.Store.Data.Items.Count==before+1&&File.Exists(png)&&File.Exists(jpg));}finally{line.Shutdown();}}
}
public static class ScreenshotPageTests {
 static async System.Threading.Tasks.Task VerifyDrag(ScreenshotLine line,Action<string,bool> check){
  var grid=AppearanceProbe.Visuals<UniformGrid>(line).Single();
  var before=line.Store.Data.Items.Select(item=>item.Id).ToArray();
  var original=(Viewbox)grid.Children[0];
  var following=(Viewbox)grid.Children[1];
  double width=grid.ActualWidth/line.PageColumns,height=grid.ActualHeight/line.PageRows;
  var grip=new Point(width/2,height/2);
  line.BeginSort(before[0],original,grip);
  bool preview=line.PreviewSortAt(new Point(width*1.5,height*.5));
  var ghost=AppearanceProbe.Visuals<Image>(line).Single(image=>Object.Equals(image.Tag,"screenshot-drag-image"));
  double firstLeft=Canvas.GetLeft(ghost);
  line.PreviewSortAt(new Point(width*1.5+10,height*.5));
  var shift=(TranslateTransform)following.RenderTransform;
  await System.Threading.Tasks.Task.Delay(60);
  var sourceShift=(TranslateTransform)original.RenderTransform;
  check("拖动图以半透明显示并让出原格，拖影随指针移动",
   preview&&ghost.IsVisible&&ghost.Opacity>0&&ghost.Opacity<1&&Canvas.GetLeft(ghost)>firstLeft+9&&
   original.Opacity>0&&original.Opacity<1&&sourceShift.GetAnimationBaseValue(TranslateTransform.XProperty).Equals(width));
  check("相邻图在释放前实际启动平移动画",shift.HasAnimatedProperties&&shift.GetAnimationBaseValue(TranslateTransform.XProperty).Equals(-width));
  check("拖动预览未写入截图顺序",new ScreenshotStore(Path.GetDirectoryName(line.Store.Root)).Data.Items.Select(item=>item.Id).SequenceEqual(before));
  await System.Threading.Tasks.Task.Delay(180);
  line.Capture(Path.Combine(Program.OutDir,"截图拖动让位预览.png"));
  line.FinishSort();
  await System.Threading.Tasks.Task.Delay(220);
  check("取消排序恢复原位和原顺序",line.Store.Data.Items.Select(item=>item.Id).SequenceEqual(before)&&
   ((Viewbox)grid.Children[0]).Opacity==1&&!AppearanceProbe.Visuals<Image>(line).Any(image=>Object.Equals(image.Tag,"screenshot-drag-image")));
  line.BeginSort(before[0],(Viewbox)grid.Children[0],grip);
  line.SelectPage(1);
  line.UpdateLayout();
  line.PreviewSortAt(grip);
  var gap=AppearanceProbe.Visuals<Border>(line).Single(border=>Object.Equals(border.Tag,"screenshot-sort-gap"));
  var pagePreview=gap.Child as Image;
  check("跨页拖动时插入位置仍显示半透明截图",pagePreview!=null&&pagePreview.IsVisible&&pagePreview.Opacity>0&&pagePreview.Opacity<1);
  int destination=line.PageCapacity;
  line.CommitSort(before[0],destination);
  line.FinishSort();
  check("跨页放置写入目标格且其他图片相对顺序保留",
   new ScreenshotStore(Path.GetDirectoryName(line.Store.Root)).Data.Items[destination].Id==before[0]&&
   line.Store.Data.Items.Where(item=>item.Id!=before[0]).Select(item=>item.Id).SequenceEqual(before.Skip(1)));
  line.Store.MoveTo(before[0],0);
  line.SelectPage(0);
  line.UpdateLayout();
 }
 public static async System.Threading.Tasks.Task Run(Action<string,bool> check){var line=new ScreenshotLine(Path.Combine(Program.S.Root,"截图分页隔离"));try{for(int index=0;index<19;index++){var pixels=new byte[180*100*4];for(int pixel=0;pixel<pixels.Length;pixel+=4){pixels[pixel]=(byte)(25+index*9);pixels[pixel+1]=(byte)(110+index*5);pixels[pixel+2]=(byte)(220-index*7);pixels[pixel+3]=255;}bool added;line.Store.Add(BitmapSource.Create(180,100,96,96,PixelFormats.Bgra32,null,pixels,180*4),out added);}line.Show();line.Width=520;line.Height=390;line.Left=60;line.Top=60;line.UpdateLayout();await System.Threading.Tasks.Task.Delay(100);line.Render();line.UpdateLayout();check("截图晾衣架左上角只显示标题",AppearanceProbe.Visuals<TextBlock>(line).Any(text=>text.Text=="截图晾衣架")&&!AppearanceProbe.Visuals<TextBlock>(line).Any(text=>text.Text.Contains("双击系统看图")));check("截图晾衣架按宽高排列成多行并分页",line.PageColumns==3&&line.PageRows==2&&line.PageCapacity==6&&line.PageCount==4);check("只解码并显示当前页且没有左右滚动控件",AppearanceProbe.Visuals<Button>(line).Count(button=>Object.Equals(button.Tag,"screenshot-delete"))==6&&!AppearanceProbe.Visuals<ScrollViewer>(line).Any());await VerifyDrag(line,check);var pageButtons=AppearanceProbe.Visuals<Button>(line).Where(button=>button.Tag is string&&((string)button.Tag).StartsWith("screenshot-page:")).ToArray();check("底部灰色圆点对应每页且当前圆点更深",pageButtons.Length==4&&((SolidColorBrush)((System.Windows.Shapes.Ellipse)pageButtons[0].Content).Fill).Color.R<((SolidColorBrush)((System.Windows.Shapes.Ellipse)pageButtons[1].Content).Fill).Color.R);pageButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));line.UpdateLayout();check("点击第二页灰点实际切换显示图片",line.CurrentPage==1&&AppearanceProbe.Visuals<Button>(line).Any(button=>Object.Equals(button.Tag,line.Store.Data.Items[6].Id))&&!AppearanceProbe.Visuals<Button>(line).Any(button=>Object.Equals(button.Tag,line.Store.Data.Items[0].Id)));line.SelectPage(3);line.UpdateLayout();check("最后一页仅显示余下截图且每张均能删除",AppearanceProbe.Visuals<Button>(line).Count(button=>Object.Equals(button.Tag,"screenshot-delete"))==1);
 line.SelectPage(0);line.Capture(Path.Combine(Program.OutDir,"截图自适应分页.png"));line.Width=830;line.Height=540;line.UpdateLayout();await System.Threading.Tasks.Task.Delay(100);line.UpdateLayout();check("拖大窗口自动增加行列和每页容量",line.PageColumns==5&&line.PageRows==3&&line.PageCapacity==15&&line.PageCount==2);line.Capture(Path.Combine(Program.OutDir,"截图扩大自动排列.png"));line.SelectPage(1);line.Width=360;line.Height=230;line.UpdateLayout();await System.Threading.Tasks.Task.Delay(100);line.UpdateLayout();check("缩小窗口自动重新分页且页码有效",line.PageColumns==2&&line.PageRows==1&&line.PageCapacity==2&&line.CurrentPage<line.PageCount);line.Capture(Path.Combine(Program.OutDir,"截图缩小自动分页.png"));var moving=line.Store.Data.Items.Last();var target=line.Store.Data.Items.First();line.Store.Move(moving.Id,target.Id,false);line.SelectPage(0);line.Render();check("分页后排序仍可跨页保存且外拖格式保留",new ScreenshotStore(Path.Combine(Program.S.Root,"截图分页隔离")).Data.Items[0].Id==moving.Id&&line.DragData(moving).GetDataPresent(DataFormats.FileDrop)&&line.DragData(moving).GetDataPresent("PNG"));line.Remove(line.Store.Data.Items.First());line.UpdateLayout();check("分页删除移至回收站并自动修正页数",line.Store.Data.Items.Count==18&&line.PageCount==9&&line.CurrentPage<line.PageCount);
 line.Remember(BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{12,34,56,255},4));IntPtr before=Native.GetForegroundWindow();line.PinRememberedWithNotice();await System.Threading.Tasks.Task.Delay(100);check("F4共用成功路径保存截图并显示不抢焦点的提示",line.Store.Data.Items.Count==19&&line.PinNoticeVisible&&Native.GetForegroundWindow()==before);var toast=Application.Current.Windows.OfType<ScreenshotPinNotice>().Single();check("保存提示居于屏幕正上方且文字正确",AppearanceProbe.Visuals<TextBlock>(toast).Any(text=>text.Text=="已放入截屏晾衣架")&&!toast.ShowInTaskbar&&Native.ToolWindow(toast)&&toast.Top<80);var material=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(new JavaScriptSerializer().Serialize(toast.Material.Info()));
 check("顶部提示使用与组件同款原生磨砂且位于内容层下方",(bool)material["ready"]&&(bool)material["visible"]&&(bool)material["directlyBelowPanel"]&&(bool)material["topmost"]&&(bool)material["inputTransparent"]);
 var noticeFrame=(Border)toast.Content;
 var rect=(Dictionary<string,object>)material["bounds"];
 int w=(int)rect["width"],h=(int)rect["height"];
 var backdropHandle=new IntPtr(toast.Material.Handle);
 bool fourCorners=Native.ClipContains(backdropHandle,w/2,h/2)&&
  !Native.ClipContains(backdropHandle,4,4)&&!Native.ClipContains(backdropHandle,w-5,4)&&
  !Native.ClipContains(backdropHandle,4,h-5)&&!Native.ClipContains(backdropHandle,w-5,h-5);
 check("提示玻璃无叠加染色且原生材质四角裁圆",
  ((SolidColorBrush)noticeFrame.Background).Color.A==0&&fourCorners);
 material["noTint"]=((SolidColorBrush)noticeFrame.Background).Color.A==0;
 material["fourCornersRounded"]=fourCorners;
 var noticeHandle=new WindowInteropHelper(toast).Handle;
 var firstBounds=Native.WindowPixelBounds(noticeHandle);
 var primary=System.Windows.Forms.Screen.PrimaryScreen.Bounds;
 var dpi=HwndSource.FromHwnd(noticeHandle).CompositionTarget.TransformToDevice;
 bool centered=firstBounds.Left==primary.Left+(primary.Width-firstBounds.Width)/2&&
  firstBounds.Top==primary.Top+(int)Math.Round(ScreenshotPinNotice.TopOffset*dpi.M22);
 bool borderless=noticeFrame.BorderThickness.Equals(new Thickness(0))&&
  toast.WindowBorderSuppressed&&toast.Material.WindowBorderSuppressed;
 bool frontCorners=!Native.ClipContains(noticeHandle,4,4)&&!Native.ClipContains(noticeHandle,w-5,4)&&
  !Native.ClipContains(noticeHandle,4,h-5)&&!Native.ClipContains(noticeHandle,w-5,h-5)&&
  Native.ClipContains(noticeHandle,w/2,h/2);
 check("前后层统一圆角裁切且无系统或界面描边",borderless&&frontCorners&&fourCorners);
 check("首次提示按实际像素固定在主屏幕上方居中",centered);
 material["borderless"]=borderless;
 material["foregroundCornersRounded"]=frontCorners;
 material["primaryTopCenter"]=centered;
 material["firstBounds"]=firstBounds.ToString();
 File.WriteAllText(Path.Combine(Program.OutDir,"截图提示磨砂验证.json"),new JavaScriptSerializer().Serialize(material));
 var toastBitmap=new RenderTargetBitmap((int)toast.Width,(int)toast.Height,96,96,PixelFormats.Pbgra32);toastBitmap.Render(toast);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(toastBitmap));using(var file=File.Create(Path.Combine(Program.OutDir,"截图保存顶部提示.png")))encoder.Save(file);await System.Threading.Tasks.Task.Delay(1200);line.ShowPinNotice();await System.Threading.Tasks.Task.Delay(2000);check("连续保存复用窗口且保持同一屏幕位置",line.PinNoticeVisible&&Application.Current.Windows.OfType<ScreenshotPinNotice>().Count()==1&&Native.WindowPixelBounds(noticeHandle)==firstBounds);await System.Threading.Tasks.Task.Delay(1200);check("保存提示三秒后关闭并释放窗口",!line.PinNoticeVisible&&!Application.Current.Windows.OfType<ScreenshotPinNotice>().Any()&&toast.Material.Handle==0);
 File.WriteAllText(Path.Combine(Program.OutDir,"截图分页布局核对.json"),new JavaScriptSerializer().Serialize(new{time=DateTimeOffset.Now.ToString("o"),line.PageColumns,line.PageRows,line.PageCapacity,line.PageCount,line.CurrentPage,noticeSeconds=3,scope="Real isolated WPF window resize/layout, routed page-button click, PNG roundtrip and recycle of fixtures. Shared F4 completion path and real dispatcher timer; no physical drag or user-image deletion."}),System.Text.Encoding.UTF8);
 }finally{line.Shutdown();}}
}
public sealed class ScreenshotPinNotice:Window {
 const double Radius=23;
 internal const double TopOffset=18;
 readonly DispatcherTimer expiry;
 readonly Border frame;
 internal readonly PanelBackdrop Material;
 internal bool WindowBorderSuppressed{get;private set;}
 Size lastClippedSize;
 public ScreenshotPinNotice(){
  Title="rushbin 截图已保存";Width=252;Height=46;
  WindowStyle=WindowStyle.None;WindowStartupLocation=WindowStartupLocation.Manual;
  ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;
  Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;
  Topmost=true;IsHitTestVisible=false;
  var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,
   VerticalAlignment=VerticalAlignment.Center};
  row.Effect=new System.Windows.Media.Effects.DropShadowEffect{
   Color=Colors.Black,BlurRadius=4,ShadowDepth=1,Opacity=.65};
  var mark=new System.Windows.Shapes.Path{Data=Geometry.Parse("M 4,10 L 8,14 L 16,6"),
   Stroke=Brushes.White,StrokeThickness=1.6,
   StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,
   VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,9,0)};
  row.Children.Add(mark);
  row.Children.Add(new TextBlock{Text="已放入截屏晾衣架",Foreground=Brushes.White,
   FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=13,VerticalAlignment=VerticalAlignment.Center});
  frame=new Border{CornerRadius=new CornerRadius(Radius),Child=row};
  Content=frame;
  Material=new PanelBackdrop(this,()=>1,Radius,()=>new Rect(RenderSize));
  expiry=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};
  expiry.Tick+=(o,e)=>{expiry.Stop();Close();};
  SourceInitialized+=(o,e)=>{
   Native.PreviewStyle(this);
   Native.PassThroughMouse(this);
   var handle=new WindowInteropHelper(this).Handle;
   WindowBorderSuppressed=Native.SuppressWindowChrome(handle);
   Closed+=(a,b)=>Native.ForgetAppearance(handle);
   var source=HwndSource.FromHwnd(handle);
   source.AddHook((IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled)=>{
    if(msg==0x21||msg==0x84){handled=true;return new IntPtr(msg==0x84?-1:3);}
    if(msg==0x7e||msg==0x2e0)Dispatcher.BeginInvoke(new Action(()=>{
     if(IsVisible)AnchorAndPaint();
    }),DispatcherPriority.Loaded);
    return IntPtr.Zero;
   });
  };
  LocationChanged+=(o,e)=>UpdateMaterial();
  SizeChanged+=(o,e)=>UpdateMaterial();
  Closed+=(o,e)=>{expiry.Stop();Material.Dispose();};
 }
 void UpdateMaterial(){
  if(!IsVisible||ActualWidth<1||ActualHeight<1)return;
  var handle=new WindowInteropHelper(this).Handle;
  var bounds=Native.WindowPixelBounds(handle);
  var size=new Size(bounds.Width,bounds.Height);
  if(!lastClippedSize.Equals(size)&&bounds.Width>0&&bounds.Height>0){
   // The foreground window and material both use the capsule height as the corner diameter.
   if(Native.ClipRoundedWindow(handle,0,0,bounds.Width,bounds.Height,bounds.Height,bounds.Height))
    lastClippedSize=size;
  }
  bool glass=Material.Update()&&Material.Ready;
  frame.Background=glass?Brushes.Transparent:new SolidColorBrush(Color.FromArgb(208,30,30,30));
 }
 void AnchorAndPaint(){
  var handle=new WindowInteropHelper(this).Handle;
  var bounds=Native.WindowPixelBounds(handle);
  var screen=System.Windows.Forms.Screen.PrimaryScreen.Bounds;
  var scale=HwndSource.FromHwnd(handle).CompositionTarget.TransformToDevice;
  int x=screen.Left+(screen.Width-bounds.Width)/2;
  int y=screen.Top+(int)Math.Round(TopOffset*scale.M22);
  if(bounds.X!=x||bounds.Y!=y)Native.SetWindowPos(handle,IntPtr.Zero,x,y,0,0,0x15);
  UpdateMaterial();
 }
 public void Reveal(){
  var handle=new WindowInteropHelper(this).EnsureHandle();
  if(!IsVisible){
   // Position the first frame with the HWND's DPI, which is available before Show.
   var scale=HwndSource.FromHwnd(handle).CompositionTarget.TransformToDevice;
   var screen=System.Windows.Forms.Screen.PrimaryScreen.Bounds;
   Left=(screen.Left+(screen.Width-Math.Round(Width*scale.M11))/2)/scale.M11;
   Top=(screen.Top+Math.Round(TopOffset*scale.M22))/scale.M22;
   Show();
  }
  UpdateLayout();
  AnchorAndPaint();
  expiry.Stop();expiry.Start();
 }
}

}
