# Upstream patches

When a defect in a library this repository depends on turns out to be ours to fix, we fix it
and send the patch to the people who own the project. That is the whole program: goodwill
paid in bug fixes rather than in cash. This page says which bugs qualify, how a patch is
sent, and what is never attached to one.

## Status

No patch has been sent under this program yet, and the policy is written before the first
one is. Etiquette settled after the awkward pull request is not etiquette; it is an excuse
written afterwards.

## Which bugs qualify

A bug qualifies when the defect is in the upstream library itself — it would be there with
or without an adapter — and it reaches the work published from here. In practice that means
one of two things:

- the library produces an incorrect file, and our fidelity QA pipeline catches it, or
- the library forces a workaround, either in an adapter here or in the documented steps we
  publish for carrying a document from that library into Rendlio Sheets rendering.

The boundary is deliberately narrow, and it is the same one the [support and triage
policy](SUPPORT.md) draws. We fix what our own work touches. Becoming general-purpose free
labour for an arbitrary backlog is not a promise we could keep, and a promise that cannot be
kept is worse than none at all.

Two kinds of upstream are on the receiving end. ClosedXML and MiniExcel, because adapters
here depend on them and are forbidden from routing around them
([rule 2](README.md#2-fork-rules)). And the projects with a standing offer of co-maintenance
behind them — MiniWord and ClosedXML.Report — because rule 2 already says improvements to
those go upstream as contributions. The etiquette below governs both.

## How a patch is sent

- **Their process, exactly.** Each upstream's contribution guide is followed as written: an
  issue first where the project asks for one, tests always, and their code style rather than
  ours. A patch that arrives in our house style costs a maintainer time before it saves them
  any.
- **Plain disclosure.** Every issue and pull request says who we are and why the defect
  matters to us, from an account that is visibly ours. No unattributed accounts, no second
  voice arriving in a thread to agree with the first, and no version of the story that
  leaves out why we turned up.
- **A person reviews AI-authored code before it is sent.** Much of what we write starts as
  AI-authored code, and none of it reaches an upstream tracker unread: a person on our side
  reviews the patch, and that person's name is on the commit. The patch is contributed under
  the upstream project's own licence, and whoever signs it holds the rights to what they are
  contributing. A maintainer merging our code inherits no ambiguity about where it came from.
- **Small and single-issue.** One pull request per fix, with a small file that reproduces
  it. No drive-by refactors, no unrelated tidying. A maintainer's verdict is accepted without
  pressure, and a declined pull request is their right.
- **Nothing is attached.** A patch never carries a request with it — not certification, not
  co-maintenance, not a link, not a mention — and never a hint that one would be welcome.
  Goodwill with a price tag is not goodwill.

The disclosure is one short paragraph, and only its first sentence changes from patch to
patch:

> [Why this defect reached us — the adapter or the documented path it affects.] Rendlio is a
> Swiss association in formation whose profits are pledged to charities; our spreadsheet
> adapters are MIT, and the Rendlio Sheets rendering engine is source-available under the
> Business Source License. This patch comes with nothing attached to it.

## The fork workbench

A pull request needs a fork, and rule 2 says never fork the living — so it is worth being
exact about which kind of fork this is. A contribution fork is not a continuation of the
project, and not a private copy kept in order to route around it. GitHub stamps it *forked
from* the upstream, and that is all it ever is: a workbench for producing pull requests.

The mechanics are fixed, and each one is part of what keeps that distinction real:

- Contribution forks live under the `rendlio` organisation, so a pull request reads *rendlio
  wants to merge …*. The goodwill is signed rather than anonymous.
- A fork exists only to feed pull requests. It never publishes a package, never presents
  itself as an alternative to the upstream, and is deleted once its patches are merged.
- The fork's `main` stays identical to upstream and is synced regularly. Nothing of ours is
  committed to it.
- All work happens in short-lived branches: one branch and one pull request per fix.
- **Allow edits by maintainers** is ticked on every pull request, so a maintainer who wants
  to adjust the patch can do it in place rather than ask.

Together those keep the fork harmless. It cannot drift into being a competing distribution,
because it publishes nothing and differs from the upstream only inside a branch that lives
until the pull request closes.

## No cash

**This program pays no cash sponsorships.** Not to a project, not to a maintainer, not as a
condition of anything, and not as thanks afterwards. What it pays is work: the reproduction,
the test, and the fix that somebody else would otherwise have had to write.

That is not thrift. In this community the currency that carries weight is contribution, and
a payment changes a relationship in a way a patch does not — the moment a maintainer could
reasonably wonder whether a decision of theirs is being bought, the goodwill is already
spent. Whether Rendlio ever supports the libraries it depends on financially is a separate
question, decided elsewhere and not by this program.

## When a patch is declined

It is declined, and that is the end of it. The fix is not resubmitted with a better
argument, the maintainer is not pursued in another thread or another forum, and the change
does not reappear as a patched copy inside an adapter. A decline is not a loophole in rule 2.
