using System.Buffers.Binary;
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

    /// <summary>
    /// Slices off the leading carrier warmup syllable ("Út: " / "Ất: ") by locating the target word's
    /// first major syllable peak and walking backward across both voiced energy and high-ZCR fricative onsets
    /// (e.g. 'gi-' /z/, 's-', 'kh-') into the true inter-word silence valley, eliminating any '-t' release burst
    /// from the carrier with a smooth 8ms half-cosine fade-in.
    /// </summary>
    public static void TrimLeadingCarrierSyllableInPlace(string wavPath)
    {
        var info = ReadPcmInfo(wavPath);
        int totalFrames = (int)info.FrameCount;
        if (totalFrames < info.SampleRate / 5) return; // < 200ms: too short to contain carrier + target

        byte[] rawBytes;
        using (var fs = new FileStream(wavPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            fs.Seek(info.DataOffset, SeekOrigin.Begin);
            rawBytes = new byte[info.DataLength];
            int offset = 0;
            while (offset < rawBytes.Length)
            {
                int r = fs.Read(rawBytes, offset, rawBytes.Length - offset);
                if (r == 0) break;
                offset += r;
            }
        }

        int channels = info.Channels;
        int winFrames = Math.Max(1, (int)(info.SampleRate * 5 / 1000)); // 5ms analysis window
        int nWin = totalFrames / winFrames;
        if (nWin < 24) return;

        var rms = new double[nWin];
        var zcr = new int[nWin];
        double peak = 0;
        ReadOnlySpan<byte> byteSpan = rawBytes;
        for (int w = 0; w < nWin; w++)
        {
            double sumSq = 0;
            int crossings = 0;
            int baseFrame = w * winFrames;
            short prevSample = BinaryPrimitives.ReadInt16LittleEndian(byteSpan.Slice(baseFrame * info.BlockAlign, 2));
            for (int f = 0; f < winFrames; f++)
            {
                int byteIdx = (baseFrame + f) * info.BlockAlign;
                short s = BinaryPrimitives.ReadInt16LittleEndian(byteSpan.Slice(byteIdx, 2));
                sumSq += (double)s * s;
                if (f > 0 && ((s >= 0) != (prevSample >= 0)))
                    crossings++;
                prevSample = s;
            }
            double val = Math.Sqrt(sumSq / winFrames);
            rms[w] = val;
            zcr[w] = crossings;
            if (val > peak) peak = val;
        }
        if (peak < 80.0) return;

        int firstVoice = 0;
        for (int k = 0; k < nWin; k++)
        {
            if (rms[k] > peak * 0.06) { firstVoice = k; break; }
        }

        // 1. Find the energy peak of the FIRST syllable (the carrier 'Út'/'Ất'/'At').
        int carrierPeakEnd = Math.Min(nWin - 6, firstVoice + (int)(0.165 / 0.005));
        int carrierPeakIdx = firstVoice;
        for (int k = firstVoice + 1; k <= carrierPeakEnd; k++)
        {
            if (rms[k] > rms[carrierPeakIdx]) carrierPeakIdx = k;
        }
        double carrierPeak = rms[carrierPeakIdx];
        if (carrierPeak < 80.0) return;

        // 2. Step past the carrier vowel slope until energy drops below 22% of carrierPeak.
        int searchEnd = Math.Min(nWin - 2, firstVoice + (int)(0.550 / 0.005));
        int dropIdx = carrierPeakIdx + 1;
        while (dropIdx < searchEnd && rms[dropIdx] > carrierPeak * 0.22)
            dropIdx++;
        if (dropIdx >= nWin - 4) return;

        // 3. Locate the target word's FIRST voiced syllable peak (>= 14% of overall peak) after carrier drop.
        int targetPeakIdx = -1;
        for (int k = dropIdx + 1; k < nWin - 1; k++)
        {
            if (rms[k] >= peak * 0.14 && rms[k] >= rms[k - 1] && rms[k] >= rms[k + 1])
            {
                targetPeakIdx = k;
                break;
            }
        }
        if (targetPeakIdx < 0)
        {
            targetPeakIdx = dropIdx + 1;
            for (int k = dropIdx + 2; k < nWin; k++)
            {
                if (rms[k] > rms[targetPeakIdx]) targetPeakIdx = k;
            }
            if (rms[targetPeakIdx] < peak * 0.10) return;
        }

        // 4. Find the deepest silence floor inside [dropIdx .. targetPeakIdx], then walk BACKWARD from targetPeakIdx
        //    strictly down to the gap noise floor so soft breathy onsets (like 'h-', 'ph-', 'th-', 'gi-') are 100% preserved.
        int gapMinIdx = dropIdx;
        for (int k = dropIdx + 1; k < targetPeakIdx; k++)
        {
            if (rms[k] < rms[gapMinIdx]) gapMinIdx = k;
        }
        double gapMin = rms[gapMinIdx];
        if (gapMin > carrierPeak * 0.25) return;

        double silFloor = Math.Max(35.0, gapMin * 2.15);
        double hissFloor = Math.Max(16.0, gapMin * 1.30);
        int maxFricativeZcr = Math.Max(14, (int)Math.Round(18.0 * info.SampleRate / 22050.0));

        int onset = targetPeakIdx;
        while (onset > gapMinIdx)
        {
            int prev = onset - 1;
            bool hasVoice = rms[prev] > silFloor;
            bool hasHiss = rms[prev] > hissFloor && zcr[prev] >= maxFricativeZcr;
            if (hasVoice || hasHiss)
                onset = prev;
            else
                break;
        }

        int cutWin = Math.Max(gapMinIdx, onset - 2);
        int cutFrame = Math.Clamp(cutWin * winFrames, 0, totalFrames - (int)(info.SampleRate / 20));
        if (cutFrame <= 0) return;

        int keptFrames = totalFrames - cutFrame;
        int keptBytesLen = keptFrames * info.BlockAlign;
        byte[] slicedBytes = new byte[keptBytesLen];
        Buffer.BlockCopy(rawBytes, cutFrame * info.BlockAlign, slicedBytes, 0, keptBytesLen);

        // Smooth 8ms half-cosine (Hann) fade-in at slice boundary to prevent any zero-crossing or murmur click
        int fadeFrames = Math.Min(keptFrames, Math.Max(1, (int)(info.SampleRate * 8 / 1000)));
        Span<byte> outSpan = slicedBytes;
        for (int f = 0; f < fadeFrames; f++)
        {
            double gain = 0.5 * (1.0 - Math.Cos(Math.PI * f / fadeFrames));
            for (int ch = 0; ch < channels; ch++)
            {
                int idx = f * info.BlockAlign + ch * 2;
                short orig = BinaryPrimitives.ReadInt16LittleEndian(outSpan.Slice(idx, 2));
                short ramped = (short)Math.Clamp((int)Math.Round(orig * gain), short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(outSpan.Slice(idx, 2), ramped);
            }
        }

        var tmpPath = wavPath + ".trim.tmp";
        try
        {
            using (var outFs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(outFs, Encoding.ASCII))
            {
                writer.Write("RIFF"u8);
                writer.Write(checked((uint)(36 + keptBytesLen)));
                writer.Write("WAVEfmt "u8);
                writer.Write(16u);
                writer.Write((ushort)1);
                writer.Write(info.Channels);
                writer.Write(info.SampleRate);
                writer.Write(checked(info.SampleRate * info.BlockAlign));
                writer.Write(info.BlockAlign);
                writer.Write(info.BitsPerSample);
                writer.Write("data"u8);
                writer.Write(checked((uint)keptBytesLen));
                writer.Write(slicedBytes);
            }
            File.Move(tmpPath, wavPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpPath)) try { File.Delete(tmpPath); } catch { }
        }
    }

    /// <summary>
    /// Stretches ONLY the steady periodic vocalic core of the final voiced syllable using YIN fundamental
    /// period estimation + Bidirectional Glottal-Epoch Pitch-Mark Tracking + Exact Complementary Interval TD-PSOLA
    /// (Praat / Moulines-Charpentier pitch-synchronous overlap-add) for trailing ellipsis ("Giết...", "Không...", "Sư phụ ơi...").
    /// Eliminates fixed-window WSOLA subharmonic beating ("lỗi rè rè") on both checked ('Giết...') and open syllables
    /// while preserving initial consonants ('gi-') and final codas/off-glides ('-t', '-i', '-ng') with C1 phase continuity.
    /// </summary>
    public static void ElongateFinalSyllableVowelInPlace(string wavPath, double stretchFactor, int minTargetCoreMs = 0)
    {
        if ((!double.IsFinite(stretchFactor) || stretchFactor <= 1.05) && minTargetCoreMs <= 0) return;
        stretchFactor = double.IsFinite(stretchFactor) ? Math.Clamp(stretchFactor, 1.05, 5.2) : 1.05;

        var info = ReadPcmInfo(wavPath);
        int sr = (int)info.SampleRate;
        int channels = info.Channels;
        int blockAlign = info.BlockAlign;
        int totalFrames = (int)info.FrameCount;
        if (totalFrames < sr / 8) return; // < 125ms: too short to elongate safely

        byte[] rawBytes;
        using (var fs = new FileStream(wavPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            fs.Seek(info.DataOffset, SeekOrigin.Begin);
            rawBytes = new byte[info.DataLength];
            int offset = 0;
            while (offset < rawBytes.Length)
            {
                int r = fs.Read(rawBytes, offset, rawBytes.Length - offset);
                if (r == 0) break;
                offset += r;
            }
        }

        var samples = new short[totalFrames * channels];
        ReadOnlySpan<byte> inBytes = rawBytes;
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(inBytes.Slice(i * 2, 2));

        int rmsWin = Math.Max(1, sr * 5 / 1000); // 5ms envelope window
        int nRms = totalFrames / rmsWin;
        if (nRms < 16) return;

        var rms = new double[nRms];
        double peak = 0;
        for (int w = 0; w < nRms; w++)
        {
            double sumSq = 0;
            int baseFrame = w * rmsWin;
            for (int f = 0; f < rmsWin; f++)
            {
                short cur = samples[(baseFrame + f) * channels];
                sumSq += (double)cur * cur;
            }
            double val = Math.Sqrt(sumSq / rmsWin);
            rms[w] = val;
            if (val > peak) peak = val;
        }
        if (peak < 80.0) return;

        double tailThresh = Math.Max(60.0, peak * 0.035);
        int lastVoiced = -1;
        for (int k = nRms - 1; k >= 0; k--)
        {
            if (rms[k] >= tailThresh) { lastVoiced = k; break; }
        }
        if (lastVoiced < 6) return;

        int peakLookBack = Math.Max(0, lastVoiced - (int)(0.240 / 0.005));
        int localPeakIdx = lastVoiced;
        for (int k = lastVoiced; k >= peakLookBack; k--)
        {
            if (rms[k] > rms[localPeakIdx]) localPeakIdx = k;
        }
        double localPeak = rms[localPeakIdx];
        if (localPeak < peak * 0.14) return;

        // Isolate the pure high-energy vocalic plateau (>= 38% of syllable peak); YIN CMNDF below verifies true periodicity.
        double corePlateauThresh = Math.Max(95.0, localPeak * 0.38);
        int maxCoreBack = Math.Max(0, localPeakIdx - (int)(0.220 / 0.005));
        int cStart = localPeakIdx;
        while (cStart > maxCoreBack && rms[cStart - 1] >= corePlateauThresh)
            cStart--;
        int cEnd = localPeakIdx;
        while (cEnd + 1 <= lastVoiced && rms[cEnd + 1] >= corePlateauThresh)
            cEnd++;

        int rawStart = Math.Clamp(cStart * rmsWin, 0, totalFrames - 1);
        int rawEnd = Math.Clamp((cEnd + 1) * rmsWin, rawStart + 1, totalFrames);

        // Preserve 15ms front consonant formant transition and 22ms back coda/off-glide transition untouched.
        int vStart = rawStart + (int)(sr * 0.015);
        int vEnd = rawEnd - (int)(sr * 0.022);
        if (vEnd - vStart < (int)(sr * 0.040))
        {
            int midFallback = (rawStart + rawEnd) / 2;
            int half = Math.Max((int)(sr * 0.020), ((rawEnd - rawStart) - (int)(sr * 0.016)) / 2);
            vStart = Math.Clamp(midFallback - half, 0, totalFrames - 1);
            vEnd = Math.Clamp(midFallback + half, vStart + 1, totalFrames);
        }

        int coreLen = vEnd - vStart;
        if (coreLen < Math.Max(64, (int)(sr * 0.036))) return;

        // 1. YIN Cumulative Mean Normalized Difference Function (CMNDF) pitch period estimator (65 Hz .. 450 Hz).
        int minP = Math.Max(16, (int)(sr / 450.0));
        int maxP = Math.Max(minP + 8, (int)(sr / 65.0));
        int coreMid = (vStart + vEnd) / 2;
        int yinHalf = Math.Min(totalFrames / 2 - 4, Math.Max(coreLen / 2, (int)(sr * 0.024)));
        int yinStart = Math.Max(0, coreMid - yinHalf);
        int yinEnd = Math.Min(totalFrames, coreMid + yinHalf);
        int yinLen = yinEnd - yinStart;
        int maxTau = Math.Min(maxP, yinLen / 2 - 2);
        if (maxTau <= minP + 4) return;

        int yinW = yinLen - maxTau;
        var cmndf = new double[maxTau + 1];
        cmndf[0] = 1.0;
        double runningSum = 0;
        for (int tau = 1; tau <= maxTau; tau++)
        {
            double diffSq = 0;
            for (int j = 0; j < yinW; j++)
            {
                double df = samples[(yinStart + j) * channels] - samples[(yinStart + j + tau) * channels];
                diffSq += df * df;
            }
            runningSum += diffSq;
            cmndf[tau] = runningSum > 1e-9 ? (diffSq * tau / runningSum) : 1.0;
        }

        int bestLag = -1;
        for (int tau = minP; tau < maxTau; tau++)
        {
            if (cmndf[tau] < 0.15)
            {
                while (tau + 1 <= maxTau && cmndf[tau + 1] <= cmndf[tau])
                    tau++;
                bestLag = tau;
                break;
            }
        }
        if (bestLag < 0)
        {
            bestLag = minP;
            for (int tau = minP + 1; tau <= maxTau; tau++)
                if (cmndf[tau] < cmndf[bestLag]) bestLag = tau;
        }
        // Strict periodicity guard: abort untouched if vowel is creaky/hoarse/aperiodic (prevents any buzz!)
        if (cmndf[bestLag] > 0.35) return;

        // 2. Dominant glottal impulse polarity (+1 or -1) inside [vStart .. vEnd]
        int posPeak = 1, negPeak = 1;
        for (int f = vStart; f < vEnd; f++)
        {
            int s = samples[f * channels];
            if (s > posPeak) posPeak = s;
            if (-s > negPeak) negPeak = -s;
        }
        int sign = posPeak >= negPeak ? 1 : -1;
        double ampNorm = Math.Max(1.0, Math.Max(posPeak, negPeak));

        // 3. Bidirectional Glottal-Epoch Pitch-Mark Tracking from vowel center outward
        int seedLo = Math.Max(maxP + 2, coreMid - (int)(bestLag * 0.65));
        int seedHi = Math.Min(totalFrames - maxP - 2, coreMid + (int)(bestLag * 0.65));
        if (seedHi <= seedLo) return;

        int seed = seedLo;
        for (int f = seedLo + 1; f <= seedHi; f++)
        {
            if (sign * samples[f * channels] > sign * samples[seed * channels])
                seed = f;
        }

        var fwdMarks = new List<int>(64) { seed };
        int curMark = seed;
        int locT = bestLag;
        int limitHi = Math.Min(totalFrames - maxP - 2, vEnd + bestLag);
        while (curMark + (int)(locT * 0.78) <= limitHi)
        {
            int lo = curMark + (int)(locT * 0.80);
            int hi = Math.Min(limitHi, curMark + (int)(locT * 1.22));
            if (hi <= lo) break;
            int hw = Math.Max(12, (int)(locT * 0.65));
            double refE = 1e-6;
            for (int n = -hw; n < hw; n++)
            {
                double r = samples[(curMark + n) * channels];
                refE += r * r;
            }
            int bestM = lo;
            double bestSc = double.NegativeInfinity;
            for (int m = lo; m <= hi; m++)
            {
                double dot = 0, candE = 1e-6;
                for (int n = -hw; n < hw; n++)
                {
                    double r = samples[(curMark + n) * channels];
                    double c = samples[(m + n) * channels];
                    dot += r * c;
                    candE += c * c;
                }
                double corr = dot / Math.Sqrt(refE * candE);
                double prox = 1.0 - 0.15 * Math.Abs((m - curMark) - locT) / (double)locT;
                double amp = (sign * samples[m * channels]) / ampNorm;
                double sc = corr * prox + 0.15 * amp;
                if (sc > bestSc) { bestSc = sc; bestM = m; }
            }
            locT = Math.Clamp((int)Math.Round(0.75 * locT + 0.25 * (bestM - curMark)), minP, maxP);
            fwdMarks.Add(bestM);
            curMark = bestM;
        }

        var bwdMarks = new List<int>(64);
        curMark = seed;
        locT = bestLag;
        int limitLo = Math.Max(maxP + 2, vStart - bestLag);
        while (curMark - (int)(locT * 0.78) >= limitLo)
        {
            int hi = curMark - (int)(locT * 0.80);
            int lo = Math.Max(limitLo, curMark - (int)(locT * 1.22));
            if (hi <= lo) break;
            int hw = Math.Max(12, (int)(locT * 0.65));
            double refE = 1e-6;
            for (int n = -hw; n < hw; n++)
            {
                double r = samples[(curMark + n) * channels];
                refE += r * r;
            }
            int bestM = hi;
            double bestSc = double.NegativeInfinity;
            for (int m = lo; m <= hi; m++)
            {
                double dot = 0, candE = 1e-6;
                for (int n = -hw; n < hw; n++)
                {
                    double r = samples[(curMark + n) * channels];
                    double c = samples[(m + n) * channels];
                    dot += r * c;
                    candE += c * c;
                }
                double corr = dot / Math.Sqrt(refE * candE);
                double prox = 1.0 - 0.15 * Math.Abs((curMark - m) - locT) / (double)locT;
                double amp = (sign * samples[m * channels]) / ampNorm;
                double sc = corr * prox + 0.15 * amp;
                if (sc > bestSc) { bestSc = sc; bestM = m; }
            }
            locT = Math.Clamp((int)Math.Round(0.75 * locT + 0.25 * (curMark - bestM)), minP, maxP);
            bwdMarks.Add(bestM);
            curMark = bestM;
        }

        bwdMarks.Reverse();
        var coreMarks = new List<int>(bwdMarks.Count + fwdMarks.Count);
        foreach (var m in bwdMarks) if (m >= vStart && m <= vEnd) coreMarks.Add(m);
        foreach (var m in fwdMarks) if (m >= vStart && m <= vEnd) coreMarks.Add(m);
        if (coreMarks.Count < 3) return;

        int nIn = coreMarks.Count;
        var rightPeriods = new int[nIn];
        for (int i = 0; i < nIn; i++)
        {
            int p = i + 1 < nIn ? (coreMarks[i + 1] - coreMarks[i]) : (coreMarks[i] - coreMarks[i - 1]);
            rightPeriods[i] = Math.Max(minP, p);
        }

        int firstM = coreMarks[0];
        int lastM = coreMarks[nIn - 1];
        int origSpan = lastM - firstM;
        if (origSpan < minP * 2) return;

        double maxRatioCap = stretchFactor >= 4.0 ? 5.0 : 4.5;
        int byFactor = (int)Math.Round(origSpan * stretchFactor);
        int byTarget = minTargetCoreMs > 0 ? (int)Math.Round(sr * (minTargetCoreMs / 1000.0)) : 0;
        int targetSpan = Math.Max(origSpan + minP, Math.Max(byFactor, byTarget));
        targetSpan = Math.Min(targetSpan, (int)Math.Round(origSpan * maxRatioCap));
        double effRatio = targetSpan / (double)origSpan;
        if (effRatio <= 1.12) return;

        int nOut = Math.Max(nIn + 1, (int)Math.Round((nIn - 1) * effRatio) + 1);
        var outMarks = new int[nOut];
        var outSrc = new int[nOut];
        outMarks[0] = 0;
        outSrc[0] = 0;
        for (int j = 1; j < nOut; j++)
        {
            double u = j / (double)(nOut - 1);
            double fIdx = u * (nIn - 1);
            int k = j == nOut - 1 ? (nIn - 1) : Math.Min(nIn - 1, (int)Math.Round(fIdx));
            int k0 = Math.Min(nIn - 2, (int)Math.Floor(fIdx));
            double frac = fIdx - k0;
            int step = Math.Max(minP, (int)Math.Round((1.0 - frac) * rightPeriods[k0] + frac * rightPeriods[k0 + 1]));
            outMarks[j] = outMarks[j - 1] + step;
            outSrc[j] = k;
        }

        int spanLen = outMarks[nOut - 1];
        int suffixLen = totalFrames - lastM;
        int newTotalFrames = firstM + spanLen + suffixLen;
        long newDataBytesLong = (long)newTotalFrames * blockAlign;
        if (newDataBytesLong > uint.MaxValue - 36L) return;
        int newDataBytes = (int)newDataBytesLong;

        var outBytes = new byte[newDataBytes];
        Span<byte> outSpan = outBytes;

        // 1. Untouched prefix + initial consonant up to firstM (exact C1 phase match with j=0!)
        if (firstM > 0)
            Buffer.BlockCopy(rawBytes, 0, outBytes, 0, firstM * blockAlign);

        // 2. Exact Complementary Raised-Cosine Interval TD-PSOLA across [firstM .. firstM + spanLen]
        for (int j = 0; j < nOut - 1; j++)
        {
            int kA = outSrc[j];
            int kB = outSrc[j + 1];
            int mA = coreMarks[kA];
            int mB = coreMarks[kB];
            int D = outMarks[j + 1] - outMarks[j];
            int dstStart = firstM + outMarks[j];

            double uA = j / (double)(nOut - 1);
            double uB = (j + 1) / (double)(nOut - 1);
            // Smooth natural breath arc (1.0 -> 0.88 -> 1.0 at lastM so splice into coda has zero gain step!)
            double gA = 1.0 - 0.12 * Math.Sin(Math.PI * uA);
            double gB = 1.0 - 0.12 * Math.Sin(Math.PI * uB);

            for (int n = 0; n < D; n++)
            {
                double wA = 0.5 * (1.0 + Math.Cos(Math.PI * n / D));
                double wB = 1.0 - wA;
                int idxA = mA + n;
                int idxB = mB - (D - n);
                int writeFrame = dstStart + n;
                for (int ch = 0; ch < channels; ch++)
                {
                    double sA = (uint)idxA < (uint)totalFrames ? samples[idxA * channels + ch] * gA : 0.0;
                    double sB = (uint)idxB < (uint)totalFrames ? samples[idxB * channels + ch] * gB : 0.0;
                    short val = (short)Math.Clamp((int)Math.Round(wA * sA + wB * sB), short.MinValue, short.MaxValue);
                    BinaryPrimitives.WriteInt16LittleEndian(outSpan.Slice(writeFrame * blockAlign + ch * 2, 2), val);
                }
            }
        }

        // 3. Untouched coda + trailing silence from lastM onward (exact C1 phase match with end of j=nOut-2!)
        if (suffixLen > 0)
        {
            Buffer.BlockCopy(rawBytes, lastM * blockAlign, outBytes, (firstM + spanLen) * blockAlign, suffixLen * blockAlign);
        }

        var tmpPath = wavPath + ".ellip.tmp";
        try
        {
            using (var outFs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(outFs, Encoding.ASCII))
            {
                writer.Write("RIFF"u8);
                writer.Write(checked((uint)(36 + newDataBytes)));
                writer.Write("WAVEfmt "u8);
                writer.Write(16u);
                writer.Write((ushort)1);
                writer.Write(info.Channels);
                writer.Write(info.SampleRate);
                writer.Write(checked(info.SampleRate * info.BlockAlign));
                writer.Write(info.BlockAlign);
                writer.Write(info.BitsPerSample);
                writer.Write("data"u8);
                writer.Write(checked((uint)newDataBytes));
                writer.Write(outBytes);
            }
            File.Move(tmpPath, wavPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpPath)) try { File.Delete(tmpPath); } catch { }
        }
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
