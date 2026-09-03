using Godot;
using Overburden.Core.Lighting;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;

namespace Overburden.Game.Rendering;

/// <summary>
/// A single-channel texture of "how high is the ground here" around the player, so a particle
/// shader can kill rain that has reached it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the whole of "rain stops at the mine entrance", which DESIGN calls the detail people
/// notice.</b> The alternative — collision — is per particle against a voxel world on the GPU, and
/// there is no version of that which costs one texture read. What makes the cheap answer exact
/// enough is that the field is built from <see cref="SkyColumn"/>, which is <em>edited</em> height
/// rather than analytic: dig a shaft and its columns open downward, so the rain follows it in with
/// no special case anywhere. <b>M5b built that structure a milestone early and said this is what it
/// was for.</b>
/// </para>
/// <para>
/// <b>The resolution is the decision, and 1 m would have been wrong for a reason that is easy to
/// miss.</b> A <c>hand_pick</c> bore is 0.75 m — three voxels — so the shafts this game is about
/// are <i>narrower than a metre</i>, and a field sampled every metre would miss the one case the
/// feature exists for. At 0.5 m each texel covers a 2×2 block of voxel columns and takes the
/// <b>minimum</b> of the four, so any opening at least two voxels across in both axes is found.
/// Taking the minimum rather than point-sampling one of the four is what makes that a guarantee
/// instead of an argument about which parity the samples land on — and it survives the resolution
/// being retuned, which the parity argument does not.
/// <b>Revisit trigger: a shaft rain should fall into and does not.</b>
/// </para>
/// <para>
/// <b>A texel with no loaded column is written as unknown rather than guessed</b>, and the shader
/// does not kill on unknown. The tempting alternative is the analytic surface height, which is
/// available everywhere and needs no chunk — but it is ~55 ns a sample against an array read, so
/// filling this field from it would cost milliseconds on the sim thread every time the player
/// crossed a chunk. At the view distances this is played at the window is inside the loaded region
/// anyway; the visible cost is that a small-radius profiling run sees rain fall past the edge of
/// the world, which is a profiling artefact rather than a defect.
/// </para>
/// <para>
/// <b>The field is deliberately larger than the rain box it serves.</b> 128 m against 48 m, so the
/// box is a look constant that can be retuned without anybody having to think about the field.
/// </para>
/// </remarks>
public sealed class SkyHeightField
{
    /// <summary>Texels on a side. 128 × 0.5 m is a 64 m window.</summary>
    /// <remarks>
    /// <b>The plan said 128 m, and the measurement moved it.</b> At 256² the refill cost 2.5 ms
    /// mean and 3.6 ms worst on the sim thread — ten times the plan's ~0.25 ms estimate, and
    /// enough to roughly double the cost of a chunk crossing next to <c>viewer ms</c>'s 4.7 ms.
    /// The window was only that large so the rain box could be retuned without touching the field,
    /// which is a convenience rather than a requirement: 64 m still clears the 48 m box by 8 m on
    /// every side. <b>The resolution — the decision the plan actually argued — is untouched.</b>
    /// </remarks>
    public const int Size = 128;

    /// <summary>Metres per texel. See the remarks — one metre is wider than a starting bore.</summary>
    public const float MetresPerTexel = 0.5f;

    /// <summary>Voxel columns each texel covers on a side. 0.5 m / 0.25 m.</summary>
    private const int VoxelsPerTexel = (int)(MetresPerTexel * VoxelConstants.VoxelsPerMetre);

    /// <summary>Metres across the whole field.</summary>
    public const float SpanMetres = Size * MetresPerTexel;

    /// <summary>
    /// What an unknown texel holds. Far below anything, so the shader's "kill below the ground"
    /// test is false there without needing a second test for validity.
    /// </summary>
    /// <remarks>
    /// A sentinel rather than a companion mask, because the comparison it feeds is
    /// <c>y &lt; height</c> and a value no <c>y</c> can be under is exactly the same statement as
    /// "do not kill". A mask would be a second texture fetch to say something the first one can.
    /// </remarks>
    public const float UnknownHeight = -1e9f;

    private readonly float[] _heights = new float[Size * Size];
    private readonly Image _image;

    public SkyHeightField()
    {
        _image = Image.CreateEmpty(Size, Size, false, Image.Format.Rf);
        Texture = ImageTexture.CreateFromImage(_image);
    }

    /// <summary>The field, as the process shader samples it.</summary>
    public ImageTexture Texture { get; }

    /// <summary>World X of the field's lower corner, in metres.</summary>
    public float OriginX { get; private set; }

    /// <summary>World Z of the field's lower corner, in metres.</summary>
    public float OriginZ { get; private set; }

    /// <summary>How many texels the last rebuild could not answer for.</summary>
    /// <remarks>
    /// <b>Printed beside the rebuild count rather than kept private</b>, because "the rain fell
    /// through the ground" and "the field was never built" look identical from outside and this is
    /// what tells them apart. A large share at a small view radius is expected and is the recorded
    /// artefact; a large share at r16 is a bug.
    /// </remarks>
    public int UnknownTexels { get; private set; }

    /// <summary>How many times the field has been filled this session.</summary>
    public int Rebuilds { get; private set; }

    /// <summary>Milliseconds the last rebuild took.</summary>
    public double LastRebuildMillis { get; private set; }

    /// <summary>The lowest and highest known ground in the field, in metres.</summary>
    /// <remarks>
    /// <b>Printed because "the field is a terrain" and "the field is a flat plane" are the two
    /// ways this can be built and only one of them is right</b>, and a screenshot cannot tell them
    /// apart: rain stopping at a plane a metre below the player looks exactly like rain stopping
    /// at the ground, from anywhere the ground happens to be a metre below the player. A spread of
    /// a few metres over a 128 m window is terrain; a spread of zero is a bug that would otherwise
    /// have shipped looking correct.
    /// </remarks>
    public float LowestKnown { get; private set; }

    public float HighestKnown { get; private set; }

    /// <summary>
    /// Refills the field centred on a chunk, and uploads it.
    /// </summary>
    /// <remarks>
    /// <b>Centred on a chunk rather than on the camera, so it changes only on a crossing.</b> A
    /// field that followed the camera continuously would re-upload every frame to say almost
    /// exactly the same thing, and would make the kill height under a standing player jitter by a
    /// texel as they shifted their weight.
    /// </remarks>
    public void Rebuild(ChunkStreamer streamer, ChunkPos centre)
    {
        System.ArgumentNullException.ThrowIfNull(streamer);

        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        // The lower corner in voxels, snapped so a texel always covers whole voxel columns.
        int originVoxelX = (centre.X << VoxelConstants.ChunkSizeLog2) - (Size * VoxelsPerTexel / 2);
        int originVoxelZ = (centre.Z << VoxelConstants.ChunkSizeLog2) - (Size * VoxelsPerTexel / 2);

        OriginX = originVoxelX * VoxelConstants.VoxelSize;
        OriginZ = originVoxelZ * VoxelConstants.VoxelSize;

        UnknownTexels = 0;

        // Walked chunk column by chunk column rather than texel by texel: the lookup is a
        // dictionary hit and there are 16×16 of them this way against 256×256 the other, while
        // the reads inside are a flat array. Same answer, two orders of magnitude fewer lookups.
        const int TexelsPerChunk = VoxelConstants.ChunkSize / VoxelsPerTexel;

        for (int chunkRow = 0; chunkRow < Size / TexelsPerChunk; chunkRow++)
        {
            int columnVoxelZ = originVoxelZ + (chunkRow * VoxelConstants.ChunkSize);

            for (int chunkCol = 0; chunkCol < Size / TexelsPerChunk; chunkCol++)
            {
                int columnVoxelX = originVoxelX + (chunkCol * VoxelConstants.ChunkSize);

                SkyColumn? column = streamer.ColumnAt(
                    columnVoxelX >> VoxelConstants.ChunkSizeLog2,
                    columnVoxelZ >> VoxelConstants.ChunkSizeLog2);

                FillChunkColumn(column, chunkCol * TexelsPerChunk, chunkRow * TexelsPerChunk);
            }
        }

        float lowest = float.MaxValue;
        float highest = float.MinValue;
        foreach (float height in _heights)
        {
            if (height == UnknownHeight) continue;
            if (height < lowest) lowest = height;
            if (height > highest) highest = height;
        }

        LowestKnown = lowest == float.MaxValue ? 0f : lowest;
        HighestKnown = highest == float.MinValue ? 0f : highest;

        Upload();

        Rebuilds++;
        LastRebuildMillis =
            (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
            / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>Writes one chunk column's worth of texels, or marks them unknown.</summary>
    private void FillChunkColumn(SkyColumn? column, int texelX, int texelZ)
    {
        const int TexelsPerChunk = VoxelConstants.ChunkSize / VoxelsPerTexel;

        if (column is null)
        {
            for (int tz = 0; tz < TexelsPerChunk; tz++)
            {
                int row = (texelZ + tz) * Size;
                for (int tx = 0; tx < TexelsPerChunk; tx++)
                {
                    _heights[row + texelX + tx] = UnknownHeight;
                }
            }

            UnknownTexels += TexelsPerChunk * TexelsPerChunk;
            return;
        }

        for (int tz = 0; tz < TexelsPerChunk; tz++)
        {
            int row = (texelZ + tz) * Size;
            int localZ = tz * VoxelsPerTexel;

            for (int tx = 0; tx < TexelsPerChunk; tx++)
            {
                int localX = tx * VoxelsPerTexel;

                // The minimum of the block, so a bore narrower than a texel is still found.
                int lowest = int.MaxValue;
                for (int dz = 0; dz < VoxelsPerTexel; dz++)
                {
                    for (int dx = 0; dx < VoxelsPerTexel; dx++)
                    {
                        int open = column.OpenAboveY(localX + dx, localZ + dz);
                        if (open < lowest) lowest = open;
                    }
                }

                _heights[row + texelX + tx] = lowest * VoxelConstants.VoxelSize;
            }
        }
    }

    private void Upload()
    {
        // One block copy into a reused buffer rather than SetPixel per texel. SetPixel is a
        // scripting-API call each time and 65,536 of them per crossing would dwarf everything
        // else here; and the buffer is a field rather than a local because it is 256 KB, it is
        // refilled entirely every time, and handing the GC a quarter of a megabyte on every chunk
        // boundary is a collection nobody would connect to walking.
        System.Buffer.BlockCopy(_heights, 0, _bytes, 0, _bytes.Length);

        _image.SetData(Size, Size, false, Image.Format.Rf, _bytes);
        Texture.Update(_image);
    }

    private readonly byte[] _bytes = new byte[Size * Size * sizeof(float)];

    public void Dispose()
    {
        Texture.Dispose();
        _image.Dispose();
    }
}
