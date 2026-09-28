param(
  [Parameter(Mandatory = $true)]
  [string]$ImagePath,
  [Parameter(Mandatory = $false)]
  [string]$Language = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.Runtime.WindowsRuntime

$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Storage.FileAccessMode, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Globalization.Language, Windows.Foundation, ContentType = WindowsRuntime]

function Wait-WinRtOperation {
  param(
    [Parameter(Mandatory = $true)] $Operation,
    [Parameter(Mandatory = $true)] [Type] $ResultType
  )

  $method = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object {
      $_.Name -eq 'AsTask' -and
      $_.IsGenericMethodDefinition -and
      $_.GetParameters().Count -eq 1
    } |
    Select-Object -First 1
  if ($null -eq $method) { throw 'Windows Runtime AsTask adapter is unavailable.' }

  $task = $method.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
  $task.Wait()
  return $task.Result
}

$resolvedPath = [IO.Path]::GetFullPath($ImagePath)
$file = Wait-WinRtOperation (
  [Windows.Storage.StorageFile]::GetFileFromPathAsync($resolvedPath)
) ([Windows.Storage.StorageFile])
$stream = Wait-WinRtOperation (
  $file.OpenAsync([Windows.Storage.FileAccessMode]::Read)
) ([Windows.Storage.Streams.IRandomAccessStream])

try {
  $decoder = Wait-WinRtOperation (
    [Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)
  ) ([Windows.Graphics.Imaging.BitmapDecoder])
  $bitmap = Wait-WinRtOperation (
    $decoder.GetSoftwareBitmapAsync()
  ) ([Windows.Graphics.Imaging.SoftwareBitmap])

  try {
    $engine = $null
    if (-not [string]::IsNullOrWhiteSpace($Language)) {
      $requestedLanguage = [Windows.Globalization.Language]::new($Language)
      if ([Windows.Media.Ocr.OcrEngine]::IsLanguageSupported($requestedLanguage)) {
        $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($requestedLanguage)
      }
      else {
        throw "Windows OCR language is not installed: $Language"
      }
    }
    if ($null -eq $engine) {
      $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
    }
    if ($null -eq $engine) {
      throw 'Windows OCR is unavailable. Install an OCR language pack in Windows Settings.'
    }

    $result = Wait-WinRtOperation (
      $engine.RecognizeAsync($bitmap)
    ) ([Windows.Media.Ocr.OcrResult])
    [Console]::Out.Write((($result.Lines | ForEach-Object { $_.Text }) -join [Environment]::NewLine))
  }
  finally {
    if ($null -ne $bitmap) { $bitmap.Dispose() }
  }
}
finally {
  if ($null -ne $stream) { $stream.Dispose() }
}
