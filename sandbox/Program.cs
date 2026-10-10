#!/usr/bin/env dotnet
#:include ../src/BoringAvatarsSharp/AvatarGenerator.cs
#:property ImplicitUsings=disable
#:property TreatWarningsAsErrors=true
#:property PublishAot=false
#:property CheckForOverflowUnderflow=true

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using BoringAvatarsSharp;

string outputDirectory = Path.Combine(SourceDirectory(), "output");
Directory.CreateDirectory(outputDirectory);
AvatarVariant[] variants = Enum.GetValues<AvatarVariant>();
XNamespace svgNamespace = "http://www.w3.org/2000/svg";
string[] names = ["Ada Lovelace", "Grace Hopper", "山田 太郎", "A&B <C> 😀"];
// Reference palette attribution and license terms are embedded in the gallery below.
// ColorBrewer's five-color RGB values are converted to uint literals without changing their order.
(string Label, PaletteColors Colors, string Source, string Author)[] palettes =
[
    ("BoringAvatars (default)", static () => [0x92A1C6, 0x146A7C, 0xF0AB3D, 0xC271B4, 0xC20D90], "https://github.com/boringdesigners/boring-avatars#props", "boringdesigners"),
    ("Ice Cream GB", static () => [0x7C3F58, 0xEB6B6F, 0xF9A875, 0xFFF6D3], "https://lospec.com/palette-list/ice-cream-gb", "Kerrie Lake"),
    ("Nord Aurora", static () => [0xBF616A, 0xD08770, 0xEBCB8B, 0xA3BE8C, 0xB48EAD], "https://www.nordtheme.com/docs/colors-and-palettes/", "Sven Greb"),
    ("Nord Frost", static () => [0x8FBCBB, 0x88C0D0, 0x81A1C1, 0x5E81AC], "https://www.nordtheme.com/docs/colors-and-palettes/", "Sven Greb"),
    ("Nord Polar Night", static () => [0x2E3440, 0x3B4252, 0x434C5E, 0x4C566A], "https://www.nordtheme.com/docs/colors-and-palettes/", "Sven Greb"),
    ("Nord Snow Storm", static () => [0xD8DEE9, 0xE5E9F0, 0xECEFF4], "https://www.nordtheme.com/docs/colors-and-palettes/", "Sven Greb"),
    ("ColorBrewer Set2 (5)", static () => [0x66C2A5, 0xFC8D62, 0x8DA0CB, 0xE78AC3, 0xA6D854], "https://colorbrewer2.org/?type=qualitative&scheme=Set2&n=5", "Cynthia Brewer"),
    ("ColorBrewer Dark2 (5)", static () => [0x1B9E77, 0xD95F02, 0x7570B3, 0xE7298A, 0x66A61E], "https://colorbrewer2.org/?type=qualitative&scheme=Dark2&n=5", "Cynthia Brewer")
];
var paletteSamples = new List<(string Label, string Name, PaletteColors Colors, bool Square)>();
foreach (var palette in palettes)
    foreach (bool square in new[] { false, true })
        paletteSamples.Add(($"{palette.Label} / {(square ? "Square" : "Round")}", names[0], palette.Colors, square));

var gallery = new StringBuilder("""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>BoringAvatarsSharp samples</title>
<style>
body{font:16px/1.5 system-ui,sans-serif;margin:32px;background:#f4f3ef;color:#202020}
main{max-width:1240px;margin:auto}h1{margin-bottom:0}h2{margin-top:40px}
a{color:#005b78}.table-scroll{overflow-x:auto}table{border-collapse:collapse;width:100%}
th,td{padding:12px;text-align:center;border-bottom:1px solid #d6d3cb}th:first-child{text-align:left}
thead th{vertical-align:top;font-size:14px}tbody th{font-weight:600}
img{display:block;width:88px;height:88px;margin:auto}
.swatches{display:flex;flex-wrap:wrap;justify-content:center;gap:2px;max-width:128px;margin:8px auto 0}
.swatches span{width:16px;height:16px;border:1px solid #0002}
pre{overflow-x:auto;background:#fff;padding:16px;border-radius:8px}code{font-size:13px}
.palettes{display:flex;flex-wrap:wrap;gap:12px}.palette{background:#fff;padding:12px;border-radius:8px}
.palette code{display:block;max-width:34ch;overflow-wrap:anywhere}.palette .swatches{justify-content:flex-start;margin-left:0}
@media(max-width:600px){body{margin:16px}th,td{padding:8px}}
</style>
</head>
<body><main>
<h1>BoringAvatarsSharp</h1>
<p>Six styles, four names, and eight reference palettes in round and square outlines.</p>
<p><a href="#names">Compare names</a> · <a href="#palettes">Compare palettes</a></p>
<p>Each image is a separate SVG document. Select an image to open its SVG file.</p>
<h2>Usage</h2>
<pre><code>string svg = AvatarGenerator.GetSvg(
    "Ada Lovelace", AvatarVariant.Beam, size: 88,
    square: true, includeTitle: true,
    colors: [0x7C3F58, 0xEB6B6F, 0xF9A875, 0xFFF6D3]);</code></pre>
<p>This example uses Ice Cream GB; see its source below.</p>
<section id="names">
<h2>Names</h2><p>The same default palette and round outline, with four different names.</p>
""");
int count = 0;
WriteTable("", names.Select(name => (name, name, palettes[0].Colors, false)).ToArray());
gallery.Append("""
</section>
<section id="palettes">
<h2>Palettes</h2>
<p>Every image below uses <strong>Ada Lovelace</strong>. Each palette is shown with
<code>square: false</code> and <code>square: true</code>. Omit <code>colors</code> for the default palette.
Beam uses black or white facial features for contrast.</p>
<p>These palettes are references for choosing your own <code>colors</code> values.
The library includes only its default colors; the other palettes are defined in this sample.</p>
<p>Every color in a palette is eligible; colors after the fifth entry are used too.
Selections can repeat: Marble makes 3 palette selections, Beam 2 (plus black or white facial features),
Pixel 64, Sunset 4 gradient stops, Ring 5, and Bauhaus 4. A larger palette adds variety across names,
but not every color will appear in each image. Gradients and blending can produce intermediate colors.</p>
<p>BoringAvatars and Nord use the MIT license; ColorBrewer uses Apache 2.0.
The Ice Cream GB listing credits Kerrie Lake but does not specify a separate license.
See <a href="#palette-notices">palette credits and licenses</a>.</p>
<div class="palettes">
""");
foreach (var palette in palettes)
{
    ReadOnlySpan<uint> colors = palette.Colors();
    gallery.Append($"<div class=\"palette\"><strong>{palette.Label}</strong>");
    WriteSwatches(colors);
    gallery.Append($"<p>{colors.Length} colors · {WebUtility.HtmlEncode(palette.Author)}</p><code>");
    for (int i = 0; i < colors.Length; i++)
    {
        if (i > 0)
            gallery.Append(", ");
        gallery.Append($"0x{colors[i]:X6}");
    }
    gallery.Append($"</code><p><a href=\"{WebUtility.HtmlEncode(palette.Source)}\">Official palette</a></p>");
    gallery.Append("</div>");
}
gallery.Append("</div>");
WriteTable("palette-", paletteSamples);
gallery.Append("""
<details id="palette-notices">
<summary>Palette credits and licenses</summary>
<p>Sources and authors are linked alongside each palette above.</p>
<details>
<summary>BoringAvatars default — MIT</summary>
<pre>
MIT License

Copyright (c) 2021 boringdesigners

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the &quot;Software&quot;), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED &quot;AS IS&quot;, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
</pre>
</details>
<details>
<summary>Nord Aurora, Frost, Polar Night, and Snow Storm — MIT</summary>
<pre>
MIT License (MIT)

Copyright (c) 2016-present Sven Greb &lt;development@svengreb.de&gt; (https://www.svengreb.de)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the &quot;Software&quot;), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED &quot;AS IS&quot;, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
</pre>
</details>
<p>Ice Cream GB by Kerrie Lake: the source listing does not specify a separate license.
This project does not assign an MIT license to this palette.</p>
<details>
<summary>ColorBrewer Set2 and Dark2 — Apache 2.0</summary>
<p>Copyright (c) Cynthia Brewer, Mark Harrower and The Pennsylvania State University.</p>
<p>This product includes color specifications and designs developed by Cynthia Brewer
(<a href="http://colorbrewer.org/">colorbrewer.org</a>).</p>
<p>The official five-color RGB values were converted to C# uint literals; values and order are unchanged.</p>
<pre>
                                 Apache License
                           Version 2.0, January 2004
                        http://www.apache.org/licenses/

   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

   1. Definitions.

      &quot;License&quot; shall mean the terms and conditions for use, reproduction,
      and distribution as defined by Sections 1 through 9 of this document.

      &quot;Licensor&quot; shall mean the copyright owner or entity authorized by
      the copyright owner that is granting the License.

      &quot;Legal Entity&quot; shall mean the union of the acting entity and all
      other entities that control, are controlled by, or are under common
      control with that entity. For the purposes of this definition,
      &quot;control&quot; means (i) the power, direct or indirect, to cause the
      direction or management of such entity, whether by contract or
      otherwise, or (ii) ownership of fifty percent (50%) or more of the
      outstanding shares, or (iii) beneficial ownership of such entity.

      &quot;You&quot; (or &quot;Your&quot;) shall mean an individual or Legal Entity
      exercising permissions granted by this License.

      &quot;Source&quot; form shall mean the preferred form for making modifications,
      including but not limited to software source code, documentation
      source, and configuration files.

      &quot;Object&quot; form shall mean any form resulting from mechanical
      transformation or translation of a Source form, including but
      not limited to compiled object code, generated documentation,
      and conversions to other media types.

      &quot;Work&quot; shall mean the work of authorship, whether in Source or
      Object form, made available under the License, as indicated by a
      copyright notice that is included in or attached to the work
      (an example is provided in the Appendix below).

      &quot;Derivative Works&quot; shall mean any work, whether in Source or Object
      form, that is based on (or derived from) the Work and for which the
      editorial revisions, annotations, elaborations, or other modifications
      represent, as a whole, an original work of authorship. For the purposes
      of this License, Derivative Works shall not include works that remain
      separable from, or merely link (or bind by name) to the interfaces of,
      the Work and Derivative Works thereof.

      &quot;Contribution&quot; shall mean any work of authorship, including
      the original version of the Work and any modifications or additions
      to that Work or Derivative Works thereof, that is intentionally
      submitted to Licensor for inclusion in the Work by the copyright owner
      or by an individual or Legal Entity authorized to submit on behalf of
      the copyright owner. For the purposes of this definition, &quot;submitted&quot;
      means any form of electronic, verbal, or written communication sent
      to the Licensor or its representatives, including but not limited to
      communication on electronic mailing lists, source code control systems,
      and issue tracking systems that are managed by, or on behalf of, the
      Licensor for the purpose of discussing and improving the Work, but
      excluding communication that is conspicuously marked or otherwise
      designated in writing by the copyright owner as &quot;Not a Contribution.&quot;

      &quot;Contributor&quot; shall mean Licensor and any individual or Legal Entity
      on behalf of whom a Contribution has been received by Licensor and
      subsequently incorporated within the Work.

   2. Grant of Copyright License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      copyright license to reproduce, prepare Derivative Works of,
      publicly display, publicly perform, sublicense, and distribute the
      Work and such Derivative Works in Source or Object form.

   3. Grant of Patent License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      (except as stated in this section) patent license to make, have made,
      use, offer to sell, sell, import, and otherwise transfer the Work,
      where such license applies only to those patent claims licensable
      by such Contributor that are necessarily infringed by their
      Contribution(s) alone or by combination of their Contribution(s)
      with the Work to which such Contribution(s) was submitted. If You
      institute patent litigation against any entity (including a
      cross-claim or counterclaim in a lawsuit) alleging that the Work
      or a Contribution incorporated within the Work constitutes direct
      or contributory patent infringement, then any patent licenses
      granted to You under this License for that Work shall terminate
      as of the date such litigation is filed.

   4. Redistribution. You may reproduce and distribute copies of the
      Work or Derivative Works thereof in any medium, with or without
      modifications, and in Source or Object form, provided that You
      meet the following conditions:

      (a) You must give any other recipients of the Work or
          Derivative Works a copy of this License; and

      (b) You must cause any modified files to carry prominent notices
          stating that You changed the files; and

      (c) You must retain, in the Source form of any Derivative Works
          that You distribute, all copyright, patent, trademark, and
          attribution notices from the Source form of the Work,
          excluding those notices that do not pertain to any part of
          the Derivative Works; and

      (d) If the Work includes a &quot;NOTICE&quot; text file as part of its
          distribution, then any Derivative Works that You distribute must
          include a readable copy of the attribution notices contained
          within such NOTICE file, excluding those notices that do not
          pertain to any part of the Derivative Works, in at least one
          of the following places: within a NOTICE text file distributed
          as part of the Derivative Works; within the Source form or
          documentation, if provided along with the Derivative Works; or,
          within a display generated by the Derivative Works, if and
          wherever such third-party notices normally appear. The contents
          of the NOTICE file are for informational purposes only and
          do not modify the License. You may add Your own attribution
          notices within Derivative Works that You distribute, alongside
          or as an addendum to the NOTICE text from the Work, provided
          that such additional attribution notices cannot be construed
          as modifying the License.

      You may add Your own copyright statement to Your modifications and
      may provide additional or different license terms and conditions
      for use, reproduction, or distribution of Your modifications, or
      for any such Derivative Works as a whole, provided Your use,
      reproduction, and distribution of the Work otherwise complies with
      the conditions stated in this License.

   5. Submission of Contributions. Unless You explicitly state otherwise,
      any Contribution intentionally submitted for inclusion in the Work
      by You to the Licensor shall be under the terms and conditions of
      this License, without any additional terms or conditions.
      Notwithstanding the above, nothing herein shall supersede or modify
      the terms of any separate license agreement you may have executed
      with Licensor regarding such Contributions.

   6. Trademarks. This License does not grant permission to use the trade
      names, trademarks, service marks, or product names of the Licensor,
      except as required for reasonable and customary use in describing the
      origin of the Work and reproducing the content of the NOTICE file.

   7. Disclaimer of Warranty. Unless required by applicable law or
      agreed to in writing, Licensor provides the Work (and each
      Contributor provides its Contributions) on an &quot;AS IS&quot; BASIS,
      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
      implied, including, without limitation, any warranties or conditions
      of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
      PARTICULAR PURPOSE. You are solely responsible for determining the
      appropriateness of using or redistributing the Work and assume any
      risks associated with Your exercise of permissions under this License.

   8. Limitation of Liability. In no event and under no legal theory,
      whether in tort (including negligence), contract, or otherwise,
      unless required by applicable law (such as deliberate and grossly
      negligent acts) or agreed to in writing, shall any Contributor be
      liable to You for damages, including any direct, indirect, special,
      incidental, or consequential damages of any character arising as a
      result of this License or out of the use or inability to use the
      Work (including but not limited to damages for loss of goodwill,
      work stoppage, computer failure or malfunction, or any and all
      other commercial damages or losses), even if such Contributor
      has been advised of the possibility of such damages.

   9. Accepting Warranty or Additional Liability. While redistributing
      the Work or Derivative Works thereof, You may choose to offer,
      and charge a fee for, acceptance of support, warranty, indemnity,
      or other liability obligations and/or rights consistent with this
      License. However, in accepting such obligations, You may act only
      on Your own behalf and on Your sole responsibility, not on behalf
      of any other Contributor, and only if You agree to indemnify,
      defend, and hold each Contributor harmless for any liability
      incurred by, or claims asserted against, such Contributor by reason
      of your accepting any such warranty or additional liability.

   END OF TERMS AND CONDITIONS

   APPENDIX: How to apply the Apache License to your work.

      To apply the Apache License to your work, attach the following
      boilerplate notice, with the fields enclosed by brackets &quot;{}&quot;
      replaced with your own identifying information. (Don&#39;t include
      the brackets!)  The text should be enclosed in the appropriate
      comment syntax for the file format. We also recommend that a
      file or class name and description of purpose be included on the
      same &quot;printed page&quot; as the copyright notice for easier
      identification within third-party archives.

   Copyright {yyyy} {name of copyright owner}

   Licensed under the Apache License, Version 2.0 (the &quot;License&quot;);
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an &quot;AS IS&quot; BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
</pre>
</details>
</details>
""");
gallery.Append("</section></main></body></html>\n");

int expectedCount = variants.Length * (names.Length + paletteSamples.Count);
if (count != expectedCount)
    throw new InvalidOperationException($"Expected {expectedCount} samples, generated {count}.");
File.WriteAllText(Path.Combine(outputDirectory, "index.html"), gallery.ToString().ReplaceLineEndings("\n"));
Console.WriteLine($"Wrote and validated {count} SVG files: {outputDirectory}");

void WriteTable(string filePrefix, IReadOnlyList<(string Label, string Name, PaletteColors Colors, bool Square)> samples)
{
    gallery.Append("<div class=\"table-scroll\" role=\"region\" aria-label=\"Avatar comparison\" tabindex=\"0\"><table><thead><tr><th scope=\"col\">Variant</th>");
    foreach (var sample in samples)
    {
        gallery.Append($"<th scope=\"col\">{WebUtility.HtmlEncode(sample.Label)}");
        if (filePrefix.Length != 0)
            WriteSwatches(sample.Colors());
        gallery.Append("</th>");
    }
    gallery.Append("</tr></thead><tbody>");
    foreach (AvatarVariant variant in variants)
    {
        gallery.Append($"<tr><th scope=\"row\">{variant}</th>");
        for (int i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            ReadOnlySpan<uint> colors = sample.Colors();
            string svg = AvatarGenerator.GetSvg(sample.Name, variant, size: 88,
                square: sample.Square, includeTitle: true, colors: colors);
            var buffer = new ArrayBufferWriter<byte>();
            AvatarGenerator.WriteSvg(buffer, Encoding.UTF8.GetBytes(sample.Name), variant, size: 88,
                square: sample.Square, includeTitle: true, colors: colors);
            XElement root = XElement.Parse(svg);
            XElement clip = root.Descendants(svgNamespace + "clipPath").Single().Element(svgNamespace + "rect")!;
            var allowedPaints = new HashSet<string>();
            foreach (uint color in colors)
                allowedPaints.Add($"#{color:X6}");
            if (variant == AvatarVariant.Beam)
                allowedPaints.UnionWith(["#000000", "#FFFFFF"]);
            string[] paints = root.Descendants().Attributes()
                .Where(attribute => attribute.Name.LocalName is "fill" or "stroke" or "stop-color")
                .Select(attribute => attribute.Value).Where(value => value.StartsWith('#')).ToArray();
            if (svg != Encoding.UTF8.GetString(buffer.WrittenSpan)
                || root.Name != svgNamespace + "svg"
                || (string?)root.Attribute("aria-label") != sample.Name
                || (string?)root.Element(svgNamespace + "title") != sample.Name
                || (string?)root.Attribute("width") != "88"
                || (string?)root.Attribute("height") != "88"
                || paints.Length == 0 || paints.Any(paint => !allowedPaints.Contains(paint))
                || (clip.Attribute("rx") is null) != sample.Square)
                throw new InvalidOperationException($"Invalid output for {variant}, {sample.Label}.");

            string fileName = $"{variant.ToString().ToLowerInvariant()}-{filePrefix}{i + 1}.svg";
            File.WriteAllText(Path.Combine(outputDirectory, fileName), svg);
            string alt = WebUtility.HtmlEncode($"{variant}: {sample.Name}, {sample.Label}");
            gallery.Append($"<td><a href=\"{fileName}\"><img src=\"{fileName}\" width=\"88\" height=\"88\" alt=\"{alt}\"></a></td>");
            count++;
        }
        gallery.Append("</tr>");
    }
    gallery.Append("</tbody></table></div>");
}

void WriteSwatches(ReadOnlySpan<uint> colors)
{
    gallery.Append("<div class=\"swatches\" aria-hidden=\"true\">");
    foreach (uint color in colors)
        gallery.Append($"<span style=\"background:#{color:X6}\" title=\"#{color:X6}\"></span>");
    gallery.Append("</div>");
}

static string SourceDirectory([CallerFilePath] string sourceFile = "") => Path.GetDirectoryName(sourceFile)!;

delegate ReadOnlySpan<uint> PaletteColors();
