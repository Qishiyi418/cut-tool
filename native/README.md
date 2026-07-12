# CutTool Native 2.0

CutTool Native is the lightweight Windows implementation. It uses Win32 and
WinForms directly and does not start Electron, Chromium, Node.js, a GPU helper,
or a resident PowerShell process.

## Requirements

- Windows 10 or Windows 11
- .NET Framework 4.8 (included with current Windows releases)
- A Windows OCR language pack for OCR in that language

## Build

Run from the repository root:

```powershell
.\native\build.ps1 -Clean
```

The portable application is written to `native\dist\CutTool.exe`. The adjacent
`tools\windows-ocr.ps1` file must remain beside it in the packaged directory.

To create a portable release archive:

```powershell
.\native\package.ps1
```

## Test

```powershell
.\native\test.ps1
.\native\test.ps1 -IncludeNetwork
```

The default suite verifies hotkey parsing and performs OCR against a generated
image. The optional network suite also exercises the translation fallback chain.

## Runtime model

- Idle: one process, no browser or helper children.
- Capture: one desktop bitmap and one borderless native form, both disposed when
  the capture finishes.
- OCR: Windows OCR runs through a hidden on-demand process that exits after each
  recognition request.
- Translation: HTTPS clients exist only for the active request.

Settings remain compatible with the Electron version and are stored in
`%APPDATA%\cuttool\settings.json`.
