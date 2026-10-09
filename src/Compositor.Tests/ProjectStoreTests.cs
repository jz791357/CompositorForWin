using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Compositor.Document;
using Compositor.IO;
using Xunit;

namespace Compositor.Tests;

// Contract tests for the .comp format layer, mirroring the rules in upstream
// ProjectStore.swift's validate() and the ProjectTests suite. The manifest JSON
// is the cross-platform contract: field names, strictness and defaults match
// Swift's synthesized Codable behavior.
public class ProjectStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "comp-tests-" + Guid.NewGuid().ToString("N"));

    private static string PngName(Guid id) => id.ToString("D").ToUpperInvariant() + ".png";
    private static string MaskName(Guid id) => id.ToString("D").ToUpperInvariant() + ".mask.png";

    private string NewPackage(ProjectManifest? manifest = null, string manifestJson = "{}")
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "images"));
        File.WriteAllText(Path.Combine(path, "manifest.json"), manifest != null
            ? JsonSerializer.Serialize(manifest, ProjectStore.Json)
            : manifestJson);
        return path;
    }

    private static ProjectLayerRecord Layer(
        Guid? id = null, string? imageFile = null, bool isGroup = false, Guid? parentID = null,
        double? opacity = null, LayerBlendMode? blendMode = null, string? maskFile = null,
        Guid? maskSourceID = null, LayerAdjustment? adjustment = null, LayerTextStyle? text = null,
        LayerTransform? transform = null, string? name = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name ?? "Layer",
        IsVisible = true,
        Transform = transform ?? new LayerTransform(new CGPoint(0, 0), new CGSize(64, 64)),
        ImageFile = imageFile,
        IsGroup = isGroup ? true : null,
        ParentID = parentID,
        Opacity = opacity,
        BlendMode = blendMode,
        MaskFile = maskFile,
        MaskSourceID = maskSourceID,
        Adjustment = adjustment,
        Text = text,
    };

    private static ProjectManifest Manifest(int version, ProjectLayerRecord[]? layers = null) => new()
    {
        Version = version,
        DocumentID = Guid.NewGuid(),
        Width = 512,
        Height = 512,
        Layers = layers ?? [Layer()],
    };

    private static ProjectError? ErrorFrom(Action act) => (Record.Exception(act) as ProjectException)?.Error;

    // --- JSON shape ---

    [Fact]
    public void Manifest_UsesSwiftCodableFieldNames()
    {
        var json = JsonSerializer.Serialize(Manifest(11), ProjectStore.Json);
        Assert.Contains("\"documentID\"", json);
        Assert.Contains("\"colorSpace\"", json);
        Assert.Contains("\"activeLayerID\"", json);

        var withGroup = Manifest(11, [Layer(isGroup: true, parentID: Guid.NewGuid())]);
        var groupJson = JsonSerializer.Serialize(withGroup, ProjectStore.Json);
        Assert.Contains("\"parentID\"", groupJson);
        Assert.Contains("\"isGroup\"", groupJson);
    }

    [Fact]
    public void Manifest_RoundTripsThroughJson()
    {
        var id = Guid.NewGuid();
        var manifest = Manifest(11,
            [Layer(id, imageFile: PngName(id), opacity: 0.5, blendMode: LayerBlendMode.ColorDodge)])
            with { ActiveLayerID = id, Guides = [new CanvasGuide(Guid.NewGuid(), CanvasGuideAxis.Vertical, 42.5)] };
        var json = JsonSerializer.Serialize(manifest, ProjectStore.Json);
        var back = JsonSerializer.Deserialize<ProjectManifest>(json, ProjectStore.Json);
        Assert.Equal(manifest.DocumentID, back!.DocumentID);
        Assert.Equal(0.5, back.Layers[0].Opacity);
        Assert.Equal(LayerBlendMode.ColorDodge, back.Layers[0].BlendMode);
        Assert.Equal(id, back.ActiveLayerID);
        Assert.Equal(42.5, back.Guides![0].Position);
        Assert.Equal(new CGSize(64, 64), back.Layers[0].Transform.Size);
    }

    [Fact]
    public void Manifest_RequiresEssentialKeys()
    {
        var path = NewPackage(manifestJson: """
            {"format":"com.compositor.project","version":11,"documentID":"00000000-0000-0000-0000-000000000001"}
            """);
        Assert.Equal(ProjectError.Invalid, Assert.Throws<ProjectException>(() => ProjectStore.Load(path)).Error);
    }

    [Fact]
    public void Dictionary_KeysUseRawValues()
    {
        var settings = new HueSaturationSettings(12, -3, 4)
            { Adjustments = new() { [ColorRange.Reds] = new RangeAdjustment(1, 2, 3) } };
        var json = JsonSerializer.Serialize(settings, ProjectStore.Json);
        Assert.Contains("\"Master\"", json);
        Assert.Contains("\"Reds\"", json);
        var back = JsonSerializer.Deserialize<HueSaturationSettings>(json, ProjectStore.Json);
        Assert.Equal(1, back!.Adjustments[ColorRange.Reds].Hue);
    }

    // --- loading ---

    [Fact]
    public void Load_MinimalProjectReadsImage()
    {
        var id = Guid.NewGuid();
        var package = NewPackage(Manifest(11, [Layer(id, imageFile: PngName(id))]));
        File.WriteAllBytes(Path.Combine(package, "images", PngName(id)), Png.Gray1x1());
        var snapshot = ProjectStore.Load(package);
        Assert.Single(snapshot.Images);
        Assert.Empty(snapshot.Masks);
        Assert.Equal(1, snapshot.Images[id].Width);
    }

    [Fact]
    public void Load_RejectsUnknownVersion()
    {
        var package = NewPackage(manifestJson: """{"format":"com.compositor.project","version":12}""");
        Assert.Equal(ProjectError.Version, Assert.Throws<ProjectException>(() => ProjectStore.Load(package)).Error);
    }

    [Fact]
    public void Load_RejectsWrongFormat() =>
        Assert.Equal(ProjectError.Invalid, Assert.Throws<ProjectException>(() => ProjectStore.Load(NewPackage())).Error);

    [Fact]
    public void Load_RejectsColoredMask()
    {
        var id = Guid.NewGuid();
        var package = NewPackage(Manifest(11, [Layer(id, imageFile: PngName(id), maskFile: MaskName(id))]));
        File.WriteAllBytes(Path.Combine(package, "images", PngName(id)), Png.Gray1x1());
        File.WriteAllBytes(Path.Combine(package, "images", MaskName(id)), Png.Rgba1x1());
        Assert.Equal(ProjectError.Invalid, Assert.Throws<ProjectException>(() => ProjectStore.Load(package)).Error);
    }

    [Fact]
    public void Load_RejectsImageOutsideBudget()
    {
        var id = Guid.NewGuid();
        var package = NewPackage(Manifest(11, [Layer(id, imageFile: PngName(id))]));
        File.WriteAllBytes(Path.Combine(package, "images", PngName(id)), Png.HeaderOnly(30_000, 30_000, colorType: 6));
        Assert.Equal(ProjectError.TooLarge, Assert.Throws<ProjectException>(() => ProjectStore.Load(package)).Error);
    }

    [Fact]
    public void Load_RejectsPathEscape()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "escape.png"), "not a png");
        var package = NewPackage(Manifest(11, [Layer(imageFile: "../../escape.png")]));
        Assert.Equal(ProjectError.Invalid, Assert.Throws<ProjectException>(() => ProjectStore.Load(package)).Error);
    }

    // --- validation rules, one per version gate ---

    [Fact]
    public void Validate_V1RejectsGroups()
    {
        var group = Layer(isGroup: true);
        var child = Layer(parentID: group.Id);
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(Manifest(1, [group, child]))));
    }

    [Fact]
    public void Validate_V2RejectsOpacity_V3AllowsIt()
    {
        var group = Layer(isGroup: true);
        var child = Layer(parentID: group.Id, opacity: 0.5);
        Assert.Null(ErrorFrom(() => ProjectStore.Validate(Manifest(3, [group, child]))));
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(Manifest(2, [group, child]))));
    }

    [Fact]
    public void Validate_FolderOpacityRequiresV8()
    {
        var group = Layer(isGroup: true, opacity: 0.5);
        Assert.Null(ErrorFrom(() => ProjectStore.Validate(Manifest(8, [group]))));
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(Manifest(7, [group]))));
    }

    [Fact]
    public void Validate_LayerMaskRequiresV4_FolderMaskRequiresV6()
    {
        var plain = Layer(maskFile: "placeholder");   // wrong name: also invalid, set properly below
        var named = Layer(id: plain.Id, maskFile: MaskName(plain.Id));
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(Manifest(4, [plain]))));
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(Manifest(3, [named]))));
        Assert.Null(ErrorFrom(() => ProjectStore.Validate(Manifest(4, [named]))));
        var folder = Layer(id: plain.Id, isGroup: true, maskFile: MaskName(plain.Id));
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(Manifest(5, [folder]))));
        Assert.Null(ErrorFrom(() => ProjectStore.Validate(Manifest(6, [folder]))));
    }

    [Fact]
    public void Validate_ImageFileNameMustMatchUUID()
    {
        Assert.Equal(ProjectError.Invalid,
            ErrorFrom(() => ProjectStore.Validate(Manifest(11, [Layer(imageFile: "wrong.png")]))));
    }

    [Fact]
    public void Validate_AdjustmentRequiresV7_NoiseKindsRequireV9()
    {
        var plain = Layer(adjustment: new LayerAdjustment { Kind = AdjustmentKind.Levels });
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(Manifest(6, [plain]))));
        Assert.Null(ErrorFrom(() => ProjectStore.Validate(Manifest(7, [plain]))));
        var noisy = Layer(adjustment: new LayerAdjustment { Kind = AdjustmentKind.AddNoise, NoiseAmount = 10 });
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(Manifest(8, [noisy]))));
        Assert.Null(ErrorFrom(() => ProjectStore.Validate(Manifest(9, [noisy]))));
    }

    [Fact]
    public void Validate_TextRunsRequireV10AndV11()
    {
        var colored = Layer(imageFile: "placeholder", text: new LayerTextStyle { ColorRuns = [new(0, 1, 1, 0, 0)] });
        colored = Layer(id: colored.Id, imageFile: PngName(colored.Id),
            text: new LayerTextStyle { ColorRuns = [new(0, 1, 1, 0, 0)] });
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(Manifest(9, [colored]))));
        Assert.Null(ErrorFrom(() => ProjectStore.Validate(Manifest(10, [colored]))));
        var fonted = Layer(id: colored.Id, imageFile: PngName(colored.Id),
            text: new LayerTextStyle { FontRuns = [new(0, 1, "Arial-BoldMT")] });
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(Manifest(10, [fonted]))));
        Assert.Null(ErrorFrom(() => ProjectStore.Validate(Manifest(11, [fonted]))));
    }

    [Fact]
    public void Validate_RejectsParentCycles()
    {
        var a = Layer(isGroup: true);
        var b = Layer(isGroup: true);
        var cycled = Manifest(11, [Layer(id: a.Id, isGroup: true, parentID: b.Id),
                                   Layer(id: b.Id, isGroup: true, parentID: a.Id)]);
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(cycled)));
    }

    [Fact]
    public void Validate_RejectsLiveMaskCycle()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var linked = Manifest(11, [Layer(id: a, maskSourceID: b), Layer(id: b, maskSourceID: a)]);
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(linked)));
    }

    [Fact]
    public void Validate_RejectsLiveMaskBeforeV5()
    {
        var source = Guid.NewGuid();
        var target = Layer(maskSourceID: source);
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(Manifest(4, [target]))));
        Assert.Null(ErrorFrom(() => ProjectStore.Validate(Manifest(5, [Layer(id: source), target]))));
    }

    [Fact]
    public void Validate_RejectsDuplicateIDsAndBadNames()
    {
        var id = Guid.NewGuid();
        Assert.Equal(ProjectError.Invalid,
            ErrorFrom(() => ProjectStore.Validate(Manifest(11, [Layer(id), Layer(id)]))));
        Assert.Equal(ProjectError.Invalid,
            ErrorFrom(() => ProjectStore.Validate(Manifest(11, [Layer(name: "   ")]))));
        Assert.Equal(ProjectError.Invalid,
            ErrorFrom(() => ProjectStore.Validate(Manifest(11, [Layer(name: new string('x', 16_385))]))));
    }

    [Fact]
    public void Validate_RejectsBadTransformAndMissingActiveLayer()
    {
        var bad = Layer(transform: new LayerTransform(new CGPoint(0, 0), new CGSize(0, 64)));
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(Manifest(11, [bad]))));
        Assert.Equal(ProjectError.Invalid,
            ErrorFrom(() => ProjectStore.Validate(Manifest(11) with { ActiveLayerID = Guid.NewGuid() })));
    }

    [Fact]
    public void Validate_GuidesRequireV8AndStayBounded()
    {
        var withGuides = Manifest(7) with { Guides = [new CanvasGuide(Guid.NewGuid(), CanvasGuideAxis.Horizontal, 10)] };
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(withGuides)));
        var ok = withGuides with { Version = 8 };
        Assert.Null(ErrorFrom(() => ProjectStore.Validate(ok)));
        var far = ok with { Guides = [new CanvasGuide(Guid.NewGuid(), CanvasGuideAxis.Horizontal, 2_000_000)] };
        Assert.Equal(ProjectError.Invalid, ErrorFrom(() => ProjectStore.Validate(far)));
    }

    [Fact]
    public void Validate_RejectsBadResolution() =>
        Assert.Equal(ProjectError.Invalid,
            ErrorFrom(() => ProjectStore.Validate(Manifest(11) with { Resolution = 0.5 })));

    // --- digest ---

    [Fact]
    public void Digest_IgnoresTouch_SeesContent()
    {
        var id = Guid.NewGuid();
        var package = NewPackage(Manifest(11, [Layer(id, imageFile: PngName(id))]));
        File.WriteAllBytes(Path.Combine(package, "images", PngName(id)), Png.Gray1x1());
        var first = ProjectDigest.Compute(package);
        File.SetLastWriteTimeUtc(Path.Combine(package, "images"), DateTime.UtcNow);   // metadata-only touch
        Assert.Equal(first, ProjectDigest.Compute(package));
        File.WriteAllBytes(Path.Combine(package, "images", PngName(id)), Png.Rgba1x1());  // different asset size
        Assert.NotEqual(first, ProjectDigest.Compute(package));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp cleanup is best-effort */ }
    }
}

/// PNG fixtures. Gray1x1/Rgba1x1 are complete valid PNGs (M1.2's decoder will read them);
/// HeaderOnly builds signature+IHDR without pixel data for size-limit tests.
internal static class Png
{
    public static byte[] Gray1x1() => Build(1, 1, bitDepth: 8, colorType: 0, pixel: [128]);
    public static byte[] Rgba1x1() => Build(1, 1, bitDepth: 8, colorType: 6, pixel: [10, 20, 30, 255]);
    public static byte[] HeaderOnly(int width, int height, int colorType) =>
        Build(width, height, 8, colorType, []);

    private static byte[] Build(int width, int height, int bitDepth, int colorType, byte[] pixel)
    {
        var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var ihdr = new byte[13];
        WriteInt32BE(ihdr, 0, width); WriteInt32BE(ihdr, 4, height);
        ihdr[8] = (byte)bitDepth; ihdr[9] = (byte)colorType;
        Chunk(output, "IHDR", ihdr);
        if (pixel.Length > 0)
        {
            var scanline = new byte[1 + pixel.Length];   // filter 0 + row
            pixel.CopyTo(scanline, 1);
            Chunk(output, "IDAT", ZlibWrap(scanline));
        }
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var header = new byte[8];
        WriteInt32BE(header, 0, data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(header, 4);
        output.Write(header);
        output.Write(data);
        var crc = Crc32(Encoding.ASCII.GetBytes(type).Concat(data).ToArray());
        var crcBytes = new byte[4];
        WriteInt32BE(crcBytes, 0, crc);
        output.Write(crcBytes);
    }

    private static byte[] ZlibWrap(byte[] data)
    {
        using var deflated = new MemoryStream();
        using (var deflate = new DeflateStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data);
        using var wrapped = new MemoryStream();
        wrapped.Write([0x78, 0x01]);
        wrapped.Write(deflated.ToArray());
        var adler = Adler32(data);
        var adlerBytes = new byte[4];
        WriteInt32BE(adlerBytes, 0, adler);
        wrapped.Write(adlerBytes);
        return wrapped.ToArray();
    }

    private static int Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320 & (crc & 1));
        }
        return unchecked((int)(crc ^ 0xFFFFFFFF));
    }

    private static int Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        return unchecked((int)((b << 16) | a));
    }

    private static void WriteInt32BE(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >>> 24);
        target[offset + 1] = (byte)(value >>> 16);
        target[offset + 2] = (byte)(value >>> 8);
        target[offset + 3] = (byte)value;
    }
}
