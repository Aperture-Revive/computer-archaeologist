namespace ComputerArchaeologist.Core.Discovery;

/// <summary>
/// Decides which paths have no archaeological value. This is the single authority for exclusion: the
/// discovery walk prunes excluded folders before entering them, which also makes the policy directly
/// unit-testable.
/// </summary>
public sealed class PathExclusionPolicy
{
    /// <summary>Directory roots that never contain personal artifacts.</summary>
    public static readonly IReadOnlyList<string> DefaultExcludedRoots = new[]
    {
        @"c:\windows\",
        @"c:\program files\",
        @"c:\program files (x86)\",
        @"c:\programdata\microsoft\windows\",
        @"c:\programdata\package cache\",
        @"c:\$recycle.bin\",
        @"c:\$sysreset\",
        @"c:\recovery\",
        @"c:\system volume information\",
        @"c:\perflogs\",
        @"c:\msocache\",
        @"c:\config.msi\",
        @"c:\windowsapps\",
    };

    /// <summary>
    /// Path fragments that mark machine-generated noise. They are matched on whole segments so that
    /// "D:\Games\Save Games\obj" does not accidentally match the "obj" build folder rule below.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow: only fragments that unambiguously identify machine-generated state.
    /// A whole game installation is <b>not</b> excluded, because game saves are prime archaeology
    /// material; only the shader/cache sub-folders are.
    /// </remarks>
    public static readonly IReadOnlyList<string> DefaultExcludedFragments = new[]
    {
        @"\node_modules\",
        @"\.git\objects\",
        @"\.git\lfs\",
        @"\.svn\",
        @"\.hg\",
        @"\.nuget\packages\",
        @"\packages\localcache\",
        @"\npm-cache\",
        @"\node-gyp\",
        @"\.cache\",
        @"\.gradle\caches\",
        @"\.m2\repository\",
        @"\.cargo\registry\",
        @"\appdata\local\temp\",
        @"\windows\temp\",
        @"\appdata\local\microsoft\windows\inetcache\",
        @"\appdata\local\microsoft\windows\webcache\",
        @"\temporary internet files\",
        @"\code cache\",
        @"\appcache\",
        @"\cache_data\",
        @"\browsercache\",
        @"\gpucache\",
        @"\shadercache\",
        @"\shader_cache\",
        @"\d3dscache\",
        @"\cache2\",
        @"\startupcache\",
        @"\cachestorage\",
        @"\service worker\cachestorage\",
        @"\onedrivetemp\",
        @"\winsxs\",
        @"\driverstore\",
        @"\softwaredistribution\",
        @"\crashdumps\",
        @"\wer\reportarchive\",
        @"\wer\reportqueue\",
        @"\microsoft\windows\recent\",
        @"\microsoft\windows\sendto\",
        @"\microsoft\windows\fontcache\",
        @"\microsoft\crypto\",
        @"\microsoft\protect\",
        @"\microsoft\systemcertificates\",
        @"\microsoft\windows\caches\",
        @"\microsoft\windows\history\",
        @"\google\chrome\user data\default\cache\",
        @"\google\chrome\user data\default\code cache\",
        @"\tempstate\",
        @"\.vs\",
        @"\.idea\",
        @"\cmake-build-debug\",
        @"\cmake-build-release\",
        @"\build\intermediates\",
    };

    /// <summary>
    /// Build output folders, matched on segment boundaries only so that source trees survive.
    /// These are the entries the specification lists explicitly (<c>build</c>, <c>bin</c>, <c>obj</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultExcludedTrailingSegments = new[]
    {
        @"\bin",
        @"\obj",
        @"\bin\debug",
        @"\bin\release",
        @"\obj\debug",
        @"\obj\release",
        @"\target\debug",
        @"\target\release",
        @"\dist\debug",
    };

    /// <summary>Exact file names that are pure operating-system plumbing.</summary>
    public static readonly IReadOnlyList<string> DefaultExcludedFileNames = new[]
    {
        "pagefile.sys",
        "swapfile.sys",
        "hiberfil.sys",
        "dumpstack.log",
        "dumpstack.log.tmp",
        "ntuser.dat",
        "ntuser.dat.log1",
        "ntuser.dat.log2",
        "usrclass.dat",
        "desktop.ini",
        "thumbs.db",
        "ehthumbs.db",
        ".ds_store",
        "iconcache.db",
        "iconcache_16.db",
        "iconcache_32.db",
        "iconcache_48.db",
        "iconcache_96.db",
        "iconcache_256.db",
        "iconcache_idx.db",
        "bootstat.dat",
    };

    /// <summary>Extensions that are machine output rather than human artifacts.</summary>
    public static readonly IReadOnlyList<string> DefaultExcludedExtensions = new[]
    {
        ".tmp", ".temp", ".~tmp", ".part", ".partial", ".crdownload", ".download",
        ".pdb", ".ilk", ".exp", ".obj", ".lib",
        ".cache", ".lock", ".lck", ".pid",
        ".dmp", ".mdmp", ".hdmp",
        ".etl", ".evtx",
        ".mui", ".cat",
        ".sys", ".drv", ".ocx", ".tlb",
        ".pf", ".db-journal", ".db-wal", ".db-shm",
        ".chk",
        ".suo", ".ncb", ".sdf", ".opensdf",
        ".pyc", ".pyo", ".class", ".o", ".a", ".so", ".dylib",
        ".tsbuildinfo",
        ".thumbnails", ".msp", ".msi", ".cab",
        ".lnk", ".url",
    };

    private readonly HashSet<string> _excludedRoots;
    private readonly List<string> _excludedFragments;
    private readonly List<string> _excludedTrailingSegments;
    private readonly HashSet<string> _excludedFileNames;
    private readonly HashSet<string> _excludedExtensions;
    private readonly List<string> _includedRoots;

    public PathExclusionPolicy(
        IEnumerable<string>? extraPaths = null,
        IEnumerable<string>? extraExtensions = null,
        IEnumerable<string>? includedRoots = null,
        bool useDefaults = true)
    {
        _excludedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _excludedFragments = new List<string>();
        _excludedTrailingSegments = new List<string>();
        _excludedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _excludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _includedRoots = new List<string>();

        if (useDefaults)
        {
            foreach (var root in DefaultExcludedRoots)
            {
                _excludedRoots.Add(Normalize(root));
            }

            _excludedFragments.AddRange(DefaultExcludedFragments.Select(Normalize));
            _excludedTrailingSegments.AddRange(DefaultExcludedTrailingSegments.Select(Normalize));
            foreach (var name in DefaultExcludedFileNames)
            {
                _excludedFileNames.Add(name);
            }

            foreach (var extension in DefaultExcludedExtensions)
            {
                _excludedExtensions.Add(Models.FileMetadata.NormalizeExtension(extension));
            }

            // The machine's own scratch directory is always noise, whatever it is called.
            var temp = Normalize(Path.GetTempPath());
            if (!string.IsNullOrWhiteSpace(temp) && temp.Length > 3)
            {
                _excludedRoots.Add(temp);
            }
        }

        if (extraPaths is not null)
        {
            foreach (var path in extraPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                var normalized = Normalize(path);
                if (normalized.EndsWith('\\') || !Path.HasExtension(normalized))
                {
                    _excludedRoots.Add(normalized);
                }
                else
                {
                    _excludedFragments.Add(normalized);
                }
            }
        }

        if (extraExtensions is not null)
        {
            foreach (var extension in extraExtensions.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                _excludedExtensions.Add(Models.FileMetadata.NormalizeExtension(extension));
            }
        }

        if (includedRoots is not null)
        {
            foreach (var root in includedRoots.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                _includedRoots.Add(Normalize(root));
            }
        }
    }

    public IReadOnlyList<string> IncludedRoots => _includedRoots;

    public bool HasIncludedRoots => _includedRoots.Count > 0;

    /// <summary>True when the file must be ignored entirely.</summary>
    public bool IsExcluded(string fullPath, string? extension = null)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return true;
        }

        var path = Normalize(fullPath);

        if (HasIncludedRoots && !IsUnderIncludedRoot(path))
        {
            return true;
        }

        var fileName = Path.GetFileName(path);
        if (_excludedFileNames.Contains(fileName))
        {
            return true;
        }

        foreach (var root in _excludedRoots)
        {
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var probe = path.EndsWith('\\') ? path : path + "\\";
        foreach (var fragment in _excludedFragments)
        {
            if (probe.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var segment in _excludedTrailingSegments)
        {
            if (probe.Contains(segment + "\\", StringComparison.OrdinalIgnoreCase) ||
                probe.EndsWith(segment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var ext = Models.FileMetadata.NormalizeExtension(extension ?? Path.GetExtension(path));
        return _excludedExtensions.Contains(ext);
    }

    /// <summary>True when the path is inside one of the user-selected exploration roots.</summary>
    public bool IsUnderIncludedRoot(string fullPath)
    {
        if (!HasIncludedRoots)
        {
            return true;
        }

        var path = Normalize(fullPath);
        return _includedRoots.Any(root => path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string path)
    {
        var value = path.Trim().Replace('/', '\\');
        if (value.Length == 2 && value[1] == ':')
        {
            value += '\\';
        }

        return value.ToLowerInvariant();
    }
}
