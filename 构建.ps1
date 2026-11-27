param(
    [string]$OutputPath,
    [switch]$Steam
)

$ErrorActionPreference = 'Stop'
if (-not $OutputPath) {
    if ($Steam) { throw 'Set -OutputPath to a separate Steam candidate directory.' }
    $OutputPath = Join-Path $PSScriptRoot 'DeskBox.exe'
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$productionPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'DeskBox.exe'))
if ($Steam -and $OutputPath -eq $productionPath) { throw 'Steam build must not replace the portable executable.' }
$null = New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path -Parent $compiler
$refs = @(
    'System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Web.Extensions.dll',
    'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.IO.Compression.dll', 'System.Runtime.dll',
    'System.Runtime.WindowsRuntime.dll', 'System.Numerics.Vectors.dll', 'System.Numerics.dll',
    'System.Management.dll', 'System.Runtime.InteropServices.WindowsRuntime.dll'
) | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$refs += @('PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll', 'UIAutomationClient.dll', 'UIAutomationTypes.dll') |
    ForEach-Object { '/r:' + (Join-Path $framework ('WPF\' + $_)) }
$refs += @('Windows.UI.winmd', 'Windows.Foundation.winmd', 'Windows.Graphics.winmd', 'Windows.Devices.winmd', 'Windows.Networking.winmd') |
    ForEach-Object { '/r:' + (Join-Path $env:WINDIR ('System32\WinMetadata\' + $_)) }

$sources = @(
    'DeskBox.cs', 'native/TrayModel.cs', 'native/TrayMirror.cs', 'native/TrayFixture.cs', 'native/TrayArtwork.cs',
    'native/TrayArrangement.cs', 'native/TrayRecovery.cs', 'native/WifiScan.cs', 'native/DockHold.cs',
    'native/RecycleEntry.cs', 'native/ScreenshotLine.cs', 'native/Components.cs',
    'native/TaskbarVisibility.cs', 'native/FileShelf.cs', 'native/DesktopFiles.cs',
    'native/NativeFileMenu.cs', 'native/StatusMetrics.cs', 'native/SteamEdition.cs'
) | ForEach-Object { Join-Path $PSScriptRoot $_ }

$buildFlags = @()
$nativeStage = $null
try {
    if ($Steam) {
        $buildFlags += '/define:STEAM_EDITION'
    } else {
        $nativeStage = Join-Path ([IO.Path]::GetTempPath()) ('rushbin-build-' + [Guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $nativeStage
        $trayBridge = Join-Path $nativeStage 'TrayBridge64.dll'
        $taskbarGuard = Join-Path $nativeStage 'TaskbarGuard.exe'
        $tinycc = Join-Path $PSScriptRoot 'tools\tinycc\tcc\tcc.exe'
        & $tinycc -shared -o $trayBridge (Join-Path $PSScriptRoot 'native\TrayBridge.c') (Join-Path $PSScriptRoot 'native\TrayModelBridge.c') -luser32 -lole32
        if ($LASTEXITCODE -ne 0) { throw '托盘事件桥编译失败' }
        & $tinycc -o $taskbarGuard (Join-Path $PSScriptRoot 'native\TaskbarGuard.c') -luser32 -lshell32
        if ($LASTEXITCODE -ne 0) { throw '任务栏恢复保护编译失败' }
        $buildFlags += ('/resource:' + $trayBridge + ',DeskBox.TrayBridge64')
        $buildFlags += ('/resource:' + $taskbarGuard + ',DeskBox.TaskbarGuard')
    }
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output @buildFlags (
        '/out:' + $OutputPath
    ) ('/win32manifest:' + (Join-Path $PSScriptRoot 'deskbox.manifest')) (
        '/win32icon:' + (Join-Path $PSScriptRoot 'deskbox.ico')
    ) @refs @sources
    if ($LASTEXITCODE -ne 0) { throw '编译失败' }
} finally {
    if ($nativeStage) {
        foreach ($name in @('TrayBridge64.dll', 'TrayBridge64.def', 'TaskbarGuard.exe')) {
            $stagedFile = Join-Path $nativeStage $name
            if (Test-Path -LiteralPath $stagedFile) { Remove-Item -LiteralPath $stagedFile }
        }
        [IO.Directory]::Delete($nativeStage, $false)
    }
}
