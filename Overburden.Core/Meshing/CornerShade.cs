namespace Overburden.Core.Meshing;

/// <summary>
/// One quad's four corner shades — a (skylight × AO, blocklight × AO) byte pair per corner —
/// packed into eight bytes so the merge key stays a single comparison.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two channels rather than one pre-multiplied value, and DESIGN is what forces it.</b>
/// With the directional light gone the grid is the only light, and DESIGN's M7 needs skylight
/// on its own — <i>"wet-surface shader term driven by skylight × recent rainfall"</i> — which a
/// pre-multiplied <c>sky × block × AO</c> cannot supply. AO multiplies into <em>both</em>,
/// because AO is geometric darkening and geometry does not care where the photon came from.
/// That keeps AO out of the shader entirely, and it means a diurnal cycle is a uniform
/// scaling the sky channel rather than a reason to reflood the light grid every frame.
/// </para>
/// <para>
/// <b>Corner order is the quad's own</b>, the same order <see cref="MeshBuffer.AddQuad"/>
/// emits vertices in: 0 = <c>p</c>, 1 = <c>p+du</c>, 2 = <c>p+du+dv</c>, 3 = <c>p+dv</c>.
/// </para>
/// <para>
/// Both channels are constant at M5a — skylight is full everywhere and nothing emits block
/// light until M5b — so only AO varies. They are in the merge key from the start anyway, the
/// same way light and AO were in it from M1c: light drops into these bytes at M5c with no
/// structural change to the sweep.
/// </para>
/// </remarks>
public readonly record struct CornerShade(ulong Packed)
{
    /// <summary>Corners per quad.</summary>
    public const int Corners = 4;

    private const int BitsPerCorner = 16;

    /// <summary>Full skylight, no block light — every corner. The no-AO key.</summary>
    public static readonly CornerShade FullSky = Uniform(byte.MaxValue, 0);

    /// <summary>The same pair at all four corners.</summary>
    public static CornerShade Uniform(byte sky, byte block)
    {
        ulong pair = sky | ((ulong)block << 8);
        return new CornerShade(pair | (pair << 16) | (pair << 32) | (pair << 48));
    }

    /// <summary>Four corners' pairs, in quad corner order.</summary>
    public static CornerShade FromCorners(ReadOnlySpan<byte> sky, ReadOnlySpan<byte> block)
    {
        if (sky.Length != Corners || block.Length != Corners)
        {
            throw new ArgumentException($"Expected {Corners} corners.", nameof(sky));
        }

        ulong packed = 0;
        for (int corner = 0; corner < Corners; corner++)
        {
            ulong pair = sky[corner] | ((ulong)block[corner] << 8);
            packed |= pair << (corner * BitsPerCorner);
        }

        return new CornerShade(packed);
    }

    public byte Sky(int corner) => (byte)(Packed >> (corner * BitsPerCorner));

    public byte Block(int corner) => (byte)(Packed >> (corner * BitsPerCorner + 8));

    /// <summary>Both channels at one corner. What the diagonal flip compares.</summary>
    public int Total(int corner) => Sky(corner) + Block(corner);

    /// <summary>
    /// Whether this quad must be split along its 1–3 diagonal rather than the default 0–2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A quad whose corners are not planar in <em>shade</em> renders differently depending on
    /// which diagonal splits it into triangles: the two corners on the split diagonal set the
    /// value at the quad's centre and the other two are ignored there. Left alone, the
    /// artefact is a visible crease running one way along every lit floor.
    /// </para>
    /// <para>
    /// <b>M5-PLAN states the rule on AO alone; this compares the total shade, which is the
    /// same rule at M5a and the right one afterwards.</b> At M5a block light is zero and the
    /// sky channel is AO-monotone, so the two are identical. At M5c a flat floor has constant
    /// AO and varying block light, and an AO-only test would never fire on it — leaving
    /// exactly the crease the flip exists to remove, on the surface most likely to show it.
    /// The comparison is a pure function of the merge key either way, so every face merged
    /// into a quad agrees about which diagonal that quad takes.
    /// </para>
    /// </remarks>
    public bool FlipDiagonal => Total(0) + Total(2) > Total(1) + Total(3);
}
