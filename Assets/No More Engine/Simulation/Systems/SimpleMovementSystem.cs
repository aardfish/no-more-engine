using Unity.Entities;
using Unity.Burst;
using UnityEngine;
using NoMoreEngine.Simulation.Components;


namespace NoMoreEngine.Simulation.Systems
{
    /// <summary>
    /// Simple movement system that updates positions based on velocity
    /// Now uses the deterministic time system instead of hardcoded values
    /// </summary>
    [UpdateInGroup(typeof(PhysicsPhase))]
    public partial struct SimpleMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // Require time component to exist
            state.RequireForUpdate<SimulationTimeComponent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Get deterministic delta time from time system
            var time = SystemAPI.GetSingleton<SimulationTimeComponent>();
            fix deltaTime = time.deltaTime;

            // Update positions for all entities with movement
            foreach (var (transform, movement) in SystemAPI.Query<RefRW<FixTransformComponent>, 
                RefRO<SimpleMovementComponent>>())
            {
                // Only move if movement is enabled
                if (!movement.ValueRO.isMoving) continue;

                // Simple position integration: position += velocity * deltaTime
                fix3 currentPosition = transform.ValueRO.position;
                fix3 velocity = movement.ValueRO.velocity;

                transform.ValueRW.position = currentPosition + velocity * deltaTime;
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            // Cleanup when system is destroyed
        }
    }
}