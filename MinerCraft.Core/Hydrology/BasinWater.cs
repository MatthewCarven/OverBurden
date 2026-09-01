namespace MinerCraft.Core.Hydrology;

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

    public BasinWater(MergeTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        _tree = tree;
        _volume = new long[tree.NodeCount];
        _free = new long[tree.NodeCount];
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
        int node = basin;
        while (_free[node] == 0)
        {
            if (_tree.SpillsOut(node))
            {
                return _tree.SpillHeight(node);
            }

            // Brim-full is not the same as merged. Two siblings are separate lakes until the water
            // stands ABOVE the ridge between them, which is exactly when the parent's own slab
            // starts to hold something — before that, this basin is simply full and its neighbour
            // is at whatever level its own water reaches.
            int parent = _tree.Parent(node);
            if (_volume[parent] == 0)
            {
                return _tree.SpillHeight(node);
            }

            node = parent;
        }

        return _tree.Curve(node).LevelAt(StoredIn(node));
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
