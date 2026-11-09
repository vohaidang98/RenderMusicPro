using System.Text;

namespace RenderMusicPro.Services;

internal static class WaveDurationReader
{
    // Read PCM/RF64 headers only; never decode or scan the audio payload.
    public static bool TryRead(string path, out double duration)
    {
        duration = 0;
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.ASCII);
            if (stream.Length < 12) return false;
            string kind = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (kind != "RIFF" && kind != "RF64") return false;
            reader.ReadUInt32();
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") return false;
            uint sampleRate = 0;
            ushort blockAlign = 0;
            ulong rf64DataSize = 0;
            while (stream.Position + 8 <= stream.Length)
            {
                string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
                uint size = reader.ReadUInt32();
                long start = stream.Position;
                if (chunk == "ds64" && size >= 28)
                {
                    reader.ReadUInt64();
                    rf64DataSize = reader.ReadUInt64();
                }
                else if (chunk == "fmt " && size >= 16)
                {
                    ushort format = reader.ReadUInt16();
                    if (format is not (1 or 3 or 0xFFFE)) return false;
                    reader.ReadUInt16();
                    sampleRate = reader.ReadUInt32();
                    reader.ReadUInt32();
                    blockAlign = reader.ReadUInt16();
                }
                else if (chunk == "data")
                {
                    ulong bytes = size == uint.MaxValue ? rf64DataSize : size;
                    if (sampleRate == 0 || blockAlign == 0 || bytes == 0 ||
                        bytes > (ulong)(stream.Length - start)) return false;
                    duration = bytes / ((double)sampleRate * blockAlign);
                    return double.IsFinite(duration) && duration > 0;
                }
                long next = start + size + (size & 1);
                if (next > stream.Length) return false;
                stream.Position = next;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }
}
