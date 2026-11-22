namespace Ambermoon;

/// <summary>Fork identity, separate from the upstream engine and game-data versions.</summary>
public static class EditionInfo
{
    public const string Name = "Ambermoon Experimental";
    // Keep in sync with the desktop project's Version property.
    public const string Version = "1.2.0-preview.1";
    public const string UpstreamBaseVersion = "1.15.1";
    public const string DisplayName = Name + " v" + Version;
    public const string BackportInfo = "Selected fixes: 1.15.2 and later";
    // Its version comparison and downloads belong to the upstream product.
    public const bool SupportsUpstreamPatcher = false;
}
