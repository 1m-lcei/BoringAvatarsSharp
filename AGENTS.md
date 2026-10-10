# Repository guide

- Keep the drop-in implementation and public types in `src/BoringAvatarsSharp/AvatarGenerator.cs`, with no external runtime dependencies. Preserve its MIT notice.
- Preserve deterministic SVG output, UTF-8/UTF-16 parity, XML escaping, and validation before writing. Add focused TUnit tests for behavior changes.
- Keep changes small. Update the English README when public behavior or usage changes.
- Use .NET SDK 10.0.300 or later in the .NET 10 series. CI uses file-based builds; solution and project files support IDE use.
- Keep the sample generator and its SVG/HTML gallery in tracked `sandbox/`. Regenerate with `dotnet run --file sandbox/Program.cs --configuration Release` after changes that affect examples or SVG output. Do not commit build outputs, test reports, or IDE caches. CI does not create releases.

## Verification

Run from the repository root after code or build changes:

```sh
dotnet build src/BoringAvatarsSharp/AvatarGenerator.cs --configuration Release -p:OutputType=Library -p:PublishAot=false -p:ImplicitUsings=disable -p:TreatWarningsAsErrors=true
dotnet run --file test.cs --configuration Release
```

`test.cs` includes the production source and tests directly. Keep its TUnit version aligned with the IDE test project.

## Commits

Use Conventional Commits: `type(scope): summary`, with an optional scope and a concise English summary. Use types such as `feat`, `fix`, `refactor`, `test`, `docs`, `ci`, and `chore`; mark breaking changes with `!` and explain them in the commit body.

<!-- CODEGRAPH_START -->
## CodeGraph

If `.codegraph/` exists at the repository root, use `codegraph_explore` or `codegraph explore "<question or symbol>"` before text searches or file reads to locate or understand code. Otherwise skip CodeGraph; do not create an index unless asked.
<!-- CODEGRAPH_END -->
