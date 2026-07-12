param(
  [switch]$Silent
)

$ErrorActionPreference = 'Stop'
$installDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
$programsRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
if (-not $installDirectory.StartsWith($programsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
  throw 'Refusing to uninstall from outside the current user Programs directory.'
}

Get-CimInstance Win32_Process -Filter "Name='CutTool.exe'" -ErrorAction SilentlyContinue |
  Where-Object {
    $_.ExecutablePath -and
    [IO.Path]::GetDirectoryName($_.ExecutablePath).Equals($installDirectory, [StringComparison]::OrdinalIgnoreCase)
  } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'CutTool.lnk'
if (Test-Path -LiteralPath $desktopShortcut) {
  $shell = New-Object -ComObject WScript.Shell
  $shortcut = $shell.CreateShortcut($desktopShortcut)
  if ($shortcut.TargetPath -and [IO.Path]::GetFullPath($shortcut.TargetPath).StartsWith($installDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    Remove-Item -LiteralPath $desktopShortcut -Force
  }
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValue = (Get-ItemProperty -LiteralPath $runKey -Name CutTool -ErrorAction SilentlyContinue).CutTool
if ($runValue -and $runValue.IndexOf($installDirectory, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
  Remove-ItemProperty -LiteralPath $runKey -Name CutTool -ErrorAction SilentlyContinue
}
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CutTool.Native' -Recurse -Force -ErrorAction SilentlyContinue

$escapedDirectory = $installDirectory.Replace("'", "''")
$cleanup = "Start-Sleep -Milliseconds 800; Remove-Item -LiteralPath '$escapedDirectory' -Recurse -Force"
Start-Process -FilePath 'powershell.exe' -ArgumentList @(
  '-NoProfile', '-NonInteractive', '-WindowStyle', 'Hidden', '-Command', $cleanup
) -WindowStyle Hidden

if (-not $Silent) {
  Add-Type -AssemblyName PresentationFramework
  [System.Windows.MessageBox]::Show('CutTool 已卸载。', 'CutTool') | Out-Null
}
