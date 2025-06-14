using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using NoMoreEngine.Simulation.Components;

namespace NoMoreEngine.Viewer.Game
{
    /// <summary>
    /// Clean rendering system for SimEntities with proper Simulation/Viewer separation
    /// Only handles data transformation and caching - no direct rendering
    /// Runs in presentation phase for the Viewer layer to consume
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class SimEntityRenderSystem : SystemBase
    {
        // Public struct for cached render data (accessible from outside)
        public struct CachedEntityRenderData
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public SimEntityType entityType;
            public Entity entity;
        }

        private NativeList<CachedEntityRenderData> cachedRenderData;

        protected override void OnCreate()
        {
            // Initialize cache if needed
            cachedRenderData = new NativeList<CachedEntityRenderData>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            // Cleanup
            if (cachedRenderData.IsCreated)
            {
                cachedRenderData.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            // Clear previous frame's cache
            cachedRenderData.Clear();

            // Process entities and cache render data
            // This system now only prepares data - no rendering
            Entities.WithoutBurst().ForEach((Entity entity,
                in FixTransformComponent fixTransform,
                in SimEntityTypeComponent simEntityType) =>
            {
                // Convert fixed-point to Unity types using extensions
                var renderData = new CachedEntityRenderData
                {
                    position = fixTransform.position.ToUnityVec(),
                    rotation = fixTransform.rotation.ToUnityQuat(),
                    scale = fixTransform.scale.ToUnityVec(),
                    entityType = simEntityType.simEntityType,
                    entity = entity
                };

                cachedRenderData.Add(renderData);

            }).Run();

            // Note: ViewerManager will access this data through public API if needed
            // No more direct rendering happens here
        }

        /// <summary>
        /// Public API for Viewer to access cached render data
        /// </summary>
        public NativeArray<CachedEntityRenderData> GetCachedRenderData()
        {
            return cachedRenderData.AsArray();
        }
    }
}