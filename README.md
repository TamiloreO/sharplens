# sharplens

An interactive visualiser for what C# and .NET actually do underneath your code. The real Roslyn compiler runs in your browser; the app shows you what it produced.

![The async scene: a method, its generated state machine, and the fields that make resumption work](docs/async.png)

## Why this exists

SharpLab shows you IL. DotNetLab lets you compile C# in a browser. Both are good at what they do, and neither explains *why* the output looks the way it does. A list of state machine fields is not an explanation of how `await` releases a thread.

sharplens takes the opposite approach: a small number of concepts, each backed by a real compilation or a real measurement, each with prose that stands on its own. You can edit the code, and the explanation updates because the compiler actually ran again.

## The scenes

**async / await.** Type a method, press compile, and read the type the compiler generated for it. The app finds the state machine in the emitted metadata and shows its fields with an explanation for each: which one holds the resumption point, which one owns the `Task`, which one keeps the `this` reference alive across an await, and why a local had to move out of the stack into a field. States are listed alongside.

**LINQ.** A query over a sequence that logs every yield, so the trace shows the order of operations. Building the query reads nothing. The first `foreach` pulls values one at a time. The second enumeration starts again from the beginning, which is why `Count()` is not free.

**Allocations.** Four workloads measured with the runtime's own counter: string concatenation, `StringBuilder`, boxing, and list growth.

![The LINQ scene, showing that building a query reads nothing and each enumeration starts over](docs/linq.png)

The numbers in the last scene are real, taken in the browser:

| workload | bytes allocated |
| --- | --- |
| `text += "x"`, 10,000 times | 100,179,984 |
| `StringBuilder`, 10,000 appends | 32,912 |
| boxing 10,000 ints | 200,016 |
| `List<int>` grown to 10,000 | 131,288 |

## How it looks

The page uses the swiss skin from [style-labs](https://tamiloreo.github.io/style-labs/?s=swiss): a fixed grid, hairline rules, one red, and type doing the hierarchy, so a dense field table stays readable next to code.

## How it works

The site is Blazor WebAssembly, so there is no server and nothing to secure. Three pieces make it work:

Compilation is Roslyn, referenced directly and running in the browser. Reference assemblies come from `Basic.Reference.Assemblies.Net80`, which ships them as embedded resources, because WebAssembly has no file system to read a targeting pack from.

Inspection reads metadata with `System.Reflection.Metadata` instead of loading the emitted assembly and reflecting over it. That is not a stylistic choice. Loading the assembly throws under WebAssembly, where the runtime cannot always resolve the facade references a compilation produces, and the first version of this app failed exactly that way. Reading the metadata sidesteps the problem and costs less.

The state numbers are derived from the source rather than read from IL: one resumption point per `await`, plus start (`0`) and completed (`-1`). The field names, types, interfaces and method signatures are all real, straight out of the compiler's output.

## Run it

Needs the .NET 8 SDK.

```bash
git clone https://github.com/TamiloreO/sharplens.git
cd sharplens
dotnet run --project src/Sharplens.Web
```

To deploy, publish and serve the output as static files:

```bash
dotnet publish src/Sharplens.Web -c Release -o publish
# serve publish/wwwroot behind any static host with Brotli enabled
```

## Tests

```bash
dotnet run --project tests/Sharplens.Tests
```

Nineteen checks, no test framework dependency, covering the parts that would silently rot:

| Area | What is checked |
| --- | --- |
| async | A two await method compiles, yields a state machine implementing `IAsyncStateMachine`, lists four states, explains the state and builder fields, and exposes `MoveNext` |
| async | A synchronous method produces no state machine rather than a wrong one |
| async | Broken code returns diagnostics instead of throwing |
| LINQ | The trace starts with the build, no yield precedes it, and both enumerations are visible |
| allocations | Four results, all positive, with concatenation allocating more than `StringBuilder` |

## Limits, stated plainly

The code you type is compiled, not executed. Nothing you write runs in the page, which keeps the app honest about what it shows and avoids turning a demo into a sandbox problem.

The GC and allocation behaviour of WebAssembly is not desktop .NET: one thread, no server GC, different collector settings. The numbers are real for this runtime, and the scene does not pretend otherwise.

Two of the five scenes described in the roadmap are not built yet: dependency injection lifetimes and value versus reference. The state numbers are derived, not read from a PDB; line level highlighting needs sequence points.

The payload is heavy. The published site is 52 MB on disk and the framework assemblies compress to about 8 MB with Brotli, so a first visit on a slow connection is a real cost. Trimming that is the first item in the roadmap.

## Roadmap

[ROADMAP.md](ROADMAP.md) covers the remaining scenes, PDB sequence points for line mapping, an IL pane with a real disassembler, and the work needed to cut the download.

## License

[MIT](LICENSE) © Oluwatamilore Olufohunsi
