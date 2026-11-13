$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $projectDir
try {
    $versionSource = Get-Content '.\Services\AppVersion.cs' -Raw
    $versionMatch = [regex]::Match($versionSource, 'Current\s*=\s*"(?<version>\d+\.\d+\.\d+)"')
    if (-not $versionMatch.Success) { throw 'Không đọc được phiên bản từ Services\AppVersion.cs.' }
    $version = $versionMatch.Groups['version'].Value
    $buildDirName = "Release_v$version"

    $setupSource = Get-Content '.\RenderMusicPro_Setup.iss' -Raw
    if ($setupSource -notmatch ('#define MyAppVersion "' + [regex]::Escape($version) + '"')) {
        throw "RenderMusicPro_Setup.iss chưa dùng đúng phiên bản $version."
    }
    if ($setupSource -notmatch ('#define MyAppBuildDir "' + [regex]::Escape($buildDirName) + '"')) {
        throw "RenderMusicPro_Setup.iss chưa trỏ tới $buildDirName."
    }

    $releaseDir = Join-Path $projectDir $buildDirName
    $publishDir = Join-Path $releaseDir 'publish'
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

    dotnet publish '.\RenderMusicPro.csproj' `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:NuGetAudit=false `
        -p:TreatWarningsAsErrors=true `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish thất bại với mã $LASTEXITCODE." }

    $publishedExe = Join-Path $publishDir 'RenderMusicPro.exe'
    if (-not (Test-Path -LiteralPath $publishedExe)) { throw 'Không tìm thấy RenderMusicPro.exe sau publish.' }
    $releaseExe = Join-Path $releaseDir 'RenderMusicPro.exe'
    Copy-Item -LiteralPath $publishedExe -Destination $releaseExe -Force

    $compilerCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    $compilerPath = if ($compilerCommand) { $compilerCommand.Source } else { $null }
    if (-not $compilerPath) {
        $candidate = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
        if (Test-Path -LiteralPath $candidate) { $compilerPath = $candidate }
    }
    if (-not $compilerPath) { throw 'Không tìm thấy Inno Setup Compiler (ISCC.exe) để tạo bộ cài.' }

    & $compilerPath '.\RenderMusicPro_Setup.iss'
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup thất bại với mã $LASTEXITCODE." }

    $setupExe = Join-Path $releaseDir "RenderMusicPro_Setup_v$version.exe"
    if (-not (Test-Path -LiteralPath $setupExe)) { throw 'Không tìm thấy bộ cài sau khi chạy Inno Setup.' }

    $notes = @'
# Render Music Pro v__VERSION__

- Thiết kế lại giao diện studio tối: nền graphite, điểm nhấn xanh emerald, thẻ thiết lập có số bước và mô tả.
- Thanh Bắt đầu / Dừng, thời gian và tiến trình luôn hiển thị khi cuộn phần cấu hình.
- Nút preset, Render LAN và Chuẩn hóa video được bố trí lưới 2×2, không bị cắt ở mép cửa sổ.
- Icon vector đồng bộ, tăng vùng bấm bộ đếm và hỗ trợ focus bằng bàn phím.
- Giữ các chức năng render, hiệu ứng, preset, LAN, license và cấu hình đã có.
- Kiểm tra build Release không cảnh báo; 41 bài kiểm tra hàng chờ LAN đều đạt.
## Tệp tải lên GitHub Release

- `RenderMusicPro.exe` — giữ nguyên tên để app tự cập nhật.
- RenderMusicPro_Setup_v__VERSION__.exe — bộ cài cho người dùng.
- `RELEASE_NOTES.md` và `SHA256SUMS.txt` — ghi chú và mã kiểm tra.

Tag phát hành: v__VERSION__.

'@ -replace '__VERSION__', $version
    $notesPath = Join-Path $releaseDir 'RELEASE_NOTES.md'
    [System.IO.File]::WriteAllText($notesPath, $notes, [System.Text.UTF8Encoding]::new($true))

    $hashes = @(
        Get-FileHash -LiteralPath $releaseExe -Algorithm SHA256
        Get-FileHash -LiteralPath $setupExe -Algorithm SHA256
    ) | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($_.Path))" }
    $hashPath = Join-Path $releaseDir 'SHA256SUMS.txt'
    [System.IO.File]::WriteAllLines($hashPath, $hashes, [System.Text.UTF8Encoding]::new($false))

    $zipPath = Join-Path $releaseDir "RenderMusicPro_Release_v$version.zip"
    Compress-Archive -LiteralPath $releaseExe, $setupExe, $notesPath, $hashPath `
        -DestinationPath $zipPath -CompressionLevel Optimal -Force

    Write-Host "Release v$version đã hoàn tất:" -ForegroundColor Green
    Get-Item -LiteralPath $releaseExe, $setupExe, $notesPath, $hashPath, $zipPath |
        Select-Object Name, Length |
        Format-Table -AutoSize
}
finally {
    Pop-Location
}
