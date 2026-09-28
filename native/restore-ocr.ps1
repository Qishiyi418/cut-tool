$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$cache = Join-Path $root 'dependencies'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Get-VerifiedFile([string]$Uri, [string]$Path, [string]$Hash) {
  if (-not (Test-Path -LiteralPath $Path)) { Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $Path }
  if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Hash) {
    throw "OCR dependency checksum mismatch: $Path"
  }
}

$archive = Join-Path $cache 'tesseract.5.2.0.zip'
Get-VerifiedFile 'https://api.nuget.org/v3-flatcontainer/tesseract/5.2.0/tesseract.5.2.0.nupkg' $archive '202D82FC7C7D8384DF7DA57206D5E1F456CCDABD648C46E67CDFAA3A911D4795'
Get-VerifiedFile 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/87416418657359cb625c412a48b6e1d6d41c29bd/eng.traineddata' (Join-Path $cache 'eng.traineddata') '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2'
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $cache 'tesseract') -Force
