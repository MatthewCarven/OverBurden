namespace Overburden.Core.Hydrology;

/// <summary>
/// The water in a <see cref="MergeTree"/>: where it is, what leaves the world, and how high the
/// surface stands over any basin.
/// </summary>
/// <remarks>
/// <para>
/// <b>Water is held per node in slabs, not per basin in a pile.</b> A leaf holds what it can up to
/// its own rim; a composite holds only the slab between its children's rims and its own. So the
/// water in any subtree is the sum of its nodes, the capacity of a subtree is the sum of their
/// capacities, and "how much is in this lake" is one subtraction rather than a walk.
/// </para>
/// <para>
/// <b>Overflow goes up the tree and therefore terminates.</b> That is the whole reason
/// <see cref="MergeTree"/> exists: a crossing is symmetric, so two leaf basins with no cheaper exit
/// are each other's lowest crossing, and surplus routed between leaves ping-pongs forever. Routed
/// upward, each step is strictly closer to the root and the water either finds room or leaves the
/// world.
/// </para>
/// <para>
/// <b>Pouring fills the children before the parent's own slab</b>, which is not an optimisation but
/// the physics: water crossing a ridge runs downhill into whatever is on the other side and settles
/// at the lowest space it can find. Only once both sides are brim-full does the surface rise into
/// the ground between them.
/// </para>
/// <para>
/// <b>Removing takes off the top of the lake, which is the opposite walk</b> — see
/// <see cref="Remove"/>. Water leaving through a basin's floor lowers the surface of whatever lake
/// that basin is part of, and the surface is the topmost slab on the path from the leaf up; it
/// never reaches sideways into a sibling, because once the slab above a ridge is empty the other
/// side is its own lake. Both verbs find "the lake this basin is part of" through one walk,
/// <see cref="LakeTop"/>, which <see cref="LevelAt"/> uses too, so there is one definition of it.
/// </para>
/// <para>
/// Litres are <c>long</c> and exact, per M6c-2's argument for the inventory: a float accumulator
/// drifts and "how full is it" stops having one answer. M7b's integrate-forward oracle — 600 s in
/// one call equalling 600 calls of one second, to the litre — rests on this staying integral.
/// </para>
/// </remarks>
public sealed class BasinWater
{
    private readonly MergeTree _tree;
    private readonly long[] _volume;
    private readonly long[] _free;

    /// <summary>Scratch for <see cref="LakeTop"/>: the nodes from a leaf up to its lake's top.</summary>
    /// <remarks>
    /// Sized to the node count because a merge tree can be a chain, and allocated once because a
    /// removal runs per portal per tick once M8b-2c wires the seam.
    /// </remarks>
    private readonly int[] _path;

    public BasinWater(MergeTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        _tree = tree;
        _volume = new long[tree.NodeCount];
        _free = new long[tree.NodeCount];
        _path = new int[Math.Max(1, tree.NodeCount)];
        for (int node = 0; node < tree.NodeCount; node++)
        {
            _free[node] = tree.Capacity(node);
        }
    }

    public MergeTree Tree => _tree;

    /// <summary>Litres held everywhere.</summary>
    public long TotalStoredLitres
    {
        get
        {
            long total = 0;
            foreach (long litres in _volume)
            {
                total += litres;
            }

            return total;
        }
    }

    /// <summary>Litres in this node's slab alone.</summary>
    public long SlabLitres(int node) => _volume[node];

    /// <summary>Litres anywhere in this node's subtree.</summary>
    public long StoredIn(int node) => _tree.Capacity(node) - _free[node];

    /// <summary>Litres this node's subtree could still take before it overflows.</summary>
    public long FreeIn(int node) => _free[node];

    /// <summary>
    /// Adds water to a basin and returns what left the world — which is only ever what could not
    /// be held anywhere between that basin and the window edge.
    /// </summary>
    public long Add(int basin, long litres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(litres);

        long spilled = 0;
        long remaining = Pour(basin, litres);
        int node = basin;
        while (remaining > 0)
        {
            if (_tree.SpillsOut(node))
            {
                spilled = remaining;
                break;
            }

            node = _tree.Parent(node);
            remaining = Pour(node, remaining);
        }

        // Pour refreshes the free counts of everything it touched, which after a drop that fits in
        // its own basin is one leaf. Every ancestor's rolled-up count is then stale — and a stale
        // count is not merely slow: LevelAt asks a composite how much its subtree holds, so a
        // merged lake would report a level computed from a volume that had not been updated since
        // the last overflow.
        Refresh(basin);
        return spilled;
    }

    /// <summary>
    /// The surface level over a basin, in voxel Y — its own while it is filling on its own, and the
    /// combined lake's once the water has risen past the ridge it shares with a neighbour.
    /// </summary>
    public double LevelAt(int basin)
    {
        // A brim-full top — spilling out of the window, or waiting at its rim on a neighbour that
        // has not filled — is at its spill height, and the curve says so on its own: a subtree
        // holding its whole capacity is at its saddle, which Close fixed as the same number.
        int top = LakeTop(basin, out _);
        return _tree.Curve(top).LevelAt(StoredIn(top));
    }

    /// <summary>
    /// Takes water out of the lake this basin is part of, through its floor, and returns what was
    /// taken — less than asked when the lake runs out on the way down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is <see cref="LevelAt"/>'s walk reversed, not <c>Pour</c>'s.</b> Water leaving
    /// through a hole in a basin's floor comes off the <i>top</i> of whatever lake that basin is
    /// part of, so the walk climbs exactly as <c>LevelAt</c> does — to the lake's top node — takes
    /// from that slab, then from each node down the path to the leaf. Pouring runs the other way,
    /// children before a parent's slab, because water arriving runs downhill; water leaving lowers
    /// the surface, and the surface is the topmost slab that holds anything.
    /// </para>
    /// <para>
    /// <b>Never a sibling off the path.</b> Once the shared slab above a ridge is empty the level
    /// is at the ridge and the other side is its own lake; nothing over there can reach a hole over
    /// here. That is Tier B's <i>no siphons</i> rule arriving in Tier A, and it is also what keeps
    /// the water in a state a fill could have reached: a parent's slab holds something only while
    /// both children are full, which <c>Pour</c> guarantees going in and taking top-down preserves
    /// coming out. Taking from the leaf first would empty this pit while the shared slab still
    /// stood above it — the wrong physics, and visibly so.
    /// </para>
    /// <para>
    /// <b>Which identity holds, stated exactly, because M8-PLAN stated the other one.</b>
    /// <c>Remove(x)</c> then <c>Add(what it took)</c> restores every slab, always: the removal
    /// drains path slabs from the top, every sibling along the walk was full, and the add refills
    /// the same slabs from the bottom. <c>Add(x)</c> then <c>Remove(x)</c> restores every slab
    /// only while the add stays inside one lake — the moment it crosses a ridge into a neighbour
    /// with room, the neighbour keeps its share and the removal comes back short. Correct physics,
    /// not a defect, and both are tests.
    /// </para>
    /// </remarks>
    public long Remove(int basin, long litres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(litres);

        LakeTop(basin, out int depth);

        long removed = 0;
        for (int i = depth - 1; i >= 0 && removed < litres; i--)
        {
            int node = _path[i];
            long take = Math.Min(litres - removed, _volume[node]);
            _volume[node] -= take;
            removed += take;
        }

        // Same reason Add refreshes: every slab touched is on the path from this leaf to the root,
        // and a stale rolled-up count would give LevelAt a volume the lake no longer holds.
        if (removed > 0)
        {
            Refresh(basin);
        }

        return removed;
    }

    /// <summary>
    /// The node whose slab is the surface of the lake this basin is part of, with the path from the
    /// basin up to it left in <see cref="_path"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Climbs while the node is brim-full and the water stands above the ridge into its parent.
    /// <b>Brim-full is not the same as merged.</b> Two siblings are separate lakes until the water
    /// stands ABOVE the ridge between them, which is when the parent's own slab starts to hold
    /// something — before that, this basin is simply full and its neighbour is at whatever level
    /// its own water reaches.
    /// </para>
    /// <para>
    /// <b>A parent whose slab cannot hold anything does not end the walk, and until M8b-2a it
    /// did.</b> A composite closed at the same crossing height as its children has zero own
    /// capacity — three pits merging across one flat shelf produce it, and on integer heights
    /// sampled every few metres that is the common case, not the corner. Its slab is empty for ever, so
    /// "the parent's slab is empty" says nothing about whether the water has risen past the
    /// ridge; the answer is one level up, and both children being full is what carries the walk
    /// there. Stopping at it reported one lake at two heights — the pits on one side at the shelf,
    /// the pit on the other at the true level — and it was found by building
    /// <see cref="Remove"/>, which would have drained the wrong slab for the same reason.
    /// </para>
    /// </remarks>
    private int LakeTop(int basin, out int depth)
    {
        int node = basin;
        depth = 0;
        _path[depth++] = node;

        while (_free[node] == 0 && !_tree.SpillsOut(node))
        {
            int parent = _tree.Parent(node);
            if (_volume[parent] == 0 && (_tree.OwnCapacity(parent) > 0 || _free[parent] != 0))
            {
                break;
            }

            node = parent;
            _path[depth++] = node;
        }

        return node;
    }

    /// <summary>Rolls free capacity back up from a basin to the root.</summary>
    private void Refresh(int node)
    {
        while (true)
        {
            _free[node] = _tree.OwnCapacity(node) - _volume[node];
            int left = _tree.Left(node);
            if (left != MergeTree.None)
            {
                _free[node] += _free[left] + _free[_tree.Right(node)];
            }

            if (_tree.SpillsOut(node))
            {
                return;
            }

            node = _tree.Parent(node);
        }
    }

    /// <summary>
    /// The water in this tree, attributed to basins so it can be stored against something that
    /// outlives the window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pouring the result back into a fresh <see cref="BasinWater"/> over the same tree
    /// reproduces this state exactly</b>, which is what makes it a capture rather than a summary.
    /// It works because fill-and-spill is order-independent: the resting place of the water is
    /// determined by how much entered at each basin, not by the order it arrived in — so the
    /// attribution below need only be conservative, not physical.
    /// </para>
    /// <para>
    /// A composite's slab is water standing <i>above</i> two basins' rims and belongs to neither,
    /// so it is attributed to the leftmost basin beneath it. Poured back, that basin fills, its
    /// sibling fills from its own share, and the surplus lands in the composite's slab again —
    /// exactly where it was. <b>The alternative, keying composites in the store, would need an
    /// identity for a lake that only exists while the window holds both its halves.</b>
    /// </para>
    /// <para>
    /// <b>Which basin gets the credit is arbitrary, and the break-recipe battery says so rather
    /// than the comment claiming it:</b> attributing composites to the <i>rightmost</i> basin
    /// instead leaves the whole suite green. That is the order-independence above, measured.
    /// Dropping composite slabs entirely is a different matter and fails two tests, because that
    /// water is simply lost.
    /// </para>
    /// </remarks>
    public long[] CaptureByBasin()
    {
        var perBasin = new long[_tree.LeafCount];
        for (int node = 0; node < _tree.NodeCount; node++)
        {
            if (_volume[node] == 0)
            {
                continue;
            }

            int leaf = node;
            while (_tree.Left(leaf) != MergeTree.None)
            {
                leaf = _tree.Left(leaf);
            }

            perBasin[leaf] += _volume[node];
        }

        return perBasin;
    }

    private long Pour(int node, long litres)
    {
        if (litres <= 0 || _free[node] == 0)
        {
            return litres;
        }

        int left = _tree.Left(node);
        if (left != MergeTree.None)
        {
            litres = Pour(left, litres);
            litres = Pour(_tree.Right(node), litres);
        }

        long take = Math.Min(litres, _tree.OwnCapacity(node) - _volume[node]);
        _volume[node] += take;
        litres -= take;

        _free[node] = _tree.OwnCapacity(node) - _volume[node];
        if (left != MergeTree.None)
        {
            _free[node] += _free[left] + _free[_tree.Right(node)];
        }

        return litres;
    }
}
