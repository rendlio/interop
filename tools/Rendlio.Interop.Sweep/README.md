# Rendlio.Interop.Sweep

A repeatable read of public package registries. One run asks each registry what it publishes,
writes down what it saw, and prints what moved since the run before it.

It exists because the alternative is doing it by hand, and doing it by hand means it happens
when somebody remembers to. A watch that runs on a clock notices the thing nobody was looking
for; an afternoon of searching finds what the searcher already suspected.

This is a tool, not a package. It lives under `tools/`, so it is never packed and never
installed by anyone consuming this repository — see `tools/Directory.Build.props`.

## Running it

```sh
dotnet run --project tools/Rendlio.Interop.Sweep -- \
  --recipe <path to a recipe> \
  --ledger <path to the ledger> \
  [--run <identifier>]
```

| Option | Meaning |
| --- | --- |
| `--recipe` | The file saying what to look for. Required; there is no default. |
| `--ledger` | The append-only file to read the previous run from and add this run to. |
| `--run` | The identifier stamped on this run. Defaults to the UTC moment it started. |

`GITHUB_TOKEN`, if the environment has one, raises GitHub's rate limit and changes nothing
else. It is read from the environment rather than an argument so it stays out of a shell
history and a process listing, and it is attached to GitHub requests only.

The run exits 0 when it completed and 1 when it could not. Whether anything changed is in the
report, not the exit code — an unchanged week is a successful run.

## What it reads

Official APIs only, one collector each. Nothing here reads a page that was meant for a
browser, and nothing here logs in to anything.

| Registry | Endpoint | What a term means |
| --- | --- | --- |
| crates.io | `crates.io/api/v1/crates` | a search expression over name, description and keywords |
| nuget.org | resolved from `api.nuget.org/v3/index.json` | a search expression; `packageid:` makes it a lookup |
| npm | `registry.npmjs.org/-/v1/search` | a search expression, with npm's own qualifiers |
| PyPI | `pypi.org/pypi/<project>/json` | **a project name** — see below |
| GitHub | `api.github.com/search/repositories` | a repository-search expression |

Two of those deserve their footnotes.

**nuget.org does not promise a fixed search address.** It publishes where search currently is
in a service index and expects clients to ask, so a run asks, once, and uses the answer.
Hard-coding today's address is how a tool quietly stops working a year from now.

**PyPI publishes no search API.** The XML-RPC one was withdrawn, and what replaced it is the
website, which is not ours to scrape. So a PyPI query names a project rather than describing
one, and the recipe carries the names. That is a real gap in coverage on that registry, and
saying so is more use than a search that quietly worked on the HTML.

Neither npm's search results nor GitHub's repository results carry a download count, so those
records leave it empty rather than spending a request per result to fill it in.

## The rules it is built to keep

- **It reads.** The interface every collector reaches the network through has one method, and
  that method is a GET. There is no way to write a collector against it that posts, edits, or
  opens anything.
- **It goes where the collector decided.** A recipe supplies terms, never addresses. Terms are
  escaped into the query string, and the one registry that takes a term in the URL path holds
  it to what a project may be called.
- **It carries no list of its own.** The queries and the patterns are in the recipe, and a
  recipe is not part of this repository. A run is handed one.
- **What it writes is not published.** The ledger and the report are working notes for
  whoever reads them. The ignore rules keep both out of this repository, and a fixture keeps
  the ignore rules honest.

## The record

Every registry projects into one shape, so a diff can compare a crate to a crate without
knowing which registry either came from.

| Field | Notes |
| --- | --- |
| `id` | `source:name`, lower-cased. The identity two runs are joined on. |
| `source`, `name`, `url` | Where it is, what it is called, and where to see it. |
| `version` | Latest version, where the registry publishes one. |
| `description` | The registry's own line, unedited. |
| `downloads`, `stars` | On whatever window the registry uses. |
| `updated` | When the registry says it last moved, in UTC. |
| `queries` | Which recipe queries surfaced it. |
| `claims` | Which recipe patterns matched its published text — ids only, never the pattern. |

## The ledger

One JSON object per line, appended and never rewritten. That is the discipline rather than an
implementation detail: a file that was rewritten each run could answer what is out there this
week and would have lost the only question worth asking afterwards — when did this first
appear, and what has it done since.

A line that cannot be read stops the run. Skipping it would be worse: the candidate on that
line would read as a new arrival every week for as long as the line stayed there.

## The diff

The previous run is the last run in the ledger. This run is compared against it, and the
report names three things:

- **new** — an identity the previous run did not have.
- **changed** — `url`, `version`, `downloads`, `stars`, `updated`, or which patterns matched.
- **not seen this run** — an identity the previous run had. Not the same as gone: a page of
  results drops its tail when the page fills. It is a prompt to look, not a finding.

A description is not compared, because prose churns and would bury everything else. Nor is the
query list, because it describes the recipe rather than the candidate — renaming a query would
otherwise report the whole field as having moved.

Download and star counts move on every live package, so the recipe sets how far one has to
move before it counts:

```json
"sensitivity": { "minDownloadsDelta": 500, "minStarsDelta": 25 }
```

A count that appears or disappears is reported however small it is, thresholds notwithstanding
— the registry starting or stopping publishing one is not drift.

A run that finds nothing prints `no changes`, and that is the point of the whole thing.

## The recipe

```json
{
  "name": "a-recipe",
  "sensitivity": { "minDownloadsDelta": 500, "minStarsDelta": 25 },
  "queries": [
    { "id": "crates-by-keyword", "source": "CratesIo", "term": "xlsx pdf", "take": 50 },
    { "id": "a-tracked-project", "source": "PyPi",     "term": "some-project" }
  ],
  "patterns": [
    { "id": "a-pattern-name", "expression": "a .NET regular expression" }
  ]
}
```

`source` is one of `CratesIo`, `NuGet`, `Npm`, `PyPi`, `GitHub`. `take` defaults to 50 and
cannot exceed 100, which is the largest page every one of these registries serves.

A recipe is validated in full before the first request. A missing name, a duplicate query id,
an empty term, a page size no registry will serve, a pattern that does not compile, or a
registry this run has no collector for — each of them fails the run at the start. Half a sweep
is worse than none, because everything the missing half would have contributed reads as having
disappeared.

Patterns are compiled with a two-second budget per candidate. A pattern that spends longer than
that backtracks, and the run says which one rather than hanging until something else notices.
