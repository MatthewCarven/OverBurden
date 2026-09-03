using System.Numerics;
using Overburden.Core.Content;
using Overburden.Core.Voxels;

namespace Overburden.Core.Physics;

/// <summary>Which faces of the box a resolve pushed back on during a move.</summary>
/// <remarks>
/// Six flags rather than three, because the sign is the whole meaning at two of them:
/// <see cref="NegativeY"/> is <i>standing on something</i> and <see cref="PositiveY"/> is
/// <i>head against something</i>, and M4b does opposite things with them. Aggregated with OR
/// across substeps — a face is blocked if any substep's resolve fired on it.
/// </remarks>
[Flags]
public enum BlockedFaces
{
    None = 0,
    NegativeX = 1 << 0,
    PositiveX = 1 << 1,
    NegativeY = 1 << 2,
    PositiveY = 1 << 3,
    NegativeZ = 1 << 4,
    PositiveZ = 1 << 5,
}

/// <summary>Where a box ended up, and what stopped it.</summary>
/// <param name="Box">The resolved box.</param>
/// <param name="Blocked">Which faces a resolve pushed back on.</param>
/// <param name="Substeps">
/// How many times the full X-Y-Z resolve ran. One while walking, more while falling — see
/// <see cref="VoxelCollider.MaxSubstepMetres"/>.
/// </param>
/// <param name="VoxelQueries">
/// How many <see cref="IVoxelWorld.TryGetMaterial"/> calls the move cost. Reported because
/// M4a guessed this number (~400 per step) and M4d is supposed to check the guess; counting
/// it at the only place it can be counted is cheaper than inferring it later from a profile.
/// </param>
/// <param name="BlockedByUnloaded">
/// Of <paramref name="Blocked"/>, the faces stopped by a voxel whose <b>chunk had not
/// loaded</b> rather than by actual material. Always a subset of <paramref name="Blocked"/>.
/// </param>
/// <remarks>
/// <para>
/// <b><paramref name="BlockedByUnloaded"/> is the signal M4b deliberately did not build.</b>
/// It was left out then because an unread flag with no profiler consuming it is a number
/// waiting to be believed; M4d's fall profile is that consumer. "Stopped by rock" and "stopped
/// by a chunk that has not arrived" look identical to a player and are opposite events — the
/// first is the world working, the second is the player outrunning the streamer, and only the
/// second argues about terminal velocity.
/// </para>
/// <para>
/// A subset rather than a separate answer, so it cannot contradict
/// <paramref name="Blocked"/>: it is aggregated with OR across substeps at the same place and
/// only ever on a face that was already recorded as blocked. A face can appear in
/// <paramref name="Blocked"/> and not here — stopped by rock — but never the reverse.
/// </para>
/// </remarks>
public readonly record struct MoveResult(
    Aabb Box,
    BlockedFaces Blocked,
    int Substeps,
    int VoxelQueries,
    BlockedFaces BlockedByUnloaded = BlockedFaces.None)
{
    /// <summary>
    /// The body is standing on a chunk that has not loaded, rather than on ground.
    /// </summary>
    /// <remarks>
    /// <b>M4d's <i>steps spent held</i>, at the layer that knows.</b> The body is suspended
    /// over world that does not exist yet — it will keep falling once the chunk arrives, and if
    /// this is true for long the player is descending faster than the streamer can produce the
    /// world beneath them. That is the measurement terminal velocity is supposed to be chosen
    /// from.
    /// </remarks>
    public bool HeldByUnloadedFloor => (BlockedByUnloaded & BlockedFaces.NegativeY) != 0;

    /// <summary>Any face was stopped by unloaded world rather than by material.</summary>
    public bool HeldByUnloadedWorld => BlockedByUnloaded != BlockedFaces.None;

    /// <summary>
    /// The −Y resolve fired this step, so the box is resting on something.
    /// </summary>
    /// <remarks>
    /// <b>An output of the solve, not a separate probe.</b> Gravity gives a standing player a
    /// small downward displacement every step, which always resolves against the floor, which
    /// always sets this. There is nothing to keep in sync and it cannot disagree with the
    /// collision that just happened. The cost, stated because M4b's step-up depends on it: X
    /// and Z resolve before Y, so step-up reads the <i>previous</i> step's ground state — one
    /// frame, 16 ms at 60 Hz, against roughly 40 extra voxel reads per step to remove it.
    /// </remarks>
    public bool Grounded => (Blocked & BlockedFaces.NegativeY) != 0;

    /// <summary>The +Y resolve fired — a jump that met a ceiling.</summary>
    public bool HitCeiling => (Blocked & BlockedFaces.PositiveY) != 0;

    /// <summary>An X or Z resolve fired. What M4b's step-up triggers on.</summary>
    public bool BlockedHorizontally =>
        (Blocked & (BlockedFaces.NegativeX | BlockedFaces.PositiveX
                    | BlockedFaces.NegativeZ | BlockedFaces.PositiveZ)) != 0;

    public override string ToString() =>
        $"{Box} blocked {Blocked} over {Substeps} substep(s), {VoxelQueries} voxel read(s)"
        + (BlockedByUnloaded == BlockedFaces.None ? "" : $", unloaded on {BlockedByUnloaded}");
}

/// <summary>
/// Moves an <see cref="Aabb"/> through the voxel grid and works out where it actually ends up.
/// </summary>
/// <remarks>
/// <para>
/// <b>Axis-separated: move X and resolve, then Y, then Z.</b> DESIGN settled this and the
/// reason is worth restating, because the alternative looks better on paper. A combined sweep
/// finds the earliest time of impact across all three axes and stops there — so sliding along
/// a flat wall built out of voxels stops at every voxel boundary, and the player crawls along
/// a wall that should be frictionless. Resolving one axis at a time makes sliding the
/// default: the blocked axis is cancelled and the free axes keep their full displacement,
/// with no seam to catch on because the wall is one flat constraint per axis. The cost is
/// that a diagonal move into an inside corner resolves both axes independently and lands
/// slightly differently than a true sweep would, which is the trade DESIGN made deliberately.
/// </para>
/// <para>
/// <b>An unloaded voxel is solid</b> — see <see cref="IsSolid"/>. This is the decision the
/// whole milestone turns on and the one that is unrecoverable if it goes the other way.
/// </para>
/// <para>
/// <b>Substepped, because move-then-resolve tunnels above 15 m/s.</b> The thinnest obstacle is
/// one voxel, 0.25 m, and a physics step is 1/60 s. A sprint is under that threshold and a
/// fall passes it in well under a second, so <i>walking never tunnels and falling always
/// does</i> — the worst possible shape for a bug, because it cannot appear while movement is
/// being tested. See <see cref="MaxSubstepMetres"/>.
/// </para>
/// <para>
/// <b>Known knife edge: a gap exactly the box's width.</b> The resolve stops the leading face
/// <see cref="ContactEpsilonMetres"/> short of contact, so when the free space equals the
/// box's size to the millimetre there is nowhere for that margin to come from and the
/// trailing face ends 0.1 mm inside the far wall. The box still cannot pass through either
/// side — the next move in that direction reads the wall's layer and pushes it back out —
/// and dropping straight down such a gap is exact, because an axis with no displacement is
/// never resolved at all. It only arises when a corridor is dug to precisely the player's
/// width; M4b's coupling note has the weakest tool leaving a 0.75 m bore against a 0.5 m box.
/// </para>
/// </remarks>
public static class VoxelCollider
{
    /// <summary>
    /// No substep may displace the box further than this, in metres.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 0.2 m: under one voxel (0.25 m), with margin for
    /// <see cref="ContactEpsilonMetres"/> and for float error in the leading-face
    /// arithmetic. At a 40 m/s terminal velocity that is four substeps in the worst case and
    /// one whenever the player is walking.
    /// </para>
    /// <para>
    /// <b>The cap is on displacement, not on the box's own dimensions.</b> Sizing the substep
    /// to the box is the obvious-looking mistake: a 0.25 m floor is thinner than the player,
    /// so a substep the height of the player steps clean over it.
    /// </para>
    /// <para>
    /// <b>This constant is load-bearing, and the sealed-room fuzz is what proves it.</b>
    /// <see cref="ResolveAxis"/> tests one voxel layer — the one the leading face ends in —
    /// which is sound only because a substep cannot cross more than one layer boundary. Raise
    /// this past <see cref="VoxelConstants.VoxelSize"/> and the box passes through walls.
    /// </para>
    /// <para>
    /// <b>Rejected:</b> a swept AABB against the grid, which is exact at any speed and
    /// unbounded in cost — but it is a different algorithm with its own corner cases, and
    /// sweeping is precisely what DESIGN rejected for snagging on flat walls. Substepping
    /// leaves the axis-separated resolve untouched and bounds the cost at four iterations.
    /// <b>Revisit trigger:</b> anything that moves a body faster than this budget makes
    /// sensible — a mine cart at 100 m/s wants nine substeps a frame and is still fine; one at
    /// 1,000 m/s deserves a real sweep.
    /// </para>
    /// </remarks>
    public const float MaxSubstepMetres = 0.2f;

    /// <summary>
    /// How far short of contact a blocked face is placed, in metres.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 0.1 mm — 1/2500 of a voxel, far below anything visible, and comfortably above one
    /// float ULP out to several hundred metres from the origin.
    /// </para>
    /// <para>
    /// <b>Belt and braces rather than the load-bearing part — measured, not assumed.</b> What
    /// actually makes resting contact stable is <see cref="Aabb"/>'s half-open interval: a face
    /// lying exactly on a voxel plane is touching, not overlapping, so a box snapped exactly
    /// onto a plane is already at rest. Voxel planes are exact in binary (0.25 m is a power of
    /// two), so that snap is exact and needs no margin. Setting this constant to zero was tried
    /// on purpose and <b>the whole suite still passes</b>, which is the evidence for the
    /// paragraph above rather than an argument for it. It is kept because the reasoning stops
    /// holding for a voxel size that is not a power of two, or for coordinates large enough
    /// that a plane is no longer exactly representable — and it costs the knife edge named in
    /// the type's remarks, which zero would not.
    /// </para>
    /// </remarks>
    public const float ContactEpsilonMetres = 1e-4f;

    /// <summary>
    /// The most substeps one <see cref="Move"/> will run before refusing the displacement.
    /// </summary>
    /// <remarks>
    /// 1,024 substeps is 204.8 m in one step, or about 12,000 m/s at 60 Hz — two orders of
    /// magnitude past the fastest thing this project has contemplated (a 100 m/s mine cart
    /// needs nine). So reaching it means a velocity that has blown up, not a fast body, and
    /// the alternative to throwing is a loop long enough to look like a freeze. The same
    /// reasoning that bounds <see cref="ToolDefinition.MaxRadiusMetres"/>, and the number is
    /// an estimate for the same reason: it bounds a failure, it does not describe a limit
    /// anything is expected to approach.
    /// </remarks>
    public const int MaxSubsteps = 1024;

    /// <summary>
    /// Whether a voxel stops a body — <b>and an unloaded voxel does</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IVoxelWorld.TryGetMaterial"/> hands back air <i>and</i> false for a chunk
    /// that is not loaded, so a caller that ignores the return value gets exactly the wrong
    /// answer. This is <c>RayOutcome.Blocked</c>-is-not-<c>Missed</c> again, at a layer where
    /// the consequence is not confusing but unrecoverable: read unloaded as empty and the
    /// player falls out of the world, accelerating away from every chunk that could have
    /// stopped them, each frame further from any hope of a collision. Treated as solid, the
    /// player stops at the edge of the loaded world and waits for it to catch up.
    /// </para>
    /// <para>
    /// <b>Solidity is a predicate, not a comparison against air.</b>
    /// <see cref="MaterialTable.IsFluid"/> already exists and is already projected, so this is
    /// correct today — fluids do not exist yet — and still correct at M8 when they do, with
    /// wading falling out as the default rather than as a special case. <see cref="VoxelRay"/>
    /// carries a recorded trigger to revisit its own stop test at M8; this needs none, because
    /// the predicate form costs nothing now.
    /// </para>
    /// </remarks>
    public static bool IsSolid(IVoxelWorld world, MaterialTable materials, VoxelPos voxel)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);

        return IsSolidUncounted(world, materials, voxel, out _);
    }

    /// <summary>Whether any voxel the box overlaps is solid.</summary>
    /// <remarks>
    /// The postcondition every motion test shares — a box never <i>ends</i> a step overlapping
    /// solid — asserted through here rather than re-derived per test, and the seed of the
    /// unstick rule M4c needs for a body that finds itself inside rock.
    /// </remarks>
    public static bool OverlapsSolid(IVoxelWorld world, MaterialTable materials, Aabb box)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);

        (VoxelPos min, VoxelPos max) = box.VoxelBounds;

        for (int z = min.Z; z <= max.Z; z++)
        for (int x = min.X; x <= max.X; x++)
        for (int y = min.Y; y <= max.Y; y++)
        {
            if (IsSolidUncounted(world, materials, new VoxelPos(x, y, z), out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Moves <paramref name="box"/> by <paramref name="displacementMetres"/> and returns where
    /// it actually ends up.
    /// </summary>
    /// <remarks>
    /// A pure function of a box, a displacement and a world: no gravity, no input, no state
    /// between calls. That is what lets M4b sit on top of it by adding only state, and what
    /// lets the sealed-room fuzz be written against it with no reference implementation.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The box or the displacement is not finite. Left alone, a NaN displacement produces a
    /// NaN substep count and a box that is nowhere.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The displacement needs more than <see cref="MaxSubsteps"/> substeps.
    /// </exception>
    public static MoveResult Move(
        IVoxelWorld world,
        MaterialTable materials,
        Aabb box,
        Vector3 displacementMetres)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);

        if (!float.IsFinite(box.Min.X) || !float.IsFinite(box.Min.Y) || !float.IsFinite(box.Min.Z))
        {
            throw new ArgumentException($"A box needs a finite position; got {box}.", nameof(box));
        }

        if (!float.IsFinite(displacementMetres.X)
            || !float.IsFinite(displacementMetres.Y)
            || !float.IsFinite(displacementMetres.Z))
        {
            throw new ArgumentException(
                $"A move needs a finite displacement; got {displacementMetres}.",
                nameof(displacementMetres));
        }

        int substeps = SubstepsFor(displacementMetres);
        if (substeps > MaxSubsteps)
        {
            throw new ArgumentOutOfRangeException(
                nameof(displacementMetres),
                displacementMetres,
                $"A displacement of {displacementMetres.Length():0.###} m in one step needs "
                + $"{substeps} substeps, past the {MaxSubsteps} bound. A velocity that large "
                + "is a bug upstream, not a fast body; clamp it where it is integrated.");
        }

        // Division by exactly one is exact, but skipping it says so rather than relying on it,
        // and one substep is the overwhelmingly common case.
        Vector3 step = substeps == 1 ? displacementMetres : displacementMetres / substeps;

        var blocked = BlockedFaces.None;
        var byUnloaded = BlockedFaces.None;
        int queries = 0;

        for (int i = 0; i < substeps; i++)
        {
            // X, then Y, then Z. The order is DESIGN's, and M4b's step-up reads the ground
            // state this leaves behind — so reordering it silently changes step-up's latency.
            box = ResolveAxis(world, materials, box, 0, step.X, ref blocked, ref byUnloaded, ref queries);
            box = ResolveAxis(world, materials, box, 1, step.Y, ref blocked, ref byUnloaded, ref queries);
            box = ResolveAxis(world, materials, box, 2, step.Z, ref blocked, ref byUnloaded, ref queries);
        }

        return new MoveResult(box, blocked, substeps, queries, byUnloaded);
    }

    /// <summary>
    /// How many substeps a displacement needs so that none of them exceeds
    /// <see cref="MaxSubstepMetres"/>.
    /// </summary>
    /// <remarks>
    /// On the vector's magnitude, not per axis: a diagonal move is one displacement, and
    /// capping each axis separately would let a move of 0.2 m on all three axes through as a
    /// single 0.35 m substep.
    /// </remarks>
    public static int SubstepsFor(Vector3 displacementMetres)
    {
        float distance = displacementMetres.Length();
        if (distance <= MaxSubstepMetres)
        {
            return 1;
        }

        double count = Math.Ceiling(distance / (double)MaxSubstepMetres);
        return count >= MaxSubsteps + 1 ? MaxSubsteps + 1 : (int)count;
    }

    /// <summary>
    /// Moves the box on one axis and pushes it back out if that put it into something solid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Exactly one voxel layer is tested: the one the leading face ends inside.</b> That is
    /// sound because the box did not overlap anything before the move and a substep cannot
    /// cross more than one layer boundary — see <see cref="MaxSubstepMetres"/>, which is what
    /// guarantees it. Everything else the moved box overlaps, it already overlapped.
    /// </para>
    /// <para>
    /// <b>The layer comes from the moved box, not from the original plus one</b>, and the
    /// difference is the whole of this function's robustness. Taking it from the moved box
    /// means the snap can never place the leading face <i>further along</i> than the move
    /// asked for, and it means a box that somehow starts overlapping — a mode toggle inside a
    /// wall, a placement at M6, the knife edge in this type's remarks — is pushed out on its
    /// leading side rather than being allowed to sink deeper. Testing "the layer one past
    /// where the box already was" instead gets both of those wrong, and the second one wrong
    /// silently: once a box is inside solid, every subsequent move finds no <i>new</i> layer
    /// and walks straight on through.
    /// </para>
    /// </remarks>
    private static Aabb ResolveAxis(
        IVoxelWorld world,
        MaterialTable materials,
        Aabb box,
        int axis,
        float delta,
        ref BlockedFaces blocked,
        ref BlockedFaces byUnloaded,
        ref int queries)
    {
        if (delta == 0.0f)
        {
            // Not just an optimisation. An axis with no displacement is left bitwise alone,
            // which is what makes standing still on a floor stable to the last bit rather than
            // to within a rounding of adding zero.
            return box;
        }

        Aabb moved = box.Translated(Aabb.OnAxis(axis, delta));
        (VoxelPos min, VoxelPos max) = moved.VoxelBounds;

        bool forward = delta > 0.0f;
        int layer = Aabb.Component(forward ? max : min, axis);

        // The cross-section of the moved box on the other two axes. Unchanged by the move —
        // only one axis was displaced — so this is the box's own footprint on the layer.
        int minA = Aabb.Component(min, Other(axis, 0));
        int maxA = Aabb.Component(max, Other(axis, 0));
        int minB = Aabb.Component(min, Other(axis, 1));
        int maxB = Aabb.Component(max, Other(axis, 1));

        for (int b = minB; b <= maxB; b++)
        for (int a = minA; a <= maxA; a++)
        {
            queries++;
            if (!IsSolidUncounted(world, materials, Compose(axis, layer, a, b), out bool unloaded))
            {
                continue;
            }

            // Recorded on the face that actually stopped the box, so BlockedByUnloaded stays a
            // subset of Blocked by construction rather than by agreement between two places.
            BlockedFaces face = forward ? PositiveFace(axis) : NegativeFace(axis);
            blocked |= face;
            if (unloaded)
            {
                byUnloaded |= face;
            }

            return forward
                ? box.WithMaxOn(axis, (layer * VoxelConstants.VoxelSize) - ContactEpsilonMetres)
                : box.WithMinOn(axis, ((layer + 1) * VoxelConstants.VoxelSize) + ContactEpsilonMetres);
        }

        return moved;
    }

    /// <summary>
    /// Whether the voxel stops a body, and whether it does so only because its chunk is
    /// missing.
    /// </summary>
    /// <param name="unloaded">
    /// True when the answer is "solid" purely because the chunk has not loaded. <b>Only
    /// meaningful when this returns true</b>, and false for every voxel that is genuinely
    /// material.
    /// </param>
    /// <remarks>
    /// The two cases were indistinguishable until M4d needed them apart. They still produce
    /// the <i>same</i> collision — an unloaded voxel is solid, which is the decision the whole
    /// milestone turns on — and differ only in what they mean: one is the world working, the
    /// other is the player having arrived somewhere before the world did.
    /// </remarks>
    private static bool IsSolidUncounted(
        IVoxelWorld world,
        MaterialTable materials,
        VoxelPos voxel,
        out bool unloaded)
    {
        if (!world.TryGetMaterial(voxel, out int material))
        {
            unloaded = true;
            return true;
        }

        unloaded = false;
        return material != MaterialIds.AirNumericId && !materials.IsFluid(material);
    }

    /// <summary>The <paramref name="which"/>'th axis that is not <paramref name="axis"/>.</summary>
    private static int Other(int axis, int which) => (axis + 1 + which) % 3;

    /// <summary>
    /// A voxel at <paramref name="layer"/> on <paramref name="axis"/>, and
    /// <paramref name="a"/>/<paramref name="b"/> on the other two in the order
    /// <see cref="Other"/> gives them.
    /// </summary>
    private static VoxelPos Compose(int axis, int layer, int a, int b) => axis switch
    {
        0 => new VoxelPos(layer, a, b),
        1 => new VoxelPos(b, layer, a),
        _ => new VoxelPos(a, b, layer),
    };

    private static BlockedFaces PositiveFace(int axis) => axis switch
    {
        0 => BlockedFaces.PositiveX,
        1 => BlockedFaces.PositiveY,
        _ => BlockedFaces.PositiveZ,
    };

    private static BlockedFaces NegativeFace(int axis) => axis switch
    {
        0 => BlockedFaces.NegativeX,
        1 => BlockedFaces.NegativeY,
        _ => BlockedFaces.NegativeZ,
    };
}
