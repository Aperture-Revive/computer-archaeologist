using ComputerArchaeologist.Core.Options;

namespace ComputerArchaeologist.Core.Discovery;

/// <summary>
/// Hands out the current exclusion policy. Settings can change at any time, so the policy is rebuilt
/// on demand instead of being frozen at start-up, and a run-specific policy can be produced for the
/// scope the user chose in the archaeology dialog.
/// </summary>
public sealed class ExclusionPolicyProvider
{
    private readonly ArchaeologyOptions _options;
    private readonly bool _useDefaults;
    private readonly object _gate = new();
    private PathExclusionPolicy _current;

    /// <param name="options">Live options; the policy is rebuilt whenever settings change.</param>
    /// <param name="useDefaults">
    /// When false only the user-configured exclusions and included roots apply. Used by tests that
    /// need to observe discovery over a folder the built-in rules would normally skip.
    /// </param>
    public ExclusionPolicyProvider(ArchaeologyOptions options, bool useDefaults = true)
    {
        _options = options;
        _useDefaults = useDefaults;
        _current = Build(_options.IncludedRoots);
    }

    /// <summary>The policy built from the persistent settings.</summary>
    public PathExclusionPolicy Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Builds the policy for one run.
    /// <para>
    /// An empty <paramref name="roots"/> means the user chose "Entire computer", so the persistent
    /// include list is deliberately <b>not</b> applied: the dialog answer is authoritative for the run
    /// it belongs to.
    /// </para>
    /// </summary>
    public PathExclusionPolicy ForRoots(IReadOnlyList<string>? roots)
    {
        if (roots is null)
        {
            return Current;
        }

        var effective = roots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PathExclusionPolicy(_options.ExcludedPaths, _options.ExcludedExtensions, effective, _useDefaults);
    }

    /// <summary>Call after the exclusion lists or the default include list in settings change.</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            _current = Build(_options.IncludedRoots);
        }
    }

    private PathExclusionPolicy Build(IEnumerable<string>? includedRoots) =>
        new(_options.ExcludedPaths, _options.ExcludedExtensions, includedRoots, _useDefaults);
}
