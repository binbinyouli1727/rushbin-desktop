using System;
using System.Collections.Generic;
using System.Linq;
namespace DesktopBox {
public static class TrayArrangement {
 public static string Identity(TrayMirror.IconItem item){return item.Guid!=Guid.Empty?"g:"+item.Guid.ToString("D"):!String.IsNullOrWhiteSpace(item.Executable)?"e:"+item.Executable.ToLowerInvariant()+"|"+item.Id:"session:"+item.Key;}
 public static List<string> Normalize(IEnumerable<string> values){return (values??Enumerable.Empty<string>()).Where(x=>!String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();}
 public static List<TrayMirror.IconItem> Apply(IEnumerable<TrayMirror.IconItem> items,IEnumerable<string> order){var positions=Normalize(order);return items.OrderBy(item=>{int index=positions.FindIndex(key=>String.Equals(key,Identity(item),StringComparison.OrdinalIgnoreCase));return index<0?Int32.MaxValue:index;}).ToList();}
 public static List<string> Move(IEnumerable<string> saved,IEnumerable<string> visible,string moving,string target,bool after){var current=Normalize(visible);var master=Normalize(Normalize(saved).Concat(current));if(String.Equals(moving,target,StringComparison.OrdinalIgnoreCase)||!current.Contains(moving,StringComparer.OrdinalIgnoreCase)||!current.Contains(target,StringComparer.OrdinalIgnoreCase))return master;master.RemoveAll(key=>String.Equals(key,moving,StringComparison.OrdinalIgnoreCase));int at=master.FindIndex(key=>String.Equals(key,target,StringComparison.OrdinalIgnoreCase));master.Insert(at+(after?1:0),moving);return master;}
}
}
