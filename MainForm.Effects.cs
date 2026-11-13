using RenderMusicPro.Services;

namespace RandomVideoDangVH;

public sealed partial class MainForm
{
    private static readonly HashSet<string> EffectImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff" };
    private bool? _effectCudaAvailable;

    private void BrowseEffectSource()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Chọn thư mục hiệu ứng ảnh / video nền xanh hoặc đen (để trống nếu không dùng)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (Directory.Exists(txtEffectSource.Text.Trim())) dialog.SelectedPath = txtEffectSource.Text.Trim();
        if (dialog.ShowDialog(this) == DialogResult.OK) txtEffectSource.Text = dialog.SelectedPath;
    }

    private MusicPoolDeck? CreateEffectDeck()
    {
        // Thư mục riêng (như cũ) hoặc thư mục của bộ có sẵn đang chọn; "" khi tắt hiệu ứng.
        string folder = EffectSourceFolder();
        if (folder.Length == 0) return null;
        if (!Directory.Exists(folder))
            throw new InvalidOperationException("Thư mục hiệu ứng không tồn tại. Xóa đường dẫn nếu không sử dụng hiệu ứng.");
        var files = SafeEnumerateFiles(folder).Where(path => EffectImageExtensions.Contains(Path.GetExtension(path)) ||
            VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).ToList();
        if (files.Count == 0)
            throw new InvalidOperationException("Thư mục hiệu ứng không có ảnh hoặc video được hỗ trợ. Để trống đường dẫn để bỏ qua.");
        return new MusicPoolDeck(files);
    }

    private async Task RenderEffectVideoAsync(string ffmpeg, string list, string music, bool audioEncoded, string effect,
        string output, string duration, bool greenScreen, CancellationToken token)
    {
        bool image = EffectImageExtensions.Contains(Path.GetExtension(effect));
        if (greenScreen)
        {
            effect = (await GetPreparedEffectAsync(ffmpeg, effect, token)).Path;
            AddLog("  ✓ Dùng hiệu ứng đã chuẩn bị; không tách nền lại khi lặp và ghép.", Cyan);
        }
        // Overlay holds a still image's decoded frame in RAM (repeatlast=1).
        // Do not keep reading/decoding the PNG for every output frame.
        string loop = image ? "-framerate 30" : "-stream_loop -1";
        string end = $" -map \"[outv]\" -map 2:a:0 -t {duration} {AudioCodecArgs(audioEncoded)}" +
            $"{FastStartArgs}{Q(output)}";
        string nvenc = NvencEncodeArgs;
        string effectGraph = "[1:v:0]setpts=PTS-STARTPTS,format=rgba[fx];";
        string cpuGraph = "[0:v:0]setpts=PTS-STARTPTS[bg];" +
            effectGraph +
            "[bg][fx]overlay=x=(main_w-overlay_w)/2:y=(main_h-overlay_h)/2:shortest=0:eof_action=repeat:repeatlast=1,format=yuv420p[outv]";
        // Khung camera (nếu bật): input 3 sau nhạc, phủ trên cùng theo kích thước video nền.
        string frameArgs = CameraFrameInputArgs();
        if (frameArgs.Length > 0)
            cpuGraph = ApplyCameraFrame(cpuGraph, 3, _effectCanvasSize ?? await GetEffectVideoSizeAsync(ffmpeg, list, true, token));
        string common = $"-hide_banner -y -f concat -safe 0 -i {Q(list)} {loop} -i {Q(effect)} -i {Q(music)}{frameArgs} " +
            $"-filter_complex {Q(cpuGraph)} ";
        if (_cudaPipelineAvailable != false && _nvencAvailable != false)
        {
            try
            {
                var canvas = _effectCanvasSize ?? await GetEffectVideoSizeAsync(ffmpeg, list, true, token);
                string graph = cpuGraph.Replace("[0:v:0]setpts=PTS-STARTPTS[bg];",
                    $"[0:v:0]hwdownload,format=nv12,crop={canvas.Width}:{canvas.Height}:0:0,setpts=PTS-STARTPTS[bg];",
                    StringComparison.Ordinal);
                string accelerated = $"-hide_banner -y -hwaccel cuda -hwaccel_output_format cuda -f concat -safe 0 -i {Q(list)} {loop} -i {Q(effect)} -i {Q(music)}{frameArgs} " +
                    $"-filter_complex {Q(graph)} ";
                await RunFfmpegAsync(ffmpeg, accelerated + nvenc + end, token, "Giải mã nền CUDA, ghép cache và mã hóa NVENC");
                _cudaPipelineAvailable = true;
                _nvencAvailable = true;
                AddLog("  ✓ GPU: giải mã nền CUDA + mã hóa NVENC; dùng hiệu ứng trong suốt đã chuẩn bị.", Green);
                return;
            }
            catch (InvalidOperationException)
            {
                token.ThrowIfCancellationRequested();
                _cudaPipelineAvailable = false;
                TryDelete(output);
                AddLog("  ⚠ Giải mã nền CUDA không phù hợp; vẫn ưu tiên mã hóa NVENC.", Orange);
            }
        }
        await EncodeOverlayWithFallbackAsync(ffmpeg, common, end, output, token,
            "Ghép hiệu ứng và mã hóa NVENC",
            "  ✓ Đã ghép hiệu ứng · mã hóa NVIDIA NVENC · giữ độ phân giải video nền.",
            "  ⚠ NVENC không sử dụng được cho hiệu ứng này; chuyển sang CPU.",
            "Ghép hiệu ứng dự phòng CPU",
            "  ✓ Đã ghép hiệu ứng bằng CPU · giữ độ phân giải video nền.");
    }

    // Hai tầng cuối dùng chung cho nền video và nền ảnh: NVENC trước, lỗi thì xóa file dở và chạy CPU libx264.
    // `common` = đầu vào + filter_complex, `end` = map/thời lượng/audio/đầu ra.
    private async Task EncodeOverlayWithFallbackAsync(string ffmpeg, string common, string end, string output,
        CancellationToken token, string nvencStep, string nvencDone, string nvencFailed, string cpuStep, string cpuDone)
    {
        if (_nvencAvailable != false)
        {
            try
            {
                await RunFfmpegAsync(ffmpeg, common + NvencEncodeArgs + end, token, nvencStep);
                _nvencAvailable = true;
                AddLog(nvencDone, Green);
                return;
            }
            catch (InvalidOperationException)
            {
                token.ThrowIfCancellationRequested();
                _nvencAvailable = false;
                TryDelete(output);
                AddLog(nvencFailed, Orange);
            }
        }
        await RunFfmpegAsync(ffmpeg, common + $"-c:v libx264 -preset veryfast -crf 20 -profile:v high {BitrateCapArgs}" + end,
            token, cpuStep);
        AddLog(cpuDone, Green);
    }

    private async Task<(int Width, int Height)> GetEffectVideoSizeAsync(
        string ffmpeg, string input, bool concat, CancellationToken token)
    {
        if (!concat)
        {
            var info = new FileInfo(input);
            lock (_videoDurationCacheLock)
            {
                if (info.Exists && _videoDurationCache.TryGetValue(info.FullName, out var cached) &&
                    cached.FileLength == info.Length && cached.LastWriteUtc == info.LastWriteTimeUtc &&
                    cached.Width > 0 && cached.Height > 0) return (cached.Width, cached.Height);
            }
        }
        string prefix = concat ? "-f concat -safe 0 " : "";
        string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        if (File.Exists(ffprobe))
        {
            string result = await RunProcessCaptureAsync(ffprobe,
                $"-v error {prefix}-i {Q(input)} -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0", token);
            string[] dimensions = result.Trim().Split('\n')[0].Trim().Split('x');
            if (dimensions.Length == 2 && int.TryParse(dimensions[0], out int width) &&
                int.TryParse(dimensions[1], out int height) && width > 0 && height > 0)
            {
                if (!concat) RememberVideoSize(input, width, height);
                return (width, height);
            }
        }
        string header = await RunProcessCaptureAsync(ffmpeg,
            $"-hide_banner {prefix}-i {Q(input)}", token, captureError: true);
        var match = System.Text.RegularExpressions.Regex.Match(header,
            @"Video:[^\r\n]*?\b(\d{2,5})x(\d{2,5})(?:[\s,\[])");
        if (match.Success)
        {
            int width = int.Parse(match.Groups[1].Value), height = int.Parse(match.Groups[2].Value);
            if (!concat) RememberVideoSize(input, width, height);
            return (width, height);
        }
        throw new InvalidOperationException("Không đọc được kích thước video / hiệu ứng để giữ nguyên độ phân giải.");
    }

    private async Task<PreparedEffect> GetPreparedEffectAsync(string ffmpeg, string source, CancellationToken token)
    {
        bool image = EffectImageExtensions.Contains(Path.GetExtension(source));
        // Resolved once at render start; re-resolving per playlist could open a folder dialog mid-run.
        string root = string.IsNullOrWhiteSpace(_effectCacheDirectory)
            ? ResolveCacheDirectory() : _effectCacheDirectory;
        LoadPersistentMetadataCache(root);
        var service = new EffectCacheService(Path.Combine(root, "PreparedEffects"));
        EffectBackground background = _runEffectBackgroundMode switch
        {
            1 => EffectBackground.Green,
            2 => EffectBackground.Black,
            _ => await EffectBackgroundDetector.DetectAsync(ffmpeg, source, image, root,
                () => GetDurationAsync(ffmpeg, source, token), token)
        };
        if (background == EffectBackground.Uncertain)
        {
            AddLog("  ℹ Nền không rõ hoặc đã có alpha: giữ nguyên. Có thể chọn tay Nền xanh / Nền đen nếu cần.", Cyan);
            return new PreparedEffect(source, image, true);
        }
        _runEffectBlack = background == EffectBackground.Black;
        string recipe = _runEffectBlack
            ? "black-light-v1:max-rgb:unpremultiply:original-alpha:png-rgba-ffv1-bgra"
            : "greenscreen-v1:00ff00:0.18:0.08:original-alpha:png-rgba-ffv1-bgra";
        AddLog("  ✓ Nền hiệu ứng: " + (_runEffectBlack ? "đen" : "xanh") +
            (_runEffectBackgroundMode == 0 ? " (tự nhận diện)." : " (chọn tay)."), Cyan);
        var result = await service.GetOrCreateAsync(source, image, recipe,
            async (output, ct) =>
            {
                lblCurrent.Text = "Đang tách hiệu ứng lần đầu và lưu cache...";
                AddLog("  ✦ Tách nền một lần: " + Path.GetFileName(source), Cyan);
                await PrepareEffectCacheFileAsync(ffmpeg, source, output, image, ct);
            },
            (output, ct) => ValidateEffectCacheFileAsync(ffmpeg, output, image, ct), token);
        AddLog(result.Reused
            ? "  ✓ Dùng lại hiệu ứng đã tách từ cache trên ổ đĩa."
            : "  ✓ Đã lưu hiệu ứng trong suốt để lần sau dùng tiếp.", Green);
        lblCurrent.Text = "Đang lặp và ghép hiệu ứng đã tách (ưu tiên GPU)...";
        return result;
    }

    private async Task PrepareEffectCacheFileAsync(string ffmpeg, string source, string output,
        bool image, CancellationToken token)
    {
        var size = image ? (Width: 0, Height: 0) : await GetEffectVideoSizeAsync(ffmpeg, source, false, token);
        string encode = image ? "-frames:v 1 -c:v png -pix_fmt rgba" :
            "-c:v ffv1 -level 3 -coder 1 -context 1 -g 1 -pix_fmt bgra -slicecrc 1 -fps_mode passthrough";
        if (!image && !_runEffectBlack && _effectCudaAvailable != false)
        {
            try
            {
                // despill sau cùng (trên bgra, đã về CPU): colorkey/chromakey chỉ xoá nền xanh gần như
                // thuần, viền mép bán trong suốt (tóc, mờ nét) vẫn còn ám xanh -> "tách nền không sạch".
                string filter = $"chromakey_cuda=0x00FF00:0.18:0.08,hwdownload,format=yuva420p,crop={size.Width}:{size.Height}:0:0,setpts=PTS-STARTPTS,format=bgra,despill=type=green:mix=0.5:expand=0";
                await RunFfmpegAsync(ffmpeg,
                    $"-hide_banner -y -hwaccel cuda -hwaccel_output_format cuda -i {Q(source)} -map 0:v:0 -an -sn -dn -vf {Q(filter)} {encode} {Q(output)}",
                    token, "Tách xanh CUDA và lưu cache lossless có alpha");
                _effectCudaAvailable = true;
                AddLog("  ✓ Đã tách hiệu ứng gốc bằng CUDA, giữ alpha trong cache lossless.", Green);
                return;
            }
            catch (InvalidOperationException)
            {
                token.ThrowIfCancellationRequested();
                _effectCudaAvailable = false;
                TryDelete(output);
                AddLog("  ⚠ CUDA không phù hợp; tách xanh một lần bằng CPU.", Orange);
            }
        }
        string graph = "[0:v:0]setpts=PTS-STARTPTS,format=rgba,split[keySource][alphaSource];" +
            "[alphaSource]alphaextract[originalAlpha];" +
            "[keySource]colorkey=0x00FF00:0.18:0.08,despill=type=green:mix=0.5:expand=0,split[keyedColor][keyAlphaSource];" +
            "[keyAlphaSource]alphaextract[greenAlpha];" +
            "[originalAlpha][greenAlpha]blend=all_mode=multiply[combinedAlpha];" +
            "[keyedColor][combinedAlpha]alphamerge[out]";
        if (_runEffectBlack)
        {
            // Brightness becomes alpha; unpremultiply keeps soft colored light at its
            // original intensity instead of darkening it a second time during overlay.
            graph = "[0:v:0]setpts=PTS-STARTPTS,format=rgba,split=3[rgb][mask][original];" +
                "[original]alphaextract[originalAlpha];" +
                "[mask]format=gbrp,extractplanes=r+g+b[red][green][blue];" +
                "[red][green]blend=all_mode=lighten[maxRG];[maxRG][blue]blend=all_mode=lighten,split[light][lightCopy];" +
                "[originalAlpha][lightCopy]blend=all_mode=multiply[combinedAlpha];" +
                "[rgb][light]alphamerge,format=gbrap,setparams=alpha_mode=premultiplied,unpremultiply=inplace=1:planes=7,format=rgba[straight];" +
                "[straight][combinedAlpha]alphamerge,format=rgba[out]";
        }
        try
        {
            await RunFfmpegAsync(ffmpeg,
                $"-hide_banner -y -i {Q(source)} -filter_complex {Q(graph)} -map \"[out]\" -an -sn -dn {encode} {Q(output)}",
                token, "Tách nền và lưu cache lossless có alpha");
        }
        catch (InvalidOperationException ex) when (_runEffectBlack &&
            ex.Message.Contains("alpha_mode", StringComparison.OrdinalIgnoreCase) &&
            (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("Error applying option", StringComparison.OrdinalIgnoreCase)))
        {
            // Older FFmpeg versions have no automatic alpha-mode conversion and do
            // not expose this metadata option. Their unpremultiply works directly.
            token.ThrowIfCancellationRequested();
            TryDelete(output);
            graph = graph.Replace("setparams=alpha_mode=premultiplied,", "");
            await RunFfmpegAsync(ffmpeg,
                $"-hide_banner -y -i {Q(source)} -filter_complex {Q(graph)} -map \"[out]\" -an -sn -dn {encode} {Q(output)}",
                token, "Tách nền đen với FFmpeg phiên bản cũ");
        }
    }

    private static async Task ValidateEffectCacheFileAsync(string ffmpeg, string file, bool image, CancellationToken token)
    {
        string probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        if (File.Exists(probe))
        {
            string result = await RunProcessCaptureAsync(probe,
                $"-v error -select_streams v:0 -show_entries stream=codec_name,pix_fmt,width,height -of json {Q(file)}", token);
            using var doc = System.Text.Json.JsonDocument.Parse(result);
            var streams = doc.RootElement.GetProperty("streams");
            if (streams.GetArrayLength() == 0) throw new InvalidDataException("Cache không có hình ảnh.");
            var stream = streams[0];
            string? codec = stream.GetProperty("codec_name").GetString();
            string? format = stream.GetProperty("pix_fmt").GetString();
            if (codec != (image ? "png" : "ffv1") || format != (image ? "rgba" : "bgra") ||
                stream.GetProperty("width").GetInt32() <= 0 || stream.GetProperty("height").GetInt32() <= 0)
                throw new InvalidDataException("Cache hiệu ứng chưa giữ được alpha hoặc định dạng lossless.");
        }
        else
        {
            string header = await RunProcessCaptureAsync(ffmpeg, $"-hide_banner -i {Q(file)}", token, true);
            if (!header.Contains(image ? "rgba" : "bgra", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Không xác minh được alpha của cache hiệu ứng.");
        }
    }
}
