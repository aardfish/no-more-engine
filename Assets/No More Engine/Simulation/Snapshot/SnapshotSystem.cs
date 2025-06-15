using Unity.Entities;
using Unity.Burst;
using UnityEngine;
using Unity.Collections;
using NoMoreEngine.Simulation.Components;
using System.Collections.Generic;
using NoMoreEngine.Simulation.Systems;
using NoMoreEngine.Input;

namespace NoMoreEngine.Simulation.Snapshot
{
    /// <summary>
    /// ECS System that manages snapshot capture and restoration
    /// Runs after all simulation systems to capture consistent state
    /// </summary>
    [UpdateInGroup(typeof(SimulationStepSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(CleanupPhase))]
    public partial class SnapshotSystem : SystemBase
    {
        private SnapshotManager snapshotManager;
        private SimulationTimeSystem timeSystem;

        // Configuration
        private bool autoSnapshot = false;
        private uint snapshotInterval = 60; // Snapshot every second at 60Hz
        private uint lastSnapshotTick = 0;

        // Rollback support
        private Queue<uint> snapshotHistory;
        private const int MAX_HISTORY = 10;

        // Initialization tracking
        private float initializationTime = 0f;
        private const float INITIALIZATION_DELAY = 0.1f; // Wait 100ms before allowing first snapshot

        protected override void OnCreate()
        {
            // Initialize snapshot manager
            snapshotManager = new SnapshotManager(World, MAX_HISTORY);
            snapshotHistory = new Queue<uint>(MAX_HISTORY);

            // Get reference to time system
            timeSystem = World.GetExistingSystemManaged<SimulationTimeSystem>();

            Debug.Log("[SnapshotSystem] Initialized");
        }

        protected override void OnDestroy()
        {
            snapshotManager?.Dispose();
        }

        protected override void OnUpdate()
        {
            // Track initialization time
            initializationTime += SystemAPI.Time.DeltaTime;

            // Try to get time component - it might not exist on first frame
            if (!SystemAPI.TryGetSingleton<SimulationTimeComponent>(out var time))
            {
                return; // Skip if time system hasn't initialized yet
            }

            // Auto-snapshot at intervals if enabled
            if (autoSnapshot && time.currentTick >= lastSnapshotTick + snapshotInterval)
            {
                CaptureSnapshot();
                lastSnapshotTick = time.currentTick;
            }
        }

        /// <summary>
        /// Manually capture a snapshot at the current tick
        /// </summary>
        public void CaptureSnapshot()
        {
            // Wait for brief initialization period to ensure all systems are ready
            if (initializationTime < INITIALIZATION_DELAY)
            {
                Debug.LogWarning($"[SnapshotSystem] Waiting for initialization ({initializationTime:F3}s / {INITIALIZATION_DELAY}s)");
                return;
            }

            if (!SystemAPI.TryGetSingleton<SimulationTimeComponent>(out var time))
            {
                Debug.LogWarning("[SnapshotSystem] Cannot capture snapshot - time system not ready");
                return;
            }

            if (snapshotManager.CaptureSnapshot(time.currentTick))
            {
                // Track snapshot history
                snapshotHistory.Enqueue(time.currentTick);
                while (snapshotHistory.Count > MAX_HISTORY)
                {
                    snapshotHistory.Dequeue();
                }

                // Create metadata entity
                var metadataEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(metadataEntity, new SnapshotMetadata
                {
                    snapshotTick = time.currentTick,
                    snapshotHash = snapshotManager.GetSnapshotInfo(time.currentTick).hash,
                    entityCount = snapshotManager.GetSnapshotInfo(time.currentTick).entityCount,
                    totalDataSize = snapshotManager.GetSnapshotInfo(time.currentTick).dataSize,
                    captureTimeMs = snapshotManager.LastCaptureTimeMs,
                    restoreTimeMs = 0
                });
            }
        }

        /// <summary>
        /// Restore simulation state to a specific tick
        /// </summary>
        public bool RestoreSnapshot(uint tick)
        {
            if (snapshotManager.RestoreSnapshot(tick))
            {
                // First, destroy any existing time entities
                var timeQuery = EntityManager.CreateEntityQuery(typeof(SimulationTimeComponent));
                EntityManager.DestroyEntity(timeQuery);
                timeQuery.Dispose();
                
                // Create new time entity
                var entity = EntityManager.CreateEntity();
                EntityManager.AddComponent<TimeAccumulatorComponent>(entity);
                EntityManager.AddComponent<SimulationTimeComponent>(entity);
                
                // Reset accumulator to 0 to ensure next frame processes correctly
                EntityManager.SetComponentData(entity, new TimeAccumulatorComponent 
                { 
                    accumulator = 0f,
                    fixedDeltaTime = 1f/60f
                });
                
                // Set current tick from snapshot
                EntityManager.SetComponentData(entity, new SimulationTimeComponent 
                { 
                    currentTick = tick,
                    lastConfirmedTick = tick - 1
                });

                // Find and reconnect player controlled entities to input system
                var playerQuery = EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PlayerControlledTag>(),
                    ComponentType.ReadOnly<PlayerControlComponent>()
                );

                var inputProcessor = InputProcessor.Instance;
                if (inputProcessor != null)
                {
                    var playerEntities = playerQuery.ToEntityArray(Allocator.Temp);
                    Debug.Log($"[SnapshotSystem] Found {playerEntities.Length} player controlled entities after restore");
                    
                    foreach (var playerEntity in playerEntities)
                    {
                        var playerIndex = EntityManager.GetComponentData<PlayerControlComponent>(playerEntity).playerIndex;
                        Debug.Log($"[SnapshotSystem] Restoring control for player {playerIndex} on entity {playerEntity.Index}");
                        
                        // Verify components exist
                        bool hasControl = EntityManager.HasComponent<PlayerControlComponent>(playerEntity);
                        bool hasTag = EntityManager.HasComponent<PlayerControlledTag>(playerEntity);
                        Debug.Log($"[SnapshotSystem] Entity {playerEntity.Index} has Control: {hasControl}, Tag: {hasTag}");
                        
                        inputProcessor.RegisterPlayerEntity(playerEntity, playerIndex);
                    }
                    playerEntities.Dispose();
                }
                else
                {
                    Debug.LogError("[SnapshotSystem] Could not find InputProcessor instance!");
                }
                playerQuery.Dispose();

                Debug.Log($"[SnapshotSystem] Restored snapshot at tick {tick} and recreated time components");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Rollback to a previous tick (for rollback networking)
        /// </summary>
        public bool RollbackToTick(uint targetTick)
        {
            if (!SystemAPI.TryGetSingleton<SimulationTimeComponent>(out var time))
            {
                Debug.LogWarning("[SnapshotSystem] Cannot rollback - time system not ready");
                return false;
            }

            var currentTick = time.currentTick;

            if (targetTick >= currentTick)
            {
                Debug.LogWarning($"[SnapshotSystem] Cannot rollback to future tick {targetTick}");
                return false;
            }

            // Find the nearest snapshot before or at the target tick
            uint nearestSnapshot = 0;
            foreach (var tick in snapshotHistory)
            {
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

            // Restore to nearest snapshot
            if (RestoreSnapshot(nearestSnapshot))
            {
                Debug.Log($"[SnapshotSystem] Rolled back from tick {currentTick} to {nearestSnapshot} " +
                         $"(target was {targetTick})");

                // TODO: Re-simulate from nearestSnapshot to targetTick
                // This requires replaying input and running simulation steps

                return true;
            }

            return false;
        }

        /// <summary>
        /// Compare two snapshots for determinism verification
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
        /// Get snapshot info for debugging
        /// </summary>
        public SnapshotInfo GetSnapshotInfo(uint tick)
        {
            return snapshotManager.GetSnapshotInfo(tick);
        }

        /// <summary>
        /// Get all available snapshot ticks
        /// </summary>
        public uint[] GetAvailableSnapshots()
        {
            return snapshotHistory.ToArray();
        }

        // Configuration methods
        public void SetAutoSnapshot(bool enabled) => autoSnapshot = enabled;
        public void SetSnapshotInterval(uint interval) => snapshotInterval = interval;
        public bool IsAutoSnapshotEnabled => autoSnapshot;
        public uint SnapshotInterval => snapshotInterval;
        public int SnapshotCount => snapshotManager.SnapshotCount;
    }

    /// <summary>
    /// Singleton component for snapshot configuration
    /// </summary>
    public struct SnapshotConfigComponent : IComponentData
    {
        public bool autoSnapshotEnabled;
        public uint snapshotInterval;
        public uint maxSnapshots;
        public bool enableValidation;
    }
}