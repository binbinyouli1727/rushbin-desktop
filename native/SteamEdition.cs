using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace DesktopBox {
public static class SteamEdition {
#if STEAM_EDITION
 public static readonly bool Enabled = true;
#else
 public static readonly bool Enabled = false;
#endif
 public const string ManagerShortcut = "Ctrl+Shift+F1";
 public const string PanelShortcut = "Ctrl+Shift+F5";
 public const string PinShortcut = "Ctrl+Shift+F4";

 public static string DataRoot {
  get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rushbin"); }
 }

 public static System.Windows.DragDropEffects ReferenceEffect(System.Windows.DragDropEffects allowed) {
  var effect = allowed & System.Windows.DragDropEffects.Link;
  return effect != 0 ? effect : allowed & System.Windows.DragDropEffects.Copy;
 }

 public static void WriteJson(string path, object value) {
  string temporary = path + ".tmp";
  var json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
  using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
  using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) {
   writer.Write(json.Serialize(value));
   writer.Flush();
   stream.Flush(true);
  }
  if (File.Exists(path)) File.Replace(temporary, path, null);
  else File.Move(temporary, path);
 }
}

public sealed class DesktopMoveHistory {
 public sealed class MoveRecord {
  public string Source { get; set; }
  public string Destination { get; set; }
  public string Identity { get; set; }
  public bool Directory { get; set; }
 }

 [StructLayout(LayoutKind.Sequential)]
 struct FileInformation {
  public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
  public uint Volume, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow;
 }
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
 static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
    uint disposition, uint flags, IntPtr template);
 [DllImport("kernel32.dll", SetLastError=true)]
 static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);

 readonly string journalPath;
 readonly DesktopFiles desktop;
 readonly JavaScriptSerializer json = new JavaScriptSerializer();

 public DesktopMoveHistory(string root, DesktopFiles desktop) {
  journalPath = Path.Combine(root, "desktop-moves.json");
  this.desktop = desktop;
 }

 static string Identity(string path) {
  using (var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)) {
   FileInformation info;
   if (handle.IsInvalid || !GetFileInformationByHandle(handle, out info))
    throw new IOException("无法确认文件身份：" + path, new System.ComponentModel.Win32Exception());
   if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
    throw new IOException("链接或联接请在资源管理器中处理");
   return String.Format("{0:X8}:{1:X8}{2:X8}:{3:X8}{4:X8}",
       info.Volume, info.IndexHigh, info.IndexLow, info.CreationHigh, info.CreationLow);
  }
 }

 List<MoveRecord> Read() {
  if (!File.Exists(journalPath)) return new List<MoveRecord>();
  var records = json.Deserialize<List<MoveRecord>>(File.ReadAllText(journalPath));
  if (records == null || records.Any(record => record == null || String.IsNullOrEmpty(record.Source)
      || String.IsNullOrEmpty(record.Destination) || String.IsNullOrEmpty(record.Identity)))
   throw new InvalidDataException("移动记录无法读取，原文件和记录已保留。");
  return records;
 }

 static bool Same(string first, string second) {
  return String.Equals(first, second, StringComparison.OrdinalIgnoreCase);
 }

 static void MoveExact(string from, string to, bool directory) {
  if (directory) Directory.Move(from, to);
  else File.Move(from, to);
 }

 public bool CanOffer(string path) {
  if (EntryPath.IsShell(path) || !DesktopFiles.Ordinary(path) || !EntryPath.Exists(path)) return false;
  return !DesktopStorage.Under(path, desktop.Root) && !DesktopStorage.Under(desktop.Root, path)
      && !DesktopStorage.Under(path, SteamEdition.DataRoot)
      && !DesktopStorage.Under(path, AppDomain.CurrentDomain.BaseDirectory);
 }

 public MoveRecord Prepare(string path) {
  if (!CanOffer(path)) throw new IOException("此项目不适合通过 Rushbin 移动到桌面");
  string source = FolderTransfer.Validate(new[]{path}, desktop.Root).Single();
  if (!Same(Path.GetPathRoot(source), Path.GetPathRoot(desktop.Root)))
   throw new IOException("为保留可验证的撤销，跨盘移动请使用资源管理器。");
  bool isDirectory = Directory.Exists(source);
  string name = Path.GetFileName(source);
  string extension = isDirectory ? "" : Path.GetExtension(name);
  string stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
  for (int index = 0; index < 10000; index++) {
   string destination = Path.Combine(desktop.Root, index == 0 ? name : stem + " (" + (index + 1) + ")" + extension);
   if (!EntryPath.Exists(destination))
    return new MoveRecord { Source = source, Destination = destination, Identity = Identity(source), Directory = isDirectory };
  }
  throw new IOException("同名项目过多，未移动源文件");
 }

 public void Execute(MoveRecord record) {
  if (!CanOffer(record.Source) || !Same(Path.GetDirectoryName(record.Destination), desktop.Root)
      || !Same(Path.GetPathRoot(record.Source), Path.GetPathRoot(record.Destination)))
   throw new IOException("来源或目标已变化，请重新检查");
  FolderTransfer.Validate(new[]{record.Source}, desktop.Root);
  if (EntryPath.Exists(record.Destination) || Identity(record.Source) != record.Identity)
   throw new IOException("来源或目标已变化，请重新检查");
  var records = Read();
  records.Add(record);
  SteamEdition.WriteJson(journalPath, records);
  // Persist the intended paths before touching the filesystem. Undo can reconcile an interrupted move.
  try {
   MoveExact(record.Source, record.Destination, record.Directory);
  } catch {
   // A normal failure leaves the original intact. Drop that intent so older moves can still be undone.
   if (EntryPath.Exists(record.Source) && Identity(record.Source) == record.Identity) {
    records.RemoveAt(records.Count - 1);
    SteamEdition.WriteJson(journalPath, records);
   }
   throw;
  }
  desktop.Renamed(record.Source, record.Destination, record.Directory);
 }

 public MoveRecord Latest() {
  var records = Read();
  return records.LastOrDefault();
 }

 public void Undo() {
  var records = Read();
  if (records.Count == 0) throw new InvalidOperationException("没有可撤销的桌面移动");
  MoveRecord record = records.Last();
  if (!Same(Path.GetDirectoryName(record.Destination), desktop.Root)
      || DesktopStorage.Under(record.Source, desktop.Root)
      || !Same(Path.GetPathRoot(record.Source), Path.GetPathRoot(record.Destination)))
   throw new IOException("记录的桌面位置已变化，请在资源管理器中检查原路径");
  bool atSource = EntryPath.Exists(record.Source);
  bool atDestination = EntryPath.Exists(record.Destination);
  if (atSource == atDestination)
   throw new IOException("原位置存在冲突或目标已丢失；没有覆盖或移动任何文件。");
  string current = atSource ? record.Source : record.Destination;
  if (Identity(current) != record.Identity)
   throw new IOException("该路径中的文件已被替换，无法安全撤销。");
  if (atDestination) {
   FolderTransfer.Validate(new[]{record.Destination}, Path.GetDirectoryName(record.Source));
   MoveExact(record.Destination, record.Source, record.Directory);
  }
  desktop.Renamed(record.Destination, record.Source, record.Directory);
  records.RemoveAt(records.Count - 1);
  SteamEdition.WriteJson(journalPath, records);
 }
}

public static class PortableMigration {
 static readonly string[] ModelFiles = {"data.json", "files-widget\\data.json"};
 static readonly string[] Options = {"files-widget-appearance.json", "screenshot-line\\line.json"};

 public static List<string> Plan(string source, string destination) {
  source = Path.GetFullPath(source);
  destination = Path.GetFullPath(destination);
  if (!Directory.Exists(source) || !File.Exists(Path.Combine(source, "data.json")))
   throw new IOException("请选择包含 data.json 的便携版“用户数据”文件夹。");
  if (DesktopStorage.Under(source, destination) || DesktopStorage.Under(destination, source))
   throw new IOException("迁移来源和目标必须是独立目录。");
  if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
   throw new IOException("Steam 数据目录已有内容。请先退出 Rushbin 并将现有目录另存为备份。");
  var files = new List<string>();
  foreach (string name in ModelFiles.Concat(Options)) {
   AddFile(source, name, files);
   AddFile(source, name + ".bak", files);
  }
  string screenshots = Path.Combine(source, "screenshot-line");
  if (Directory.Exists(screenshots) && (File.GetAttributes(screenshots) & FileAttributes.ReparsePoint) == 0) {
   foreach (string file in Directory.EnumerateFiles(screenshots, "*.png")) {
    Guid id;
    if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out id))
     AddFile(source, Path.Combine("screenshot-line", Path.GetFileName(file)), files);
   }
  }
  return files;
 }

 static void AddFile(string source, string relative, List<string> files) {
  string file = Path.Combine(source, relative);
  if (!File.Exists(file)) return;
  for (string part = file; DesktopStorage.Under(part, source); part = Path.GetDirectoryName(part)) {
   if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
    throw new IOException("旧数据包含链接，请先复制到普通文件夹。");
   if (String.Equals(part, source, StringComparison.OrdinalIgnoreCase)) break;
  }
  files.Add(relative);
 }

 public static int Import(string source, string destination) {
  source = Path.GetFullPath(source);
  destination = Path.GetFullPath(destination);
  var files = Plan(source, destination);
  string stage = destination + ".import-" + Guid.NewGuid().ToString("N");
  Directory.CreateDirectory(stage);
  try {
   foreach (string relative in files) {
    string target = Path.Combine(stage, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(target));
    File.Copy(Path.Combine(source, relative), target, false);
   }
   foreach (string relative in ModelFiles) {
    string file = Path.Combine(stage, relative);
    if (!File.Exists(file)) continue;
    var store = new Store(Path.GetDirectoryName(file));
    if (store.Warning != "" && !store.Warning.Contains("已从备份恢复"))
     throw new InvalidDataException("旧收纳数据无法读取，迁移已停止：" + relative);
   }
   if (Directory.Exists(destination)) Directory.Delete(destination, false);
   Directory.Move(stage, destination);
   return files.Count;
  } finally {
   if (Directory.Exists(stage)) Directory.Delete(stage, true);
  }
 }
}

public static class SteamSetup {
 public static void ImportPortable(string destination) {
  try {
   using (var chooser = new System.Windows.Forms.FolderBrowserDialog {
    Description = "选择旧版的“用户数据”文件夹（包含 data.json）",
    ShowNewFolderButton = false
   }) {
    if (chooser.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
    string source = chooser.SelectedPath;
    int count = PortableMigration.Plan(source, destination).Count;
    var result = System.Windows.MessageBox.Show(
        "将复制 " + count + " 个收纳配置和截图文件。旧目录继续保留，可作为备份。快捷键需在新版重新设置。\n\n来源：" + source + "\n\n目标：" + destination,
        "预览旧数据迁移", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question);
    if (result != System.Windows.MessageBoxResult.OK) return;
    int copied = PortableMigration.Import(source, destination);
    System.Windows.MessageBox.Show("已迁移 " + copied + " 个文件。现在可以启动 Rushbin。", "Rushbin");
   }
  } catch (Exception ex) {
   System.Windows.MessageBox.Show(ex.Message, "迁移未完成");
  }
 }

 static System.Windows.Window guide;

 public static void ShowGuide(Box box) {
  if (guide != null) { guide.Activate(); return; }
  var options = Program.Components.Options;
  Func<string,string> key = value => String.IsNullOrWhiteSpace(value) ? "未设置" : value;
  var lines = new[] {
   "用 " + key(options.Items["panel"].Shortcut) + " 打开收纳面板。拖入文件或文件夹即可添加入口，拖到分组标签可直接归类。",
   "先把截图放进剪贴板，再按 " + key(options.PinShortcut) + " 挂上图片。也可以把图片拖进截图晾衣架。",
   "托盘右键菜单可打开组件管理、暂停全局快捷键或退出。截图和收纳配置保存在本机。"
  };
  var content = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(24) };
  foreach (string line in lines)
   content.Children.Add(new System.Windows.Controls.TextBlock {
    Text = line, TextWrapping = System.Windows.TextWrapping.Wrap, Margin = new System.Windows.Thickness(0,0,0,14)
   });
  var start = new System.Windows.Controls.Button {
   Content = "开始使用", Padding = new System.Windows.Thickness(14,6,14,6), IsDefault = true,
   HorizontalAlignment = System.Windows.HorizontalAlignment.Right
  };
  content.Children.Add(start);
  var window = new System.Windows.Window {
   Title = "Rushbin 快速开始", Width = 500, SizeToContent = System.Windows.SizeToContent.Height,
   Content = content, ShowInTaskbar = true, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
   ResizeMode = System.Windows.ResizeMode.NoResize, Background = System.Windows.Media.Brushes.White,
   Foreground = System.Windows.Media.Brushes.Black, FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
   FontSize = 14
  };
  guide = window;
  window.Closed += (sender, args) => { guide = null; };
  start.Click += (sender, args) => { window.Close(); box.Reveal(); };
  window.Show();
  window.Activate();
 }

 public static void Onboarding(Box box) {
  string path = Path.Combine(Program.S.Root, "steam-guide.json");
  if (File.Exists(path)) return;
  ShowGuide(box);
  SteamEdition.WriteJson(path, new { shown = true });
 }

 public static void ScheduleSmoke(Box box, DockBar dock) {
  var process = System.Diagnostics.Process.GetCurrentProcess();
  var cpuBefore = process.TotalProcessorTime;
  var elapsed = System.Diagnostics.Stopwatch.StartNew();
  var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
  timer.Tick += (sender, args) => {
   timer.Stop();elapsed.Stop();process.Refresh();
   double cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds / elapsed.Elapsed.TotalMilliseconds;
   Smoke(box, dock, cpu, elapsed.Elapsed.TotalMilliseconds);
   process.Dispose();
  };
  timer.Start();
 }

 static void Smoke(Box box, DockBar dock, double cpuCoresEquivalent, double sampleMilliseconds) {
  var checks = new List<object>();
  bool passed = true;
  Action<string,bool> check = (name, condition) => { checks.Add(new { name, passed = condition }); passed &= condition; };
  try {
   RunSmoke(box, dock, check);
  } catch (Exception ex) {
   check(ex.ToString(), false);
  }
  var process = System.Diagnostics.Process.GetCurrentProcess();
  process.Refresh();
  var screens = System.Windows.Forms.Screen.AllScreens;
  SteamEdition.WriteJson(Path.Combine(Program.S.Root, "steam-smoke.json"), new {
   passed, checks, defaultData = SteamEdition.DataRoot, screenCount = screens.Length,
   memoryWorkingSetBytes = process.WorkingSet64, cpuCoresEquivalent, sampleMilliseconds,
   scope = "Windows current session; real WPF startup and fixture filesystem, without Steam client or physical input"
  });
  Environment.ExitCode = passed ? 0 : 1;
  box.Quit();
 }

 static void RunSmoke(Box box, DockBar dock, Action<string,bool> check) {
  var hub = Program.Components;
  check("任务栏隐藏服务、托盘桥和底栏按住键未启动", Program.Taskbar == null && dock.Hold == null &&
      !dock.Attention.Registered && Program.Control.TrayHost.Visibility == System.Windows.Visibility.Collapsed);
  var resources = typeof(SteamEdition).Assembly.GetManifestResourceNames();
  check("候选运行包未嵌入任务栏或托盘桥本机二进制",
      !resources.Contains("DeskBox.TrayBridge64") && !resources.Contains("DeskBox.TaskbarGuard"));
  check("默认按需读剪贴板且 F4 未注册", !Program.Shots.ListenerReady && !Program.Shots.F4Registered && hub.PinHotkeyRegistered);
  check("默认面板与管理快捷键使用组合键", hub.Options.Items["panel"].Shortcut == SteamEdition.PanelShortcut &&
      hub.Options.ManagerShortcut == SteamEdition.ManagerShortcut && hub.ManagerHotkeyRegistered);
  string error;
  check("阻止将 F12 设置为全局键", !hub.ChangeShortcut("panel", "F12", out error));
  string changedKey = "Ctrl+Alt+Shift+F9";
  check("面板快捷键可更改并还原", hub.ChangeShortcut("panel", changedKey, out error) &&
      hub.Options.Items["panel"].Shortcut == changedKey && hub.ChangeShortcut("panel", SteamEdition.PanelShortcut, out error));
  hub.ToggleShortcutPause();
  check("可暂停全局快捷键", hub.ShortcutsPaused && !hub.ManagerHotkeyRegistered && !hub.PinHotkeyRegistered);
  hub.ToggleShortcutPause();
  check("可恢复全局快捷键", !hub.ShortcutsPaused && hub.ManagerHotkeyRegistered && hub.PinHotkeyRegistered);
  check("首次启动将控制中心留在托盘菜单", !Program.Control.IsVisible);
  hub.ToggleManager();
  var pinInput = AppearanceProbe.Visuals<System.Windows.Controls.TextBox>(hub.Manager).SingleOrDefault(input=>Object.Equals(input.Tag,"pin-screenshot-shortcut"));
  hub.Manager.UpdateLayout();
  check("挂图快捷键输入框在组件管理首屏", pinInput!=null && pinInput.Text==SteamEdition.PinShortcut &&
      pinInput.TranslatePoint(new System.Windows.Point(0,pinInput.ActualHeight),hub.Manager).Y<hub.Manager.ActualHeight);
  FrostSkin.Capture(hub.Manager, Path.Combine(Program.S.Root,"组件和快捷键.png"));
  hub.ToggleManager();
  check("默认数据目录在用户 LocalAppData", String.Equals(AppPaths.DataRoot, SteamEdition.DataRoot, StringComparison.OrdinalIgnoreCase));
  check("只支持 Link 或 Copy 的引用效果", SteamEdition.ReferenceEffect(System.Windows.DragDropEffects.Move) == System.Windows.DragDropEffects.None &&
      SteamEdition.ReferenceEffect(System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move) == System.Windows.DragDropEffects.Copy);

  string baseDir = Directory.CreateDirectory(Path.Combine(Program.S.Root, "fixtures")).FullName;
  string source = Path.Combine(baseDir, "example.txt");
  string collision = Path.Combine(Program.DesktopFiles.Root, "example.txt");
  File.WriteAllText(source, "original");
  File.WriteAllText(collision, "existing desktop file");
  box.Receive(new[]{source});
  Program.Files.Receive(new[]{source});
  check("主面板和文件窗口保留原始引用", File.ReadAllText(source) == "original" &&
      Program.S.Data.Items.Any(item => item.Path == source) && Program.Files.Files.Data.Items.Any(item => item.Path == source));
  var move = Program.DesktopMoves.Prepare(source);
  check("桌面同名文件选择独立目标", move.Destination != collision && !File.Exists(move.Destination));
  Program.DesktopMoves.Execute(move);
  check("明确移动后两个收纳入口同步", !File.Exists(source) && File.ReadAllText(move.Destination) == "original" &&
      File.ReadAllText(collision) == "existing desktop file" &&
      Program.S.Data.Items.Any(item => item.Path == move.Destination) &&
      Program.Files.Files.Data.Items.Any(item => item.Path == move.Destination));
  File.WriteAllText(source, "new occupant");
  bool blocked = false;
  try { Program.DesktopMoves.Undo(); } catch (IOException) { blocked = true; }
  check("撤销拒绝覆盖新原路径且可重试", blocked && File.ReadAllText(source) == "new occupant" &&
      File.ReadAllText(move.Destination) == "original" && Program.DesktopMoves.Latest() != null);
  File.Delete(source);
  var reloaded = new DesktopMoveHistory(Program.S.Root, Program.DesktopFiles);
  reloaded.Undo();
  check("可从磁盘记录撤销实际移动", File.ReadAllText(source) == "original" && !File.Exists(move.Destination) &&
      reloaded.Latest() == null && Program.S.Data.Items.Any(item => item.Path == source));
  string folder = Directory.CreateDirectory(Path.Combine(baseDir, "folder")).FullName;
  File.WriteAllText(Path.Combine(folder,"nested.txt"),"nested");
  var folderMove = reloaded.Prepare(folder);
  reloaded.Execute(folderMove);
  reloaded.Undo();
  check("可撤销文件夹移动并保留内容", File.ReadAllText(Path.Combine(folder,"nested.txt")) == "nested" &&
      !Directory.Exists(folderMove.Destination));
  string replaced = Path.Combine(baseDir, "replacement.txt");
  File.WriteAllText(replaced,"original identity");
  var altered = reloaded.Prepare(replaced);
  reloaded.Execute(altered);
  File.Move(altered.Destination, altered.Destination+".saved");
  File.WriteAllText(altered.Destination,"different identity");
  blocked = false;
  try { reloaded.Undo(); } catch (IOException) { blocked = true; }
  check("撤销拒绝移动被替换的目标文件", blocked && !File.Exists(replaced) &&
      File.ReadAllText(altered.Destination) == "different identity");
  File.Delete(altered.Destination);
  File.Move(altered.Destination+".saved", altered.Destination);
  reloaded.Undo();

  string locked = Path.Combine(baseDir, "locked.txt");
  File.WriteAllText(locked, "locked");
  var lockedMove = reloaded.Prepare(locked);
  blocked = false;
  using (var lockStream = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
   try { reloaded.Execute(lockedMove); } catch (IOException) { blocked = true; }
  check("文件被占用时保留原件并清理失败移动的记录", blocked && File.ReadAllText(locked) == "locked" &&
      !File.Exists(lockedMove.Destination) && reloaded.Latest() == null);

  string old = Directory.CreateDirectory(Path.Combine(baseDir, "portable")).FullName;
  var oldStore = new Store(old);
  int rejected;
  oldStore.Add(new[]{source}, "inbox", out rejected);
  var oldShots = new ScreenshotStore(old);
  bool added;
  var pixels = System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,
      System.Windows.Media.PixelFormats.Bgra32,null,new byte[]{17,82,184,255},4);
  var oldShot = oldShots.Add(pixels, out added);
  File.WriteAllText(Path.Combine(old, "runtime-errors.log"), "private log");
  string migrationTarget = Path.Combine(baseDir, "migrated");
  int count = PortableMigration.Plan(old, migrationTarget).Count;
  check("迁移预览保留来源且不创建目标", count > 0 && !Directory.Exists(migrationTarget) && File.Exists(oldStore.FilePath));
  int copied = PortableMigration.Import(old, migrationTarget);
  check("迁移只复制白名单配置并保留原文件", copied == count &&
      new Store(migrationTarget).Data.Items.Any(item => item.Path == source) &&
      File.Exists(oldStore.FilePath) && !File.Exists(Path.Combine(migrationTarget, "runtime-errors.log")));
  var newShots = new ScreenshotStore(migrationTarget);
  check("迁移后截图清单和原 PNG 可恢复", added && newShots.Data.Items.Count == 1 &&
      newShots.Data.Items[0].Id == oldShot.Id &&
      File.ReadAllBytes(newShots.PathFor(oldShot)).SequenceEqual(File.ReadAllBytes(oldShots.PathFor(oldShot))));
  blocked = false;
  try { PortableMigration.Import(old, migrationTarget); } catch (IOException) { blocked = true; }
  check("迁移拒绝覆盖已有数据", blocked && File.Exists(Path.Combine(migrationTarget, "data.json")));
 }
}

}
