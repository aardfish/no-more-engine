using NoMoreEngine.Simulation.Components;
using NoMoreEngine.Simulation.Systems;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;


namespace NoMoreEngine.Simulation.Bridge
{
    /// <summary>
    /// Core simulation world manager - handles ECS world setup and essential management
    /// No debug UI or visualization - pure simulation management
    /// Provides clean API for other systems to interact with the simulation world
    /// </summary>
    public class SimulationWorldManager : MonoBehaviour
    {
        [Header("World Settings")]
        [SerializeField] private bool autoInitialize = true;
        [SerializeField] private bool createDefaultGravity = true;

        // Core ECS references
        private EntityManager entityManager;
        private World simulationWorld;

        // Essential entity queries
        private EntityQuery simEntityQuery;
        private EntityQuery movingEntityQuery;
        private EntityQuery physicsEntityQuery;
        private EntityQuery collisionEntityQuery;

        // Initialization state
        private bool isInitialized = false;

        #region Unity Lifecycle

        void Start()
        {
            if (autoInitialize)
            {
                Initialize();
            }
        }

        void OnDestroy()
        {
            Cleanup();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initialize the simulation world manager
        /// </summary>
        public void Initialize()
        {
            if (isInitialized)
            {
                Debug.LogWarning("SimulationWorldManager already initialized");
                return;
            }

            // Get reference to ECS world
            simulationWorld = World.DefaultGameObjectInjectionWorld;
            if (simulationWorld == null || !simulationWorld.IsCreated)
            {
                Debug.LogError("Default ECS World not found or not created");
                return;
            }

            entityManager = simulationWorld.EntityManager;

            // Create essential entity queries
            CreateEntityQueries();

            // Setup default components if needed
            if (createDefaultGravity)
            {
                EnsureGlobalGravityExists();
            }

            isInitialized = true;
            Debug.Log("SimulationWorldManager initialized successfully");
        }

        /// <summary>
        /// Create all essential entity queries for simulation management
        /// </summary>
        private void CreateEntityQueries()
        {
            // Core SimEntity query
            simEntityQuery = entityManager.CreateEntityQuery(
                typeof(FixTransformComponent),
                typeof(SimEntityTypeComponent)
            );

            // Moving entities query
            movingEntityQuery = entityManager.CreateEntityQuery(
                typeof(FixTransformComponent),
                typeof(SimpleMovementComponent)
            );

            // Physics entities query
            physicsEntityQuery = entityManager.CreateEntityQuery(
                typeof(FixTransformComponent),
                typeof(PhysicsComponent)
            );

            // Collision entities query
            collisionEntityQuery = entityManager.CreateEntityQuery(
                typeof(FixTransformComponent),
                typeof(CollisionBoundsComponent)
            );
        }

        /// <summary>
        /// Ensure global gravity component exists
        /// </summary>
        private void EnsureGlobalGravityExists()
        {
            var gravityQuery = entityManager.CreateEntityQuery(typeof(GlobalGravityComponent));

            if (gravityQuery.IsEmpty)
            {
                var entity = entityManager.CreateEntity();
                entityManager.AddComponentData(entity, GlobalGravityComponent.EarthGravity);
                Debug.Log("Created default global gravity component");
            }

            gravityQuery.Dispose();
        }

        #endregion

        #region Public API - World Access

        /// <summary>
        /// Get the simulation world (read-only access)
        /// </summary>
        public World SimulationWorld => simulationWorld;

        /// <summary>
        /// Get the entity manager (use carefully - prefer specific methods)
        /// </summary>
        public EntityManager EntityManager => entityManager;

        /// <summary>
        /// Check if the simulation world is properly initialized
        /// </summary>
        public bool IsInitialized => isInitialized && simulationWorld != null && simulationWorld.IsCreated;

        #endregion

        #region Public API - Entity Queries

        /// <summary>
        /// Get query for all SimEntities
        /// </summary>
        public EntityQuery GetSimEntityQuery() => simEntityQuery;

        /// <summary>
        /// Get query for moving entities
        /// </summary>
        public EntityQuery GetMovingEntityQuery() => movingEntityQuery;

        /// <summary>
        /// Get query for physics entities
        /// </summary>
        public EntityQuery GetPhysicsEntityQuery() => physicsEntityQuery;

        /// <summary>
        /// Get query for collision entities
        /// </summary>
        public EntityQuery GetCollisionEntityQuery() => collisionEntityQuery;

        #endregion

        #region Public API - Entity Counts

        /// <summary>
        /// Get total count of SimEntities
        /// </summary>
        public int GetSimEntityCount() => IsInitialized ? simEntityQuery.CalculateEntityCount() : 0;

        /// <summary>
        /// Get count of moving entities
        /// </summary>
        public int GetMovingEntityCount() => IsInitialized ? movingEntityQuery.CalculateEntityCount() : 0;

        /// <summary>
        /// Get count of physics entities
        /// </summary>
        public int GetPhysicsEntityCount() => IsInitialized ? physicsEntityQuery.CalculateEntityCount() : 0;

        /// <summary>
        /// Get count of collision entities
        /// </summary>
        public int GetCollisionEntityCount() => IsInitialized ? collisionEntityQuery.CalculateEntityCount() : 0;

        /// <summary>
        /// Get detailed entity counts by type
        /// </summary>
        public SimEntityCounts GetDetailedEntityCounts()
        {
            var counts = new SimEntityCounts();

            if (!IsInitialized) return counts;

            var entities = simEntityQuery.ToEntityArray(Allocator.Temp);
            var types = simEntityQuery.ToComponentDataArray<SimEntityTypeComponent>(Allocator.Temp);

            for (int i = 0; i < types.Length; i++)
            {
                switch (types[i].simEntityType)
                {
                    case SimEntityType.Player: counts.playerCount++; break;
                    case SimEntityType.Enemy: counts.enemyCount++; break;
                    case SimEntityType.Projectile: counts.projectileCount++; break;
                    case SimEntityType.Environment: counts.environmentCount++; break;
                }
            }

            entities.Dispose();
            types.Dispose();

            return counts;
        }

        #endregion

        #region Public API - Gravity Control

        /// <summary>
        /// Set global gravity scale
        /// </summary>
        public void SetGlobalGravityScale(fix scale)
        {
            if (!IsInitialized) return;
            GravityUtility.SetGlobalGravityScale(entityManager, scale);
        }

        /// <summary>
        /// Enable/disable global gravity
        /// </summary>
        public void SetGlobalGravityEnabled(bool enabled)
        {
            if (!IsInitialized) return;
            GravityUtility.SetGlobalGravityEnabled(entityManager, enabled);
        }

        /// <summary>
        /// Set global gravity configuration
        /// </summary>
        public void SetGlobalGravity(GlobalGravityComponent newGravity)
        {
            if (!IsInitialized) return;
            GravityUtility.SetGlobalGravity(entityManager, newGravity);
        }

        /// <summary>
        /// Get current global gravity settings
        /// </summary>
        public GlobalGravityComponent? GetGlobalGravity()
        {
            if (!IsInitialized) return null;

            var gravityQuery = entityManager.CreateEntityQuery(typeof(GlobalGravityComponent));

            if (!gravityQuery.IsEmpty)
            {
                var gravity = gravityQuery.GetSingleton<GlobalGravityComponent>();
                gravityQuery.Dispose();
                return gravity;
            }

            gravityQuery.Dispose();
            return null;
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Clean up resources
        /// </summary>
        private void Cleanup()
        {
            // Entity queries are automatically disposed by ECS
            isInitialized = false;
        }

        #endregion
    }

    /// <summary>
    /// Struct for detailed entity count information
    /// </summary>
    public struct SimEntityCounts
    {
        public int playerCount;
        public int enemyCount;
        public int projectileCount;
        public int environmentCount;

        public int TotalCount => playerCount + enemyCount + projectileCount + environmentCount;
    }
}