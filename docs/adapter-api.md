# Adapter API specification — `Rendlio.Interop.*`

**Status:** normative for this repository. **Fixed:** 2026-08-26 (INT-2).
**Binds:** INT-5, INT-6, INT-7 — the three Excel-side adapter packages. This text is their
requirement; whoever writes one should need no further API decision. Where it is silent, §12 says
so explicitly — silence is never an invitation to invent.

**Cited sources.** Two documents that are not published with this repository are cited by short
name throughout: the **API spec**, which fixes the rendering engine's own .NET library surface,
and the **report spec**, which fixes the compatibility report. Every rule this document takes from
either of them is restated here in full, so nothing below depends on reading them. The **charter**
is the product-level document that fixes the entry-point grammar and the safe-default rule; it
requires the exact signatures to be settled in this repository's own spec file, and this is that
file. The repository rules the charter hands down are published verbatim in
[README.md](../README.md).

## 0. Scope

Fixed here, for every package in this repository:

1. the `SaveToRendlioPdf` entry-point surface — correct-by-default, with an options escape hatch;
2. the return shape — the compatibility report is returned on every successful call, never swallowed;
3. the pre-save recalculation default for the ClosedXML-family adapters — on, overridable, never silently off;
4. the path/`Stream` overload set.

Out of scope: the adapters' internals, the certification programme, and any package other than the
three fixed in §5.

## 1. Precedence

Where two sources disagree, the higher row wins, and the disagreement is recorded in §12 rather
than resolved quietly:

| # | Source |
| --- | --- |
| 1 | The charter's two constitutional rules, and the public-surface rules |
| 2 | The engine's own specifications — the API spec and the report spec — as the shipped contract |
| 3 | The charter's normative sketch of the adapter surface |
| 4 | This document |

The charter sketch is the *starting shape*. This document may make it compile and may resolve a
conflict with row 2; it may not extend it. Anything contract-shaped that is missing is recorded in
§12 rather than invented here.

## 2. The engine surface the adapters bind to

From the API spec §3, quoted to the extent the adapters touch it:

```csharp
public sealed class RendlioWorkbook : IDisposable
{
    public static RendlioWorkbook Load(string path, LoadOptions? options = null,
        CancellationToken cancellationToken = default);
    public static RendlioWorkbook Load(Stream stream, LoadOptions? options = null,
        CancellationToken cancellationToken = default);

    public RenderResult RenderPdf(Stream output, PdfRenderOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed record RenderResult
{
    public required CompatReport Report { get; init; }
    public required int PageCount { get; init; }
    public RendlioStatus Status => Report.Result.Status;
    public int ExitCodeEquivalent => Report.Result.ExitCode;
}
```

**Adapters bind to `RendlioWorkbook.Load` + `RenderPdf`, never to the one-call static facade.**
Three reasons, all from the API spec: the `Load(Stream)` overload lets the ClosedXML family hand
the engine an in-memory workbook with no temporary file on disk; `LoadOptions` /
`PdfRenderOptions` split the options exactly where the API spec §5.4 says layout-affecting values
must be fixed (at load); and one primitive gives all four input/output shapes a single code path.

Consequences the adapters inherit and must not paper over:

- **Unsupported content never throws** (API spec §6 rule 1). A `Status` of `Warnings` or
  `UnsupportedContent` is a normal return.
- `InvalidInputException` (invalid workbook), `ResourceLimitException` (timeout/memory),
  `LicenseException` (invalid explicit licence), `OperationCanceledException`, and
  `RendlioException` (engine defect) propagate unchanged.
- A missing licence is not an error — it is evaluation watermark mode, reported in the report
  (API spec §6 rule 2). An adapter must not check for a licence, and must not warn about one.
- `Load(Stream)` buffers the stream fully, and the caller may dispose it as soon as `Load` returns
  (API spec §5.1). `RenderPdf` never seeks, closes, or disposes the output stream (§5.3).

## 3. The shared shape

Every package in this repository follows this shape. Divergence needs an amendment here first.

### 3.1 Naming grammar

| Element | Grammar | Examples |
| --- | --- | --- |
| Namespace | the package id | `Rendlio.Interop.ClosedXml` |
| Entry-point class | `<Upstream>Rendlio` | `MiniExcelRendlio` (the charter's, verbatim), `ClosedXmlRendlio` |
| Options record | `<Upstream>RendlioOptions` | `ClosedXmlRendlioOptions` |
| Method | `SaveToRendlioPdf` | the charter's, verbatim |

The options record is **named per package and lives in the package's own namespace**. It is not
shared: there is no `Rendlio.Interop.Core` package, and per-package names keep two adapters
installed side by side from colliding on a common type name. Six lines of duplication per package
is the price of every adapter being installable alone.

### 3.2 Entry points — the two-form options pattern

For each supported (input, output) shape, a package exposes exactly two methods:

```csharp
// Form A — defaults, or an options object built elsewhere.
public static RenderResult SaveToRendlioPdf(
    /* input */, /* output */,
    XRendlioOptions? options = null,
    CancellationToken cancellationToken = default);

// Form B — the escape hatch. The delegate receives the adapter's defaults.
public static RenderResult SaveToRendlioPdf(
    /* input */, /* output */,
    Func<XRendlioOptions, XRendlioOptions> configure,
    CancellationToken cancellationToken = default);
```

Rules:

1. **Synchronous, `CancellationToken` last.** No `*Async` surface, matching the API spec §1.2. The
   token is passed to `Load` and to `RenderPdf` unchanged.
2. **Form B is the documented escape hatch** — it is the charter's `opts => opts…` shape, and it is
   the form that survives a name collision (§3.3, footgun 2). Every `<example>` uses it.
3. **`Func`, not `Action`.** The API spec §1.4 makes every options record `init`-only, so a mutating
   `Action<T>` cannot compile. `Func<T, T>` with a `with` expression is the same call-site shape and
   does compile — verified, §11.
4. **The delegate starts from the adapter's defaults**, i.e. `configure(new XRendlioOptions())`. A
   caller who changes one field keeps every other default. A delegate returning `null` is an
   `ArgumentException` naming `configure`.
5. **Known nuance:** an explicit `null` as the third argument is `CS0121` ambiguous between forms A
   and B. It has no use — form A's default covers it — and the fix is to omit the argument or cast
   it. Verified, §11. Documented, not designed away: removing either form would cost the charter's
   lambda shape or configuration-built options.

### 3.3 The options record

```csharp
public sealed record XRendlioOptions
{
    // Present only in the ClosedXML family — see §4.
    public bool RecalculateBeforeSave { get; init; } = true;

    /// Engine load-time options, passed through unchanged.
    public LoadOptions Load { get; init; } = new();

    /// Engine PDF options, passed through unchanged.
    public PdfRenderOptions Pdf { get; init; } = new();
}
```

1. **`sealed record`, parameterless constructor, `init`-only properties, never positional** —
   API spec §1.4, so adding a field later stays a minor version.
2. **Defaults live in the property initializers, not in method bodies.** Therefore no call path can
   lose a default: `new XRendlioOptions { … }`, `o with { … }`, and the no-options overload all start
   from the same values. This is what makes "never silently off" (§4) structural rather than a
   matter of discipline in the implementation.
3. **The escape hatch is the engine's own options objects, held by reference, not a flattened
   copy.** The adapter re-declares no engine field, maps nothing, and defaults nothing on the
   engine's behalf, so it cannot drift from the API spec and cannot drop an option added there later.
4. **`LoadOptions` and `PdfRenderOptions`, not the flat `ConvertOptions`.** `ConvertOptions` carries
   `Format` and `Dpi`, which a method named `SaveToRendlioPdf` would have to reject or ignore; the
   split pair has no meaningless field, and `PdfA` — legitimately still PDF — stays reachable
   through `Pdf.PdfA`.
5. The adapter passes an instance, never `null`; `new LoadOptions()` is by the API spec §4 the same
   as omitting it.

Two footguns to document rather than hide:

- **Footgun 1 — the engine's defaults are the adapter's defaults.** The adapter overrides nothing on
  the engine side (§6), so a consumer reads the API spec's defaults, not a second set.
- **Footgun 2 — `LoadOptions` collides.** `ClosedXML.Excel.LoadOptions` exists (verified, §11), so a
  consumer with `using ClosedXML.Excel;` and `using <engine>;` cannot name `LoadOptions`
  unqualified. Form B never names it. The object form needs an alias
  (`using RendlioLoadOptions = <engine>.LoadOptions;`), and the package README must show it. Inside
  the adapter's own sources the collision does not arise while the engine namespace encloses the
  adapter's — which is part of what §12 item 1 leaves open.

### 3.4 The return value — the report is never swallowed

1. **Every entry point returns `RenderResult`** — the engine's own type, carrying `Report`,
   `PageCount` and `Status`. No `void`, no `bool`, no `out` parameter, no adapter-side wrapper type.
   `out` is rejected because `out _` is a swallow with syntax; a wrapper is rejected because it is a
   second shape that can drift from the report spec.
2. **The adapter returns the engine's report object unchanged.** It does not filter, reorder,
   summarise, re-serialize, or cache it, and it never inspects `Status` to decide anything. The JSON
   from `Report.ToJson()` / `Report.WriteJson(Stream)` after an adapter call is therefore
   byte-identical to the CLI's for the same input and options — API spec §1.6 parity, reachable
   through the adapter.
3. **`Warnings` and `UnsupportedContent` are returns, not throws** (API spec §6 rule 1). An adapter
   that threw on a fidelity gap would convert the honesty organ into an exception and lose the
   detail.
4. **On the exception paths there is no report, and the adapter must not fabricate one.** By the
   engine's contract a report exists only for a completed render; invalid input, resource limits and
   cancellation throw. An empty or synthesised report would be a number without a run.
5. **v1 writes the report nowhere.** No sidecar `.json`, no logging, no console output — the caller
   owns the returned object. Adding an opt-in output later is additive.

### 3.5 Argument validation and exceptions

Validated before any work starts, mirroring the API spec §4:

| Condition | Exception |
| --- | --- |
| extension target / input `null` | `ArgumentNullException` |
| output `Stream` `null`, or `configure` `null` | `ArgumentNullException` |
| `inputPath` / `outputPath` `null`, empty, or whitespace | `ArgumentException` |
| input `Stream` with `CanRead == false` | `ArgumentException` (API spec §5.1) |
| output `Stream` with `CanWrite == false` | `ArgumentException` (API spec §5.3) |
| `configure` returned `null` | `ArgumentException`, `paramName: "configure"` |

The extension methods null-check their `this` parameter explicitly: extension syntax on a null
reference does not throw on its own, so without the check the failure surfaces later and in the
wrong place. Adapters define **no new exception type** — every failure is either a BCL argument
exception from this table or an engine/upstream exception propagating unchanged.

### 3.6 Streams, files, cancellation, threading

1. **The adapter never disposes a stream the caller supplied**, input or output, and never seeks the
   output stream. Streams it creates itself, it disposes.
2. **Path output form:** opened `FileMode.Create` / `FileAccess.Write` / `FileShare.None`. If
   anything throws after the file is created, the adapter deletes the partially written file before
   letting the exception propagate — a truncated PDF that a later process can read is worse than no
   PDF, and the report spec §4 already deletes outputs on the resource-limit path.
3. **Stream output form:** partial content on an exception is unspecified and the caller's, exactly
   as the API spec §5.6 states. The adapter does not truncate or rewind it.
4. **Cancellation** is the engine's: the token goes to `Load` and `RenderPdf`, and
   `OperationCanceledException` propagates. The adapter polls nothing itself. On the path output
   form, rule 2 applies to cancellation too.
5. **Threading.** Entry points hold no static mutable state. They inherit the thread-safety of what
   the caller passes: the engine's handle is safe (API spec §5.5), an upstream workbook object
   generally is not, and the adapter promises nothing it cannot keep — the XML docs say the call is
   safe to use concurrently only with distinct workbook instances.
6. **Memory.** The ClosedXML family buffers the serialized workbook in memory once (§5.1), so peak
   footprint is roughly the `.xlsx` size on top of the caller's workbook. A temporary file would
   trade that for workbook content on disk and cleanup risk, and is rejected.
   `LoadOptions.MaxMemoryMb` caps engine-tracked allocation only (API spec §4.1) — it does not cover
   this buffer. Documented in `<remarks>`, not silently absorbed.

## 4. Pre-save recalculation — the ClosedXML-family default

The charter's safe-default rule makes this the canonical correct-by-default behaviour: the engine is
a read-only, no-recalculation renderer, so whatever cached formula values the upstream workbook
carries are what gets rendered. A workbook built or mutated in memory can carry stale ones. The
adapter's job is to make that footgun unreachable by accident.

**The default is on.** `RecalculateBeforeSave` is `true` in the options record's property
initializer, so every call path starts from it (§3.3 rule 2).

**What it does.** Immediately before serializing the workbook for the engine, the adapter calls
`IXLWorkbook.RecalculateAllFormulas()` on it — present in ClosedXML 0.102.2 through 0.105.0
(verified, §11).

**Unconditional, by design:**

- It does not consult `IXLWorkbook.CalculateMode`. A workbook left in manual calculation mode is
  precisely the stale-value case this default exists for, so reading that property could only
  disable the fix exactly when it is needed. The adapter neither reads nor writes it.
- It does not depend on upstream dirty-tracking, which is why `RecalculateAllFormulas()` is used
  rather than the `evaluateFormulae` parameter of the three-argument `SaveAs` overload. That
  overload would also drag in the `validate` parameter — schema validation nobody asked for — and it
  varies across the range, while `RecalculateAllFormulas()` and `SaveAs(Stream)` are present
  throughout it.
- It does not scan for formulas to "optimise" the call away. What work is needed is upstream's
  decision.

**Never silently off.** There is exactly one way to turn it off: set `RecalculateBeforeSave = false`.
The adapter must not disable it based on workbook size, elapsed time, a previous failure, or the
absence of formulas, and must not re-enable it when the caller turned it off.

**Failure propagates.** If upstream recalculation throws, the exception propagates unchanged and
nothing is rendered. Catching it and rendering the stale values would be a silent approximation —
forbidden by the charter's safe-default rule — and the adapter cannot write into the engine's report
to disclose it (§12 item 3). Upstream is not contractually bound to a particular exception type, and
the adapter does not wrap it into one: a wrapper would imply a stability promise this repository
cannot make over an unmodified upstream.

**It has an observable side effect.** The caller's workbook has its cached values refreshed — the
adapter mutates the object it was handed. That is the point of the default, and it must be stated in
`<remarks>` on every affected method, in the XML doc of `RecalculateBeforeSave`, and in the package
README.

**Two different recalculations. The adapter changes one and never the other:**

| | Where | Default | Who sets it |
| --- | --- | --- | --- |
| `RecalculateBeforeSave` | upstream, before the workbook reaches the engine | **`true`** — the adapter's only behavioural default | adapter, overridable by the caller |
| `LoadOptions.Recalculate` | engine-side, its own `strict-v1` recalculation mode | `Off` — the engine's default, untouched | caller only, through `Load` |

The adapter must never set `LoadOptions.Recalculate`. Turning engine recalculation on for the caller
would change rendered values behind an option the caller never chose; leaving it at the engine
default keeps CLI parity exact.

**MiniExcel family:** `RecalculateBeforeSave` is **absent** from `MiniExcelRendlioOptions`, not
present-and-false. There is no upstream workbook object in that call — the input is an already
written file or stream — so there is nothing to recalculate, and a field that read `false` would
suggest a default had been silently turned off. Its absence is documented in the record's XML doc.

## 5. Per-package surfaces

### 5.1 `Rendlio.Interop.ClosedXml`

Upstream: ClosedXML (unmodified). Extension target: `ClosedXML.Excel.IXLWorkbook` — the interface, so
`XLWorkbook` and any other implementation both work.

```csharp
namespace Rendlio.Interop.ClosedXml;

public sealed record ClosedXmlRendlioOptions
{
    public bool RecalculateBeforeSave { get; init; } = true;
    public LoadOptions Load { get; init; } = new();
    public PdfRenderOptions Pdf { get; init; } = new();
}

public static class ClosedXmlRendlio
{
    public static RenderResult SaveToRendlioPdf(this IXLWorkbook workbook, string outputPath,
        ClosedXmlRendlioOptions? options = null, CancellationToken cancellationToken = default);

    public static RenderResult SaveToRendlioPdf(this IXLWorkbook workbook, string outputPath,
        Func<ClosedXmlRendlioOptions, ClosedXmlRendlioOptions> configure,
        CancellationToken cancellationToken = default);

    public static RenderResult SaveToRendlioPdf(this IXLWorkbook workbook, Stream output,
        ClosedXmlRendlioOptions? options = null, CancellationToken cancellationToken = default);

    public static RenderResult SaveToRendlioPdf(this IXLWorkbook workbook, Stream output,
        Func<ClosedXmlRendlioOptions, ClosedXmlRendlioOptions> configure,
        CancellationToken cancellationToken = default);
}
```

Output overloads mirror upstream's own: `IXLWorkbook.SaveAs(String)` and `SaveAs(Stream)` both exist
across the range (verified, §11), and `RenderPdf(Stream)` is the engine primitive, so the stream form
is the direct one and the path form is the convenience over it.

Order of operations, fixed:

1. validate arguments (§3.5);
2. if `RecalculateBeforeSave`, recalculate (§4);
3. serialize once into a `MemoryStream` via `SaveAs(Stream)` — the one-argument overload, so no
   schema validation is imposed;
4. `RendlioWorkbook.Load(buffer, options.Load, cancellationToken)`;
5. `RenderPdf(output, options.Pdf, cancellationToken)`, returning its `RenderResult` unchanged;
6. dispose the buffer and the handle; never the caller's streams.

The happy path a consumer sees, which is the charter's own example unchanged:

```csharp
using ClosedXML.Excel;
using Rendlio.Interop.ClosedXml;

using var wb = new XLWorkbook("template.xlsx");

var result = wb.SaveToRendlioPdf("out.pdf");                    // safe defaults
Console.WriteLine(result.Report.ToJson());                      // the report is right there

wb.SaveToRendlioPdf("out.pdf", o => o with                      // escape hatch
{
    Pdf = o.Pdf with { PdfA = true, Deterministic = true },
    Load = o.Load with { Culture = "de-CH" },
});
```

### 5.2 `Rendlio.Interop.ClosedXmlReport`

Upstream: ClosedXML.Report (unmodified). Extension target: its template type (`XLTemplate` — see §11
for what is and is not verified). Same four-overload set, same order of operations, with
`ClosedXmlReportRendlioOptions` and `ClosedXmlReportRendlio`.

Two package-specific rules:

1. **The adapter never calls `Generate()`.** It renders the template as the caller left it.
   Generating implicitly would run the caller's data binding at a moment they did not choose, and
   re-running it on an already-generated template is a behaviour change the adapter has no standing
   to make. The XML docs state plainly that the template must be generated first, and the
   `<example>` shows `Generate()` before the call.
2. **The recalculation default of §4 applies through the workbook the template wraps.** If the
   pinned upstream version does not expose it, the adapter is blocked and the fix is an upstream
   contribution (repository rule 2) — never a fork, and never a quietly dropped default.

### 5.3 `Rendlio.Interop.MiniExcel`

Upstream: MiniExcel (unmodified). Static entry points, not extension methods: the inputs are a path
or a stream, and there is no upstream object to extend. The charter fixes both the class name and
this shape.

```csharp
namespace Rendlio.Interop.MiniExcel;

public sealed record MiniExcelRendlioOptions
{
    public LoadOptions Load { get; init; } = new();
    public PdfRenderOptions Pdf { get; init; } = new();
}

public static class MiniExcelRendlio
{
    // Four (input, output) shapes; each in both option forms of §3.2 — eight methods.
    public static RenderResult SaveToRendlioPdf(string inputPath, string outputPath, …);
    public static RenderResult SaveToRendlioPdf(string inputPath, Stream output,     …);
    public static RenderResult SaveToRendlioPdf(Stream input,     string outputPath, …);
    public static RenderResult SaveToRendlioPdf(Stream input,     Stream output,     …);
}
```

`…` is `MiniExcelRendlioOptions? options = null, CancellationToken cancellationToken = default` in
form A, and `Func<MiniExcelRendlioOptions, MiniExcelRendlioOptions> configure, CancellationToken
cancellationToken = default` in form B.

Order of operations: validate; `RendlioWorkbook.Load(inputPath | input, options.Load, ct)`;
`RenderPdf(output, options.Pdf, ct)`. **No buffering** — the input already is a file or a stream, so
the adapter adds no copy, which is the point of the stream-shaped surface. §3.6 rule 2 still applies
to the path output form.

What this package does **not** do in v1: it takes no data collection and writes no workbook. The
charter's sketch renders an already-written file, and a data-to-PDF overload would be new surface.
See §12 item 2 — the upstream-dependency question is still open, and any answer is additive.

## 6. Defaults that are forbidden

The charter's safe-default rule: no default may trade fidelity for convenience, and warnings are
never suppressed to look cleaner. Concretely, an adapter must not:

1. set `UseSystemFonts = true` — it trades the engine's determinism guarantee for whatever fonts the
   host happens to have;
2. set `Deterministic`, `PdfA`, `Culture`, `FontsDir`, `ReferenceDate`, `Timeout`, `MaxMemoryMb`,
   `License`, or `Recalculate` — every one of them is the caller's, and the engine's default is the
   parity anchor;
3. inspect `RenderResult.Status` and throw, filter, or warn on it;
4. catch an engine exception to return a "failed" result instead;
5. touch the report: no filtering, no re-serializing, no logging, no writing it to disk;
6. suppress a compiler, analyzer, or trim warning to make the package look clean — the repository
   builds with warnings as errors precisely so this surfaces (`README.md`, "Contributing");
7. add a runtime dependency beyond the engine package and its own unmodified upstream;
8. vendor, patch, or fork any upstream source (repository rule 2).

## 7. Documentation the surface must carry

`GenerateDocumentationFile` plus warnings-as-errors makes an undocumented public member a build
error, so this is a build gate, not a wish. Required on every public member:

- `<summary>` saying what the call does — not what it is, and never a fidelity promise;
- `<param>` for every parameter, including what happens when a stream is not seekable and what
  relative paths are relative to;
- `<returns>` naming the report explicitly: the result carries the compatibility report for this
  conversion;
- `<exception>` for every entry in §3.5 plus the engine exceptions of §2 that can reach the caller;
- `<remarks>` on the ClosedXML-family methods stating the recalculation default, that it mutates the
  caller's workbook, and how to turn it off;
- `<example>` on each package's primary overload, in the form-B shape of §3.2;
- `<see cref="…"/>` cross-references, so the options record is reachable from the method in
  IntelliSense.

**XML doc comments are shipped public surface.** The repository's fixture scans every shipped file,
`src/**/*.cs` included, so a doc comment may not name an unannounced product, carry an internal
identifier or path, or describe how fidelity is measured (`PublicSurfaceRulesTests`). It must also
never claim a permissive licence for the rendering engine: the engine is BUSL, and the accurate term
is **source-available**. That rule is machine-checked over every shipped file too, by a pattern that
still matches the inaccurate phrase through a line wrap, a hyphen, or an emphasis marker — so it
cannot be walked past by typesetting. The ban is deliberately blanket rather than scoped to the
engine, so a doc comment describes an upstream by naming the licence it carries — "ClosedXML is
MIT-licensed" — which says strictly more anyway. `RepositoryRulesTests` keeps only the positive half
of the rule, that `README.md` states the accurate term.

Each package README states, prominently: the recalculation default and how to override it (the
charter's safe-default rule), and the version-pinning policy (rule 3 in [README.md](../README.md)).

## 8. Package-level requirements

| Requirement | Value | Source |
| --- | --- | --- |
| Target frameworks | `net8.0;net10.0` | API spec §8.1; the adapter cannot target below the engine, and upstream `netstandard2.0`/`2.1` assets are compatible with both (verified, §11) |
| Dependencies | the engine library + one unmodified upstream, nothing else | the charter; `README.md` "Contributing" |
| Upstream version | a range in `Directory.Packages.props`, covering certified versions only | repository rule 3 |
| Size | ~200–500 LOC of glue | the charter |
| Packability | project lives under `src/`, which flips `IsPackable` | `src/Directory.Build.props` |
| XML docs | mandatory (§7) | `src/Directory.Build.props` |
| Public surface | recorded in `PublicAPI.Shipped.txt` + `PublicAPI.Unshipped.txt` beside the project file, each starting `#nullable enable`; a new member lands in the unshipped half and moves across when its version publishes | `src/Directory.Build.props` references `Microsoft.CodeAnalysis.PublicApiAnalyzers`, so a public member neither file records fails the build (`RS0016`), and a project given one of the two files is told so (`RS0048`) |
| AOT / trimming | **do not** set `IsAotCompatible` / `IsTrimmable` unless the pinned upstream is verified clean; never suppress an `IL2xxx`/`IL3xxx` warning to claim it | §6 rule 6; API spec §8.2 binds the engine, not its upstreams |

## 9. Conformance checklist

An adapter conforms when all of these hold:

1. The one-liner works end-to-end for the upstream's idiomatic hello-world, and the returned report
   is reachable (the charter's acceptance criteria).
2. Recalculation is demonstrated, not asserted: a formula-bearing workbook, an input cell mutated,
   saved through the adapter, and the rendered PDF shows the recomputed value — with a control path
   that does not use the adapter showing the stale one (the charter's acceptance criteria).
3. `RecalculateBeforeSave = false` is the only thing that disables it, including for a workbook in
   manual calculation mode.
4. Report JSON from an adapter call is byte-identical to the CLI's for the same input and options.
5. A workbook with unsupported content returns — `Status == UnsupportedContent`, no exception.
6. Caller-supplied streams are open and undisposed after the call; the adapter's own are gone.
7. A failed render leaves no partially written file behind on the path output form.
8. Every public member carries the docs of §7, and `dotnet build -warnaserror` is clean.
9. The public surface matches §5 exactly — no extra public type, no widened visibility for a test.
   Half of this is a build gate rather than a reading: the two `PublicAPI` files of §8 record the
   surface, so an extra public type fails the build. That what they record is what §5 says is still
   read — the analyzer knows the surface is deliberate, not that it is the right one.
10. The upstream range in `Directory.Packages.props` covers certified versions only.

## 10. Derivations

Every decision here that is not verbatim in the charter, with what it resolves. A reviewer who
disagrees should push back on the row, not on the whole document.

| # | Decision | Resolves |
| --- | --- | --- |
| D1 | Bind to `RendlioWorkbook.Load` + `RenderPdf`, not the one-call facade | The facade needs a file path for the ClosedXML family; §2 gives the three reasons |
| D2 | `Func<T, T>` escape hatch instead of the sketch's bare `opts => opts…` | The API spec's `init`-only records cannot be mutated by an `Action<T>`; this is the same call-site shape and compiles |
| D3 | Options record exposes `LoadOptions` + `PdfRenderOptions`, not `ConvertOptions` | `ConvertOptions.Format`/`Dpi` are meaningless in a PDF-only method; the split pair has no dead field (§3.3 rule 4) |
| D4 | Return `RenderResult`; no `out`, no wrapper | Charter allows return or out; `out _` swallows, a wrapper drifts from the report spec (§3.4 rule 1) |
| D5 | Per-package options record name, no shared package | Two adapters installed together must not collide, and no shared package exists to hold one (§3.1) |
| D6 | `RecalculateAllFormulas()`, not `SaveAs(…, evaluateFormulae: true)` | Independent of dirty-tracking; avoids imposing `validate`; present across the whole range (§4) |
| D7 | Recalculation ignores `CalculateMode` | Manual mode is the stale-value case the default exists for (§4) |
| D8 | Recalculation failure propagates unwrapped | A silent stale render is forbidden; a wrapper type would promise stability over an unmodified upstream (§4) |
| D9 | Stream output overloads in addition to path | Upstream parity (`SaveAs(String)`/`SaveAs(Stream)`); the engine primitive is the stream form (§5.1) |
| D10 | A partially written output file is deleted on failure | A readable truncated PDF is worse than none; the report spec §4 deletes outputs on the resource-limit path (§3.6 rule 2) |
| D11 | The ClosedXML.Report adapter never calls `Generate()` | Implicit generation runs the caller's binding at a moment they did not choose (§5.2) |
| D12 | `RecalculateBeforeSave` absent, not false, in the MiniExcel options | Nothing to recalculate; a `false` field would read as a default silently turned off (§4) |
| D13 | Adapters make no AOT/trim claim | The claim would be about an upstream this repository does not control, and suppressing the warnings to keep it is forbidden (§8) |
| D14 | The explicit-`null` ambiguity is accepted and documented | Removing either overload form costs the charter's lambda or configuration-built options (§3.2 rule 5) |

## 11. Verified, and not

Checked on a development machine, 2026-08-26, against the packages in the local NuGet cache
(`ClosedXML` 0.102.2 / 0.104.2 / 0.105.0 — documentation files and assemblies):

- `ClosedXML.Excel.IXLWorkbook.RecalculateAllFormulas()` — present in all three versions.
- `ClosedXML.Excel.IXLWorkbook.SaveAs(Stream)` — present in all three; the three-argument overloads
  `SaveAs(String, Boolean, Boolean)` / `SaveAs(Stream, Boolean, Boolean)` exist in 0.105.0 with an
  `evaluateFormulae` parameter.
- `ClosedXML.Excel.IXLWorkbook.CalculateMode` exists — hence the explicit rule not to read it.
- ClosedXML ships `netstandard2.0` and `netstandard2.1` assets, compatible with `net8.0`/`net10.0`.
- **`ClosedXML.Excel.LoadOptions` exists** and collides with the engine's `LoadOptions`
  (§3.3 footgun 2). Confirmed as `CS0104` at a consumer call site, and confirmed absent for
  `PdfRenderOptions`, `RenderResult`, `CompatReport`, `RecalcMode` and `RendlioStatus`.

Compiled, in a scratch project outside the repository (`net10.0`, nullable on, warnings as errors,
real ClosedXML 0.105.0, the API spec's shapes standing in for the unpublished engine library): the
full §5.1 surface plus five consumer call-site forms — the happy path, the form-B lambda including a
nested `with`, the options-object form, the stream-output form, and the static call form — build with
zero warnings and zero errors. The explicit-`null` ambiguity of §3.2 rule 5 was reproduced as
`CS0121`. The probe was deleted afterwards.

Not verifiable here, and why:

- **The engine library is not published yet**, so no snippet in this document can be compiled
  against the real package. The engine shapes above are quoted verbatim from the API spec; INT-5
  must re-run the probe against the real package.
- **ClosedXML.Report is not in the local cache**, so its template type name is taken from upstream
  documentation rather than verified. It is the one public-surface element here that is unchecked; a
  wrong name cannot ship, because it fails to compile on day one, and nothing else in §5.2 depends
  on it.
- **MiniExcel is not in the local cache.** §5.3 needs no MiniExcel member, which is itself the
  substance of §12 item 2.

## 12. Open, and not decided here

Three questions this document deliberately does not answer. None of them changes the adapter surface
fixed above, and none blocks INT-5, INT-6 or INT-7 beyond the engine's publication, which §11
already gates them on. Item 1 is a hard prerequisite — an adapter project file cannot be written
without the `PackageReference` id — but it is answered by observation the moment the engine package
ships, and nothing can start before that anyway.

1. **The engine package id and root namespace.** The naming law puts the library at
   `Rendlio.Sheets`; the API spec, written earlier, still says `PackageId=Rendlio` and still puts the
   public types in namespace `Rendlio`. The adapters need one `PackageReference` id and one `using`.
   It also decides footgun 2's blast radius: while the engine namespace encloses
   `Rendlio.Interop.*`, the `LoadOptions` collision is a consumer-only problem; if the namespace
   becomes `Rendlio.Sheets`, the adapters' own sources need the alias too.
2. **Whether `Rendlio.Interop.MiniExcel` takes a MiniExcel dependency at all.** The charter's sketch
   renders an already-written file, which needs no MiniExcel API — yet its acceptance criteria
   require each package to reference its unmodified upstream. Either the acceptance criterion or the
   surface has to move: a data-to-PDF entry point would earn the dependency, and it would be new
   surface this document may not invent.
3. **Adapter provenance in the report.** The report is the honesty organ, but an adapter-side
   recalculation that changes rendered values is invisible in it: `options` echoes engine options
   only, and `CompatReport` is immutable, so an adapter cannot record that it recalculated. The
   report spec's v1 is frozen and additive optional fields bump the minor version, so this is an
   engine-side decision. Until it is made, the adapters disclose the default in their XML docs and
   READMEs (§7) and nowhere else.
