using Overburden.Core.Voxels;

namespace Overburden.Core.Navigation;

/// <summary>
/// Which world coordinate each pixel of the map is, and the inverse.
/// </summary>
/// <remarks>
/// <para>
/// <b>The map is drawn facing-up</b> — the top of the image is the direction the player is looking
/// — which is Matthew's ask and the right one: walking to a thing you can see on the map is "up
/// and a bit left", where a north-up map makes you do the rotation in your head at every glance.
/// A north tick on the rim and the HUD's own coordinates keep the absolute frame available.
/// </para>
/// <para>
/// <b>Rotating costs nothing and loses nothing here, which is unusual.</b> The terrain is an
/// analytic function of two coordinates, so sampling along a rotated axis is just sampling — there
/// is no texture to rotate, nothing to resample, no blurring, and no black corners. A facing-up
/// map is exactly as sharp as a north-up one.
/// </para>
/// <para>
/// <b>The player is the centre pixel</b>, at index <see cref="Size"/> / 2, rather than the
/// geometric centre of an even-sized image, which falls between four pixels. One pixel of
/// asymmetry on a rotating map is invisible; "the middle pixel is where you are standing" is a
/// property a test can state and a marker can be drawn on.
/// </para>
/// </remarks>
/// <param name="CentreVoxelX">Where the player is, in world voxels.</param>
/// <param name="CentreVoxelZ">Where the player is, in world voxels.</param>
/// <param name="VoxelsPerPixel">How much ground one pixel covers. 64 is 16 m, the shipped scale.</param>
/// <param name="Size">Pixels on a side.</param>
/// <param name="BearingDegrees">Which way is up: a compass bearing, from <see cref="Compass"/>.</param>
public readonly record struct MapProjection(
    int CentreVoxelX,
    int CentreVoxelZ,
    int VoxelsPerPixel,
    int Size,
    float BearingDegrees)
{
    /// <summary>
    /// The shipped scale: 16 m a pixel, so a 128-pixel map is 2 km — one region across.
    /// </summary>
    /// <remarks>
    /// <b>Matthew's, 2026-09-23</b>, from the two choices the pass handed him. The fit is with
    /// <see cref="Worldgen.WorldGenerator.ReliefFrequencyRatio"/>: a region is about 2 km, so a
    /// map at this scale answers "which way are the hills" with one region in view. Sampling cost
    /// does not change with the scale — 128 × 128 samples is 128 × 128 samples whether a pixel is
    /// 4 m or 4 km — so the zoom range is a question about what reads well and never about speed.
    /// </remarks>
    public const int DefaultVoxelsPerPixel = 16 * VoxelConstants.VoxelsPerMetre;

    public const int DefaultSize = 128;

    /// <summary>The pixel the player stands on, on both axes.</summary>
    public int CentrePixel => Size / 2;

    /// <summary>Metres of ground across the whole map.</summary>
    public float SpanMetres => (float)Size * VoxelsPerPixel / VoxelConstants.VoxelsPerMetre;

    /// <summary>The world direction the top of the map points in, as a unit (x, z).</summary>
    public (float X, float Z) Forward
    {
        get
        {
            float radians = BearingDegrees * (MathF.PI / 180.0f);
            return (MathF.Sin(radians), -MathF.Cos(radians));
        }
    }

    /// <summary>The world direction the right of the map points in — forward turned 90° clockwise.</summary>
    public (float X, float Z) Right
    {
        get
        {
            (float x, float z) = Forward;
            return (-z, x);
        }
    }

    /// <summary>The world voxel under a pixel. Fractional pixels are allowed and land between voxels.</summary>
    public (float X, float Z) WorldAt(float pixelX, float pixelY)
    {
        (float rx, float rz) = Right;
        (float fx, float fz) = Forward;

        // Image Y grows downward and the top of the image is forward, so the forward component
        // is negated. Getting this wrong mirrors the map north-to-south, which on a rotating map
        // looks like a map right up until you walk the wrong way.
        float alongRight = (pixelX - CentrePixel) * VoxelsPerPixel;
        float alongForward = (CentrePixel - pixelY) * VoxelsPerPixel;

        return (
            CentreVoxelX + (alongRight * rx) + (alongForward * fx),
            CentreVoxelZ + (alongRight * rz) + (alongForward * fz));
    }

    /// <summary>The pixel a world voxel falls on. Outside 0..Size-1 when it is off the map.</summary>
    /// <remarks>
    /// Exact rather than approximate, because <see cref="Right"/> and <see cref="Forward"/> are
    /// orthonormal: a dot product against each recovers the components a rotation put there. This
    /// is what places a machine's pin and the mine's marks.
    /// </remarks>
    public (float X, float Y) PixelAt(float worldVoxelX, float worldVoxelZ)
    {
        (float rx, float rz) = Right;
        (float fx, float fz) = Forward;

        float dx = worldVoxelX - CentreVoxelX;
        float dz = worldVoxelZ - CentreVoxelZ;

        float alongRight = ((dx * rx) + (dz * rz)) / VoxelsPerPixel;
        float alongForward = ((dx * fx) + (dz * fz)) / VoxelsPerPixel;

        return (CentrePixel + alongRight, CentrePixel - alongForward);
    }
}
