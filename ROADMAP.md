# Roadmap

The current build ships three scenes and the compiler pipeline behind them. This is what comes next, in the order it earns its keep, with rough effort and the reason for the ordering.

## Phase 1: the remaining scenes (about 18 hours)

**Dependency injection lifetimes.** Build a miniature container that records every resolve and dispose, then resolve the same graph as singleton, scoped and transient and show instance identity. The lesson is that a lifetime is a decision about identity, and nothing teaches that faster than watching the same object appear three times or once.

**Value versus reference.** Draw stack frames and the heap as the code runs: what copies on assignment, what aliases, and where boxing allocates. This one needs an instrumented interpreter for a small subset rather than real execution, which is why it is more than a weekend.

Done when: all five scenes from the original brief are live, each with prose that survives being read without the app.

## Phase 2: line level accuracy (about 10 hours)

The state numbers are currently derived from the source: one per await. That is correct for simple methods and wrong the moment control flow gets interesting, because `try`/`finally`, loops and conditional awaits change where the resumption points land.

Emit a portable PDB alongside the assembly, read its sequence points with `System.Reflection.Metadata`, and map each IL offset back to a source line. Then the state list can say which line each state resumes at, and the field explanations can point at the local they belong to.

Done when: a method with an await inside a `try` and an await inside a loop is described correctly, and a test covers both.

## Phase 3: the IL pane (about 12 hours)

A disassembler over the emitted method body: opcode table, operand decoding, and a readable listing with the stack effect annotated. This is the feature SharpLab already owns, so it is only worth building if the listing is explained rather than dumped, with the state machine's `MoveNext` switch called out line by line.

Done when: `MoveNext` for a two await method reads as a narrative, not a wall of `ldloc`.

## Phase 4: weight (about 8 hours)

The payload is the biggest weakness. 52 MB on disk, about 8 MB over Brotli, and most of it is Roslyn. Options, in order:

Install the `wasm-tools` workload and enable relinking, which drops unused runtime code. Load the compiler lazily, after the first screen paints, so the explainer is readable while Roslyn arrives. Consider a second mode that ships precompiled snippets for the three fixed scenes, so a visitor on a slow link sees something useful in under a second and only pays for Roslyn when they edit code.

Done when: first meaningful paint under two seconds on a throttled connection, with editing still available for anyone who waits.

## Phase 5: small things that matter (about 10 hours)

Shareable snippets, encoded in the URL fragment so nothing leaves the browser. Keyboard navigation and a proper mobile layout for the code panes. A copy button for the generated type listing. Golden file tests across a dozen snippet shapes, so a Roslyn upgrade that changes field naming fails loudly instead of quietly producing nonsense.

## Deliberately not planned

Executing user code in the page. It would make the async scene more vivid (watch the thread hop) but it turns a static demo into a security surface, and the value is mostly theatre: `MoveNext` is the explanation, not a breakpoint.

A hosted compile service. Same reason, plus it would make the app depend on infrastructure for no gain.

## Housekeeping

- GitHub Actions running the check suite on push.
- A `dotnet format` pass in CI.
- Publish the inspector as a NuGet package so the metadata reading can be reused.
