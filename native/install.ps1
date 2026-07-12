param(
  [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root 'build.ps1') -Clean | Out-Host

$source = [IO.Path]::GetFullPath((Join-Path $root 'dist'))
$target = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\CutTool'))
$programsRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
if (-not $target.StartsWith($programsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
  throw 'The install directory is outside the current user Programs directory.'
}

$settingsPath = Join-Path $env:APPDATA 'cuttool\settings.json'
$settingsBackup = Join-Path $env:TEMP ('cuttool-settings-' + [guid]::NewGuid().ToString('N') + '.json')
if (Test-Path -LiteralPath $settingsPath) { Copy-Item -LiteralPath $settingsPath -Destination $settingsBackup -Force }

$oldApp = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
  Where-Object { $_.DisplayName -like 'CutTool 1.*' } |
  Select-Object -First 1
if ($oldApp) {
  Get-CimInstance Win32_Process -Filter "Name='CutTool.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -and $_.ExecutablePath.IndexOf('\Programs\cuttool\', [StringComparison]::OrdinalIgnoreCase) -ge 0 } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

  $quiet = $oldApp.QuietUninstallString
  if (-not $quiet) { $quiet = $oldApp.UninstallString + ' /S' }
  $match = [regex]::Match($quiet, '^\s*"([^"]+)"\s*(.*)$')
  if ($match.Success -and (Test-Path -LiteralPath $match.Groups[1].Value)) {
    $uninstaller = Start-Process -FilePath $match.Groups[1].Value -ArgumentList $match.Groups[2].Value -WindowStyle Hidden -PassThru -Wait
    if ($uninstaller.ExitCode -ne 0) { throw "Old CutTool uninstaller exited with code $($uninstaller.ExitCode)." }
    for ($attempt = 0; $attempt -lt 50 -and (Test-Path -LiteralPath $target); $attempt++) {
      Start-Sleep -Milliseconds 200
    }
  }
}

if (Test-Path -LiteralPath $target) {
  $resolvedTarget = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $target).Path)
  if (-not $resolvedTarget.Equals($target, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The existing install path resolves outside the intended directory.'
  }
  Remove-Item -LiteralPath $target -Recurse -Force
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force

if ((Test-Path -LiteralPath $settingsBackup) -and -not (Test-Path -LiteralPath $settingsPath)) {
  New-Item -ItemType Directory -Path (Split-Path -Parent $settingsPath) -Force | Out-Null
  Copy-Item -LiteralPath $settingsBackup -Destination $settingsPath -Force
}
Remove-Item -LiteralPath $settingsBackup -Force -ErrorAction SilentlyContinue

$executable = Join-Path $target 'CutTool.exe'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'CutTool.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($desktopShortcut)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = $executable + ',0'
$shortcut.Description = 'CutTool 轻量截图工具'
$shortcut.Save()

$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CutTool.Native'
New-Item -Path $uninstallKey -Force | Out-Null
$uninstallScript = Join-Path $target 'uninstall.ps1'
$uninstallCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + $uninstallScript + '"'
New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'CutTool 2.0.0' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value '2.0.0' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name Publisher -Value 'CutTool' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $target -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value ($executable + ',0') -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name UninstallString -Value $uninstallCommand -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name QuietUninstallString -Value ($uninstallCommand + ' -Silent') -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
$estimatedSize = [int][math]::Ceiling((Get-ChildItem -LiteralPath $target -File -Recurse | Measure-Object Length -Sum).Sum / 1KB)
New-ItemProperty -Path $uninstallKey -Name EstimatedSize -Value $estimatedSize -PropertyType DWord -Force | Out-Null

if (-not $NoLaunch) { Start-Process -FilePath $executable -WorkingDirectory $target }

[pscustomobject]@{
  InstalledVersion = '2.0.0'
  Executable = $executable
  DesktopShortcut = $desktopShortcut
  SettingsPreserved = Test-Path -LiteralPath $settingsPath
}
