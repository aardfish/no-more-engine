using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using NoMoreEngine.Simulation.Components;
using Unity.Mathematics.FixedPoint;

namespace NoMoreEngine.Viewer.Game
{
    /// <summary>
    /// Handles visualization of SimEntity transforms and basic rendering
    /// Replaces the rendering logic from SimEntityRenderBridgeSystem
    /// </summary>
    public class SimEntityViewer
    {
        private EntityManager entityManager;
        private EntityQuery simEntityQuery;

        // Rendering settings
        private bool showForwardDirection = true;
        private bool showEntityTypes = true;
        private float forwardDirectionLength = 0.75f;

        public void Initialize(EntityManager entityManager)
        {
            this.entityManager = entityManager;

            // Create query for SimEntities
            simEntityQuery = entityManager.CreateEntityQuery(
                typeof(FixTransformComponent),
                typeof(SimEntityTypeComponent)
            );
        }

        public void Update(int maxEntitiesPerFrame)
        {
            if (simEntityQuery.IsEmpty) return;

            // Get all entities and their components
            var entities = simEntityQuery.ToEntityArray(Allocator.Temp);
            var transforms = simEntityQuery.ToComponentDataArray<FixTransformComponent>(Allocator.Temp);
            var entityTypes = simEntityQuery.ToComponentDataArray<SimEntityTypeComponent>(Allocator.Temp);

            // Limit rendering for performance
            int entityCount = Mathf.Min(entities.Length, maxEntitiesPerFrame);

            for (int i = 0; i < entityCount; i++)
            {
                RenderSimEntity(entities[i], transforms[i], entityTypes[i]);
            }

            // Cleanup
            entities.Dispose();
            transforms.Dispose();
            entityTypes.Dispose();
        }

        private void RenderSimEntity(Entity entity, FixTransformComponent transform, SimEntityTypeComponent entityType)
        {
            // Convert fixed-point to Unity types
            Vector3 position = transform.position.ToVector3();
            Quaternion rotation = transform.rotation.ToQuaternion();
            Vector3 scale = transform.scale.ToVector3();

            // Get debug color for this entity type
            Color entityColor = GetEntityTypeColor(entityType.simEntityType);

            // Draw different shapes based on SimEntity type
            switch (entityType.simEntityType)
            {
                case SimEntityType.Player:
                    DevTools.Debug.DrawCapsule(position, rotation, scale.y, scale.x * 0.5f, entityColor, true);
                    break;

                case SimEntityType.Enemy:
                    DevTools.Debug.DrawCapsule(position, rotation, scale.y, scale.x * 0.5f, entityColor, true);
                    break;

                case SimEntityType.Projectile:
                    DevTools.Debug.DrawSphere(position, rotation, scale.x * 0.5f, entityColor);
                    break;

                case SimEntityType.Environment:
                    DevTools.Debug.DrawBox(position, rotation, scale, entityColor);
                    break;

                default:
                    DevTools.Debug.DrawCube(position, rotation, scale.x, entityColor);
                    break;
            }

            // Draw forward direction indicator
            if (showForwardDirection)
            {
                Vector3 forward = rotation * Vector3.forward * forwardDirectionLength;
                DevTools.Debug.DrawLine(position, position + forward, entityColor);
            }

            // Draw entity ID for debugging
            if (showEntityTypes)
            {
                // Draw a small indicator above the entity
                Vector3 labelPos = position + Vector3.up * (scale.y + 0.3f);
                DevTools.Debug.DrawLine(position, labelPos, entityColor * 0.7f);
            }
        }

        private Color GetEntityTypeColor(SimEntityType entityType)
        {
            return entityType switch
            {
                SimEntityType.Player => new Color(0f, 1f, 1f, 0.8f),      // Cyan
                SimEntityType.Enemy => new Color(1f, 0.5f, 0f, 0.8f),     // Orange
                SimEntityType.Projectile => new Color(1f, 1f, 0f, 0.8f),  // Yellow
                SimEntityType.Environment => new Color(1f, 1f, 1f, 0.8f), // White
                _ => new Color(0.5f, 0.5f, 0.5f, 0.8f)                    // Gray
            };
        }

        // Public configuration methods
        public void SetShowForwardDirection(bool show) => showForwardDirection = show;
        public void SetShowEntityTypes(bool show) => showEntityTypes = show;
        public void SetForwardDirectionLength(float length) => forwardDirectionLength = length;
    }
}