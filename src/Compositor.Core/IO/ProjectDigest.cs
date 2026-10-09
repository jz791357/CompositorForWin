using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Compositor.IO;

/// A fingerprint of what a project package contains (upstream ProjectDigest.swift): the
/// manifest byte for byte, and each asset's name and size. A package that was only
/// touched — rewritten metadata, a permission change, the same bytes saved again — has
/// the same digest as before, so it is not treated as a change. Assets are not read:
/// hashing every image of a large project would hold each save for seconds.
public readonly struct ProjectDigest : IEquatable<ProjectDigest>
{
    public byte[] Value { get; }

    private ProjectDigest(byte[] value) => Value = value;

    public static ProjectDigest Compute(string path)
    {
        using var stream = new MemoryStream();
        var manifest = File.ReadAllBytes(Path.Combine(path, "manifest.json"));
        stream.Write(manifest);
        var images = Path.Combine(path, "images");
        var names = Directory.Exists(images)
            ? Directory.GetFiles(images).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()
            : [];
        foreach (var name in names)
        {
            var file = Path.Combine(images, name);
            var info = new FileInfo(file);
            if (info.Attributes.HasFlag(FileAttributes.Directory)) continue;
            stream.Write(Encoding.UTF8.GetBytes(name));
            stream.Write(BitConverter.GetBytes((ulong)info.Length));
        }
        return new ProjectDigest(SHA256.HashData(stream.ToArray()));
    }

    public bool Equals(ProjectDigest other) => Value.AsSpan().SequenceEqual(other.Value);
    public override bool Equals(object? obj) => obj is ProjectDigest other && Equals(other);
    public override int GetHashCode() => Value.Length > 0 ? BitConverter.ToInt32(Value, 0) : 0;
}
