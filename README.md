# Rendlio Interop

MIT adapter packages bridging free .NET spreadsheet libraries to Rendlio Sheets rendering.
Thin glue over unmodified upstreams — not forks.

Rendlio is a Swiss association in formation; its profits are pledged to charities. The
Rendlio Sheets rendering engine is source-available under the Business Source License.
Everything in *this* repository is MIT.

## Status

Scaffolding. No adapter package has shipped from this repository yet. What is here is the
solution, the shared build settings, the CI workflow, and the three rules below that every
adapter landing here has to follow.

## Layout

| Path | Contents |
| --- | --- |
| `src/` | Adapter packages, one project per upstream library (`Rendlio.Interop.*`). |
| `tests/` | Test projects. |
| `Directory.Build.props` | Shared build settings: nullable enabled, warnings as errors. |
| `Directory.Packages.props` | Central package versions — where rule 3 is enforced. |
| `NuGet.config` | Restore resolves from nuget.org only, so an upstream is always the published package. |
| `.github/workflows/ci.yml` | Build and test on every push and pull request. |
| `SUPPORT.md` | What is in scope here, how a bug is routed, and what a response looks like. |

## Building

```sh
dotnet restore
dotnet build
dotnet test
```

The SDK version is pinned in `global.json`. Formatting is checked with
`dotnet format --verify-no-changes`, which CI runs too.

## The rules

Three rules govern this repository. They decide what may be written here, what may be
forked, and what may be depended on. Each is quoted verbatim, then explained.

### 1. Licence follows function

> moat code = BUSL; funnel code = MIT/Apache

Moat code — the rendering engine — is licensed BUSL and lives elsewhere. Funnel code —
adapters, samples, the tooling that helps people reach the engine — is MIT or Apache and
lives in repositories like this one.

The rule fixes the licence before the code is written, which is what makes it useful: no
file here is ever a candidate for relicensing, and a change that would have to be BUSL is
a change that does not belong in this repository.

### 2. Fork rules

> fork the dead, ask-with-blessing the sleepy (MiniWord, ClosedXML.Report co-maintenance offers), NEVER fork the living (ClosedXML core, MiniExcel - active + .NET Foundation)

An adapter takes its upstream as an ordinary, unmodified NuGet package. It does not vendor
it, patch it, or fork it.

- **Fork the dead.** A library with no maintainer and no releases can be forked, because
  there is nobody left to work with.
- **Ask, with blessing, for the sleepy.** A library that has gone quiet but is still owned
  gets an offer of co-maintenance first — the standing offers to MiniWord and
  ClosedXML.Report are exactly that. Improvements go upstream as contributions.
- **Never fork the living.** ClosedXML core and MiniExcel are actively maintained, and
  MiniExcel is a .NET Foundation project. A bug found through an adapter is reported
  upstream and, where we can, fixed there — not routed around in a private copy.

### 3. Version pinning

> ranges pin to certified upstream versions; widening requires a new certification run first

Each adapter's upstream dependency is a version range in `Directory.Packages.props`, and
that range covers the upstream versions that have been certified — no more. The certified
set is published at `/certified.json`.

Widening a range is therefore not a routine dependency bump. A newer upstream version sits
outside the range until a certification run puts it inside: the run comes first, the range
moves afterwards. Narrowing a range needs no run and can happen at any time.

## Contributing

- One adapter package per upstream library, with tests.
- Public members carry XML documentation. `GenerateDocumentationFile` is on for everything
  under `src/` and warnings are errors, so an undocumented public member fails the build.
- Run `dotnet format --verify-no-changes` before opening a pull request.
- Do not add a dependency to a packable project without saying why in the pull request — it
  becomes a transitive dependency for everyone who installs the package.

Before opening an issue or a pull request, read the [support and triage policy](SUPPORT.md).
It says what is in scope, how an upstream bug is told apart from an adapter bug, and what
you can expect afterwards.

## Licence

MIT — see [LICENSE](LICENSE). That covers every file in this repository, by rule 1.
