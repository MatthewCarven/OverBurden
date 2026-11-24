using Overburden.Core.Content;
using Overburden.Core.Machines;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-6b, decision 16: <c>--placeat=</c> to collapse decision 13's grammar, so f-2 reuses it, and <c>--deposit=</c>.
/// </summary>
public class HeadlessDoorsTests
{
    [Fact]
    public void APlaceAtReadsTheIdTheVoxelAndThePortsInOrder()
    {
        Assert.True(PlaceAt.TryParse("overburden:quantum_vault/high@12,-40,7,channel=3,enabled=0", out PlaceAt at, out string? error), error);
        Assert.Equal(Identifier.Parse("overburden:quantum_vault/high"), at.Material);
        Assert.Equal(new VoxelPos(12, -40, 7), at.Voxel);
        Assert.Equal([("channel", 3L), ("enabled", 0L)], at.Ports);
    }

    [Fact]
    public void ABarePathIsTheBaseGamesAndPortsAreOptional()
    {
        Assert.True(PlaceAt.TryParse("vacuum_cell@0,0,0", out PlaceAt at, out _));
        Assert.Equal(Identifier.Parse("overburden:vacuum_cell"), at.Material);
        Assert.Empty(at.Ports);

        // A mod's sensor, f-2's arm.
        Assert.True(PlaceAt.TryParse("amod:sensor@1,2,3,radius=2", out PlaceAt sensor, out _));
        Assert.Equal("amod", sensor.Material.Namespace);
        Assert.Equal([("radius", 2L)], sensor.Ports);
    }

    [Theory]
    [InlineData("")]
    [InlineData("vault")]
    [InlineData("@1,2,3")]
    [InlineData("vault@1,2")]
    [InlineData("vault@1,2,x")]
    [InlineData("vault@1.5,2,3")]
    [InlineData("vault@1,2,3,channel")]
    [InlineData("vault@1,2,3,channel=three")]
    [InlineData("vault@1,2,3,=3")]
    [InlineData("Bad Id@1,2,3")]
    public void AMalformedPlaceAtIsRefusedWithAReason(string text)
    {
        Assert.False(PlaceAt.TryParse(text, out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void ADepositReadsTheIdTheCountAndAnOptionalCell()
    {
        Assert.True(DepositAt.TryParse("stone:6400", out DepositAt bare, out string? error), error);
        Assert.Equal(Identifier.Parse("overburden:stone"), bare.Material);
        Assert.Equal(6_400, bare.Voxels);
        Assert.Null(bare.Voxel);

        // The count is after the last colon, so a namespaced ID keeps its own.
        Assert.True(DepositAt.TryParse("overburden:clay:64@4,5,-6", out DepositAt named, out error), error);
        Assert.Equal(Identifier.Parse("overburden:clay"), named.Material);
        Assert.Equal(64, named.Voxels);
        Assert.Equal(new VoxelPos(4, 5, -6), named.Voxel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("stone")]
    [InlineData("overburden:stone")]
    [InlineData("stone:0")]
    [InlineData("stone:-5")]
    [InlineData("stone:lots")]
    [InlineData(":64")]
    [InlineData("stone:64@1,2")]
    [InlineData("stone:64@1,2,3,4")]
    public void AMalformedDepositIsRefusedWithAReason(string text)
    {
        Assert.False(DepositAt.TryParse(text, out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
