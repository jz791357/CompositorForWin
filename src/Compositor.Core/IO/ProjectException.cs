using Compositor.Document;

namespace Compositor.IO;

// Upstream ProjectError, mapped to an exception so validation code mirrors Swift's `throws`.

public enum ProjectError
{
    Invalid,
    Version,
    MissingImage,
    TooLarge,
    Encode,
}

public sealed class ProjectException(ProjectError error, int version = 0) : Exception
{
    public ProjectError Error { get; } = error;

    public override string Message => Error switch
    {
        ProjectError.Invalid =>
            "This is not a valid Compositor project, or its metadata is damaged.",
        ProjectError.Version =>
            $"This project uses format version {version}. This app supports versions {ProjectManifest.SupportedMin}–{ProjectManifest.Current}.",
        ProjectError.MissingImage =>
            "An image inside the project is missing or damaged. The current document has not been replaced.",
        ProjectError.TooLarge =>
            $"This project exceeds the supported canvas, layer, file-size, or {DocumentLimits.DocumentBudgetMegapixels}-megapixel document limit.",
        ProjectError.Encode =>
            "An image could not be saved. The previous project has not been replaced.",
        _ => base.Message ?? "",
    };
}
