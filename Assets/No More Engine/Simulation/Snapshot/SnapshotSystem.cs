using Unity.Entities;
using UnityEngine;
using Unity.Collections;
using NoMoreEngine.Simulation.Components;
using System.Collections.Generic;
using NoMoreEngine.Simulation.Systems;
using NoMoreEngine.Input;
using NoMoreEngine.Simulation.Bridge;

namespace NoMoreEngine.Simulation.Snapshot
{
    /// <summary>
    /// Optimized snapshot system that works with pooled snapshots and in-place restoration
    /// Defers to SimulationTimeSystem for timing and SimulationEntityManager for entity ops
    /// </summary>
    [UpdateInGroup(typeof(SimulationStepSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(CleanupPhase))]
    public partial class SnapshotSystem : SystemBase
    {
        private SnapshotManager snapshotManager;
        private SimulationTimeSystem timeSystem;
        private SimulationEntityManager simEntityManager;
        private PlayerInputMovementSystem inputMovementSystem;

        // Configuration
        private bool autoSnapshot = false;
        private uint snapshotInterval = 60; // Snapshot every second at 60Hz
        private uint lastSnapshotTick = 0;
        
        // Performance monitoring
        private float averageCaptureTime = 0f;
        private float averageRestoreTime = 0f;
        private int captureCount = 0;
        private int restoreCount = 0;

        // Rollback support
        private CircularBuffer<uint> snapshotHistory;
        private const int MAX_HISTORY = 10;

        // Deferred operations to avoid mid-frame issues
        private Queue<System.Action> deferredOperations;

        protected override void OnCreate()
        {
            // Initialize optimized snapshot manager
            snapshotManager = new SnapshotManager(World, MAX_HISTORY);
            snapshotHistory = new CircularBuffer<uint>(MAX_HISTORY);
            deferredOperations = new Queue<System.Action>();

            // Get system references
            timeSystem = World.GetExistingSystemManaged<SimulationTimeSystem>();
            simEntityManager = SimulationEntityManager.Instance;
            inputMovementSystem = World.GetExistingSystemManaged<PlayerInputMovementSystem>();

            // Validate architectural dependencies
            if (timeSystem == null)
            {
                Debug.LogError("[SnapshotSystem] SimulationTimeSystem not found - this violates architecture!");
            }

            Debug.Log("[SnapshotSystem] Initialized with optimized pooling system");
        }

        protected override void OnDestroy()
        {
            snapshotManager?.Dispose();
            
            // Log performance summary
            if (captureCount > 0 || restoreCount > 0)
            {
                Debug.Log($"[SnapshotSystem] Performance Summary:\n" +
                         $"  Captures: {captureCount}, Avg: {averageCaptureTime:F2}ms\n" +
                         $"  Restores: {restoreCount}, Avg: {averageRestoreTime:F2}ms");
            }
        }

        protected override void OnUpdate()
        {
            // Process any deferred operations first
            while (deferredOperations.Count > 0)
            {
                var operation = deferredOperations.Dequeue();
                operation?.Invoke();
            }

            // Always defer to SimulationTimeSystem for timing
            if (!SystemAPI.TryGetSingleton<SimulationTimeComponent>(out var time))
            {
                return; // No time system, can't proceed
            }

            // Auto-snapshot at intervals if enabled
            if (autoSnapshot && time.currentTick >= lastSnapshotTick + snapshotInterval)
            {
                // Defer snapshot to avoid mid-frame capture
                deferredOperations.Enqueue(() => CaptureSnapshotInternal(time.currentTick));
                lastSnapshotTick = time.currentTick;
            }
        }

        /// <summary>
        /// Public API to capture a snapshot
        /// </summary>
        public void CaptureSnapshot()
        {
            if (timeSystem == null)
            {
                Debug.LogError("[SnapshotSystem] Cannot capture - SimulationTimeSystem not available");
                return;
            }

            var currentTick = timeSystem.GetCurrentTick();
            
            // Defer to next frame to ensure consistent state
            deferredOperations.Enqueue(() => CaptureSnapshotInternal(currentTick));
        }

        /// <summary>
        /// Internal capture implementation
        /// </summary>
        private void CaptureSnapshotInternal(uint tick)
        {
            if (snapshotManager.CaptureSnapshot(tick))
            {
                // Track in history
                snapshotHistory.Add(tick);
                
                // Update performance metrics
                captureCount++;
                averageCaptureTime = ((averageCaptureTime * (captureCount - 1)) + 
                                     (float)snapshotManager.LastCaptureTimeMs) / captureCount;
                
                // Create metadata entity
                CreateSnapshotMetadata(tick);
                
                Debug.Log($"[SnapshotSystem] Captured snapshot at tick {tick} " +
                         $"(avg: {averageCaptureTime:F2}ms)");
            }
        }

        /// <summary>
        /// Restore simulation to a specific tick using optimized in-place restoration
        /// </summary>
        public bool RestoreSnapshot(uint tick)
        {
            Debug.Log($"[SnapshotSystem] Restoring to tick {tick} using optimized in-place method");
            
            // Validate dependencies
            if (simEntityManager == null)
            {
                Debug.LogError("[SnapshotSystem] SimulationEntityManager not available!");
                return false;
            }
            
            if (timeSystem == null)
            {
                Debug.LogError("[SnapshotSystem] SimulationTimeSystem not available!");
                return false;
            }

            // 1. Restore snapshot using optimized manager (in-place updates)
            if (!snapshotManager.RestoreSnapshot(tick))
            {
                Debug.LogError($"[SnapshotSystem] Failed to restore snapshot for tick {tick}");
                return false;
            }

            // 2. Restore time through SimulationTimeSystem
            timeSystem.RestoreToTick(tick);

            // 3. Ensure collision states exist (lightweight operation)
            EnsureCollisionStates();

            // 4. Reconnect input system
            RestorePlayerControl();
            
            // Update performance metrics
            restoreCount++;
            averageRestoreTime = ((averageRestoreTime * (restoreCount - 1)) + 
                                 (float)snapshotManager.LastRestoreTimeMs) / restoreCount;
            
            Debug.Log($"[SnapshotSystem] Restored to tick {tick} in {snapshotManager.LastRestoreTimeMs:F2}ms " +
                     $"(avg: {averageRestoreTime:F2}ms)");
            
            return true;
        }

        /// <summary>
        /// Ensure collision state components exist (doesn't modify values)
        /// </summary>
        private void EnsureCollisionStates()
        {
            // Only add components if missing, don't modify existing ones
            var needsStateQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CollisionBoundsComponent>(),
                ComponentType.ReadOnly<SimpleMovementComponent>(),
                ComponentType.Exclude<CollisionStateComponent>()
            );

            if (!needsStateQuery.IsEmpty)
            {
                var count = needsStateQuery.CalculateEntityCount();
                EntityManager.AddComponent<CollisionStateComponent>(needsStateQuery);
                Debug.Log($"[SnapshotSystem] Added collision states to {count} entities");
            }

            needsStateQuery.Dispose();
        }

        /// <summary>
        /// Restore player control connections after snapshot restore
        /// </summary>
        private void RestorePlayerControl()
        {
            // Force the input movement system to reconnect
            if (inputMovementSystem != null)
            {
                inputMovementSystem.ForceReconnection();
            }

            // Register player entities with InputProcessor
            var inputProcessor = InputProcessor.Instance;
            if (inputProcessor != null)
            {
                var playerQuery = EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PlayerControlledTag>(),
                    ComponentType.ReadOnly<PlayerControlComponent>()
                );

                using (var playerEntities = playerQuery.ToEntityArray(Allocator.Temp))
                using (var playerControls = playerQuery.ToComponentDataArray<PlayerControlComponent>(Allocator.Temp))
                {
                    for (int i = 0; i < playerEntities.Length; i++)
                    {
                        inputProcessor.RegisterPlayerEntity(playerEntities[i], playerControls[i].playerIndex);
                    }
                    
                    Debug.Log($"[SnapshotSystem] Re-registered {playerEntities.Length} player entities");
                }

                playerQuery.Dispose();
            }
        }

        /// <summary>
        /// Rollback to a previous tick with optimized performance
        /// </summary>
        public bool RollbackToTick(uint targetTick)
        {
            if (timeSystem == null || !SystemAPI.TryGetSingleton<SimulationTimeComponent>(out var time))
            {
                Debug.LogError("[SnapshotSystem] Cannot rollback - time system not available");
                return false;
            }

            var currentTick = time.currentTick;

            if (targetTick >= currentTick)
            {
                Debug.LogWarning($"[SnapshotSystem] Cannot rollback to future tick {targetTick}");
                return false;
            }

            // Find the nearest snapshot
            uint nearestSnapshot = 0;
            for (int i = 0; i < snapshotHistory.Count; i++)
            {
                var tick = snapshotHistory[i];
                if (tick <= targetTick && tick > nearestSnapshot)
                {
                    nearestSnapshot = tick;
                }
            }

            if (nearestSnapshot == 0)
            {
                Debug.LogWarning($"[SnapshotSystem] No snapshot available for rollback to tick {targetTick}");
                return false;
            }

            // Perform optimized restore
            if (RestoreSnapshot(nearestSnapshot))
            {
                Debug.Log($"[SnapshotSystem] Rolled back from tick {currentTick} to {nearestSnapshot} " +
                         $"(target was {targetTick}, will resimulate {targetTick - nearestSnapshot} ticks)");

                // TODO: Re-simulate from nearestSnapshot to targetTick
                // This requires replaying input and running simulation steps

                return true;
            }

            return false;
        }

        /// <summary>
        /// Create snapshot metadata entity
        /// </summary>
        private void CreateSnapshotMetadata(uint tick)
        {
            var metadataEntity = EntityManager.CreateEntity();
            EntityManager.SetName(metadataEntity, $"SnapshotMetadata_Tick{tick}");
            
            EntityManager.AddComponentData(metadataEntity, new SnapshotMetadata
            {
                snapshotTick = tick,
                snapshotHash = snapshotManager.GetSnapshotInfo(tick).hash,
                entityCount = snapshotManager.GetSnapshotInfo(tick).entityCount,
                totalDataSize = snapshotManager.GetSnapshotInfo(tick).dataSize,
                captureTimeMs = snapshotManager.LastCaptureTimeMs,
                restoreTimeMs = snapshotManager.LastRestoreTimeMs
            });
        }

        /// <summary>
        /// Verify determinism between snapshots
        /// </summary>
        public bool VerifyDeterminism(uint tickA, uint tickB)
        {
            if (snapshotManager.CompareSnapshots(tickA, tickB, out string differences))
            {
                Debug.Log($"[SnapshotSystem] Snapshots {tickA} and {tickB} are identical - determinism verified!");
                return true;
            }
            else
            {
                Debug.LogError($"[SnapshotSystem] Determinism failure between ticks {tickA} and {tickB}: {differences}");
                return false;
            }
        }

        /// <summary>
        /// Get performance statistics
        /// </summary>
        public SnapshotPerformanceStats GetPerformanceStats()
        {
            return new SnapshotPerformanceStats
            {
                captureCount = captureCount,
                restoreCount = restoreCount,
                averageCaptureTimeMs = averageCaptureTime,
                averageRestoreTimeMs = averageRestoreTime,
                activeSnapshotCount = snapshotManager.SnapshotCount,
                lastCaptureTimeMs = (float)snapshotManager.LastCaptureTimeMs,
                lastRestoreTimeMs = (float)snapshotManager.LastRestoreTimeMs
            };
        }

        // Configuration methods
        public void SetAutoSnapshot(bool enabled) => autoSnapshot = enabled;
        public void SetSnapshotInterval(uint interval) => snapshotInterval = interval;
        public bool IsAutoSnapshotEnabled => autoSnapshot;
        public uint SnapshotInterval => snapshotInterval;
        public int SnapshotCount => snapshotManager.SnapshotCount;
        public uint[] GetAvailableSnapshots() => snapshotHistory.ToArray();
        public SnapshotInfo GetSnapshotInfo(uint tick) => snapshotManager.GetSnapshotInfo(tick);
    }

    /// <summary>
    /// Circular buffer for snapshot history
    /// </summary>
    public class CircularBuffer<T>
    {
        private T[] buffer;
        private int head;
        private int count;

        public CircularBuffer(int capacity)
        {
            buffer = new T[capacity];
            head = 0;
            count = 0;
        }

        public void Add(T item)
        {
            buffer[head] = item;
            head = (head + 1) % buffer.Length;
            if (count < buffer.Length)
                count++;
        }

        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= count)
                    throw new System.IndexOutOfRangeException();
                
                int actualIndex = (head - count + index + buffer.Length) % buffer.Length;
                return buffer[actualIndex];
            }
        }

        public int Count => count;

        public T[] ToArray()
        {
            var result = new T[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = this[i];
            }
            return result;
        }
    }

    /// <summary>
    /// Performance statistics for snapshot operations
    /// </summary>
    public struct SnapshotPerformanceStats
    {
        public int captureCount;
        public int restoreCount;
        public float averageCaptureTimeMs;
        public float averageRestoreTimeMs;
        public int activeSnapshotCount;
        public float lastCaptureTimeMs;
        public float lastRestoreTimeMs;
    }
}