using System.Collections.Concurrent;
using System.Diagnostics;
using ComputerArchaeologist.Core.Ai;
using ComputerArchaeologist.Core.Analysis;
using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Options;
using ComputerArchaeologist.Core.Reports;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.Core.Orchestration;

/// <summary>What the user asked to explore.</summary>
public sealed record ArchaeologyRequest
{
    /// <summary>Selected roots. Empty means "the whole machine".</summary>
    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();

    /// <summary>When true the run deliberately skips AI and produces the degraded local report.</summary>
    public bool LocalOnly { get; init; }

    public string Language { get; init; } = "en-US";
}

public interface IArchaeologyPipeline
{
    /// <summary>
    /// Runs the seven stage pipeline. The returned session is always populated, even when the run was
    /// cancelled, so the caller can persist and display whatever was actually measured.
    /// </summary>
    Task<ArchaeologySession> RunAsync(
        ArchaeologyRequest request,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The scan pipeline: discovery, metadata, local scoring, AI analysis, fusion, report.
/// <para>
/// Design constraints honoured throughout: results are streamed and bounded (never one object per
/// discovered file), AI concurrency is capped, every file read is bounded, hashing happens only for the
/// finalists, and cancellation is cooperative rather than abortive.
/// </para>
/// </summary>
public sealed class ArchaeologyPipeline : IArchaeologyPipeline
{
    private readonly IFileDiscoveryService _discovery;
    private readonly IFileAnalysisService _analysis;
    private readonly InterestingnessCalculator _calculator;
    private readonly IAiAnalysisService _ai;
    private readonly IReportGenerationService _reports;
    private readonly ILocalizationService _localization;
    private readonly ArchaeologyOptions _options;
    private readonly DiscoveryOptions _discoveryOptions;
    private readonly OpenAiOptions _aiOptions;
    private readonly ILogger<ArchaeologyPipeline>? _logger;

    public ArchaeologyPipeline(
        IFileDiscoveryService discovery,
        IFileAnalysisService analysis,
        InterestingnessCalculator calculator,
        IAiAnalysisService ai,
        IReportGenerationService reports,
        ILocalizationService localization,
        ArchaeologyOptions options,
        DiscoveryOptions discoveryOptions,
        OpenAiOptions aiOptions,
        ILogger<ArchaeologyPipeline>? logger = null)
    {
        _discovery = discovery;
        _analysis = analysis;
        _calculator = calculator;
        _ai = ai;
        _reports = reports;
        _localization = localization;
        _options = options;
        _discoveryOptions = discoveryOptions;
        _aiOptions = aiOptions;
        _logger = logger;
    }

    public async Task<ArchaeologySession> RunAsync(
        ArchaeologyRequest request,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = new ArchaeologySession { Language = _localization.CurrentLanguage };
        var reporter = new ProgressReporter(progress);
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();

        try
        {
            // ---------------------------------------------------------- stage 1: prepare
            reporter.Stage(ScanStage.Preparing, 0, "Arch_Stage_Preparing");

            var discoveryRequest = new DiscoveryRequest
            {
                Roots = request.Roots,
                MaxFiles = _discoveryOptions.SafeMaxFiles,
                Budget = _discoveryOptions.Budget,
                Concurrency = _discoveryOptions.SafeConcurrency,
            };

            session.Roots = request.Roots;
            session.DiscoverySource = _discovery.SourceLabelKey;

            _logger?.LogInformation(
                "Starting an archaeology run over {Scope} with {Workers} walkers",
                request.Roots.Count == 0 ? "the whole machine" : string.Join(", ", request.Roots),
                discoveryRequest.SafeConcurrency);

            // ---------------------------------------------------------- stage 2: discovery
            reporter.Stage(ScanStage.DiscoveringFiles, 0, "Arch_Stage_Discovering");
            reporter.Detail(request.Roots.Count == 0
                ? _localization.Get("Arch_Scope_WholeMachine")
                : _localization.Format("Arch_Scope_Roots", request.Roots.Count));

            var maxCandidates = _options.SafeMaxCandidates;
            var extensionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var candidates = new PriorityQueue<FileMetadata, double>();
            long discovered = 0;
            var lastReport = Stopwatch.StartNew();
            var now = DateTimeOffset.UtcNow;

            await foreach (var file in _discovery.DiscoverAsync(discoveryRequest, cancellationToken).ConfigureAwait(false))
            {
                discovered++;
                var extension = file.Extension;
                extensionCounts[extension] = extensionCounts.TryGetValue(extension, out var count) ? count + 1 : 1;

                var cheap = _calculator.ComputeCheapScore(file, now);
                if (candidates.Count < maxCandidates)
                {
                    candidates.Enqueue(file, cheap);
                }
                else if (candidates.TryPeek(out _, out var worst) && cheap > worst)
                {
                    candidates.Dequeue();
                    candidates.Enqueue(file, cheap);
                }

                if (lastReport.ElapsedMilliseconds >= 200)
                {
                    lastReport.Restart();
                    reporter.Discovering(discovered, candidates.Count, file.FullPath);
                }
            }

            reporter.Discovering(discovered, candidates.Count, null);
            _logger?.LogInformation("Discovery finished: {Discovered} files inspected, {Candidates} kept", discovered, candidates.Count);

            if (discovered >= discoveryRequest.SafeMaxFiles)
            {
                warnings.Add("Warning_ScanLimitReached");
            }

            if (candidates.Count == 0)
            {
                reporter.Stage(ScanStage.Completed, 1, "Arch_Stage_Completed");
                return await FinishAsync(session, warnings, aiAvailable: false, aiError: null, stopwatch, cancellationToken)
                    .ConfigureAwait(false);
            }

            // ------------------------------------------------- stage 3: metadata + clusters
            cancellationToken.ThrowIfCancellationRequested();
            reporter.Stage(ScanStage.CollectingMetadata, 0, "Arch_Stage_Metadata");

            var candidateList = candidates.UnorderedItems.Select(i => i.Element).ToArray();
            var directories = candidateList.Select(c => c.DirectoryPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var clusters = await _analysis
                .BuildDirectoryClustersAsync(directories, cancellationToken)
                .ConfigureAwait(false);

            reporter.Stage(ScanStage.CollectingMetadata, 1, "Arch_Stage_Metadata");

            // ---------------------------------------------------------- stage 4: local score
            reporter.Stage(ScanStage.LocalScoring, 0, "Arch_Stage_LocalScore");

            var scoringContext = new ScoringContext
            {
                TotalFilesScanned = Math.Max(1, discovered),
                ExtensionCounts = extensionCounts,
                DirectoryClusters = clusters,
                Now = now,
            };

            var scored = new List<(FileMetadata File, LocalScoreBreakdown Breakdown)>(candidateList.Length);
            foreach (var file in candidateList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scored.Add((file, _calculator.ComputeLocalScore(file, scoringContext)));
            }

            scored.Sort((a, b) => b.Breakdown.LocalScore.CompareTo(a.Breakdown.LocalScore));

            var aiBudget = request.LocalOnly || !_ai.IsConfigured ? 0 : _options.SafeMaxAiAnalysis;
            var deep = scored
                .Take(Math.Max(1, Math.Max(aiBudget, _options.SafeMaxDiscoveries)))
                .ToArray();

            reporter.Stage(ScanStage.LocalScoring, 1, "Arch_Stage_LocalScore");

            // --------------------------------------------------- stage 5: AI semantic analysis
            var aiAvailable = false;
            string? aiError = null;
            var aiResults = new ConcurrentDictionary<string, AiAnalysisResult>(StringComparer.OrdinalIgnoreCase);
            var contexts = new ConcurrentDictionary<string, FileContext>(StringComparer.OrdinalIgnoreCase);
            var analyzedByAi = 0;

            var toAnalyze = aiBudget > 0 ? deep.Take(aiBudget).ToArray() : Array.Empty<(FileMetadata File, LocalScoreBreakdown Breakdown)>();

            // Even without AI the folder context is needed for the report.
            var contextTargets = toAnalyze.Length == 0 ? deep : toAnalyze;

            reporter.Stage(ScanStage.AiAnalysis, 0, "Arch_Stage_Context");
            await BuildContextsAsync(contextTargets, clusters, contexts, reporter, cancellationToken).ConfigureAwait(false);

            if (toAnalyze.Length > 0)
            {
                reporter.Stage(ScanStage.AiAnalysis, 0.05, "Arch_Stage_Ai");
                using var gate = new SemaphoreSlim(_aiOptions.SafeMaxConcurrency, _aiOptions.SafeMaxConcurrency);
                var completed = 0;
                var failures = 0;

                var tasks = toAnalyze.Select(async item =>
                {
                    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var context = contexts.TryGetValue(item.File.FullPath, out var c) ? c : new FileContext();
                        var result = await _ai.AnalyzeFileAsync(
                            new AiFileRequest
                            {
                                File = item.File,
                                Context = context,
                                Local = item.Breakdown,
                                Language = request.Language,
                                PrivacyMode = _options.PrivacyMode,
                            },
                            cancellationToken).ConfigureAwait(false);

                        aiResults[item.File.FullPath] = result;
                        if (result.Succeeded)
                        {
                            Interlocked.Increment(ref analyzedByAi);
                        }
                        else
                        {
                            Interlocked.Increment(ref failures);
                            aiError ??= result.Error;
                        }

                        var done = Interlocked.Increment(ref completed);
                        reporter.AiProgress(item.File.FullPath, done, toAnalyze.Length);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }).ToArray();

                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    warnings.Add("Warning_CancelledDuringAi");
                    _logger?.LogInformation("AI stage cancelled after {Done}/{Total} candidates", completed, toAnalyze.Length);
                }

                aiAvailable = analyzedByAi > 0;
                if (!aiAvailable)
                {
                    aiError ??= "Ai_Error_AllRequestsFailed";
                    warnings.Add("Report_AiUnavailable");
                }
                else if (failures > 0)
                {
                    warnings.Add("Report_AiPartial");
                }
            }
            else
            {
                warnings.Add(request.LocalOnly ? "Report_AiSkipped" : "Report_AiNotConfigured");
            }

            reporter.Stage(ScanStage.AiAnalysis, 1, "Arch_Stage_Ai");

            // ------------------------------------------------------- stage 6: fused scoring
            cancellationToken.ThrowIfCancellationRequested();
            reporter.Stage(ScanStage.FinalScoring, 0, "Arch_Stage_FinalScore");

            var minScore = _options.SafeMinInterestingness;
            var maxDiscoveries = _options.SafeMaxDiscoveries;
            var artifacts = new List<FileArtifact>(deep.Length);

            foreach (var (candidateFile, breakdown) in deep)
            {
                var file = candidateFile;

                if (_options.ComputeHashesForTopCandidates && artifacts.Count < maxDiscoveries)
                {
                    var hash = await _analysis.ComputeSha256Async(file.FullPath, cancellationToken).ConfigureAwait(false);
                    if (hash is not null)
                    {
                        file = CloneWithHash(file, hash);
                    }
                }

                aiResults.TryGetValue(file.FullPath, out var ai);
                var fused = _calculator.Fuse(breakdown, ai);
                if (fused.FinalScore < minScore)
                {
                    continue;
                }

                var context = contexts.TryGetValue(file.FullPath, out var ctx) ? ctx : new FileContext();
                artifacts.Add(BuildArtifact(file, fused, context, ai));
            }

            artifacts.Sort((a, b) => b.Score.FinalScore.CompareTo(a.Score.FinalScore));
            var final = artifacts.Take(maxDiscoveries).ToArray();
            for (var i = 0; i < final.Length; i++)
            {
                final[i].Rank = i + 1;
            }

            reporter.Stage(ScanStage.FinalScoring, 1, "Arch_Stage_FinalScore");

            session.FilesDiscovered = (int)Math.Min(int.MaxValue, discovered);
            session.Candidates = candidateList.Length;
            session.FilesAnalyzed = toAnalyze.Length;
            session.FilesAnalyzedByAi = analyzedByAi;
            session.FinalDiscoveries = final;

            // ------------------------------------------------------------- stage 7: report
            // The outcome flags are attached before the report is written so that warnings such as
            // "AI analysis was skipped" actually appear in the report the user reads.
            session.Warnings = warnings.Distinct().ToArray();
            session.AiAvailable = aiAvailable;
            session.AiError = aiError;

            reporter.Stage(ScanStage.GeneratingReport, 0, "Arch_Stage_Report");
            session.Report = await _reports
                .GenerateAsync(session, allowAiNarrative: !request.LocalOnly, cancellationToken)
                .ConfigureAwait(false);

            reporter.Stage(ScanStage.Completed, 1, "Arch_Stage_Completed");

            _logger?.LogInformation(
                "Archaeology run {SessionId} complete: {Discovered} discovered, {Candidates} candidates, {Analysed} AI, {Discoveries} discoveries in {Duration}",
                session.SessionId,
                discovered,
                candidateList.Length,
                analyzedByAi,
                final.Length,
                stopwatch.Elapsed);

            return await FinishAsync(session, warnings, aiAvailable, aiError, stopwatch, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Archaeology run {SessionId} cancelled by the user", session.SessionId);
            session.Cancelled = true;
            return await FinishAsync(session, new List<string> { "Warning_Cancelled" }, false, null, stopwatch, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Archaeology run {SessionId} failed", session.SessionId);
            session.Warnings = new[] { "Error_Unexpected" };
            reporter.Stage(ScanStage.Failed, 1, "Error_Unexpected", ex.Message);
            return await FinishAsync(session, new List<string> { "Error_Unexpected" }, false, ex.Message, stopwatch, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------ helpers

    private async Task<ArchaeologySession> FinishAsync(
        ArchaeologySession session,
        IReadOnlyList<string> warnings,
        bool aiAvailable,
        string? aiError,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        session.Warnings = warnings.Distinct().ToArray();
        session.AiAvailable = aiAvailable;
        session.AiError = aiError;
        session.EndTime = DateTimeOffset.UtcNow;

        if (session.Report is null)
        {
            try
            {
                session.Report = await _reports
                    .GenerateAsync(session, allowAiNarrative: false, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A report is nice to have; never let it mask the original outcome.
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "The fallback report could not be generated");
            }
        }

        _logger?.LogInformation("Run finished in {Duration}", stopwatch.Elapsed);
        return session;
    }

    private async Task BuildContextsAsync(
        IReadOnlyList<(FileMetadata File, LocalScoreBreakdown Breakdown)> targets,
        IReadOnlyDictionary<string, DirectoryClusterInfo> clusters,
        ConcurrentDictionary<string, FileContext> sink,
        ProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return;
        }

        using var gate = new SemaphoreSlim(_options.SafeFileReadConcurrency, _options.SafeFileReadConcurrency);
        var done = 0;

        var tasks = targets.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var cluster = clusters.TryGetValue(item.File.DirectoryPath ?? string.Empty, out var info)
                    ? info
                    : DirectoryClusterInfo.Empty;

                var context = await _analysis.BuildContextAsync(item.File, cluster, cancellationToken).ConfigureAwait(false);
                sink[item.File.FullPath] = context;

                var count = Interlocked.Increment(ref done);
                if (count % 25 == 0 || count == targets.Count)
                {
                    reporter.ContextProgress(item.File.FullPath, count, targets.Count);
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private FileArtifact BuildArtifact(
        FileMetadata file,
        InterestingnessResult fused,
        FileContext context,
        AiAnalysisResult? ai)
    {
        var category = ai is { Succeeded: true } && ai.Category != ArtifactCategories.Unknown
            ? ai.Category
            : FileTypeCatalog.CategoryOf(file.Extension);

        var evidence = new List<string>();
        foreach (var reason in fused.Local.Reasons.Where(r => r.Weighted > 0 || r.Value >= 40).Take(6))
        {
            evidence.Add(_localization.Format(reason.ExplanationKey, reason.Arguments?.ToArray() ?? Array.Empty<object?>()));
        }

        var summary = ai is { Succeeded: true, Summary.Length: > 0 }
            ? ai.Summary
            : evidence.Count > 0
                ? string.Join(" ", evidence.Take(2))
                : _localization.Get("Reason_LocalOnly");

        return new FileArtifact
        {
            File = file,
            Score = fused,
            Context = context,
            Category = category,
            Summary = summary,
            Evidence = evidence,
        };
    }

    private static FileMetadata CloneWithHash(FileMetadata file, string hash) => new()
    {
        FullPath = file.FullPath,
        FileName = file.FileName,
        Extension = file.Extension,
        DirectoryPath = file.DirectoryPath,
        SizeBytes = file.SizeBytes,
        CreatedUtc = file.CreatedUtc,
        ModifiedUtc = file.ModifiedUtc,
        AccessedUtc = file.AccessedUtc,
        Attributes = file.Attributes,
        Source = file.Source,
        Sha256 = hash,
    };

    /// <summary>Throttles high-frequency progress so the UI thread is never flooded.</summary>
    private sealed class ProgressReporter
    {
        private readonly IProgress<ScanProgress>? _progress;
        private readonly Stopwatch _throttle = Stopwatch.StartNew();
        private ScanStage _stage = ScanStage.Idle;

        public ProgressReporter(IProgress<ScanProgress>? progress) => _progress = progress;

        public void Stage(ScanStage stage, double stageProgress, string? messageKey, string? detail = null)
        {
            _stage = stage;
            _throttle.Restart();
            Report(new ScanProgress
            {
                Stage = stage,
                StageProgress = Math.Clamp(stageProgress, 0, 1),
                OverallProgress = Overall(stage, stageProgress),
                MessageKey = messageKey,
                Detail = detail,
            });
        }

        public void Detail(string detail) =>
            Report(new ScanProgress { Stage = _stage, MessageKey = null, Detail = detail });

        public void Discovering(long scanned, int candidates, string? current)
        {
            Report(new ScanProgress
            {
                Stage = ScanStage.DiscoveringFiles,
                StageProgress = -1,
                OverallProgress = Overall(ScanStage.DiscoveringFiles, 0.5),
                FilesScanned = scanned,
                FilesTotal = null,
                Candidates = candidates,
                CurrentItem = current,
            });
        }

        public void AiProgress(string current, int done, int total)
        {
            if (_throttle.ElapsedMilliseconds < 120 && done != total)
            {
                return;
            }

            _throttle.Restart();
            var stageProgress = total > 0 ? done / (double)total : 0;
            Report(new ScanProgress
            {
                Stage = ScanStage.AiAnalysis,
                StageProgress = stageProgress,
                OverallProgress = Overall(ScanStage.AiAnalysis, stageProgress),
                CurrentItem = current,
                AiAnalyzed = done,
                AiPlanned = total,
            });
        }

        public void ContextProgress(string current, int done, int total)
        {
            if (_throttle.ElapsedMilliseconds < 150 && done != total)
            {
                return;
            }

            _throttle.Restart();
            var stageProgress = total > 0 ? done / (double)total : 0;
            Report(new ScanProgress
            {
                Stage = ScanStage.CollectingMetadata,
                StageProgress = stageProgress,
                OverallProgress = Overall(ScanStage.CollectingMetadata, stageProgress),
                CurrentItem = current,
            });
        }

        private void Report(ScanProgress progress) => _progress?.Report(progress);

        private static double Overall(ScanStage stage, double stageProgress)
        {
            var (start, weight) = stage switch
            {
                ScanStage.Preparing => (0.0, 0.01),
                ScanStage.DiscoveringFiles => (0.01, 0.53),
                ScanStage.CollectingMetadata => (0.54, 0.10),
                ScanStage.LocalScoring => (0.64, 0.08),
                ScanStage.AiAnalysis => (0.72, 0.20),
                ScanStage.FinalScoring => (0.92, 0.04),
                ScanStage.GeneratingReport => (0.96, 0.04),
                ScanStage.Completed => (1.0, 0.0),
                _ => (0.0, 0.0),
            };

            return Math.Clamp(start + (weight * Math.Clamp(stageProgress, 0, 1)), 0, 1);
        }
    }
}
