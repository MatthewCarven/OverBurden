using System;

namespace Overburden.Game.Player;

/// <summary>
/// One key with two meanings: a press released inside the threshold is a <em>tap</em>, a press
/// held past it is a <em>hold</em>, and a hold never taps.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built for Q (2026-09-19) — tapped puts the quantum miner on the mouse, held runs it — and
/// the shape E takes at M10g</b>, where tapped is the inventory screen and held is the crank
/// (the ladder's decision 11). One helper rather than two, because the trap is the same on both
/// keys: a toggle on the key-down edge cannot share the key with a hold, or every hold flips the
/// mode on its way in. So the tap moves to the <em>release</em> and costs up to
/// <see cref="HoldSeconds"/> of latency, which is the price of the second meaning.
/// </para>
/// <para>
/// Polled rather than event-driven, like <c>P</c>, <c>R</c> and <c>L</c> in <c>Main</c>: fed the
/// key's state once a frame with the frame's <c>delta</c>. Deliberately free of Godot types for
/// the reason <see cref="MiningController"/> is — what a held key means is input state, and
/// input state wants to be readable without a scene.
/// </para>
/// </remarks>
public sealed class TapOrHold
{
    /// <summary>The default threshold — an estimate; <c>--holdafter=</c> is its sweep.</summary>
    public const double DefaultHoldSeconds = 0.25;

    /// <summary>How long a press must last to be a hold rather than a tap.</summary>
    public double HoldSeconds { get; set; } = DefaultHoldSeconds;

    /// <summary>Whether the key is held past the threshold now.</summary>
    public bool Holding { get; private set; }

    /// <summary>Seconds the key has been down, or a negative number while it is up.</summary>
    private double _downFor = -1.0;

    /// <summary>Whether this press has been a hold at any point, so its release cannot tap.</summary>
    private bool _wasHold;

    /// <summary>
    /// Feeds one frame. Returns true on the frame a tap completed — the release of a press
    /// that never became a hold.
    /// </summary>
    public bool Update(bool pressed, double delta)
    {
        if (delta < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), delta, "A frame cannot run backwards.");
        }

        if (pressed)
        {
            // The first frame down counts from zero, so a threshold of zero is a hold at once and
            // the default is a quarter-second of nothing before the first bite.
            _downFor = _downFor < 0.0 ? 0.0 : _downFor + delta;
            Holding = _downFor >= HoldSeconds;
            _wasHold |= Holding;
            return false;
        }

        bool tapped = _downFor >= 0.0 && !_wasHold;
        _downFor = -1.0;
        Holding = false;
        _wasHold = false;
        return tapped;
    }
}
