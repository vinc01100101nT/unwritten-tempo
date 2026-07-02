<#
  Batch-convert every .mp3 in a folder to .wav using ffmpeg.
  Output matches the game's Audacity export: mono, 44100 Hz, signed 16-bit PCM.

  Usage:
    ./mp3-to-wav.ps1                 # converts .mp3 files in the current folder
    ./mp3-to-wav.ps1 -Folder "C:\Downloads\sfx"
    ./mp3-to-wav.ps1 -Folder .\sfx -Recurse -Delete

  CMD template:
powershell -File mp3-to-wav.ps1 -Folder "D:\tempo\projects\roguelike-mmo\unity\Assets\Audio\Customs" -Delete

#>
param(
  [string]$Folder = ".",
  [switch]$Recurse,   # also descend into subfolders
  [switch]$Delete     # delete the source .mp3 after a successful convert
)

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
  Write-Error "ffmpeg not found on PATH. Install with:  winget install Gyan.FFmpeg"
  exit 1
}

$files = Get-ChildItem -Path $Folder -Filter *.mp3 -File -Recurse:$Recurse
if ($files.Count -eq 0) { Write-Host "No .mp3 files found in $Folder"; exit 0 }

foreach ($f in $files) {
  $wav = [IO.Path]::ChangeExtension($f.FullName, ".wav")
  Write-Host "-> $($f.Name)"
  & ffmpeg -y -loglevel error -i $f.FullName -ac 1 -ar 44100 -c:a pcm_s16le $wav
  if ($LASTEXITCODE -eq 0) {
    if ($Delete) { Remove-Item $f.FullName }
  } else {
    Write-Warning "Failed: $($f.Name)"
  }
}
Write-Host "Done ($($files.Count) file(s))."
