using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Security.Cryptography;
using NoMoreEngine.Simulation.Components;
using NoMoreEngine.Simulation.Bridge;
using Unity.Entities.UniversalDelegates;

namespace NoMoreEngine.Simulation.Snapshot
{
    /// <summary>
    /// Core snapshot management system
    /// Automatically discovers and handles all snapshotable components
    /// </summary>
    public class SnapshotManager
    {
        private World world;
        private EntityManager entityManager;
        private SimulationEntityManager simEntityManager => SimulationEntityManager.Instance;

        // Single unified type list
        private List<SnapshotTypeInfo> snapshotTypes;
        private Dictionary<Type, int> typeToIndex;
        private Dictionary<Type, ISnapshotHandler> handlers;
        private EntityQuery snapshotQuery;

        // Entity remapping for buffers
        private NativeHashMap<Entity, Entity> entityRemapTable;
        
        // Snapshot storage
        private Dictionary<uint, SimulationSnapshot> snapshots;
        private int maxSnapshots;
        
        // Performance tracking
        private double lastCaptureTime;
        private double lastRestoreTime;
        
        // Configuration
        private const int DEFAULT_MAX_SNAPSHOTS = 10;
        private const int DEFAULT_SNAPSHOT_SIZE = 1024 * 1024; // 1MB default
        private const int DEFAULT_MAX_ENTITIES = 10000;
        private const int MAX_SNAPSHOT_TYPES = 64; // Limited by ulong mask
        
        public SnapshotManager(World world, int maxSnapshots = DEFAULT_MAX_SNAPSHOTS)
        {
            this.world = world;
            this.entityManager = world.EntityManager;
            this.maxSnapshots = maxSnapshots;
            this.snapshots = new Dictionary<uint, SimulationSnapshot>(maxSnapshots);
            this.typeToIndex = new Dictionary<Type, int>();
            this.handlers = new Dictionary<Type, ISnapshotHandler>();
            
            // Discover all snapshotable types
            DiscoverSnapshotableTypes();

            // Create entity query
            CreateSnapshotQuery();

            Debug.Log($"[SnapshotManager] Initialized with {snapshotTypes.Count} components and buffers");
        }

        /// <summary>
        /// Automatically discover all components that implement ISnapshotable
        /// </summary>
        private void DiscoverSnapshotableTypes()
        {
            snapshotTypes = new List<SnapshotTypeInfo>();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var discoveredTypes = new List<(Type type, SnapshotableAttribute attr, bool isBuffer)>();

            foreach (var assembly in assemblies)
            {
                try
                {
                    // Find all types with SnapshotableAttribute
                    var types = assembly.GetTypes()
                    .Where
                    (
                        t => t.IsValueType &&
                        !t.IsGenericType &&
                        t.GetCustomAttribute<SnapshotableAttribute>() != null &&
                        (typeof(IComponentData).IsAssignableFrom(t) || typeof(IBufferElementData).IsAssignableFrom(t))
                    ).ToList();

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

            // Sort by priority and separate into components and buffers
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

                Debug.Log($"[SnapshotManager] [{i}] Registered {(isBuffer ? "buffer" : "component")}: {type.Name} " + $"(size: {typeInfo.size}, priority: {attr.Priority})");
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
                    // Special handlers for specific buffer types
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
                    var handlerType = typeof(GenericComponentHandler<>).MakeGenericType(type);
                    handler = Activator.CreateInstance(handlerType, typeInfo) as ISnapshotHandler;
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
        /// Create entity query for all snapshotable entities
        /// </summary>
        private void CreateSnapshotQuery()
        {
            if (snapshotTypes.Count == 0)
            {
                Debug.LogWarning("[SnapshotManager] No snapshotable types found!");
                return;
            }

            var allTypes = snapshotTypes.Select(t => t.componentType).ToArray();

            var queryDesc = new EntityQueryDesc
            {
                Any = allTypes,
                Options = EntityQueryOptions.IncludeDisabledEntities
            };

            snapshotQuery = entityManager.CreateEntityQuery(queryDesc);
        }
        
        /// <summary>
        /// Capture a snapshot of the current simulation state
        /// </summary>
        public bool CaptureSnapshot(uint tick)
        {
            var startTime = Time.realtimeSinceStartupAsDouble;
            
            // Check if we already have a snapshot for this tick
            if (snapshots.ContainsKey(tick))
            {
                Debug.LogWarning($"[SnapshotManager] Snapshot already exists for tick {tick}");
                return false;
            }
            
            // Remove oldest snapshot if at capacity
            if (snapshots.Count >= maxSnapshots)
            {
                var oldestTick = snapshots.Keys.Min();
                snapshots[oldestTick].Dispose();
                snapshots.Remove(oldestTick);
            }
            
            // Create new snapshot
            var snapshot = new SimulationSnapshot(tick, DEFAULT_SNAPSHOT_SIZE, DEFAULT_MAX_ENTITIES);
            
            try
            {
                // Capture all entities
                CaptureEntities(ref snapshot);
                
                // Calculate hash
                snapshot.hash = CalculateSnapshotHash(ref snapshot);
                
                // Store snapshot
                snapshots[tick] = snapshot;
                
                lastCaptureTime = (Time.realtimeSinceStartupAsDouble - startTime) * 1000.0;
                
                Debug.Log($"[SnapshotManager] Captured snapshot for tick {tick} " +
                         $"({snapshot.entityCount} entities, {snapshot.dataSize} bytes) in {lastCaptureTime:F2}ms");
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SnapshotManager] Failed to capture snapshot: {e.Message}");
                Debug.LogError($"[SnapshotManager] Stack trace: {e.StackTrace}");
                snapshot.Dispose();
                return false;
            }
        }
        
        /// <summary>
        /// Capture all entities and their component data
        /// </summary>
        private unsafe void CaptureEntities(ref SimulationSnapshot snapshot)
        {
            var entities = snapshotQuery.ToEntityArray(Allocator.Temp);
            var dataPtr = (byte*)snapshot.data.GetUnsafePtr();
            int currentOffset = 0;
            int entityIndex = 0;
            
            foreach (var entity in entities)
            {
                if (entityIndex >= snapshot.entities.Length)
                {
                    Debug.LogWarning("[SnapshotManager] Too many entities for snapshot buffer");
                    break;
                }
                
                int entityDataStart = currentOffset;
                ulong typeMask = 0;

                // Single loop for all types
                for (int i = 0; i < snapshotTypes.Count; i++)
                {
                    var typeInfo = snapshotTypes[i];
                    bool hasType = false;

                    if (typeInfo.isBuffer)
                    {
                        hasType = HasBuffer(entity, typeInfo.managedType);
                    }
                    else
                    {
                        hasType = entityManager.HasComponent(entity, typeInfo.componentType);
                    }

                    if (hasType)
                    {
                        typeMask |= (1UL << i);

                        if (handlers.TryGetValue(typeInfo.managedType, out var handler))
                        {
                            // Copy component or buffer data to snapshot
                            handler.CopyToSnapshot(entityManager, entity, (IntPtr)(dataPtr + currentOffset), out int bytesWritten);
                            currentOffset += bytesWritten;
                        }
                    }
                }
                
                // Store entity snapshot info
                snapshot.entities[entityIndex] = EntitySnapshot.Create(
                    entity, 
                    entityDataStart,
                    currentOffset - entityDataStart,
                    typeMask
                );
                
                entityIndex++;
            }
            
            snapshot.entityCount = entityIndex;
            snapshot.dataSize = currentOffset;
            
            entities.Dispose();
        }
        
        /// <summary>
        /// Restore snapshot using SimulationEntityManager
        /// </summary>
        public bool RestoreSnapshot(uint tick)
        {
            var startTime = Time.realtimeSinceStartupAsDouble;
            
            if (!snapshots.TryGetValue(tick, out var snapshot))
            {
                Debug.LogError($"[SnapshotManager] No snapshot found for tick {tick}");
                return false;
            }
            
            try
            {
                // Use SimulationEntityManager to destroy all managed entities
                if (simEntityManager != null)
                {
                    simEntityManager.DestroyAllManagedEntities();
                }
                else
                {
                    // Fallback
                    entityManager.DestroyEntity(snapshotQuery);
                }
                
                // Restore entities from snapshot
                RestoreEntities(ref snapshot);
                
                lastRestoreTime = (Time.realtimeSinceStartupAsDouble - startTime) * 1000.0;
                
                Debug.Log($"[SnapshotManager] Restored snapshot for tick {tick} in {lastRestoreTime:F2}ms");
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SnapshotManager] Failed to restore snapshot: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Restore all entities from snapshot data with entity remapping support
        /// </summary>
        private unsafe void RestoreEntities(ref SimulationSnapshot snapshot)
        {
            var dataPtr = (byte*)snapshot.data.GetUnsafeReadOnlyPtr();
            
            // Create entity remap table
            entityRemapTable = new NativeHashMap<Entity, Entity>(snapshot.entityCount * 2, Allocator.Temp);

            // First pass: Use SimulationEntityManager to create entities
            NativeArray<Entity> newEntities;
            if (simEntityManager != null)
            {
                // This ensures all entities are tracked from creation
                newEntities = simEntityManager.CreateEntitiesForRestore(snapshot.entityCount, Allocator.Temp);
            }
            else
            {
                // Fallback if somehow we don't have SimulationEntityManager
                Debug.LogError("[SnapshotManager] SimulationEntityManager not available! Entities will not be tracked!");
                newEntities = new NativeArray<Entity>(snapshot.entityCount, Allocator.Temp);
                entityManager.CreateEntity(entityManager.CreateArchetype(), newEntities);
            }
            
            // Build remap table
            for (int i = 0; i < snapshot.entityCount; i++)
            {
                var entitySnapshot = snapshot.entities[i];
                var oldEntity = entitySnapshot.ToEntity();
                var newEntity = newEntities[i];
                entityRemapTable.Add(oldEntity, newEntity);
            }

            // Second pass: Restore all data
            for (int i = 0; i < snapshot.entityCount; i++)
            {
                var entitySnapshot = snapshot.entities[i];
                var entity = newEntities[i];
                int dataOffset = entitySnapshot.dataOffset;

                // Track what type of entity this is for category update
                EntityCategory detectedCategory = EntityCategory.Unknown;
                string entityName = null;

                // Single loop for all types
                for (int typeIndex = 0; typeIndex < snapshotTypes.Count; typeIndex++)
                {
                    if ((entitySnapshot.typeMask & (1UL << typeIndex)) != 0)
                    {
                        var typeInfo = snapshotTypes[typeIndex];

                        // Add component/buffer if needed
                        if (typeInfo.isBuffer)
                        {
                            if (!HasBuffer(entity, typeInfo.managedType))
                            {
                                AddBuffer(entity, typeInfo.managedType);
                            }
                        }
                        else
                        {
                            if (!entityManager.HasComponent(entity, typeInfo.componentType))
                            {
                                entityManager.AddComponent(entity, typeInfo.componentType);
                            }
                        }

                        // Restore data
                        if (handlers.TryGetValue(typeInfo.managedType, out var handler))
                        {
                            handler.CopyFromSnapshot(entityManager, entity, (IntPtr)(dataPtr + dataOffset));

                            // Check for SimEntityTypeComponent to determine proper category
                            if (typeInfo.managedType == typeof(SimEntityTypeComponent))
                            {
                                var simEntityComp = entityManager.GetComponentData<SimEntityTypeComponent>(entity);

                                // Map SimEntityType to EntityCategory
                                detectedCategory = simEntityComp.simEntityType switch
                                {
                                    SimEntityType.Player => EntityCategory.Player,
                                    SimEntityType.Enemy => EntityCategory.Enemy,
                                    SimEntityType.Projectile => EntityCategory.Projectile,
                                    SimEntityType.Environment => EntityCategory.Environment,
                                    _ => EntityCategory.Unknown
                                };

                                entityName = $"{simEntityComp.simEntityType}_{entity.Index}";
                            }

                            // Advance by the actual size stored
                            if (typeInfo.isBuffer)
                            {
                                // For buffers, we need to calculate the actual size
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

                // Update entity category now that we know what type it is
                if (simEntityManager != null && detectedCategory != EntityCategory.Unknown)
                {
                    simEntityManager.UpdateEntityCategory(entity, detectedCategory, entityName);
                }
            }

            // Third pass: Remap entity references in buffers
            for (int i = 0; i < snapshot.entityCount; i++)
            {
                var entity = newEntities[i];

                for (int typeIndex = 0; typeIndex < snapshotTypes.Count; typeIndex++)
                {
                    var typeInfo = snapshotTypes[typeIndex];
                    if (typeInfo.requiresRemapping && (snapshot.entities[i].typeMask & (1UL << typeIndex)) != 0)
                    {
                        if (handlers.TryGetValue(typeInfo.managedType, out var handler) && handler.RequiresRemapping)
                        {
                            handler.RemapEntityReferences(entityManager, entity, entityRemapTable);
                        }
                    }
                }
            }
            
            // Cleanup
            entityRemapTable.Dispose();
            newEntities.Dispose();
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
        /// Calculate deterministic hash of snapshot data
        /// </summary>
        private unsafe ulong CalculateSnapshotHash(ref SimulationSnapshot snapshot)
        {
            using (var sha256 = SHA256.Create())
            {
                // Hash only the actual data, not the entire buffer
                var dataArray = new byte[snapshot.dataSize];
                fixed (byte* dst = dataArray)
                {
                    UnsafeUtility.MemCpy(dst, snapshot.data.GetUnsafeReadOnlyPtr(), snapshot.dataSize);
                }

                var hashBytes = sha256.ComputeHash(dataArray);

                // Convert first 8 bytes to ulong for quick comparison
                return BitConverter.ToUInt64(hashBytes, 0);
            }
        }
        
        /// <summary>
        /// Compare two snapshots for determinism testing
        /// </summary>
        public bool CompareSnapshots(uint tickA, uint tickB, out string differences)
        {
            differences = "";
            
            if (!snapshots.TryGetValue(tickA, out var snapshotA) || 
                !snapshots.TryGetValue(tickB, out var snapshotB))
            {
                differences = "One or both snapshots not found";
                return false;
            }
            
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
            if (snapshots.TryGetValue(tick, out var snapshot))
            {
                return new SnapshotInfo
                {
                    tick = snapshot.tick,
                    hash = snapshot.hash,
                    entityCount = snapshot.entityCount,
                    dataSize = snapshot.dataSize,
                    componentTypeCount = snapshotTypes.Count  // Just use the unified count
                };
            }
            
            return default;
        }
        
        /// <summary>
        /// Cleanup and dispose all snapshots
        /// </summary>
        public void Dispose()
        {
            foreach (var snapshot in snapshots.Values)
            {
                snapshot.Dispose();
            }
            snapshots.Clear();
            
            if (snapshotQuery != null)
            {
                snapshotQuery.Dispose();
            }
        }
        
        // Public properties
        public int SnapshotCount => snapshots.Count;
        public double LastCaptureTimeMs => lastCaptureTime;
        public double LastRestoreTimeMs => lastRestoreTime;
        public IReadOnlyList<SnapshotTypeInfo> RegisteredTypes => snapshotTypes.AsReadOnly();
    }
    
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
            bytesWritten = typeInfo.size;
            
            if (typeInfo.size == 0) return; // Tag component
            
            try
            {
                var component = em.GetComponentData<T>(entity);
                UnsafeUtility.CopyStructureToPtr(ref component, (void*)dest);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SnapshotHandler] Failed to copy component {typeof(T).Name}: {e.Message}");
            }
        }
        
        public unsafe void CopyFromSnapshot(EntityManager em, Entity entity, IntPtr src)
        {
            if (typeInfo.size == 0) return; // Tag component
            
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
        
        public void RemapEntityReferences(EntityManager em, Entity entity, NativeHashMap<Entity, Entity> remap)
        {
            // Components don't have entity references by default
        }
        
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