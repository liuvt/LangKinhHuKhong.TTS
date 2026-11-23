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
    /// Slices off the leading carrier warmup syllable ("À: " / "Ừ: ") at the exact silence valley
    /// produced by ':' before the target 1-2 word utterance, preserving 100% of initial voiced fricatives
    /// (e.g. 'z-' in "Giết", "Giữ", "Giếng") with a click-free 4ms linear ramp-in.
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
        int winFrames = Math.Max(1, (int)(info.SampleRate * 6 / 1000)); // 6ms analysis window
        int nWin = totalFrames / winFrames;
        if (nWin < 20) return;

        var rms = new double[nWin];
        double peak = 0;
        ReadOnlySpan<byte> byteSpan = rawBytes;
        for (int w = 0; w < nWin; w++)
        {
            double sumSq = 0;
            int baseFrame = w * winFrames;
            for (int f = 0; f < winFrames; f++)
            {
                int byteIdx = (baseFrame + f) * info.BlockAlign;
                short s = BinaryPrimitives.ReadInt16LittleEndian(byteSpan.Slice(byteIdx, 2));
                sumSq += (double)s * s;
            }
            double val = Math.Sqrt(sumSq / winFrames);
            rms[w] = val;
            if (val > peak) peak = val;
        }
        if (peak < 80.0) return;

        int firstVoice = 0;
        for (int k = 0; k < nWin; k++)
        {
            if (rms[k] > peak * 0.06) { firstVoice = k; break; }
        }

        // 1. Find the energy peak of the FIRST syllable (the carrier 'Út'/'Ất'/'At').
        int carrierPeakEnd = Math.Min(nWin - 4, firstVoice + (int)(0.165 / 0.006));
        int carrierPeakIdx = firstVoice;
        for (int k = firstVoice + 1; k <= carrierPeakEnd; k++)
        {
            if (rms[k] > rms[carrierPeakIdx]) carrierPeakIdx = k;
        }
        double carrierPeak = rms[carrierPeakIdx];
        if (carrierPeak < 80.0) return;

        // 2. Walk down the back slope of the carrier syllable into the FIRST ':' silence/closure valley,
        //    and STOP immediately as soon as speech energy rebounds on the target word!
        //    This guarantees we never skip over word #1 of a 2-word phrase or jump to trailing EOF silence.
        int walkStart = Math.Min(nWin - 3, carrierPeakIdx + 2);
        int walkEnd = Math.Min(nWin - 3, firstVoice + (int)(0.380 / 0.006));
        if (walkEnd <= walkStart) return;

        double quietFloor = Math.Max(85.0, peak * 0.030);
        int valleyFirstIdx = walkStart;
        int valleyMinIdx = walkStart;
        double valleyMin = rms[walkStart];
        bool enteredValley = valleyMin <= quietFloor;
        int targetStart = -1;

        for (int k = walkStart + 1; k <= walkEnd; k++)
        {
            double cur = rms[k];
            if (cur < valleyMin)
            {
                valleyMin = cur;
                valleyMinIdx = k;
                if (!enteredValley && cur <= quietFloor)
                {
                    enteredValley = true;
                    valleyFirstIdx = k;
                }
            }
            else if (!enteredValley && cur <= quietFloor)
            {
                enteredValley = true;
                valleyFirstIdx = k;
            }

            // Once we have descended into a genuine valley (<= 20% of carrierPeak), detect the exact rising onset of the target word.
            if (valleyMin <= carrierPeak * 0.20)
            {
                double wakeThresh = Math.Max(95.0, Math.Max(quietFloor * 1.20, valleyMin * 2.15));
                if (cur > wakeThresh && rms[k + 1] > wakeThresh * 0.85)
                {
                    targetStart = k;
                    break;
                }
            }
        }

        // If target onset started slightly after walkEnd (long '>'380ms pause after ':'), scan forward from valleyMinIdx.
        if (targetStart < 0 && valleyMin <= carrierPeak * 0.20)
        {
            double wakeThresh = Math.Max(95.0, Math.Max(quietFloor * 1.20, valleyMin * 2.15));
            for (int k = walkEnd + 1; k < nWin - 1; k++)
            {
                if (rms[k] > wakeThresh)
                {
                    targetStart = k;
                    break;
                }
            }
        }
        if (targetStart < 0 || targetStart >= nWin - 2) return;

        double postTargetPeak = 0;
        for (int k = targetStart; k < nWin; k++)
            if (rms[k] > postTargetPeak) postTargetPeak = rms[k];
        if (postTargetPeak < peak * 0.15) return; // Safety: never slice if target speech energy isn't after cut!

        // On deep silent gaps (enteredValley), keep ~12ms silent pre-breath; on tight voiced/stop transitions (lacphi), cut right at valleyMinIdx.
        int minSafeCut = enteredValley ? valleyFirstIdx + 1 : valleyMinIdx;
        int cutWin = Math.Max(minSafeCut, targetStart - 2);
        int cutFrame = Math.Clamp(cutWin * winFrames, 0, totalFrames - (int)(info.SampleRate / 20));
        if (cutFrame <= 0) return;

        int keptFrames = totalFrames - cutFrame;
        int keptBytesLen = keptFrames * info.BlockAlign;
        byte[] slicedBytes = new byte[keptBytesLen];
        Buffer.BlockCopy(rawBytes, cutFrame * info.BlockAlign, slicedBytes, 0, keptBytesLen);

        // Smooth 4ms linear fade-in at slice boundary to prevent any zero-crossing click
        int fadeFrames = Math.Min(keptFrames, Math.Max(1, (int)(info.SampleRate * 4 / 1000)));
        Span<byte> outSpan = slicedBytes;
        for (int f = 0; f < fadeFrames; f++)
        {
            double gain = f / (double)fadeFrames;
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
    /// Stretches ONLY the steady vocalic core of the final voiced syllable using pitch-locked
    /// time-domain WSOLA (Waveform Similarity Overlap-Add) for trailing ellipsis ("Giết...", "Sư phụ ơi...").
    /// Preserves initial consonants (e.g. 'gi-') and final off-glides/codas ('-t', '-i', '-ng') untouched
    /// with zero phase-vocoder metallic artifacts, while keeping downstream PCM/SRT frame counts exact.
    /// </summary>
    public static void ElongateFinalSyllableVowelInPlace(string wavPath, double stretchFactor, int minTargetCoreMs = 0)
    {
        if ((!double.IsFinite(stretchFactor) || stretchFactor <= 1.05) && minTargetCoreMs <= 0) return;
        stretchFactor = double.IsFinite(stretchFactor) ? Math.Clamp(stretchFactor, 1.05, 8.0) : 1.05;

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
        if (nRms < 12) return;

        var rms = new double[nRms];
        double peak = 0;
        for (int w = 0; w < nRms; w++)
        {
            double sumSq = 0;
            int baseFrame = w * rmsWin;
            for (int f = 0; f < rmsWin; f++)
            {
                double s = samples[(baseFrame + f) * channels];
                sumSq += s * s;
            }
            double val = Math.Sqrt(sumSq / rmsWin);
            rms[w] = val;
            if (val > peak) peak = val;
        }
        if (peak < 80.0) return;

        // Low 3.5% threshold ensures soft final glides/codas ('-i' in "ơi", '-u', '-o') stay inside lastVoiced.
        double tailThresh = Math.Max(60.0, peak * 0.035);
        int lastVoiced = -1;
        for (int k = nRms - 1; k >= 0; k--)
        {
            if (rms[k] >= tailThresh) { lastVoiced = k; break; }
        }
        if (lastVoiced < 4) return;

        int peakLookBack = Math.Max(0, lastVoiced - (int)(0.240 / 0.005));
        int localPeakIdx = lastVoiced;
        for (int k = lastVoiced; k >= peakLookBack; k--)
        {
            if (rms[k] > rms[localPeakIdx]) localPeakIdx = k;
        }
        double localPeak = rms[localPeakIdx];
        if (localPeak < peak * 0.12) return;

        // Isolate the pure high-energy vocalic plateau (>= 36% of syllable peak) around localPeakIdx.
        // This naturally excludes lower-energy initial fricatives/plosives ('gi-', 'kh-', 'đ-') AND
        // excludes final stop closures/off-glides ('-t', '-p', '-c', '-i', '-u') without wasting half the vowel!
        double corePlateauThresh = Math.Max(90.0, localPeak * 0.36);
        int maxCoreBack = Math.Max(0, localPeakIdx - (int)(0.220 / 0.005));
        int cStart = localPeakIdx;
        while (cStart > maxCoreBack && rms[cStart - 1] >= corePlateauThresh)
            cStart--;
        int cEnd = localPeakIdx;
        while (cEnd + 1 <= lastVoiced && rms[cEnd + 1] >= corePlateauThresh)
            cEnd++;

        int rawStart = Math.Clamp(cStart * rmsWin, 0, totalFrames - 1);
        int rawEnd = Math.Clamp((cEnd + 1) * rmsWin, rawStart + 1, totalFrames);

        // Preserve 12ms front formant transition and 22ms back coda/off-glide transition untouched.
        int vStart = rawStart + (int)(sr * 0.012);
        int vEnd = rawEnd - (int)(sr * 0.022);
        if (vEnd - vStart < (int)(sr * 0.042))
        {
            int mid = (rawStart + rawEnd) / 2;
            int half = Math.Max((int)(sr * 0.021), ((rawEnd - rawStart) - (int)(sr * 0.016)) / 2);
            vStart = Math.Clamp(mid - half, 0, totalFrames - 1);
            vEnd = Math.Clamp(mid + half, vStart + 1, totalFrames);
        }

        int coreLen = vEnd - vStart;
        int minCore = Math.Max(64, (int)(sr * 0.036));
        if (coreLen < minCore) return;

        int byFactor = (int)Math.Round(coreLen * stretchFactor);
        int byTarget = minTargetCoreMs > 0 ? (int)Math.Round(sr * (minTargetCoreMs / 1000.0)) : 0;
        int outCoreLen = Math.Max(coreLen + 1, Math.Max(byFactor, byTarget));
        outCoreLen = Math.Min(outCoreLen, (int)Math.Round(coreLen * 8.0)); // Cap ratio at 8x for naturalness
        if (outCoreLen <= coreLen + (int)(sr * 0.015)) return;

        int wLen = Math.Max(32, (int)(sr * 0.018) & ~1); // 18ms Hann grain (even)
        if (wLen > coreLen - 6)
            wLen = Math.Max(16, (coreLen / 2) & ~1);
        int hSyn = wLen / 2; // 50% hop -> exact Constant Overlap-Add (COLA = 1.0)
        int search = Math.Max(4, (int)(sr * 0.0065)); // ±6.5ms F0 pitch correlation range
        int minSelfDist = Math.Max(8, (int)(sr * 0.0012)); // Prevents locking onto 1 single glottal cycle on 5x-7.5x holds

        var hann = new double[wLen];
        for (int n = 0; n < wLen; n++)
            hann[n] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * n / wLen));

        int accFrames = outCoreLen + wLen + 4;
        var outAcc = new double[accFrames * channels];
        var normAcc = new double[accFrames];

        void AddGrain(int outFrameOffset, int coreFrameOffset)
        {
            int srcBase = (vStart + coreFrameOffset) * channels;
            int dstBase = outFrameOffset * channels;
            for (int n = 0; n < wLen; n++)
            {
                double w = hann[n];
                normAcc[outFrameOffset + n] += w;
                int sIdx = srcBase + n * channels;
                int dIdx = dstBase + n * channels;
                for (int ch = 0; ch < channels; ch++)
                    outAcc[dIdx + ch] += samples[sIdx + ch] * w;
            }
        }

        AddGrain(0, 0);
        int prevIn = 0;
        int outPos = hSyn;
        int maxStart = Math.Max(0, coreLen - wLen);

        while (outPos < outCoreLen)
        {
            double progress = Math.Clamp(outPos / (double)Math.Max(1, outCoreLen - wLen), 0.0, 1.0);
            int targetIn = (int)Math.Round(progress * maxStart);
            int bestPos = targetIn;
            int refPos = prevIn + hSyn;

            if (refPos + hSyn <= coreLen && maxStart > 0)
            {
                int lo = Math.Max(0, targetIn - search);
                int hi = Math.Min(maxStart, targetIn + search);
                if (hi > lo)
                {
                    double refEnergy = 0;
                    int refBase = (vStart + refPos) * channels;
                    for (int m = 0; m < hSyn; m++)
                    {
                        double r = samples[refBase + m * channels];
                        refEnergy += r * r;
                    }

                    if (refEnergy > 1.0)
                    {
                        bool enforceDiversity = (hi - lo) > minSelfDist * 3;
                        double bestScore = double.NegativeInfinity;
                        for (int cand = lo; cand <= hi; cand++)
                        {
                            if (enforceDiversity && Math.Abs(cand - prevIn) < minSelfDist)
                                continue;
                            double dot = 0;
                            double candEnergy = 0;
                            int candBase = (vStart + cand) * channels;
                            for (int m = 0; m < hSyn; m++)
                            {
                                double r = samples[refBase + m * channels];
                                double c = samples[candBase + m * channels];
                                dot += r * c;
                                candEnergy += c * c;
                            }
                            double score = dot / Math.Sqrt(refEnergy * candEnergy + 1e-6);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestPos = cand;
                            }
                        }
                    }
                }
            }

            AddGrain(outPos, bestPos);
            prevIn = bestPos;
            outPos += hSyn;
        }

        var stretchedCore = new short[outCoreLen * channels];
        for (int f = 0; f < outCoreLen; f++)
        {
            double u = f / (double)outCoreLen;
            double norm = normAcc[f] > 1e-4 ? normAcc[f] : 1.0;
            // Expressive vocal breath arc: slight initial resonance bloom (+3%) then natural breath taper (-16%)
            double taper = 1.0 + 0.03 * Math.Sin(Math.PI * Math.Min(1.0, u * 1.5)) - 0.16 * Math.Pow(u, 1.35);
            double scale = taper / norm;
            for (int ch = 0; ch < channels; ch++)
            {
                int v = (int)Math.Round(outAcc[f * channels + ch] * scale);
                stretchedCore[f * channels + ch] = (short)Math.Clamp(v, short.MinValue, short.MaxValue);
            }
        }

        int tailAvailable = totalFrames - vEnd;
        int xfade = Math.Min(Math.Min(outCoreLen, tailAvailable), Math.Max(1, sr * 10 / 1000));
        int newTotalFrames = totalFrames - coreLen + outCoreLen;
        long newDataBytesLong = (long)newTotalFrames * blockAlign;
        if (newDataBytesLong > uint.MaxValue - 36L) return;
        int newDataBytes = (int)newDataBytesLong;

        var outBytes = new byte[newDataBytes];
        Span<byte> outSpan = outBytes;
        int writeFrame = 0;

        // 1. Prefix + syllable onset (untouched)
        Buffer.BlockCopy(rawBytes, 0, outBytes, 0, vStart * blockAlign);
        writeFrame += vStart;

        // 2. Stretched vowel core before crossfade
        int coreBodyFrames = outCoreLen - xfade;
        for (int f = 0; f < coreBodyFrames; f++, writeFrame++)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(
                    outSpan.Slice(writeFrame * blockAlign + ch * 2, 2),
                    stretchedCore[f * channels + ch]);
            }
        }

        // 3. 8ms smooth crossfade from stretched vowel core into untouched coda
        for (int f = 0; f < xfade; f++, writeFrame++)
        {
            double alpha = f / (double)xfade;
            int stretchIdx = (coreBodyFrames + f) * channels;
            int codaIdx = (vEnd + f) * channels;
            for (int ch = 0; ch < channels; ch++)
            {
                double mixed = (1.0 - alpha) * stretchedCore[stretchIdx + ch] + alpha * samples[codaIdx + ch];
                short val = (short)Math.Clamp((int)Math.Round(mixed), short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(outSpan.Slice(writeFrame * blockAlign + ch * 2, 2), val);
            }
        }

        // 4. Remaining coda + trailing silence (untouched)
        int remStartFrame = vEnd + xfade;
        int remFrames = totalFrames - remStartFrame;
        if (remFrames > 0)
        {
            Buffer.BlockCopy(rawBytes, remStartFrame * blockAlign, outBytes, writeFrame * blockAlign, remFrames * blockAlign);
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
