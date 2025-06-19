using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using System.Reflection;

namespace NoMoreEngine.Simulation.Snapshot
{
    /// <summary>
    /// Base interface for all snapshotable types (components and buffers)
    /// </summary>
    public interface ISnapshotable
    {
        int GetSnapshotSize();
        bool ValidateSnapshot();
    }

    /// <summary>
    /// Interface for snapshotable components
    /// </summary>
    public interface ISnapshotableComponent<T> : ISnapshotable 
        where T : unmanaged, IComponentData, ISnapshotableComponent<T>
    {
    }

    /// <summary>
    /// Interface for snapshotable buffers
    /// </summary>
    public interface ISnapshotableBuffer<T> : ISnapshotable 
        where T : unmanaged, IBufferElementData, ISnapshotableBuffer<T>
    {
    }

    /// <summary>
    /// Unified attribute for marking both components and buffers as snapshotable
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct)]
    public class SnapshotableAttribute : Attribute
    {
        public bool IncludeInHash { get; set; } = true;
        public int Priority { get; set; } = 0;
        public int MaxElements { get; set; } = 32; // For buffers
        public bool RequiresEntityRemapping { get; set; } = false; // For buffers with entity refs
        
        public SnapshotableAttribute() { }
    }

    /// <summary>
    /// Complete simulation state snapshot
    /// </summary>
    public struct SimulationSnapshot : IDisposable
    {
        public uint tick;
        public ulong hash;
        public NativeArray<byte> data;
        public NativeArray<EntitySnapshot> entities;
        public int dataSize;
        public int entityCount;
        
        public SimulationSnapshot(uint tick, int maxDataSize, int maxEntities)
        {
            this.tick = tick;
            this.hash = 0;

            // Validate parameters to prevent massive allocations
            if (maxDataSize <= 0 || maxDataSize > 10 * 1024 * 1024) // 10MB max
            {
                Debug.LogError($"[SimulationSnapshot] Invalid maxDataSize: {maxDataSize}, using default");
                maxDataSize = 1024 * 1024; // 1MB default
            }

            if (maxEntities <= 0 || maxEntities > 100000) // 100k max
            {
                Debug.LogError($"[SimulationSnapshot] Invalid maxEntities: {maxEntities}, using default");
                maxEntities = 10000;
            }

            this.data = new NativeArray<byte>(maxDataSize, Allocator.Persistent);
            this.entities = new NativeArray<EntitySnapshot>(maxEntities, Allocator.Persistent);
            this.dataSize = 0;
            this.entityCount = 0;
        }
        
        public void Dispose()
        {
            if (data.IsCreated) data.Dispose();
            if (entities.IsCreated) entities.Dispose();
        }
    }

    /// <summary>
    /// Entity reference within a snapshot
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EntitySnapshot
    {
        public int entityIndex;
        public int entityVersion;
        public int dataOffset;      // Single offset for all data
        public int dataSize;        // Total size of all data
        public ulong typeMask;      // Single mask for all types (64 bits = up to 64 types)
        
        public Entity ToEntity() => new Entity { Index = entityIndex, Version = entityVersion };
        
        public static EntitySnapshot Create(Entity entity, int dataOffset, int dataSize, ulong typeMask)
        {
            return new EntitySnapshot
            {
                entityIndex = entity.Index,
                entityVersion = entity.Version,
                dataOffset = dataOffset,
                dataSize = dataSize,
                typeMask = typeMask
            };
        }
    }

    /// <summary>
    /// Unified type info for snapshot discovery
    /// </summary>
    public struct SnapshotTypeInfo
    {
        public Type managedType;
        public ComponentType componentType;
        public int typeIndex;
        public int size;
        public bool isBuffer;
        public bool includeInHash;
        public int priority;
        
        // Buffer-specific
        public int maxElements;
        public bool requiresRemapping;
        
        public static SnapshotTypeInfo CreateComponent(Type type, int index, SnapshotableAttribute attr)
        {
            return new SnapshotTypeInfo
            {
                managedType = type,
                componentType = ComponentType.ReadOnly(type),
                typeIndex = index,
                size = GetTypeSize(type),
                isBuffer = false,
                includeInHash = attr?.IncludeInHash ?? true,
                priority = attr?.Priority ?? 0,
                maxElements = 0,
                requiresRemapping = false
            };
        }
        
        public static SnapshotTypeInfo CreateBuffer(Type elementType, int index, SnapshotableAttribute attr)
        {
            var elementSize = GetTypeSize(elementType);
            return new SnapshotTypeInfo
            {
                managedType = elementType,
                componentType = ComponentType.ReadOnly(elementType),
                typeIndex = index,
                size = elementSize,
                isBuffer = true,
                includeInHash = attr?.IncludeInHash ?? true,
                priority = attr?.Priority ?? 0,
                maxElements = attr?.MaxElements ?? 32,
                requiresRemapping = attr?.RequiresEntityRemapping ?? false
            };
        }
        
        private static int GetTypeSize(Type type)
        {
            // Check if this is a tag component (has no fields)
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
            if (fields.Length == 0)
            {
                Debug.Log($"[SnapshotTypeInfo] Type {type.Name} is a tag component (no fields)");
                return 0; // Mark as zero-sized for our purposes
            }
            
            try
            {
                int size = UnsafeUtility.SizeOf(type);
                Debug.Log($"[SnapshotTypeInfo] Type {type.Name} has size: {size} with {fields.Length} fields");
                return size;
            }
            catch (Exception e)
            {
                Debug.Log($"[SnapshotTypeInfo] Type {type.Name} size check failed: {e.Message}");
                return 0;
            }
        }
    }

    /// <summary>
    /// Unmanaged version of SnapshotTypeInfo for use in jobs
    /// </summary>
    public struct UnmanagedSnapshotTypeInfo
    {
        public int typeIndex;
        public int size;
        public bool isBuffer;
        public bool includeInHash;
        public int priority;
        public int maxElements;
        public bool requiresRemapping;
        
        /// <summary>
        /// Create from managed SnapshotTypeInfo
        /// </summary>
        public static UnmanagedSnapshotTypeInfo FromManaged(SnapshotTypeInfo managed)
        {
            return new UnmanagedSnapshotTypeInfo
            {
                typeIndex = managed.typeIndex,
                size = managed.size,
                isBuffer = managed.isBuffer,
                includeInHash = managed.includeInHash,
                priority = managed.priority,
                maxElements = managed.maxElements,
                requiresRemapping = managed.requiresRemapping
            };
        }
    }

    /// <summary>
    /// Snapshot metadata for validation
    /// </summary>
    public struct SnapshotMetadata : IComponentData
    {
        public uint snapshotTick;
        public ulong snapshotHash;
        public int entityCount;
        public int totalDataSize;
        public double captureTimeMs;
        public double restoreTimeMs;
    }
}