using Unity.Entities;
using UnityEngine;
using System.Collections.Generic;
using NoMoreEngine.Simulation.Components;
using Unity.Mathematics.FixedPoint;
using Unity.Collections;

namespace NoMoreEngine.Simulation.Bridge
{
    /// <summary>
    /// Centralized entity management system for the simulation
    /// Handles all entity creation, destruction, and tracking within the simulation world
    /// 
    /// FIXED: Now properly persists as a singleton throughout the entire play session
    /// </summary>
    public class SimulationEntityManager : MonoBehaviour
    {
        [Header("Entity Management Settings")]
        [SerializeField] private int initialEntityCapacity = 1000;
        [SerializeField] private bool debugLogging = false;

        // Core references
        private EntityManager entityManager;
        private World simulationWorld;

        // Entity tracking
        private Dictionary<EntityCategory, HashSet<Entity>> categorizedEntities;
        private HashSet<Entity> allSimulationEntities;
        private Dictionary<Entity, EntityMetadata> entityMetadata;

        // Entity templates/archetypes cache
        private Dictionary<string, EntityArchetype> archetypeCache;

        // Statistics
        private int totalEntitiesCreated = 0;
        private int totalEntitiesDestroyed = 0;

        // Events
        public event System.Action<Entity, EntityCategory> OnEntityCreated;
        public event System.Action<Entity, EntityCategory> OnEntityDestroyed;

        // Singleton for easy access
        private static SimulationEntityManager instance;
        public static SimulationEntityManager Instance
        {
            get
            {
                if (instance == null)
                {
                    // Try to find existing instance
                    instance = FindAnyObjectByType<SimulationEntityManager>();
                    
                    // If not found, create one
                    if (instance == null)
                    {
                        Debug.Log("[SimulationEntityManager] Creating singleton instance");
                        var go = new GameObject("SimulationEntityManager");
                        instance = go.AddComponent<SimulationEntityManager>();
                        
                        // Make it persist across scenes
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        #region Initialization

        void Awake()
        {
            // Enforce singleton pattern
            if (instance != null && instance != this)
            {
                Debug.LogWarning("[SimulationEntityManager] Duplicate instance detected, destroying...");
                Destroy(gameObject);
                return;
            }
            
            instance = this;
            
            // Make this persist across scene loads
            DontDestroyOnLoad(gameObject);
            
            InitializeCollections();
            
            Debug.Log("[SimulationEntityManager] Singleton initialized and set to persist");
        }

        void OnDestroy()
        {
            if (instance == this)
            {
                Debug.Log("[SimulationEntityManager] Singleton being destroyed");
                instance = null;
            }
        }

        private void InitializeCollections()
        {
            categorizedEntities = new Dictionary<EntityCategory, HashSet<Entity>>();
            allSimulationEntities = new HashSet<Entity>(initialEntityCapacity);
            entityMetadata = new Dictionary<Entity, EntityMetadata>(initialEntityCapacity);
            archetypeCache = new Dictionary<string, EntityArchetype>();

            // Initialize categories
            foreach (EntityCategory category in System.Enum.GetValues(typeof(EntityCategory)))
            {
                categorizedEntities[category] = new HashSet<Entity>();
            }
        }

        /// <summary>
        /// Initialize with simulation world reference
        /// Can be called multiple times safely (e.g., when entering InGame state)
        /// </summary>
        public void Initialize(World world)
        {
            // If already initialized with the same world, skip
            if (simulationWorld == world && entityManager == world.EntityManager)
            {
                if (debugLogging)
                    Debug.Log("[SimulationEntityManager] Already initialized with this world");
                return;
            }
            
            simulationWorld = world;
            entityManager = world.EntityManager;
            
            // Clear any stale entity references if world changed
            if (allSimulationEntities.Count > 0)
            {
                Debug.LogWarning("[SimulationEntityManager] World changed, clearing stale entity references");
                ClearAllTracking();
            }

            if (debugLogging)
                Debug.Log("[SimulationEntityManager] Initialized with simulation world");
        }
        
        /// <summary>
        /// Clear all tracking data (used when world changes)
        /// </summary>
        private void ClearAllTracking()
        {
            allSimulationEntities.Clear();
            entityMetadata.Clear();
            foreach (var category in categorizedEntities.Values)
            {
                category.Clear();
            }
            // Don't clear archetype cache - it's still valid
        }

        #endregion

        // ... (rest of the implementation remains the same) ...

        #region Entity Creation

        /// <summary>
        /// Create a player entity with standard components
        /// </summary>
        public Entity CreatePlayerEntity(fp3 position, byte playerIndex, bool isLocal = true)
        {
            if (!ValidateInitialized()) return Entity.Null;
            
            var entity = entityManager.CreateEntity();
            
            // Core components
            entityManager.AddComponentData(entity, new FixTransformComponent
            {
                position = position,
                rotation = fpquaternion.identity,
                scale = new fp3(fp.one)
            });

            entityManager.AddComponentData(entity, new SimEntityTypeComponent(SimEntityType.Player));
            entityManager.AddComponentData(entity, new SimpleMovementComponent(fp3.zero, false));

            // Collision
            entityManager.AddComponentData(entity, new CollisionBoundsComponent(
                new fp3(1, 2, 1), fp3.zero, (fp)0.001f));

            entityManager.AddComponentData(entity, new CollisionResponseComponent(
                CollisionResponse.Stop,
                CollisionLayer.Player,
                CollisionLayer.Environment | CollisionLayer.Enemy | CollisionLayer.Player,
                (fp)0.1f, (fp)0.2f));

            entityManager.AddBuffer<CollisionEventBuffer>(entity);

            // Physics
            entityManager.AddComponentData(entity, PhysicsComponent.Normal);

            // Player control
            if (isLocal)
            {
                entityManager.AddComponent<PlayerControlledTag>(entity);
                entityManager.AddComponentData(entity, new PlayerControlComponent(playerIndex));
            }

            // Track entity
            TrackEntity(entity, EntityCategory.Player, $"Player_{playerIndex}");

            return entity;
        }

        /// <summary>
        /// Create an environment entity (wall, platform, etc)
        /// </summary>
        public Entity CreateEnvironmentEntity(fp3 position, fp3 size, bool isStatic = true)
        {
            if (!ValidateInitialized()) return Entity.Null;
            
            var entity = entityManager.CreateEntity();

            entityManager.AddComponentData(entity, new FixTransformComponent
            {
                position = position,
                rotation = fpquaternion.identity,
                scale = size
            });

            entityManager.AddComponentData(entity, new SimEntityTypeComponent(SimEntityType.Environment));

            entityManager.AddComponentData(entity, new CollisionBoundsComponent(size, fp3.zero, (fp)0.001f));

            entityManager.AddComponentData(entity, new CollisionResponseComponent(
                CollisionResponse.None,
                CollisionLayer.Environment,
                CollisionLayer.All,
                (fp)0, (fp)0.5f));

            if (isStatic)
            {
                entityManager.AddComponentData(entity, new StaticColliderComponent(true));
            }

            TrackEntity(entity, EntityCategory.Environment, "Environment");

            return entity;
        }

        /// <summary>
        /// Create a projectile entity
        /// </summary>
        public Entity CreateProjectileEntity(fp3 position, fp3 velocity, Entity owner)
        {
            if (!ValidateInitialized()) return Entity.Null;
            
            var entity = entityManager.CreateEntity();

            entityManager.AddComponentData(entity, new FixTransformComponent
            {
                position = position,
                rotation = fpquaternion.identity,
                scale = new fp3((fp)0.5f)
            });

            entityManager.AddComponentData(entity, new SimEntityTypeComponent(SimEntityType.Projectile));
            entityManager.AddComponentData(entity, new SimpleMovementComponent(velocity, true));

            entityManager.AddComponentData(entity, new CollisionBoundsComponent(
                new fp3((fp)0.5f), fp3.zero, (fp)0.001f));

            entityManager.AddComponentData(entity, new CollisionResponseComponent(
                CollisionResponse.Destroy,
                CollisionLayer.Projectile,
                CollisionLayer.Player | CollisionLayer.Enemy | CollisionLayer.Environment,
                (fp)0, (fp)0));

            entityManager.AddBuffer<CollisionEventBuffer>(entity);

            // Physics for projectiles
            entityManager.AddComponentData(entity, new PhysicsComponent(
                mass: (fp)0.1f,
                gravityScale: (fp)0.5f,
                useGlobalGravity: true,
                affectedByGravity: true,
                terminalVelocity: (fp)100));

            TrackEntity(entity, EntityCategory.Projectile, $"Projectile_from_{owner.Index}");

            return entity;
        }

        /// <summary>
        /// Create multiple empty entities for snapshot restoration
        /// </summary>
        public NativeArray<Entity> CreateEntitiesForRestore(int count, Allocator allocator)
        {
            if (!ValidateInitialized())
            {
                return new NativeArray<Entity>(0, allocator);
            }
            
            // Create empty archetype for restoration
            var archetype = entityManager.CreateArchetype();

            // Create entities
            var entities = new NativeArray<Entity>(count, allocator);
            entityManager.CreateEntity(archetype, entities);

            // Pre-track all entities as Unknown category (will be updated during restore)
            for (int i = 0; i < count; i++)
            {
                TrackEntity(entities[i], EntityCategory.Unknown, $"Restoring_{entities[i].Index}");
            }

            if (debugLogging)
                Debug.Log($"[SimulationEntityManager] Created and tracked {count} entities for snapshot restore");

            return entities;
        }

        /// <summary>
        /// Create a generic entity with custom archetype
        /// </summary>
        public Entity CreateEntity(EntityArchetype archetype, EntityCategory category, string name = null)
        {
            if (!ValidateInitialized()) return Entity.Null;
            
            var entity = entityManager.CreateEntity(archetype);
            TrackEntity(entity, category, name ?? category.ToString());
            return entity;
        }

        /// <summary>
        /// Get or create a cached archetype
        /// </summary>
        public EntityArchetype GetOrCreateArchetype(string name, params ComponentType[] components)
        {
            if (!ValidateInitialized()) return default;
            
            if (!archetypeCache.TryGetValue(name, out var archetype))
            {
                archetype = entityManager.CreateArchetype(components);
                archetypeCache[name] = archetype;
            }
            return archetype;
        }

        #endregion

        #region Entity Destruction

        /// <summary>
        /// Destroy a single entity with proper cleanup
        /// </summary>
        public void DestroyEntity(Entity entity)
        {
            if (!ValidateInitialized()) return;
            
            if (!entityManager.Exists(entity)) return;

            // Get category before destruction
            var category = GetEntityCategory(entity);

            // Remove from tracking
            UntrackEntity(entity);

            // Destroy the entity
            entityManager.DestroyEntity(entity);

            totalEntitiesDestroyed++;

            // Notify listeners
            OnEntityDestroyed?.Invoke(entity, category);

            if (debugLogging)
                Debug.Log($"[SimulationEntityManager] Destroyed entity {entity.Index} (category: {category})");
        }

        /// <summary>
        /// Destroy all entities in a category
        /// </summary>
        public void DestroyAllInCategory(EntityCategory category)
        {
            if (!ValidateInitialized()) return;
            
            if (!categorizedEntities.TryGetValue(category, out var entities)) return;

            // Copy to array to avoid modification during iteration
            var toDestroy = new Entity[entities.Count];
            entities.CopyTo(toDestroy);

            foreach (var entity in toDestroy)
            {
                DestroyEntity(entity);
            }

            Debug.Log($"[SimulationEntityManager] Destroyed {toDestroy.Length} entities in category {category}");
        }

        /// <summary>
        /// Destroy all managed entities excluding singletons
        /// </summary>
        public void DestroyAllManagedEntities()
        {
            if (!ValidateInitialized()) return;
            
            Debug.Log($"[SimulationEntityManager] DestroyAllManagedEntities called - tracking {allSimulationEntities.Count} entities");

            // Copy to avoid modification during iteration
            var toDestroy = new Entity[allSimulationEntities.Count];
            allSimulationEntities.CopyTo(toDestroy);

            int destroyedCount = 0;
            foreach (var entity in toDestroy)
            {
                if (entityManager.Exists(entity))
                {
                    entityManager.DestroyEntity(entity);
                    destroyedCount++;
                }
            }

            // Clear all tracking
            allSimulationEntities.Clear();
            entityMetadata.Clear();
            foreach (var category in categorizedEntities.Values)
            {
                category.Clear();
            }

            Debug.Log($"[SimulationEntityManager] Destroyed {destroyedCount} game entities");
        }

        #endregion

        #region Entity Tracking

        private void TrackEntity(Entity entity, EntityCategory category, string name)
        {
            allSimulationEntities.Add(entity);
            categorizedEntities[category].Add(entity);

            var metadata = new EntityMetadata
            {
                entity = entity,
                category = category,
                name = name,
                creationTime = Time.time,
                creationFrame = Time.frameCount
            };

            entityMetadata[entity] = metadata;
            totalEntitiesCreated++;

            OnEntityCreated?.Invoke(entity, category);

            if (debugLogging)
                Debug.Log($"[SimulationEntityManager] Tracked entity {entity.Index} as {name} (category: {category})");
        }

        private void UntrackEntity(Entity entity)
        {
            allSimulationEntities.Remove(entity);

            if (entityMetadata.TryGetValue(entity, out var metadata))
            {
                categorizedEntities[metadata.category].Remove(entity);
                entityMetadata.Remove(entity);
            }
        }

        #endregion

        #region Queries and Utilities

        /// <summary>
        /// Get all entities in a specific category
        /// </summary>
        public Entity[] GetEntitiesInCategory(EntityCategory category)
        {
            if (!ValidateInitialized()) return new Entity[0];
            
            if (categorizedEntities.TryGetValue(category, out var entities))
            {
                var array = new Entity[entities.Count];
                entities.CopyTo(array);
                return array;
            }
            return new Entity[0];
        }

        /// <summary>
        /// Update the category of a tracked entity after restoration
        /// </summary>
        public void UpdateEntityCategory(Entity entity, EntityCategory newCategory, string newName = null)
        {
            if (!ValidateInitialized()) return;
            
            if (!entityMetadata.TryGetValue(entity, out var metadata))
            {
                Debug.LogError($"[SimulationEntityManager] Cannot update category for untracked entity {entity.Index}");
                return;
            }
            
            // Remove from old category
            categorizedEntities[metadata.category].Remove(entity);
            
            // Add to new category
            categorizedEntities[newCategory].Add(entity);
            
            // Update metadata
            metadata.category = newCategory;
            if (!string.IsNullOrEmpty(newName))
                metadata.name = newName;
            
            entityMetadata[entity] = metadata;
            
            if (debugLogging)
                Debug.Log($"[SimulationEntityManager] Updated entity {entity.Index} from {metadata.category} to {newCategory}");
        }

        /// <summary>
        /// Get entity category
        /// </summary>
        public EntityCategory GetEntityCategory(Entity entity)
        {
            if (entityMetadata.TryGetValue(entity, out var metadata))
            {
                return metadata.category;
            }
            return EntityCategory.Unknown;
        }

        /// <summary>
        /// Check if entity is managed by this system
        /// </summary>
        public bool IsManagedEntity(Entity entity)
        {
            return allSimulationEntities.Contains(entity);
        }

        /// <summary>
        /// Get entity count by category
        /// </summary>
        public int GetEntityCount(EntityCategory category)
        {
            return categorizedEntities.TryGetValue(category, out var entities) ? entities.Count : 0;
        }

        /// <summary>
        /// Get total managed entity count
        /// </summary>
        public int GetTotalManagedEntities()
        {
            return allSimulationEntities.Count;
        }

        /// <summary>
        /// Get entity statistics
        /// </summary>
        public EntityStatistics GetStatistics()
        {
            var stats = new EntityStatistics
            {
                totalCreated = totalEntitiesCreated,
                totalDestroyed = totalEntitiesDestroyed,
                currentActive = allSimulationEntities.Count,
                categoryCounts = new Dictionary<EntityCategory, int>()
            };

            foreach (var kvp in categorizedEntities)
            {
                stats.categoryCounts[kvp.Key] = kvp.Value.Count;
            }

            return stats;
        }

        #endregion

        #region Validation and Cleanup

        /// <summary>
        /// Validate that the manager is properly initialized
        /// </summary>
        private bool ValidateInitialized()
        {
            if (simulationWorld == null || !simulationWorld.IsCreated || entityManager == null)
            {
                Debug.LogError("[SimulationEntityManager] Not initialized! Call Initialize(world) first.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Validate all tracked entities still exist
        /// </summary>
        public void ValidateTrackedEntities()
        {
            if (!ValidateInitialized()) return;
            
            var toRemove = new List<Entity>();

            foreach (var entity in allSimulationEntities)
            {
                if (!entityManager.Exists(entity))
                {
                    toRemove.Add(entity);
                }
            }

            foreach (var entity in toRemove)
            {
                UntrackEntity(entity);
                Debug.LogWarning($"[SimulationEntityManager] Removed invalid entity {entity.Index} from tracking");
            }

            if (toRemove.Count > 0)
            {
                Debug.Log($"[SimulationEntityManager] Cleaned up {toRemove.Count} invalid entities");
            }
        }

        #endregion
    }

    // ... (EntityCategory, EntityMetadata, and EntityStatistics remain the same) ...
    
    /// <summary>
    /// Categories for organizing entities
    /// </summary>
    public enum EntityCategory
    {
        Unknown,
        Player,
        Enemy,
        Projectile,
        Environment,
        Pickup,
        Effect,
        UI,
        System
    }

    /// <summary>
    /// Metadata tracked for each entity
    /// </summary>
    public struct EntityMetadata
    {
        public Entity entity;
        public EntityCategory category;
        public string name;
        public float creationTime;
        public int creationFrame;
    }

    /// <summary>
    /// Entity statistics for debugging/monitoring
    /// </summary>
    public class EntityStatistics
    {
        public int totalCreated;
        public int totalDestroyed;
        public int currentActive;
        public Dictionary<EntityCategory, int> categoryCounts;
    }
}