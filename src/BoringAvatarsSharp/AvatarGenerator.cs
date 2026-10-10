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
    /// <remarks>Uses <see cref="WriteSvg(IBufferWriter{byte}, ReadOnlySpan{byte}, AvatarVariant, int, bool, bool, ReadOnlySpan{uint})"/> for generation and validation, including its fixed element IDs. Temporary output buffers are pooled and returned even if generation fails.</remarks>
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
        using var output = new ArrayPoolBufferWriter<byte>();
        WriteSvg(output, name, variant, size, square, includeTitle, colors);
        return Encoding.UTF8.GetString(output.WrittenSpan);
    }

    /// <summary>Returns a complete SVG avatar as a string from a UTF-16 encoded name.</summary>
    /// <param name="name">The UTF-16 encoded name used to choose the drawing. An empty span is valid.</param>
    /// <param name="variant">The avatar style.</param>
    /// <param name="size">The positive width and height in pixels.</param>
    /// <param name="square">Whether to use a square instead of a circular outline.</param>
    /// <param name="includeTitle">Whether to include the name in an SVG title element.</param>
    /// <param name="colors">Optional RGB colors in 0xRRGGBB form. An empty span uses the default palette.</param>
    /// <returns>A self-contained SVG document.</returns>
    /// <remarks>Uses <see cref="WriteSvg(IBufferWriter{byte}, ReadOnlySpan{char}, AvatarVariant, int, bool, bool, ReadOnlySpan{uint})"/> for generation and validation, including its fixed element IDs. Equivalent UTF-8 and UTF-16 names with the same options produce identical strings. Temporary output buffers are pooled and returned even if generation fails.</remarks>
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
        using var output = new ArrayPoolBufferWriter<byte>();
        WriteSvg(output, name, variant, size, square, includeTitle, colors);
        return Encoding.UTF8.GetString(output.WrittenSpan);
    }

    private sealed class ArrayPoolBufferWriter<T> : IBufferWriter<T>, IDisposable
    {
        private T[] _buffer = ArrayPool<T>.Shared.Rent(1024);
        private int _written;

        public ReadOnlySpan<T> WrittenSpan => _buffer.AsSpan(0, _written);

        public Memory<T> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<T> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_written);
        }

        public void Advance(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _buffer.Length - _written);
            _written += count;
        }

        private void EnsureCapacity(int sizeHint)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
            sizeHint = Math.Max(sizeHint, 1);
            if (sizeHint <= _buffer.Length - _written)
            {
                return;
            }

            int capacity = (int)Math.Min((long)_buffer.Length * 2, Array.MaxLength);
            T[] buffer = ArrayPool<T>.Shared.Rent(Math.Max(checked(_written + sizeHint), capacity));
            WrittenSpan.CopyTo(buffer);
            ArrayPool<T>.Shared.Return(_buffer);
            _buffer = buffer;
        }

        // shortcut: only used by GetSvg's using scope; add reuse/disposal guards if exposed elsewhere.
        public void Dispose() => ArrayPool<T>.Shared.Return(_buffer);
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if (!Enum.IsDefined(variant))
        {
            throw new ArgumentOutOfRangeException(nameof(variant));
        }

        if (colors.IsEmpty)
        {
            colors = [0x92A1C6, 0x146A7C, 0xF0AB3D, 0xC271B4, 0xC20D90];
        }
        else
        {
            foreach (uint color in colors)
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThan(color, 0xFFFFFFu, nameof(colors));
            }
        }

        ulong seed = ValidateAndHashName(name);
        int viewSize = variant switch
        {
            AvatarVariant.Beam => 36,
            AvatarVariant.Ring => 90,
            _ => 80
        };

        destination.Write("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 "u8);
        WriteNumber(destination, viewSize);
        destination.Write(" "u8);
        WriteNumber(destination, viewSize);
        destination.Write("\" width=\""u8);
        WriteNumber(destination, size);
        destination.Write("\" height=\""u8);
        WriteNumber(destination, size);
        destination.Write("\" role=\"img\" aria-label=\""u8);
        WriteEscaped(destination, name);
        destination.Write("\">"u8);
        if (includeTitle)
        {
            destination.Write("<title>"u8);
            WriteEscaped(destination, name);
            destination.Write("</title>"u8);
        }

        destination.Write("<defs><clipPath id=\"c\"><rect width=\""u8);
        WriteNumber(destination, viewSize);
        destination.Write("\" height=\""u8);
        WriteNumber(destination, viewSize);
        if (!square)
        {
            destination.Write("\" rx=\""u8);
            WriteNumber(destination, viewSize / 2);
        }
        destination.Write("\"/></clipPath></defs><g clip-path=\"url(#c)\">"u8);

        switch (variant)
        {
            case AvatarVariant.Marble: Marble(destination, seed, colors); break;
            case AvatarVariant.Beam: Beam(destination, seed, colors); break;
            case AvatarVariant.Pixel: Pixel(destination, seed, colors); break;
            case AvatarVariant.Sunset: Sunset(destination, seed, colors); break;
            case AvatarVariant.Ring: Ring(destination, seed, colors); break;
            case AvatarVariant.Bauhaus: Bauhaus(destination, seed, colors); break;
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
        int byteCount = Encoding.UTF8.GetByteCount(name);
        byte[]? rented = null;
        Span<byte> utf8Name = byteCount <= 512
            ? stackalloc byte[512]
            : rented = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            if (Utf8.FromUtf16(name, utf8Name, out _, out int written, replaceInvalidSequences: false) != OperationStatus.Done)
            {
                throw new ArgumentException("Name contains invalid UTF-16.", nameof(name));
            }

            WriteSvg(destination, utf8Name[..written], variant, size, square, includeTitle, colors);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static ulong ValidateAndHashName(ReadOnlySpan<byte> name)
    {
        // Validate during hashing so invalid input never reaches the destination.
        ulong hash = 14695981039346656037UL;
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

            foreach (byte b in name[..consumed])
            {
                hash = unchecked((hash ^ b) * 1099511628211UL);
            }

            name = name[consumed..];
        }
        return hash;
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

    private static void WriteColor(IBufferWriter<byte> destination, uint color)
    {
        destination.Write("#"u8);
        WriteNumber(destination, color, "X6");
    }

    private static void WriteNumber<T>(IBufferWriter<byte> destination, T value, ReadOnlySpan<char> format = default)
        where T : IUtf8SpanFormattable
    {
        int sizeHint = 32;
        int written;
        while (!value.TryFormat(destination.GetSpan(sizeHint), out written, format, CultureInfo.InvariantCulture))
        {
            sizeHint = checked(sizeHint * 2);
        }

        destination.Advance(written);
    }

    private static void WriteEscaped(IBufferWriter<byte> destination, ReadOnlySpan<byte> name)
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

    private static void Marble(IBufferWriter<byte> destination, ulong state, ReadOnlySpan<uint> colors)
    {
        destination.Write("<rect width=\"80\" height=\"80\" fill=\""u8);
        WriteColor(destination, NextColor(ref state, colors));
        destination.Write("\"/>"u8);
        for (int i = 0; i < 2; i++)
        {
            destination.Write("<path d=\""u8);
            destination.Write(i == 0
                ? "M32.414 59.35L50.376 70.5H72.5v-71H33.728L26.5 13.381l19.057 27.08L32.414 59.35z"u8
                : "M22.216 24L0 46.75l14.108 38.129L78 86l-3.081-59.276-22.378 4.005 12.972 20.186-23.35 27.395L22.215 24z"u8);
            destination.Write("\" fill=\""u8);
            WriteColor(destination, NextColor(ref state, colors));
            destination.Write("\" filter=\"url(#f)\""u8);
            if (i == 1)
            {
                destination.Write(" style=\"mix-blend-mode:overlay\""u8);
            }

            destination.Write(" transform=\"translate("u8);
            WriteNumber(destination, Next(ref state, 15) - 7);
            destination.Write(" "u8);
            WriteNumber(destination, Next(ref state, 15) - 7);
            destination.Write(") rotate("u8);
            WriteNumber(destination, Next(ref state, 360));
            destination.Write(" 40 40) scale("u8);
            WriteNumber(destination, 1.2 + Next(ref state, 4) / 10d, "0.###");
            destination.Write(")\"/>"u8);
        }
        destination.Write("<defs><filter id=\"f\" x=\"-40\" y=\"-40\" width=\"160\" height=\"160\" filterUnits=\"userSpaceOnUse\"><feGaussianBlur stdDeviation=\"7\"/></filter></defs>"u8);
    }

    private static void Beam(IBufferWriter<byte> destination, ulong state, ReadOnlySpan<uint> colors)
    {
        uint wrapper = NextColor(ref state, colors);
        int x = Next(ref state, 13) - 4;
        int y = Next(ref state, 13) - 4;
        uint face = Contrast(wrapper);
        destination.Write("<rect width=\"36\" height=\"36\" fill=\""u8);
        WriteColor(destination, NextColor(ref state, colors));
        destination.Write("\"/><rect width=\"36\" height=\"36\" fill=\""u8);
        WriteColor(destination, wrapper);
        destination.Write("\" rx=\""u8);
        WriteNumber(destination, Next(ref state, 2) == 0 ? 36 : 6);
        destination.Write("\" transform=\"translate("u8);
        WriteNumber(destination, x);
        destination.Write(" "u8);
        WriteNumber(destination, y);
        destination.Write(") rotate("u8);
        WriteNumber(destination, Next(ref state, 360));
        destination.Write(" 18 18) scale("u8);
        WriteNumber(destination, 1 + Next(ref state, 3) / 10d, "0.###");
        destination.Write(")\"/><g transform=\"translate("u8);
        WriteNumber(destination, x / 2);
        destination.Write(" "u8);
        WriteNumber(destination, y / 2);
        destination.Write(") rotate("u8);
        WriteNumber(destination, Next(ref state, 19) - 9);
        destination.Write(" 18 18)\">"u8);
        if (Next(ref state, 2) == 0)
        {
            destination.Write("<path d=\"M15 "u8);
            WriteNumber(destination, 19 + Next(ref state, 3));
            destination.Write("c2 1 4 1 6 0\" fill=\"none\" stroke=\""u8);
            WriteColor(destination, face);
            destination.Write("\" stroke-linecap=\"round\"/>"u8);
        }
        else
        {
            destination.Write("<path d=\"M13,"u8);
            WriteNumber(destination, 19 + Next(ref state, 3));
            destination.Write(" a1,0.75 0 0,0 10,0\" fill=\""u8);
            WriteColor(destination, face);
            destination.Write("\"/>"u8);
        }
        int spread = Next(ref state, 5);
        destination.Write("<rect x=\""u8);
        WriteNumber(destination, 14 - spread);
        destination.Write("\" y=\"14\" width=\"1.5\" height=\"2\" rx=\"1\" fill=\""u8);
        WriteColor(destination, face);
        destination.Write("\"/><rect x=\""u8);
        WriteNumber(destination, 20 + spread);
        destination.Write("\" y=\"14\" width=\"1.5\" height=\"2\" rx=\"1\" fill=\""u8);
        WriteColor(destination, face);
        destination.Write("\"/></g>"u8);
    }

    private static uint Contrast(uint color)
    {
        uint r = color >> 16;
        uint g = (color >> 8) & 0xFF;
        uint b = color & 0xFF;
        return r * 299 + g * 587 + b * 114 >= 128000 ? 0x000000u : 0xFFFFFFu;
    }

    private static void Pixel(IBufferWriter<byte> destination, ulong state, ReadOnlySpan<uint> colors)
    {
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                destination.Write("<rect x=\""u8);
                WriteNumber(destination, x * 10);
                destination.Write("\" y=\""u8);
                WriteNumber(destination, y * 10);
                destination.Write("\" width=\"10\" height=\"10\" fill=\""u8);
                WriteColor(destination, NextColor(ref state, colors));
                destination.Write("\"/>"u8);
            }
        }
    }

    private static void Sunset(IBufferWriter<byte> destination, ulong state, ReadOnlySpan<uint> colors)
    {
        destination.Write("<path d=\"M0 0h80v40H0z\" fill=\"url(#g0)\"/><path d=\"M0 40h80v40H0z\" fill=\"url(#g1)\"/><defs>"u8);
        for (int i = 0; i < 2; i++)
        {
            destination.Write("<linearGradient id=\"g"u8);
            WriteNumber(destination, i);
            destination.Write("\" x1=\"40\" y1=\""u8);
            WriteNumber(destination, i * 40);
            destination.Write("\" x2=\"40\" y2=\""u8);
            WriteNumber(destination, (i + 1) * 40);
            destination.Write("\" gradientUnits=\"userSpaceOnUse\"><stop stop-color=\""u8);
            WriteColor(destination, NextColor(ref state, colors));
            destination.Write("\"/><stop offset=\"1\" stop-color=\""u8);
            WriteColor(destination, NextColor(ref state, colors));
            destination.Write("\"/></linearGradient>"u8);
        }
        destination.Write("</defs>"u8);
    }

    private static void Ring(IBufferWriter<byte> destination, ulong state, ReadOnlySpan<uint> colors)
    {
        uint c0 = NextColor(ref state, colors);
        uint c1 = NextColor(ref state, colors);
        uint c2 = NextColor(ref state, colors);
        uint c3 = NextColor(ref state, colors);
        destination.Write("<path d=\"M0 0h90v45H0z\" fill=\""u8);
        WriteColor(destination, c0);
        destination.Write("\"/><path d=\"M0 45h90v45H0z\" fill=\""u8);
        WriteColor(destination, c1);
        destination.Write("\"/><path d=\"M83 45a38 38 0 00-76 0h76z\" fill=\""u8);
        WriteColor(destination, c1);
        destination.Write("\"/><path d=\"M83 45a38 38 0 01-76 0h76z\" fill=\""u8);
        WriteColor(destination, c2);
        destination.Write("\"/><path d=\"M77 45a32 32 0 10-64 0h64z\" fill=\""u8);
        WriteColor(destination, c2);
        destination.Write("\"/><path d=\"M77 45a32 32 0 11-64 0h64z\" fill=\""u8);
        WriteColor(destination, c3);
        destination.Write("\"/><path d=\"M71 45a26 26 0 00-52 0h52z\" fill=\""u8);
        WriteColor(destination, c3);
        destination.Write("\"/><path d=\"M71 45a26 26 0 01-52 0h52z\" fill=\""u8);
        WriteColor(destination, c0);
        destination.Write("\"/><circle cx=\"45\" cy=\"45\" r=\"23\" fill=\""u8);
        WriteColor(destination, NextColor(ref state, colors));
        destination.Write("\"/>"u8);
    }

    private static void Bauhaus(IBufferWriter<byte> destination, ulong state, ReadOnlySpan<uint> colors)
    {
        destination.Write("<rect width=\"80\" height=\"80\" fill=\""u8);
        WriteColor(destination, NextColor(ref state, colors));
        destination.Write("\"/><rect x=\"10\" y=\"30\" width=\"80\" height=\""u8);
        WriteNumber(destination, Next(ref state, 2) == 0 ? 80 : 10);
        destination.Write("\" fill=\""u8);
        WriteColor(destination, NextColor(ref state, colors));
        destination.Write("\" transform=\"translate("u8);
        WriteNumber(destination, Next(ref state, 43) - 21);
        destination.Write(" "u8);
        WriteNumber(destination, Next(ref state, 43) - 21);
        destination.Write(") rotate("u8);
        WriteNumber(destination, Next(ref state, 360));
        destination.Write(" 40 40)\"/><circle cx=\"40\" cy=\"40\" r=\"16\" fill=\""u8);
        WriteColor(destination, NextColor(ref state, colors));
        destination.Write("\" transform=\"translate("u8);
        WriteNumber(destination, Next(ref state, 41) - 20);
        destination.Write(" "u8);
        WriteNumber(destination, Next(ref state, 41) - 20);
        destination.Write(")\"/><line x1=\"0\" y1=\"40\" x2=\"80\" y2=\"40\" stroke-width=\"2\" stroke=\""u8);
        WriteColor(destination, NextColor(ref state, colors));
        destination.Write("\" transform=\"translate("u8);
        WriteNumber(destination, Next(ref state, 39) - 19);
        destination.Write(" "u8);
        WriteNumber(destination, Next(ref state, 39) - 19);
        destination.Write(") rotate("u8);
        WriteNumber(destination, Next(ref state, 360));
        destination.Write(" 40 40)\"/>"u8);
    }
}
