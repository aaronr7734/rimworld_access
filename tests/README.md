# tests/

Game-free unit tests for RimWorld Access.

## Why a separate project that *links* source files

The mod (`rimworld_access.csproj`) targets **net472** and depends on
`Verse` / `UnityEngine` / `RimWorld`, which cannot load outside a running copy
of the game. So we cannot reference the mod DLL from a normal test host.

Instead `RimWorldAccess.Tests.csproj` targets **net10.0** and **links the
individual `.cs` files under test** (`<Compile Include="..\..\src\..." />`).
Only files with **zero game dependencies** can be linked this way — currently
`ScreenReader/SpeechSanitizer.cs` (pure `System.Text.RegularExpressions`).

This mirrors the approach used by the say-the-spire2 mod: pure logic is
testable precisely because it has been separated from the game-coupled layer.
The test suite grows as more pure logic is extracted from `*State` / `*Helper`
classes during the structural refactor (roadmap Phase E).

## Run

```bash
dotnet test tests/RimWorldAccess.Tests/RimWorldAccess.Tests.csproj
```

Needs only the .NET 10 SDK — no RimWorld install, no Unity. Also runs in CI on
every push / pull request (`.github/workflows/tests.yml`).

## Adding a file under test

1. Confirm it imports **no** `Verse` / `UnityEngine` / `RimWorld` namespace.
2. Add a linked `<Compile Include="..\..\src\<path>.cs" Link="UnderTest\<name>.cs" />`
   entry to `RimWorldAccess.Tests.csproj`.
3. Add a `<Name>Tests.cs` with `[Fact]` / `[Theory]` cases.

If the logic you want to test is tangled with game types, extract the pure part
into its own class first — that extraction is the point, and it is what makes
the structural refactor verifiable.
