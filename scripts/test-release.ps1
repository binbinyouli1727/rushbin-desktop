param(
    [Parameter(Mandatory=$true)][string]$ExecutablePath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$ExecutablePath=(Resolve-Path -LiteralPath $ExecutablePath).Path
if (@(Get-Process -Name DeskBox -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Exit other DeskBox instances before testing: F5 and desktop anchoring are process-global resources.'
}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new output directory to avoid reading stale results.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$candidateDirectory=Join-Path $OutputDirectory 'candidate'
New-Item -ItemType Directory -Path $candidateDirectory | Out-Null
$candidate=Join-Path $candidateDirectory 'DeskBox.exe'
Copy-Item -LiteralPath $ExecutablePath -Destination $candidate
function Run-Checked([string]$file,[string[]]$arguments,[int]$timeout=180000) {
    $process=Start-Process -FilePath $file -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit($timeout)) { throw "Test timed out (PID $($process.Id)); inspect this test process before retrying." }
    if ($process.ExitCode -ne 0) { throw "Test exited with $($process.ExitCode): $file. Inspect $OutputDirectory." }
}
$data=Join-Path $OutputDirectory 'regression-data'
Run-Checked $candidate @('--test','--data-dir',('"'+$data+'"'))
$regression=Get-Content -LiteralPath (Join-Path $candidateDirectory '验证\测试结果.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $regression.passed) { throw 'Regression results include a failure.' }
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework=Split-Path $compiler
$references=@('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll') | ForEach-Object {'/r:'+(Join-Path $framework $_)}
$references+=@('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll') | ForEach-Object {'/r:'+(Join-Path $framework ('WPF\'+$_))}
$references+=('/r:'+$candidate)
foreach ($name in @('NativeScreenshotProbe','PortablePathsProbe')) {
    $probe=Join-Path $candidateDirectory ($name+'.exe')
    & $compiler /nologo /target:exe /platform:anycpu /optimize+ /utf8output ('/out:'+$probe) @references (Join-Path $PSScriptRoot ('tests\'+$name+'.cs'))
    if ($LASTEXITCODE -ne 0) { throw "Probe compilation failed: $name" }
    if ($name -eq 'NativeScreenshotProbe') {
        Run-Checked $probe @(('"'+(Join-Path $OutputDirectory 'native-screenshots')+'"'))
    } else {
        Run-Checked $probe @()
    }
}
$screenshots=Get-Content -LiteralPath (Join-Path $OutputDirectory 'native-screenshots\results.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $screenshots.passed) { throw 'Native screenshot checks include a failure.' }
$result=[pscustomobject]@{
    passed=$true
    executableVersion=[Reflection.AssemblyName]::GetAssemblyName($candidate).Version.ToString()
    executableSHA256=(Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
    regressionChecks=@($regression.results).Count
    nativeScreenshotChecks=@($screenshots.results).Count
    portablePathProbePassed=$true
    scope='Local interactive Windows session; isolated fixtures, WPF and Windows API calls. Does not verify all hardware controls, physical Photos navigation, multi-machine/DPI or Windows login cold startup.'
}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'summary.json') -Encoding UTF8
$result | ConvertTo-Json
