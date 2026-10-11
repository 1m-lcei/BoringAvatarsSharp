// BoringAvatarsSharp v0.1.0 — SVG styles adapted from https://github.com/boringdesigners/boring-avatars
// MIT License
// Copyright (c) 2026 1m-lcei
// Copyright (c) 2021 boringdesigners
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

#nullable enable

using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace BoringAvatarsSharp;

/// <summary>The available SVG avatar styles.</summary>
public enum AvatarVariant
{
    /// <summary>Soft overlapping shapes.</summary>
    Marble,
    /// <summary>A geometric face.</summary>
    Beam,
    /// <summary>A grid of colored cells.</summary>
    Pixel,
    /// <summary>Two vertical gradients.</summary>
    Sunset,
    /// <summary>Concentric colored arcs.</summary>
    Ring,
    /// <summary>Layered geometric shapes.</summary>
    Bauhaus
}

/// <summary>Generates self-contained SVG avatars from names.</summary>
public static class AvatarGenerator
{
    /// <summary>Returns a complete SVG avatar as a string from a UTF-8 encoded name.</summary>
    /// <param name="name">The UTF-8 encoded name used to choose the drawing. An empty span is valid.</param>
    /// <param name="variant">The avatar style.</param>
    /// <param name="size">The positive width and height in pixels.</param>
    /// <param name="square">Whether to use a square instead of a circular outline.</param>
    /// <param name="includeTitle">Whether to include the name in an SVG title element.</param>
    /// <param name="colors">Optional RGB colors in 0xRRGGBB form. An empty span uses the default palette.</param>
    /// <returns>A self-contained SVG document.</returns>
    /// <remarks>Shares generation and validation with <see cref="WriteSvg(IBufferWriter{byte}, ReadOnlySpan{byte}, AvatarVariant, int, bool, bool, ReadOnlySpan{uint})"/>, including its fixed element IDs. A temporary output buffer is pooled and returned even if generation fails.</remarks>
    /// <exception cref="ArgumentException">The name contains invalid UTF-8 or an invalid XML character.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size is not positive, the variant is unknown, or a color is outside the RGB range.</exception>
    public static string GetSvg(
        ReadOnlySpan<byte> name,
        AvatarVariant variant = AvatarVariant.Marble,
        int size = 40,
        bool square = false,
        bool includeTitle = false,
        ReadOnlySpan<uint> colors = default)
    {
        ValidateOptions(variant, size, colors);
        ulong seed = ValidateAndHashName(name, out long escapedLength);
        int capacity = checked((int)GetMaxSvgLength(variant, includeTitle, escapedLength));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);
        try
        {
            var output = new SvgWriter(buffer);
            WriteSvgCore(ref output, name, seed, variant, size, square, includeTitle, colors);
            return Encoding.UTF8.GetString(buffer.AsSpan(0, output.WrittenCount));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Returns a complete SVG avatar as a string from a UTF-16 encoded name.</summary>
    /// <param name="name">The UTF-16 encoded name used to choose the drawing. An empty span is valid.</param>
    /// <param name="variant">The avatar style.</param>
    /// <param name="size">The positive width and height in pixels.</param>
    /// <param name="square">Whether to use a square instead of a circular outline.</param>
    /// <param name="includeTitle">Whether to include the name in an SVG title element.</param>
    /// <param name="colors">Optional RGB colors in 0xRRGGBB form. An empty span uses the default palette.</param>
    /// <returns>A self-contained SVG document.</returns>
    /// <remarks>Shares generation and validation with <see cref="WriteSvg(IBufferWriter{byte}, ReadOnlySpan{char}, AvatarVariant, int, bool, bool, ReadOnlySpan{uint})"/>, including its fixed element IDs. Equivalent UTF-8 and UTF-16 names with the same options produce identical strings. Temporary buffers are pooled and returned even if generation fails.</remarks>
    /// <exception cref="ArgumentException">The name contains invalid UTF-16 or an invalid XML character.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size is not positive, the variant is unknown, or a color is outside the RGB range.</exception>
    public static string GetSvg(
        ReadOnlySpan<char> name,
        AvatarVariant variant = AvatarVariant.Marble,
        int size = 40,
        bool square = false,
        bool includeTitle = false,
        ReadOnlySpan<uint> colors = default)
    {
        byte[]? rented = null;
        try
        {
            ReadOnlySpan<byte> utf8Name = EncodeName(name, stackalloc byte[512], out rented);
            return GetSvg(utf8Name, variant, size, square, includeTitle, colors);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>Writes a complete SVG avatar as UTF-8 without a byte order mark.</summary>
    /// <param name="destination">The buffer writer to append to. The caller owns flushing and completion.</param>
    /// <param name="name">The UTF-8 encoded name used to choose the drawing. An empty span is valid.</param>
    /// <param name="variant">The avatar style.</param>
    /// <param name="size">The positive width and height in pixels.</param>
    /// <param name="square">Whether to use a square instead of a circular outline.</param>
    /// <param name="includeTitle">Whether to include the name in an SVG title element.</param>
    /// <param name="colors">Optional RGB colors in 0xRRGGBB form. An empty span uses the default palette.</param>
    /// <remarks>Arguments are validated before writing. A destination failure may leave partial output. The same inputs produce identical SVG bytes. Fixed element IDs assume standalone SVG documents; callers must resolve ID conflicts when combining documents inline. Unicode normalization is the caller's responsibility.</remarks>
    public static void WriteSvg(
        IBufferWriter<byte> destination,
        ReadOnlySpan<byte> name,
        AvatarVariant variant = AvatarVariant.Marble,
        int size = 40,
        bool square = false,
        bool includeTitle = false,
        ReadOnlySpan<uint> colors = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateOptions(variant, size, colors);
        ulong seed = ValidateAndHashName(name, out long escapedLength);
        long capacity = GetMaxSvgLength(variant, includeTitle, escapedLength);
        // Bound contiguous requests for exceptionally long names while batching ordinary SVGs.
        var output = new SvgWriter(destination.GetSpan((int)Math.Min(capacity, 65536)), destination);
        WriteSvgCore(ref output, name, seed, variant, size, square, includeTitle, colors);
        destination.Advance(output.WrittenCount);
    }

    private static void ValidateOptions(AvatarVariant variant, int size, ReadOnlySpan<uint> colors)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if (!Enum.IsDefined(variant))
        {
            throw new ArgumentOutOfRangeException(nameof(variant));
        }

        foreach (uint color in colors)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(color, 0xFFFFFFu, nameof(colors));
        }
    }

    private static long GetMaxSvgLength(AvatarVariant variant, bool includeTitle, long escapedLength)
    {
        // The frame bound includes 10-digit sizes and round clipping; recheck when SVG output changes.
        int drawingLength = variant switch
        {
            AvatarVariant.Marble => 848, // Frame 236 + drawing 604 = 840 bytes.
            AvatarVariant.Beam => 672, // Frame 236 + drawing 423 = 659 bytes.
            AvatarVariant.Pixel => 4000, // Frame 236 + drawing 3760 = 3996 bytes.
            AvatarVariant.Sunset => 680, // Frame 236 + drawing 437 = 673 bytes.
            AvatarVariant.Ring => 688, // Frame 236 + drawing 446 = 682 bytes.
            _ => 600 // Frame 236 + drawing 352 = 588 bytes (Bauhaus).
        };
        return drawingLength + escapedLength * (includeTitle ? 2 : 1) + (includeTitle ? 15 : 0);
    }

    private static void WriteSvgCore(
        ref SvgWriter destination, ReadOnlySpan<byte> name, ulong seed, AvatarVariant variant,
        int size, bool square, bool includeTitle, ReadOnlySpan<uint> colors)
    {
        if (colors.IsEmpty)
        {
            colors = [0x92A1C6, 0x146A7C, 0xF0AB3D, 0xC271B4, 0xC20D90];
        }

        int viewSize = variant switch
        {
            AvatarVariant.Beam => 36,
            AvatarVariant.Ring => 90,
            _ => 80
        };

        destination.Write("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 "u8);
        WriteNumber(ref destination, viewSize);
        destination.Write(" "u8);
        WriteNumber(ref destination, viewSize);
        destination.Write("\" width=\""u8);
        WriteNumber(ref destination, size);
        destination.Write("\" height=\""u8);
        WriteNumber(ref destination, size);
        destination.Write("\" role=\"img\" aria-label=\""u8);
        WriteEscaped(ref destination, name);
        destination.Write("\">"u8);
        if (includeTitle)
        {
            destination.Write("<title>"u8);
            WriteEscaped(ref destination, name);
            destination.Write("</title>"u8);
        }

        destination.Write("<defs><clipPath id=\"c\"><rect width=\""u8);
        WriteNumber(ref destination, viewSize);
        destination.Write("\" height=\""u8);
        WriteNumber(ref destination, viewSize);
        if (!square)
        {
            destination.Write("\" rx=\""u8);
            WriteNumber(ref destination, viewSize / 2);
        }
        destination.Write("\"/></clipPath></defs><g clip-path=\"url(#c)\">"u8);

        switch (variant)
        {
            case AvatarVariant.Marble: Marble(ref destination, seed, colors); break;
            case AvatarVariant.Beam: Beam(ref destination, seed, colors); break;
            case AvatarVariant.Pixel: Pixel(ref destination, seed, colors); break;
            case AvatarVariant.Sunset: Sunset(ref destination, seed, colors); break;
            case AvatarVariant.Ring: Ring(ref destination, seed, colors); break;
            case AvatarVariant.Bauhaus: Bauhaus(ref destination, seed, colors); break;
        }

        destination.Write("</g></svg>"u8);
    }

    /// <summary>Writes a complete SVG avatar as UTF-8, converting the name from UTF-16.</summary>
    /// <param name="destination">The buffer writer to append to. The caller owns flushing and completion.</param>
    /// <param name="name">The UTF-16 encoded name used to choose the drawing. An empty span is valid.</param>
    /// <param name="variant">The avatar style.</param>
    /// <param name="size">The positive width and height in pixels.</param>
    /// <param name="square">Whether to use a square instead of a circular outline.</param>
    /// <param name="includeTitle">Whether to include the name in an SVG title element.</param>
    /// <param name="colors">Optional RGB colors in 0xRRGGBB form. An empty span uses the default palette.</param>
    /// <remarks>Uses the UTF-8 overload after conversion, including its fixed element IDs. Invalid UTF-16 is rejected without writing. Equivalent UTF-8 and UTF-16 names with the same options produce identical SVG bytes. Unicode normalization is the caller's responsibility.</remarks>
    public static void WriteSvg(
        IBufferWriter<byte> destination,
        ReadOnlySpan<char> name,
        AvatarVariant variant = AvatarVariant.Marble,
        int size = 40,
        bool square = false,
        bool includeTitle = false,
        ReadOnlySpan<uint> colors = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        byte[]? rented = null;
        try
        {
            ReadOnlySpan<byte> utf8Name = EncodeName(name, stackalloc byte[512], out rented);
            WriteSvg(destination, utf8Name, variant, size, square, includeTitle, colors);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static ReadOnlySpan<byte> EncodeName(ReadOnlySpan<char> name, Span<byte> scratch, out byte[]? rented)
    {
        int byteCount = Encoding.UTF8.GetByteCount(name);
        rented = byteCount > scratch.Length ? ArrayPool<byte>.Shared.Rent(byteCount) : null;
        Span<byte> utf8Name = rented is null ? scratch : rented;
        if (Utf8.FromUtf16(name, utf8Name, out _, out int written, replaceInvalidSequences: false) != OperationStatus.Done)
        {
            throw new ArgumentException("Name contains invalid UTF-16.", nameof(name));
        }
        return utf8Name[..written];
    }

    private static ulong ValidateAndHashName(ReadOnlySpan<byte> name, out long escapedLength)
    {
        // Validate during hashing so invalid input never reaches the destination.
        ulong hash = 14695981039346656037UL;
        escapedLength = name.Length;
        while (!name.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(name, out Rune rune, out int consumed) != OperationStatus.Done)
            {
                throw new ArgumentException("Name contains invalid UTF-8.", nameof(name));
            }

            int value = rune.Value;
            if (value < 0x20 && value is not ('\t' or '\n' or '\r') || value is 0xFFFE or 0xFFFF)
            {
                throw new ArgumentException("Name contains an invalid XML character.", nameof(name));
            }

            escapedLength += value switch
            {
                '&' or '\t' or '\n' or '\r' => 4,
                '<' or '>' => 3,
                '"' or '\'' => 5,
                _ => 0
            };

            foreach (byte b in name[..consumed])
            {
                hash = unchecked((hash ^ b) * 1099511628211UL);
            }

            name = name[consumed..];
        }
        return hash;
    }

    private ref struct SvgWriter(Span<byte> buffer, IBufferWriter<byte>? destination = null)
    {
        private Span<byte> _remaining = buffer;
        private readonly IBufferWriter<byte>? _destination = destination;
        public int WrittenCount { get; private set; }
        public readonly Span<byte> FreeSpan => _remaining;

        public void Write(ReadOnlySpan<byte> value)
        {
            if (value.Length > _remaining.Length)
            {
                WriteChunked(value);
                return;
            }
            value.CopyTo(_remaining);
            Advance(value.Length);
        }

        // Keep chunking out of the hot path so writes of constant SVG fragments can inline.
        private void WriteChunked(ReadOnlySpan<byte> value)
        {
            while (value.Length > _remaining.Length)
            {
                int length = _remaining.Length;
                value[..length].CopyTo(_remaining);
                Advance(length);
                value = value[length..];
                GetNextSpan(4096);
            }
            value.CopyTo(_remaining);
            Advance(value.Length);
        }

        public void Advance(int count)
        {
            _remaining = _remaining[count..];
            WrittenCount += count;
        }

        public void GetNextSpan(int sizeHint)
        {
            if (_destination is null)
            {
                throw new InvalidOperationException("SVG exceeded its estimated capacity.");
            }
            _destination.Advance(WrittenCount);
            _remaining = _destination.GetSpan(Math.Max(sizeHint, 4096));
            WrittenCount = 0;
        }
    }

    private static int Next(ref ulong state, int exclusiveMax)
    {
        // SplitMix64 avoids tying drawing parameters to decimal digits in the seed.
        // Algorithm: https://prng.di.unimi.it/splitmix64.c (public domain).
        unchecked
        {
            ulong value = state += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return (int)(((value >> 32) * (uint)exclusiveMax) >> 32);
        }
    }

    private static uint NextColor(ref ulong state, ReadOnlySpan<uint> colors) =>
        colors[Next(ref state, colors.Length)];

    private static void WriteColor(ref SvgWriter destination, uint color)
    {
        destination.Write("#"u8);
        WriteNumber(ref destination, color, "X6");
    }

    private static void WriteNumber<T>(ref SvgWriter destination, T value, ReadOnlySpan<char> format = default)
        where T : IUtf8SpanFormattable
    {
        int sizeHint = 32;
        int written;
        while (!value.TryFormat(destination.FreeSpan, out written, format, CultureInfo.InvariantCulture))
        {
            destination.GetNextSpan(sizeHint);
            sizeHint = checked(sizeHint * 2);
        }

        destination.Advance(written);
    }

    private static void WriteEscaped(ref SvgWriter destination, ReadOnlySpan<byte> name)
    {
        int start = 0;
        for (int i = 0; i < name.Length; i++)
        {
            ReadOnlySpan<byte> escape = name[i] switch
            {
                (byte)'&' => "&amp;"u8,
                (byte)'<' => "&lt;"u8,
                (byte)'>' => "&gt;"u8,
                (byte)'"' => "&quot;"u8,
                (byte)'\'' => "&apos;"u8,
                (byte)'\t' => "&#x9;"u8,
                (byte)'\n' => "&#xA;"u8,
                (byte)'\r' => "&#xD;"u8,
                _ => default
            };
            if (escape.IsEmpty)
            {
                continue;
            }

            destination.Write(name[start..i]);
            destination.Write(escape);
            start = i + 1;
        }
        destination.Write(name[start..]);
    }

    private static void Marble(ref SvgWriter destination, ulong state, ReadOnlySpan<uint> colors)
    {
        destination.Write("<rect width=\"80\" height=\"80\" fill=\""u8);
        WriteColor(ref destination, NextColor(ref state, colors));
        destination.Write("\"/>"u8);
        for (int i = 0; i < 2; i++)
        {
            destination.Write("<path d=\""u8);
            destination.Write(i == 0
                ? "M32.414 59.35L50.376 70.5H72.5v-71H33.728L26.5 13.381l19.057 27.08L32.414 59.35z"u8
                : "M22.216 24L0 46.75l14.108 38.129L78 86l-3.081-59.276-22.378 4.005 12.972 20.186-23.35 27.395L22.215 24z"u8);
            destination.Write("\" fill=\""u8);
            WriteColor(ref destination, NextColor(ref state, colors));
            destination.Write("\" filter=\"url(#f)\""u8);
            if (i == 1)
            {
                destination.Write(" style=\"mix-blend-mode:overlay\""u8);
            }

            destination.Write(" transform=\"translate("u8);
            WriteNumber(ref destination, Next(ref state, 15) - 7);
            destination.Write(" "u8);
            WriteNumber(ref destination, Next(ref state, 15) - 7);
            destination.Write(") rotate("u8);
            WriteNumber(ref destination, Next(ref state, 360));
            destination.Write(" 40 40) scale("u8);
            destination.Write(Next(ref state, 4) switch
            {
                0 => "1.2"u8,
                1 => "1.3"u8,
                2 => "1.4"u8,
                _ => "1.5"u8
            });
            destination.Write(")\"/>"u8);
        }
        destination.Write("<defs><filter id=\"f\" x=\"-40\" y=\"-40\" width=\"160\" height=\"160\" filterUnits=\"userSpaceOnUse\"><feGaussianBlur stdDeviation=\"7\"/></filter></defs>"u8);
    }

    private static void Beam(ref SvgWriter destination, ulong state, ReadOnlySpan<uint> colors)
    {
        uint wrapper = NextColor(ref state, colors);
        int x = Next(ref state, 13) - 4;
        int y = Next(ref state, 13) - 4;
        uint face = Contrast(wrapper);
        destination.Write("<rect width=\"36\" height=\"36\" fill=\""u8);
        WriteColor(ref destination, NextColor(ref state, colors));
        destination.Write("\"/><rect width=\"36\" height=\"36\" fill=\""u8);
        WriteColor(ref destination, wrapper);
        destination.Write("\" rx=\""u8);
        WriteNumber(ref destination, Next(ref state, 2) == 0 ? 36 : 6);
        destination.Write("\" transform=\"translate("u8);
        WriteNumber(ref destination, x);
        destination.Write(" "u8);
        WriteNumber(ref destination, y);
        destination.Write(") rotate("u8);
        WriteNumber(ref destination, Next(ref state, 360));
        destination.Write(" 18 18) scale("u8);
        destination.Write(Next(ref state, 3) switch
        {
            0 => "1"u8,
            1 => "1.1"u8,
            _ => "1.2"u8
        });
        destination.Write(")\"/><g transform=\"translate("u8);
        WriteNumber(ref destination, x / 2);
        destination.Write(" "u8);
        WriteNumber(ref destination, y / 2);
        destination.Write(") rotate("u8);
        WriteNumber(ref destination, Next(ref state, 19) - 9);
        destination.Write(" 18 18)\">"u8);
        if (Next(ref state, 2) == 0)
        {
            destination.Write("<path d=\"M15 "u8);
            WriteNumber(ref destination, 19 + Next(ref state, 3));
            destination.Write("c2 1 4 1 6 0\" fill=\"none\" stroke=\""u8);
            WriteColor(ref destination, face);
            destination.Write("\" stroke-linecap=\"round\"/>"u8);
        }
        else
        {
            destination.Write("<path d=\"M13,"u8);
            WriteNumber(ref destination, 19 + Next(ref state, 3));
            destination.Write(" a1,0.75 0 0,0 10,0\" fill=\""u8);
            WriteColor(ref destination, face);
            destination.Write("\"/>"u8);
        }
        int spread = Next(ref state, 5);
        destination.Write("<rect x=\""u8);
        WriteNumber(ref destination, 14 - spread);
        destination.Write("\" y=\"14\" width=\"1.5\" height=\"2\" rx=\"1\" fill=\""u8);
        WriteColor(ref destination, face);
        destination.Write("\"/><rect x=\""u8);
        WriteNumber(ref destination, 20 + spread);
        destination.Write("\" y=\"14\" width=\"1.5\" height=\"2\" rx=\"1\" fill=\""u8);
        WriteColor(ref destination, face);
        destination.Write("\"/></g>"u8);
    }

    private static uint Contrast(uint color)
    {
        uint r = color >> 16;
        uint g = (color >> 8) & 0xFF;
        uint b = color & 0xFF;
        return r * 299 + g * 587 + b * 114 >= 128000 ? 0x000000u : 0xFFFFFFu;
    }

    private static void Pixel(ref SvgWriter destination, ulong state, ReadOnlySpan<uint> colors)
    {
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                destination.Write("<rect x=\""u8);
                WriteNumber(ref destination, x * 10);
                destination.Write("\" y=\""u8);
                WriteNumber(ref destination, y * 10);
                destination.Write("\" width=\"10\" height=\"10\" fill=\""u8);
                WriteColor(ref destination, NextColor(ref state, colors));
                destination.Write("\"/>"u8);
            }
        }
    }

    private static void Sunset(ref SvgWriter destination, ulong state, ReadOnlySpan<uint> colors)
    {
        destination.Write("<path d=\"M0 0h80v40H0z\" fill=\"url(#g0)\"/><path d=\"M0 40h80v40H0z\" fill=\"url(#g1)\"/><defs>"u8);
        for (int i = 0; i < 2; i++)
        {
            destination.Write("<linearGradient id=\"g"u8);
            WriteNumber(ref destination, i);
            destination.Write("\" x1=\"40\" y1=\""u8);
            WriteNumber(ref destination, i * 40);
            destination.Write("\" x2=\"40\" y2=\""u8);
            WriteNumber(ref destination, (i + 1) * 40);
            destination.Write("\" gradientUnits=\"userSpaceOnUse\"><stop stop-color=\""u8);
            WriteColor(ref destination, NextColor(ref state, colors));
            destination.Write("\"/><stop offset=\"1\" stop-color=\""u8);
            WriteColor(ref destination, NextColor(ref state, colors));
            destination.Write("\"/></linearGradient>"u8);
        }
        destination.Write("</defs>"u8);
    }

    private static void Ring(ref SvgWriter destination, ulong state, ReadOnlySpan<uint> colors)
    {
        uint c0 = NextColor(ref state, colors);
        uint c1 = NextColor(ref state, colors);
        uint c2 = NextColor(ref state, colors);
        uint c3 = NextColor(ref state, colors);
        destination.Write("<path d=\"M0 0h90v45H0z\" fill=\""u8);
        WriteColor(ref destination, c0);
        destination.Write("\"/><path d=\"M0 45h90v45H0z\" fill=\""u8);
        WriteColor(ref destination, c1);
        destination.Write("\"/><path d=\"M83 45a38 38 0 00-76 0h76z\" fill=\""u8);
        WriteColor(ref destination, c1);
        destination.Write("\"/><path d=\"M83 45a38 38 0 01-76 0h76z\" fill=\""u8);
        WriteColor(ref destination, c2);
        destination.Write("\"/><path d=\"M77 45a32 32 0 10-64 0h64z\" fill=\""u8);
        WriteColor(ref destination, c2);
        destination.Write("\"/><path d=\"M77 45a32 32 0 11-64 0h64z\" fill=\""u8);
        WriteColor(ref destination, c3);
        destination.Write("\"/><path d=\"M71 45a26 26 0 00-52 0h52z\" fill=\""u8);
        WriteColor(ref destination, c3);
        destination.Write("\"/><path d=\"M71 45a26 26 0 01-52 0h52z\" fill=\""u8);
        WriteColor(ref destination, c0);
        destination.Write("\"/><circle cx=\"45\" cy=\"45\" r=\"23\" fill=\""u8);
        WriteColor(ref destination, NextColor(ref state, colors));
        destination.Write("\"/>"u8);
    }

    private static void Bauhaus(ref SvgWriter destination, ulong state, ReadOnlySpan<uint> colors)
    {
        destination.Write("<rect width=\"80\" height=\"80\" fill=\""u8);
        WriteColor(ref destination, NextColor(ref state, colors));
        destination.Write("\"/><rect x=\"10\" y=\"30\" width=\"80\" height=\""u8);
        WriteNumber(ref destination, Next(ref state, 2) == 0 ? 80 : 10);
        destination.Write("\" fill=\""u8);
        WriteColor(ref destination, NextColor(ref state, colors));
        destination.Write("\" transform=\"translate("u8);
        WriteNumber(ref destination, Next(ref state, 43) - 21);
        destination.Write(" "u8);
        WriteNumber(ref destination, Next(ref state, 43) - 21);
        destination.Write(") rotate("u8);
        WriteNumber(ref destination, Next(ref state, 360));
        destination.Write(" 40 40)\"/><circle cx=\"40\" cy=\"40\" r=\"16\" fill=\""u8);
        WriteColor(ref destination, NextColor(ref state, colors));
        destination.Write("\" transform=\"translate("u8);
        WriteNumber(ref destination, Next(ref state, 41) - 20);
        destination.Write(" "u8);
        WriteNumber(ref destination, Next(ref state, 41) - 20);
        destination.Write(")\"/><line x1=\"0\" y1=\"40\" x2=\"80\" y2=\"40\" stroke-width=\"2\" stroke=\""u8);
        WriteColor(ref destination, NextColor(ref state, colors));
        destination.Write("\" transform=\"translate("u8);
        WriteNumber(ref destination, Next(ref state, 39) - 19);
        destination.Write(" "u8);
        WriteNumber(ref destination, Next(ref state, 39) - 19);
        destination.Write(") rotate("u8);
        WriteNumber(ref destination, Next(ref state, 360));
        destination.Write(" 40 40)\"/>"u8);
    }
}
