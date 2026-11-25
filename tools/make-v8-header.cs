#:project ../Overburden.Core/Overburden.Core.csproj
#:property PublishAot=false

// Writes a golden v8 world header with the v8 writer itself: IDEAL-MACHINES-PLAN, IM-6's pass, decision 19.
//
// Run it from the repository root, on a tree whose writer is still v8:
//
//     dotnet run tools/make-v8-header.cs
//
// It writes Overburden.Core.Tests/Fixtures/v8-header-before-the-mode.bin (or the path given as its one argument), and
// refuses to run unless WorldHeaderBlob.Version is 8, because a "v8 fixture" written by a v9 writer is a v9 header and
// tests nothing. The v7 fixture's program was never kept (alpha session C); this one is, outside every project, so the
// next bump copies it, changes the version and the fields it adds, and runs it *before* touching the writer.
//
// What it holds, so the reading test can say what it expects:
// - the shipped base game's table, as a new world assigns it, from Overburden.Game/mods;
// - a row that is not ID order: stone, glass, clay, the vacuum cell, an empty slot, the high vault (a container, only
//   ever on the row by a pin), the relay pump held at zero (a slot keeps its material), and two empty slots;
// - seven holdings, among them a computer and two lithium-ion packs;
// - a lead-acid pack worn on channel 3, and one charged lithium-ion pack carried;
// - a player flying at (12.5, 40.25, -630.75), 3,723,000 ms of world time, and LakeTest2's seed.

using System.Numerics;
using System.Security.Cryptography;
using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Persistence;
using Overburden.Core.Worldgen;

// A constant compared with a constant, so the compiler calls one branch unreachable; that is the point of the check.
#pragma warning disable CS0162
if (WorldHeaderBlob.Version != 8)
{
    Console.Error.WriteLine(
        $"make-v8-header: this tree's writer is v{WorldHeaderBlob.Version}, not v8. A v8 fixture must come from the v8 "
        + "writer; check out the commit before the bump, or copy this program for the next version.");
    return 1;
}
#pragma warning restore CS0162

string root = Directory.GetCurrentDirectory();
string mods = Path.Combine(root, "Overburden.Game", "mods");
if (!Directory.Exists(mods))
{
    Console.Error.WriteLine($"make-v8-header: no {mods}; run it from the repository root.");
    return 1;
}

var content = new GameContent();
LoadResult loaded = content.Load(new DirectoryContentSource(mods));
if (!loaded.Success)
{
    Console.Error.WriteLine($"make-v8-header: the base game did not load: {string.Join("; ", loaded.Errors)}");
    return 1;
}

int Id(string path) => content.Materials.GetNumericId(Identifier.Parse($"overburden:{path}"));
const long Cell = 64;

int stone = Id("stone");
int glass = Id("glass");
int clay = Id("clay");
int cell = Id("vacuum_cell");
int vault = Id("quantum_vault/high");
int pump = Id("relay_pump");
int computer = Id("computer");
int leadAcid = Id("lead_acid_pack");
int lithium = Id("lithium_ion_pack");

var header = new WorldHeader
{
    Settings = new WorldGenSettings { Seed = 0xD8FF0AD },
    MaterialIdTable = content.Materials.ExportIdTable(),
    Inventory =
    [
        new InventoryEntry(stone, 640 * Cell),
        new InventoryEntry(glass, 12 * Cell),
        new InventoryEntry(clay, 30 * Cell),
        new InventoryEntry(cell, 2 * Cell),
        new InventoryEntry(vault, 1 * Cell),
        new InventoryEntry(computer, 1),
        new InventoryEntry(lithium, 2),
    ],
    Player = new PlayerSave(new Vector3(12.5f, 40.25f, -630.75f), Flying: true),
    ElapsedMillis = 3_723_000,
    Pack = new PackSave(leadAcid, 1_234_567_000, Channel: 3),
    CarriedPacks = [new PackSave(lithium, 9_876_543_000, Channel: 0)],
    Hotbar = [stone, glass, clay, cell, HotbarSlots.Empty, vault, pump, HotbarSlots.Empty, HotbarSlots.Empty],
};

byte[] blob = WorldHeaderBlob.Write(header);
WorldHeader back = WorldHeaderBlob.Read(blob);
if (!back.Hotbar.SequenceEqual(header.Hotbar) || back.Inventory.Count != header.Inventory.Count || back.Pack != header.Pack)
{
    Console.Error.WriteLine("make-v8-header: the v8 writer's blob does not read back as written; nothing saved.");
    return 1;
}

string output = args.Length > 0
    ? args[0]
    : Path.Combine(root, "Overburden.Core.Tests", "Fixtures", "v8-header-before-the-mode.bin");
File.WriteAllBytes(output, blob);

Console.WriteLine(
    $"make-v8-header: wrote {output} — {blob.Length} bytes, version {blob[0]}, "
    + $"{header.MaterialIdTable.Count} materials in the table, SHA-256 {Convert.ToHexString(SHA256.HashData(blob))}");
return 0;
