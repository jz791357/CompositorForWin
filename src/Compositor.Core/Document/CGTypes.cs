using System.Text.Json;
using System.Text.Json.Serialization;

namespace Compositor.Document;

// CoreGraphics stand-ins (CGPoint, CGSize, CGRect, CGFloat-as-double) so the Document
// and IO layers stay free of WPF types and compile for both target frameworks. JSON
// mirrors Swift's synthesized Codable: {"x":…,"y":…}, {"width":…,"height":…},
// {"origin":…,"size":…} with every key required.

public readonly struct CGPoint(double x, double y) : IEquatable<CGPoint>
{
    public double X { get; init; } = x;
    public double Y { get; init; } = y;
    public static CGPoint Zero { get; } = new(0, 0);
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
    public bool Equals(CGPoint other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is CGPoint other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public static bool operator ==(CGPoint left, CGPoint right) => left.Equals(right);
    public static bool operator !=(CGPoint left, CGPoint right) => !left.Equals(right);
    public override string ToString() => $"{{{X}, {Y}}}";
}

public readonly struct CGSize(double width, double height) : IEquatable<CGSize>
{
    public double Width { get; init; } = width;
    public double Height { get; init; } = height;
    public static CGSize Zero { get; } = new(0, 0);
    public bool IsFinite => double.IsFinite(Width) && double.IsFinite(Height);
    public bool Equals(CGSize other) => Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is CGSize other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Width, Height);
    public static bool operator ==(CGSize left, CGSize right) => left.Equals(right);
    public static bool operator !=(CGSize left, CGSize right) => !left.Equals(right);
    public override string ToString() => $"{{{Width}, {Height}}}";
}

public readonly struct CGRect(CGPoint origin, CGSize size)
{
    public CGPoint Origin { get; init; } = origin;
    public CGSize Size { get; init; } = size;
    public double MinX => Origin.X;
    public double MinY => Origin.Y;
    public double MaxX => Origin.X + Size.Width;
    public double MaxY => Origin.Y + Size.Height;
    public double MidX => Origin.X + Size.Width / 2;
    public double MidY => Origin.Y + Size.Height / 2;
    public override string ToString() => $"{{{Origin}, {Size}}}";
}

public sealed class CGPointJsonConverter : JsonConverter<CGPoint>
{
    public override CGPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        double x = 0, y = 0;
        bool hasX = false, hasY = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            var key = reader.GetString();
            reader.Read();
            switch (key)
            {
                case "x": x = reader.GetDouble(); hasX = true; break;
                case "y": y = reader.GetDouble(); hasY = true; break;
                default: throw new JsonException();
            }
        }
        if (!hasX || !hasY) throw new JsonException();
        return new CGPoint(x, y);
    }
    public override void Write(Utf8JsonWriter writer, CGPoint value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteEndObject();
    }
}

public sealed class CGSizeJsonConverter : JsonConverter<CGSize>
{
    public override CGSize Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        double w = 0, h = 0;
        bool hasW = false, hasH = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            var key = reader.GetString();
            reader.Read();
            switch (key)
            {
                case "width": w = reader.GetDouble(); hasW = true; break;
                case "height": h = reader.GetDouble(); hasH = true; break;
                default: throw new JsonException();
            }
        }
        if (!hasW || !hasH) throw new JsonException();
        return new CGSize(w, h);
    }
    public override void Write(Utf8JsonWriter writer, CGSize value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("width", value.Width);
        writer.WriteNumber("height", value.Height);
        writer.WriteEndObject();
    }
}

// Swift's UUID.uuidString is upper case; .NET's Guid.ToString() is lower. Both parse
// either case, but the imageFile naming check ("<UUID>.png") compares against the
// upper-case spelling, so serialization writes upper case to stay identical.
public sealed class UuidJsonConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && Guid.TryParse(reader.GetString(), out var id) ? id
        : throw new JsonException();
    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("D").ToUpperInvariant());
}
