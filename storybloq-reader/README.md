# Storybloq Reader for Windows

A native Windows sidebar that watches [Storybloq](https://github.com/Storybloq/storybloq)
`.story/` directories and shows project state live while Claude Code or Codex works.

Storybloq gives AI coding sessions cross-session memory by keeping tickets, issues, notes,
lessons, roadmap phases, and session handovers in a `.story/` directory committed to git. Its
author ships a companion sidebar app for macOS only. This is a Windows equivalent — plus a
project switcher and a cross-project attention view, for people working across several
repositories at once.

**Read-only by design.** The reader never writes to `.story/`. An agent is often mid-write in
there, and a viewer that races it could corrupt the very state the agent depends on.

## Relationship to Storybloq

This is an independent, clean-room implementation of the documented `.story/` file convention.
It does not vendor, port, link against, or depend on Storybloq's source or its npm package. It
is not affiliated with or endorsed by the Storybloq project, and it carries no Storybloq
branding — the name describes what it reads.

Storybloq itself is distributed under PolyForm Shield 1.0.0, a source-available licence with a
non-compete clause. That licence governs Storybloq's own software; this reader implements a file
format rather than reusing their code. If you intend to distribute this, read that licence and
form your own view first.

## Installing

Grab `storybloq-reader-win-x64` from a green run of the **Storybloq Reader** workflow, unzip it,
and run `Storybloq.Reader.App.exe`. The build is unpackaged and self-contained — no installer, no
MSIX, no .NET runtime prerequisite.

Windows 11 has the required WebView-era runtime already; on Windows 10 you may need the
[Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).

## Using it

Click **Add project…** and pick a folder. Any folder *inside* the repository works — the reader
walks up to the nearest `.story/config.json` exactly as the Storybloq CLI does, and honours the
`STORYBLOQ_PROJECT_ROOT` override.

Panels: **Overview** (roadmap phases, progress, uncleared blockers), **Tickets**, **Issues**,
**Handovers**, **Notes & lessons**, and **Attention** — which rolls up unresolved critical and
high issues, blocked work, and uncleared roadmap blockers across *every* open project.

Panels hide themselves when `config.json` disables the corresponding feature. Projects added
before `storybloq init` has run are watched anyway and light up the moment `.story/` appears.

## Numbers that match the CLI

The aggregation rules deliberately mirror upstream's, because a sidebar that quietly disagrees
with `storybloq status` is worse than no sidebar:

- Counts are over **leaf** tickets. A ticket with active children is an umbrella and contributes
  nothing of its own — so a phase can be complete while its umbrella still reads "open".
- A ticket is blocked when any `blockedBy` entry points at a ticket that is neither complete nor
  deleted. An **unresolvable** reference counts as blocking.
- Phase status is rolled up from leaf tickets, ignoring the status stored on the umbrella.
- Records with `lifecycle: "deleted"` are excluded everywhere.

## Robustness

The failure modes that make a naive file watcher unusable are handled explicitly, because they
occur during ordinary agent work rather than in edge cases:

| Situation | Behaviour |
| --- | --- |
| `git checkout` rewrites many `.story/` files | Kernel buffer overflow is caught; a full reload is forced rather than silently dropping events |
| The CLI writes a file atomically | Reads retry briefly instead of failing on a sharing violation |
| `.story/` is deleted, or lives on a disconnected drive | Degrades to "unavailable" and recovers automatically via a liveness check |
| One ticket is corrupt, empty, or has no id | That file becomes a diagnostic; every healthy record still loads |
| A newer Storybloq adds a field or a status | Unknown fields round-trip; unknown enum values display verbatim rather than being mislabelled |
| `snapshots/` and `bus/` churn | Ignored entirely — gitignored runtime state with nothing to show |

## Layout

```
src/Storybloq.Reader.Core/       net8.0 — models, tolerant JSON, discovery, loading, aggregation
src/Storybloq.Reader.Platform/   net8.0 — filesystem watcher, sessions, workspace, presentation
src/Storybloq.Reader.App/        WinUI 3 — XAML, MVVM wiring, dispatcher marshalling
tests/                           xUnit, plus committed .story/ fixtures
```

`Storybloq.Reader.Platform` is Windows-tuned but targets plain `net8.0` on purpose: none of it
needs Windows-only APIs, so the watcher and presentation logic are covered by the Linux test job
as well as the Windows one, instead of being provable only on a runner nobody can debug
interactively. Everything the UI displays is formatted in `Presentation`, which leaves the WinUI
project holding nothing but layout and thread marshalling.

## Building

```bash
# Anywhere, including Linux and macOS — Core and Platform plus their tests
dotnet test tests/Storybloq.Reader.Core.Tests/Storybloq.Reader.Core.Tests.csproj
dotnet test tests/Storybloq.Reader.Platform.Tests/Storybloq.Reader.Platform.Tests.csproj

# Windows only — the WinUI app
dotnet build StorybloqReader.sln -c Release
dotnet publish src/Storybloq.Reader.App/Storybloq.Reader.App.csproj -c Release -r win-x64 --self-contained
```

## Not in this version

Writes of any kind, Bus and federation views, MSIX/Store packaging, toast notifications, and
ARM64 binaries (the runtime identifier is wired up; only x64 is built). Handover bodies render as
plain text — the WinUI markdown control still lives in Community Toolkit Labs, and a preview-feed
dependency was not worth the build fragility.
