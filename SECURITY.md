# Security policy

Where to send a suspected vulnerability in what this repository publishes, and what happens
after you do. It is short for the same reason the [support and triage policy](SUPPORT.md)
is: a small promise that is kept is worth more than a large one that is not.

## Status

No package has been published from this repository yet, and this page is in place before the
first one is. A disclosure route arranged in the hour it is first needed is not a route — it
is an improvisation, and the person who pays for it is the one who found the problem.

## Reporting a vulnerability

A suspected vulnerability is not an ordinary issue: do not open a public one describing it.
Report it privately through this repository's **Security** tab (*Report a vulnerability*),
which reaches the maintainers without disclosing the problem to everyone else first.

If that route is not offered, open an ordinary issue that says only that you have a security
report and asks where to send it — no detail, no reproduction, no file. An issue that says
nothing more than that discloses nothing, and it comes back with somewhere private to send
the rest. A missing button is never a reason to publish the details.

Either route is enough on its own; there is no need to use both.

## What helps in a report

None of this is required. A report that is one paragraph of prose is a report, and a partial
one sent today is worth more than a complete one sent next month. These are only the things
that get asked for afterwards when they are missing:

- which package and version, and which upstream version it resolved against
- what it gets an attacker: what they can read, change, or make the process do
- the smallest input that shows it, and the code that fed it in
- anything unusual it needs — a particular platform, a hostile input file, a caller that is
  already privileged

If showing it needs a file, keep the file inside the private thread. Putting it somewhere
public in order to link to it is the disclosure this page exists to avoid.

## Where a vulnerability belongs

The routing is the one the [triage policy](SUPPORT.md#where-a-bug-belongs) already draws,
and it matters more here: a report that went to the wrong project leaves the people who
could fix it not knowing there is anything to fix.

| Where the defect is | Where it is reported |
| --- | --- |
| The glue — an adapter published from this repository | Here, privately, by the route above. |
| The upstream library — it is vulnerable with or without an adapter | Privately to that project, by whatever route it publishes. |
| The rendering engine — reached through an adapter, but present without one | Privately to Rendlio Sheets rather than here. |

**Working out which one it is is not your job, and neither is finding somebody else's
channel.** If you cannot tell them apart — or you can, and the route it points to is not one
you can find — send it here privately: it is read, and you are told where it belongs. It is
passed on only if you ask for that, because where a report goes next is a disclosure
decision and the decision is yours. Guessing wrong costs you nothing.

## What happens next

**There is no response-time target here, and there will not be one** — the same answer the
triage policy gives, for the same reason. A number that could not be held through a quiet
month would be decoration, and decoration is worse than saying nothing, because it gets
believed.

What is promised instead is small enough to keep:

- **The report is read, and it gets an answer.** Confirmed, needs more information, or
  declined with a reason. "Not a vulnerability" is one of the answers, and it arrives with
  the reasoning rather than as silence.
- **You are told what was decided**, and when a fix goes out, that it has.
- **You are credited by name** wherever the problem is written up, unless you would rather
  not be — say so and you are not.
- **Nothing is asked of you in return.** No agreement to sign, no embargo to accept before
  anyone will read it, and no silence expected of you afterwards.

There is no bug bounty here. Everything in this repository is MIT, and what it can offer
someone who finds a problem is credit and a fix rather than money — which is worth saying on
the page rather than leaving to be discovered after the work is done.

## Disclosure

**The timetable is yours.** Tell us the date you mean to publish on and that date stands:
you will not be asked to move it, and you will not be asked to stay quiet once it has
passed. If you would rather wait for a fix, you are told when one is out.

Where the repair belongs upstream it is sent upstream, and never worked around in a private
copy — [rule 2](README.md#2-fork-rules) forbids the fork that would take, and a
vulnerability is not an exception to it. It travels by that project's own private security
channel, and never as an ordinary public pull request: the [upstream patches
policy](UPSTREAM-PATCHES.md) asks for an issue first and a small file that reproduces the
defect, and for a vulnerability that file is the exploit and that issue is the disclosure.
The public half of that policy resumes once the upstream has run its own process, which is
theirs to run rather than ours to hurry.

## Which versions are covered

Nothing has been published from this repository, so there is no list to give yet. The rule
that applies once there is one is settled now rather than under pressure later: a fix goes
out in a new release of the affected package, and versions already released are not patched
in place. There is no long-term support branch here to back-port to.
