# CutTool Native 2.0.2

CutTool Native is the lightweight Windows implementation. It uses Win32 and
WinForms directly and does not start Electron, Chromium, Node.js, a GPU helper,
or a resident PowerShell process.

## Requirements

- Windows 10 or Windows 11
- .NET Framework 4.8 (included with current Windows releases)
- x64 Windows and the Visual C++ 2019/2022 x64 runtime for English OCR
- A Windows Chinese OCR language pack for Chinese OCR; the English model is bundled

## Build

Run from the repository root:

```powershell
.\native\build.ps1 -Clean
```

The portable application is written to `native\dist\CutTool.exe`. The adjacent
`tools` and `licenses` directories must remain beside it in the packaged directory.
The first build downloads pinned, SHA-256-verified OCR dependencies into the ignored
`dependencies` directory. Subsequent builds use that local cache.

To create a portable release archive:

```powershell
.\native\package.ps1
```

## Test

```powershell
.\native\test.ps1
.\native\test.ps1 -IncludeNetwork
```

The default suite verifies hotkeys, small English text on light/dark backgrounds,
and Chinese OCR routing. The optional network suite exercises translation fallback.
`test-clipboard.ps1` checks persistent native image formats and file drops across
process boundaries; it replaces the clipboard with a generated test image.

## Runtime model

- Idle: one process, no browser or helper children.
- Capture: one desktop bitmap and one borderless native form, both disposed when
  the capture finishes.
- OCR: hidden helpers exit after each request; no model is loaded into the tray
  process. English uses a 4 MB quantized LSTM model and image preprocessing capped
  around four megapixels. Requests are serialized and each helper has a 30s limit.
- Auto language selection uses a Chinese-character ratio heuristic. Explicit
  English/Chinese settings can resolve ambiguity in mixed or very short text.
- Translation: HTTPS clients exist only for the active request.

Settings remain compatible with the Electron version and are stored in
`%APPDATA%\cuttool\settings.json`.
