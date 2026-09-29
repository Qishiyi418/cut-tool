param(
  [string]$Version = '2.0.3'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root 'build.ps1') -Clean | Out-Host

$release = Join-Path $root 'release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
$archive = Join-Path $release ("CutTool-$Version-win-x64.zip")
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $root 'dist\*') -DestinationPath $archive -CompressionLevel Optimal

$file = Get-Item -LiteralPath $archive
[pscustomobject]@{
  Package = $file.FullName
  SizeKB = [math]::Round($file.Length / 1KB, 1)
  SHA256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
}
