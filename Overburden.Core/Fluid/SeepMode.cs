namespace Overburden.Core.Fluid;

/// <summary>
/// Where a chunk's seepage lands: one running spring at its lowest face, or the whole wall
/// sweating. The rate is the same on both; see <see cref="SeepDrain"/>.
/// </summary>
/// <remarks>
/// A constructor argument like <see cref="SpreadRule"/>, and a play arm like it: <c>spring</c> is
/// what was played first and read as *"more like a tap"*, kept as an arm because that is how hard
/// rock lets water in; <c>sweat</c> is the arm Matthew then asked for, played, and liked — *"I
/// like this sweating"* — and it ships as the game's default from 2026-09-11, being also the
/// cheaper arm for the automaton (drops rest, a stream runs). The eventual rule is per material and
/// needs no field: a rock's permeability says whether it springs or sweats.
/// </remarks>
public enum SeepMode
{
    /// <summary>The chunk's rate pooled and landed lowest-face-first: a floor spring before a wall weep.</summary>
    Spring,

    /// <summary>Each group owes its own litres and hands each to the next face in it with room.</summary>
    Sweat,
}
