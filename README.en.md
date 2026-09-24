# Computer Archaeologist · 计算机考古学家

> **Rediscover the forgotten corners of your computer.**
> 重新发现电脑里被遗忘的角落。

[简体中文](README.md) | **English**

**by [@Aperture-Revive](https://github.com/Aperture-Revive)** · [MIT licensed](LICENSE)

Computer Archaeologist is a Windows desktop application that searches *your own machine* for files
that carry history — abandoned projects, old documents, strange one-offs, game saves, student
homework, code experiments — scores how interesting each one is, and writes a readable
**archaeology report**.

It is a **read-only** tool. It never modifies, moves, renames or deletes a single file, and it depends
on **nothing outside itself**: no index server, no background service, no cloud.

---

## Table of contents

- [What it does](#what-it-does)
- [Requirements](#requirements)
- [Download](#download)
- [Building](#building)
- [Running](#running)
- [How file discovery works](#how-file-discovery-works)
- [Configuring the AI endpoint](#configuring-the-ai-endpoint)
- [Privacy: exactly what leaves your machine](#privacy-exactly-what-leaves-your-machine)
- [How the interestingness algorithm works](#how-the-interestingness-algorithm-works)
- [Architecture](#architecture)
- [Localisation](#localisation)
- [Tests](#tests)
- [Troubleshooting](#troubleshooting)
- [Known limitations](#known-limitations)
- [License](#license)
- [Author](#author)

---

## What it does

A run moves through seven stages:

| # | Stage | What happens |
|---|-------|--------------|
| 1 | Preparing the scan | Resolves the scope you chose (whole machine, a drive, or specific folders) and starts the walker pool. |
| 2 | Searching files | Walks the file system in parallel, pruning excluded folders before entering them and streaming results. |
| 3 | Collecting metadata | Streams the matches, reads folder structure for the surviving candidates. |
| 4 | Computing local interestingness | Eight explainable local features produce a `0–100` local score; a bounded top-N heap keeps memory flat. |
| 5 | AI analysis of candidates | Only the highest-scoring candidates are sent to the configured OpenAI-compatible endpoint, with capped concurrency and bounded payloads. |
| 6 | Combining scores | Local evidence and AI judgement are fused, with the AI weight scaled by the confidence the model reported. |
| 7 | Generating report | A full report is rendered **inside the app**, with sections, discovery cards, a timeline and a Markdown export. |

The result is a list of artifacts you can browse, sort, filter, inspect, preview and open.

---

## Requirements

| Component | Requirement |
|-----------|-------------|
| Operating system | Windows 10 version 1809 (build 17763) or later, including Windows 11 |
| Architecture | x64 (x86 and ARM64 are also configured) |
| .NET | **.NET 8** desktop runtime. The app is framework-dependent, so a .NET 8 runtime must be present. |
| Windows App SDK | **Windows App SDK 2.5.1** — bundled *inside* the build output (`WindowsAppSDKSelfContained`), so no separate Windows App Runtime installation or MSIX deployment is needed. |
| AI endpoint | Optional. Any OpenAI-compatible chat-completions endpoint. |

Nothing else is required. There is no index to install, no service to start and no configuration to
prepare before the first scan.

The application is built and tested with the .NET SDK 10.0.4xx targeting `net8.0-windows10.0.19041.0`,
and it runs on a machine that only has the .NET 8 runtime installed.

---

## Download

If you do not want to build it yourself, take the prebuilt package from the **Releases** page:

### [⬇ Download Computer-Archaeologist-1.0.0-win-x64.zip](https://github.com/Aperture-Revive/computer-archaeologist/releases/download/v1.0.0/Computer-Archaeologist-1.0.0-win-x64.zip)

| | |
|---|---|
| Platform | Windows 10 1809+ / Windows 11, x64 |
| Size | 42.0 MB (127 MB extracted) |
| SHA-256 | `ae5a2cf477ca679b9e234d61717b9c400a0cade523b6dd64ce092b22ad54c743` |
| Notes | [v1.0.0](https://github.com/Aperture-Revive/computer-archaeologist/releases/tag/v1.0.0) · [all releases](https://github.com/Aperture-Revive/computer-archaeologist/releases) |

Extract it anywhere and run `Computer Archaeologist.exe`. **No installer is required** — the Windows
App SDK runtime is bundled — and the only prerequisite is the
[.NET 8 desktop runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

The archive includes `READ-ME-FIRST.txt` with the quick start, privacy notes and licence. An identical
copy is kept in [`dist/`](dist/) inside the repository.

---

## Building

```powershell
git clone https://github.com/Aperture-Revive/computer-archaeologist.git
cd "Computer Archaeologist"

# Restore + build everything (app, core library, tests)
dotnet build "Computer Archaeologist.slnx" -c Debug -p:Platform=x64

# Run the test suite
dotnet test "Computer Archaeologist.Tests\Computer Archaeologist.Tests.csproj"

# Produce a release build
dotnet publish "Computer Archaeologist\Computer Archaeologist.csproj" `
  -c Release -p:Platform=x64 -r win-x64 --self-contained false -o publish\win-x64
```

The app targets `net8.0-windows10.0.19041.0` and needs the Windows SDK projections that come with the
.NET SDK on Windows. The solution is a `.slnx`; `dotnet build` and Visual Studio 2022+ both open it.

> **On Windows App SDK 2.x components**
> The project references the components it actually uses (`Base`, `Foundation`, `InteractiveExperiences`,
> `WinUI`, `DWrite`, `Runtime`) rather than the `Microsoft.WindowsAppSDK` metapackage. That keeps the
> unused AI / ML / Search / Widgets payload (`onnxruntime.dll`, `DirectML.dll`, `Microsoft.Windows.AI.*`
> and friends — roughly 60 MB) out of the build output entirely, taking the publish folder from 187 MB
> down to 128 MB.

---

## Running

```powershell
# From the build output
.\"Computer Archaeologist\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\Computer Archaeologist.exe"
```

or press <kbd>F5</kbd> in Visual Studio.

The first screen is **Home**, which restores the previous run on start-up: the last scan's metrics, the
most interesting artifact found so far and the run history are all there before you start anything new.
Press **Start Archaeology** and choose a scope. The dialog is pre-filled
from **Settings → Restrict discovery to**, and the answer is authoritative for that run:

- **Entire computer** (default)
- **Drive C:** / **Drive D:** / …
- **Selected folders** — pick one or more folders with the native folder picker

Everything the app writes lives under `%LOCALAPPDATA%\Computer Archaeologist\`:

```
settings.json          non-sensitive configuration only
credentials.bin        DPAPI fallback for the API key (only when the Credential Manager is unavailable)
Logs\                  structured daily logs (never contain the API key or file contents)
sessions\              one JSON file per run, plus a small summary file
exports\               Markdown exports you explicitly request
```

---

## How file discovery works

Computer Archaeologist walks the file system itself. There is **no external index and no third-party
service** involved, which removes an entire class of failure: nothing to install, nothing to keep
running, and no separate process whose index rebuild can stall the scan.

The walk is designed to be genuinely fast on a multi-million file machine:

- **Breadth first with a shared work queue.** A pool of walkers (6 by default, configurable 1–32) pulls
  directories from one queue, so every drive is busy instead of one thread crawling a deep tree.
- **Excluded folders are pruned before they are entered.** `node_modules`, `.git\objects`, `bin`,
  `obj`, package caches, browser caches, `AppData\Local\Temp`, `C:\Windows` and friends cost one
  attribute check rather than a filter pass over millions of entries.
- **Entries are read straight from the directory listing.** On Windows a directory listing already
  carries size, timestamps and attributes, so building the metadata record costs no extra file system
  round trip.
- **Results flow through a bounded channel.** Discovery can never outrun scoring, so memory stays flat
  regardless of scope; the pipeline only ever holds one file plus a fixed-size candidate heap.
- **Two hard budgets.** A maximum inspected-file count (400,000 by default) and a wall-clock budget
  (10 minutes by default) both apply, and cancellation is cooperative with no thread aborts.
- **Unreadable directories are skipped, never fatal.** A single permission error cannot end a scan.

Measured on a development machine with roughly 2.6 million indexed files, a whole-machine scan of
250,000 files across six drives takes **1 minute 44 seconds** with the default eight walkers
(≈2,400 files/second, about 21 seconds of CPU), with the interface responsive throughout.

### What is never touched

- Symbolic links, junctions and other reparse points are never followed, so a link loop cannot cause
  an infinite walk.
- Only directory listings and file metadata are read during discovery. Excluded paths are matched
  case-insensitively and on whole path segments, so `D:\Games\Save Games` is not mistaken for a build
  output folder.

---

## Configuring the AI endpoint

Open **Settings → OpenAI-compatible API**:

| Field | Meaning |
|-------|---------|
| API key | Stored in the **Windows Credential Manager** (target `ComputerArchaeologist/OpenAI`). If that API is unavailable the key is written to a DPAPI-encrypted file scoped to your user account. It is never written to `settings.json`, the repository, a log file or a crash dump. |
| Base URL | Defaults to `https://api.openai.com/v1`. Any OpenAI-compatible endpoint works. |
| Model | Defaults to `gpt-4o-mini`. Not hard-coded — change it freely. |

Press **Test connection** to issue a minimal request and get a human-readable verdict
(`Connected`, `The API key was rejected (401)`, `The endpoint or model was not found (404)`, …).
The key is never displayed back and never appears in an error message or log line.

**You do not need an API key to use the application.** Without one, the run produces a report built
entirely from local measurements.

---

## Privacy: exactly what leaves your machine

The default is **local-first**:

- File discovery, metadata collection, folder analysis and the whole interestingness algorithm run
  entirely on this machine.
- Only **candidates that survive local scoring** are ever sent to the AI endpoint, and the number is
  capped (`Settings → Maximum AI analyses per run`, default 300).

Three privacy modes are available in **Settings → Privacy**:

| Mode | What is sent |
|------|--------------|
| **Metadata only** *(default)* | File name, path, extension, size, timestamps, attributes, folder context (sibling count, nearby file names), and the local feature scores. **No file contents.** |
| **Smart content** | The above, plus a bounded excerpt (default 32 KB, hard-capped at 256 KB) read from text-like files only. |
| **Disable AI content analysis** | Nothing at all. No request is ever issued; the report is generated locally. |

Hard guarantees enforced in code:

- Large files are never uploaded. The excerpt reader stops at the configured limit; archives, audio,
  video and images are never read as raw payloads.
- Image analysis reads **header bytes only** (dimensions and format). Pixels are never transmitted.
  The in-app preview decodes a down-scaled thumbnail locally for display and nothing else.
- Prompt payloads wrap paths and file names in quotes and strip newlines, so a hostile file name
  cannot smuggle instructions into the prompt.
- The AI is instructed never to invent timestamps, contents, authorship or purpose, never to repeat
  secrets found in an excerpt, and to hedge when the evidence is thin.
- Reports separate **Facts** (measured) from **AI interpretation**, and the report header states
  whether the narrative was written by the model or generated locally.

The application **never**:

- requires administrator rights,
- writes to the registry,
- modifies, deletes, moves or renames your files,
- changes permissions,
- executes a file without an explicit click — and for `.exe`, `.bat`, `.cmd`, `.ps1`, `.vbs`, `.js`,
  `.scr` and friends it shows a confirmation dialog first.

---

## How the interestingness algorithm works

### Local features (all explainable, all `0–100`)

| Feature | Default weight | What it measures |
|---------|----------------|------------------|
| `Age` | 0.15 | Non-linear age curve. One month is almost nothing, one year is moderate, three years is high, ten years saturates. **Old is never automatically interesting**, and age is capped below 100 so it can never dominate alone. |
| `Rarity` | 0.15 | How often the extension occurs in the scanned set (logarithmic). A `.jpg` scores low, a `.blend` high, an unknown extension higher. |
| `PathContext` | 0.15 | Semantics of the folder: `Documents\School\Grade8\Physics` scores high, `AppData\Local\Temp` scores low. |
| `Filename` | 0.10 | Signals in the name (`old`, `backup`, `final`, `prototype`, `homework`, `diary`, years, `v2`, `draft` …) with word-boundary matching and a saturating curve, so one keyword cannot dominate. |
| `Cluster` | 0.15 | Does the folder look like a project? Sibling count plus markers (`README`, manifest files, several sources, an `assets` folder). |
| `PersonalArtifact` | 0.15 | Is this the kind of file a person makes (project, source, game save, design file, note) rather than the system? |
| `ModificationPattern` | 0.10 | Created long ago **and never touched again** = abandoned (high). Created long ago but edited yesterday = still alive (low). |
| `Unusualness` | 0.05 | Structural oddities: no extension, very long name, doubled extension, non-ASCII characters, odd punctuation, hidden/system attributes. |

```
LocalScore = Σ(feature × weight) / Σ(weights)      -- weights are normalised, so they need not sum to 1
```

### Fusion with the AI

```
EffectiveAiWeight = ConfiguredAiWeight × clamp(Confidence / 100, MinConfidenceScale, 1)

FinalScore = LocalScore × (1 − EffectiveAiWeight) + AiScore × EffectiveAiWeight
```

A model that answers with `confidence: 10` barely moves the result, no matter how extreme its
`interestingness` value. If the AI is unavailable or fails, `EffectiveAiWeight` becomes `0` and the
local evidence decides on its own. All weights live in
`Computer Archaeologist.Core/Options/InterestingnessWeightsOptions.cs` and are editable in
**Settings → Advanced → Interestingness weights**.

### Stage funnelling

```
File system walk (hundreds of thousands of files, pruned and streamed)
        │  exclusion policy + run scope
        ▼
streamed candidates ──► bounded top-N heap (MaxCandidates, default 5 000)
        │  full local scoring + folder clusters
        ▼
AI candidates (MaxAiAnalysis, default 300, concurrent ≤ 3)
        │  fusion, confidence scaling, MinInterestingness filter
        ▼
Discoveries (MaxDiscoveries, default 40) ──► report
```

Memory stays flat because the discovery stage never materialises the full result set: candidates go
into a fixed-size priority queue, SHA-256 is computed only for finalists, and every file read is
bounded.

---

### Layout notes for maintainers

- **`UniformGridLayout` measures every item with a fixed cell size.** `MinItemWidth`/`MinItemHeight`
  are therefore a *hard* height, not a hint: a caption that needs more room is clipped to a sliver.
  Metric tiles therefore use a generous `MinItemHeight` and a `*` row for the caption, and captions are
  `TextWrapping="NoWrap"` + `TextTrimming="CharacterEllipsis"` so they ellipsize instead of vanishing.
- **Nothing is two-way bound to a persisted option except the Settings page's own drafts**, and a
  background session bookmark only patches `app.lastSessionId` in the file. A control that reports
  "no selection" while its items are still loading can otherwise overwrite real settings.

## Architecture

```
Computer Archaeologist.slnx
├── Computer Archaeologist.Core/          net8.0 — no UI dependency, fully unit-testable
│   ├── Models/                           FileMetadata, FileArtifact, scores, session, report
│   ├── Options/                          every tunable value, including the weights
│   ├── Discovery/                        parallel file system walk, exclusion policy
│   ├── Analysis/                         local scoring, folder context, previews
│   ├── Ai/                               prompts, JSON validation, OpenAI-compatible client
│   ├── Reports/                          report assembly + Markdown export
│   ├── Security/                         Windows Credential Manager / DPAPI storage
│   ├── Settings/  Storage/  Infrastructure/  Localization/  Utilities/
│   └── Strings/{en-US,zh-CN}/Resources.resw
├── Computer Archaeologist/               net8.0-windows — WinUI 3 shell (MVVM)
│   ├── App.xaml(.cs)  MainWindow.xaml(.cs)
│   ├── Views/           Home, Archaeology, Discoveries, DiscoveryDetail, Reports, Settings
│   ├── ViewModels/      one per page + shared AppState
│   ├── Services/        navigation, theme, launcher, clipboard, UI dispatcher
│   ├── Converters/  Resources/Styles/  Infrastructure/
│   └── Package.appxmanifest  app.manifest
├── Computer Archaeologist.Tests/         net8.0 — xUnit
└── tools/                                string-catalogue generator, UI Automation smoke test
```

Design rules that are actually enforced:

- **MVVM.** Views contain no business logic. Code-behind is limited to navigation wiring and native
  dialogs (folder picker, confirmation dialogs).
- **Dependency injection.** `Microsoft.Extensions.DependencyInjection`; the object graph is assembled
  once in `ServiceCollectionExtensions`. `new LocalFileDiscoveryService()` appears nowhere in app code.
- **Services own the logic.** Discovery, scoring, AI, reporting, settings, storage and secure storage
  are all interfaces behind which the implementation can be replaced.
- **The UI never scans.** The pipeline drives discovery; the UI only observes progress and cancels.

---

## Localisation

Every user-visible string lives in a `.resw` catalogue — there is no hard-coded UI text in XAML.

- `Computer Archaeologist.Core/Strings/en-US/Resources.resw`
- `Computer Archaeologist.Core/Strings/zh-CN/Resources.resw`

Those exact files are used twice: embedded into `ComputerArchaeologist.Core` (read by
`ReswLocalizationService`, which also runs in unit tests and in the report generator) and linked into
the Windows resource pipeline as `PRIResource`. There is exactly one source of truth.

- XAML binds through `{Binding [Some_Key], Source={StaticResource Loc}}`; switching language raises a
  single `PropertyChanged("Item[]")` and the whole visual tree re-reads itself live, with no restart.
- **Appearance** offers Light / Dark / Follow system, applied to the window content so the built-in
  WinUI theme resources, Mica backdrop and the system accent colour all keep working.
- The AI prompt is told which language to answer in, so reports follow the selected language.
- `tools/generate-strings.py` regenerates both catalogues and fails if the two key sets diverge.

---

## Tests

```powershell
dotnet test "Computer Archaeologist.Tests\Computer Archaeologist.Tests.csproj"
```

**126 tests**, all green, covering:

| Area | What is verified |
|------|------------------|
| Discovery walk | Recursive walking; metadata populated from the directory listing; excluded folders pruned and never entered; run scope enforced; file budget respected; cancellation stops promptly; 40 parallel directories each visited exactly once with no duplicates; empty trees; missing roots; unreadable paths. |
| Interestingness | Non-linear age curve and its ceiling; rarity from real frequency data; path semantics; word-boundary filename matching; project-cluster detection; abandonment vs. still-active modification patterns; the weighted sum reconciles with the reported local score; the final score stays inside `0–100` for extreme inputs. |
| Confidence weighting | High-confidence AI moves the score, low-confidence AI barely does, and a missing/failed AI leaves the local score untouched. |
| Exclusion policy | Windows/Program Files/`$Recycle.Bin`/caches are excluded; `node_modules`, `.git`, `bin`, `obj` and package caches are excluded; **genuine user files — including game saves in a `\Games\...` tree — are never excluded**; user-configured exclusions and included roots work; matching is case- and separator-insensitive. |
| AI JSON parsing | Valid JSON, fenced JSON, prose-wrapped JSON, nested braces inside strings, numbers sent as strings, out-of-range values, missing fields, unknown categories, invalid actions, and unusable responses — none of which may throw. |
| Report generation | A report is always produced, with and without an AI narrative; failed AI analyses become warnings; section classification; the overview states only measured numbers; Markdown export. |
| Pipeline (integration) | Real end-to-end runs over a real directory tree using the real walker, real scoring and real report writing: discovery, ranking, hashing of finalists only, all seven stages reported, cancellation returning a partial session, an empty tree, and a missing root. |
| Run state | The previous run and the run history are restored on start-up; a dangling session pointer falls back to the newest archive; an empty archive leaves the state empty. |
| Localisation | Both catalogues load from the assembly, have identical key sets, contain no empty translations, and **every key referenced from XAML or from code exists** — checked by scanning the source tree. |
| Persistence | Settings round-trip, contain no credential field, a corrupt file is quarantined instead of blocking start-up, and bookmarking a run never rewrites the other settings; session save/load/list; secure-storage round-trip. |
| File analysis | UTF-8 decoding, binary/control-character rejection, PNG/GIF header parsing, ZIP listing, unreadable files returning `null`, SHA-256 correctness. |

`tools/uia-smoke.ps1` is a UI Automation harness that drives the real window: it starts a scan from
Home, answers the scope dialog, waits for the run, and asserts that the Discoveries, detail, Reports
and Settings pages actually rendered their content, including after the window is maximized.

---

## Troubleshooting

| Symptom | What to do |
|---------|-----------|
| The scan is slower than expected | Raise **Settings → File discovery → Parallel directory walkers** on an SSD; lower it on a mechanical drive or a network share. Narrowing the scope is always faster. |
| The scan stopped before finishing | Either the **maximum scanned files** or the **time budget** was reached; the report records a warning. Raise them in Settings and run again. |
| Nothing interesting was found | Check the scope, and lower **Settings → Minimum interestingness**. |
| `401` on Test Connection | The key was rejected. Clear it and paste a fresh one. |
| `404` on Test Connection | The Base URL or the model name is wrong. Check for a missing `/v1`. |
| `429` during a run | Rate limited. The client retries with exponential backoff (1 s, 2 s, 4 s, capped) and then continues with local scoring; the report notes the partial failure. |
| Nothing happens when opening a file | Executable types require a confirmation dialog; if the file no longer exists the action reports it instead of failing silently. |

Logs are written to `%LOCALAPPDATA%\Computer Archaeologist\Logs\`. They never contain your API key
(log lines are regex-scrubbed for `sk-…` and `Bearer …`) and never contain file contents.

---

## Known limitations

These are deliberate, documented boundaries rather than unfinished work:

1. **No external file index is used.** The application walks the file system itself, which keeps it
   dependency-free and makes the reported numbers trustworthy, at the cost of being slower than a
   pre-built index on very large drives.
2. **No image pixels are ever sent to the AI.** Vision upload is intentionally not implemented, to
   honour the "never upload whole files" rule. Image analysis is header/metadata based, and the
   in-app preview decodes a down-scaled thumbnail locally.
3. **PDF and Office documents are shown as metadata**, not parsed. Rendering those formats would pull
   in large dependencies and slow extraction; the metadata view still shows type, size, dates, path
   and folder context.
4. **Only ZIP archives are listed.** `.rar`, `.7z` and others are reported by format only.
5. **Bilingual UI is English + Simplified Chinese.** The catalogue generator makes adding a language a
   matter of adding one `.resw` file.
6. **The packaged (MSIX) path is configured but the default build is unpackaged and self-contained**,
   so the app starts straight from the build output without a Windows App Runtime installation.

---

## License

Released under the [MIT License](LICENSE).

```
MIT License

Copyright (c) 2026 @Aperture-Revive
```

You are free to use, modify, distribute and sell this software, provided the copyright notice and
licence text are retained. The software is provided "as is", without warranty of any kind.

---

## Author

**Computer Archaeologist** is designed and built by **[@Aperture-Revive](https://github.com/Aperture-Revive)**.

```
Computer Archaeologist · 计算机考古学家
© 2026 @Aperture-Revive
https://github.com/Aperture-Revive
```

Feedback, bug reports and pull requests are welcome.
