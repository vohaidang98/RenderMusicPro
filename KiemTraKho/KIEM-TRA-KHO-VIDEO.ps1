param([string]$Kho = '')
# Kiem tra kho video (CHI DOC, khong sua/xoa clip). Ghi bao cao KET-QUA-KIEM-TRA-KHO.txt canh file nay.
$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Kho) { $Kho = Read-Host 'Dan duong dan thu muc kho video roi Enter' }
$Kho = $Kho.Trim('"').Trim()
if (-not (Test-Path -LiteralPath $Kho)) { Write-Host ('Khong thay thu muc: ' + $Kho) -ForegroundColor Red; exit 1 }

$probe = $null
$cmd = Get-Command ffprobe.exe -ErrorAction SilentlyContinue
if ($cmd) { $probe = $cmd.Source }
if (-not $probe) {
  foreach ($root in @("$env:LOCALAPPDATA\Microsoft\WinGet\Packages", "$env:ProgramFiles\RenderMusicPro", 'C:\ffmpeg', 'C:\Program Files\ffmpeg')) {
    if (Test-Path -LiteralPath $root) {
      $f = Get-ChildItem -LiteralPath $root -Recurse -Filter ffprobe.exe -ErrorAction SilentlyContinue | Select-Object -First 1
      if ($f) { $probe = $f.FullName; break }
    }
  }
}
if (-not $probe) { Write-Host 'Khong tim thay ffprobe.exe (di kem ffmpeg).' -ForegroundColor Red; exit 1 }
Write-Host ('ffprobe: ' + $probe)

$files = @(Get-ChildItem -LiteralPath $Kho -Recurse -File -ErrorAction SilentlyContinue | Where-Object { @('.mp4','.mov','.mkv','.m4v','.avi','.webm') -contains $_.Extension.ToLower() })
Write-Host ('So clip: ' + $files.Count)
$rows = New-Object System.Collections.ArrayList
$i = 0
foreach ($f in $files) {
  $i++
  if ($i % 25 -eq 0) { Write-Host ('  da quet ' + $i + '/' + $files.Count) }
  $r = [ordered]@{ Path = $f.FullName; Err = ''; Codec = ''; W = 0; H = 0; Fps = ''; VDur = 0.0; FDur = 0.0; Frames = 0; HasAudio = $false }
  try {
    $json = & $probe -v error -show_entries 'stream=codec_type,codec_name,width,height,r_frame_rate,duration,nb_frames' -show_entries 'format=duration' -of json $f.FullName 2>&1
    $o = ($json -join "`n") | ConvertFrom-Json
    $v = @($o.streams | Where-Object { $_.codec_type -eq 'video' }) | Select-Object -First 1
    $a = @($o.streams | Where-Object { $_.codec_type -eq 'audio' })
    if (-not $v) { $r.Err = 'khong co luong hinh' }
    else {
      $r.Codec = [string]$v.codec_name; $r.W = [int]$v.width; $r.H = [int]$v.height; $r.Fps = [string]$v.r_frame_rate
      if ($v.duration) { $r.VDur = [double]::Parse([string]$v.duration, [Globalization.CultureInfo]::InvariantCulture) }
      if ($v.nb_frames) { $r.Frames = [int]$v.nb_frames }
    }
    if ($o.format.duration) { $r.FDur = [double]::Parse([string]$o.format.duration, [Globalization.CultureInfo]::InvariantCulture) }
    $r.HasAudio = ($a.Count -gt 0)
  } catch { $r.Err = 'file loi/khong doc duoc' }
  [void]$rows.Add([pscustomobject]$r)
}

function Mode($vals) { ($vals | Group-Object | Sort-Object Count -Descending | Select-Object -First 1).Name }
$ok = @($rows | Where-Object { -not $_.Err })
$mCodec = Mode ($ok | ForEach-Object { $_.Codec })
$mSize = Mode ($ok | ForEach-Object { '' + $_.W + 'x' + $_.H })
$mFps = Mode ($ok | ForEach-Object { $_.Fps })

$bad = New-Object System.Collections.ArrayList
foreach ($x in $rows) {
  $why = @()
  if ($x.Err) { $why += $x.Err }
  else {
    if ($x.Codec -ne $mCodec) { $why += ('codec ' + $x.Codec + ' (da so: ' + $mCodec + ')') }
    if (('' + $x.W + 'x' + $x.H) -ne $mSize) { $why += ('kich thuoc ' + $x.W + 'x' + $x.H + ' (da so: ' + $mSize + ')') }
    if ($x.Fps -ne $mFps) { $why += ('fps ' + $x.Fps + ' (da so: ' + $mFps + ')') }
    if ($x.VDur -gt 0 -and $x.FDur -gt 0 -and ($x.FDur - $x.VDur) -gt 0.15) { $why += ('hinh ngan hon tong file: hinh ' + [math]::Round($x.VDur,2) + 's / file ' + [math]::Round($x.FDur,2) + 's') }
    $d = $x.VDur; if ($d -le 0) { $d = $x.FDur }
    if ($d -lt 7.5 -or $d -gt 10.5) { $why += ('thoi luong la: ' + [math]::Round($d,2) + 's') }
    if ($x.Frames -gt 0 -and $d -gt 0 -and [math]::Abs($x.Frames / $d - 24) -gt 6 -and $mFps -eq '24/1') { $why += 'so khung hinh bat thuong' }
  }
  if ($why.Count -gt 0) { [void]$bad.Add(($x.Path + '  =>  ' + ($why -join '; '))) }
}

$out = Join-Path $here 'KET-QUA-KIEM-TRA-KHO.txt'
$lines = @()
$lines += ('Kho: ' + $Kho)
$lines += ('Ngay: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
$lines += ('Tong clip: ' + $files.Count + ' | Chuan da so: codec ' + $mCodec + ', ' + $mSize + ', fps ' + $mFps)
$lines += ('Clip lech chuan / loi: ' + $bad.Count)
$lines += ''
$lines += $bad
Set-Content -LiteralPath $out -Value $lines -Encoding UTF8
Write-Host ''
Write-Host ('Xong. Clip lech chuan / loi: ' + $bad.Count + ' / ' + $files.Count) -ForegroundColor Green
Write-Host ('Bao cao: ' + $out)
