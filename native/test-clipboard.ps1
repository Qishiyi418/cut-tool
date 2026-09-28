param(
  [string]$ApplicationPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ApplicationPath) { $ApplicationPath = Join-Path $root 'dist\CutTool.exe' }
$ApplicationPath = (Resolve-Path -LiteralPath $ApplicationPath).Path
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$output = Join-Path $root 'clipboard-test-dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$testExecutable = Join-Path $output 'ClipboardTests.exe'
$references = @('System.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll') |
  ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& (Join-Path $framework 'csc.exe') @('/nologo', '/target:exe', '/codepage:65001', ('/out:' + $testExecutable)) $references (Join-Path $root 'tests\ClipboardTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Clipboard test build failed.' }
Write-Host 'Clipboard integration tests replace the current clipboard with a generated test image.'
& $testExecutable $ApplicationPath
if ($LASTEXITCODE -ne 0) { throw 'Clipboard integration tests failed.' }
