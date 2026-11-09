namespace OpenCorneroids.Content.Xnb;

/// <summary>
/// A decoder for LZX, the LZ77 + Huffman compression XNA uses for compressed <c>.xnb</c> files, written from the
/// published description of the format (as in Microsoft's cabinet LZX). One decoder decompresses one stream of frames:
/// the sliding window, repeated offsets and Huffman code lengths carry over from frame to frame.
/// </summary>
/// <remarks>
/// Bits are read most significant first from 16-bit little-endian words. A block is verbatim (literals and matches),
/// aligned-offset (matches whose low three offset bits have their own Huffman code) or uncompressed. Code lengths are
/// sent as deltas against the previous block's, through a 20-symbol pre-tree. Damaged input throws
/// <see cref="InvalidDataException"/>; it never reads or writes outside its buffers.
/// </remarks>
public sealed class LzxDecoder
{
    const int MinMatch = 2;
    const int NumChars = 256;
    const int PretreeElements = 20;
    const int AlignedElements = 8;
    const int LengthElements = 249;
    const int MaxCodeLength = 16;

    enum BlockType { Invalid = 0, Verbatim = 1, Aligned = 2, Uncompressed = 3 }

    static readonly int[] ExtraBits = new int[51];
    static readonly int[] PositionBase = new int[51];

    static LzxDecoder()
    {
        for (int i = 0, j = 0; i < 51; i += 2)
        {
            ExtraBits[i] = j;
            if (i + 1 < 51) ExtraBits[i + 1] = j;
            if (i != 0 && j < 17) j++;
        }
        for (int i = 0, j = 0; i < 51; i++)
        {
            PositionBase[i] = j;
            j += 1 << ExtraBits[i];
        }
    }

    readonly byte[] window;
    readonly int windowSize;
    readonly int mainElements;
    readonly byte[] mainLengths;
    readonly byte[] lengthLengths = new byte[LengthElements];
    readonly byte[] alignedLengths = new byte[AlignedElements];
    Huffman main = null!, length = null!, aligned = null!;
    int windowPos;
    int r0 = 1, r1 = 1, r2 = 1;
    bool headerRead;
    BlockType blockType = BlockType.Invalid;
    int blockRemaining;
    int blockLength;
    bool intelStarted;
    int intelFileSize;
    int framesRead;

    /// <param name="windowBits">15 to 21; XNA uses 16 (a 64 KB window).</param>
    public LzxDecoder(int windowBits = 16)
    {
        if (windowBits is < 15 or > 21) throw new ArgumentOutOfRangeException(nameof(windowBits));
        windowSize = 1 << windowBits;
        window = new byte[windowSize];
        int positionSlots = windowBits == 20 ? 42 : windowBits == 21 ? 50 : windowBits * 2;
        mainElements = NumChars + positionSlots * 8;
        mainLengths = new byte[mainElements];
    }

    /// <summary>Decompresses one frame: <paramref name="input"/> holds its compressed bytes and it yields exactly
    /// <paramref name="output"/>.Length bytes (at most 32 KB, and never more than the window).</summary>
    public void Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (output.Length > windowSize) throw new InvalidDataException("LZX frame is larger than the window.");
        var bits = new BitReader(input);
        if (!headerRead)
        {
            if (bits.Read(1) != 0) intelFileSize = (bits.Read(16) << 16) | bits.Read(16);
            headerRead = true;
        }

        int togo = output.Length;
        int start = windowPos;
        while (togo > 0)
        {
            if (blockRemaining == 0)
            {
                if (blockType == BlockType.Uncompressed)
                {
                    // An uncompressed block of odd length is padded to a whole word.
                    if ((blockLength & 1) != 0) bits.SkipByte();
                    bits.Reset();
                }
                blockType = (BlockType)bits.Read(3);
                blockRemaining = blockLength = (bits.Read(16) << 8) | bits.Read(8);
                switch (blockType)
                {
                    case BlockType.Aligned:
                        for (int i = 0; i < AlignedElements; i++) alignedLengths[i] = (byte)bits.Read(3);
                        aligned = new Huffman(alignedLengths);
                        ReadMainAndLength(ref bits);
                        break;
                    case BlockType.Verbatim:
                        ReadMainAndLength(ref bits);
                        break;
                    case BlockType.Uncompressed:
                        intelStarted = true;
                        bits.AlignForBytes();
                        r0 = bits.ReadInt32();
                        r1 = bits.ReadInt32();
                        r2 = bits.ReadInt32();
                        break;
                    default:
                        throw new InvalidDataException($"LZX block type {(int)blockType} is not valid.");
                }
            }

            int run = Math.Min(blockRemaining, togo);
            togo -= run;
            blockRemaining -= run;
            if (windowPos + run > windowSize) throw new InvalidDataException("LZX block runs past the window.");

            if (blockType == BlockType.Uncompressed)
            {
                bits.CopyBytes(window.AsSpan(windowPos, run));
                windowPos += run;
                continue;
            }

            while (run > 0)
            {
                int symbol = main.Decode(ref bits);
                if (symbol < NumChars)
                {
                    window[windowPos++] = (byte)symbol;
                    run--;
                    continue;
                }
                symbol -= NumChars;
                int matchLength = symbol & 7;
                if (matchLength == 7) matchLength += length.Decode(ref bits);
                matchLength += MinMatch;
                int matchOffset = symbol >> 3;
                matchOffset = matchOffset switch
                {
                    0 => r0,
                    1 => SwapR1(),
                    2 => SwapR2(),
                    _ => NewOffset(matchOffset, ref bits),
                };
                if (matchLength > run) throw new InvalidDataException("LZX match runs past its block.");
                if (matchOffset <= 0 || matchOffset > windowSize) throw new InvalidDataException("LZX match offset is not valid.");
                run -= matchLength;
                int source = windowPos - matchOffset;
                if (source < 0) source += windowSize;
                for (int i = 0; i < matchLength; i++)
                {
                    window[windowPos++] = window[source++];
                    if (source == windowSize) source = 0;
                }
            }
            // A match may end past the run's end only if the data is bad; checked above.
        }

        window.AsSpan(start, output.Length).CopyTo(output);
        if (windowPos == windowSize) windowPos = 0;
        framesRead++;
        if (intelStarted && intelFileSize != 0 && output.Length > 10 && framesRead <= 32768) UndoE8(output);
    }

    int SwapR1()
    {
        (r0, r1) = (r1, r0);
        return r0;
    }

    int SwapR2()
    {
        (r0, r2) = (r2, r0);
        return r0;
    }

    int NewOffset(int slot, ref BitReader bits)
    {
        if (slot >= 51) throw new InvalidDataException("LZX position slot is not valid.");
        int extra = ExtraBits[slot];
        int offset = PositionBase[slot] - 2;
        if (blockType == BlockType.Aligned)
        {
            if (extra > 3)
            {
                offset += bits.Read(extra - 3) << 3;
                offset += aligned.Decode(ref bits);
            }
            else if (extra == 3) offset += aligned.Decode(ref bits);
            else if (extra > 0) offset += bits.Read(extra);
            else offset = 1;
        }
        else if (extra > 0)
        {
            offset += bits.Read(extra);
        }
        r2 = r1;
        r1 = r0;
        r0 = offset;
        return offset;
    }

    void ReadMainAndLength(ref BitReader bits)
    {
        ReadLengths(ref bits, mainLengths, 0, NumChars);
        ReadLengths(ref bits, mainLengths, NumChars, mainElements);
        main = new Huffman(mainLengths);
        if (mainLengths[0xE8] != 0) intelStarted = true;
        ReadLengths(ref bits, lengthLengths, 0, LengthElements);
        length = new Huffman(lengthLengths);
    }

    /// <summary>Code lengths <paramref name="first"/>..<paramref name="last"/> as deltas through a fresh pre-tree.</summary>
    static void ReadLengths(ref BitReader bits, byte[] lengths, int first, int last)
    {
        var preLengths = new byte[PretreeElements];
        for (int i = 0; i < PretreeElements; i++) preLengths[i] = (byte)bits.Read(4);
        var pre = new Huffman(preLengths);
        for (int x = first; x < last;)
        {
            int z = pre.Decode(ref bits);
            if (z == 17)
            {
                int run = bits.Read(4) + 4;
                Fill(lengths, ref x, last, run, 0);
            }
            else if (z == 18)
            {
                int run = bits.Read(5) + 20;
                Fill(lengths, ref x, last, run, 0);
            }
            else if (z == 19)
            {
                int run = bits.Read(1) + 4;
                if (x >= last) throw new InvalidDataException("LZX code lengths overrun.");
                int value = (lengths[x] - pre.Decode(ref bits) + 17) % 17;
                Fill(lengths, ref x, last, run, (byte)value);
            }
            else
            {
                lengths[x] = (byte)((lengths[x] - z + 17) % 17);
                x++;
            }
        }
    }

    static void Fill(byte[] lengths, ref int x, int last, int run, byte value)
    {
        if (x + run > last) throw new InvalidDataException("LZX code lengths overrun.");
        for (int i = 0; i < run; i++) lengths[x++] = value;
    }

    /// <summary>Undoes the x86 CALL translation the encoder may apply (never seen in XNB data, kept for completeness).</summary>
    void UndoE8(Span<byte> data)
    {
        int currentPos = (framesRead - 1) * 0x8000;
        for (int i = 0; i < data.Length - 10; i++)
        {
            if (data[i] != 0xE8) continue;
            int absolute = data[i + 1] | (data[i + 2] << 8) | (data[i + 3] << 16) | (data[i + 4] << 24);
            int pos = currentPos + i;
            if (absolute >= -pos && absolute < intelFileSize)
            {
                int relative = absolute >= 0 ? absolute - pos : absolute + intelFileSize;
                data[i + 1] = (byte)relative;
                data[i + 2] = (byte)(relative >> 8);
                data[i + 3] = (byte)(relative >> 16);
                data[i + 4] = (byte)(relative >> 24);
            }
            i += 4;
        }
    }

    /// <summary>Canonical Huffman code from code lengths, decoded a bit at a time (the data is small).</summary>
    sealed class Huffman
    {
        readonly int[] count = new int[MaxCodeLength + 1];
        readonly int[] symbols;

        public Huffman(byte[] lengths)
        {
            foreach (var l in lengths) count[l]++;
            count[0] = 0;
            var offsets = new int[MaxCodeLength + 2];
            for (int len = 1; len <= MaxCodeLength; len++) offsets[len + 1] = offsets[len] + count[len];
            symbols = new int[lengths.Length];
            for (int s = 0; s < lengths.Length; s++)
                if (lengths[s] != 0) symbols[offsets[lengths[s]]++] = s;
        }

        public int Decode(ref BitReader bits)
        {
            int code = 0, first = 0, index = 0;
            for (int len = 1; len <= MaxCodeLength; len++)
            {
                code |= bits.Read(1);
                int n = count[len];
                if (code - n < first) return symbols[index + (code - first)];
                index += n;
                first += n;
                first <<= 1;
                code <<= 1;
            }
            throw new InvalidDataException("LZX Huffman code is not valid.");
        }
    }

    /// <summary>MSB-first bits from 16-bit little-endian words, as LZX packs them.</summary>
    ref struct BitReader
    {
        readonly ReadOnlySpan<byte> data;
        int pos;
        uint buffer;
        int count;

        public BitReader(ReadOnlySpan<byte> data)
        {
            this.data = data;
            pos = 0;
            buffer = 0;
            count = 0;
        }

        public void Reset()
        {
            buffer = 0;
            count = 0;
        }

        void Fill()
        {
            while (count <= 16)
            {
                // Past the end the stream reads as zeros; a real overrun shows as a bad code or length.
                uint word = pos + 1 < data.Length ? (uint)(data[pos] | (data[pos + 1] << 8)) : 0;
                pos += 2;
                buffer |= word << (16 - count);
                count += 16;
            }
        }

        public int Read(int n)
        {
            if (n == 0) return 0;
            Fill();
            int value = (int)(buffer >> (32 - n));
            buffer <<= n;
            count -= n;
            return value;
        }

        /// <summary>Before an uncompressed block's header: drop to the next 16-bit boundary in the byte stream.</summary>
        public void AlignForBytes()
        {
            // The header is followed by 1 to 16 bits of padding up to the next word: a whole word when it ended on one.
            int consumedBits = pos * 8 - count;
            pos = (consumedBits / 16 + 1) * 2;
            buffer = 0;
            count = 0;
        }

        public void SkipByte() => pos++;

        public int ReadInt32()
        {
            if (pos + 4 > data.Length) throw new InvalidDataException("LZX stream ends inside a header.");
            int value = data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24);
            pos += 4;
            return value;
        }

        public void CopyBytes(Span<byte> destination)
        {
            if (pos + destination.Length > data.Length) throw new InvalidDataException("LZX uncompressed block is cut short.");
            data.Slice(pos, destination.Length).CopyTo(destination);
            pos += destination.Length;
        }
    }
}
