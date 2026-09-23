namespace Overburden.Core.Navigation;

/// <summary>
/// Which way is north, and how to say a direction out loud.
/// </summary>
/// <remarks>
/// <para>
/// <b>North is −Z and east is +X.</b> That is Godot's own forward axis and the convention every
/// map this project prints already uses, and it is written down here — in Core, with tests —
/// rather than inferred at each call site, because the cost of getting it wrong is telling
/// somebody to walk the opposite way.
/// </para>
/// <para>
/// <b>A bearing is taken from a forward vector rather than from a yaw</b>, deliberately. A yaw
/// needs a sign convention and a zero point to mean anything, and reasoning about those correctly
/// is exactly the kind of thing that is wrong half the time and invisible in a screenshot — a
/// compass pointing consistently in the wrong direction looks like a compass. A forward vector is
/// the thing itself, whatever the engine does with angles.
/// </para>
/// </remarks>
public static class Compass
{
    private static readonly string[] Points =
    [
        "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
        "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW",
    ];

    /// <summary>
    /// The compass bearing of a horizontal direction, in degrees clockwise from north: 0 is
    /// north, 90 east, 180 south, 270 west.
    /// </summary>
    /// <remarks>
    /// A direction of zero length has no bearing and is reported as north, because a readout has
    /// to print something and a player looking straight down is not pointing anywhere.
    /// </remarks>
    public static float BearingDegrees(float forwardX, float forwardZ)
    {
        if (forwardX == 0.0f && forwardZ == 0.0f)
        {
            return 0.0f;
        }

        float degrees = MathF.Atan2(forwardX, -forwardZ) * (180.0f / MathF.PI);
        return degrees < 0.0f ? degrees + 360.0f : degrees;
    }

    /// <summary>The nearest of sixteen named points, so a bearing reads as a direction.</summary>
    public static string Point(float bearingDegrees)
    {
        float bearing = bearingDegrees % 360.0f;
        if (bearing < 0.0f)
        {
            bearing += 360.0f;
        }

        return Points[(int)MathF.Round(bearing / 22.5f) % 16];
    }
}
