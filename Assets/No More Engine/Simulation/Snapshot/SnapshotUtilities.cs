using Unity.Entities;
using Unity.Collections;
using Unity.Burst;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;

namespace NoMoreEngine.Simulation.Snapshot
{
    /// <summary>
    /// Utilities for working with the optimized snapshot system
    /// </summary>
    public static class SnapshotUtilities
    {
        /// <summary>
        /// Request a snapshot capture on the next safe frame
        /// </summary>
        public static void RequestSnapshot()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[SnapshotUtilities] No simulation world available");
                return;
            }

            var snapshotSystem = world.GetExistingSystemManaged<SnapshotSystem>();
            if (snapshotSystem != null)
            {
                snapshotSystem.CaptureSnapshot();
            }
            else
            {
                Debug.LogError("[SnapshotUtilities] SnapshotSystem not found");
            }
        }

        /// <summary>
        /// Restore to a specific tick
        /// </summary>
        public static bool RestoreToTick(uint tick)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[SnapshotUtilities] No simulation world available");
                return false;
            }

            var snapshotSystem = world.GetExistingSystemManaged<SnapshotSystem>();
            if (snapshotSystem != null)
            {
                return snapshotSystem.RestoreSnapshot(tick);
            }
            else
            {
                Debug.LogError("[SnapshotUtilities] SnapshotSystem not found");
                return false;
            }
        }

        /// <summary>
        /// Get performance statistics
        /// </summary>
        public static SnapshotPerformanceStats GetPerformanceStats()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return default;
            }

            var snapshotSystem = world.GetExistingSystemManaged<SnapshotSystem>();
            if (snapshotSystem != null)
            {
                return snapshotSystem.GetPerformanceStats();
            }

            return default;
        }

        /// <summary>
        /// Configure auto-snapshot behavior
        /// </summary>
        public static void ConfigureAutoSnapshot(bool enabled, uint intervalTicks = 60)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[SnapshotUtilities] No simulation world available");
                return;
            }

            var snapshotSystem = world.GetExistingSystemManaged<SnapshotSystem>();
            if (snapshotSystem != null)
            {
                snapshotSystem.SetAutoSnapshot(enabled);
                if (intervalTicks > 0)
                {
                    snapshotSystem.SetSnapshotInterval(intervalTicks);
                }
                
                Debug.Log($"[SnapshotUtilities] Auto-snapshot {(enabled ? "enabled" : "disabled")} " +
                         $"with interval {intervalTicks} ticks");
            }
        }

        /// <summary>
        /// Get list of available snapshot ticks
        /// </summary>
        public static uint[] GetAvailableSnapshots()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return new uint[0];
            }

            var snapshotSystem = world.GetExistingSystemManaged<SnapshotSystem>();
            if (snapshotSystem != null)
            {
                return snapshotSystem.GetAvailableSnapshots();
            }

            return new uint[0];
        }

        /// <summary>
        /// Verify determinism between two ticks
        /// </summary>
        public static bool VerifyDeterminism(uint tickA, uint tickB)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[SnapshotUtilities] No simulation world available");
                return false;
            }

            var snapshotSystem = world.GetExistingSystemManaged<SnapshotSystem>();
            if (snapshotSystem != null)
            {
                return snapshotSystem.VerifyDeterminism(tickA, tickB);
            }

            return false;
        }
    }

    /// <summary>
    /// Fast hashing utilities using xxHash
    /// </summary>
    [BurstCompile]
    public static class FastHash
    {
        /// <summary>
        /// Calculate xxHash3 for a byte array
        /// </summary>
        [BurstCompile]
        public static unsafe ulong CalculateHash(byte* data, int length)
        {
            var hash64 = xxHash3.Hash64(data, length);
            // Combine the two 32-bit values into a single 64-bit value
            return ((ulong)hash64.x << 32) | (ulong)hash64.y;
        }

        /// <summary>
        /// Calculate xxHash3 for a NativeArray
        /// </summary>
        [BurstCompile]
        public static unsafe ulong CalculateHash<T>(NativeArray<T> data) where T : unmanaged
        {
            var hash64 = xxHash3.Hash64((byte*)data.GetUnsafeReadOnlyPtr(), data.Length * sizeof(T));
            // Combine the two 32-bit values into a single 64-bit value
            return ((ulong)hash64.x << 32) | (ulong)hash64.y;
        }

        /// <summary>
        /// Calculate incremental hash for streaming data
        /// </summary>
        [BurstCompile]
        public struct IncrementalHash
        {
            private xxHash3.StreamingState state;

            public static IncrementalHash Create()
            {
                return new IncrementalHash
                {
                    state = new xxHash3.StreamingState(false)
                };
            }

            [BurstCompile]
            public unsafe void Update(byte* data, int length)
            {
                state.Update(data, length);
            }

            [BurstCompile]
            public unsafe void Update<T>(T value) where T : unmanaged
            {
                state.Update(&value, sizeof(T));
            }

            [BurstCompile]
            public ulong Finalize()
            {
                // For incremental hashing, we need to use the streaming state
                // and extract a 64-bit value from the result
                var hash128 = state.DigestHash128();
                
                // Get hash code and properly cast to avoid sign extension warning
                uint hashCode = (uint)hash128.GetHashCode();
                // Extend to 64-bit by combining with a shifted version
                ulong result = ((ulong)hashCode << 32) | (ulong)hashCode;
                return result;
            }
        }
    }

    /// <summary>
    /// Memory pool utilities for reducing allocations
    /// </summary>
    public static class MemoryPoolUtilities
    {
        private static Dictionary<int, Stack<NativeArray<byte>>> byteArrayPools = new Dictionary<int, Stack<NativeArray<byte>>>();
        
        /// <summary>
        /// Rent a byte array from the pool
        /// </summary>
        public static NativeArray<byte> RentByteArray(int size, Allocator allocator = Allocator.TempJob)
        {
            // Round up to nearest power of 2
            int poolSize = Mathf.NextPowerOfTwo(size);
            
            if (byteArrayPools.TryGetValue(poolSize, out var pool) && pool.Count > 0)
            {
                return pool.Pop();
            }
            
            return new NativeArray<byte>(poolSize, allocator);
        }
        
        /// <summary>
        /// Return a byte array to the pool
        /// </summary>
        public static void ReturnByteArray(NativeArray<byte> array)
        {
            if (!array.IsCreated) return;
            
            int poolSize = array.Length;
            
            if (!byteArrayPools.TryGetValue(poolSize, out var pool))
            {
                pool = new Stack<NativeArray<byte>>();
                byteArrayPools[poolSize] = pool;
            }
            
            // Only keep a reasonable number pooled
            if (pool.Count < 10)
            {
                pool.Push(array);
            }
            else
            {
                array.Dispose();
            }
        }
        
        /// <summary>
        /// Clear all memory pools
        /// </summary>
        public static void ClearPools()
        {
            foreach (var pool in byteArrayPools.Values)
            {
                while (pool.Count > 0)
                {
                    var array = pool.Pop();
                    if (array.IsCreated)
                    {
                        array.Dispose();
                    }
                }
            }
            byteArrayPools.Clear();
        }
    }
}