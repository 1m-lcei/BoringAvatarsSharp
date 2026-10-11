# BoringAvatarsSharp

[![CI](https://github.com/1m-lcei/BoringAvatarsSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/1m-lcei/BoringAvatarsSharp/actions/workflows/ci.yml) ![Version](https://img.shields.io/badge/dynamic/xml?url=https%3A%2F%2Fraw.githubusercontent.com%2F1m-lcei%2FBoringAvatarsSharp%2Fmain%2Fsrc%2FBoringAvatarsSharp%2FBoringAvatarsSharp.csproj&query=%2FProject%2FPropertyGroup%2FVersion&label=version&color=blue)

A C# SVG avatar generator with six styles: **Marble**, **Beam**, **Pixel**, **Sunset**, **Ring**, and **Bauhaus**. No runtime packages, network calls, or image libraries are required.

The styles are adapted from [boring-avatars](https://github.com/boringdesigners/boring-avatars). This is **not a direct port**: it uses a different name hash and drawing parameter generator, so the same name and options produce different avatars from the original. Within this implementation, identical inputs produce identical SVG output.

## Sample gallery

[View the online gallery](https://1m-lcei.github.io/BoringAvatarsSharp/sandbox/output/)

## Drop it into your project

Use a **.NET 10** or above project. Copy [AvatarGenerator.cs](src/BoringAvatarsSharp/AvatarGenerator.cs) into it. That file contains `AvatarGenerator` and `AvatarVariant`; no project reference or NuGet package is needed. It also works with implicit usings disabled.

```csharp
using System.IO;
using BoringAvatarsSharp;

string svg = AvatarGenerator.GetSvg("Ada Lovelace", AvatarVariant.Beam, size: 88);
File.WriteAllText("avatar.svg", svg);
```

For UTF-8 output, write directly to any `IBufferWriter<byte>`:

```csharp
using System.Buffers;
using BoringAvatarsSharp;

var buffer = new ArrayBufferWriter<byte>();
AvatarGenerator.WriteSvg(buffer, "Ada Lovelace"u8, AvatarVariant.Pixel);
// Consume buffer.WrittenSpan or buffer.WrittenMemory.
```

Both methods accept UTF-16 (`string` / `ReadOnlySpan<char>`) and UTF-8 (`ReadOnlySpan<byte>`) names, with the same options:

| Option | Default | Meaning |
| --- | --- | --- |
| `variant` | `AvatarVariant.Marble` | One of the six styles above |
| `size` | `40` | Positive integer width and height in pixels |
| `square` | `false` | Use a square outline instead of a circle |
| `includeTitle` | `false` | Include the name in a `<title>` element |
| `colors` | Built-in palette | RGB values as `ReadOnlySpan<uint>`, e.g. `colors: [0x264653, 0xE9C46A, 0xE76F51]` |

An empty palette selects the default colors: `#92A1C6`, `#146A7C`, `#F0AB3D`, `#C271B4`, and `#C20D90`. Beam uses black or white for facial features to maintain contrast.

## Custom colors

Pass RGB values directly to either generation method. Color order affects the generated avatar:

```csharp
using BoringAvatarsSharp;

string svg = AvatarGenerator.GetSvg(
    "Ada Lovelace", AvatarVariant.Beam, square: true,
    colors: [0x264653, 0xE9C46A, 0xE76F51]);
```

The library provides only its default colors.

The generator selects from the entire palette, including entries after the fifth. Selections can repeat, so an image need not use every supplied color. The number of palette selections per avatar is:

| Variant | Palette selections |
| --- | --- |
| Marble | 3 |
| Beam | 2, plus black or white facial features |
| Pixel | 64, one per cell |
| Sunset | 4 gradient stops |
| Ring | 5 |
| Bauhaus | 4 |

More colors expand the choices across names, while smaller palettes constrain the overall tone. Pixel can use more than five distinct palette colors in one image. Gradients, blur, and blending can produce intermediate rendered colors beyond those selected from the palette.

## Behavior and integration

- Empty names are valid; a null string is treated as empty. Equivalent UTF-8 and UTF-16 names produce the same SVG. Unicode normalization is left to the caller.
- Names are XML-escaped. Invalid Unicode or XML characters, nonpositive sizes, unknown variants, and colors outside `0x000000`–`0xFFFFFF` are rejected. `WriteSvg` validates arguments before writing; a destination failure can leave partial output. The caller owns flushing and completion.
- The name is always included in `aria-label`, even with `includeTitle: false`. Use a public display name or an opaque seed if the original input is private.
- SVGs use fixed internal IDs. Save them as separate files or display them through `<img>` elements. When inserting multiple SVGs inline into one HTML document, give each SVG's IDs and references unique prefixes.
- Output is SVG only. Rendering support for filters and blend modes depends on the viewer. Avatars are decorative identifiers, not unique IDs or cryptographic hashes.

## Credits and license

The original [boring-avatars](https://github.com/boringdesigners/boring-avatars) is available under the [MIT license](https://github.com/boringdesigners/boring-avatars/blob/master/LICENSE).

BoringAvatarsSharp's own code is distributed under the [MIT license](LICENSE).
