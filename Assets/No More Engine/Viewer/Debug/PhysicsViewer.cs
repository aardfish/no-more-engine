using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using NoMoreEngine.Simulation.Components;


namespace NoMoreEngine.Viewer.Debug
{
    /// <summary>
    /// Handles visualization of physics debug information
    /// Replaces the rendering logic from PhysicsDebugSystem
    /// </summary>
    public class PhysicsViewer
    {
        private EntityManager entityManager;
        private EntityQuery physicsEntitiesQuery;
        private EntityQuery globalGravityQuery;

        // Visualization settings
        private bool showGravityVectors = true;
        private bool showVelocityVectors = true;
        private bool showMassIndicators = true;
        private bool showGlobalGravity = true;
        private float vectorScale = 2.0f;

        public void Initialize(EntityManager entityManager)
        {
            this.entityManager = entityManager;

            // Query for entities with physics components
            physicsEntitiesQuery = entityManager.CreateEntityQuery(
                typeof(FixTransformComponent),
                typeof(PhysicsComponent)
            );

            // Query for global gravity
            globalGravityQuery = entityManager.CreateEntityQuery(
                typeof(GlobalGravityComponent)
            );
        }

        public void Update()
        {
            // Draw global gravity indicator
            if (showGlobalGravity)
            {
                DrawGlobalGravityIndicator();
            }

            // Draw per-entity physics visualization
            DrawPhysicsEntities();
        }

        private void DrawGlobalGravityIndicator()
        {
            if (globalGravityQuery.IsEmpty) return;

            var globalGravity = globalGravityQuery.GetSingleton<GlobalGravityComponent>();

            if (!globalGravity.enabled) return;

            // Draw global gravity at world origin
            Vector3 origin = Vector3.zero;
            Vector3 gravityVector = globalGravity.FinalGravity.ToUnityVec();

            // Scale the vector for visibility
            Vector3 gravityEnd = origin + gravityVector.normalized * 5f;

            // Draw main gravity vector
            Color gravityColor = globalGravity.enabled ? Color.cyan : Color.gray;
            DevTools.Debug.DrawLine(origin, gravityEnd, gravityColor, Time.deltaTime);

            // Draw gravity strength indicator (multiple lines for strength)
            float strength = gravityVector.magnitude;
            int strengthLines = Mathf.Clamp(Mathf.RoundToInt(strength), 1, 10);

            for (int i = 1; i <= strengthLines; i++)
            {
                Vector3 offset = Vector3.right * (i * 0.2f - strengthLines * 0.1f);
                Vector3 lineStart = origin + offset;
                Vector3 lineEnd = lineStart + gravityVector.normalized * (3f + i * 0.2f);

                Color strengthColor = Color.Lerp(Color.yellow, Color.red, (float)i / strengthLines);
                strengthColor.a = 0.7f;
                DevTools.Debug.DrawLine(lineStart, lineEnd, strengthColor, Time.deltaTime);
            }

            // Draw scale indicator text representation
            Vector3 textPos = origin + Vector3.up * 6f;
            DevTools.Debug.DrawLine(textPos, textPos + Vector3.right * 0.5f, gravityColor, Time.deltaTime);
        }

        private void DrawPhysicsEntities()
        {
            if (physicsEntitiesQuery.IsEmpty) return;

            var entities = physicsEntitiesQuery.ToEntityArray(Allocator.Temp);
            var transforms = physicsEntitiesQuery.ToComponentDataArray<FixTransformComponent>(Allocator.Temp);
            var physicsComponents = physicsEntitiesQuery.ToComponentDataArray<PhysicsComponent>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var transform = transforms[i];
                var physics = physicsComponents[i];

                Vector3 position = transform.position.ToUnityVec();

                // Draw gravity vector for this entity
                if (showGravityVectors && physics.affectedByGravity)
                {
                    DrawEntityGravityVector(position, physics);
                }

                // Draw velocity vector if entity has movement
                if (showVelocityVectors && entityManager.HasComponent<SimpleMovementComponent>(entity))
                {
                    var movement = entityManager.GetComponentData<SimpleMovementComponent>(entity);
                    DrawEntityVelocityVector(position, movement);
                }

                // Draw mass indicator
                if (showMassIndicators)
                {
                    DrawEntityMassIndicator(position, physics);
                }
            }

            entities.Dispose();
            transforms.Dispose();
            physicsComponents.Dispose();
        }

        private void DrawEntityGravityVector(Vector3 position, PhysicsComponent physics)
        {
            if (!globalGravityQuery.IsEmpty)
            {
                var globalGravity = globalGravityQuery.GetSingleton<GlobalGravityComponent>();
                var gravityAcceleration = physics.CalculateGravityAcceleration(globalGravity);

                if (fixMath.lengthsq(gravityAcceleration) > fix.Epsilon)
                {
                    Vector3 gravityVector = gravityAcceleration.ToUnityVec();
                    Vector3 gravityEnd = position + gravityVector.normalized * vectorScale;

                    // Color based on gravity source
                    Color gravityColor = physics.useGlobalGravity ? Color.green : Color.red;

                    // Adjust alpha based on strength
                    float strength = gravityVector.magnitude;
                    gravityColor.a = Mathf.Clamp(strength / 20f, 0.3f, 1f);

                    DevTools.Debug.DrawLine(position, gravityEnd, gravityColor, Time.deltaTime);

                    // Draw arrow head
                    Vector3 arrowTip = gravityEnd;
                    Vector3 arrowBase = arrowTip - gravityVector.normalized * 0.3f;
                    Vector3 arrowSide1 = arrowBase + Vector3.Cross(gravityVector.normalized, Vector3.forward).normalized * 0.15f;
                    Vector3 arrowSide2 = arrowBase + Vector3.Cross(gravityVector.normalized, Vector3.back).normalized * 0.15f;

                    DevTools.Debug.DrawLine(arrowTip, arrowSide1, gravityColor, Time.deltaTime);
                    DevTools.Debug.DrawLine(arrowTip, arrowSide2, gravityColor, Time.deltaTime);
                }
            }
        }

        private void DrawEntityVelocityVector(Vector3 position, SimpleMovementComponent movement)
        {
            if (!movement.isMoving) return;

            Vector3 velocity = movement.velocity.ToUnityVec();
            if (velocity.magnitude < 0.1f) return;

            Vector3 velocityEnd = position + velocity.normalized * vectorScale * 1.5f;

            // Color based on speed
            float speed = velocity.magnitude;
            Color velocityColor = Color.Lerp(Color.blue, Color.magenta, Mathf.Clamp01(speed / 50f));

            DevTools.Debug.DrawLine(position, velocityEnd, velocityColor, Time.deltaTime);

            // Draw speed indicator (thicker line for faster movement)
            if (speed > 5f)
            {
                Vector3 offset1 = Vector3.Cross(velocity.normalized, Vector3.up).normalized * 0.1f;
                Vector3 offset2 = -offset1;

                DevTools.Debug.DrawLine(position + offset1, velocityEnd + offset1, velocityColor, Time.deltaTime);
                DevTools.Debug.DrawLine(position + offset2, velocityEnd + offset2, velocityColor, Time.deltaTime);
            }
        }

        private void DrawEntityMassIndicator(Vector3 position, PhysicsComponent physics)
        {
            // Draw mass as a series of concentric circles
            float massRadius = Mathf.Clamp((float)physics.mass * 0.3f, 0.1f, 2f);
            int circleCount = Mathf.Clamp(Mathf.RoundToInt((float)physics.mass), 1, 5);

            Color massColor = Color.yellow;
            massColor.a = 0.4f;

            for (int i = 1; i <= circleCount; i++)
            {
                float radius = massRadius * ((float)i / circleCount);
                DrawDebugCircle(position, radius, massColor, 12);
            }

            // Draw mass value indicator (vertical line height represents mass)
            Vector3 massLineEnd = position + Vector3.up * massRadius * 2f;
            DevTools.Debug.DrawLine(position, massLineEnd, massColor, Time.deltaTime);
        }

        private void DrawDebugCircle(Vector3 center, float radius, Color color, int segments = 16)
        {
            float angleStep = 360f / segments;

            for (int i = 0; i < segments; i++)
            {
                float angle1 = i * angleStep * Mathf.Deg2Rad;
                float angle2 = (i + 1) * angleStep * Mathf.Deg2Rad;

                Vector3 point1 = center + new Vector3(Mathf.Cos(angle1), 0, Mathf.Sin(angle1)) * radius;
                Vector3 point2 = center + new Vector3(Mathf.Cos(angle2), 0, Mathf.Sin(angle2)) * radius;

                DevTools.Debug.DrawLine(point1, point2, color, Time.deltaTime);
            }
        }

        // Public configuration methods
        public void SetShowGravityVectors(bool show) => showGravityVectors = show;
        public void SetShowVelocityVectors(bool show) => showVelocityVectors = show;
        public void SetShowMassIndicators(bool show) => showMassIndicators = show;
        public void SetShowGlobalGravity(bool show) => showGlobalGravity = show;
        public void SetVectorScale(float scale) => vectorScale = Mathf.Clamp(scale, 0.5f, 10f);

        public bool IsShowingGravityVectors => showGravityVectors;
        public bool IsShowingVelocityVectors => showVelocityVectors;
        public bool IsShowingMassIndicators => showMassIndicators;
        public bool IsShowingGlobalGravity => showGlobalGravity;
        public float VectorScale => vectorScale;
    }
}