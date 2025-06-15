using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System;
using System.Runtime.InteropServices;

namespace NoMoreEngine.Simulation.Snapshot
{
    /// <summary>
    /// Interface that components must implement to be included in snapshots
    /// Uses generic constraint to ensure it's only used on unmanaged components
    /// </summary>
    public interface ISnapshotable<T> where T : unmanaged, IComponentData, ISnapshotable<T>
    {
        /// <summary>
        /// Get the size of this component for serialization
        /// Most components can just return sizeof(T)
        /// </summary>
        int GetSnapshotSize();
        
        /// <summary>
        /// Validate that this component's data is valid for snapshotting
        /// Used to catch issues early in development
        /// </summary>
        bool ValidateSnapshot();
    }

    /// <summary>
    /// Attribute to mark components as snapshotable
    /// Provides metadata about how to handle the component
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct)]
    public class SnapshotableAttribute : Attribute
    {
        public bool IncludeInHash { get; set; } = true;
        public int Priority { get; set; } = 0; // Lower = earlier in snapshot
        
        public SnapshotableAttribute() { }
    }

    /// <summary>
    /// Represents a complete simulation state snapshot
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
    /// Lightweight entity reference for snapshots
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EntitySnapshot
    {
        public int entityIndex;
        public int entityVersion;
        public int dataOffset;      // Offset into snapshot data buffer
        public int dataSize;        // Size of this entity's data
        public uint componentMask;  // Bit mask of which components this entity has
        
        public EntitySnapshot(Entity entity, int dataOffset, int dataSize, uint componentMask)
        {
            this.entityIndex = entity.Index;
            this.entityVersion = entity.Version;
            this.dataOffset = dataOffset;
            this.dataSize = dataSize;
            this.componentMask = componentMask;
        }
        
        public Entity ToEntity() => new Entity { Index = entityIndex, Version = entityVersion };
    }

    /// <summary>
    /// Component type info for automatic discovery
    /// </summary>
    public struct SnapshotComponentType
    {
        public ComponentType componentType;
        public Type managedType;
        public int typeIndex;
        public int size;
        public bool includeInHash;
        public int priority;
        
        public SnapshotComponentType(Type type, int typeIndex, SnapshotableAttribute attr)
        {
            this.componentType = ComponentType.ReadOnly(type);
            this.managedType = type;
            this.typeIndex = typeIndex;
            
            // Try to get size, but handle zero-sized components
            try
            {
                this.size = UnsafeUtility.SizeOf(type);
            }
            catch
            {
                this.size = 0; // Zero-sized component (tag)
            }
            
            this.includeInHash = attr?.IncludeInHash ?? true;
            this.priority = attr?.Priority ?? 0;
        }
    }

    /// <summary>
    /// Snapshot metadata for validation and debugging
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