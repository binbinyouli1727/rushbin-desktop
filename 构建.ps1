param([string]$OutputPath=(Join-Path $PSScriptRoot 'DeskBox.exe'))
$ErrorActionPreference='Stop'
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework=Split-Path $compiler
$refs=@('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','System.IO.Compression.dll','System.Runtime.dll','System.Runtime.WindowsRuntime.dll','System.Numerics.Vectors.dll','System.Numerics.dll') | ForEach-Object {'/r:'+(Join-Path $framework $_)}
$refs+=@('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','UIAutomationClient.dll','UIAutomationTypes.dll') | ForEach-Object {'/r:'+(Join-Path $framework ('WPF\'+$_))}
$refs+=@('Windows.UI.winmd','Windows.Foundation.winmd','Windows.Graphics.winmd','Windows.Devices.winmd','Windows.Networking.winmd') | ForEach-Object {'/r:'+(Join-Path 'C:\Windows\System32\WinMetadata' $_)}
$refs+=('/r:'+(Join-Path $framework 'System.Management.dll'))
$refs+=('/r:'+(Join-Path $framework 'System.Runtime.InteropServices.WindowsRuntime.dll'))
$tinycc=Join-Path $PSScriptRoot 'tools\tinycc\tcc\tcc.exe'
& $tinycc -shared -o (Join-Path $PSScriptRoot 'native\TrayBridge64.dll') (Join-Path $PSScriptRoot 'native\TrayBridge.c') -luser32
if($LASTEXITCODE -ne 0){throw '托盘事件桥编译失败'}
& $tinycc -o (Join-Path $PSScriptRoot 'native\TaskbarGuard.exe') (Join-Path $PSScriptRoot 'native\TaskbarGuard.c') -luser32 -lshell32
if($LASTEXITCODE -ne 0){throw '任务栏恢复保护编译失败'}
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output ('/out:'+$OutputPath) ('/win32manifest:'+(Join-Path $PSScriptRoot 'deskbox.manifest')) ('/win32icon:'+(Join-Path $PSScriptRoot 'deskbox.ico')) ('/resource:'+(Join-Path $PSScriptRoot 'native\TrayBridge64.dll')+',DeskBox.TrayBridge64') ('/resource:'+(Join-Path $PSScriptRoot 'native\TaskbarGuard.exe')+',DeskBox.TaskbarGuard') @refs (Join-Path $PSScriptRoot 'DeskBox.cs') (Join-Path $PSScriptRoot 'native\TrayMirror.cs') (Join-Path $PSScriptRoot 'native\TrayFixture.cs') (Join-Path $PSScriptRoot 'native\TrayArtwork.cs') (Join-Path $PSScriptRoot 'native\TrayArrangement.cs') (Join-Path $PSScriptRoot 'native\TrayRecovery.cs') (Join-Path $PSScriptRoot 'native\WifiScan.cs') (Join-Path $PSScriptRoot 'native\DockHold.cs') (Join-Path $PSScriptRoot 'native\RecycleEntry.cs') (Join-Path $PSScriptRoot 'native\ScreenshotLine.cs') (Join-Path $PSScriptRoot 'native\Components.cs') (Join-Path $PSScriptRoot 'native\TaskbarVisibility.cs') (Join-Path $PSScriptRoot 'native\FileShelf.cs')
if($LASTEXITCODE -ne 0){throw '编译失败'}
