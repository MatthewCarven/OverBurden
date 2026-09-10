using Overburden.Core.Hydrology;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// Tier C's seam: once a tick, every dug chunk under a lake draws what its faces pass out of that
/// lake and puts it into Tier B at its lowest faces first.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="PortalDrain"/> with a rate.</b> A portal passes whatever room the cell under the
/// mouth has; a seep face passes <c>permeability × head × area × seepageScale</c> litres a second,
/// summed over the chunk's faces by <see cref="SeepIndex.Coefficient"/>. The litres come out of
/// the basin under the face's sample through <see cref="WaterSystem.Seep"/> and go into the
/// automaton through <see cref="FluidSimulation.Add"/>, whole litres both ways, so
/// <c>seeped</c> is one number on both tiers' lines and Tier A's equals Tier B's to the litre.
/// A lake that has drained has no table; a chunk under no lake costs its groups' table reads
/// and nothing else.
/// </para>
/// <para>
/// <b>Two arms, <see cref="SeepMode"/>, and the played one is the default.</b> <i>Spring</i> pools
/// the chunk's whole rate and lands it at the lowest face that has room and is below the table — a
/// floor spring before a wall weep, one point that runs, which Matthew read as *"more like a tap"*
/// and kept: *"that is how it works in real life"*, and in hard rock it is, where water comes
/// through joints and not through the wall. <i>Sweat</i> lets each group (one sample, one Y —
/// every face in it under the same head) owe its own litres, and hands each litre to the next face
/// in that group with room, round-robin: the wall weeps everywhere at once, deeper faces more. Same
/// index, same rate, same identities; only where the litres land differs, and where they land is
/// what the automaton then does with them, so the awake count under each is the measurement the
/// switch exists to take. A face above the table is dry rock and receives nothing on either arm,
/// which matters on a divide, where one sample's faces are under a lake and the next sample's are
/// not. What does not divide into whole litres is carried as a debt against faces, not water —
/// per chunk on the index for spring (decision 7), per group here for sweat — bounded at one cell
/// and lost with the chunk, or, for sweat, with any change to the chunk's faces.
/// </para>
/// <para>
/// <b>A chunk whose faces are all full is skipped until something changes.</b> The lake's head does
/// not fall as the mine fills (the rate is measured against the lake, not against the water
/// already in the mine), so a flooded room under a lake would otherwise walk every face every
/// tick for ever, finding no room. Once a pass delivers nothing for want of room the chunk is
/// marked full at the index's revision, and looked at again when the revision moves — a dig, a
/// placed block, a neighbour arriving — or after <see cref="FullRecheckPasses"/> passes, which
/// catches water leaving by a route that files no face (the relay taking it back, an eviction).
/// This is where "it never settles" costs one flag rather than a walk.
/// </para>
/// <para>
/// <b>Every delivery is an inflow to the settle</b> (<see cref="FluidSimulation.Add"/> notes it),
/// so a chunk fed every tick never reaches the sheet solve's quiet count and its puddle levels
/// diffusively, a cell a tick. Recorded as the cost of a source with no end; the revisit trigger
/// is a play that reads the puddle as syrup, and the lever is water bodies, where a delivery is a
/// number added to a body and nothing wakes.
/// </para>
/// </remarks>
public sealed class SeepDrain
{
    /// <summary>How many passes a full chunk is skipped before it is looked at again regardless.</summary>
    public const int FullRecheckPasses = 16;

    /// <summary>The sweat arm's per-chunk state: what each group owes and which face it hands to next, valid for one index revision.</summary>
    private sealed class Sweat
    {
        public long Revision = -1;
        public long[] Owed = [];
        public int[] Cursor = [];
        public List<int>[] Faces = [];
    }

    private readonly Dictionary<ChunkPos, Sweat> _sweat = [];
    private readonly List<double> _groupCoefficients = [];

    private readonly SeepIndex _index;
    private readonly WaterSystem _water;
    private readonly FluidSimulation _fluid;
    private readonly double _tickSeconds;
    private readonly List<ChunkPos> _chunks = [];
    private readonly List<ChunkPos> _stale = [];
    private readonly Dictionary<ChunkPos, (long Revision, long Pass)> _full = [];
    private readonly Dictionary<(int X, int Z), (int Basin, double? Level)> _samples = [];

    public SeepDrain(
        SeepIndex index,
        WaterSystem water,
        FluidSimulation fluid,
        double seepageScale,
        double tickSeconds = 0.5,
        SeepMode mode = SeepMode.Spring)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(water);
        ArgumentNullException.ThrowIfNull(fluid);
        ArgumentOutOfRangeException.ThrowIfNegative(seepageScale);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickSeconds);

        _index = index;
        _water = water;
        _fluid = fluid;
        _tickSeconds = tickSeconds;
        SeepageScale = seepageScale;
        Mode = mode;
    }

    /// <summary>Litres a second per square metre of face per metre of head. <see cref="SeepIndex.DefaultSeepageScale"/> unless swept.</summary>
    public double SeepageScale { get; }

    /// <summary>Where the litres land: one running spring, or the whole wall sweating.</summary>
    public SeepMode Mode { get; }

    /// <summary>Millilitres that have crossed the seam. Equals <see cref="WaterSystem.SeepedLitres"/> × 1,000, always.</summary>
    public long SeepedMillilitres { get; private set; }

    /// <summary>Deliveries that moved water, over the lifetime.</summary>
    public long Deliveries { get; private set; }

    /// <summary>Passes run.</summary>
    public long Passes { get; private set; }

    /// <summary>Chunks with faces on the last pass.</summary>
    public int ChunksLastPass { get; private set; }

    /// <summary>Chunks that had a face under a lake on the last pass, skipped-as-full ones excluded.</summary>
    public int ChunksSeepingLastPass { get; private set; }

    /// <summary>Chunks that delivered at least a litre on the last pass.</summary>
    public int ChunksDeliveredLastPass { get; private set; }

    /// <summary>Chunks skipped on the last pass because every face below the table was full.</summary>
    public int ChunksFullLastPass { get; private set; }

    /// <summary>Chunks marked full now.</summary>
    public int FullChunks => _full.Count;

    /// <summary>One pass over every chunk with faces. Returns the millilitres delivered.</summary>
    public long Draw()
    {
        Passes++;
        ChunksLastPass = 0;
        ChunksSeepingLastPass = 0;
        ChunksDeliveredLastPass = 0;
        ChunksFullLastPass = 0;

        if (_water.Water is null || _index.Count == 0 || SeepageScale == 0)
        {
            return 0;
        }

        // Sorted rather than dictionary order, so the same state seeps the same way twice.
        _chunks.Clear();
        _chunks.AddRange(_index.Chunks);
        _chunks.Sort(static (a, b) =>
        {
            int c = a.Y.CompareTo(b.Y);
            if (c != 0) return c;
            c = a.X.CompareTo(b.X);
            return c != 0 ? c : a.Z.CompareTo(b.Z);
        });

        // A full mark outlives its chunk's faces only until here: a chunk with no store reads
        // revision zero, which no store ever has. The sweat state goes the same way.
        if (_full.Count > _chunks.Count || _sweat.Count > _chunks.Count)
        {
            _stale.Clear();
            foreach (ChunkPos chunk in _full.Keys)
            {
                if (_index.RevisionOf(chunk) == 0)
                {
                    _stale.Add(chunk);
                }
            }

            foreach (ChunkPos chunk in _sweat.Keys)
            {
                if (_index.RevisionOf(chunk) == 0)
                {
                    _stale.Add(chunk);
                }
            }

            foreach (ChunkPos chunk in _stale)
            {
                _full.Remove(chunk);
                _sweat.Remove(chunk);
            }
        }

        long deliveredThisPass = 0;
        foreach (ChunkPos chunk in _chunks)
        {
            ChunksLastPass++;

            if (_full.TryGetValue(chunk, out (long Revision, long Pass) full))
            {
                if (full.Revision == _index.RevisionOf(chunk) && Passes - full.Pass < FullRecheckPasses)
                {
                    ChunksFullLastPass++;
                    continue;
                }

                _full.Remove(chunk);
            }

            if (Mode == SeepMode.Sweat)
            {
                deliveredThisPass += SweatChunk(chunk);
                continue;
            }

            double coefficient = _index.Coefficient(chunk, _water);
            if (coefficient <= 0)
            {
                continue;
            }

            ChunksSeepingLastPass++;
            double litresPerSecond = SeepIndex.LitresPerSecond(coefficient, SeepageScale);
            long owed = (long)(litresPerSecond * _tickSeconds * FluidConstants.MillilitresPerLitre)
                        + _index.RemainderMillilitres(chunk);
            long litres = owed / FluidConstants.MillilitresPerLitre;
            if (litres == 0)
            {
                _index.SetRemainderMillilitres(chunk, Math.Min(owed, FluidConstants.CellCapacityMillilitres));
                continue;
            }

            long delivered = Deliver(chunk, litres, out bool starved);
            if (delivered > 0)
            {
                ChunksDeliveredLastPass++;
                deliveredThisPass += delivered * FluidConstants.MillilitresPerLitre;
            }

            if (delivered < litres && !starved)
            {
                // Nowhere below the table had room for a litre: full. The chunk is not looked
                // at again until its faces change. A lake that ran dry on the way is not that —
                // its table is gone at the next pass and comes back with the rain.
                _full[chunk] = (_index.RevisionOf(chunk), Passes);
            }

            // The debt is capped at a cell whichever way the pass ended.
            long left = owed - (delivered * FluidConstants.MillilitresPerLitre);
            _index.SetRemainderMillilitres(chunk, Math.Min(left, FluidConstants.CellCapacityMillilitres));
        }

        return deliveredThisPass;
    }

    /// <summary>
    /// The sweat arm for one chunk: each group owes its own litres and hands each to the next face
    /// in it with room. Returns the millilitres delivered.
    /// </summary>
    private long SweatChunk(ChunkPos chunk)
    {
        _groupCoefficients.Clear();
        double coefficient = _index.GroupCoefficients(chunk, _water, _groupCoefficients);
        if (coefficient <= 0)
        {
            return 0;
        }

        ChunksSeepingLastPass++;
        IReadOnlyList<SeepFace> faces = _index.FacesIn(chunk);
        IReadOnlyList<SeepGroup> groups = _index.GroupsIn(chunk);
        Sweat state = SweatStateFor(chunk, faces, groups);
        _samples.Clear();

        long delivered = 0;
        bool wanted = false;
        bool anyRoom = false;
        bool starved = false;
        for (int g = 0; g < groups.Count; g++)
        {
            if (_groupCoefficients[g] <= 0)
            {
                continue;
            }

            long owed = state.Owed[g]
                + (long)(SeepIndex.LitresPerSecond(_groupCoefficients[g], SeepageScale) * _tickSeconds * FluidConstants.MillilitresPerLitre);
            List<int> members = state.Faces[g];
            wanted |= owed >= FluidConstants.MillilitresPerLitre;
            int tried = 0;
            while (owed >= FluidConstants.MillilitresPerLitre && tried < members.Count)
            {
                int cursor = state.Cursor[g] % members.Count;
                state.Cursor[g] = cursor + 1;
                tried++;
                SeepFace face = faces[members[cursor]];
                if (_fluid.Grid.FreeAt(face.Inside) < FluidConstants.MillilitresPerLitre)
                {
                    continue;
                }

                anyRoom = true;
                tried = 0;
                long seeped = SeepInto(face, 1, ref starved);
                if (seeped == 0)
                {
                    break;
                }

                owed -= FluidConstants.MillilitresPerLitre;
                delivered += FluidConstants.MillilitresPerLitre;
            }

            state.Owed[g] = Math.Min(owed, FluidConstants.CellCapacityMillilitres);
        }

        if (delivered > 0)
        {
            ChunksDeliveredLastPass++;
        }
        else if (wanted && !anyRoom)
        {
            // Every group that owed a litre found no face with room: full, as on the spring arm.
            // A pass where nothing owed a litre yet is not that, and neither is a starved lake —
            // starvation is only ever found through a face that had room, so anyRoom covers it.
            _full[chunk] = (_index.RevisionOf(chunk), Passes);
        }

        return delivered;
    }

    private Sweat SweatStateFor(ChunkPos chunk, IReadOnlyList<SeepFace> faces, IReadOnlyList<SeepGroup> groups)
    {
        if (!_sweat.TryGetValue(chunk, out Sweat? state))
        {
            state = new Sweat();
            _sweat[chunk] = state;
        }

        long revision = _index.RevisionOf(chunk);
        if (state.Revision == revision)
        {
            return state;
        }

        // The faces changed under the groups: rebuild the membership and start the debts again.
        // Bounded — what each group owed was under a litre — and stated as the arm's cost.
        state.Revision = revision;
        state.Owed = new long[groups.Count];
        state.Cursor = new int[groups.Count];
        state.Faces = new List<int>[groups.Count];
        var byKey = new Dictionary<(int Y, int X, int Z), int>(groups.Count);
        for (int g = 0; g < groups.Count; g++)
        {
            state.Faces[g] = [];
            byKey[(groups[g].Y, groups[g].SampleX, groups[g].SampleZ)] = g;
        }

        for (int i = 0; i < faces.Count; i++)
        {
            (int sx, int sz) = _index.SampleOf(faces[i].Inside);
            state.Faces[byKey[(faces[i].Y, sx, sz)]].Add(i);
        }

        return state;
    }

    /// <summary>
    /// Takes up to <paramref name="litres"/> out of the lake over the face's sample and puts them
    /// into its inside. Returns the litres moved; sets <paramref name="starved"/> when the lake
    /// gave less than asked or is gone after. The face is assumed to have the room.
    /// </summary>
    private long SeepInto(SeepFace face, long litres, ref bool starved)
    {
        (int X, int Z) key = _index.SampleOf(face.Inside);
        if (!_samples.TryGetValue(key, out (int Basin, double? Level) sample))
        {
            int basin = _water.BasinAtSample(key.X, key.Z);
            sample = (basin, basin == Watershed.Outlet ? null : _water.LevelAt(basin));
            _samples[key] = sample;
        }

        if (sample.Level is null || sample.Level.Value <= face.Y + 0.5)
        {
            return 0;
        }

        long seeped = _water.Seep(sample.Basin, litres);
        if (seeped < litres)
        {
            starved = true;
            _samples[key] = (sample.Basin, _water.LevelAt(sample.Basin));
            if (seeped == 0)
            {
                return 0;
            }
        }

        long millilitres = seeped * FluidConstants.MillilitresPerLitre;
        long refused = _fluid.Add(face.Inside, millilitres);
        if (refused != 0)
        {
            throw new InvalidOperationException(
                $"Seep face {face} refused {refused} mL it had room for: the index holds a face "
                + "the terrain cannot hold, so an edit never reached it.");
        }

        SeepedMillilitres += millilitres;
        Deliveries++;
        double? after = _water.LevelAt(sample.Basin);
        _samples[key] = (sample.Basin, after);
        if (after is null)
        {
            starved = true;
        }

        return seeped;
    }

    /// <summary>
    /// Puts up to <paramref name="litres"/> into the chunk's faces, lowest first, each out of the
    /// lake over its own sample. Returns the litres delivered.
    /// </summary>
    private long Deliver(ChunkPos chunk, long litres, out bool starved)
    {
        _samples.Clear();
        long delivered = 0;
        starved = false;

        foreach (SeepFace face in _index.FacesIn(chunk))
        {
            if (delivered >= litres)
            {
                break;
            }

            (int X, int Z) key = _index.SampleOf(face.Inside);
            if (!_samples.TryGetValue(key, out (int Basin, double? Level) sample))
            {
                int basin = _water.BasinAtSample(key.X, key.Z);
                sample = (basin, basin == Watershed.Outlet ? null : _water.LevelAt(basin));
                _samples[key] = sample;
            }

            // Re-read per face: the deliveries before this one lowered the lake.
            if (sample.Level is null || sample.Level.Value <= face.Y + 0.5)
            {
                continue;
            }

            long room = _fluid.Grid.FreeAt(face.Inside) / FluidConstants.MillilitresPerLitre;
            long ask = Math.Min(room, litres - delivered);
            if (ask == 0)
            {
                continue;
            }

            // The lake may run out on the way down — that is the lake running out, not the
            // chunk filling up, whatever the faces after this one find — and SeepInto re-reads
            // its level for them.
            delivered += SeepInto(face, ask, ref starved);
        }

        return delivered;
    }
}
