using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using NoMoreEngine.Simulation.Components;
using Unity.Mathematics.FixedPoint;

namespace NoMoreEngine.Viewer.Debug
{
    /// <summary>
    /// Handles visualization of collision bounds and collision events
    /// Replaces the rendering logic from CollisionDebugSystem
    /// </summary>
    public class CollisionViewer
    {
        private EntityManager entityManager;
        private EntityQuery collisionEntitiesQuery;
        private EntityQuery collisionEventsQuery;

        // Visualization settings
        private bool showCollisionBounds = true;
        private bool showCollisionEvents = true;
        private bool showCollisionLayers = true;
        private float debugDisplayTime = 0.1f;

        public void Initialize(EntityManager entityManager)
        {
            this.entityManager = entityManager;

            // Query for entities with collision bounds
            collisionEntitiesQuery = entityManager.CreateEntityQuery(
                typeof(FixTransformComponent),
                typeof(CollisionBoundsComponent)
            );

            // Query for entities with collision events
            collisionEventsQuery = entityManager.CreateEntityQuery(
                typeof(CollisionEventBuffer)
            );
        }

        public void Update()
        {
            if (showCollisionBounds)
            {
                DrawCollisionBounds();
            }

            if (showCollisionEvents)
            {
                DrawCollisionEvents();
            }
        }

        private void DrawCollisionBounds()
        {
            if (collisionEntitiesQuery.IsEmpty) return;

            var entities = collisionEntitiesQuery.ToEntityArray(Allocator.Temp);
            var transforms = collisionEntitiesQuery.ToComponentDataArray<FixTransformComponent>(Allocator.Temp);
            var bounds = collisionEntitiesQuery.ToComponentDataArray<CollisionBoundsComponent>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var transform = transforms[i];
                var bound = bounds[i];

                // Convert to Unity types
                Vector3 position = transform.position.ToVector3();
                Vector3 boundsCenter = position + bound.offset.ToVector3();
                Vector3 boundsSize = bound.size.ToVector3();
                Quaternion rotation = transform.rotation.ToQuaternion();

                // Get color based on entity type or collision properties
                Color boundsColor = GetCollisionBoundsColor(entity);

                // Check if entity is static
                bool isStatic = entityManager.HasComponent<StaticColliderComponent>(entity);

                if (isStatic)
                {
                    // Static colliders: solid, less transparent
                    boundsColor.a = 0.3f;
                    DevTools.Debug.DrawBox(boundsCenter, rotation, boundsSize, boundsColor);
                }
                else
                {
                    // Dynamic colliders: wireframe, more visible
                    boundsColor.a = 0.8f;
                    DevTools.Debug.DrawBox(boundsCenter, rotation, boundsSize, boundsColor);
                }

                // Draw collision layer indicator if enabled
                if (showCollisionLayers && entityManager.HasComponent<CollisionResponseComponent>(entity))
                {
                    var response = entityManager.GetComponentData<CollisionResponseComponent>(entity);
                    Color layerColor = GetCollisionLayerColor(response.entityLayer);
                    DevTools.Debug.DrawSphere(boundsCenter, rotation, 0.1f, layerColor);
                }
            }

            entities.Dispose();
            transforms.Dispose();
            bounds.Dispose();
        }

        private void DrawCollisionEvents()
        {
            if (collisionEventsQuery.IsEmpty) return;

            var entities = collisionEventsQuery.ToEntityArray(Allocator.Temp);

            foreach (var entity in entities)
            {
                if (!entityManager.HasComponent<FixTransformComponent>(entity)) continue;

                var transform = entityManager.GetComponentData<FixTransformComponent>(entity);
                var collisionEvents = entityManager.GetBuffer<CollisionEventBuffer>(entity);

                for (int i = 0; i < collisionEvents.Length; i++)
                {
                    var collision = collisionEvents[i].collisionEvent;

                    // Convert to Unity types
                    Vector3 contactPoint = collision.contactPoint.ToVector3();
                    Vector3 contactNormal = collision.contactNormal.ToVector3();

                    // Draw contact point as a small sphere
                    Color eventColor = GetCollisionEventColor(collision.layerA, collision.layerB);
                    DevTools.Debug.DrawSphere(contactPoint, Quaternion.identity, 0.05f, eventColor);

                    // Draw contact normal as a line
                    Vector3 normalEnd = contactPoint + contactNormal * 0.5f;
                    DevTools.Debug.DrawLine(contactPoint, normalEnd, eventColor, debugDisplayTime);

                    // Draw penetration depth indicator
                    float penetration = (float)collision.penetrationDepth;
                    Vector3 penetrationEnd = contactPoint - contactNormal * penetration;
                    DevTools.Debug.DrawLine(contactPoint, penetrationEnd, Color.red, debugDisplayTime);

                    // Draw collision info indicator
                    DevTools.Debug.DrawLine(contactPoint, contactPoint + Vector3.up * 0.2f, eventColor, debugDisplayTime);
                }
            }

            entities.Dispose();
        }

        private Color GetCollisionBoundsColor(Entity entity)
        {
            // Try to get entity type for color coding
            if (entityManager.HasComponent<SimEntityTypeComponent>(entity))
            {
                var entityType = entityManager.GetComponentData<SimEntityTypeComponent>(entity);
                return entityType.simEntityType switch
                {
                    SimEntityType.Player => new Color(0f, 1f, 1f, 0.8f),      // Cyan
                    SimEntityType.Enemy => new Color(1f, 0.5f, 0f, 0.8f),     // Orange
                    SimEntityType.Projectile => new Color(1f, 1f, 0f, 0.8f),  // Yellow
                    SimEntityType.Environment => new Color(1f, 1f, 1f, 0.8f), // White
                    _ => new Color(0.5f, 0.5f, 0.5f, 0.8f)                    // Gray
                };
            }

            return new Color(0.7f, 0.7f, 0.7f, 0.8f); // Default gray
        }

        private Color GetCollisionLayerColor(CollisionLayer layer)
        {
            return layer switch
            {
                CollisionLayer.Player => Color.cyan,
                CollisionLayer.Enemy => Color.red,
                CollisionLayer.Projectile => Color.yellow,
                CollisionLayer.Environment => Color.white,
                CollisionLayer.Pickup => Color.green,
                CollisionLayer.Trigger => Color.magenta,
                _ => Color.gray
            };
        }

        private Color GetCollisionEventColor(CollisionLayer layerA, CollisionLayer layerB)
        {
            // Create a mixed color based on the two colliding layers
            Color colorA = GetCollisionLayerColor(layerA);
            Color colorB = GetCollisionLayerColor(layerB);
            return Color.Lerp(colorA, colorB, 0.5f);
        }

        // Public configuration methods
        public void SetShowCollisionBounds(bool show) => showCollisionBounds = show;
        public void SetShowCollisionEvents(bool show) => showCollisionEvents = show;
        public void SetShowCollisionLayers(bool show) => showCollisionLayers = show;
        public void SetDebugDisplayTime(float time) => debugDisplayTime = time;

        public bool IsShowingCollisionBounds => showCollisionBounds;
        public bool IsShowingCollisionEvents => showCollisionEvents;
        public bool IsShowingCollisionLayers => showCollisionLayers;
    }
}