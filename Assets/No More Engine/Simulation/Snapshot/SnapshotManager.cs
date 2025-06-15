using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Security.Cryptography;
using NoMoreEngine.Simulation.Components;

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
        
        // Discovered snapshot types
        private List<SnapshotComponentType> snapshotTypes;
        private Dictionary<ComponentType, int> typeToIndex;
        private Dictionary<Type, IComponentSnapshotHandler> snapshotHandlers;
        private EntityQuery snapshotQuery;
        private List<SnapshotBufferType> snapshotBufferTypes;
        private Dictionary<Type, int> bufferTypeToIndex;
        private Dictionary<Type, IBufferSnapshotHandler> bufferHandlers;
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
        
        public SnapshotManager(World world, int maxSnapshots = DEFAULT_MAX_SNAPSHOTS)
        {
            this.world = world;
            this.entityManager = world.EntityManager;
            this.maxSnapshots = maxSnapshots;
            this.snapshots = new Dictionary<uint, SimulationSnapshot>(maxSnapshots);
            this.snapshotHandlers = new Dictionary<Type, IComponentSnapshotHandler>();
            this.snapshotBufferTypes = new List<SnapshotBufferType>();
            this.bufferTypeToIndex = new Dictionary<Type, int>();
            this.bufferHandlers = new Dictionary<Type, IBufferSnapshotHandler>();
            
            // Discover all snapshotable component types
            DiscoverSnapshotableTypes();

            // Discover snapshotable buffer types
            DiscoverSnapshotableBufferTypes();
            
            // Create entity query for all snapshotable entities
            CreateSnapshotQuery();
            
            Debug.Log($"[SnapshotManager] Initialized with {snapshotTypes.Count} snapshotable component types");
        }
        
        /// <summary>
        /// Automatically discover all components that implement ISnapshotable
        /// </summary>
        private void DiscoverSnapshotableTypes()
        {
            snapshotTypes = new List<SnapshotComponentType>();
            typeToIndex = new Dictionary<ComponentType, int>();
            
            // Get all loaded assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            
            // Find all types that have SnapshotableAttribute
            var snapshotableTypes = new List<Type>();
            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                        .Where(t => t.IsValueType && 
                               !t.IsGenericType &&
                               typeof(IComponentData).IsAssignableFrom(t) &&
                               t.GetCustomAttribute<SnapshotableAttribute>() != null)
                        .ToList();
                    
                    snapshotableTypes.AddRange(types);
                }
                catch (ReflectionTypeLoadException)
                {
                    // Some assemblies might not be fully loaded, skip them
                }
            }
            
            // Sort by priority and create snapshot type info
            var sortedTypes = snapshotableTypes
                .Select(t => new { Type = t, Attr = t.GetCustomAttribute<SnapshotableAttribute>() })
                .OrderBy(x => x.Attr.Priority)
                .ToList();
            
            for (int i = 0; i < sortedTypes.Count; i++)
            {
                var type = sortedTypes[i].Type;
                var attr = sortedTypes[i].Attr;
                
                // Get component size safely
                int componentSize = 0;
                try
                {
                    componentSize = UnsafeUtility.SizeOf(type);
                }
                catch
                {
                    // Zero-sized component (tag), size remains 0
                }
                
                var snapshotType = new SnapshotComponentType(type, i, attr);
                snapshotType.size = componentSize; // Override with actual size
                snapshotTypes.Add(snapshotType);
                typeToIndex[snapshotType.componentType] = i;
                
                // Create handler for this component type
                try
                {
                    var handlerType = typeof(ComponentSnapshotHandler<>).MakeGenericType(type);
                    var handler = Activator.CreateInstance(handlerType) as IComponentSnapshotHandler;
                    if (handler != null)
                    {
                        snapshotHandlers[type] = handler;
                    }
                    else
                    {
                        Debug.LogWarning($"[SnapshotManager] Failed to create handler for type: {type.Name}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SnapshotManager] Error creating handler for type {type.Name}: {e.Message}");
                    // Create a null handler that does nothing
                    snapshotHandlers[type] = new NullComponentSnapshotHandler();
                }
                
                Debug.Log($"[SnapshotManager] Registered snapshotable type: {type.Name} " +
                         $"(size: {snapshotType.size}, priority: {attr.Priority})");
            }
        }
        
        /// <summary>
        /// Check if entity has a specific buffer type (using reflection for now)
        /// </summary>
        private bool HasBuffer(Entity entity, Type bufferElementType)
        {
            // This is not ideal but works for now
            var hasBufferMethod = typeof(EntityManager).GetMethod("HasBuffer");
            var genericMethod = hasBufferMethod.MakeGenericMethod(bufferElementType);
            return (bool)genericMethod.Invoke(entityManager, new object[] { entity });
        }
        
        /// <summary>
        /// Discover all buffer components marked with BufferSnapshotableAttribute
        /// </summary>
        private void DiscoverSnapshotableBufferTypes()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var bufferTypes = new List<Type>();

            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                        .Where(t => t.IsValueType &&
                                    !t.IsGenericType &&
                                    typeof(IBufferElementData).IsAssignableFrom(t) &&
                                    t.GetCustomAttribute<BufferSnapshotableAttribute>() != null)
                        .ToList();

                    bufferTypes.AddRange(types);
                }
                catch (ReflectionTypeLoadException)
                {
                    // Some assemblies might not be fully loaded, skip them
                }
            }

            // Sort by priority and create snapshot buffer type info
            var sortedTypes = bufferTypes
                .Select(t => new { Type = t, Attr = t.GetCustomAttribute<BufferSnapshotableAttribute>() })
                .OrderBy(x => x.Attr.Priority)
                .ToList();

            for (int i = 0; i < sortedTypes.Count; i++)
            {
                var type = sortedTypes[i].Type;
                var attr = sortedTypes[i].Attr;

                var bufferType = new SnapshotBufferType(type, i, attr);
                snapshotBufferTypes.Add(bufferType);
                bufferTypeToIndex[type] = i;

                // Create appropriate handler
                IBufferSnapshotHandler handler;

                // Use specialized handlers for specific types
                if (type == typeof(CollisionContactBuffer))
                {
                    handler = new CollisionContactBufferHandler();
                }
                else
                {
                    // Generic handler
                    var handlerType = typeof(BufferSnapshotHandler<>).MakeGenericType(type);
                    handler = Activator.CreateInstance(handlerType, attr.MaxElements, attr.RequiresEntityRemapping) as IBufferSnapshotHandler;
                }

                if (handler != null)
                {
                    bufferHandlers[type] = handler;
                    Debug.Log($"[SnapshotManager] Registered buffer type: {type.Name} " +
                         $"(maxElements: {attr.MaxElements}, priority: {attr.Priority}, " +
                         $"remapping: {attr.RequiresEntityRemapping})");
                }

            }
        }

        /// <summary>
        /// Create entity query that includes all snapshotable components
        /// </summary>
        private void CreateSnapshotQuery()
        {
            if (snapshotTypes.Count == 0)
            {
                Debug.LogWarning("[SnapshotManager] No snapshotable types found!");
                return;
            }

            // Build query that includes ANY of the snapshotable components
            var queryDesc = new EntityQueryDesc
            {
                Any = snapshotTypes.Select(t => t.componentType).ToArray(),
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
            
            // Removed TypeManager.IsInitialized check because it is not accessible.
            // If you need to ensure initialization, consider another approach or handle exceptions gracefully.
            
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
                         $"({snapshot.entityCount} entities, {snapshot.dataSize} bytes, " +
                         $"hash: {snapshot.hash:X16}) in {lastCaptureTime:F2}ms");
                
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
            // Add debug logging at start
            var playerQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerControlledTag>(),
                ComponentType.ReadOnly<PlayerControlComponent>()
            );
            Debug.Log($"[SnapshotManager] Found {playerQuery.CalculateEntityCount()} player entities during capture");
            playerQuery.Dispose();

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
                uint componentMask = 0;
                
                // Capture each component type
                for (int i = 0; i < snapshotTypes.Count; i++)
                {
                    var snapshotType = snapshotTypes[i];
                    
                    if (entityManager.HasComponent(entity, snapshotType.componentType))
                    {
                        // Mark component as present in mask
                        componentMask |= (1u << i);
                        
                        // Skip data copying for zero-sized components
                        if (snapshotType.size == 0)
                            continue;
                        
                        // Get handler for this component type
                        if (!snapshotHandlers.TryGetValue(snapshotType.managedType, out var handler))
                        {
                            Debug.LogWarning($"[SnapshotManager] No handler found for type {snapshotType.managedType.Name}");
                            continue;
                        }
                        
                        // Only copy data and advance offset for non-zero sized components
                        handler.CopyToSnapshot(entityManager, entity, (IntPtr)(dataPtr + currentOffset));
                        currentOffset += snapshotType.size;
                    }
                }

                int bufferDataStart = currentOffset;
                uint bufferMask = 0;

                for (int i = 0; i < snapshotBufferTypes.Count; i++)
                {
                    var bufferType = snapshotBufferTypes[i];

                    // Check if entity has this buffer type
                    if (HasBuffer(entity, bufferType.bufferElementType))
                    {
                        bufferMask |= (1u << i);

                        if (bufferHandlers.TryGetValue(bufferType.bufferElementType, out var handler))
                        {
                            handler.CopyBufferToSnapshot(entityManager, entity, (IntPtr)(dataPtr + currentOffset), out int bytesWritten);
                            currentOffset += bytesWritten;
                        }
                    }
                }
                
                // Store entity snapshot info
                snapshot.entities[entityIndex] = new EntitySnapshot(
                    entity, 
                    entityDataStart, 
                    currentOffset - entityDataStart, 
                    componentMask,
                    bufferDataStart,
                    currentOffset - bufferDataStart,
                    bufferMask
                );
                
                entityIndex++;
            }
            
            snapshot.entityCount = entityIndex;
            snapshot.dataSize = currentOffset;
            
            entities.Dispose();
        }
        
        /// <summary>
        /// Restore simulation state from a snapshot
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
                // First, destroy all existing entities
                entityManager.DestroyEntity(snapshotQuery);
                
                // Restore entities from snapshot
                RestoreEntities(ref snapshot);
                
                lastRestoreTime = (Time.realtimeSinceStartupAsDouble - startTime) * 1000.0;
                
                Debug.Log($"[SnapshotManager] Restored snapshot for tick {tick} " +
                         $"({snapshot.entityCount} entities) in {lastRestoreTime:F2}ms");
                
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
            // Debug logging for initial state
            var beforePlayerQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerControlledTag>(),
                ComponentType.ReadOnly<PlayerControlComponent>()
            );
            Debug.Log($"[SnapshotManager] Player entities before restore: {beforePlayerQuery.CalculateEntityCount()}");
            beforePlayerQuery.Dispose();

            var dataPtr = (byte*)snapshot.data.GetUnsafeReadOnlyPtr();
            
            // Create entity remap table
            entityRemapTable = new NativeHashMap<Entity, Entity>(snapshot.entityCount * 2, Allocator.Temp);
            
            // First pass: Create all entities and build remap table
            var newEntities = new NativeArray<Entity>(snapshot.entityCount, Allocator.Temp);
            entityManager.CreateEntity(entityManager.CreateArchetype(), newEntities);
            
            // Build remap table
            for (int i = 0; i < snapshot.entityCount; i++)
            {
                var entitySnapshot = snapshot.entities[i];
                var oldEntity = entitySnapshot.ToEntity();
                var newEntity = newEntities[i];
                entityRemapTable.Add(oldEntity, newEntity);
            }
            
            // Second pass: Restore components and buffers
            for (int i = 0; i < snapshot.entityCount; i++)
            {
                var entitySnapshot = snapshot.entities[i];
                var entity = newEntities[i];
                int dataOffset = entitySnapshot.dataOffset;
                int bufferOffset = entitySnapshot.bufferDataOffset;
                
                // Restore components
                for (int typeIndex = 0; typeIndex < snapshotTypes.Count; typeIndex++)
                {
                    if ((entitySnapshot.componentMask & (1u << typeIndex)) != 0)
                    {
                        var snapshotType = snapshotTypes[typeIndex];

                        // Add component regardless of size
                        if (!entityManager.HasComponent(entity, snapshotType.componentType))
                        {
                            entityManager.AddComponent(entity, snapshotType.componentType);
                        }

                        // Skip data restoration for zero-sized components
                        if (snapshotType.size == 0)
                            continue;

                        // Get handler for this component type
                        if (!snapshotHandlers.TryGetValue(snapshotType.managedType, out var handler))
                        {
                            Debug.LogWarning($"[SnapshotManager] No handler found for type {snapshotType.managedType.Name}");
                            dataOffset += snapshotType.size;
                            continue;
                        }

                        // Copy component data
                        handler.CopyFromSnapshot(entityManager, entity, (IntPtr)(dataPtr + dataOffset));
                        dataOffset += snapshotType.size;
                    }
                }
                
                // Restore buffers
                for (int typeIndex = 0; typeIndex < snapshotBufferTypes.Count; typeIndex++)
                {
                    if ((entitySnapshot.bufferMask & (1u << typeIndex)) != 0)
                    {
                        var bufferType = snapshotBufferTypes[typeIndex];
                        
                        // Get buffer handler
                        if (bufferHandlers.TryGetValue(bufferType.bufferElementType, out var handler))
                        {
                            // Add buffer component if needed
                            if (!HasBuffer(entity, bufferType.bufferElementType))
                            {
                                var addBufferMethod = typeof(EntityManager).GetMethod("AddBuffer");
                                var genericMethod = addBufferMethod.MakeGenericMethod(bufferType.bufferElementType);
                                genericMethod.Invoke(entityManager, new object[] { entity });
                            }

                            // Restore buffer data
                            // We need to calculate bytes read based on the buffer type's max size
                            handler.CopyBufferFromSnapshot(entityManager, entity, (IntPtr)(dataPtr + bufferOffset), bufferType.maxSize);
                            bufferOffset += bufferType.maxSize; // Move by max size (actual data read is handled internally)
                        }
                    }
                }
            }
            
            // Third pass: Remap entity references in buffers
            for (int i = 0; i < snapshot.entityCount; i++)
            {
                var entity = newEntities[i];
                
                foreach (var bufferType in snapshotBufferTypes)
                {
                    if (bufferType.requiresRemapping)
                    {
                        if (bufferHandlers.TryGetValue(bufferType.bufferElementType, out var handler))
                        {
                            handler.RemapEntityReferences(entityManager, entity, entityRemapTable);
                        }
                    }
                }
            }
            
            // Cleanup
            entityRemapTable.Dispose();
            newEntities.Dispose();

            // Debug logging for final state
            var afterPlayerQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerControlledTag>(),
                ComponentType.ReadOnly<PlayerControlComponent>()
            );
            Debug.Log($"[SnapshotManager] Player entities after restore: {afterPlayerQuery.CalculateEntityCount()}");
            afterPlayerQuery.Dispose();
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
                    componentTypeCount = snapshotTypes.Count
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
        public IReadOnlyList<SnapshotComponentType> RegisteredTypes => snapshotTypes.AsReadOnly();
    }
    
    /// <summary>
    /// Interface for component-specific snapshot handlers
    /// </summary>
    public interface IComponentSnapshotHandler
    {
        void CopyToSnapshot(EntityManager entityManager, Entity entity, IntPtr destination);
        void CopyFromSnapshot(EntityManager entityManager, Entity entity, IntPtr source);
    }
    
    /// <summary>
    /// Generic component snapshot handler
    /// </summary>
    public class ComponentSnapshotHandler<T> : IComponentSnapshotHandler where T : unmanaged, IComponentData
    {
        private readonly int componentSize;
        
        public ComponentSnapshotHandler()
        {
            // Safely determine component size
            try
            {
                componentSize = UnsafeUtility.SizeOf<T>();
            }
            catch
            {
                // Zero-sized component (tag)
                componentSize = 0;
            }
        }
        
        public unsafe void CopyToSnapshot(EntityManager entityManager, Entity entity, IntPtr destination)
        {
            // Skip data copying for zero-sized components (tags)
            if (componentSize == 0)
                return;
                
            try
            {
                var component = entityManager.GetComponentData<T>(entity);
                UnsafeUtility.CopyStructureToPtr(ref component, (void*)destination);
            }
            catch (Exception e)
            {
                if (!typeof(T).Name.EndsWith("Tag"))
                {
                    // Only log error for non-tag components
                    Debug.LogError($"[SnapshotHandler] Failed to copy component {typeof(T).Name}: {e.Message}");
                }
            }
        }
        
        public unsafe void CopyFromSnapshot(EntityManager entityManager, Entity entity, IntPtr source)
        {
            // Skip data copying for zero-sized components (tags)
            if (componentSize == 0)
                return;
                
            try
            {
                var component = default(T);
                UnsafeUtility.CopyPtrToStructure((void*)source, out component);
                entityManager.SetComponentData(entity, component);
            }
            catch (Exception e)
            {
                if (!typeof(T).Name.EndsWith("Tag"))
                {
                    // Only log error for non-tag components
                    Debug.LogError($"[SnapshotHandler] Failed to restore component {typeof(T).Name}: {e.Message}");
                }
            }
        }
    }
    
    /// <summary>
    /// Null handler for components that fail to create proper handlers
    /// </summary>
    public class NullComponentSnapshotHandler : IComponentSnapshotHandler
    {
        public void CopyToSnapshot(EntityManager entityManager, Entity entity, IntPtr destination)
        {
            // Do nothing - this is a fallback for failed handler creation
        }
        
        public void CopyFromSnapshot(EntityManager entityManager, Entity entity, IntPtr source)
        {
            // Do nothing - this is a fallback for failed handler creation
        }
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