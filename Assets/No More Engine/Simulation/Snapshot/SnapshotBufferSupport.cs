using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System;
using NoMoreEngine.Simulation.Components;

namespace NoMoreEngine.Simulation.Snapshot
{
    /// <summary>
    /// Attribute to mark buffer components as snapshotable
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct)]
    public class BufferSnapshotableAttribute : Attribute
    {
        public int Priority { get; set; } = 0;
        public int MaxElements { get; set; } = 32;
        public bool RequiresEntityRemapping { get; set; } = false;
    }

    /// <summary>
    /// Interface for buffer snapshot handlers
    /// </summary>
    public interface IBufferSnapshotHandler
    {
        void CopyBufferToSnapshot(EntityManager entityManager, Entity entity, IntPtr destination, out int bytesWritten);
        void CopyBufferFromSnapshot(EntityManager entityManager, Entity entity, IntPtr source, int bufferSize);
        void RemapEntityReferences(EntityManager entityManager, Entity entity, NativeHashMap<Entity, Entity> remapTable);
        int GetMaxBufferSize();
        Type GetBufferElementType();
        bool RequiresEntityRemapping();
    }

    /// <summary>
    /// Generic buffer snapshot handler
    /// </summary>
    public class BufferSnapshotHandler<T> : IBufferSnapshotHandler where T : unmanaged, IBufferElementData
    {
        private readonly int maxElements;
        private readonly int elementSize;
        private readonly bool requiresRemapping;

        public BufferSnapshotHandler(int maxElements, bool requiresRemapping)
        {
            this.maxElements = maxElements;
            this.elementSize = UnsafeUtility.SizeOf<T>();
            this.requiresRemapping = requiresRemapping;
        }

        public unsafe void CopyBufferToSnapshot(EntityManager entityManager, Entity entity, IntPtr destination, out int bytesWritten)
        {
            if (!entityManager.HasBuffer<T>(entity))
            {
                bytesWritten = sizeof(int); // Just write count of 0
                *(int*)destination = 0;
                return;
            }

            var buffer = entityManager.GetBuffer<T>(entity, true); // Read-only access
            var ptr = (byte*)destination;

            // Write element count
            int elementCount = Math.Min(buffer.Length, maxElements);
            *(int*)ptr = elementCount;
            ptr += sizeof(int);

            // Write elements
            for (int i = 0; i < elementCount; i++)
            {
                var element = buffer[i];
                UnsafeUtility.CopyStructureToPtr(ref element, ptr);
                ptr += elementSize;
            }

            bytesWritten = sizeof(int) + (elementCount * elementSize);
        }

        public unsafe void CopyBufferFromSnapshot(EntityManager entityManager, Entity entity, IntPtr source, int bufferSize)
        {
            var ptr = (byte*)source;
            
            // Read element count
            int elementCount = *(int*)ptr;
            ptr += sizeof(int);

            if (elementCount == 0)
                return;

            // Ensure entity has buffer
            if (!entityManager.HasBuffer<T>(entity))
            {
                entityManager.AddBuffer<T>(entity);
            }

            var buffer = entityManager.GetBuffer<T>(entity);
            buffer.Clear();
            buffer.EnsureCapacity(elementCount);

            // Read elements
            for (int i = 0; i < elementCount; i++)
            {
                var element = default(T);
                UnsafeUtility.CopyPtrToStructure(ptr, out element);
                buffer.Add(element);
                ptr += elementSize;
            }
        }

        public virtual void RemapEntityReferences(EntityManager entityManager, Entity entity, NativeHashMap<Entity, Entity> remapTable)
        {
            // Base implementation does nothing - override in specialized handlers
        }

        public int GetMaxBufferSize()
        {
            return sizeof(int) + (maxElements * elementSize);
        }

        public Type GetBufferElementType() => typeof(T);
        public bool RequiresEntityRemapping() => requiresRemapping;
    }

    /// <summary>
    /// Specialized handler for CollisionContactBuffer that handles entity remapping
    /// </summary>
    public class CollisionContactBufferHandler : BufferSnapshotHandler<CollisionContactBuffer>
    {
        public CollisionContactBufferHandler() : base(32, true) { }

        public override void RemapEntityReferences(EntityManager entityManager, Entity entity, NativeHashMap<Entity, Entity> remapTable)
        {
            if (!entityManager.HasBuffer<CollisionContactBuffer>(entity))
                return;

            var buffer = entityManager.GetBuffer<CollisionContactBuffer>(entity);
            
            for (int i = 0; i < buffer.Length; i++)
            {
                var element = buffer[i];
                if (remapTable.TryGetValue(element.otherEntity, out Entity remapped))
                {
                    element.otherEntity = remapped;
                    buffer[i] = element;
                }
            }
        }
    }

    /// <summary>
    /// Buffer type info for snapshot system
    /// </summary>
    public struct SnapshotBufferType
    {
        public Type bufferElementType;
        public Type bufferType;
        public int typeIndex;
        public int maxElements;
        public int maxSize;
        public int priority;
        public bool requiresRemapping;

        public SnapshotBufferType(Type elementType, int typeIndex, BufferSnapshotableAttribute attr)
        {
            this.bufferElementType = elementType;
            this.bufferType = typeof(DynamicBuffer<>).MakeGenericType(elementType);
            this.typeIndex = typeIndex;
            this.maxElements = attr?.MaxElements ?? 32;
            this.priority = attr?.Priority ?? 0;
            this.requiresRemapping = attr?.RequiresEntityRemapping ?? false;
            
            try
            {
                var elementSize = UnsafeUtility.SizeOf(elementType);
                this.maxSize = sizeof(int) + (maxElements * elementSize);
            }
            catch
            {
                this.maxSize = sizeof(int); // Just count for zero-sized elements
            }
        }
    }
}