param(
  [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $projectRoot 'CutTool.Native'
$outputRoot = Join-Path $projectRoot 'dist'
$windowsRoot = if ($env:WINDIR) { $env:WINDIR } elseif ($env:SystemRoot) { $env:SystemRoot } else { 'C:\Windows' }
$frameworkRoot = Join-Path $windowsRoot 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
  throw 'The Windows .NET Framework compiler was not found.'
}

if ($Clean -and (Test-Path -LiteralPath $outputRoot)) {
  Remove-Item -LiteralPath $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$references = @(
  'System.dll',
  'System.Core.dll',
  'System.Drawing.dll',
  'System.Windows.Forms.dll',
  'System.Net.Http.dll',
  'System.Runtime.Serialization.dll',
  'System.Web.dll',
  'System.Web.Extensions.dll'
) | ForEach-Object { '/reference:' + (Join-Path $frameworkRoot $_) }

$sources = Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' |
  Sort-Object Name |
  ForEach-Object { $_.FullName }
$assetRoot = Join-Path $projectRoot 'assets'
New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null
$iconPath = Join-Path $assetRoot 'cuttool.ico'
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object Drawing.Bitmap 32,32
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.Color]::Transparent)
$accent = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(64,222,178))
$ink = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(18,35,31)),2.5
$graphics.FillEllipse($accent,1,1,30,30)
$graphics.DrawRectangle($ink,8,8,16,16)
$iconHandle = $bitmap.GetHicon()
$icon = [Drawing.Icon]::FromHandle($iconHandle)
$stream = [IO.File]::Create($iconPath)
$icon.Save($stream)
$stream.Dispose()
$icon.Dispose()
$ink.Dispose()
$accent.Dispose()
$graphics.Dispose()
$bitmap.Dispose()

$output = Join-Path $outputRoot 'CutTool.exe'
$arguments = @(
  '/nologo',
  '/target:winexe',
  '/platform:anycpu',
  '/optimize+',
  '/codepage:65001',
  '/warn:4',
  ('/win32icon:' + $iconPath),
  ('/out:' + $output)
) + $references + $sources

& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw "Native build failed with exit code $LASTEXITCODE." }

$toolsOutput = Join-Path $outputRoot 'tools'
New-Item -ItemType Directory -Path $toolsOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'tools\windows-ocr.ps1') -Destination $toolsOutput -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'tools\uninstall.ps1') -Destination $outputRoot -Force

$size = (Get-ChildItem -LiteralPath $outputRoot -File -Recurse | Measure-Object Length -Sum).Sum
[pscustomobject]@{
  Executable = $output
  Files = (Get-ChildItem -LiteralPath $outputRoot -File -Recurse).Count
  SizeKB = [math]::Round($size / 1KB, 1)
}
