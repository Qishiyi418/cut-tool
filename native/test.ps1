param(
  [switch]$IncludeNetwork
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$windowsRoot = if ($env:WINDIR) { $env:WINDIR } elseif ($env:SystemRoot) { $env:SystemRoot } else { 'C:\Windows' }
$framework = Join-Path $windowsRoot 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$output = Join-Path $root 'test-dist'
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $output 'tools') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'tools\windows-ocr.ps1') -Destination (Join-Path $output 'tools')
if (-not (Test-Path -LiteralPath (Join-Path $root 'dist\tools\ocr\CutTool.Ocr.exe'))) { throw 'Run build.ps1 before testing.' }
Copy-Item -LiteralPath (Join-Path $root 'dist\tools\ocr') -Destination (Join-Path $output 'tools') -Recurse

$references = @(
  'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
  'System.Net.Http.dll', 'System.Web.dll', 'System.Web.Extensions.dll'
) | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$sources = @(
  (Join-Path $root 'CutTool.Native\NativeMethods.cs'),
  (Join-Path $root 'CutTool.Native\HotKeyWindow.cs'),
  (Join-Path $root 'CutTool.Native\TextServices.cs'),
  (Join-Path $root 'tests\NativeSmokeTests.cs')
)
$testExecutable = Join-Path $output 'NativeSmokeTests.exe'
& $compiler @('/nologo','/target:exe','/platform:anycpu','/optimize+','/codepage:65001',('/out:' + $testExecutable)) $references $sources
if ($LASTEXITCODE -ne 0) { throw "Native test build failed with exit code $LASTEXITCODE." }

$arguments = if ($IncludeNetwork) { @('--network') } else { @() }
& $testExecutable $arguments
if ($LASTEXITCODE -ne 0) { throw "Native smoke tests failed with exit code $LASTEXITCODE." }
