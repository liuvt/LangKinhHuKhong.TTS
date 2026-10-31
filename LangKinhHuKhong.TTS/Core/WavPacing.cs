using System.Text;

namespace LangKinhHuKhong.TTS.Core;

/// <summary>
/// Ghép WAV PCM do Piper tạo, chèn khoảng lặng chính xác ở mức sample.
/// Không chỉnh giọng/âm sắc và không chèn dấu giả vào lời thoại.
/// </summary>
public static class WavPacing
{
    public sealed record Piece(string WavPath, int PauseAfterMs);
    public sealed record PcmInfo(long DataOffset, uint DataLength,
        ushort Channels, uint SampleRate, ushort BitsPerSample, ushort BlockAlign)
    {
        public long FrameCount => DataLength / BlockAlign;
    }

    public static async Task ComposeAsync(IReadOnlyList<Piece> pieces, string targetWav,
        int initialSilenceMs, CancellationToken ct)
    {
        if (pieces.Count == 0) throw new ArgumentException("Không có WAV để ghép.", nameof(pieces));
        if (initialSilenceMs < 0 || pieces.Any(p => p.PauseAfterMs < 0))
            throw new ArgumentOutOfRangeException(nameof(initialSilenceMs), "Khoảng nghỉ không được âm.");

        var sources = new List<PcmInfo>(pieces.Count);
        foreach (var piece in pieces)
        {
            ct.ThrowIfCancellationRequested();
            sources.Add(ReadPcmInfo(piece.WavPath));
        }
        var format = sources[0];
        foreach (var source in sources)
            if (source.Channels != format.Channels || source.SampleRate != format.SampleRate ||
                source.BitsPerSample != format.BitsPerSample || source.BlockAlign != format.BlockAlign)
                throw new InvalidDataException("Các WAV có định dạng khác nhau, không thể ghép PCM trực tiếp.");

        var totalBytes = SilenceBytes(format, initialSilenceMs);
        for (var i = 0; i < pieces.Count; i++)
            totalBytes = checked(totalBytes + sources[i].DataLength + SilenceBytes(format, pieces[i].PauseAfterMs));
        if (totalBytes > uint.MaxValue - 36L)
            throw new InvalidOperationException("WAV kết quả vượt giới hạn 4 GB của định dạng RIFF.");

        await using var output = new FileStream(targetWav, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous);
        using (var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(checked((uint)(36 + totalBytes)));
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16u);
            writer.Write((ushort)1); // signed PCM
            writer.Write(format.Channels);
            writer.Write(format.SampleRate);
            writer.Write(checked(format.SampleRate * format.BlockAlign));
            writer.Write(format.BlockAlign);
            writer.Write(format.BitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(checked((uint)totalBytes));
        }

        await WriteSilenceAsync(output, SilenceBytes(format, initialSilenceMs), ct);
        for (var i = 0; i < pieces.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await using var input = new FileStream(pieces[i].WavPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 128 * 1024, FileOptions.Asynchronous);
            input.Seek(sources[i].DataOffset, SeekOrigin.Begin);
            await CopyBytesAsync(input, output, sources[i].DataLength, ct);
            await WriteSilenceAsync(output, SilenceBytes(format, pieces[i].PauseAfterMs), ct);
        }
        await output.FlushAsync(ct);
    }

    // Streaming avoids a 4 GB intermediate RIFF WAV for very long stories.
    public static PcmInfo Inspect(string wav) => ReadPcmInfo(wav);

    public static async Task AppendPcmAsync(string wav, Stream target, PcmInfo expected, CancellationToken ct)
    {
        var found = ReadPcmInfo(wav);
        if (found.Channels != expected.Channels || found.SampleRate != expected.SampleRate ||
            found.BitsPerSample != expected.BitsPerSample || found.BlockAlign != expected.BlockAlign)
            throw new InvalidDataException("WAV không cùng cấu hình PCM; không thể nối trực tiếp.");
        await using var input = new FileStream(wav, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous);
        input.Seek(found.DataOffset, SeekOrigin.Begin);
        await CopyBytesAsync(input, target, found.DataLength, ct);
    }

    public static async Task AppendSilenceAsync(Stream target, PcmInfo format, int milliseconds,
        CancellationToken ct)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        await WriteSilenceAsync(target, SilenceBytes(format, milliseconds), ct);
    }

    public static long SilenceFrameCount(uint sampleRate, int milliseconds)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        return (long)Math.Round(sampleRate * (double)milliseconds / 1000,
            MidpointRounding.AwayFromZero);
    }

    public static TimeSpan GetDuration(string path)
    {
        var info = ReadPcmInfo(path);
        return TimeSpan.FromSeconds((info.DataLength / (double)info.BlockAlign) / info.SampleRate);
    }

    private static long SilenceBytes(PcmInfo info, int ms) =>
        (long)Math.Round(info.SampleRate * (double)ms / 1000, MidpointRounding.AwayFromZero) * info.BlockAlign;

    private static async Task WriteSilenceAsync(Stream output, long bytes, CancellationToken ct)
    {
        var silence = new byte[64 * 1024];
        // Piper phát signed PCM 16-bit; zero là mức im lặng.
        while (bytes > 0)
        {
            ct.ThrowIfCancellationRequested();
            var count = (int)Math.Min(bytes, silence.Length);
            await output.WriteAsync(silence.AsMemory(0, count), ct);
            bytes -= count;
        }
    }

    private static async Task CopyBytesAsync(Stream input, Stream output, long bytes, CancellationToken ct)
    {
        var buffer = new byte[128 * 1024];
        while (bytes > 0)
        {
            ct.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, bytes)), ct);
            if (read == 0) throw new EndOfStreamException("WAV bị thiếu dữ liệu PCM.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            bytes -= read;
        }
    }

    private static PcmInfo ReadPcmInfo(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        if (stream.Length < 44 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
            throw new InvalidDataException($"WAV không phải RIFF: {path}");
        _ = reader.ReadUInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
            throw new InvalidDataException($"WAV không hợp lệ: {path}");

        ushort channels = 0, bits = 0, align = 0;
        uint rate = 0, dataSize = 0;
        long dataOffset = -1;
        var hasFormat = false;
        while (stream.Position + 8 <= stream.Length)
        {
            var tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadUInt32();
            var start = stream.Position;
            if (start + size > stream.Length)
                throw new InvalidDataException("WAV bị lỗi chiều dài chunk.");
            if (tag == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("WAV fmt quá ngắn.");
                var codec = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                rate = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                align = reader.ReadUInt16();
                bits = reader.ReadUInt16();
                if (codec != 1 || bits != 16 || channels == 0 || rate == 0 ||
                    align != channels * (bits / 8))
                    throw new InvalidDataException("Piper cần xuất WAV PCM 16-bit để chèn nhịp.");
                hasFormat = true;
            }
            else if (tag == "data")
            {
                dataOffset = start;
                dataSize = size;
            }
            stream.Position = start + size + (size & 1);
        }
        if (!hasFormat || dataOffset < 0 || dataSize == 0 || dataSize % align != 0)
            throw new InvalidDataException("WAV thiếu fmt/data hoặc dữ liệu không nguyên khung PCM.");
        return new PcmInfo(dataOffset, dataSize, channels, rate, bits, align);
    }
}
