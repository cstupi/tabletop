# Task 1: Reference the libs, the client test project and the two greps

**Files:**
- Modify: `client/Tabletop.csproj`, `Tabletop.sln`
- Create: `tests/Tabletop.Client.Tests/Tabletop.Client.Tests.csproj`, `tests/Tabletop.Client.Tests/ReferencesTests.cs`, `scripts/check-client`

**Interfaces:**
- Consumes: `Point2`, `TableSize`, `Table`, `ZoneStyles` (`Tabletop.Geometry`, contracts plan Task 2 and geometry plan Tasks 11 and 13); `TableStateTests.Sample()` (`Tabletop.Content.Tests`).
- Produces: the test project `Tabletop.Client.Tests` compiling `client/src/Core/**/*.cs` against the three libs and the two sample-holding test projects; `./scripts/check-client` exiting non-zero on a physics body in a scene or a rules type in a script.

Spec: `architecture.md` → What is actually shared (the client consumes the three libs); client shell → Testing (two greps). The table frame is one frame — origin at the table's corner — on the wire and in `Tabletop.Geometry` (geometry plan → Global Constraints; game server plan → `DraftEditor.OnSlab`), so the client converts nothing; the first tests pin that the library's `Table` and `ZoneStyles` answer in the wire frame.

- [ ] **Step 1: Add the ProjectReferences to `client/Tabletop.csproj`** (inside `<Project>`, after the `PropertyGroup`)

```xml
  <ItemGroup>
    <ProjectReference Include="..\libs\Tabletop.Geometry\Tabletop.Geometry.csproj" />
    <ProjectReference Include="..\libs\Tabletop.Content\Tabletop.Content.csproj" />
    <ProjectReference Include="..\libs\Tabletop.Protocol\Tabletop.Protocol.csproj" />
  </ItemGroup>
```

The client takes no `PackageReference` at all. `System.Security.Cryptography.ProtectedData` is not
referenced for the remembered session: that package is Windows-only and the client may not be, so
Task 2 uses the BCL's file APIs and nothing outside the three project references is needed.

- [ ] **Step 2: Write the test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>Tabletop.Client.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2" />
  </ItemGroup>
  <ItemGroup>
    <!-- Core is compiled here from source: if it needs Godot, this project does not build. That is the test. -->
    <Compile Include="../../client/src/Core/**/*.cs" LinkBase="Core" />
    <ProjectReference Include="../../libs/Tabletop.Geometry/Tabletop.Geometry.csproj" />
    <ProjectReference Include="../../libs/Tabletop.Content/Tabletop.Content.csproj" />
    <ProjectReference Include="../../libs/Tabletop.Protocol/Tabletop.Protocol.csproj" />
    <ProjectReference Include="../Tabletop.Content.Tests/Tabletop.Content.Tests.csproj" />
    <ProjectReference Include="../Tabletop.Protocol.Tests/Tabletop.Protocol.Tests.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

The two test-project references bring `ArmyTests.Sample()`, `TableStateTests.Sample()` and `Samples` in.

- [ ] **Step 3: Write the tests that pin the references and the one table frame**

```csharp
using Tabletop.Content.Tests;
using Tabletop.Geometry;

namespace Tabletop.Client.Tests;

/// <summary>Smoke tests for Task 1: the three libs and the two sample projects resolve, and the geometry library answers in the wire frame — corner origin, 0 ≤ x ≤ width, 0 ≤ y ≤ length — so nothing in the client converts.</summary>
public class ReferencesTests
{
    static readonly TableSize Opr = new(72, 48);

    [Fact] public void Zone_polygons_are_in_the_wire_frame()
    {
        var zone = ZoneStyles.Resolve("frontline/1", Opr)[0];
        Assert.Equal(36, zone.Vertices.Min(v => v.Y), 6);
        Assert.Equal(48, zone.Vertices.Max(v => v.Y), 6);
        Assert.Equal(0, zone.Vertices.Min(v => v.X), 6);
    }
    [Fact] public void On_slab_is_the_game_servers_rule()
    {
        Assert.True(Table.ContainsPoint(Opr, new Point2(72, 48)));
        Assert.False(Table.ContainsPoint(Opr, new Point2(72.5, 48)));
        Assert.False(Table.ContainsPoint(Opr, new Point2(-1, 10)));
    }
    [Fact] public void The_sample_table_state_is_on_the_slab()
    {
        var sample = TableStateTests.Sample();
        Assert.All(sample.Terrain, t => Assert.True(Table.ContainsPoint(sample.Size, t.Position.Horizontal)));
        Assert.All(sample.Markers, m => Assert.True(Table.ContainsPoint(sample.Size, m.Position.Horizontal)));
    }
}
```

- [ ] **Step 4: Add the test project to the solution and run; expect 3 passed (the references resolve; `client/src/Core/` is still empty, which the `Compile Include` glob tolerates)**

```bash
cd /home/cstupi/src/tabletop
dotnet sln Tabletop.sln add --solution-folder tests tests/Tabletop.Client.Tests/Tabletop.Client.Tests.csproj
dotnet test tests/Tabletop.Client.Tests
```

- [ ] **Step 5: Write `scripts/check-client`** and `chmod +x` it

```bash
#!/usr/bin/env bash
# The client shell spec's two scripted checks: no physics body in any scene, no rules type in any client script.
set -euo pipefail
cd "$(dirname "$0")/.."
status=0
if grep -rnE 'type="(StaticBody3D|RigidBody3D|CharacterBody3D|Area3D|CollisionShape3D|CollisionPolygon3D)"' client/scenes; then
    echo "check-client: a scene contains a physics body or collision shape" >&2; status=1
fi
if grep -rnE '\b(StaticBody3D|RigidBody3D|CharacterBody3D|Area3D|CollisionShape3D|PhysicsServer3D|PhysicsBody3D)\b' client/src; then
    echo "check-client: a client script uses Godot physics" >&2; status=1
fi
# Rules-engine types only. `GameRules` alone is not one: it is a string field on GameInfo and
# PropertiesSummary that the Home screen displays, so matching the bare word is a false positive.
if grep -rnEs 'Tabletop\.GameServer|\bIGameRules\b|\bGameRulesRegistry\b|\bIRulesEngine\b|\bEnforcementPolicy\b|\bLegalSetMembership\b|\bCommandDispatcher\b|\bLegality\b' client/src; then
    echo "check-client: a client script references a rules type" >&2; status=1
fi
if grep -rlns 'using Godot' client/src/Core; then
    echo "check-client: client/src/Core must be Godot-free" >&2; status=1
fi
exit $status
```

- [ ] **Step 6: Run `dotnet test tests/Tabletop.Client.Tests` (expect 3 passed), `dotnet build` (the client builds in WSL with the references), and `./scripts/check-client` (prints nothing, exits 0). Then from the Windows host: `./scripts/godot -e`, let the import finish, build from the editor; confirm `ls libs/Tabletop.Geometry/obj` shows `Unix/` and `Windows_NT/`.**

- [ ] **Step 7: Commit**

```bash
git add client/Tabletop.csproj tests/Tabletop.Client.Tests scripts/check-client Tabletop.sln
git commit -m "Reference the contract libraries from the client and test its Godot-free core"
```
