$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'tools\tinycc'
$zip = Join-Path ([IO.Path]::GetTempPath()) ('rushbin-tcc-' + [guid]::NewGuid().ToString('N') + '.zip')
$url = 'https://download.savannah.gnu.org/releases/tinycc/tcc-0.9.27-win64-bin.zip'
$expected = '34A721949A2583FDFF725312DA092FA0F5F1F284B702E6F811C6954714FAABB2'
try {
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) { throw 'TinyCC download checksum mismatch.' }
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $dest -Force
    if (-not (Test-Path -LiteralPath (Join-Path $dest 'tcc\tcc.exe'))) { throw 'TinyCC directory structure is invalid.' }
    Write-Host 'TinyCC is ready.'
} finally {
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
}
