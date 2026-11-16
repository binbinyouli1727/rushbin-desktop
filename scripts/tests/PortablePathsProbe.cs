using System;
using System.IO;
using DesktopBox;

public static class PortablePathsProbe {
 public static int Main() {
  try {
   string expected=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"用户数据");
   Check("default root beside executable",AppPaths.DataRoot==expected);
   var primary=new Store(expected);
   var shelf=new Store(Path.Combine(expected,"files-widget"));
   var isolated=new Store(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"isolated-test"));
   Check("main store keeps desktop behavior",DesktopStorage.Production(primary));
   Check("file shelf keeps desktop behavior",DesktopStorage.Production(shelf));
   Check("new files still go to Windows desktop",NewFiles.Folder(primary)==DesktopStorage.Desktop);
   Check("isolated data stays isolated",!DesktopStorage.Production(isolated));
   Check("isolated new files stay isolated",NewFiles.Folder(isolated)==Path.Combine(isolated.Root,"CreatedFiles"));
   Check("tray bridge still embedded",typeof(Program).Assembly.GetManifestResourceStream("DeskBox.TrayBridge64")!=null);
   Check("taskbar guard still embedded",typeof(Program).Assembly.GetManifestResourceStream("DeskBox.TaskbarGuard")!=null);
   Console.WriteLine("PASS all portable-path checks"); return 0;
  } catch(Exception e) { Console.WriteLine(e); return 1; }
 }
 static void Check(string name,bool pass) { if(!pass)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name); }
}
