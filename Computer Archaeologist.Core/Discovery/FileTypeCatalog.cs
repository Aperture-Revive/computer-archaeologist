using ComputerArchaeologist.Core.Models;

namespace ComputerArchaeologist.Core.Discovery;

/// <summary>Broad families used for scoring, filtering and report sections.</summary>
public enum FileFamily
{
    Unknown = 0,
    Document,
    Spreadsheet,
    Presentation,
    Text,
    Code,
    Project,
    Image,
    Audio,
    Video,
    Archive,
    GameSave,
    Configuration,
    Log,
    Database,
    Executable,
    Font,
    ThreeD,
    Design,
    Data,
}

/// <summary>
/// Static knowledge about extensions: what family a file belongs to, whether a human probably
/// authored it, and whether it is safe to read a small text excerpt from it.
/// </summary>
public static class FileTypeCatalog
{
    private static readonly Dictionary<string, FileFamily> Families = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> TextReadable = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> StronglyPersonal = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase);

    static FileTypeCatalog()
    {
        Add(FileFamily.Document, ".doc", ".docx", ".odt", ".rtf", ".pages", ".wpd", ".wps", ".abw", ".tex", ".epub", ".mobi");
        Add(FileFamily.Spreadsheet, ".xls", ".xlsx", ".ods", ".csv", ".tsv", ".numbers");
        Add(FileFamily.Presentation, ".ppt", ".pptx", ".odp", ".key");
        Add(FileFamily.Text, ".txt", ".md", ".markdown", ".rst", ".nfo", ".me", ".readme", ".asc", ".srt", ".vtt");
        Add(FileFamily.Log, ".log", ".log1", ".log2", ".out", ".err", ".trace");
        Add(FileFamily.Configuration, ".ini", ".cfg", ".conf", ".config", ".toml", ".properties", ".env", ".editorconfig", ".gitconfig", ".yaml", ".yml");
        Add(FileFamily.Data, ".json", ".xml", ".jsonl", ".ndjson", ".plist", ".reg", ".sqlite", ".realm");
        Add(FileFamily.Database, ".db", ".sqlite3", ".mdb", ".accdb", ".dbf", ".sqlitedb");
        Add(FileFamily.Image, ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".avif", ".ico", ".jfif", ".svg");
        Add(FileFamily.Image, ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".pef");
        Add(FileFamily.Audio, ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".wma", ".aiff", ".mid", ".midi", ".mod", ".xm", ".it", ".s3m");
        Add(FileFamily.Video, ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".mpg", ".mpeg", ".m4v", ".3gp");
        Add(FileFamily.Archive, ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".lzh", ".z", ".tgz", ".lz4", ".zst");
        Add(FileFamily.Font, ".ttf", ".otf", ".woff", ".woff2", ".fon", ".fnt");
        Add(FileFamily.ThreeD, ".fbx", ".stl", ".blend", ".3ds", ".dae", ".gltf", ".glb", ".ply", ".vox", ".mcfunction");
        Add(FileFamily.Design, ".psd", ".psb", ".ai", ".xcf", ".kra", ".aseprite", ".ase", ".procreate", ".sketch", ".fig", ".afdesign", ".afphoto");
        Add(FileFamily.Project, ".sln", ".csproj", ".vbproj", ".vcxproj", ".vcproj", ".fsproj", ".pbxproj", ".gradle", ".uproject", ".unity", ".unitypackage", ".godot", ".love", ".gmx", ".rpgproject", ".yyp", ".gms", ".project", ".xcworkspace", ".xcodeproj");
        Add(FileFamily.Executable, ".exe", ".com", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".scr", ".pif", ".cpl", ".jar", ".appx", ".msix", ".appxbundle");

        Add(FileFamily.Code,
            ".cs", ".vb", ".fs", ".cpp", ".cc", ".cxx", ".c", ".h", ".hpp", ".hh", ".hxx", ".inl",
            ".java", ".kt", ".kts", ".scala", ".groovy", ".py", ".pyw", ".rb", ".php", ".pl", ".pm",
            ".lua", ".rs", ".go", ".swift", ".m", ".mm", ".dart", ".ts", ".tsx", ".jsx", ".mjs", ".cjs",
            ".sh", ".bash", ".zsh", ".fish", ".psm1", ".psd1", ".sql", ".r", ".jl", ".nim", ".zig",
            ".asm", ".s", ".bas", ".pas", ".p", ".pp", ".f", ".f90", ".for", ".cob", ".ada", ".adb",
            ".tcl", ".awk", ".sed", ".vhd", ".v", ".sv", ".elm", ".ex", ".exs", ".erl", ".hrl",
            ".hs", ".lhs", ".ml", ".mli", ".clj", ".cljs", ".scm", ".rkt", ".pro", ".cmake", ".mk",
            ".glsl", ".hlsl", ".shader", ".cginc", ".vert", ".frag", ".gd", ".cshtml", ".razor", ".aspx", ".jsp", ".vue", ".svelte");

        Add(FileFamily.GameSave,
            ".sav", ".save", ".savx", ".slot", ".esm", ".ess", ".fos", ".wld", ".mcworld", ".mca", ".mcr",
            ".dat", ".bin", ".gam", ".gam2", ".sv2", ".sv5", ".rvdata", ".rvdata2", ".rxdata", ".lsd",
            ".sol", ".rpy", ".rmmzsave", ".psv", ".max", ".srm", ".st", ".chr", ".sps", ".dsv",
            ".sims3", ".sims4", ".world", ".pkx", ".bkp", ".state");

        TextReadable.UnionWith(new[]
        {
            ".txt", ".md", ".markdown", ".rst", ".log", ".ini", ".cfg", ".conf", ".config", ".toml",
            ".json", ".xml", ".yaml", ".yml", ".csv", ".tsv", ".sql", ".properties", ".env", ".nfo",
            ".bat", ".cmd", ".ps1", ".psm1", ".sh", ".bash", ".nfo", ".me", ".srt", ".vtt",
            ".cs", ".vb", ".fs", ".cpp", ".cc", ".cxx", ".c", ".h", ".hpp", ".hh", ".hxx", ".inl",
            ".java", ".kt", ".py", ".pyw", ".rb", ".php", ".pl", ".lua", ".rs", ".go", ".swift",
            ".ts", ".tsx", ".jsx", ".mjs", ".cjs", ".js", ".sql", ".r", ".jl", ".nim", ".zig",
            ".asm", ".s", ".bas", ".pas", ".f", ".f90", ".for", ".tcl", ".v", ".sv", ".vhd", ".elm",
            ".ex", ".exs", ".erl", ".hs", ".ml", ".clj", ".scm", ".rkt", ".cmake", ".mk", ".glsl",
            ".hlsl", ".shader", ".cshtml", ".razor", ".aspx", ".jsp", ".vue", ".svelte", ".gitignore",
            ".editorconfig", ".gitattributes", ".dockerignore", ".html", ".htm", ".css", ".scss", ".less",
            ".gd", ".love", ".info", ".readme", ".reg",
        });

        StronglyPersonal.UnionWith(new[]
        {
            ".sav", ".save", ".slot", ".diary", ".journal", ".notes", ".note", ".doc", ".docx",
            ".odt", ".rtf", ".pages", ".psd", ".aseprite", ".blend", ".kra", ".xcf", ".aup",
            ".flp", ".als", ".rpp", ".band", ".procreate", ".sketch",
        });

        ArchiveExtensions.UnionWith(new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz", ".zst", ".lz4" });
        ImageExtensions.UnionWith(new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".avif" });
        ExecutableExtensions.UnionWith(new[] { ".exe", ".com", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".scr", ".pif", ".cpl", ".jar", ".msix", ".appx" });
        SourceExtensions.UnionWith(GetExtensions(FileFamily.Code));
    }

    private static void Add(FileFamily family, params string[] extensions)
    {
        foreach (var extension in extensions)
        {
            Families[FileMetadata.NormalizeExtension(extension)] = family;
        }
    }

    private static IEnumerable<string> GetExtensions(FileFamily family) =>
        Families.Where(kv => kv.Value == family).Select(kv => kv.Key);

    public static FileFamily FamilyOf(string? extension) =>
        Families.TryGetValue(FileMetadata.NormalizeExtension(extension), out var family) ? family : FileFamily.Unknown;

    public static bool IsTextReadable(string? extension) =>
        TextReadable.Contains(FileMetadata.NormalizeExtension(extension));

    public static bool IsArchive(string? extension) =>
        ArchiveExtensions.Contains(FileMetadata.NormalizeExtension(extension));

    public static bool IsImage(string? extension) =>
        ImageExtensions.Contains(FileMetadata.NormalizeExtension(extension));

    public static bool IsSourceCode(string? extension) =>
        SourceExtensions.Contains(FileMetadata.NormalizeExtension(extension));

    /// <summary>Extensions Windows will happily execute; opening them needs explicit confirmation.</summary>
    public static bool IsExecutable(string? extension) =>
        ExecutableExtensions.Contains(FileMetadata.NormalizeExtension(extension));

    /// <summary>How likely it is that a human created this file on purpose (0..1).</summary>
    public static double PersonalLikelihood(string? extension) =>
        StronglyPersonal.Contains(FileMetadata.NormalizeExtension(extension)) ? 1.0 : 0.0;

    public static string MapToCategory(FileFamily family) => family switch
    {
        FileFamily.Code => ArtifactCategories.SourceCode,
        FileFamily.Project => ArtifactCategories.ForgottenProject,
        FileFamily.Document or FileFamily.Spreadsheet or FileFamily.Presentation or FileFamily.Text => ArtifactCategories.OldDocument,
        FileFamily.Image => ArtifactCategories.OldPhoto,
        FileFamily.GameSave => ArtifactCategories.GameSave,
        FileFamily.Configuration => ArtifactCategories.Configuration,
        FileFamily.Log => ArtifactCategories.LogFile,
        FileFamily.Archive => ArtifactCategories.Archive,
        FileFamily.Audio or FileFamily.Video => ArtifactCategories.Media,
        FileFamily.Design or FileFamily.ThreeD => ArtifactCategories.PersonalNote,
        _ => ArtifactCategories.Unknown,
    };

    /// <summary>Category resources are named <c>Category_&lt;key&gt;</c>; this maps the family straight to that key.</summary>
    public static string CategoryOf(string? extension) => MapToCategory(FamilyOf(extension));
}
