using System.Globalization;
using Overburden.Core.Content;
using Overburden.Core.Voxels;

namespace Overburden.Core.Machines;

/// <summary>
/// <c>--placeat=&lt;id&gt;@x,y,z[,port=value…]</c> (collapse decision 13's grammar; IM-6's pass, decision 16): a machine
/// placed at a voxel with its ports set — the headless oracles' way in, IM-6's storm and collapse f-2's sensor.
/// </summary>
/// <param name="Material">The machine's material, namespaced; a bare path is the base game's.</param>
/// <param name="Voxel">A voxel in the brush cell the body fills.</param>
/// <param name="Ports">Each <c>port=value</c>, in the order given, written through the set's one write after the body is placed.</param>
public readonly record struct PlaceAt(Identifier Material, VoxelPos Voxel, IReadOnlyList<(string Name, long Value)> Ports)
{
    /// <summary>Reads <paramref name="text"/>, the flag's value, or says why it cannot.</summary>
    public static bool TryParse(string? text, out PlaceAt placeAt, out string? error)
    {
        placeAt = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "wants <id>@x,y,z[,port=value...]";
            return false;
        }

        int at = text.IndexOf('@');
        if (at <= 0)
        {
            error = "wants <id>@x,y,z[,port=value...]: no '@' after the ID";
            return false;
        }

        if (!Identifier.TryParse(text[..at], Identifier.BaseNamespace, out Identifier material))
        {
            error = $"'{text[..at]}' is not an ID";
            return false;
        }

        string[] parts = text[(at + 1)..].Split(',');
        if (parts.Length < 3 || !HeadlessDoors.TryVoxel(parts[0], parts[1], parts[2], out VoxelPos voxel))
        {
            error = "wants three whole voxel coordinates after the '@'";
            return false;
        }

        var ports = new List<(string, long)>();
        for (int i = 3; i < parts.Length; i++)
        {
            int equals = parts[i].IndexOf('=');
            if (equals <= 0 || !long.TryParse(parts[i][(equals + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                error = $"'{parts[i]}' is not port=value";
                return false;
            }

            ports.Add((parts[i][..equals], value));
        }

        placeAt = new PlaceAt(material, voxel, ports);
        error = null;
        return true;
    }
}

/// <summary>
/// <c>--deposit=&lt;id&gt;:&lt;voxels&gt;[@x,y,z]</c> (IM-6's pass, decision 16): a material moved from the pocket into a
/// placed vault or crate through the store's own door, after the restore, <c>--stock=</c> and every <c>--placeat=</c>.
/// With no cell, the vault the last <c>--placeat=</c> placed.
/// </summary>
/// <param name="Material">The material, namespaced; a bare path is the base game's.</param>
/// <param name="Voxels">How many voxels to move, at most what the pocket carries.</param>
/// <param name="Voxel">A voxel in the box's cell, or null for the last <c>--placeat=</c>.</param>
public readonly record struct DepositAt(Identifier Material, long Voxels, VoxelPos? Voxel)
{
    /// <summary>Reads <paramref name="text"/>, the flag's value, or says why it cannot.</summary>
    /// <remarks>The count is after the <em>last</em> colon before any <c>@</c>, so a namespaced ID keeps its own.</remarks>
    public static bool TryParse(string? text, out DepositAt deposit, out string? error)
    {
        deposit = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "wants <id>:<voxels>[@x,y,z]";
            return false;
        }

        int at = text.IndexOf('@');
        string head = at < 0 ? text : text[..at];
        int colon = head.LastIndexOf(':');
        if (colon <= 0
            || !long.TryParse(head[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long voxels)
            || voxels <= 0)
        {
            error = "wants <id>:<voxels>[@x,y,z], with a count above zero";
            return false;
        }

        if (!Identifier.TryParse(head[..colon], Identifier.BaseNamespace, out Identifier material))
        {
            error = $"'{head[..colon]}' is not an ID";
            return false;
        }

        VoxelPos? where = null;
        if (at >= 0)
        {
            string[] parts = text[(at + 1)..].Split(',');
            if (parts.Length != 3 || !HeadlessDoors.TryVoxel(parts[0], parts[1], parts[2], out VoxelPos voxel))
            {
                error = "wants three whole voxel coordinates after the '@'";
                return false;
            }

            where = voxel;
        }

        deposit = new DepositAt(material, voxels, where);
        error = null;
        return true;
    }
}

/// <summary>What the two headless doors share.</summary>
internal static class HeadlessDoors
{
    internal static bool TryVoxel(string x, string y, string z, out VoxelPos voxel)
    {
        voxel = default;
        if (!int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out int vx)
            || !int.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out int vy)
            || !int.TryParse(z, NumberStyles.Integer, CultureInfo.InvariantCulture, out int vz))
        {
            return false;
        }

        voxel = new VoxelPos(vx, vy, vz);
        return true;
    }
}
