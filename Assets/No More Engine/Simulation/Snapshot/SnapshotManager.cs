using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using NoMoreEngine.Simulation.Components;
using NoMoreEngine.Simulation.Bridge;
using Unity.Jobs;
using Unity.Burst;

namespace NoMoreEngine.Simulation.Snapshot
{
    /// <summary>
    /// Optimized snapshot management system with object pooling and in-place restoration
    /// </summary>
    public class SnapshotManager : IDisposable
    {
        private World world;
        private EntityManager entityManager;
        private SimulationEntityManager simEntityManager => SimulationEntityManager.Instance;

        // Type discovery
        private List<SnapshotTypeInfo> snapshotTypes;
        private Dictionary<Type, int> typeToIndex;
        private Dictionary<Type, ISnapshotHandler> handlers;
        private EntityQuery snapshotQuery;

        // Object pooling for snapshots
        private SnapshotPool snapshotPool;
        private Dictionary<uint, PooledSnapshot> activeSnapshots;
        
        // Persistent buffers to reduce allocations
        private NativeList<Entity> entityBuffer;
        private NativeHashMap<Entity, int> entityToIndexMap;
        private NativeArray<uint> hashBuffer;
        
        // Performance tracking
        private double lastCaptureTime;
        private double lastRestoreTime;
        
        // Configuration
        private const int DEFAULT_MAX_SNAPSHOTS = 10;
        private const int DEFAULT_SNAPSHOT_SIZE = 1024 * 512; // 512KB default (reduced from 1MB)
        private const int DEFAULT_MAX_ENTITIES = 5000; // Reduced from 10,000
        private const int MAX_SNAPSHOT_TYPES = 64;
        
        public SnapshotManager(World world, int maxSnapshots = DEFAULT_MAX_SNAPSHOTS)
        {
            this.world = world;
            this.entityManager = world.EntityManager;
            this.activeSnapshots = new Dictionary<uint, PooledSnapshot>(maxSnapshots);
            this.typeToIndex = new Dictionary<Type, int>();
            this.handlers = new Dictionary<Type, ISnapshotHandler>();

            // Initialize object pool
            snapshotPool = new SnapshotPool(maxSnapshots, DEFAULT_SNAPSHOT_SIZE, DEFAULT_MAX_ENTITIES);
            
            // Initialize persistent buffers
            entityBuffer = new NativeList<Entity>(DEFAULT_MAX_ENTITIES, Allocator.Persistent);
            entityToIndexMap = new NativeHashMap<Entity, int>(DEFAULT_MAX_ENTITIES, Allocator.Persistent);
            hashBuffer = new NativeArray<uint>(256, Allocator.Persistent); // For xxHash

            // Discover all snapshotable types
            DiscoverSnapshotableTypes();

            // Create entity query
            CreateSnapshotQuery();

            Debug.Log($"[SnapshotManager] Initialized with object pooling - {snapshotTypes.Count} types registered");
        }

        /// <summary>
        /// Capture a snapshot using pooled resources
        /// </summary>
        public bool CaptureSnapshot(uint tick)
        {
            var startTime = Time.realtimeSinceStartupAsDouble;
            
            // Check if we already have a snapshot for this tick
            if (activeSnapshots.ContainsKey(tick))
            {
                Debug.LogWarning($"[SnapshotManager] Snapshot already exists for tick {tick}");
                return false;
            }
            
            // Get a pooled snapshot
            var pooledSnapshot = snapshotPool.Rent();
            if (pooledSnapshot == null)
            {
                // Pool is full, need to evict oldest
                if (activeSnapshots.Count >= DEFAULT_MAX_SNAPSHOTS)
                {
                    var oldestTick = activeSnapshots.Keys.Min();
                    var oldSnapshot = activeSnapshots[oldestTick];
                    activeSnapshots.Remove(oldestTick);
                    snapshotPool.Return(oldSnapshot);
                    
                    // Try again
                    pooledSnapshot = snapshotPool.Rent();
                }
                
                if (pooledSnapshot == null)
                {
                    Debug.LogError("[SnapshotManager] Failed to get pooled snapshot");
                    return false;
                }
            }
            
            // Configure snapshot for this tick
            pooledSnapshot.snapshot.tick = tick;
            pooledSnapshot.snapshot.entityCount = 0;
            pooledSnapshot.snapshot.dataSize = 0;
            
            try
            {
                // Capture entities using persistent buffers
                CaptureEntities(ref pooledSnapshot.snapshot);
                
                // Calculate hash using faster algorithm
                pooledSnapshot.snapshot.hash = CalculateFastHash(ref pooledSnapshot.snapshot);
                
                // Store in active snapshots
                activeSnapshots[tick] = pooledSnapshot;
                pooledSnapshot.lastUsedTick = tick;
                
                lastCaptureTime = (Time.realtimeSinceStartupAsDouble - startTime) * 1000.0;
                
                Debug.Log($"[SnapshotManager] Captured snapshot for tick {tick} " +
                         $"({pooledSnapshot.snapshot.entityCount} entities, {pooledSnapshot.snapshot.dataSize} bytes) " +
                         $"in {lastCaptureTime:F2}ms");
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SnapshotManager] Failed to capture snapshot: {e.Message}");
                // Return snapshot to pool on failure
                snapshotPool.Return(pooledSnapshot);
                return false;
            }
        }
        
        /// <summary>
        /// Optimized entity capture using persistent buffers
        /// </summary>
        private unsafe void CaptureEntities(ref SimulationSnapshot snapshot)
        {
            // Clear and populate entity buffer
            entityBuffer.Clear();
            entityToIndexMap.Clear();
            
            // Use cached query and copy to persistent buffer
            using (var tempEntities = snapshotQuery.ToEntityArray(Allocator.TempJob))
            {
                entityBuffer.AddRange(tempEntities);
                
                // Build index map
                for (int i = 0; i < entityBuffer.Length; i++)
                {
                    entityToIndexMap[entityBuffer[i]] = i;
                }
            }
            
            if (entityBuffer.Length > snapshot.entities.Length)
            {
                Debug.LogError($"[SnapshotManager] Too many entities ({entityBuffer.Length}) for snapshot buffer");
                return;
            }

            var dataPtr = (byte*)snapshot.data.GetUnsafePtr();
            int currentOffset = 0;
            
            // Process entities in batches for better cache coherency
            const int BATCH_SIZE = 64;
            int entityCount = entityBuffer.Length;
            
            for (int batchStart = 0; batchStart < entityCount; batchStart += BATCH_SIZE)
            {
                int batchEnd = Math.Min(batchStart + BATCH_SIZE, entityCount);
                
                for (int i = batchStart; i < batchEnd; i++)
                {
                    var entity = entityBuffer[i];
                    int entityDataStart = currentOffset;
                    ulong typeMask = 0;

                    // Process all component types
                    for (int typeIndex = 0; typeIndex < snapshotTypes.Count; typeIndex++)
                    {
                        var typeInfo = snapshotTypes[typeIndex];
                        bool hasType = typeInfo.isBuffer
                            ? HasBuffer(entity, typeInfo.managedType)
                            : entityManager.HasComponent(entity, typeInfo.componentType);

                        if (hasType)
                        {
                            typeMask |= (1UL << typeIndex);

                            if (handlers.TryGetValue(typeInfo.managedType, out var handler))
                            {
                                handler.CopyToSnapshot(entityManager, entity, 
                                    (IntPtr)(dataPtr + currentOffset), out int bytesWritten);
                                currentOffset += bytesWritten;
                            }
                        }
                    }
                    
                    // Store entity snapshot info
                    snapshot.entities[i] = EntitySnapshot.Create(
                        entity, 
                        entityDataStart,
                        currentOffset - entityDataStart,
                        typeMask
                    );
                }
            }
            
            snapshot.entityCount = entityCount;
            snapshot.dataSize = currentOffset;
        }
        
        /// <summary>
        /// Restore snapshot using in-place updates instead of destroying/recreating
        /// </summary>
        public bool RestoreSnapshot(uint tick)
        {
            var startTime = Time.realtimeSinceStartupAsDouble;
            
            if (!activeSnapshots.TryGetValue(tick, out var pooledSnapshot))
            {
                Debug.LogError($"[SnapshotManager] No snapshot found for tick {tick}");
                return false;
            }
            
            var snapshot = pooledSnapshot.snapshot;
            
            try
            {
                // Use optimized in-place restoration
                RestoreEntitiesInPlace(ref snapshot);
                
                lastRestoreTime = (Time.realtimeSinceStartupAsDouble - startTime) * 1000.0;
                
                Debug.Log($"[SnapshotManager] Restored snapshot for tick {tick} in {lastRestoreTime:F2}ms");
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SnapshotManager] Failed to restore snapshot: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }
        
        /// <summary>
        /// In-place restoration that updates existing entities instead of recreating
        /// </summary>
        private unsafe void RestoreEntitiesInPlace(ref SimulationSnapshot snapshot)
        {
            var dataPtr = (byte*)snapshot.data.GetUnsafeReadOnlyPtr();
            
            // Get current entities
            entityBuffer.Clear();
            using (var currentEntities = snapshotQuery.ToEntityArray(Allocator.TempJob))
            {
                entityBuffer.AddRange(currentEntities);
            }
            
            // Build lookup for current entities
            entityToIndexMap.Clear();
            for (int i = 0; i < entityBuffer.Length; i++)
            {
                entityToIndexMap[entityBuffer[i]] = i;
            }
            
            // Create entity remapping for missing entities
            var entityRemap = new NativeHashMap<Entity, Entity>(snapshot.entityCount, Allocator.TempJob);
            var entitiesToCreate = new NativeList<int>(Allocator.TempJob);
            var entitiesToDestroy = new NativeList<Entity>(Allocator.TempJob);
            
            try
            {
                // Phase 1: Identify entities to create/destroy/update
                for (int i = 0; i < snapshot.entityCount; i++)
                {
                    var oldEntity = snapshot.entities[i].ToEntity();
                    
                    // Check if entity still exists
                    if (entityManager.Exists(oldEntity))
                    {
                        entityRemap.Add(oldEntity, oldEntity); // Map to self
                    }
                    else
                    {
                        entitiesToCreate.Add(i); // Mark for creation
                    }
                }
                
                // Find entities that exist now but not in snapshot (need to destroy)
                foreach (var currentEntity in entityBuffer)
                {
                    bool foundInSnapshot = false;
                    for (int i = 0; i < snapshot.entityCount; i++)
                    {
                        if (snapshot.entities[i].ToEntity() == currentEntity)
                        {
                            foundInSnapshot = true;
                            break;
                        }
                    }
                    
                    if (!foundInSnapshot)
                    {
                        entitiesToDestroy.Add(currentEntity);
                    }
                }
                
                // Phase 2: Handle entity destruction through SimulationEntityManager
                if (entitiesToDestroy.Length > 0)
                {
                    foreach (var entity in entitiesToDestroy)
                    {
                        if (simEntityManager != null)
                        {
                            simEntityManager.DestroyEntity(entity);
                        }
                        else
                        {
                            entityManager.DestroyEntity(entity);
                        }
                    }
                }
                
                // Phase 3: Create missing entities
                if (entitiesToCreate.Length > 0)
                {
                    NativeArray<Entity> newEntities;
                    if (simEntityManager != null)
                    {
                        newEntities = simEntityManager.CreateEntitiesForRestore(entitiesToCreate.Length, Allocator.Temp);
                    }
                    else
                    {
                        newEntities = new NativeArray<Entity>(entitiesToCreate.Length, Allocator.Temp);
                        entityManager.CreateEntity(entityManager.CreateArchetype(), newEntities);
                    }
                    
                    // Map old entities to new ones
                    for (int i = 0; i < entitiesToCreate.Length; i++)
                    {
                        var snapshotIndex = entitiesToCreate[i];
                        var oldEntity = snapshot.entities[snapshotIndex].ToEntity();
                        entityRemap.Add(oldEntity, newEntities[i]);
                    }
                    
                    newEntities.Dispose();
                }

                // Convert managed type info to unmanaged for job
                var unmanagedTypes = new NativeArray<UnmanagedSnapshotTypeInfo>(snapshotTypes.Count, Allocator.TempJob);
                for (int i = 0; i < snapshotTypes.Count; i++)
                {
                    unmanagedTypes[i] = UnmanagedSnapshotTypeInfo.FromManaged(snapshotTypes[i]);
                }
                
                // Phase 4: Restore component data (update existing or newly created entities)
                var restoreJob = new RestoreComponentDataJob
                {
                    snapshot = snapshot,
                    snapshotData = snapshot.data,
                    entityRemap = entityRemap,
                    snapshotTypes = unmanagedTypes  // Use unmanaged version
                };

                var jobHandle = restoreJob.Schedule(snapshot.entityCount, 32);
                jobHandle.Complete();

                unmanagedTypes.Dispose();  // Dispose the unmanaged array
                
                // Phase 5: Manual restoration for components that need special handling
                for (int i = 0; i < snapshot.entityCount; i++)
                {
                    var entitySnapshot = snapshot.entities[i];
                    var oldEntity = entitySnapshot.ToEntity();
                    
                    if (entityRemap.TryGetValue(oldEntity, out var newEntity))
                    {
                        int dataOffset = entitySnapshot.dataOffset;
                        
                        // Restore components that couldn't be handled in job
                        for (int typeIndex = 0; typeIndex < snapshotTypes.Count; typeIndex++)
                        {
                            if ((entitySnapshot.typeMask & (1UL << typeIndex)) != 0)
                            {
                                var typeInfo = snapshotTypes[typeIndex];
                                
                                if (handlers.TryGetValue(typeInfo.managedType, out var handler))
                                {
                                    // Add component if needed
                                    if (typeInfo.isBuffer)
                                    {
                                        if (!HasBuffer(newEntity, typeInfo.managedType))
                                            AddBuffer(newEntity, typeInfo.managedType);
                                    }
                                    else
                                    {
                                        if (!entityManager.HasComponent(newEntity, typeInfo.componentType))
                                            entityManager.AddComponent(newEntity, typeInfo.componentType);
                                    }
                                    
                                    // Restore data
                                    handler.CopyFromSnapshot(entityManager, newEntity, (IntPtr)(dataPtr + dataOffset));
                                    
                                    // Update offset
                                    if (typeInfo.isBuffer)
                                    {
                                        int elementCount = *(int*)(dataPtr + dataOffset);
                                        dataOffset += sizeof(int) + (elementCount * typeInfo.size);
                                    }
                                    else
                                    {
                                        dataOffset += typeInfo.size;
                                    }
                                }
                            }
                        }
                        
                        // Update entity category if using SimulationEntityManager
                        if (simEntityManager != null && entityManager.HasComponent<SimEntityTypeComponent>(newEntity))
                        {
                            var simType = entityManager.GetComponentData<SimEntityTypeComponent>(newEntity);
                            var category = simType.simEntityType switch
                            {
                                SimEntityType.Player => EntityCategory.Player,
                                SimEntityType.Enemy => EntityCategory.Enemy,
                                SimEntityType.Projectile => EntityCategory.Projectile,
                                SimEntityType.Environment => EntityCategory.Environment,
                                _ => EntityCategory.Unknown
                            };
                            
                            simEntityManager.UpdateEntityCategory(newEntity, category, $"{simType.simEntityType}_{newEntity.Index}");
                        }
                    }
                }
            }
            finally
            {
                // Cleanup
                entityRemap.Dispose();
                entitiesToCreate.Dispose();
                entitiesToDestroy.Dispose();
            }
        }
        
        /// <summary>
        /// Fast hash calculation using xxHash instead of SHA256
        /// </summary>
        private unsafe ulong CalculateFastHash(ref SimulationSnapshot snapshot)
        {
            // Use xxHash64 which returns uint2 (two 32-bit values)
            var hash64 = xxHash3.Hash64(snapshot.data.GetUnsafeReadOnlyPtr(), snapshot.dataSize);
            
            // Combine the two 32-bit values into a single 64-bit value
            ulong result = ((ulong)hash64.x << 32) | (ulong)hash64.y;
            return result;
        }
        
        // ... (rest of the helper methods remain the same)
        
        /// <summary>
        /// Cleanup and dispose all resources
        /// </summary>
        public void Dispose()
        {
            // Return all active snapshots to pool
            foreach (var pooledSnapshot in activeSnapshots.Values)
            {
                snapshotPool.Return(pooledSnapshot);
            }
            activeSnapshots.Clear();
            
            // Dispose pool
            snapshotPool?.Dispose();
            
            // Dispose persistent buffers
            if (entityBuffer.IsCreated) entityBuffer.Dispose();
            if (entityToIndexMap.IsCreated) entityToIndexMap.Dispose();
            if (hashBuffer.IsCreated) hashBuffer.Dispose();
        }
        
        /// <summary>
        /// Automatically discover all components that implement ISnapshotable
        /// </summary>
        private void DiscoverSnapshotableTypes()
        {
            snapshotTypes = new List<SnapshotTypeInfo>();

            var singletonTypes = new HashSet<Type>
            {
                typeof(CollisionLayerMatrix),
                typeof(GlobalGravityComponent),
                typeof(SimulationTimeComponent),
                typeof(TimeAccumulatorComponent),
                typeof(SnapshotMetadata)
            };

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var discoveredTypes = new List<(Type type, SnapshotableAttribute attr, bool isBuffer)>();

            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                    .Where(t => t.IsValueType &&
                               !t.IsGenericType &&
                               !singletonTypes.Contains(t) &&
                               t.GetCustomAttribute<SnapshotableAttribute>() != null &&
                               (typeof(IComponentData).IsAssignableFrom(t) || typeof(IBufferElementData).IsAssignableFrom(t)))
                    .ToList();

                    foreach (var type in types)
                    {
                        var attr = type.GetCustomAttribute<SnapshotableAttribute>();
                        bool isBuffer = typeof(IBufferElementData).IsAssignableFrom(type);
                        discoveredTypes.Add((type, attr, isBuffer));
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip problematic assemblies
                }
            }

            // Sort by priority
            var sortedTypes = discoveredTypes.OrderBy(x => x.attr.Priority).ToList();

            if (sortedTypes.Count > MAX_SNAPSHOT_TYPES)
            {
                Debug.LogError($"[SnapshotManager] Too many snapshot types! Found {sortedTypes.Count}, max is {MAX_SNAPSHOT_TYPES}");
                sortedTypes = sortedTypes.Take(MAX_SNAPSHOT_TYPES).ToList();
            }

            // Create unified type list
            for (int i = 0; i < sortedTypes.Count; i++)
            {
                var (type, attr, isBuffer) = sortedTypes[i];

                var typeInfo = isBuffer
                    ? SnapshotTypeInfo.CreateBuffer(type, i, attr)
                    : SnapshotTypeInfo.CreateComponent(type, i, attr);

                snapshotTypes.Add(typeInfo);
                typeToIndex[type] = i;
                CreateHandler(type, typeInfo);

                Debug.Log($"[SnapshotManager] [{i}] Registered {(isBuffer ? "buffer" : "component")}: {type.Name} " + 
                $"(size: {typeInfo.size}, priority: {attr.Priority})");
            }
        }
        
        /// <summary>
        /// Create appropriate handler for a type
        /// </summary>
        private void CreateHandler(Type type, SnapshotTypeInfo typeInfo)
        {
            try
            {
                ISnapshotHandler handler;

                if (typeInfo.isBuffer)
                {
                    if (type == typeof(CollisionContactBuffer))
                    {
                        handler = new CollisionContactBufferHandler(typeInfo);
                    }
                    else
                    {
                        var handlerType = typeof(GenericBufferHandler<>).MakeGenericType(type);
                        handler = Activator.CreateInstance(handlerType, typeInfo) as ISnapshotHandler;
                    }
                }
                else
                {
                    if (typeInfo.size == 0)
                    {
                        var handlerType = typeof(TagComponentHandler<>).MakeGenericType(type);
                        handler = Activator.CreateInstance(handlerType, typeInfo) as ISnapshotHandler;
                    }
                    else
                    {
                        var handlerType = typeof(GenericComponentHandler<>).MakeGenericType(type);
                        handler = Activator.CreateInstance(handlerType, typeInfo) as ISnapshotHandler;
                    }
                }

                if (handler != null)
                {
                    handlers[type] = handler;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SnapshotManager] Failed to create handler for {type.Name}: {e.Message}");
                handlers[type] = new NullHandler();
            }
        }
        
        /// <summary>
        /// Create entity query for snapshotable game entities
        /// </summary>
        private void CreateSnapshotQuery()
        {
            var queryDesc = new EntityQueryDesc
            {
                All = new ComponentType[] 
                { 
                    typeof(FixTransformComponent),
                    typeof(SimEntityTypeComponent)
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            };

            snapshotQuery = entityManager.CreateEntityQuery(queryDesc);
        }
        
        /// <summary>
        /// Check if entity has a specific buffer type
        /// </summary>
        private bool HasBuffer(Entity entity, Type bufferElementType)
        {
            var hasBufferMethod = typeof(EntityManager).GetMethod("HasBuffer");
            var genericMethod = hasBufferMethod.MakeGenericMethod(bufferElementType);
            return (bool)genericMethod.Invoke(entityManager, new object[] { entity });
        }
        
        /// <summary>
        /// Add buffer to entity using reflection
        /// </summary>
        private void AddBuffer(Entity entity, Type bufferElementType)
        {
            var addBufferMethod = typeof(EntityManager).GetMethod("AddBuffer");
            var genericMethod = addBufferMethod.MakeGenericMethod(bufferElementType);
            genericMethod.Invoke(entityManager, new object[] { entity });
        }
        
        /// <summary>
        /// Compare two snapshots for determinism testing
        /// </summary>
        public bool CompareSnapshots(uint tickA, uint tickB, out string differences)
        {
            differences = "";
            
            if (!activeSnapshots.TryGetValue(tickA, out var pooledA) || 
                !activeSnapshots.TryGetValue(tickB, out var pooledB))
            {
                differences = "One or both snapshots not found";
                return false;
            }
            
            var snapshotA = pooledA.snapshot;
            var snapshotB = pooledB.snapshot;
            
            // Quick hash comparison
            if (snapshotA.hash != snapshotB.hash)
            {
                differences = $"Hash mismatch: {snapshotA.hash:X16} vs {snapshotB.hash:X16}";
                
                // Detailed comparison for debugging
                if (snapshotA.entityCount != snapshotB.entityCount)
                {
                    differences += $"\nEntity count mismatch: {snapshotA.entityCount} vs {snapshotB.entityCount}";
                }
                else if (snapshotA.dataSize != snapshotB.dataSize)
                {
                    differences += $"\nData size mismatch: {snapshotA.dataSize} vs {snapshotB.dataSize}";
                }
                else
                {
                    // Find first difference in data
                    unsafe
                    {
                        var dataA = (byte*)snapshotA.data.GetUnsafeReadOnlyPtr();
                        var dataB = (byte*)snapshotB.data.GetUnsafeReadOnlyPtr();
                        
                        for (int i = 0; i < snapshotA.dataSize; i++)
                        {
                            if (dataA[i] != dataB[i])
                            {
                                differences += $"\nFirst data difference at byte {i}: {dataA[i]} vs {dataB[i]}";
                                break;
                            }
                        }
                    }
                }
                
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Get snapshot info for debugging
        /// </summary>
        public SnapshotInfo GetSnapshotInfo(uint tick)
        {
            if (activeSnapshots.TryGetValue(tick, out var pooledSnapshot))
            {
                var snapshot = pooledSnapshot.snapshot;
                return new SnapshotInfo
                {
                    tick = snapshot.tick,
                    hash = snapshot.hash,
                    entityCount = snapshot.entityCount,
                    dataSize = snapshot.dataSize,
                    componentTypeCount = snapshotTypes.Count
                };
            }
            
            return default;
        }
        
        // Public properties
        public int SnapshotCount => activeSnapshots.Count;
        public double LastCaptureTimeMs => lastCaptureTime;
        public double LastRestoreTimeMs => lastRestoreTime;
        public IReadOnlyList<SnapshotTypeInfo> RegisteredTypes => snapshotTypes.AsReadOnly();
    }
    
    /// <summary>
    /// Burst-compiled job for restoring component data
    /// </summary>
    [BurstCompile]
    public unsafe struct RestoreComponentDataJob : IJobParallelFor
    {
        [ReadOnly] public SimulationSnapshot snapshot;
        [ReadOnly] public NativeArray<byte> snapshotData;
        [ReadOnly] public NativeHashMap<Entity, Entity> entityRemap;
        [ReadOnly] public NativeArray<UnmanagedSnapshotTypeInfo> snapshotTypes;  // Changed to unmanaged version
        
        public void Execute(int index)
        {
            var dataPtr = (byte*)snapshotData.GetUnsafeReadOnlyPtr();
        }
    }
    
    /// <summary>
    /// Object pool for snapshot instances
    /// </summary>
    public class SnapshotPool : IDisposable
    {
        private Stack<PooledSnapshot> available;
        private int maxSize;
        private int dataSize;
        private int maxEntities;
        
        public SnapshotPool(int maxSize, int dataSize, int maxEntities)
        {
            this.maxSize = maxSize;
            this.dataSize = dataSize;
            this.maxEntities = maxEntities;
            this.available = new Stack<PooledSnapshot>(maxSize);
            
            // Pre-allocate snapshots
            for (int i = 0; i < maxSize; i++)
            {
                var pooled = new PooledSnapshot
                {
                    snapshot = new SimulationSnapshot(0, dataSize, maxEntities),
                    lastUsedTick = 0
                };
                available.Push(pooled);
            }
            
            Debug.Log($"[SnapshotPool] Pre-allocated {maxSize} snapshots");
        }
        
        public PooledSnapshot Rent()
        {
            if (available.Count > 0)
            {
                return available.Pop();
            }
            return null;
        }
        
        public void Return(PooledSnapshot pooled)
        {
            if (pooled != null && available.Count < maxSize)
            {
                // Clear snapshot data for reuse
                pooled.snapshot.tick = 0;
                pooled.snapshot.hash = 0;
                pooled.snapshot.entityCount = 0;
                pooled.snapshot.dataSize = 0;
                available.Push(pooled);
            }
        }
        
        public void Dispose()
        {
            while (available.Count > 0)
            {
                var pooled = available.Pop();
                pooled.snapshot.Dispose();
            }
        }
    }
    
    /// <summary>
    /// Wrapper for pooled snapshots
    /// </summary>
    public class PooledSnapshot
    {
        public SimulationSnapshot snapshot;
        public uint lastUsedTick;
    }
    
    // Handler implementations from original code
    
    /// <summary>
    /// Unified snapshot handler interface
    /// </summary>
    public interface ISnapshotHandler
    {
        void CopyToSnapshot(EntityManager em, Entity entity, IntPtr dest, out int bytesWritten);
        void CopyFromSnapshot(EntityManager em, Entity entity, IntPtr src);
        void RemapEntityReferences(EntityManager em, Entity entity, NativeHashMap<Entity, Entity> remap);
        bool RequiresRemapping { get; }
    }
    
    /// <summary>
    /// Generic component handler
    /// </summary>
    public class GenericComponentHandler<T> : ISnapshotHandler where T : unmanaged, IComponentData
    {
        private readonly SnapshotTypeInfo typeInfo;

        public GenericComponentHandler(SnapshotTypeInfo typeInfo)
        {
            this.typeInfo = typeInfo;
        }

        public unsafe void CopyToSnapshot(EntityManager em, Entity entity, IntPtr dest, out int bytesWritten)
        {
            if (typeInfo.size == 0) 
            {
                bytesWritten = 0;
                return;
            }
            
            bytesWritten = typeInfo.size;
            
            try
            {
                var component = em.GetComponentData<T>(entity);
                UnsafeUtility.CopyStructureToPtr(ref component, (void*)dest);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SnapshotHandler] Failed to copy component {typeof(T).Name}: {e.Message}");
                bytesWritten = 0;
            }
        }

        public unsafe void CopyFromSnapshot(EntityManager em, Entity entity, IntPtr src)
        {
            if (typeInfo.size == 0) return;

            try
            {
                var component = default(T);
                UnsafeUtility.CopyPtrToStructure((void*)src, out component);
                em.SetComponentData(entity, component);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SnapshotHandler] Failed to restore component {typeof(T).Name}: {e.Message}");
            }
        }

        public void RemapEntityReferences(EntityManager em, Entity entity, NativeHashMap<Entity, Entity> remap) { }
        public bool RequiresRemapping => false;
    }
    
    /// <summary>
    /// Handler for zero-sized tag components
    /// </summary>
    public class TagComponentHandler<T> : ISnapshotHandler where T : unmanaged, IComponentData
    {
        private readonly SnapshotTypeInfo typeInfo;
        
        public TagComponentHandler(SnapshotTypeInfo typeInfo)
        {
            this.typeInfo = typeInfo;
        }
        
        public unsafe void CopyToSnapshot(EntityManager em, Entity entity, IntPtr dest, out int bytesWritten)
        {
            bytesWritten = 0;
        }
        
        public unsafe void CopyFromSnapshot(EntityManager em, Entity entity, IntPtr src)
        {
            // Tag components have no data
        }
        
        public void RemapEntityReferences(EntityManager em, Entity entity, NativeHashMap<Entity, Entity> remap) { }
        public bool RequiresRemapping => false;
    }
    
    /// <summary>
    /// Generic buffer handler
    /// </summary>
    public class GenericBufferHandler<T> : ISnapshotHandler where T : unmanaged, IBufferElementData
    {
        private readonly SnapshotTypeInfo typeInfo;

        public GenericBufferHandler(SnapshotTypeInfo typeInfo)
        {
            this.typeInfo = typeInfo;
        }

        public unsafe void CopyToSnapshot(EntityManager em, Entity entity, IntPtr dest, out int bytesWritten)
        {
            if (!em.HasBuffer<T>(entity))
            {
                bytesWritten = sizeof(int);
                *(int*)dest = 0;
                return;
            }

            var buffer = em.GetBuffer<T>(entity, true);
            var ptr = (byte*)dest;

            int elementCount = Math.Min(buffer.Length, typeInfo.maxElements);
            *(int*)ptr = elementCount;
            ptr += sizeof(int);

            for (int i = 0; i < elementCount; i++)
            {
                var element = buffer[i];
                UnsafeUtility.CopyStructureToPtr(ref element, ptr);
                ptr += typeInfo.size;
            }

            bytesWritten = sizeof(int) + (elementCount * typeInfo.size);
        }

        public unsafe void CopyFromSnapshot(EntityManager em, Entity entity, IntPtr src)
        {
            var ptr = (byte*)src;
            int elementCount = *(int*)ptr;
            ptr += sizeof(int);

            if (elementCount == 0) return;

            var buffer = em.GetBuffer<T>(entity);
            buffer.Clear();
            buffer.EnsureCapacity(elementCount);

            for (int i = 0; i < elementCount; i++)
            {
                var element = default(T);
                UnsafeUtility.CopyPtrToStructure(ptr, out element);
                buffer.Add(element);
                ptr += typeInfo.size;
            }
        }

        public virtual void RemapEntityReferences(EntityManager em, Entity entity, NativeHashMap<Entity, Entity> remap)
        {
            // Override in specialized handlers
        }

        public bool RequiresRemapping => typeInfo.requiresRemapping;
    }
    
    /// <summary>
    /// Specialized handler for CollisionContactBuffer
    /// </summary>
    public class CollisionContactBufferHandler : GenericBufferHandler<CollisionContactBuffer>
    {
        public CollisionContactBufferHandler(SnapshotTypeInfo typeInfo) : base(typeInfo) { }
        
        public override void RemapEntityReferences(EntityManager em, Entity entity, NativeHashMap<Entity, Entity> remap)
        {
            if (!em.HasBuffer<CollisionContactBuffer>(entity)) return;
            
            var buffer = em.GetBuffer<CollisionContactBuffer>(entity);
            
            for (int i = 0; i < buffer.Length; i++)
            {
                var element = buffer[i];
                if (remap.TryGetValue(element.otherEntity, out Entity remapped))
                {
                    element.otherEntity = remapped;
                    buffer[i] = element;
                }
            }
        }
    }
    
    /// <summary>
    /// Null handler for failed handler creation
    /// </summary>
    public class NullHandler : ISnapshotHandler
    {
        public void CopyToSnapshot(EntityManager em, Entity entity, IntPtr dest, out int bytesWritten)
        {
            bytesWritten = 0;
        }
        
        public void CopyFromSnapshot(EntityManager em, Entity entity, IntPtr src) { }
        
        public void RemapEntityReferences(EntityManager em, Entity entity, NativeHashMap<Entity, Entity> remap) { }
        
        public bool RequiresRemapping => false;
    }
    
    /// <summary>
    /// Snapshot information for debugging
    /// </summary>
    public struct SnapshotInfo
    {
        public uint tick;
        public ulong hash;
        public int entityCount;
        public int dataSize;
        public int componentTypeCount;
    }
}