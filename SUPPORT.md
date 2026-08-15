# Support and triage policy

How issues and pull requests are handled here, and what you can expect after you open one.
It is deliberately short and deliberately modest: a small promise that is kept is worth
more than a large one that is not.

## Status

No package has been published from this repository yet, and this policy is in place before
the first one is. That order is the point — a free package with no stated support posture
is a promise nobody ever actually made, and the person who finds that out is always the
person who had already depended on it.

## What you can expect

**There is no response-time target, and there will not be one.** A number we could not
hold through a quiet month would be decoration, and decoration is worse than saying
nothing, because it gets believed.

What is promised instead is small enough to keep:

- **Every issue is read** — not always quickly.
- **Every issue gets an answer, and "no" is one of the answers.** Accepted, needs more
  information, belongs upstream, or declined with a reason. A closed issue that says why
  is a real answer; an issue left open forever is not.
- **Nothing is closed for being old.** No staleness bot runs here. If something is closed,
  a person decided to close it and wrote the reason.
- **A quiet thread is not a rejection.** Work here arrives in bursts. Pinging an issue that
  has gone silent is welcome rather than rude.

## What sets priority

These packages exist to carry documents from the free .NET spreadsheet libraries into
Rendlio Sheets rendering. That purpose sets the order of the work: **the engine's needs
govern.** A defect on the documented path from an upstream library to a rendered PDF comes
first, ahead of anything else in the queue.

Feature requests from outside are welcome, and some of them will be better than our own
ideas. They are still weighed against that purpose. A request that would pull an adapter
away from it gets declined with the reason — which is a better outcome for you than an
open issue that quietly never moves.

## Where a bug belongs

An adapter is thin glue over an unmodified upstream package (README rule 2), so a bug you
hit through one sits on a particular side, and each side is repaired somewhere different:

| Where the defect is | Where it is fixed |
| --- | --- |
| The glue — a wrong default, a dropped compatibility report, a missing overload | Here, as an issue in this repository. |
| The upstream library — it writes an incorrect file, adapter or no adapter | Upstream, in that project's own tracker. |
| The rendering engine — the file is correct and the rendered result is not | Against Rendlio Sheets rather than here. |

**Working out which one it is is not your job.** If you cannot tell them apart, open the
issue here with the input file and the code that produced it, and it gets routed. Guessing
wrong costs you nothing.

When the defect is upstream it is reported upstream, and it is not worked around in a
private copy — rule 2 forbids the fork that would take. Where the defect affects something
published from this repository, we will often write the fix ourselves and send it as a
pull request through that project's own process and under that project's licence, with
nothing attached to it: no request, no condition, and the maintainers' decision is the end
of the matter.

## In scope

Issues:

- an adapter that behaves differently from its documentation
- a default that is wrong, or that trades fidelity for convenience
- a compatibility report that is incomplete, or swallowed instead of returned
- a version range that disagrees with the certified set
- documentation or a sample that no longer compiles, or no longer matches the code

Pull requests:

- a fix, with a test that shows it
- a documentation or sample correction
- a new adapter over a library that fits the rules — open an issue first, because the fork
  rules and the pinning contract decide whether it can exist here at all

## Out of scope

Issues:

- general help with an upstream library, or .NET questions with no adapter in them
- a request to vendor, patch, or fork an upstream (rule 2)
- a request for behaviour that would have to be licensed BUSL (rule 1)

Pull requests, which are declined with the reason rather than left open:

- vendored or patched upstream code, in any form (rule 2)
- widening a version range ahead of a certification run (rule 3)
- a new dependency in a packable project with no reason given — it becomes a transitive
  dependency for everyone who installs the package
- broad refactors, style-only churn, or a fix bundled together with unrelated changes

## Security

A suspected vulnerability is not an ordinary issue: do not open a public one describing it.
Report it privately through this repository's **Security** tab (*Report a vulnerability*),
which reaches the maintainers without disclosing the problem to everyone else first.

If that route is not offered, open an ordinary issue that says only that you have a security
report and asks where to send it — no detail, no reproduction, no file. An issue that says
nothing more than that discloses nothing, and it comes back with somewhere private to send
the rest. A missing button is never a reason to publish the details.

The [security policy](SECURITY.md) says the rest: what helps in a report, where a
vulnerability that is not ours to fix belongs, and what a reporter is never asked for.

## The rules still decide

Triage does not override the three rules in the [README](README.md#the-rules). Licence
follows function, never fork the living, and ranges pin to certified versions — those
settle what can be accepted here at all, before any judgement about priority is made.
