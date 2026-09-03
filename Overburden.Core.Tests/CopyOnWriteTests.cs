using System.Collections.Concurrent;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The rule M3a rests on: a <see cref="VoxelChunk"/> instance handed to a job is never
/// written to again, so a worker copying it cannot see a half-applied edit.
/// </summary>
/// <remarks>
/// This matters more than it first sounds. A torn <em>voxel</em> would be survivable — the
/// remesh that follows an edit corrects it. What is not survivable is that
/// <see cref="VoxelChunk.Set"/> can promote the representation, replacing the backing arrays
/// and the width fields together, so a reader part way through <see cref="VoxelChunk.CopyTo"/>
/// can dereference an array that has just been swapped out. See docs/M3-PLAN.md.
/// </remarks>
public class CopyOnWriteTests
{
    [Fact]
    public void ACloneIsIndependentOfItsOriginalInEveryRepresentation()
    {
        foreach (VoxelChunk original in new[] { Uniform(), Palettised(), Direct() })
        {
            VoxelChunk clone = original.Clone();
            AssertSameContent(original, clone);

            clone.Set(1, 2, 3, 7);
            Assert.NotEqual(7, original.Get(1, 2, 3));

            original.Set(4, 5, 6, 9);
            Assert.NotEqual(9, clone.Get(4, 5, 6));
        }
    }

    [Fact]
    public void ACloneOfAPalettisedChunkCanStillGrowItsPalette()
    {
        // The palette lookup is rebuilt rather than copied, so this is the test that it was
        // rebuilt correctly: a clone that kept a stale index would write the wrong material
        // the first time a new one is introduced.
        VoxelChunk clone = Palettised().Clone();

        for (int material = 1; material < 40; material++)
        {
            clone.Set(material % 32, material / 32, 0, material);
        }

        for (int material = 1; material < 40; material++)
        {
            Assert.Equal(material, clone.Get(material % 32, material / 32, 0));
        }
    }

    [Fact]
    public void AHandedOutSnapshotNeverSeesAHalfAppliedEdit()
    {
        // The sharp version of the fuzz. Every "edit" rewrites the whole chunk to version k,
        // so any snapshot a reader takes must hold exactly one version throughout — a torn
        // read shows two, and the assertion is a single comparison rather than a guess about
        // which voxel should have changed.
        //
        // Each round ends with Compact(), which demotes the chunk back to uniform, so the
        // next round promotes uniform → palettised again. That churn is the dangerous part:
        // it swaps the backing arrays and the width fields, not just a voxel.
        //
        // Verified by deleting the Clone() below: this then fails in 356 ms with
        // "snapshot of version 2 held 3". A fuzz test nobody has watched fail is a fuzz test
        // that might not be testing anything.
        const int Rounds = 300;

        var snapshots = new BlockingCollection<(VoxelChunk Chunk, int Version)>(64);
        var failures = new ConcurrentQueue<string>();

        var readers = new Thread[4];
        for (int i = 0; i < readers.Length; i++)
        {
            readers[i] = new Thread(() =>
            {
                var buffer = new int[VoxelConstants.ChunkVolume];

                foreach ((VoxelChunk snapshot, int version) in snapshots.GetConsumingEnumerable())
                {
                    // Record and keep draining, never bail out. A reader that stops consuming
                    // deadlocks the writer against the bounded queue, and this test would then
                    // *hang* on the exact bug it exists to catch rather than failing. Which it
                    // did, the first time it was run against a deliberately broken build.
                    try
                    {
                        // Exactly what MeshChunkJob does with the references it was handed.
                        snapshot.CopyTo(buffer);
                    }
                    catch (Exception error)
                    {
                        // The failure mode that matters most: a representation swapped out
                        // from under the copy, which throws rather than returning bad data.
                        failures.Enqueue($"copy of version {version} threw {error.GetType().Name}");
                        continue;
                    }

                    foreach (int material in buffer)
                    {
                        if (material != version)
                        {
                            failures.Enqueue($"snapshot of version {version} held {material}");
                            break;
                        }
                    }
                }
            });

            readers[i].Start();
        }

        // The sim thread. Publishing and editing both happen here, in that order and never
        // concurrently, which is the property the real pipeline has: VoxelsSharedWithJob is
        // only ever written by whoever also does the writing.
        var current = new VoxelChunk(1);
        bool shared = false;

        for (int version = 2; version <= Rounds; version++)
        {
            if (shared)
            {
                current = current.Clone();
                shared = false;
            }

            for (int index = 0; index < VoxelConstants.ChunkVolume; index++)
            {
                current.SetAt(index, version);
            }

            current.Compact();

            shared = true;
            snapshots.Add((current, version));
        }

        snapshots.CompleteAdding();
        foreach (Thread reader in readers)
        {
            reader.Join();
        }

        Assert.Empty(failures);
    }

    private static VoxelChunk Uniform() => new(3);

    private static VoxelChunk Palettised()
    {
        var chunk = new VoxelChunk(1);
        for (int i = 0; i < VoxelConstants.ChunkVolume; i += 3)
        {
            chunk.SetAt(i, 2 + (i % 5));
        }

        return chunk;
    }

    private static VoxelChunk Direct()
    {
        var chunk = new VoxelChunk(1);
        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            chunk.SetAt(i, 1 + (i % 400));
        }

        return chunk;
    }

    private static void AssertSameContent(VoxelChunk expected, VoxelChunk actual)
    {
        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            if (expected.GetAt(i) != actual.GetAt(i))
            {
                Assert.Fail($"differs at {i}: {expected.GetAt(i)} vs {actual.GetAt(i)}");
            }
        }
    }
}
