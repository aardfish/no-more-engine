using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Dead simple generic snapshot - just copy bytes!
    /// </summary>
    public static class GenericSnapshot
    {
        /// <summary>
        /// Capture ANY component - it's just bytes!
        /// </summary>
        public static unsafe void Capture<T>(ref T component, byte* destination) where T : unmanaged
        {
            UnsafeUtility.CopyStructureToPtr(ref component, destination);
        }
        
        /// <summary>
        /// Restore ANY component - read the bytes back!
        /// </summary>
        public static unsafe T Restore<T>(byte* source) where T : unmanaged
        {
            T component = default;
            UnsafeUtility.CopyPtrToStructure(source, out component);
            return component;
        }
    }

    /// <summary>
    /// Super simple entity snapshot that just stores raw bytes
    /// </summary>
    public struct EntitySnapshot : IDisposable
    {
        public Entity entity;
        public NativeArray<byte> data;
        public NativeArray<ComponentType> componentTypes;
        public NativeArray<int> componentOffsets;
        
        public void Dispose()
        {
            if (data.IsCreated) data.Dispose();
            if (componentTypes.IsCreated) componentTypes.Dispose();
            if (componentOffsets.IsCreated) componentOffsets.Dispose();
        }
    }

    /// <summary>
    /// The simplest possible snapshot system
    /// </summary>
    public static class SimpleSnapshotSystem
    {
        // List of component types we want to snapshot
        private static List<ComponentType> snapshotComponents = new List<ComponentType>
        {
            ComponentType.ReadOnly<FixTransformComponent>(),
            ComponentType.ReadOnly<SimpleMovementComponent>(),
            ComponentType.ReadOnly<PhysicsComponent>(),
            ComponentType.ReadOnly<CollisionBoundsComponent>(),
            ComponentType.ReadOnly<CollisionResponseComponent>(),
            ComponentType.ReadOnly<HealthComponent>(),
            ComponentType.ReadOnly<PlayerStatsComponent>()
        };
        
        /// <summary>
        /// Register a component to be included in snapshots
        /// </summary>
        public static void Register<T>() where T : unmanaged, IComponentData
        {
            var componentType = ComponentType.ReadOnly<T>();
            if (!snapshotComponents.Contains(componentType))
            {
                snapshotComponents.Add(componentType);
            }
        }
        
        /// <summary>
        /// Capture an entity's state
        /// </summary>
        public static EntitySnapshot CaptureEntity(Entity entity, EntityManager entityManager)
        {
            var presentComponents = new List<ComponentType>();
            var offsets = new List<int>();
            int totalSize = 0;
            
            // Figure out which components this entity has and calculate total size
            foreach (var componentType in snapshotComponents)
            {
                if (entityManager.HasComponent(entity, componentType))
                {
                    presentComponents.Add(componentType);
                    offsets.Add(totalSize);
                    
                    var typeInfo = TypeManager.GetTypeInfo(componentType.TypeIndex);
                    totalSize += typeInfo.TypeSize;
                }
            }
            
            // Allocate storage
            var snapshot = new EntitySnapshot
            {
                entity = entity,
                data = new NativeArray<byte>(totalSize, Allocator.Persistent),
                componentTypes = new NativeArray<ComponentType>(presentComponents.Count, Allocator.Persistent),
                componentOffsets = new NativeArray<int>(offsets.Count, Allocator.Persistent)
            };
            
            // Copy component info
            for (int i = 0; i < presentComponents.Count; i++)
            {
                snapshot.componentTypes[i] = presentComponents[i];
                snapshot.componentOffsets[i] = offsets[i];
            }
            
            // Copy all component data
            unsafe
            {
                byte* dataPtr = (byte*)snapshot.data.GetUnsafePtr();
                
                for (int i = 0; i < presentComponents.Count; i++)
                {
                    var componentType = presentComponents[i];
                    var offset = offsets[i];
                    
                    // This is the magic - we can get raw component data!
                    var typeInfo = TypeManager.GetTypeInfo(componentType.TypeIndex);

                    // Use reflection to get the component data as object, then copy bytes
                    var componentTypeObj = TypeManager.GetType(componentType.TypeIndex);
                    var getComponentDataMethod = typeof(EntityManager).GetMethod("GetComponentData").MakeGenericMethod(componentTypeObj);
                    var componentData = getComponentDataMethod.Invoke(entityManager, new object[] { entity });

                    // Copy the struct to the snapshot using GenericSnapshot
                    var captureMethod = typeof(GenericSnapshot).GetMethod("Capture").MakeGenericMethod(componentTypeObj);
                    var parameters = new object[] { componentData, new IntPtr(dataPtr + offset) };
                    captureMethod.Invoke(null, parameters);
                }
            }
            
            return snapshot;
        }
        
        /// <summary>
        /// Restore an entity's state
        /// </summary>
        public static void RestoreEntity(EntitySnapshot snapshot, EntityManager entityManager)
        {
            if (!entityManager.Exists(snapshot.entity))
                return;
            
            unsafe
            {
                byte* dataPtr = (byte*)snapshot.data.GetUnsafePtr();
                
                for (int i = 0; i < snapshot.componentTypes.Length; i++)
                {
                    var componentType = snapshot.componentTypes[i];
                    var offset = snapshot.componentOffsets[i];
                    
                    if (entityManager.HasComponent(snapshot.entity, componentType))
                    {
                        // Use reflection to restore the component data
                        var componentTypeObj = TypeManager.GetType(componentType.TypeIndex);
                        var restoreMethod = typeof(GenericSnapshot).GetMethod("Restore").MakeGenericMethod(componentTypeObj);
                        var restoredComponent = restoreMethod.Invoke(null, new object[] { new IntPtr(dataPtr + offset) });

                        var setComponentDataMethod = typeof(EntityManager).GetMethod("SetComponentData").MakeGenericMethod(componentTypeObj);
                        setComponentDataMethod.Invoke(entityManager, new object[] { snapshot.entity, restoredComponent });
                    }
                }
            }
        }
    }

    /// <summary>
    /// Example of how simple it is to add new components
    /// </summary>
    public static class SnapshotRegistration
    {
        public static void RegisterGameplayComponents()
        {
            // Just register any component you want to snapshot!
            SimpleSnapshotSystem.Register<HealthComponent>();
            SimpleSnapshotSystem.Register<PlayerStatsComponent>();
            
            // Adding new components is trivial:
            // SimpleSnapshotSystem.Register<StaminaComponent>();
            // SimpleSnapshotSystem.Register<ManaComponent>();
            // SimpleSnapshotSystem.Register<InventoryComponent>();
            // SimpleSnapshotSystem.Register<WeaponComponent>();
            // etc...
        }
    }
}