using Unity.Entities;
using Unity.Burst;
using Unity.Collections;
using static fixMath;
using NoMoreEngine.Simulation.Components;

namespace NoMoreEngine.Simulation.Systems
{
    /// <summary>
    /// Handles collision response (stop, slide, bounce) based on collision events
    /// Runs after collision detection, before transform system
    /// Uses hybrid approach: position correction + velocity modification
    /// </summary>
    [UpdateInGroup(typeof(PhysicsPhase))]
    [UpdateAfter(typeof(CollisionDetectionSystem))]
    [UpdateBefore(typeof(SimEntityTransformSystem))]
    [BurstCompile]
    public partial struct CollisionResponseSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // System requires entities with collision event buffers
            state.RequireForUpdate<CollisionEventBuffer>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //safety check
            int processedCollisions = 0;
            const int MAX_COLLISIONS_PER_FRAME = 100;

            // Process collision responses for all entities that have collision events
            foreach (var (transform, response, movement, collisionEvents, entity) in
                SystemAPI.Query<RefRW<FixTransformComponent>, RefRO<CollisionResponseComponent>,
                    RefRW<SimpleMovementComponent>, DynamicBuffer<CollisionEventBuffer>>()
                .WithEntityAccess())
            {
                // Skip if no collision events
                if (collisionEvents.Length == 0) continue;

                // Process all collision events for this entity
                // Events are already sorted deterministically by the detection system
                for (int i = 0; i < collisionEvents.Length; i++)
                {
                    var collisionEvent = collisionEvents[i].collisionEvent;

                    // Determine which entity we are in this collision
                    bool isEntityA = entity == collisionEvent.entityA;
                    fix3 contactNormal = isEntityA ? collisionEvent.contactNormal : -collisionEvent.contactNormal;

                    // Apply collision response based on response type
                    switch (response.ValueRO.responseType)
                    {
                        case CollisionResponse.Stop:
                            CollisionResponseUtility.HandleStopResponse(ref transform.ValueRW, ref movement.ValueRW,
                                in contactNormal, in collisionEvent.penetrationDepth);
                            break;

                        case CollisionResponse.Slide:
                            CollisionResponseUtility.HandleSlideResponse(ref transform.ValueRW, ref movement.ValueRW,
                                in contactNormal, in collisionEvent.penetrationDepth, in response.ValueRO.friction);
                            break;

                        case CollisionResponse.Bounce:
                            CollisionResponseUtility.HandleBounceResponse(ref transform.ValueRW, ref movement.ValueRW,
                                in contactNormal, in collisionEvent.penetrationDepth, in response.ValueRO.bounciness);
                            break;

                        case CollisionResponse.Destroy:
                            // Mark entity for destruction (implement destroy system later)
                            // For now, just stop movement
                            movement.ValueRW.velocity = fix3.zero;
                            movement.ValueRW.isMoving = false;
                            break;

                        case CollisionResponse.None:
                            // Trigger-like behavior, no physical response
                            break;
                    }

                    if (++processedCollisions > MAX_COLLISIONS_PER_FRAME)
                    {
                        break;
                    }

                    //Debug.Log($"Processing {collisionEvents.Length} collision events for entity {entity.Index}");
                }
            }
        }
    }

    /// <summary>
    /// Burst-compiled collision response utilities
    /// </summary>
    [BurstCompile]
    public static class CollisionResponseUtility
    {
        /// <summary>
        /// Stop response: Move entity out of collision and stop movement
        /// </summary>
        [BurstCompile]
        public static void HandleStopResponse(ref FixTransformComponent transform,
            ref SimpleMovementComponent movement, in fix3 contactNormal, in fix penetrationDepth)
        {
            // Position correction: move entity out of collision
            transform.position += contactNormal * penetrationDepth;

            // Stop movement in the direction of the collision
            fix3 velocity = movement.velocity;
            fix velocityAlongNormal = fix3.Dot(velocity, contactNormal);

            if (velocityAlongNormal < (fix)0) // Moving into the collision
            {
                // Remove velocity component along the normal
                movement.velocity = velocity - contactNormal * velocityAlongNormal;
            }

            // If velocity is very small after correction, stop completely
            if (fixMath.lengthsq(movement.velocity) < (fix)0.01f)
            {
                movement.velocity = fix3.zero;
                movement.isMoving = false;
            }
        }

        /// <summary>
        /// Slide response: Move entity out of collision and slide along surface
        /// </summary>
        [BurstCompile]
        public static void HandleSlideResponse(ref FixTransformComponent transform,
            ref SimpleMovementComponent movement, in fix3 contactNormal, in fix penetrationDepth, in fix friction)
        {
            // Position correction: move entity out of collision
            transform.position += contactNormal * penetrationDepth;

            // Slide along the surface
            fix3 velocity = movement.velocity;
            fix velocityAlongNormal = fix3.Dot(velocity, contactNormal);

            if (velocityAlongNormal < (fix)0) // Moving into the collision
            {
                // Project velocity onto the collision surface
                fix3 slideVelocity = velocity - contactNormal * velocityAlongNormal;

                // Apply friction
                slideVelocity *= ((fix)1 - friction);

                movement.velocity = slideVelocity;

                // If velocity is very small after sliding, stop completely
                if (fixMath.lengthsq(movement.velocity) < (fix)0.01f)
                {
                    movement.velocity = fix3.zero;
                    movement.isMoving = false;
                }
            }
        }

        /// <summary>
        /// Bounce response: Move entity out of collision and reflect velocity
        /// </summary>
        [BurstCompile]
        public static void HandleBounceResponse(ref FixTransformComponent transform,
            ref SimpleMovementComponent movement, in fix3 contactNormal, in fix penetrationDepth, in fix bounciness)
        {
            // Position correction: move entity out of collision
            transform.position += contactNormal * penetrationDepth;

            // Bounce off the surface
            fix3 velocity = movement.velocity;
            fix velocityAlongNormal = fix3.Dot(velocity, contactNormal);

            if (velocityAlongNormal < (fix)0) // Moving into the collision
            {
                // Reflect velocity component along the normal
                fix3 reflectedVelocity = velocity - contactNormal * velocityAlongNormal * ((fix)1 + bounciness);

                movement.velocity = reflectedVelocity;

                // If velocity is very small after bouncing, stop completely
                if (fixMath.lengthsq(movement.velocity) < (fix)0.01f)
                {
                    movement.velocity = fix3.zero;
                    movement.isMoving = false;
                }
            }
        }
    }
}