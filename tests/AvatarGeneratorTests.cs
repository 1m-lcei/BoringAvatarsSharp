using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace BoringAvatarsSharp.Tests;

public class AvatarGeneratorTests
{
    private const string Name = "A&B <C> \"D\" 'E' 山田 é 😀\t\r\n";
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    [Test]
    [Arguments(AvatarVariant.Marble, "44A9C2CE6B364FFC35BC89320EBE3808FD59CB9CF73B0A2B513B51228F998024")]
    [Arguments(AvatarVariant.Beam, "9682C0B0B8410777BE4BC3E7378E8ED06984237F85FE880EBC85B340D0CF7C02")]
    [Arguments(AvatarVariant.Pixel, "608B15F2BA07429CE8637E5B57EA4C31CF498EC8F8D144D9141F451329332C27")]
    [Arguments(AvatarVariant.Sunset, "6BF538EA81EC7C5D349D903A1CB9C83DA216898ECBFC7C623F8AFCD33686D199")]
    [Arguments(AvatarVariant.Ring, "3184AA3DCD65F966AD7F8BA0267765684C9933432D54ECF3FAF55192C575457A")]
    [Arguments(AvatarVariant.Bauhaus, "D6C54602706B972A8C7D9ED6FBA4C7E240DDA2FBE674093F5AB4E32E1E5D9184")]
    public async Task WriteSvg_preserves_original_bytes_in_one_span(AvatarVariant variant, string expectedHash)
    {
        // Hashes captured from the streaming implementation at commit 37baee7.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        bool usedOneSpan = true;
        uint[] colors = [0x000001, 0x00ABCD, 0xFFFFFF, 0, 0x123456, 0xFEDCBA];
        for (int i = 0; i < 4096; i++)
        {
            string name = i.ToString(CultureInfo.InvariantCulture) + " " + Name;
            var destination = new RotatingBufferWriter();
            AvatarGenerator.WriteSvg(destination, name, variant, int.MaxValue,
                square: (i & 1) != 0, includeTitle: (i & 2) != 0, colors: colors);
            usedOneSpan &= destination.BufferRequests == 1 && destination.AdvanceCalls == 1;
            hash.AppendData(destination.WrittenSpan);
        }

        await Assert.That(Convert.ToHexString(hash.GetHashAndReset())).IsEqualTo(expectedHash);
        await Assert.That(usedOneSpan).IsTrue();
    }

    [Test]
    [MatrixDataSource]
    public async Task WriteSvg_batches_escaped_names_and_commits_only_written_bytes(
        [Matrix(AvatarVariant.Marble, AvatarVariant.Beam, AvatarVariant.Pixel,
            AvatarVariant.Sunset, AvatarVariant.Ring, AvatarVariant.Bauhaus)] AvatarVariant variant,
        [Matrix(false, true)] bool utf8,
        [Matrix(128, 4096)] int repetitions)
    {
        string name = string.Concat(Enumerable.Repeat(Name, repetitions));
        string expected = AvatarGenerator.GetSvg(name, variant, int.MaxValue, includeTitle: true);
        var destination = new RotatingBufferWriter();

        if (utf8)
            AvatarGenerator.WriteSvg(destination, Utf8.GetBytes(name), variant, int.MaxValue, includeTitle: true);
        else
            AvatarGenerator.WriteSvg(destination, name, variant, int.MaxValue, includeTitle: true);

        await Assert.That(Utf8.GetString(destination.WrittenSpan)).IsEqualTo(expected);
        await Assert.That(destination.LargestSizeHint).IsLessThanOrEqualTo(65536);
        await Assert.That(destination.AdvanceCalls).IsEqualTo(destination.BufferRequests);
        if (repetitions == 128)
            await Assert.That(destination.BufferRequests).IsEqualTo(1);
        else
            await Assert.That(destination.BufferRequests).IsGreaterThan(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WriteSvg_validates_the_entire_name_before_requesting_a_buffer(bool utf8)
    {
        string name = new string('x', 65536) + '\0';
        var destination = new FailingBufferWriter();

        var exception = await Assert.That(() =>
        {
            if (utf8)
                AvatarGenerator.WriteSvg(destination, Utf8.GetBytes(name));
            else
                AvatarGenerator.WriteSvg(destination, name);
        }).Throws<ArgumentException>();

        await Assert.That(exception!.ParamName).IsEqualTo("name");
    }

    [Test]
    [Arguments(AvatarVariant.Marble)]
    [Arguments(AvatarVariant.Beam)]
    [Arguments(AvatarVariant.Pixel)]
    [Arguments(AvatarVariant.Sunset)]
    [Arguments(AvatarVariant.Ring)]
    [Arguments(AvatarVariant.Bauhaus)]
    [NotInParallel]
    public async Task WriteSvg_does_not_allocate_after_warmup_with_a_reused_buffer(AvatarVariant variant)
    {
        var destination = new ArrayBufferWriter<byte>(8192);
        for (int i = 0; i < 512; i++)
        {
            destination.ResetWrittenCount();
            AvatarGenerator.WriteSvg(destination, Name, variant, includeTitle: true);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++)
        {
            destination.ResetWrittenCount();
            AvatarGenerator.WriteSvg(destination, Name, variant, includeTitle: true);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    [Arguments("Ada")]
    [Arguments("")]
    [Arguments(null)]
    public async Task GetSvg_defaults_match_WriteSvg_and_treat_a_null_string_as_empty(string? name)
    {
        // Arrange
        string expected = Generate(name);
        byte[] utf8Name = Utf8.GetBytes(name ?? "");

        // Act
        string utf8Output = AvatarGenerator.GetSvg(utf8Name);
        string utf16Output = AvatarGenerator.GetSvg(name);

        // Assert
        await Assert.That(utf8Output).IsEqualTo(expected);
        await Assert.That(utf16Output).IsEqualTo(expected);
    }

    [Test]
    [MatrixDataSource]
    public async Task GetSvg_matches_WriteSvg_for_utf8_and_utf16_slices(
        [Matrix(AvatarVariant.Marble, AvatarVariant.Beam, AvatarVariant.Pixel,
            AvatarVariant.Sunset, AvatarVariant.Ring, AvatarVariant.Bauhaus)] AvatarVariant variant,
        [Matrix(1, 1000)] int repetitions)
    {
        // Arrange
        string name = string.Concat(Enumerable.Repeat(Name, repetitions));
        char[] chars = ("\0" + name + "\0").ToCharArray();
        byte[] bytes = [0xFF, .. Utf8.GetBytes(name), 0xFF];
        uint[] colors = [0x000001, 0x00ABCD, 0xFFFFFF];
        var destination = new ArrayBufferWriter<byte>();
        AvatarGenerator.WriteSvg(destination, name, variant,
            size: 88, square: true, includeTitle: true, colors: colors);
        string expected = Decode(destination);

        // Act
        string utf8Output = AvatarGenerator.GetSvg(bytes.AsSpan(1, bytes.Length - 2), variant,
            size: 88, square: true, includeTitle: true, colors: colors);
        string utf16Output = AvatarGenerator.GetSvg(chars.AsSpan(1, chars.Length - 2), variant,
            size: 88, square: true, includeTitle: true, colors: colors);

        // Assert
        await Assert.That(utf8Output).IsEqualTo(expected);
        await Assert.That(utf16Output).IsEqualTo(expected);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetSvg_rejects_invalid_names(bool utf8)
    {
        // Arrange
        string name = new string('x', 4000) + '\uD800';
        byte[] bytes = [.. Utf8.GetBytes(new string('x', 4000)), 0x80];

        // Act
        var exception = await Assert.That(() =>
        {
            if (utf8)
                AvatarGenerator.GetSvg(bytes);
            else
                AvatarGenerator.GetSvg(name);
        }).Throws<ArgumentException>();

        // Assert
        await Assert.That(exception!.ParamName).IsEqualTo("name");
    }

    [Test]
    public async Task WriteSvg_defaults_to_a_round_40_pixel_marble_without_a_title()
    {
        // Arrange
        var destination = new ArrayBufferWriter<byte>();
        string expected = Generate("Ada", AvatarVariant.Marble, size: 40, square: false, includeTitle: false);

        // Act
        AvatarGenerator.WriteSvg(destination, "Ada"u8);

        // Assert
        XElement root = XElement.Parse(Decode(destination));
        await Assert.That(Decode(destination)).IsEqualTo(expected);
        await Assert.That((string?)root.Attribute("width")).IsEqualTo("40");
        await Assert.That((string?)root.Attribute("height")).IsEqualTo("40");
        await Assert.That((string?)root.Descendants(Svg + "clipPath").Single().Element(Svg + "rect")!.Attribute("rx"))
            .IsEqualTo("40");
        await Assert.That(root.Element(Svg + "title")).IsNull();
    }

    [Test]
    [Arguments(AvatarVariant.Marble)]
    [Arguments(AvatarVariant.Beam)]
    [Arguments(AvatarVariant.Pixel)]
    [Arguments(AvatarVariant.Sunset)]
    [Arguments(AvatarVariant.Ring)]
    [Arguments(AvatarVariant.Bauhaus)]
    public async Task WriteSvg_produces_an_accessible_standalone_svg(AvatarVariant variant)
    {
        // Arrange
        var destination = new ArrayBufferWriter<byte>();

        // Act
        AvatarGenerator.WriteSvg(destination, Name, variant, size: 88, includeTitle: true);

        // Assert
        string output = Decode(destination);
        XElement root = XElement.Parse(output);
        string[] ids = root.Descendants().Attributes("id").Select(attribute => attribute.Value).ToArray();
        string[] references = root.Descendants().Attributes()
            .Select(attribute => attribute.Value)
            .Where(value => value.StartsWith("url(#", StringComparison.Ordinal))
            .Select(value => value[5..^1]).ToArray();

        await Assert.That(output).StartsWith("<svg ");
        await Assert.That(root.Name).IsEqualTo(Svg + "svg");
        await Assert.That((string?)root.Attribute("width")).IsEqualTo("88");
        await Assert.That((string?)root.Attribute("height")).IsEqualTo("88");
        await Assert.That((string?)root.Attribute("role")).IsEqualTo("img");
        await Assert.That((string?)root.Attribute("aria-label")).IsEqualTo(Name);
        await Assert.That((string?)root.Element(Svg + "title")).IsEqualTo(Name);
        await Assert.That(root.Element(Svg + "g")!.Elements()).IsNotEmpty();
        await Assert.That(ids.Distinct().Count()).IsEqualTo(ids.Length);
        await Assert.That(references).IsNotEmpty().And.All(reference => ids.Contains(reference));
    }

    [Test]
    [Arguments(AvatarVariant.Marble)]
    [Arguments(AvatarVariant.Beam)]
    [Arguments(AvatarVariant.Pixel)]
    [Arguments(AvatarVariant.Sunset)]
    [Arguments(AvatarVariant.Ring)]
    [Arguments(AvatarVariant.Bauhaus)]
    public async Task WriteSvg_repeats_the_same_drawing_and_changes_it_for_another_name(AvatarVariant variant)
    {
        // Arrange
        string expected = Generate("Ada", variant);

        // Act
        string repeated = Generate("Ada", variant);
        string other = Generate("Grace", variant);

        // Assert
        await Assert.That(repeated).IsEqualTo(expected);
        await Assert.That(XNode.DeepEquals(
            XElement.Parse(other).Element(Svg + "g"),
            XElement.Parse(expected).Element(Svg + "g"))).IsFalse();
    }

    [Test]
    public async Task WriteSvg_variants_produce_distinct_drawings()
    {
        // Arrange
        AvatarVariant[] variants = Enum.GetValues<AvatarVariant>();

        // Act
        string[] drawings = variants.Select(variant =>
            XElement.Parse(Generate("Ada", variant)).Element(Svg + "g")!.ToString()).ToArray();

        // Assert
        await Assert.That(drawings.Distinct().Count()).IsEqualTo(variants.Length);
    }

    [Test]
    [Arguments("", 1)]
    [Arguments(Name, 1)]
    [Arguments(Name, 1000)]
    public async Task WriteSvg_utf8_and_utf16_slices_preserve_the_name_and_options(string text, int repetitions)
    {
        // Arrange
        string name = string.Concat(Enumerable.Repeat(text, repetitions));
        char[] chars = ("\0" + name + "\0").ToCharArray();
        byte[] bytes = [0xFF, .. Utf8.GetBytes(name), 0xFF];
        uint[] colors = [0x000001, 0x00ABCD, 0xFFFFFF];
        var utf16Output = new ArrayBufferWriter<byte>();
        var utf8Output = new ArrayBufferWriter<byte>();

        // Act
        AvatarGenerator.WriteSvg(utf16Output, chars.AsSpan(1, chars.Length - 2), AvatarVariant.Sunset,
            size: 88, square: true, includeTitle: true, colors: colors);
        AvatarGenerator.WriteSvg(utf8Output, bytes.AsSpan(1, bytes.Length - 2), AvatarVariant.Sunset,
            size: 88, square: true, includeTitle: true, colors: colors);

        // Assert
        XElement root = XElement.Parse(Decode(utf8Output));
        await Assert.That(Decode(utf8Output)).IsEqualTo(Decode(utf16Output));
        await Assert.That((string?)root.Attribute("aria-label")).IsEqualTo(name);
        await Assert.That((string?)root.Element(Svg + "title")).IsEqualTo(name);
        await Assert.That((string?)root.Attribute("width")).IsEqualTo("88");
        await Assert.That((string?)root.Attribute("height")).IsEqualTo("88");
        await Assert.That(root.Descendants(Svg + "clipPath").Single().Element(Svg + "rect")!.Attribute("rx")).IsNull();
        await Assert.That(root.Descendants().Attributes("stop-color").Select(attribute => attribute.Value))
            .IsNotEmpty().And.All(color => color == "#000001" || color == "#00ABCD" || color == "#FFFFFF");
    }

    [Test]
    public async Task WriteSvg_treats_a_null_string_as_an_empty_name()
    {
        // Arrange
        string? name = null;
        string expected = Generate("");
        var destination = new ArrayBufferWriter<byte>();

        // Act
        AvatarGenerator.WriteSvg(destination, name);

        // Assert
        await Assert.That(Decode(destination)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(1, true)]
    [Arguments(int.MaxValue, false)]
    public async Task WriteSvg_size_and_outline_change_the_frame_without_changing_the_drawing(int size, bool square)
    {
        // Arrange
        XElement original = XElement.Parse(Generate("Ada", AvatarVariant.Bauhaus));

        // Act
        XElement resized = XElement.Parse(Generate("Ada", AvatarVariant.Bauhaus, size: size, square: square));

        // Assert
        string expectedSize = size.ToString(CultureInfo.InvariantCulture);
        await Assert.That((string?)resized.Attribute("width")).IsEqualTo(expectedSize);
        await Assert.That((string?)resized.Attribute("height")).IsEqualTo(expectedSize);
        await Assert.That((string?)resized.Attribute("viewBox")).IsEqualTo((string?)original.Attribute("viewBox"));
        await Assert.That((string?)resized.Descendants(Svg + "clipPath").Single().Element(Svg + "rect")!.Attribute("rx"))
            .IsEqualTo(square ? null : "40");
        await Assert.That(XNode.DeepEquals(original.Element(Svg + "g"), resized.Element(Svg + "g"))).IsTrue();
    }

    [Test]
    [Arguments(AvatarVariant.Marble)]
    [Arguments(AvatarVariant.Beam)]
    [Arguments(AvatarVariant.Pixel)]
    [Arguments(AvatarVariant.Sunset)]
    [Arguments(AvatarVariant.Ring)]
    [Arguments(AvatarVariant.Bauhaus)]
    public async Task WriteSvg_uses_the_requested_rgb_palette(AvatarVariant variant)
    {
        // Arrange
        var destination = new ArrayBufferWriter<byte>();
        uint[] colors = [0x000001];

        // Act
        AvatarGenerator.WriteSvg(destination, "Ada", variant, colors: colors);

        // Assert
        string[] paints = XElement.Parse(Decode(destination)).Descendants().Attributes()
            .Where(attribute => attribute.Name.LocalName is "fill" or "stroke" or "stop-color")
            .Select(attribute => attribute.Value)
            .Where(value => value != "none" && !value.StartsWith("url(#", StringComparison.Ordinal)).ToArray();
        await Assert.That(paints).Contains("#000001")
            .And.All(value => value == "#000001" || variant == AvatarVariant.Beam && value == "#FFFFFF");
    }

    [Test]
    [Arguments(AvatarVariant.Marble)]
    [Arguments(AvatarVariant.Beam)]
    [Arguments(AvatarVariant.Pixel)]
    [Arguments(AvatarVariant.Sunset)]
    [Arguments(AvatarVariant.Ring)]
    [Arguments(AvatarVariant.Bauhaus)]
    public async Task WriteSvg_can_select_colors_beyond_the_fifth_entry(AvatarVariant variant)
    {
        // Arrange
        uint[] colors = [0x111111, 0x111111, 0x111111, 0x111111, 0x111111, 0xABCDEF];

        // Act
        bool usesSixthColor = Enumerable.Range(0, 32).Any(i => AvatarGenerator.GetSvg(
            i.ToString(CultureInfo.InvariantCulture), variant, colors: colors)
            .Contains("#ABCDEF", StringComparison.Ordinal));

        // Assert
        await Assert.That(usesSixthColor).IsTrue();
    }

    [Test]
    public async Task WriteSvg_pixel_can_use_more_than_five_distinct_palette_colors()
    {
        // Arrange
        var destination = new ArrayBufferWriter<byte>();

        // Act
        AvatarGenerator.WriteSvg(destination, "Ada", AvatarVariant.Pixel, colors: [0x111111, 0x222222, 0x333333, 0x444444, 0x555555, 0x666666]);

        // Assert
        int colorCount = XElement.Parse(Decode(destination)).Descendants(Svg + "rect")
            .Attributes("fill").Select(attribute => attribute.Value).Distinct().Count();
        await Assert.That(colorCount).IsGreaterThan(5);
    }

    [Test]
    public async Task WriteSvg_an_empty_palette_uses_the_default_colors()
    {
        // Arrange
        string expected = Generate("Ada");
        var destination = new ArrayBufferWriter<byte>();

        // Act
        AvatarGenerator.WriteSvg(destination, "Ada", colors: []);

        // Assert
        await Assert.That(Decode(destination)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(AvatarVariant.Marble)]
    [Arguments(AvatarVariant.Beam)]
    [Arguments(AvatarVariant.Pixel)]
    [Arguments(AvatarVariant.Sunset)]
    [Arguments(AvatarVariant.Ring)]
    [Arguments(AvatarVariant.Bauhaus)]
    public async Task Explicit_default_colors_match_the_default_output(AvatarVariant variant)
    {
        // Arrange
        string expected = Generate(Name, variant);
        var destination = new ArrayBufferWriter<byte>();

        // Act
        AvatarGenerator.WriteSvg(destination, Name, variant, colors: [0x92A1C6, 0x146A7C, 0xF0AB3D, 0xC271B4, 0xC20D90]);

        // Assert
        await Assert.That(Decode(destination)).IsEqualTo(expected);
        await Assert.That(AvatarGenerator.GetSvg(Name, variant, colors: [0x92A1C6, 0x146A7C, 0xF0AB3D, 0xC271B4, 0xC20D90]))
            .IsEqualTo(expected);
    }

    [Test]
    [Arguments(0xFFFFFFu, "#000000")]
    [Arguments(0x000000u, "#FFFFFF")]
    [Arguments(0x80A000u, "#000000")]
    [Arguments(0x00A080u, "#FFFFFF")]
    public async Task WriteSvg_beam_keeps_the_face_visible_against_the_palette(uint background, string expectedFace)
    {
        // Arrange
        var destination = new ArrayBufferWriter<byte>();

        // Act
        AvatarGenerator.WriteSvg(destination, "Beam", AvatarVariant.Beam, colors: [background]);

        // Assert
        XElement face = XElement.Parse(Decode(destination)).Element(Svg + "g")!.Element(Svg + "g")!;
        string[] paints = face.Descendants().Attributes()
            .Where(attribute => attribute.Name.LocalName is "fill" or "stroke")
            .Select(attribute => attribute.Value).Where(value => value != "none").ToArray();
        await Assert.That(paints).IsNotEmpty().And.All(color => color == expectedFace);
    }

    [Test]
    [Arguments("fr-FR")]
    [Arguments("ar-SA")]
    public async Task WriteSvg_is_independent_of_the_callers_culture(string culture)
    {
        // Arrange
        CultureInfo previous = CultureInfo.CurrentCulture;
        AvatarVariant[] variants = Enum.GetValues<AvatarVariant>();
        string[] expected;
        string[] actual;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            expected = variants.Select(variant => Generate("Locale", variant)).ToArray();

            // Act
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            actual = variants.Select(variant => Generate("Locale", variant)).ToArray();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Assert
        foreach (var (First, Second) in actual.Zip(expected))
            await Assert.That(First).IsEqualTo(Second);
    }

    [Test]
    public async Task WriteSvg_concurrent_calls_produce_identical_documents()
    {
        // Arrange
        string expected = Generate("Concurrent", AvatarVariant.Sunset);

        // Act
        string[] outputs = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => Generate("Concurrent", AvatarVariant.Sunset))));

        // Assert
        await Assert.That(outputs).All(output => output == expected);
    }

    [Test]
    public async Task WriteSvg_appends_documents_without_overwriting_existing_bytes()
    {
        // Arrange
        var destination = new ArrayBufferWriter<byte>(1);
        destination.Write("prefix"u8);
        string first = Generate(Name, AvatarVariant.Pixel, includeTitle: true);
        string second = Generate("Second", AvatarVariant.Beam);

        // Act
        AvatarGenerator.WriteSvg(destination, Name, AvatarVariant.Pixel, includeTitle: true);
        int firstEnd = destination.WrittenCount;
        AvatarGenerator.WriteSvg(destination, "Second"u8, AvatarVariant.Beam);

        // Assert
        await Assert.That(Utf8.GetString(destination.WrittenSpan[..6])).IsEqualTo("prefix");
        await Assert.That(Utf8.GetString(destination.WrittenSpan[6..firstEnd])).IsEqualTo(first);
        await Assert.That(Utf8.GetString(destination.WrittenSpan[firstEnd..])).IsEqualTo(second);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WriteSvg_supports_a_destination_that_replaces_its_buffer_on_advance(bool utf8)
    {
        // Arrange
        var destination = new RotatingBufferWriter();
        string expected = Generate(Name, AvatarVariant.Sunset, includeTitle: true);

        // Act
        if (utf8)
            AvatarGenerator.WriteSvg(destination, Utf8.GetBytes(Name), AvatarVariant.Sunset, includeTitle: true);
        else
            AvatarGenerator.WriteSvg(destination, Name, AvatarVariant.Sunset, includeTitle: true);

        // Assert
        await Assert.That(Utf8.GetString(destination.WrittenSpan)).IsEqualTo(expected);
    }

    [Test]
    public async Task WriteSvg_leaves_a_pipe_open_and_buffered_until_the_caller_flushes(CancellationToken cancellationToken)
    {
        // Arrange
        await using var stream = new MemoryStream();
        PipeWriter destination = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        string expected = Generate(Name, AvatarVariant.Pixel, includeTitle: true);
        try
        {
            // Act
            AvatarGenerator.WriteSvg(destination, Utf8.GetBytes(Name), AvatarVariant.Pixel, includeTitle: true);
            long bufferedBytes = destination.UnflushedBytes;
            long lengthBeforeFlush = stream.Length;
            await destination.FlushAsync(cancellationToken);

            // Assert
            await Assert.That(bufferedBytes).IsGreaterThan(0);
            await Assert.That(lengthBeforeFlush).IsEqualTo(0);
            await Assert.That(stream.CanWrite).IsTrue();
            await Assert.That(Utf8.GetString(stream.ToArray())).IsEqualTo(expected);
        }
        finally
        {
            await destination.CompleteAsync();
        }
    }

    [Test]
    [MatrixDataSource]
    public async Task WriteSvg_rejects_nonpositive_sizes_without_writing(
        [Matrix(false, true)] bool utf8, [Matrix(0, -1)] int size)
    {
        // Arrange
        var destination = new ArrayBufferWriter<byte>();
        destination.Write("prefix"u8);

        // Act
        var exception = await Assert.That(() =>
        {
            if (utf8)
                AvatarGenerator.WriteSvg(destination, "Ada"u8, size: size);
            else
                AvatarGenerator.WriteSvg(destination, "Ada", size: size);
        }).Throws<ArgumentOutOfRangeException>();

        // Assert
        await Assert.That(exception!.ParamName).IsEqualTo("size");
        await Assert.That(Decode(destination)).IsEqualTo("prefix");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WriteSvg_rejects_an_unknown_variant_without_writing(bool utf8)
    {
        // Arrange
        var destination = new ArrayBufferWriter<byte>();
        destination.Write("prefix"u8);
        var variant = (AvatarVariant)999;

        // Act
        var exception = await Assert.That(() =>
        {
            if (utf8)
                AvatarGenerator.WriteSvg(destination, "Ada"u8, variant);
            else
                AvatarGenerator.WriteSvg(destination, "Ada", variant);
        }).Throws<ArgumentOutOfRangeException>();

        // Assert
        await Assert.That(exception!.ParamName).IsEqualTo("variant");
        await Assert.That(Decode(destination)).IsEqualTo("prefix");
    }

    [Test]
    [MatrixDataSource]
    public async Task WriteSvg_rejects_colors_outside_rgb_without_writing(
        [Matrix(false, true)] bool utf8, [Matrix(0x01000000u, uint.MaxValue)] uint color)
    {
        // Arrange
        var destination = new ArrayBufferWriter<byte>();
        destination.Write("prefix"u8);
        uint[] colors = [0x123456, color];

        // Act
        var exception = await Assert.That(() =>
        {
            if (utf8)
                AvatarGenerator.WriteSvg(destination, "Ada"u8, colors: colors);
            else
                AvatarGenerator.WriteSvg(destination, "Ada", colors: colors);
        }).Throws<ArgumentOutOfRangeException>();

        // Assert
        await Assert.That(exception!.ParamName).IsEqualTo("colors");
        await Assert.That(Decode(destination)).IsEqualTo("prefix");
    }

    [Test]
    [Arguments("\u0000", 0)]
    [Arguments("\u0001", 0)]
    [Arguments("\uD800", 0)]
    [Arguments("\uDC00", 0)]
    [Arguments("\uFFFE", 0)]
    [Arguments("\u0000", 4000)]
    [Arguments("\uD800", 4000)]
    public async Task WriteSvg_rejects_invalid_utf16_or_xml_names_without_writing(string suffix, int prefixLength)
    {
        // Arrange
        var destination = new ArrayBufferWriter<byte>();
        destination.Write("prefix"u8);
        string name = new string('x', prefixLength) + suffix;

        // Act
        var exception = await Assert.That(() => AvatarGenerator.WriteSvg(destination, name, includeTitle: true))
            .Throws<ArgumentException>();

        // Assert
        await Assert.That(exception!.ParamName).IsEqualTo("name");
        await Assert.That(Decode(destination)).IsEqualTo("prefix");
    }

    [Test]
    [Arguments(new byte[] { 0x80 })]
    [Arguments(new byte[] { 0xC0, 0xAF })]
    [Arguments(new byte[] { 0xE2, 0x82 })]
    [Arguments(new byte[] { 0xED, 0xA0, 0x80 })]
    [Arguments(new byte[] { 0xF4, 0x90, 0x80, 0x80 })]
    [Arguments(new byte[] { 0x00 })]
    [Arguments(new byte[] { 0x1F })]
    [Arguments(new byte[] { 0xEF, 0xBF, 0xBE })]
    public async Task WriteSvg_rejects_invalid_utf8_or_xml_names_without_writing(byte[] name)
    {
        // Arrange
        var destination = new ArrayBufferWriter<byte>();
        destination.Write("prefix"u8);

        // Act
        var exception = await Assert.That(() => AvatarGenerator.WriteSvg(destination, name, includeTitle: true))
            .Throws<ArgumentException>();

        // Assert
        await Assert.That(exception!.ParamName).IsEqualTo("name");
        await Assert.That(Decode(destination)).IsEqualTo("prefix");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WriteSvg_rejects_a_null_destination(bool utf8)
    {
        // Arrange
        IBufferWriter<byte> destination = null!;

        // Act
        var exception = await Assert.That(() =>
        {
            if (utf8)
                AvatarGenerator.WriteSvg(destination, "Ada"u8);
            else
                AvatarGenerator.WriteSvg(destination, "Ada");
        }).Throws<ArgumentNullException>();

        // Assert
        await Assert.That(exception!.ParamName).IsEqualTo("destination");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WriteSvg_propagates_destination_failures(bool utf8)
    {
        // Arrange
        var destination = new FailingBufferWriter();
        string name = new('山', 1000);

        // Act
        var exception = await Assert.That(() =>
        {
            if (utf8)
                AvatarGenerator.WriteSvg(destination, Utf8.GetBytes(name));
            else
                AvatarGenerator.WriteSvg(destination, name);
        }).Throws<IOException>();

        // Assert
        await Assert.That(exception!.Message).IsEqualTo("Destination failed.");
    }

    private static string Generate(
        ReadOnlySpan<char> name,
        AvatarVariant variant = AvatarVariant.Marble,
        int size = 40,
        bool square = false,
        bool includeTitle = false)
    {
        var destination = new ArrayBufferWriter<byte>();
        AvatarGenerator.WriteSvg(destination, name, variant, size, square, includeTitle);
        return Decode(destination);
    }

    private static string Decode(ArrayBufferWriter<byte> destination) => Utf8.GetString(destination.WrittenSpan);
}
