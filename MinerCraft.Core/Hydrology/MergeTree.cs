namespace MinerCraft.Core.Hydrology;

/// <summary>
/// The order basins join each other in as the water rises: which two become one lake, at what
/// height, and what the combined lake then holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because the leaf spill graph has two-cycles by construction.</b> A crossing
/// costs the higher of the two samples it joins, so it is <i>symmetric</i> — two basins with no
/// cheaper exit are each other's lowest crossing, and surplus routed around the leaves ping-pongs
/// between them forever. Above that crossing the two are not two basins at all; they are one lake
/// with a higher rim. The tree says so, and routing up a tree cannot cycle.
/// </para>
/// <para>
/// <b>Built by sorting every crossing and merging with union-find</b> — Kruskal over the terrain,
/// which is the standard join tree of a height field. A merge at height <c>S</c> is what fixes
/// <c>S</c> as the spill height of <i>both</i> components it joined; the composite's own spill
/// height is fixed later by the next merge it takes part in.
/// </para>
/// <para>
/// <b>A component that reaches the window edge stops growing.</b> Once its water can leave the
/// world, filling it higher sends water out rather than joining it to a neighbour — so it takes no
/// parent, and a live component crossing into it drains out through it. That is the window-edge
/// outlet rule of <see cref="Watershed"/> arriving one level up.
/// </para>
/// <para>
/// <b>Memory is one sorted height array per live component, so the arrays partition the samples
/// and cost n ints in total at any moment</b> — a merge consumes both children's arrays and yields
/// one. The curves are the part that accumulates, at O(n log n) over the whole build; measured at
/// M7a-2 rather than assumed, because the plan named lazy composite curves as the alternative.
/// </para>
/// </remarks>
public sealed class MergeTree
{
    /// <summary>No parent: this component's water leaves the world instead of joining another.</summary>
    public const int None = -1;

    private const int LinkIndexBits = 22;
    private const int HeightBias = HeightField.MaxAbsoluteHeight;

    private readonly int[] _parent;
    private readonly int[] _left;
    private readonly int[] _right;
    private readonly int[] _spillHeight;
    private readonly long[] _capacity;
    private readonly long[] _ownCapacity;
    private readonly VolumeCurve[] _curve;

    private MergeTree(
        int leafCount,
        int[] parent,
        int[] left,
        int[] right,
        int[] spillHeight,
        long[] capacity,
        long[] ownCapacity,
        VolumeCurve[] curve)
    {
        LeafCount = leafCount;
        _parent = parent;
        _left = left;
        _right = right;
        _spillHeight = spillHeight;
        _capacity = capacity;
        _ownCapacity = ownCapacity;
        _curve = curve;
    }

    /// <summary>Nodes 0..<see cref="LeafCount"/>-1 are the watershed's basins, in its order.</summary>
    public int LeafCount { get; }

    /// <summary>Leaves and composites together.</summary>
    public int NodeCount => _parent.Length;

    /// <summary>The composite this node joins, or <see cref="None"/> when its water leaves.</summary>
    public int Parent(int node) => _parent[node];

    public int Left(int node) => _left[node];

    public int Right(int node) => _right[node];

    /// <summary>True when filling past <see cref="SpillHeight"/> sends water out of the window.</summary>
    public bool SpillsOut(int node) => _parent[node] == None;

    /// <summary>Voxel Y at which this node overflows — into its parent, or out of the world.</summary>
    public int SpillHeight(int node) => _spillHeight[node];

    /// <summary>Litres this node's whole subtree holds at <see cref="SpillHeight"/>.</summary>
    public long Capacity(int node) => _capacity[node];

    /// <summary>
    /// Litres this node holds that its children cannot — the slab between their rims and its own.
    /// Zero-or-more by construction, and it is what makes the water in a subtree one sum.
    /// </summary>
    public long OwnCapacity(int node) => _ownCapacity[node];

    /// <summary>Volume against level for the whole subtree, capped at <see cref="SpillHeight"/>.</summary>
    public VolumeCurve Curve(int node) => _curve[node];

    public static MergeTree Build(Watershed watershed)
    {
        ArgumentNullException.ThrowIfNull(watershed);

        int leaves = watershed.Basins.Count;
        int capacityNodes = Math.Max(1, (leaves * 2) - 1);

        var parent = new int[capacityNodes];
        var left = new int[capacityNodes];
        var right = new int[capacityNodes];
        var spillHeight = new int[capacityNodes];
        var capacity = new long[capacityNodes];
        var ownCapacity = new long[capacityNodes];
        var curve = new VolumeCurve[capacityNodes];
        Array.Fill(parent, None);
        Array.Fill(left, None);
        Array.Fill(right, None);

        if (leaves == 0)
        {
            return new MergeTree(0, [], [], [], [], [], [], []);
        }

        HeightField field = watershed.Field;
        int[][] cells = CatchmentHeights(watershed, leaves);
        var union = new int[capacityNodes];
        for (int i = 0; i < capacityNodes; i++)
        {
            union[i] = i;
        }

        var drained = new bool[capacityNodes];
        var merged = new bool[capacityNodes];
        int nodeCount = leaves;

        foreach ((int crossing, int a, int b) in CrossingsInAscendingOrder(watershed))
        {
            bool outletA = a == Watershed.Outlet;
            bool outletB = b == Watershed.Outlet;
            if (outletA && outletB)
            {
                continue;
            }

            if (outletA || outletB)
            {
                int root = Find(union, outletA ? b : a);
                if (!drained[root])
                {
                    Close(root, crossing);
                }

                continue;
            }

            int rootA = Find(union, a);
            int rootB = Find(union, b);
            if (rootA == rootB)
            {
                continue;
            }

            // A component that already drains to the window edge never grows. Anything crossing
            // into it leaves the world through it, which is why this closes one side rather than
            // building a composite.
            //
            // BOTH SIDES DRAINED IS A SEPARATE CASE AND GETTING IT WRONG IS SILENT. Two components
            // that each already reach the edge meet at some crossing eventually — every pair does,
            // somewhere — and neither is filling any more. Closing "the live one" then re-closes a
            // component that was settled long ago, at the higher height, and its spill height
            // silently becomes the wrong number. Found by the test that says a leaf's spill height
            // is the saddle the watershed already computed: two definitions of one number.
            if (drained[rootA] && drained[rootB])
            {
                union[rootA] = rootB;
                continue;
            }

            if (drained[rootA] || drained[rootB])
            {
                int live = drained[rootA] ? rootB : rootA;
                int sink = drained[rootA] ? rootA : rootB;
                Close(live, crossing);
                union[live] = sink;
                continue;
            }

            int composite = nodeCount++;

            // Parent first, and the break-recipe battery says this is BELT AND BRACES rather than
            // load-bearing: Close reads the parent to decide whether the component drains to the
            // window edge, but that flag is only ever consulted through Find, which returns roots —
            // and a node that has just been given a parent is never a root again. Swapping the two
            // lines leaves the suite green. Kept because the flag should mean what its name says
            // at every moment, not only at the moments something happens to look.
            parent[rootA] = composite;
            parent[rootB] = composite;
            Close(rootA, crossing);
            Close(rootB, crossing);
            left[composite] = rootA;
            right[composite] = rootB;
            cells[composite] = MergeSorted(cells[rootA], cells[rootB]);
            cells[rootA] = [];
            cells[rootB] = [];
            union[rootA] = composite;
            union[rootB] = composite;
        }

        // Anything still open never met the window edge, which the outlet rule says cannot happen —
        // every catchment borders something and the border is an outlet. Closed at its own highest
        // sample rather than left with an unset spill height, so a future change to the labelling
        // cannot produce a tree with a hole in it and no complaint.
        for (int node = 0; node < nodeCount; node++)
        {
            if (!merged[node])
            {
                Close(node, cells[node].Length == 0 ? 0 : cells[node][^1] + 1);
            }
        }

        for (int node = 0; node < nodeCount; node++)
        {
            long children = 0;
            if (left[node] != None)
            {
                children = capacity[left[node]] + capacity[right[node]];
            }

            ownCapacity[node] = capacity[node] - children;
        }

        return new MergeTree(
            leaves,
            parent[..nodeCount],
            left[..nodeCount],
            right[..nodeCount],
            spillHeight[..nodeCount],
            capacity[..nodeCount],
            ownCapacity[..nodeCount],
            curve[..nodeCount]);

        void Close(int node, int height)
        {
            spillHeight[node] = height;
            merged[node] = true;
            drained[node] = parent[node] == None;

            int[] own = cells[node];
            int below = LowerBound(own, height);
            curve[node] = VolumeCurve.Build(own.AsSpan(0, below), height, field.LitresPerVoxelOfDepth);
            capacity[node] = curve[node].CapacityLitres;
        }
    }

    /// <summary>Every basin's catchment heights, sorted, ready to be merged upward.</summary>
    private static int[][] CatchmentHeights(Watershed watershed, int leaves)
    {
        var counts = new int[leaves];
        foreach (int label in watershed.Labels)
        {
            if (label != Watershed.Outlet)
            {
                counts[label]++;
            }
        }

        var cells = new int[Math.Max(1, (leaves * 2) - 1)][];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = i < leaves ? new int[counts[i]] : [];
        }

        var written = new int[leaves];
        ReadOnlySpan<int> heights = watershed.Field.Heights;
        for (int i = 0; i < heights.Length; i++)
        {
            int label = watershed.Labels[i];
            if (label != Watershed.Outlet)
            {
                cells[label][written[label]++] = heights[i];
            }
        }

        for (int i = 0; i < leaves; i++)
        {
            Array.Sort(cells[i]);
        }

        return cells;
    }

    /// <summary>Every crossing between two different regions, cheapest first.</summary>
    private static IEnumerable<(int Crossing, int A, int B)> CrossingsInAscendingOrder(
        Watershed watershed)
    {
        HeightField field = watershed.Field;
        int pitch = field.Pitch;
        ReadOnlySpan<int> heights = field.Heights;

        var crossings = new List<(int Crossing, int A, int B)>();
        for (int sz = 0; sz < pitch; sz++)
        {
            for (int sx = 0; sx < pitch; sx++)
            {
                int index = (sz * pitch) + sx;
                int label = watershed.Labels[index];

                // East and south only: every pair is looked at once, and a crossing is symmetric.
                if (sx + 1 < pitch)
                {
                    int other = watershed.Labels[index + 1];
                    if (other != label)
                    {
                        crossings.Add((Math.Max(heights[index], heights[index + 1]), label, other));
                    }
                }

                if (sz + 1 < pitch)
                {
                    int other = watershed.Labels[index + pitch];
                    if (other != label)
                    {
                        crossings.Add(
                            (Math.Max(heights[index], heights[index + pitch]), label, other));
                    }
                }
            }
        }

        // Packed into one key so the sort is Array.Sort over longs rather than a comparison
        // delegate, and so equal crossings keep scan order — which is row-major, and therefore
        // survives a window translation.
        var keys = new long[crossings.Count];
        var order = new int[crossings.Count];
        for (int i = 0; i < crossings.Count; i++)
        {
            keys[i] = ((long)(crossings[i].Crossing + HeightBias) << LinkIndexBits) | (uint)i;
            order[i] = i;
        }

        Array.Sort(keys, order);
        foreach (int i in order)
        {
            yield return crossings[i];
        }
    }

    private static int Find(int[] union, int node)
    {
        while (union[node] != node)
        {
            union[node] = union[union[node]];
            node = union[node];
        }

        return node;
    }

    private static int[] MergeSorted(int[] a, int[] b)
    {
        var merged = new int[a.Length + b.Length];
        int i = 0;
        int j = 0;
        int k = 0;
        while (i < a.Length && j < b.Length)
        {
            merged[k++] = a[i] <= b[j] ? a[i++] : b[j++];
        }

        while (i < a.Length)
        {
            merged[k++] = a[i++];
        }

        while (j < b.Length)
        {
            merged[k++] = b[j++];
        }

        return merged;
    }

    /// <summary>Number of entries strictly below <paramref name="value"/>.</summary>
    private static int LowerBound(int[] sorted, int value)
    {
        int low = 0;
        int high = sorted.Length;
        while (low < high)
        {
            int mid = (low + high) >> 1;
            if (sorted[mid] < value)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
