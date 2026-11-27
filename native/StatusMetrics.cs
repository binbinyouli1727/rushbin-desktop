using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopBox {
internal sealed class NetworkRateMeter {
 // netioapi.h MIB_IF_ROW2, including the counters following OutOctets.
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
 struct InterfaceRow {
  public ulong Luid;
  public uint Index;
  public Guid Guid;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=257)] public string Alias,Description;
  public uint AddressLength;
  [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)] public byte[] Address,PermanentAddress;
  public uint Mtu,Type,TunnelType,MediaType,PhysicalMediumType,AccessType,DirectionType;
  public byte Flags;
  public uint OperStatus,AdminStatus,MediaConnectState;
  public Guid NetworkGuid;
  public uint ConnectionType;
  public ulong TransmitLinkSpeed,ReceiveLinkSpeed,InOctets,InUnicast,InNonUnicast,InDiscards,InErrors,
   InUnknown,InUnicastOctets,InMulticastOctets,InBroadcastOctets,OutOctets,OutUnicast,OutNonUnicast,
   OutDiscards,OutErrors,OutUnicastOctets,OutMulticastOctets,OutBroadcastOctets,OutQueueLength;
 }
 [DllImport("iphlpapi.dll",CharSet=CharSet.Unicode)]
 static extern uint GetIfEntry2(ref InterfaceRow row);
 [DllImport("iphlpapi.dll")]
 static extern uint GetBestInterface(uint destination,out uint index);

 Guid previousInterface;
 long previousTime;
 ulong previousReceived,previousSent;
 public string InterfaceName{get;private set;}
 public bool HardwareInterface{get;private set;}
 public double SampleSeconds{get;private set;}
 public ulong ReceivedOctets{get;private set;}
 public ulong SentOctets{get;private set;}

 public void Read(out double? received,out double? sent){
  received=sent=null;SampleSeconds=0;InterfaceName=null;HardwareInterface=false;ReceivedOctets=SentOctets=0;
  InterfaceRow chosen=default(InterfaceRow);int chosenRank=0;
  try{
   uint route;
   // The route lookup only reads the local routing table.
   if(GetBestInterface(0x01010101,out route)!=0)route=0;
   foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces()){
    if(adapter.OperationalStatus!=OperationalStatus.Up||adapter.NetworkInterfaceType==NetworkInterfaceType.Loopback)continue;
    Guid id;if(!Guid.TryParse(adapter.Id,out id))continue;
    var row=new InterfaceRow{Guid=id,Address=new byte[32],PermanentAddress=new byte[32]};
    // GetIfEntry2 accepts a LUID or an index, not a GUID.
    if(ConvertInterfaceGuidToLuid(ref id,out row.Luid)!=0||GetIfEntry2(ref row)!=0)continue;
    bool physical=(row.Flags&1)!=0&&(row.Flags&2)==0&&(row.Flags&128)==0;
    bool gateway=adapter.GetIPProperties().GatewayAddresses.Any(g=>!g.Address.Equals(System.Net.IPAddress.Any)&&
     !g.Address.Equals(System.Net.IPAddress.IPv6Any));
    int rank=physical?(row.Index==route?5:gateway?4:3):row.Index==route?2:gateway?1:0;
    // Keep the last adapter in the event of a tie.
    if(rank<chosenRank||rank==0||(rank==chosenRank&&row.Guid!=previousInterface))continue;
    chosen=row;chosenRank=rank;
   }
  }catch(NetworkInformationException){}
  if(chosenRank==0){previousInterface=Guid.Empty;return;}
  InterfaceName=chosen.Alias;HardwareInterface=chosenRank>=3;
  ReceivedOctets=chosen.InOctets;SentOctets=chosen.OutOctets;
  long now=Stopwatch.GetTimestamp();
  if(previousInterface==chosen.Guid&&now>previousTime&&ReceivedOctets>=previousReceived&&SentOctets>=previousSent){
   SampleSeconds=(now-previousTime)/(double)Stopwatch.Frequency;
   received=(ReceivedOctets-previousReceived)/SampleSeconds;
   sent=(SentOctets-previousSent)/SampleSeconds;
  }
  previousInterface=chosen.Guid;previousTime=now;previousReceived=ReceivedOctets;previousSent=SentOctets;
 }
 [DllImport("iphlpapi.dll")]
 static extern uint ConvertInterfaceGuidToLuid(ref Guid guid,out ulong luid);
}

internal sealed class GpuTemperatureMeter:IDisposable {
 const string Library="nvml.dll";
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int nvmlInit_v2();
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int nvmlShutdown();
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int nvmlDeviceGetCount_v2(out uint count);
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int nvmlDeviceGetHandleByIndex_v2(uint index,out IntPtr gpu);
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl)]static extern int nvmlDeviceGetTemperature(IntPtr gpu,uint sensor,out uint celsius);
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 [DllImport(Library,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Ansi)]static extern int nvmlDeviceGetName(IntPtr gpu,StringBuilder name,uint length);
 bool initialized,unavailable;
 IntPtr device;
 double? current;
 DateTime nextRead;
 public string Source{get;private set;}
 public string DeviceName{get;private set;}

 public double? Read(){
  if(unavailable||DateTime.UtcNow<nextRead)return current;
  current=null;nextRead=DateTime.UtcNow.AddSeconds(3);
  try{
   if(!initialized){
    initialized=nvmlInit_v2()==0;
    if(!initialized){nextRead=DateTime.UtcNow.AddSeconds(20);return null;}
   }
   uint value;
   if(device!=IntPtr.Zero&&nvmlDeviceGetTemperature(device,0,out value)==0&&value<=150)return current=value;
   device=IntPtr.Zero;Source=DeviceName=null;
   uint count;
   if(nvmlDeviceGetCount_v2(out count)==0)for(uint index=0;index<count;index++){
    IntPtr candidate;
    if(nvmlDeviceGetHandleByIndex_v2(index,out candidate)!=0||nvmlDeviceGetTemperature(candidate,0,out value)!=0||value>150)continue;
    var name=new StringBuilder(96);
    DeviceName=nvmlDeviceGetName(candidate,name,(uint)name.Capacity)==0?name.ToString():"NVIDIA GPU "+index;
    Source="NVIDIA NVML";device=candidate;return current=value;
   }
  }catch(DllNotFoundException){unavailable=true;}catch(EntryPointNotFoundException){unavailable=true;}
   catch(BadImageFormatException){unavailable=true;}
  nextRead=DateTime.UtcNow.AddSeconds(20);return null;
 }
 public void Dispose(){if(initialized){nvmlShutdown();initialized=false;}}
}
}
