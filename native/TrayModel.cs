using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopBox
{
    internal sealed class TrayModel : IDisposable
    {
        internal enum Operation { Snapshot = 1, Move, Left, Double, Right, Middle, TipOpen, TipClose }
        const uint PacketType = 0x52544D32;
        const int HeaderSize = 16, ItemSize = 66072, Limit = 96, PixelOffset = 532;
        const int ImageHashOffset = ItemSize - sizeof(uint);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Request
        {
            public uint Serial, Action;
            public ulong Source, Target;
            public int After, X, Y;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 56)] public string Map;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CopyData
        {
            public IntPtr Type;
            public uint Length;
            public IntPtr Data;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string name, string title);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr sender,
            ref CopyData data, uint flags, uint timeout, out IntPtr result);

        sealed class Sender : System.Windows.Forms.NativeWindow, IDisposable
        {
            internal Sender()
            {
                CreateHandle(new System.Windows.Forms.CreateParams
                {
                    ClassName = "STATIC",
                    Caption = "rushbin native tray client v1",
                    Style = unchecked((int)0x80000000),
                    ExStyle = 0x08000080
                });
            }

            public void Dispose() { DestroyHandle(); }
        }

        internal sealed class Entry
        {
            public ulong Token;
            public string Tip;
            public BitmapSource Image;
            public uint ImageHash;
            public bool OwnsTip;
            public string Identity { get { return Token.ToString("X16"); } }
        }

        internal sealed class Snapshot
        {
            public readonly List<Entry> Entries = new List<Entry>();
            public uint Explorer;
        }

        readonly object gate = new object();
        readonly Sender sender = new Sender();
        readonly string mapName = @"Local\Rushbin.Tray." + Guid.NewGuid().ToString("N");
        MemoryMappedFile mapping;
        MemoryMappedViewAccessor view;
        Snapshot previous;
        uint serial;
        bool disposed;

        uint Send(Operation action, ulong source, ulong target, bool after, Point point)
        {
            if (disposed) throw new ObjectDisposedException("TrayModel");
            if (view == null)
            {
                mapping = MemoryMappedFile.CreateNew(mapName, HeaderSize + Limit * ItemSize);
                view = mapping.CreateViewAccessor();
            }
            var tray = FindWindow("Shell_TrayWnd", null);
            uint explorer;
            GetWindowThreadProcessId(tray, out explorer);
            if (tray == IntPtr.Zero || explorer == 0) throw new InvalidOperationException("找不到 Windows 任务栏");
            if (previous != null && previous.Explorer != explorer && action != Operation.Snapshot)
                throw new InvalidOperationException("Explorer 已重启，请在同步后重试");

            var request = new Request
            {
                Serial = ++serial,
                Action = (uint)action,
                Source = source, Target = target, After = after ? 1 : 0,
                X = (int)point.X, Y = (int)point.Y,
                Map = mapName
            };
            int length = Marshal.SizeOf(typeof(Request));
            IntPtr memory = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(request, memory, false);
                var packet = new CopyData { Type = new IntPtr(PacketType), Length = (uint)length, Data = memory };
                IntPtr result;
                if (SendMessageTimeout(tray, 0x4a, sender.Handle, ref packet, 0x23, 550, out result) == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 任务栏未响应");
            }
            finally { Marshal.FreeHGlobal(memory); }

            if (view.ReadUInt32(0) != serial || view.ReadUInt32(12) != explorer)
                throw new InvalidOperationException("未收到 Explorer 托盘模型的响应");
            int hresult = view.ReadInt32(4);
            if (hresult == unchecked((int)0x80070032))
                throw new NotSupportedException("当前 Windows 版本的原生托盘接口尚未适配");
            if (hresult < 0)
                throw new InvalidOperationException("Explorer 托盘操作失败 (0x" + hresult.ToString("X8") + ")");
            return explorer;
        }

        internal Snapshot Read()
        {
            lock (gate)
            {
                uint explorer = Send(Operation.Snapshot, 0, 0, false, new Point());
                int count = view.ReadInt32(8);
                if (count < 0 || count > Limit)
                    throw new InvalidOperationException("Explorer 返回了无效的托盘项数量");
                var known = new Dictionary<ulong, Entry>();
                if (previous != null && previous.Explorer == explorer)
                    foreach (var item in previous.Entries) known[item.Token] = item;

                var snapshot = new Snapshot { Explorer = explorer };
                for (int index = 0; index < count; index++)
                {
                    long offset = HeaderSize + index * ItemSize;
                    uint flags = view.ReadUInt32(offset + 16);
                    var entry = new Entry
                    {
                        Token = view.ReadUInt64(offset),
                        ImageHash = view.ReadUInt32(offset + ImageHashOffset),
                        OwnsTip = (flags & 2) != 0
                    };
                    var tip = new byte[512];
                    view.ReadArray(offset + 20, tip, 0, tip.Length);
                    string text = System.Text.Encoding.Unicode.GetString(tip);
                    int end = text.IndexOf('\0');
                    entry.Tip = (end < 0 ? text : text.Substring(0,end)).Trim();
                    Entry cached;
                    int width = view.ReadInt32(offset + 8), height = view.ReadInt32(offset + 12);
                    if (known.TryGetValue(entry.Token, out cached) && cached.ImageHash == entry.ImageHash &&
                        cached.Image != null && cached.Image.PixelWidth == width && cached.Image.PixelHeight == height)
                    {
                        entry.Image = cached.Image;
                    }
                    else if (width > 0 && width <= 128 && height > 0 && height <= 128)
                    {
                        var pixels = new byte[width * height * 4];
                        view.ReadArray(offset + PixelOffset, pixels, 0, pixels.Length);
                        var format = (flags & 1) != 0 ? PixelFormats.Pbgra32 : PixelFormats.Bgra32;
                        entry.Image = BitmapSource.Create(width, height, 96, 96, format, null, pixels, width * 4);
                        entry.Image.Freeze();
                    }
                    snapshot.Entries.Add(entry);
                }
                previous = snapshot;
                return snapshot;
            }
        }

        internal Snapshot Move(Entry source, Entry target, bool after)
        {
            lock (gate)
            {
                Send(Operation.Move, source.Token, target.Token, after, new Point());
                return Read();
            }
        }

        internal void Activate(Entry entry, Operation action, Point point)
        {
            lock (gate) Send(action, entry.Token, 0, false, point);
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                if (view != null) view.Dispose();
                if (mapping != null) mapping.Dispose();
                sender.Dispose();
            }
        }
    }
}
