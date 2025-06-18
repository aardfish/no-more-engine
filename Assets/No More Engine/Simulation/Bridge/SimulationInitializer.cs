using UnityEngine;
using Unity.Entities;
using NoMoreEngine.Session;
using NoMoreEngine.Simulation.Components;
using Unity.Mathematics.FixedPoint;
using UnityEngine.Video;


namespace NoMoreEngine.Simulation.Bridge
{
    /// <summary>
    /// SimulationInitializer - Bridge between GameConfiguration and ECS simulation
    /// Responsible for initial entity creation at match start only
    /// </summary>
    public class SimulationInitializer : MonoBehaviour
    {
        private EntityManager entityManager;
        private World simulationWorld;
        private SimulationEntityManager simEntityManager;

        void Awake()
        {
            // Get or create our simulation entity manager
            simEntityManager = SimulationEntityManager.Instance;
            if (simEntityManager == null)
            {
                var simEntityManagerObject = new GameObject("SimulationEntityManager");
                simEntityManager = simEntityManagerObject.AddComponent<SimulationEntityManager>();
            }
        }

        /// <summary>
        /// Initialize a match from game configuration
        /// </summary>
        public bool InitializeMatch(GameConfiguration config)
        {
            // Get world when we actually need it
            simulationWorld = World.DefaultGameObjectInjectionWorld;
            if (simulationWorld == null || !simulationWorld.IsCreated)
            {
                Debug.LogError("[SimulationInit] No ECS world available!");
                return false;
            }

            // Get EntityManager from world
            entityManager = simulationWorld.EntityManager;

            // Initialize our simulation entity manager with world
            simEntityManager.Initialize(simulationWorld);

            Debug.Log($"[SimulationInit] Initializing match: {config.stageName}, {config.GetActivePlayerCount()} players");

            // Clean up any previous match entities
            CleanupMatch();

            // Create stage/environment
            CreateStage(config.stageName);

            // Create player entities
            CreatePlayers(config);

            // Create match rules entity
            CreateMatchRules(config);

            return true;
        }

        /// <summary>
        /// Clean up all match entities
        /// </summary>
        public void CleanupMatch()
        {
            Debug.Log("[SimulationInit] Cleaning up match entities");

            simEntityManager.DestroyAllManagedEntities();
        }

        private void CreateStage(string stageName)
        {
            Debug.Log($"[SimulationInit] Creating stage: {stageName}");

            switch (stageName)
            {
                case "TestArena":
                    CreateTestArena();
                    break;

                case "SmallBox":
                    CreateSmallBox();
                    break;

                case "LargePlatform":
                    CreateLargePlatform();
                    break;

                default:
                    Debug.LogWarning($"[SimulationInit] Unknown stage '{stageName}', using TestArena");
                    CreateTestArena();
                    break;
            }
        }

        private void CreateTestArena()
        {
            // Create floor using our simulation entity manager
            var floor = CreatePlatform(new fp3(0, (fp)(-0.5f), 0), new fp3(20, 1, 20));

            // Create walls
            CreateWall(new fp3(10, 2, 0), new fp3(1, 4, 20));    // Right wall
            CreateWall(new fp3(-10, 2, 0), new fp3(1, 4, 20));   // Left wall
            CreateWall(new fp3(0, 2, 10), new fp3(20, 4, 1));    // Back wall
            CreateWall(new fp3(0, 2, -10), new fp3(20, 4, 1));   // Front wall

            Debug.Log("[SimulationInit] Created test arena");
        }

        private void CreateSmallBox()
        {
            // Small enclosed box for close combat
            var floor = CreatePlatform(fp3.zero, new fp3(10, 1, 10));

            // Walls
            CreateWall(new fp3(5, 2, 0), new fp3(1, 4, 10));    // Right
            CreateWall(new fp3(-5, 2, 0), new fp3(1, 4, 10));   // Left
            CreateWall(new fp3(0, 2, 5), new fp3(10, 4, 1));    // Back
            CreateWall(new fp3(0, 2, -5), new fp3(10, 4, 1));   // Front

            // Ceiling (optional)
            CreatePlatform(new fp3(0, 4, 0), new fp3(10, 1, 10));

            Debug.Log("[SimulationInit] Created small box arena");
        }

        private void CreateLargePlatform()
        {
            // Large open platform with some obstacles
            var floor = CreatePlatform(fp3.zero, new fp3(40, 1, 40));

            // Add some platforms at different heights
            CreatePlatform(new fp3(-10, 2, -10), new fp3(8, 1, 8));
            CreatePlatform(new fp3(10, 3, -10), new fp3(6, 1, 6));
            CreatePlatform(new fp3(-10, 4, 10), new fp3(6, 1, 6));
            CreatePlatform(new fp3(10, 2, 10), new fp3(8, 1, 8));

            // Center pillar
            CreateWall(new fp3(0, 3, 0), new fp3(4, 6, 4));

            Debug.Log("[SimulationInit] Created large platform arena");
        }

        private Entity CreatePlatform(fp3 position, fp3 size)
        {
            // Simplified - just delegate to simulation entity manager
            return simEntityManager.CreateEnvironmentEntity(position, size, isStatic: true);
        }

        private Entity CreateWall(fp3 position, fp3 size)
        {
            // Simplified - just delegate to simulation entity manager
            return simEntityManager.CreateEnvironmentEntity(position, size, isStatic: true);
        }

        private void CreatePlayers(GameConfiguration config)
        {
            var spawnPositions = config.GetSpawnPositions();
            int spawnIndex = 0;

            for (int i = 0; i < config.playerSlots.Length; i++)
            {
                var slot = config.playerSlots[i];
                if (slot.IsEmpty) continue;

                var playerEntity = simEntityManager.CreatePlayerEntity(
                    spawnPositions[spawnIndex],
                    (byte)slot.slotIndex,
                    slot.IsLocal
                );

                // Add any additional components for bots
                if (slot.IsBot)
                {
                    // AI component not yet implemented
                    // .AddComponent<AIControlledTag>(playerEntity);
                }

                spawnIndex++;
            }

            Debug.Log($"[SimulationInit] Created {spawnIndex} player entities");
        }

        private Entity CreatePlayerEntity(PlayerSlot slot, fp3 spawnPosition)
        {
            var entity = entityManager.CreateEntity();

            // Core components
            entityManager.AddComponentData(entity, new FixTransformComponent
            {
                position = spawnPosition,
                rotation = fpquaternion.identity,
                scale = new fp3(fp.one)
            });

            entityManager.AddComponentData(entity, new SimEntityTypeComponent
            {
                simEntityType = SimEntityType.Player
            });

            // Movement
            entityManager.AddComponentData(entity, new SimpleMovementComponent
            {
                velocity = fp3.zero,
                isMoving = false
            });

            // Collision
            entityManager.AddComponentData(entity, new CollisionBoundsComponent
            {
                size = new fp3(1, 2, 1),
                offset = fp3.zero,
                tolerance = (fp)0.001f
            });

            entityManager.AddComponentData(entity, new CollisionResponseComponent
            {
                responseType = CollisionResponse.Stop,
                entityLayer = CollisionLayer.Player,
                collidesWith = CollisionLayer.Environment | CollisionLayer.Enemy | CollisionLayer.Player,
                bounciness = (fp)0.1f,
                friction = (fp)0.2f
            });

            entityManager.AddBuffer<CollisionEventBuffer>(entity);

            // Physics
            entityManager.AddComponentData(entity, PhysicsComponent.Normal);

            // Player-specific components
            if (slot.IsLocal)
            {
                // Tag for player input system
                entityManager.AddComponent<PlayerControlledTag>(entity);
                
                // Add player control component with index
                entityManager.AddComponentData(entity, new PlayerControlComponent
                {
                    playerIndex = (byte)slot.slotIndex
                });
                
                Debug.Log($"[SimulationInit] Added player control components for P{slot.slotIndex + 1}");
            }
            else if (slot.IsBot)
            {
                // TODO: Add AI component
            }

            Debug.Log($"[SimulationInit] Created {slot.type} player entity at {spawnPosition}");

            return entity;
        }

        private void CreateMatchRules(GameConfiguration config)
        {
            // Create match rules as a system entity
            var archetype = simEntityManager.GetOrCreateArchetype(
                "MatchRules",
                typeof(MatchRulesComponent)
            );

            var rulesEntity = simEntityManager.CreateEntity(archetype, EntityCategory.System, "MatchRules");

            // Use Unity's EntityManger to set component data
            entityManager.SetComponentData(rulesEntity, new MatchRulesComponent
            {
                gameMode = config.gameMode,
                winCondition = config.winCondition,
                timeLimit = config.timeLimit,
                stockCount = config.stockCount,
                currentTime = 0f
            });

            Debug.Log($"[SimulationInit] Created match rules: {config.gameMode}, {config.winCondition}");
        }
    }

    /// <summary>
    /// Component to store match rules in ECS
    /// </summary>
    public struct MatchRulesComponent : IComponentData
    {
        public GameMode gameMode;
        public WinCondition winCondition;
        public float timeLimit;
        public int stockCount;
        public float currentTime;
    }
}