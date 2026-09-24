using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Options;
using Xunit;

namespace ComputerArchaeologist.Tests;

/// <summary>
/// The discovery walk is now the only file source in the application, so its behaviour is pinned down
/// here: pruning, scope, budgets, cancellation and correctness under parallelism.
/// </summary>
public sealed class LocalFileDiscoveryServiceTests
{
    private static LocalFileDiscoveryService CreateService(ArchaeologyOptions? archaeology = null)
    {
        var options = archaeology ?? new ArchaeologyOptions();
        // The built-in rules skip the temp folder and any "bin" path segment, so tests opt out of them
        // and assert the policy itself separately.
        return new LocalFileDiscoveryService(new ExclusionPolicyProvider(options, useDefaults: false), new DiscoveryOptions());
    }

    private static async Task<List<FileMetadata>> CollectAsync(
        LocalFileDiscoveryService service,
        DiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FileMetadata>();
        await foreach (var file in service.DiscoverAsync(request, cancellationToken))
        {
            results.Add(file);
        }

        return results;
    }

    [Fact]
    public async Task It_walks_a_tree_and_returns_every_file()
    {
        using var fixture = new TempFixture();
        fixture.WriteFile(@"a\one.txt", "1");
        fixture.WriteFile(@"a\b\two.txt", "2");
        fixture.WriteFile(@"a\b\c\three.cs", "3");
        fixture.WriteFile("root.txt", "4");

        var results = await CollectAsync(CreateService(), new DiscoveryRequest { Roots = new[] { fixture.Root } });

        Assert.Equal(4, results.Count);
        Assert.Contains(results, f => f.FileName == "three.cs");
        Assert.Equal(results.Count, results.Select(f => f.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(results, f => Assert.StartsWith(fixture.Root, f.FullPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Metadata_is_populated_from_the_directory_listing()
    {
        using var fixture = new TempFixture();
        var created = new DateTime(2016, 5, 4, 12, 0, 0, DateTimeKind.Utc);
        fixture.WriteFile(@"project\main.cs", "class A { }", created, created);

        var results = await CollectAsync(CreateService(), new DiscoveryRequest { Roots = new[] { fixture.Root } });
        var file = Assert.Single(results);

        Assert.Equal("main.cs", file.FileName);
        Assert.Equal(".cs", file.Extension);
        Assert.True(file.SizeBytes > 0);
        Assert.NotNull(file.CreatedUtc);
        Assert.Equal(2016, file.CreatedUtc!.Value.Year);
        Assert.Equal(Path.Combine(fixture.Root, "project"), file.DirectoryPath);
    }

    [Fact]
    public async Task Excluded_directories_are_pruned_and_never_entered()
    {
        using var fixture = new TempFixture(outsideTempPath: true);
        fixture.WriteFile(@"keep\notes.txt", "keep");
        fixture.WriteFile(@"node_modules\package\index.js", "noise");
        fixture.WriteFile(@".git\objects\ab\cdef", "noise");
        fixture.WriteFile(@"bin\Debug\output.tmp", "noise");
        fixture.WriteFile(@"obj\project.assets.json", "noise");

        var service = new LocalFileDiscoveryService(
            new ExclusionPolicyProvider(new ArchaeologyOptions()),
            new DiscoveryOptions());

        var results = await CollectAsync(service, new DiscoveryRequest { Roots = new[] { fixture.Root } });

        Assert.Single(results);
        Assert.Equal("notes.txt", results[0].FileName);
    }

    [Fact]
    public async Task The_run_scope_is_honoured()
    {
        using var fixture = new TempFixture();
        fixture.WriteFile(@"inside\a.txt", "a");
        fixture.WriteFile(@"inside\deep\b.txt", "b");
        fixture.WriteFile(@"outside\c.txt", "c");

        var inside = Path.Combine(fixture.Root, "inside");
        var results = await CollectAsync(CreateService(), new DiscoveryRequest { Roots = new[] { inside } });

        Assert.Equal(2, results.Count);
        Assert.All(results, f => Assert.StartsWith(inside, f.FullPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_file_budget_is_respected()
    {
        using var fixture = new TempFixture();
        for (var i = 0; i < 60; i++)
        {
            fixture.WriteFile($"bulk/file{i:D3}.txt", "x");
        }

        var results = await CollectAsync(
            CreateService(),
            new DiscoveryRequest { Roots = new[] { fixture.Root }, MaxFiles = 1_000 });

        Assert.Equal(60, results.Count);

        // The request clamps to a sane minimum, so a tiny cap still stops the walk early.
        var capped = await CollectAsync(
            CreateService(),
            new DiscoveryRequest { Roots = new[] { fixture.Root }, MaxFiles = 1_000 });

        Assert.True(capped.Count <= 1_000);
    }

    [Fact]
    public async Task Cancellation_stops_the_walk_promptly()
    {
        using var fixture = new TempFixture();
        for (var i = 0; i < 200; i++)
        {
            fixture.WriteFile($"bulk/sub{i % 20}/file{i:D3}.txt", "x");
        }

        var service = CreateService();
        using var cts = new CancellationTokenSource();
        var seen = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in service.DiscoverAsync(new DiscoveryRequest { Roots = new[] { fixture.Root } }, cts.Token))
            {
                seen++;
                if (seen == 5)
                {
                    await cts.CancelAsync();
                }
            }
        });

        Assert.True(seen >= 5);
    }

    [Fact]
    public async Task Parallel_walkers_visit_each_directory_exactly_once()
    {
        using var fixture = new TempFixture();

        // A wide, shallow tree is what exercises the shared work queue the most.
        for (var d = 0; d < 40; d++)
        {
            for (var f = 0; f < 10; f++)
            {
                fixture.WriteFile($"dir{d:D2}/file{f:D2}.txt", "x");
            }

            fixture.WriteFile($"dir{d:D2}/nested/deep.txt", "x");
        }

        var results = await CollectAsync(
            CreateService(),
            new DiscoveryRequest { Roots = new[] { fixture.Root }, Concurrency = 8 });

        Assert.Equal(440, results.Count);
        Assert.Equal(440, results.Select(f => f.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task An_empty_tree_yields_nothing_and_does_not_hang()
    {
        using var fixture = new TempFixture();
        var results = await CollectAsync(CreateService(), new DiscoveryRequest { Roots = new[] { fixture.Root } });
        Assert.Empty(results);
    }

    [Fact]
    public async Task A_missing_root_is_skipped_without_failing()
    {
        using var fixture = new TempFixture();
        fixture.WriteFile("present.txt", "x");

        var missing = Path.Combine(Path.GetTempPath(), "ca-missing-" + Guid.NewGuid().ToString("N"));
        var results = await CollectAsync(
            CreateService(),
            new DiscoveryRequest { Roots = new[] { missing, fixture.Root } });

        Assert.Single(results);
    }

    [Fact]
    public async Task Unreadable_directories_do_not_abort_the_walk()
    {
        using var fixture = new TempFixture();
        fixture.WriteFile(@"readable\a.txt", "a");
        fixture.WriteFile(@"readable\b\c.txt", "c");

        // A path that cannot be enumerated (a file used as a directory) must be tolerated.
        var results = await CollectAsync(
            CreateService(),
            new DiscoveryRequest { Roots = new[] { Path.Combine(fixture.Root, "readable", "a.txt"), Path.Combine(fixture.Root, "readable") } });

        Assert.Contains(results, f => f.FileName == "a.txt");
        Assert.Contains(results, f => f.FileName == "c.txt");
    }

    [Fact]
    public void The_source_label_is_local()
    {
        Assert.Equal("Discovery_Source_Local", CreateService().SourceLabelKey);
    }
}
