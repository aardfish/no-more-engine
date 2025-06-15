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
    /// Cleaned up version with proper player control restoration
    /// </summary>
    [UpdateInGroup(typeof(SimulationStepSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(CleanupPhase))]
    public partial class SnapshotSystem : SystemBase
    {
        private SnapshotManager snapshotManager;
        private SimulationTimeSystem timeSystem;
        private PlayerInputMovementSystem inputMovementSystem;

        // Configuration
        private bool autoSnapshot = false;
        private uint snapshotInterval = 60; // Snapshot every second at 60Hz
        private uint lastSnapshotTick = 0;

        // Rollback support
        private Queue<uint> snapshotHistory;
        private const int MAX_HISTORY = 10;

        // Initialization tracking
        private float initializationTime = 0f;
        private const float INITIALIZATION_DELAY = 0.1f;

        protected override void OnCreate()
        {
            // Initialize snapshot manager
            snapshotManager = new SnapshotManager(World, MAX_HISTORY);
            snapshotHistory = new Queue<uint>(MAX_HISTORY);

            // Get references to other systems
            timeSystem = World.GetExistingSystemManaged<SimulationTimeSystem>();
            inputMovementSystem = World.GetExistingSystemManaged<PlayerInputMovementSystem>();

            Debug.Log("[SnapshotSystem] Initialized with proper system references");
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
        /// Capture a snapshot of the current simulation state
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

            // Log player entity state before capture
            LogPlayerEntityState("Before Capture");

            if (snapshotManager.CaptureSnapshot(time.currentTick))
            {
                // Track snapshot history
                snapshotHistory.Enqueue(time.currentTick);
                while (snapshotHistory.Count > MAX_HISTORY)
                {
                    snapshotHistory.Dequeue();
                }

                // Create metadata entity
                CreateSnapshotMetadata(time.currentTick);
                
                Debug.Log($"[SnapshotSystem] Successfully captured snapshot at tick {time.currentTick}");
            }
        }

        /// <summary>
        /// Restore simulation state to a specific tick
        /// </summary>
        public bool RestoreSnapshot(uint tick)
        {
            Debug.Log($"[SnapshotSystem] Beginning snapshot restore to tick {tick}");
            
            // Log state before restore
            LogPlayerEntityState("Before Restore");
            
            if (!snapshotManager.RestoreSnapshot(tick))
            {
                Debug.LogError($"[SnapshotSystem] Failed to restore snapshot for tick {tick}");
                return false;
            }

            // Restore was successful, now handle the cleanup
            
            // 1. Restore time components
            RestoreTimeComponents(tick);
            
            // 2. Force input system to recognize the restored player entities
            RestorePlayerControl();
            
            // 3. Log final state
            LogPlayerEntityState("After Restore");
            
            Debug.Log($"[SnapshotSystem] Successfully restored snapshot at tick {tick}");
            return true;
        }

        /// <summary>
        /// Restore time components after snapshot restore
        /// </summary>
        private void RestoreTimeComponents(uint tick)
        {
            // Destroy existing time entities
            var timeQuery = EntityManager.CreateEntityQuery(typeof(SimulationTimeComponent));
            EntityManager.DestroyEntity(timeQuery);
            timeQuery.Dispose();
            
            // Create fresh time entity
            var entity = EntityManager.CreateEntity();
            EntityManager.SetName(entity, "SimulationTime_Restored");
            
            // Add time components
            EntityManager.AddComponent<TimeAccumulatorComponent>(entity);
            EntityManager.AddComponent<SimulationTimeComponent>(entity);
            
            // Initialize with proper values
            EntityManager.SetComponentData(entity, new TimeAccumulatorComponent 
            { 
                accumulator = 0f,
                fixedDeltaTime = 1f/60f,
                maxCatchUpSteps = 3,
                stepsLastFrame = 0
            });
            
            // Set time to restored tick
            var timeComponent = SimulationTimeComponent.Create60Hz();
            timeComponent.currentTick = tick;
            timeComponent.lastConfirmedTick = tick > 0 ? tick - 1 : 0;
            timeComponent.elapsedTime = (fix)(tick / 60f); // Assuming 60Hz
            
            EntityManager.SetComponentData(entity, timeComponent);
        }

        /// <summary>
        /// Restore player control after snapshot restore
        /// </summary>
        private void RestorePlayerControl()
        {
            // Force the input movement system to reconnect
            if (inputMovementSystem != null)
            {
                inputMovementSystem.ForceReconnection();
            }
            
            // Also ensure InputProcessor knows about the player entities
            var inputProcessor = InputProcessor.Instance;
            if (inputProcessor != null)
            {
                var playerQuery = EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PlayerControlledTag>(),
                    ComponentType.ReadOnly<PlayerControlComponent>()
                );

                var playerEntities = playerQuery.ToEntityArray(Allocator.Temp);
                
                foreach (var playerEntity in playerEntities)
                {
                    if (EntityManager.HasComponent<PlayerControlComponent>(playerEntity))
                    {
                        var playerControl = EntityManager.GetComponentData<PlayerControlComponent>(playerEntity);
                        inputProcessor.RegisterPlayerEntity(playerEntity, playerControl.playerIndex);
                    }
                }
                
                playerEntities.Dispose();
                playerQuery.Dispose();
            }
        }

        /// <summary>
        /// Helper method to log player entity state for debugging
        /// </summary>
        private void LogPlayerEntityState(string context)
        {
            var playerQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerControlledTag>(),
                ComponentType.ReadOnly<PlayerControlComponent>()
            );
            
            var count = playerQuery.CalculateEntityCount();
            Debug.Log($"[SnapshotSystem] {context}: {count} player entities exist");
            
            if (count > 0)
            {
                var entities = playerQuery.ToEntityArray(Allocator.Temp);
                var controls = playerQuery.ToComponentDataArray<PlayerControlComponent>(Allocator.Temp);
                
                for (int i = 0; i < Mathf.Min(count, 10); i++) // Limit to 10 for log spam
                {
                    Debug.Log($"  - Entity {entities[i].Index}: Player {controls[i].playerIndex}");
                }
                
                entities.Dispose();
                controls.Dispose();
            }
            
            playerQuery.Dispose();
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
                restoreTimeMs = 0
            });
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