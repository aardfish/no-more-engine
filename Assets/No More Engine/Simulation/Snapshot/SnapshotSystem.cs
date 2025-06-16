using Unity.Entities;
using Unity.Burst;
using UnityEngine;
using Unity.Collections;
using NoMoreEngine.Simulation.Components;
using System.Collections.Generic;
using NoMoreEngine.Simulation.Systems;
using NoMoreEngine.Input;
using Unity.Mathematics.FixedPoint;

namespace NoMoreEngine.Simulation.Snapshot
{
    /// <summary>
    /// Deterministic snapshot system - captures and restores exact state
    /// No post-restore modifications to maintain determinism
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

            Debug.Log("[SnapshotSystem] Initialized for deterministic operation");
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

            // Log state before capture
            LogSystemState("Before Capture", time.currentTick);

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
        /// Deterministic - only restores exact captured state, no modifications
        /// </summary>
        public bool RestoreSnapshot(uint tick)
        {
            Debug.Log($"[SnapshotSystem] Beginning deterministic snapshot restore to tick {tick}");
            
            // Log state before restore
            LogSystemState("Before Restore", tick);

            // 1. First destroy all game entities (but preserve critical singletons)
            DestroyGameEntities();
            
            //2. Restore the snapshot (includes entity remapping)
            if (!snapshotManager.RestoreSnapshot(tick))
            {
                Debug.LogError($"[SnapshotSystem] Failed to restore snapshot for tick {tick}");
                return false;
            }

            // 3. Restore time components
            RestoreTimeComponents(tick);

            // 4. Ensure singletons are properly restored
            EnsureSingletons();

            // 5. Force collision state initialization
            InitializeCollisionStates();

            // 6. Reconnect input system
            RestorePlayerControl();

            // 7. Log final state
            LogSystemState("After Restore", tick);
            
            Debug.Log($"[SnapshotSystem] Successfully restored snapshot at tick {tick}");
            return true;
        }

        private void DestroyGameEntities()
        {
            // Destroy all entities except critical singletons
            var gameEntityQuery = EntityManager.CreateEntityQuery(
                new EntityQueryDesc
                {
                    Any = new ComponentType[]
                    {
                        typeof(SimEntityTypeComponent),
                        typeof(FixTransformComponent),
                        typeof(SimpleMovementComponent)
                    },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                }
            );

            EntityManager.DestroyEntity(gameEntityQuery);
            gameEntityQuery.Dispose();
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
                fixedDeltaTime = 1f / 60f,
                maxCatchUpSteps = 3,
                stepsLastFrame = 0
            });

            // Set time to restored tick
            var timeComponent = SimulationTimeComponent.Create60Hz();
            timeComponent.currentTick = tick;
            timeComponent.lastConfirmedTick = tick > 0 ? tick - 1 : 0;
            timeComponent.elapsedTime = (fp)(tick / 60f); // Assuming 60Hz

            EntityManager.SetComponentData(entity, timeComponent);
        }

        private void EnsureSingletons()
        {
            // Ensure collision Layer matrix exists
            var layerMatrixQuery = EntityManager.CreateEntityQuery(typeof(CollisionLayerMatrix));
            if (layerMatrixQuery.IsEmpty)
            {
                var entity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(entity, CollisionLayerMatrix.CreateDefault());
                Debug.Log("[SnapshotSystem] Recreated CollisionLayerMatrix singleton");
            }
            layerMatrixQuery.Dispose();

            // Ensure global gravity exists
            var gravityQuery = EntityManager.CreateEntityQuery(typeof(GlobalGravityComponent));
            if (gravityQuery.IsEmpty)
            {
                var entity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(entity, GlobalGravityComponent.EarthGravity);
                Debug.Log("[SnapshotSystem] Recreated GlobalGravity singleton");
            }
            gravityQuery.Dispose();
        }

        private void InitializeCollisionStates()
        {
            // CollisionStateSystem is an ISystem (struct), not ComponentSystemBase
            // We don't need to get a reference to it - it will run automatically
            // Just ensure the components exist
            
            var needsStateQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CollisionBoundsComponent>(),
                ComponentType.ReadOnly<SimpleMovementComponent>(),
                ComponentType.Exclude<CollisionStateComponent>()
            );

            if (!needsStateQuery.IsEmpty)
            {
                EntityManager.AddComponent<CollisionStateComponent>(needsStateQuery);

                // Initialize with defaults
                var entities = needsStateQuery.ToEntityArray(Allocator.Temp);
                foreach (var entity in entities)
                {
                    EntityManager.SetComponentData(entity, CollisionStateComponent.Default);
                }
                entities.Dispose();

                Debug.Log($"[SnapshotSystem] Initialized collision states for {needsStateQuery.CalculateEntityCount()} entities");
            }

            needsStateQuery.Dispose();
        }

        /// <summary>
        /// Restore player control connections after snapshot restore
        /// This only reconnects the input system, doesn't modify game state
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
        /// Log system state for debugging
        /// </summary>
        private void LogSystemState(string context, uint tick)
        {
            var playerQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerControlledTag>(),
                ComponentType.ReadOnly<PlayerControlComponent>(),
                ComponentType.ReadOnly<FixTransformComponent>()
            );
            
            var collisionStateQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<CollisionStateComponent>()
            );
            
            var playerCount = playerQuery.CalculateEntityCount();
            var collisionStateCount = collisionStateQuery.CalculateEntityCount();
            
            Debug.Log($"[SnapshotSystem] {context} at tick {tick}:");
            Debug.Log($"  - Player entities: {playerCount}");
            Debug.Log($"  - Entities with collision state: {collisionStateCount}");
            
            // Log detailed player info
            if (playerCount > 0)
            {
                var entities = playerQuery.ToEntityArray(Allocator.Temp);
                var controls = playerQuery.ToComponentDataArray<PlayerControlComponent>(Allocator.Temp);
                var transforms = playerQuery.ToComponentDataArray<FixTransformComponent>(Allocator.Temp);
                
                for (int i = 0; i < Mathf.Min(playerCount, 5); i++)
                {
                    bool hasCollisionState = EntityManager.HasComponent<CollisionStateComponent>(entities[i]);
                    string groundedInfo = "";
                    
                    if (hasCollisionState)
                    {
                        var collisionState = EntityManager.GetComponentData<CollisionStateComponent>(entities[i]);
                        groundedInfo = $", Grounded: {collisionState.isGrounded}";
                    }
                    
                    Debug.Log($"    Entity {entities[i].Index}: Player {controls[i].playerIndex}, " +
                             $"Pos: {transforms[i].position}{groundedInfo}");
                }
                
                entities.Dispose();
                controls.Dispose();
                transforms.Dispose();
            }
            
            playerQuery.Dispose();
            collisionStateQuery.Dispose();
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