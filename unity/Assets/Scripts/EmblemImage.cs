using System;
using System.IO;
using System.IO.Compression;

/// <summary>
/// A cape emblem's picture: read an uploaded BMP or PNG, apply the rules, and write the small PNG that is stored and
/// sent. Plain sums on bytes and nothing from the engine, so the same code checks an upload on the map server, draws
/// the preview in the upload window and builds the texture on every screen — and can be checked outside Unity
/// (<c>tools/check-game-math.sh</c>).
///
/// <para><b>The rules</b> (decided 2026-10-16, after Ragnarok's guild emblem): a BMP or a PNG, exactly
/// <see cref="Side"/> x <see cref="Side"/> pixels, at most <see cref="MaxFileBytes"/> bytes. Pure pink (#FF00FF) is
/// see-through, and so is any pixel the PNG itself makes more than half see-through; every other pixel is solid, so the
/// stored picture is exactly what the cape draws.</para>
/// </summary>
public static class EmblemImage
{
    public const int Side = 24;
    public const int MaxFileBytes = 8192;

    /// <summary>The rules in the words the upload window shows, one per line.</summary>
    public static readonly string[] Rules =
    {
        "BMP or PNG",
        "Exactly " + Side + " x " + Side + " pixels",
        "At most " + MaxFileBytes / 1024 + " KB",
        "Pink (#FF00FF) shows as see-through",
        "Shows on the back of a worn cape",
    };

    /// <summary>Read <paramref name="file"/> into <see cref="Side"/>² pixels, four bytes each (red, green, blue, alpha),
    /// top row first, with the rules applied. Null with the reason in <paramref name="refusal"/> when it breaks one.</summary>
    public static byte[] Decode(byte[] file, out string refusal)
    {
        refusal = null;
        if (file == null || file.Length == 0) { refusal = "The file is empty"; return null; }
        if (file.Length > MaxFileBytes) { refusal = "Too big: at most " + MaxFileBytes / 1024 + " KB"; return null; }
        byte[] rgba;
        int width, height;
        try
        {
            if (file.Length > 2 && file[0] == 'B' && file[1] == 'M') rgba = ReadBmp(file, out width, out height, out refusal);
            else if (IsPng(file)) rgba = ReadPng(file, out width, out height, out refusal);
            else { refusal = "Not a BMP or PNG"; return null; }
        }
        catch (Exception) { refusal = "The picture is damaged"; return null; }
        if (rgba == null) return null;
        if (width != Side || height != Side) { refusal = "Wrong size: must be " + Side + " x " + Side + " pixels"; return null; }
        for (int i = 0; i < rgba.Length; i += 4)
        {
            bool pink = rgba[i] == 255 && rgba[i + 1] == 0 && rgba[i + 2] == 255;
            bool clear = pink || rgba[i + 3] < 128;
            if (clear) { rgba[i] = rgba[i + 1] = rgba[i + 2] = 0; rgba[i + 3] = 0; }
            else rgba[i + 3] = 255;
        }
        return rgba;
    }

    /// <summary>The pixels <see cref="Decode"/> returns, written as a PNG: eight-bit red, green, blue and alpha, no
    /// filtering, compressed.</summary>
    public static byte[] EncodePng(byte[] rgba, int width = Side, int height = Side)
    {
        var raw = new byte[height * (width * 4 + 1)];
        for (int y = 0; y < height; y++)
        {
            raw[y * (width * 4 + 1)] = 0;
            Buffer.BlockCopy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
        }
        var out_ = new MemoryStream();
        out_.Write(PngSignature, 0, 8);
        var ihdr = new byte[13];
        BigEndian(ihdr, 0, width);
        BigEndian(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        Chunk(out_, "IHDR", ihdr);
        Chunk(out_, "IDAT", Zlib(raw));
        Chunk(out_, "IEND", Array.Empty<byte>());
        return out_.ToArray();
    }

    /// <summary>The emblem's name on the wire: the first 16 hex digits of the stored PNG's SHA-256, so two people with
    /// the same picture share one download and one texture.</summary>
    public static string Id(byte[] png)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            var hash = sha.ComputeHash(png);
            var text = new System.Text.StringBuilder(16);
            for (int i = 0; i < 8; i++) text.Append(hash[i].ToString("x2"));
            return text.ToString();
        }
    }

    // ---- BMP --------------------------------------------------------------------------------------------------

    static byte[] ReadBmp(byte[] f, out int width, out int height, out string refusal)
    {
        refusal = null;
        int dataAt = Le32(f, 10);
        int header = Le32(f, 14);
        width = Le32(f, 18);
        int rawHeight = Le32(f, 22);
        height = Math.Abs(rawHeight);
        bool topDown = rawHeight < 0;
        int bits = Le16(f, 28);
        int compression = header >= 40 ? Le32(f, 30) : 0;
        // 0 = uncompressed; 3 = bit fields, which for 32-bit files Windows writes as plain blue-green-red-alpha.
        if (compression != 0 && !(compression == 3 && bits == 32)) { refusal = "BMP must be uncompressed"; return null; }
        if (bits != 8 && bits != 24 && bits != 32) { refusal = "BMP must be 8, 24 or 32 bits per pixel"; return null; }
        if (width <= 0 || width > 256 || height <= 0 || height > 256) { refusal = "Wrong size: must be " + Side + " x " + Side + " pixels"; return null; }
        int palette = 14 + header;
        int stride = (width * bits / 8 + 3) & ~3;
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int row = topDown ? y : height - 1 - y;
            int at = dataAt + row * stride;
            for (int x = 0; x < width; x++)
            {
                int o = (y * width + x) * 4;
                if (bits == 8)
                {
                    int p = palette + f[at + x] * 4;
                    rgba[o] = f[p + 2]; rgba[o + 1] = f[p + 1]; rgba[o + 2] = f[p]; rgba[o + 3] = 255;
                }
                else
                {
                    int p = at + x * (bits / 8);
                    rgba[o] = f[p + 2]; rgba[o + 1] = f[p + 1]; rgba[o + 2] = f[p];
                    // A 32-bit BMP's fourth byte is often left at zero by painting programs; only a file that uses it
                    // somewhere is read as see-through.
                    rgba[o + 3] = bits == 32 ? f[p + 3] : (byte)255;
                }
            }
        }
        if (bits == 32)
        {
            bool anyAlpha = false;
            for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
        }
        return rgba;
    }

    // ---- PNG --------------------------------------------------------------------------------------------------

    static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    static bool IsPng(byte[] f)
    {
        if (f.Length < 8) return false;
        for (int i = 0; i < 8; i++) if (f[i] != PngSignature[i]) return false;
        return true;
    }

    static byte[] ReadPng(byte[] f, out int width, out int height, out string refusal)
    {
        refusal = null;
        width = height = 0;
        int depth = 0, colour = 0, interlace = 0;
        byte[] plte = null, trns = null;
        var idat = new MemoryStream();
        int at = 8;
        while (at + 8 <= f.Length)
        {
            int length = Be32(f, at);
            string type = System.Text.Encoding.ASCII.GetString(f, at + 4, 4);
            int data = at + 8;
            if (length < 0 || data + length > f.Length) { refusal = "The picture is damaged"; return null; }
            if (type == "IHDR")
            {
                width = Be32(f, data);
                height = Be32(f, data + 4);
                depth = f[data + 8];
                colour = f[data + 9];
                interlace = f[data + 12];
            }
            else if (type == "PLTE") { plte = new byte[length]; Buffer.BlockCopy(f, data, plte, 0, length); }
            else if (type == "tRNS") { trns = new byte[length]; Buffer.BlockCopy(f, data, trns, 0, length); }
            else if (type == "IDAT") idat.Write(f, data, length);
            else if (type == "IEND") break;
            at = data + length + 4;
        }
        if (width <= 0 || width > 256 || height <= 0 || height > 256) { refusal = "Wrong size: must be " + Side + " x " + Side + " pixels"; return null; }
        if (interlace != 0) { refusal = "PNG must not be interlaced"; return null; }
        int channels = colour == 0 ? 1 : colour == 2 ? 3 : colour == 3 ? 1 : colour == 4 ? 2 : colour == 6 ? 4 : 0;
        if (channels == 0) { refusal = "Unknown PNG colour type"; return null; }
        if (depth != 8 && !(colour == 3 && (depth == 1 || depth == 2 || depth == 4))) { refusal = "PNG must be 8 bits per channel"; return null; }

        int bitsPerPixel = channels * depth;
        int stride = (width * bitsPerPixel + 7) / 8;
        int bpp = Math.Max(1, bitsPerPixel / 8);
        var raw = Inflate(idat.ToArray());
        if (raw.Length < height * (stride + 1)) { refusal = "The picture is damaged"; return null; }
        var line = new byte[stride];
        var above = new byte[stride];
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int start = y * (stride + 1);
            int filter = raw[start];
            for (int i = 0; i < stride; i++)
            {
                int x = raw[start + 1 + i];
                int a = i >= bpp ? line[i - bpp] : 0, b = above[i], c = i >= bpp ? above[i - bpp] : 0;
                switch (filter)
                {
                    case 1: x += a; break;
                    case 2: x += b; break;
                    case 3: x += (a + b) >> 1; break;
                    case 4: x += Paeth(a, b, c); break;
                }
                line[i] = (byte)x;
            }
            for (int px = 0; px < width; px++)
            {
                int o = (y * width + px) * 4;
                switch (colour)
                {
                    case 0:
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = line[px];
                        rgba[o + 3] = 255;
                        break;
                    case 2:
                        rgba[o] = line[px * 3]; rgba[o + 1] = line[px * 3 + 1]; rgba[o + 2] = line[px * 3 + 2];
                        rgba[o + 3] = 255;
                        break;
                    case 3:
                        int index = depth == 8 ? line[px] : (line[px * depth / 8] >> (8 - depth - (px * depth) % 8)) & ((1 << depth) - 1);
                        if (plte == null || index * 3 + 2 >= plte.Length) { refusal = "The picture is damaged"; return null; }
                        rgba[o] = plte[index * 3]; rgba[o + 1] = plte[index * 3 + 1]; rgba[o + 2] = plte[index * 3 + 2];
                        rgba[o + 3] = trns != null && index < trns.Length ? trns[index] : (byte)255;
                        break;
                    case 4:
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = line[px * 2];
                        rgba[o + 3] = line[px * 2 + 1];
                        break;
                    case 6:
                        Buffer.BlockCopy(line, px * 4, rgba, o, 4);
                        break;
                }
            }
            var swap = above;
            above = line;
            line = swap;
        }
        return rgba;
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    // A PNG's picture data is zlib: two header bytes, raw deflate, then a four-byte checksum.
    static byte[] Inflate(byte[] zlib)
    {
        using (var input = new MemoryStream(zlib, 2, zlib.Length - 2))
        using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            var buffer = new byte[4096];
            int n;
            while ((n = deflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, n);
                if (output.Length > 1 << 20) throw new InvalidDataException("too large");
            }
            return output.ToArray();
        }
    }

    static byte[] Zlib(byte[] data)
    {
        var out_ = new MemoryStream();
        out_.WriteByte(0x78);
        out_.WriteByte(0x9C);
        using (var deflate = new DeflateStream(out_, CompressionLevel.Optimal, true)) deflate.Write(data, 0, data.Length);
        uint a = 1, b = 0;
        foreach (byte d in data) { a = (a + d) % 65521; b = (b + a) % 65521; }
        uint adler = (b << 16) | a;
        out_.WriteByte((byte)(adler >> 24)); out_.WriteByte((byte)(adler >> 16)); out_.WriteByte((byte)(adler >> 8)); out_.WriteByte((byte)adler);
        return out_.ToArray();
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var head = new byte[8];
        BigEndian(head, 0, data.Length);
        for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
        s.Write(head, 0, 8);
        s.Write(data, 0, data.Length);
        uint crc = Crc(head, 4, 4, 0xFFFFFFFFu);
        crc = Crc(data, 0, data.Length, crc) ^ 0xFFFFFFFFu;
        var tail = new byte[4];
        BigEndian(tail, 0, (int)crc);
        s.Write(tail, 0, 4);
    }

    static uint[] crcTable;

    static uint Crc(byte[] data, int start, int count, uint crc)
    {
        if (crcTable == null)
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            crcTable = table;
        }
        for (int i = start; i < start + count; i++) crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    static int Le16(byte[] f, int at) => f[at] | (f[at + 1] << 8);
    static int Le32(byte[] f, int at) => f[at] | (f[at + 1] << 8) | (f[at + 2] << 16) | (f[at + 3] << 24);
    static int Be32(byte[] f, int at) => (f[at] << 24) | (f[at + 1] << 16) | (f[at + 2] << 8) | f[at + 3];

    static void BigEndian(byte[] b, int at, int v)
    {
        b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
    }
}
