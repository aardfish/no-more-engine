using Unity.Entities;
using Unity.Burst;
using static fixMath;
using NoMoreEngine.Simulation.Components;

namespace NoMoreEngine.Simulation.Systems
{
    /// <summary>
    /// Gravity system that applies gravitational acceleration to physics-enabled entities
    /// Now uses deterministic time from SimulationTimeComponent
    /// </summary>
    [UpdateInGroup(typeof(PhysicsPhase))]
    [BurstCompile]
    public partial struct GravitySystem : ISystem
    {
        private EntityQuery globalGravityQuery;
        private EntityQuery physicsEntitiesQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // Query for global gravity singleton
            globalGravityQuery = SystemAPI.QueryBuilder()
                .WithAll<GlobalGravityComponent>()
                .Build();

            // Query for entities affected by gravity
            physicsEntitiesQuery = SystemAPI.QueryBuilder()
                .WithAll<SimpleMovementComponent, PhysicsComponent>()
                .Build();
                
            // Require time component
            state.RequireForUpdate<SimulationTimeComponent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Ensure global gravity exists
            if (globalGravityQuery.IsEmpty)
            {
                CreateDefaultGlobalGravity(ref state);
            }

            // Get global gravity settings
            var globalGravity = SystemAPI.GetSingleton<GlobalGravityComponent>();

            // Skip if gravity is globally disabled
            if (!globalGravity.enabled) return;

            // Early exit if no physics entities
            if (physicsEntitiesQuery.IsEmpty) return;

            // Get deterministic delta time from time system
            var time = SystemAPI.GetSingleton<SimulationTimeComponent>();
            fix deltaTime = time.deltaTime;

            // Apply gravity to all physics-enabled entities
            foreach (var (movement, physics) in
                SystemAPI.Query<RefRW<SimpleMovementComponent>, RefRO<PhysicsComponent>>())
            {
                // Skip if entity is not affected by gravity
                if (!physics.ValueRO.affectedByGravity) continue;

                // Calculate gravity acceleration for this entity
                fix3 gravityAcceleration = physics.ValueRO.CalculateGravityAcceleration(globalGravity);

                // Apply gravity to velocity: v = v + a * t
                movement.ValueRW.velocity += gravityAcceleration * deltaTime;

                // Apply terminal velocity limiting if specified
                movement.ValueRW.velocity = physics.ValueRO.ApplyTerminalVelocity(movement.ValueRW.velocity);

                // Ensure movement is enabled if velocity is non-zero
                if (lengthsq(movement.ValueRO.velocity) > fix.Epsilon)
                {
                    movement.ValueRW.isMoving = true;
                }
            }
        }

        /// <summary>
        /// Create default global gravity if none exists
        /// </summary>
        [BurstCompile]
        private void CreateDefaultGlobalGravity(ref SystemState state)
        {
            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(entity, GlobalGravityComponent.EarthGravity);
        }
    }

    /// <summary>
    /// Utility class for gravity manipulation - separated from the system
    /// These methods can be called from managed code (SystemBase) only
    /// </summary>
    public static class GravityUtility
    {
        /// <summary>
        /// Set global gravity settings
        /// </summary>
        public static void SetGlobalGravity(EntityManager entityManager, GlobalGravityComponent newGravity)
        {
            var query = entityManager.CreateEntityQuery(typeof(GlobalGravityComponent));

            if (query.IsEmpty)
            {
                var entity = entityManager.CreateEntity();
                entityManager.AddComponentData(entity, newGravity);
            }
            else
            {
                var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
                if (entities.Length > 0)
                {
                    entityManager.SetComponentData(entities[0], newGravity);
                }
                entities.Dispose();
            }

            query.Dispose();
        }

        /// <summary>
        /// Modify global gravity scale
        /// </summary>
        public static void SetGlobalGravityScale(EntityManager entityManager, fix newScale)
        {
            var query = entityManager.CreateEntityQuery(typeof(GlobalGravityComponent));

            if (!query.IsEmpty)
            {
                var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
                if (entities.Length > 0)
                {
                    var gravity = entityManager.GetComponentData<GlobalGravityComponent>(entities[0]);
                    gravity.gravityScale = newScale;
                    entityManager.SetComponentData(entities[0], gravity);
                }
                entities.Dispose();
            }

            query.Dispose();
        }

        /// <summary>
        /// Enable/disable global gravity
        /// </summary>
        public static void SetGlobalGravityEnabled(EntityManager entityManager, bool enabled)
        {
            var query = entityManager.CreateEntityQuery(typeof(GlobalGravityComponent));

            if (!query.IsEmpty)
            {
                var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
                if (entities.Length > 0)
                {
                    var gravity = entityManager.GetComponentData<GlobalGravityComponent>(entities[0]);
                    gravity.enabled = enabled;
                    entityManager.SetComponentData(entities[0], gravity);
                }
                entities.Dispose();
            }

            query.Dispose();
        }
    }
}