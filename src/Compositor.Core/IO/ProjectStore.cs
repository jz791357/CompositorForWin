using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Compositor.Document;

namespace Compositor.IO;

// Upstream ProjectStore.swift: reading and validating .comp packages. Save (PNG encoding
// and atomic replace) lands with the M1.2 decoder; validation is complete from day one
// so cross-platform files are judged identically.

public static class ProjectStore
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        // C# PascalCase properties serialize to Swift Codable's camelCase keys
        // ("DocumentID" → "documentID"); decoding stays case-sensitive like Swift.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new CGPointJsonConverter(), new CGSizeJsonConverter(), new UuidJsonConverter(),
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new ColorRangeDictionaryConverterFactory(),
        },
    };

    public sealed record Header(string Format, int Version);

    public static ProjectSnapshot Load(string path)
    {
        if (!Directory.Exists(path)) throw new ProjectException(ProjectError.Invalid);
        var metadataPath = Path.Combine(path, "manifest.json");
        CheckFile(metadataPath, path, 4 * 1024 * 1024);
        var metadata = File.ReadAllBytes(metadataPath);

        Header header;
        ProjectManifest manifest;
        try
        {
            header = JsonSerializer.Deserialize<Header>(metadata, Json)
                ?? throw new ProjectException(ProjectError.Invalid);
            if (header.Format != "com.compositor.project") throw new ProjectException(ProjectError.Invalid);
            if (!ProjectManifest.Supports(header.Version))
                throw new ProjectException(ProjectError.Version, header.Version);
            manifest = JsonSerializer.Deserialize<ProjectManifest>(metadata, Json)
                ?? throw new ProjectException(ProjectError.Invalid);
        }
        catch (ProjectException) { throw; }
        catch (JsonException) { throw new ProjectException(ProjectError.Invalid); }

        Validate(manifest);

        var images = new Dictionary<Guid, ImportedImage>();
        var masks = new Dictionary<Guid, ImportedImage>();
        long pixels = 0, maskPixels = 0;
        foreach (var layer in manifest.Layers)
        {
            foreach (var isMask in new[] { false, true })
            {
                var filename = isMask ? layer.MaskFile : layer.ImageFile;
                if (filename is null) continue;
                var file = Path.Combine(Path.Combine(path, "images"), filename);
                CheckFile(file, path, 512 * 1024 * 1024);
                var asset = ReadAsset(file, layer.Name, isMask, ref pixels, ref maskPixels);
                if (isMask) masks[layer.Id] = asset; else images[layer.Id] = asset;
            }
        }
        return new ProjectSnapshot { Manifest = manifest, Images = images, Masks = masks };
    }

    /// Writes a package through a staging directory and swaps it in atomically — the
    /// Windows translation of upstream's coordinated `.atomic` FileWrapper write. A
    /// crash mid-save leaves the previous project in place (and a `.old-save` sibling
    /// only if it happened between the two moves).
    public static void Save(ProjectSnapshot snapshot, string path)
    {
        var manifest = snapshot.Manifest;
        Validate(manifest);

        var encoded = new Dictionary<string, byte[]>();
        long pixels = 0, maskPixels = 0;
        foreach (var layer in manifest.Layers)
        {
            foreach (var isMask in new[] { false, true })
            {
                var filename = isMask ? layer.MaskFile : layer.ImageFile;
                if (filename is null) continue;
                var asset = (isMask ? snapshot.Masks : snapshot.Images).GetValueOrDefault(layer.Id)
                    ?? throw new ProjectException(ProjectError.MissingImage);
                if (isMask && asset.Kind != PngPixelKind.Gray8) throw new ProjectException(ProjectError.Invalid);
                if (!isMask && asset.Kind != PngPixelKind.Rgba8) throw new ProjectException(ProjectError.Invalid);
                CheckSize(asset.Width, asset.Height, ref isMask ? ref maskPixels : ref pixels);
                try
                {
                    encoded[filename] = PngCodec.Encode(asset.Width, asset.Height, asset.Kind, asset.Pixels);
                }
                catch (PngException)
                {
                    throw new ProjectException(ProjectError.Encode);
                }
            }
        }

        var options = new JsonSerializerOptions(Json) { WriteIndented = true };
        var metadata = JsonSerializer.SerializeToUtf8Bytes(manifest, options);
        if (metadata.Length > 4 * 1024 * 1024) throw new ProjectException(ProjectError.TooLarge);

        var staging = path + ".tmp-save";
        var backup = path + ".old-save";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
        try
        {
            Directory.CreateDirectory(Path.Combine(staging, "images"));
            File.WriteAllBytes(Path.Combine(staging, "manifest.json"), metadata);
            foreach (var (filename, bytes) in encoded)
                File.WriteAllBytes(Path.Combine(staging, "images", filename), bytes);

            if (Directory.Exists(path)) Directory.Move(path, backup);
            try
            {
                Directory.Move(staging, path);
            }
            catch
            {
                if (Directory.Exists(backup) && !Directory.Exists(path)) Directory.Move(backup, path);
                throw;
            }
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static ImportedImage ReadAsset(string file, string name, bool isMask, ref long pixels, ref long maskPixels)
    {
        // Upstream reads CGImageSource properties first (dimensions, depth), runs the
        // budget check, and only then creates the image — oversized files are rejected
        // before any pixel work. The mask rule mirrors LayerMask.isValid.
        byte[] encoded;
        PngImage image;
        try
        {
            encoded = File.ReadAllBytes(file);
            if (!TryReadPngHeader(encoded, out var width, out var height, out var depth, out var color, out _))
                throw new ProjectException(ProjectError.MissingImage);
            if (depth > 8) throw new ProjectException(ProjectError.MissingImage);
            if (isMask && (color != 0 || depth != 8)) throw new ProjectException(ProjectError.Invalid);
            if (isMask) CheckSize(width, height, ref maskPixels); else CheckSize(width, height, ref pixels);
            image = PngCodec.Decode(encoded, isMask ? PngPixelKind.Gray8 : PngPixelKind.Rgba8);
        }
        catch (ProjectException) { throw; }
        catch (PngException)
        {
            throw new ProjectException(ProjectError.MissingImage);
        }
        var thumbnail = isMask
            ? GrayThumbnail(image.Width, image.Height, image.Pixels)
            : PngCodec.Thumbnail(image.Width, image.Height, image.Pixels);
        return new ImportedImage
        {
            Width = image.Width, Height = image.Height, Kind = image.Kind,
            Pixels = image.Pixels, Thumbnail = thumbnail, Encoded = encoded, Name = name,
        };
    }

    private static byte[] GrayThumbnail(int width, int height, byte[] gray)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = gray[i];
            rgba[i * 4 + 3] = 255;
        }
        return PngCodec.Thumbnail(width, height, rgba);
    }

    internal static bool TryReadPngHeader(byte[] bytes, out int width, out int height,
        out byte bitDepth, out byte colorType, out byte interlace)
    {
        width = height = 0; bitDepth = colorType = interlace = 0;
        if (bytes.Length < 33) return false;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!bytes.AsSpan(0, 8).SequenceEqual(signature)) return false;
        if (System.Text.Encoding.ASCII.GetString(bytes, 12, 4) != "IHDR") return false;
        width = BinaryPrimitivesReadInt32BE(bytes, 16);
        height = BinaryPrimitivesReadInt32BE(bytes, 20);
        bitDepth = bytes[24];
        colorType = bytes[25];
        interlace = bytes[28];
        return width > 0 && height > 0;
    }

    private static int BinaryPrimitivesReadInt32BE(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    public static void Validate(ProjectManifest manifest)
    {
        if (manifest.Format != "com.compositor.project") throw new ProjectException(ProjectError.Invalid);
        if (!ProjectManifest.Supports(manifest.Version))
            throw new ProjectException(ProjectError.Version, manifest.Version);
        if (manifest.ColorSpace != "sRGB") throw new ProjectException(ProjectError.Invalid);
        if (manifest.Resolution is { } resolution
            && (!double.IsFinite(resolution) || resolution is < 1 or > 9600))
            throw new ProjectException(ProjectError.Invalid);
        if (manifest.Width is < 1 or > DocumentLimits.MaxSide
            || manifest.Height is < 1 or > DocumentLimits.MaxSide
            || manifest.Layers.Length > 10_000)
            throw new ProjectException(ProjectError.TooLarge);

        foreach (var layer in manifest.Layers)
        {
            if (layer.Text is { } text)
            {
                // Per-letter colors arrived in version 10, per-letter faces in version 11.
                if (!text.IsValid
                    || (text.ColorRuns != null && manifest.Version < 10)
                    || (text.FontRuns != null && manifest.Version < 11)
                    || layer.ImageFile == null || layer.IsGroup == true || layer.Adjustment != null)
                    throw new ProjectException(ProjectError.Invalid);
            }
            if (layer.Adjustment is { } adjustment)
            {
                if (manifest.Version < 7 || layer.IsGroup == true || layer.ImageFile != null || !adjustment.IsValid)
                    throw new ProjectException(ProjectError.Invalid);
                if (adjustment.Kind is AdjustmentKind.GaussianBlur or AdjustmentKind.MotionBlur or AdjustmentKind.AddNoise
                    && manifest.Version < 9)
                    throw new ProjectException(ProjectError.Invalid);
            }
            // Layer masks arrived in version 4, folder masks in version 6.
            var maskVersion = layer.IsGroup == true ? 6 : 4;
            if (layer.MaskFile != null
                && (manifest.Version < maskVersion || layer.MaskFile != $"{layer.Id.ToString("D").ToUpperInvariant()}.mask.png"))
                throw new ProjectException(ProjectError.Invalid);
            if (layer.MaskEnabled != null && layer.MaskFile == null) throw new ProjectException(ProjectError.Invalid);
            if (layer.MaskPlacement is { } placement && (!placement.IsValid || layer.MaskFile == null))
                throw new ProjectException(ProjectError.Invalid);

            var opacity = layer.Opacity ?? 1;
            var blend = layer.BlendMode ?? LayerBlendMode.Normal;
            // Folders took an opacity of their own in version 8, which multiplies into what is
            // inside them; their blend mode is still pass-through, so it stays Normal.
            if (!double.IsFinite(opacity) || opacity is < 0 or > 1
                || (manifest.Version < 3 && !(opacity == 1 && blend == LayerBlendMode.Normal))
                || (layer.IsGroup == true && !(blend == LayerBlendMode.Normal
                    && (manifest.Version >= 8 || opacity == 1))))
                throw new ProjectException(ProjectError.Invalid);
        }

        LayerHierarchy.Validate(manifest.Layers);
        LiveMaskGraph.Validate(manifest.Layers);
        if (manifest.Version < 5 && manifest.Layers.Any(l => l.MaskSourceID != null))
            throw new ProjectException(ProjectError.Invalid);
        if (manifest.Version == 1 && manifest.Layers.Any(l => l.ParentID != null || l.IsGroup == true))
            throw new ProjectException(ProjectError.Invalid);

        var ids = new HashSet<Guid>();
        foreach (var layer in manifest.Layers)
        {
            var nameBytes = System.Text.Encoding.UTF8.GetByteCount(layer.Name);
            if (!ids.Add(layer.Id) || !layer.Transform.IsValid
                || string.IsNullOrWhiteSpace(layer.Name) || nameBytes > 16_384
                || (layer.ImageFile != null && layer.ImageFile != $"{layer.Id.ToString("D").ToUpperInvariant()}.png"))
                throw new ProjectException(ProjectError.Invalid);
        }
        if (manifest.ActiveLayerID is { } active && !ids.Contains(active))
            throw new ProjectException(ProjectError.Invalid);
        ValidateGuides(manifest);
    }

    private static void ValidateGuides(ProjectManifest manifest)
    {
        var guides = manifest.Guides ?? [];
        if (manifest.Version < 8)
        {
            if (guides.Length > 0) throw new ProjectException(ProjectError.Invalid);
            return;
        }
        if (guides.Length > 1_000) throw new ProjectException(ProjectError.TooLarge);
        var ids = new HashSet<Guid>();
        foreach (var guide in guides)
        {
            if (!ids.Add(guide.Id) || !double.IsFinite(guide.Position) || Math.Abs(guide.Position) > 1_000_000)
                throw new ProjectException(ProjectError.Invalid);
        }
    }

    private static void CheckSize(int width, int height, ref long used)
    {
        if (width is < 1 or > DocumentLimits.MaxSide || height is < 1 or > DocumentLimits.MaxSide
            || (long)width * height > DocumentLimits.DocumentPixelBudget - used)
            throw new ProjectException(ProjectError.TooLarge);
        used += (long)width * height;
    }

    private static void CheckFile(string file, string package, int maximumBytes)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(package))
            + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(file);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ProjectException(ProjectError.Invalid);
        var attributes = File.GetAttributes(file);
        if (attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new ProjectException(ProjectError.TooLarge);
        if (new FileInfo(file).Length > maximumBytes) throw new ProjectException(ProjectError.TooLarge);
    }
}

/// Swift encodes Dictionary<ColorRange, T> with the raw-value string keys; System.Text.Json
/// would write enum keys as numbers without this.
public sealed class ColorRangeDictionaryConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(Dictionary<,>)
        && typeToConvert.GetGenericArguments()[0] == typeof(ColorRange);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[1];
        return (JsonConverter)Activator.CreateInstance(
            typeof(ColorRangeDictionaryConverter<>).MakeGenericType(valueType))!;
    }
}

public sealed class ColorRangeDictionaryConverter<TValue> : JsonConverter<Dictionary<ColorRange, TValue>>
{
    private static string Key(ColorRange range) =>
        typeof(ColorRange).GetField(range.ToString())!
            .GetCustomAttributes(false).OfType<JsonPropertyNameAttribute>().FirstOrDefault()?.Name
        ?? range.ToString();

    public override Dictionary<ColorRange, TValue>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new Dictionary<ColorRange, TValue>();
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return result;
            var key = reader.GetString();
            reader.Read();
            if (key is null || !Enum.TryParse<ColorRange>(key, out var range)) throw new JsonException();
            result[range] = JsonSerializer.Deserialize<TValue>(ref reader, options)
                ?? throw new JsonException();
        }
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<ColorRange, TValue>? value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value != null)
        {
            foreach (var (range, item) in value.OrderBy(pair => (int)pair.Key))
            {
                writer.WritePropertyName(Key(range));
                JsonSerializer.Serialize(writer, item, options);
            }
        }
        writer.WriteEndObject();
    }
}
