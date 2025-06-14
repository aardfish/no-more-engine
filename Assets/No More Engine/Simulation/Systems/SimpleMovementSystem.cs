using Unity.Entities;
using Unity.Burst;
using UnityEngine;
using NoMoreEngine.Simulation.Components;


namespace NoMoreEngine.Simulation.Systems
{
    /// <summary>
    /// Simple movement system that updates positions based on velocity
    /// Runs before the transform to ensure proper order
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(SimEntityTransformSystem))]
    public partial struct SimpleMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            //system initialization
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //get fixed delta time for deterministic movement
            //temporary simple fixed timestep
            fix deltaTime = (fix)0.016f;

            //update positions for all entities with movement
            foreach (var (transform, movement) in SystemAPI.Query<RefRW<FixTransformComponent>, 
                RefRO<SimpleMovementComponent>>())
            {
                //only move if movement is enabled
                if (!movement.ValueRO.isMoving) continue;

                //simple position integration: position += velocity * deltaTime
                fix3 currentPosition = transform.ValueRO.position;
                fix3 velocity = movement.ValueRO.velocity;

                transform.ValueRW.position = currentPosition + velocity * deltaTime;
            }

            //TODO:
            //-----
            //* add bounds check
            //* add collision detection
            //* implement proper fixed timestep
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            //cleanup when system is destroyed
        }
    }

}