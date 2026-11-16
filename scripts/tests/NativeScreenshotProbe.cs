using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Windows;using System.Windows.Controls;using System.Windows.Input;using System.Windows.Interop;using System.Windows.Media;using System.Windows.Media.Imaging;using System.Windows.Threading;using System.Web.Script.Serialization;using DesktopBox;
public static class NativeScreenshotProbe {
 static readonly List<object> results=new List<object>();static bool passed=true;static string output;
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
 static void Check(string name,bool value){results.Add(new{name,passed=value});passed&=value;}
 static Button Button(DependencyObject root,string tag){return AppearanceProbe.Visuals<Button>(root).Single(b=>Object.Equals(b.Tag,tag));}
 static void Click(DependencyObject root,string tag){Button(root,tag).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
 static Color Dot(ScreenshotLine line,int page){return ((SolidColorBrush)((System.Windows.Shapes.Ellipse)Button(line,"screenshot-page:"+page).Content).Fill).Color;}
 static void Wheel(UIElement target,int delta){target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta){RoutedEvent=UIElement.PreviewMouseWheelEvent});}
 [STAThread]public static void Main(string[] args){output=args[0];Directory.CreateDirectory(output);Program.Testing=true;Program.ShotsCheck=true;Program.OutDir=output;Program.S=new Store(Path.Combine(output,"data"));var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Startup+=async(o,e)=>{
  ScreenshotLine line=null;
  try{
   line=new ScreenshotLine(Program.S.Root);
   for(int index=0;index<13;index++){int w=180+index,h=100+index;var pixels=new byte[w*h*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=(byte)(20+index*13);pixels[i+1]=(byte)(80+index*7);pixels[i+2]=(byte)(220-index*11);pixels[i+3]=255;}bool added;line.Store.Add(BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,w*4),out added);}
   line.Show();line.Width=1020;line.Height=225;line.UpdateLayout();await System.Threading.Tasks.Task.Delay(120);line.Render();line.UpdateLayout();
   Check("13张图片分为三页",line.PageCapacity==6&&line.PageCount==3);
   var heading=AppearanceProbe.Visuals<TextBlock>(line).First(t=>t.Text.StartsWith("截图晾衣线"));Wheel(heading,-120);line.UpdateLayout();
   Check("标题区域向下滚轮切换第二页且图片更新",line.CurrentPage==1&&AppearanceProbe.Visuals<Button>(line).Any(b=>Object.Equals(b.Tag,line.Store.Data.Items[6].Id)));
   Check("滚轮切页后第二个圆点颜色变深",Dot(line,1).R<Dot(line,0).R);
   Wheel(Button(line,"screenshot-page:1"),-120);Check("圆点区域滚轮切换第三页",line.CurrentPage==2);Wheel(line,-120);Check("末页向下不越界",line.CurrentPage==2);
   Wheel(Button(line,line.Store.Data.Items[12].Id),120);Check("图片区域向上滚轮返回第二页",line.CurrentPage==1);
   SendMessage(new WindowInteropHelper(line).Handle,0x20a,new IntPtr(120L<<16),IntPtr.Zero);Check("原生WM_MOUSEWHEEL切换一次并同步圆点",line.CurrentPage==0&&Dot(line,0).R<Dot(line,1).R);
   Wheel(line,120);Check("首页向上不越界",line.CurrentPage==0);Wheel(line,0);Check("零滚动不切页",line.CurrentPage==0);
   Wheel(line,-40);Wheel(line,-40);Check("高精度滚轮不足一个刻度不跳页",line.CurrentPage==0);Wheel(line,-40);Check("高精度滚轮累计一个刻度切一页",line.CurrentPage==1);
   Wheel(line,-60);Wheel(line,120);Check("反向滚动清除残余累计并返回上一页",line.CurrentPage==0);
   Wheel(line,-240);Check("连续两个滚轮刻度依次前进两页",line.CurrentPage==2);line.SelectPage(0);line.UpdateLayout();
   var selected=line.Store.Data.Items[6];string selectedPath=line.Store.PathFor(selected);
   var request=NativeScreenshotOpen.CreateRequest(selectedPath);
   Check("原生请求准确选中双击文件",request.FilePath==selectedPath);
   Check("原生请求保留截图同目录工作位置",request.DirectoryPath==line.Store.Root&&request.StartInfo.WorkingDirectory==line.Store.Root);
   Check("所有截图实际存在于同一目录",line.Store.Data.Items.All(item=>File.Exists(line.Store.PathFor(item))&&Path.GetDirectoryName(line.Store.PathFor(item))==line.Store.Root));
   bool photos=NativeScreenshotOpen.PhotosIsDefault(".png");
   Check("按实际默认应用选择照片原生入口",request.Route==(photos?"photos-viewer-uri":"system-default-shell"));
   Check("系统Shell直接接收目标且没有命令参数拼接",request.StartInfo.UseShellExecute&&String.IsNullOrEmpty(request.StartInfo.Arguments)&&request.StartInfo.FileName==(photos?NativeScreenshotOpen.PhotosViewerUri(selectedPath):selectedPath));
   string special=Path.Combine(line.Store.Root,"中文 空格 & #+%.png");File.Copy(selectedPath,special);
   string uri=NativeScreenshotOpen.PhotosViewerUri(special);
   Check("照片URI无损表达中文空格及特殊路径",uri.StartsWith("ms-photos:viewer?fileName=")&&Uri.UnescapeDataString(uri.Substring("ms-photos:viewer?fileName=".Length))==special&&!uri.Contains("&")&&!uri.Contains("#"));
   var specialRequest=NativeScreenshotOpen.CreateRequest(special);Check("特殊路径不会成为额外命令参数",String.IsNullOrEmpty(specialRequest.StartInfo.Arguments));
   Check("已完全移除自建看图窗口类型",typeof(ScreenshotLine).Assembly.GetType("DesktopBox.ScreenshotViewer")==null);
   bool rejected=false;try{NativeScreenshotOpen.CreateRequest(Path.Combine(line.Store.Root,"missing.png"));}catch(FileNotFoundException){rejected=true;}Check("不存在图片不触发系统启动",rejected);
   Check("原始图片及排列保持不变",ScreenshotStore.ImageHash(line.Store.Image(selected,false))==selected.Hash&&line.Store.Data.Items.IndexOf(selected)==6);
   var sourceOrder=line.Store.Data.Items.Select(item=>item.Id).ToArray();var originalBytes=line.Store.Data.Items.ToDictionary(item=>item.Id,item=>File.ReadAllBytes(line.Store.PathFor(item)));
   string orphan=Path.Combine(line.Store.Root,Guid.NewGuid().ToString("N")+".png");File.Copy(selectedPath,orphan);
   var snapshot=ScreenshotBrowseSnapshot.Prepare(line.Store,selected);
   Check("只导出组件清单中的13张图排除目录旧图",snapshot.Files.Length==13&&Directory.GetFiles(snapshot.DirectoryPath).Length==13&&snapshot.OrderedIds.SequenceEqual(sourceOrder));
   Check("按名称排序后每张图片字节与组件从左到右完全对应",snapshot.Files.OrderBy(path=>Path.GetFileName(path),StringComparer.Ordinal).Select(File.ReadAllBytes).Select((bytes,index)=>bytes.SequenceEqual(originalBytes[sourceOrder[index]])).All(value=>value));
   Check("所点击中间图片对应第7个浏览文件",snapshot.SelectedPath==snapshot.Files[6]&&Path.GetFileName(snapshot.SelectedPath).StartsWith("00000007_"));
   Check("浏览副本按创建及修改时间也是同一顺序",snapshot.Files.Select(File.GetCreationTimeUtc).SequenceEqual(snapshot.Files.Select(File.GetCreationTimeUtc).OrderBy(value=>value))&&snapshot.Files.Select(File.GetLastWriteTimeUtc).SequenceEqual(snapshot.Files.Select(File.GetLastWriteTimeUtc).OrderBy(value=>value)));
   Check("原截图和未列入组件的旧图均保留",sourceOrder.All(id=>File.ReadAllBytes(line.Store.PathFor(line.Store.Data.Items.Single(item=>item.Id==id))).SequenceEqual(originalBytes[id]))&&File.Exists(orphan)&&File.Exists(special));
   var repeated=ScreenshotBrowseSnapshot.Prepare(line.Store,line.Store.Data.Items.Last());Check("同一清单连续打开不同图片复用同一浏览目录",repeated.Reused&&repeated.DirectoryPath==snapshot.DirectoryPath&&repeated.SelectedPath==snapshot.Files.Last());
   var productRequest=NativeScreenshotOpen.CreateRequest(line.Store,selected);Check("正式打开路径使用有序副本而非整个原目录",productRequest.FilePath==snapshot.SelectedPath&&productRequest.DirectoryPath==snapshot.DirectoryPath&&productRequest.OrderedIds.SequenceEqual(sourceOrder));
   if(photos)Check("照片URI解码后精确指向所选浏览副本",Uri.UnescapeDataString(productRequest.StartInfo.FileName.Substring("ms-photos:viewer?fileName=".Length))==snapshot.SelectedPath);
   string intruder=Path.Combine(snapshot.DirectoryPath,"external.jpg");File.Copy(selectedPath,intruder);var clean=ScreenshotBrowseSnapshot.Prepare(line.Store,selected);
   Check("浏览目录混入额外图片后自动改用干净目录",clean.DirectoryPath!=snapshot.DirectoryPath&&Directory.GetFiles(clean.DirectoryPath).Length==13&&File.Exists(intruder));
   string edited=clean.Files[0];File.WriteAllText(edited,"edited browsing copy");var cleanAfterEdit=ScreenshotBrowseSnapshot.Prepare(line.Store,selected);
   Check("修改过的浏览副本保留且新浏览集恢复原截图内容",cleanAfterEdit.DirectoryPath!=clean.DirectoryPath&&File.ReadAllText(edited)=="edited browsing copy"&&File.ReadAllBytes(cleanAfterEdit.Files[0]).SequenceEqual(originalBytes[sourceOrder[0]]));
   var last=line.Store.Data.Items.Last();line.Store.Move(last.Id,line.Store.Data.Items[0].Id,false);line.Render();var reordered=ScreenshotBrowseSnapshot.Prepare(line.Store,last);
   Check("组件拖动排序后浏览顺序立即采用新清单",reordered.DirectoryPath!=cleanAfterEdit.DirectoryPath&&reordered.OrderedIds.SequenceEqual(line.Store.Data.Items.Select(item=>item.Id))&&reordered.SelectedPath==reordered.Files[0]&&File.ReadAllBytes(reordered.Files[0]).SequenceEqual(originalBytes[last.Id]));
   Check("重新排序后右边相邻图片与组件第二张一致",File.ReadAllBytes(reordered.Files[1]).SequenceEqual(originalBytes[line.Store.Data.Items[1].Id]));
   var removed=line.Store.Data.Items[3];line.Store.Data.Items.Remove(removed);line.Store.Save();line.Render();var afterRemove=ScreenshotBrowseSnapshot.Prepare(line.Store,last);
   Check("从清单移除后原文件即使残留也不会进入浏览集",afterRemove.Files.Length==12&&!afterRemove.OrderedIds.Contains(removed.Id)&&File.Exists(line.Store.PathFor(removed)));
   var newlyAdded=line.Accept(BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{1,211,63,255},4));var afterAdd=ScreenshotBrowseSnapshot.Prepare(line.Store,newlyAdded);
   Check("新加入的截图只出现一次且位于组件末尾",afterAdd.Files.Length==13&&afterAdd.OrderedIds.Last()==newlyAdded.Id&&afterAdd.SelectedPath==afterAdd.Files.Last());
   Check("组件有多页时原生浏览集包含全部页且顺序连续",afterAdd.Files.Length>line.PageCapacity&&afterAdd.OrderedIds.SequenceEqual(line.Store.Data.Items.Select(item=>item.Id)));
   bool removedRejected=false;try{ScreenshotBrowseSnapshot.Prepare(line.Store,removed);}catch(InvalidOperationException){removedRejected=true;}Check("已不在组件的截图不能进入原生浏览",removedRejected);
   string originalSource=line.Store.PathFor(last),heldSource=originalSource+".probe-held";File.Move(originalSource,heldSource);bool missingSourceRejected=false;try{ScreenshotBrowseSnapshot.Prepare(line.Store,last);}catch(FileNotFoundException){missingSourceRejected=true;}finally{File.Move(heldSource,originalSource);}Check("清单图片丢失时不退回包含旧图的原目录",missingSourceRejected);
   string record=Path.Combine(Path.GetDirectoryName(afterAdd.DirectoryPath),Path.GetFileName(afterAdd.DirectoryPath).Substring(0,64)+".json");File.WriteAllText(record,"invalid cache record");var rebuilt=ScreenshotBrowseSnapshot.Prepare(line.Store,newlyAdded);Check("缓存索引损坏时重建干净有序浏览集",rebuilt.Files.Length==13&&rebuilt.OrderedIds.SequenceEqual(line.Store.Data.Items.Select(item=>item.Id))&&rebuilt.DirectoryPath!=afterAdd.DirectoryPath);
   File.WriteAllText(Path.Combine(output,"native-request.json"),new JavaScriptSerializer().Serialize(new{route=productRequest.Route,photosIsDefault=photos,allComponentIds=rebuilt.OrderedIds,browseNames=rebuilt.Files.Select(Path.GetFileName).ToArray(),physicalPhotosNavigationVerified=false,sourceUnlistedImageCount=2}));
  }catch(Exception ex){passed=false;File.WriteAllText(Path.Combine(output,"exception.txt"),ex.ToString());}
  finally{if(line!=null)line.Shutdown();File.WriteAllText(Path.Combine(output,"results.json"),new JavaScriptSerializer().Serialize(new{passed,results,scope="Real isolated WPF wheel events, actual file bytes, ordered native browsing snapshots, orphan exclusion, cache corruption/extra-file/edit handling, reordering/add/remove and Photos URI path roundtrip. No external app launched by this probe; no physical Photos navigation verification and no production files changed."}));Environment.ExitCode=passed?0:1;app.Shutdown();}
 };app.Run();}
}
