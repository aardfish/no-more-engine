using Unity.Entities;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using NoMoreEngine.Simulation.Components;

namespace NoMoreEngine.Simulation.Systems
{
    /// <summary>
    /// Collision detection system using AABB (narrow-phase only for now)
    /// Runs after movement, before collision response
    /// Generates collision events that are processed by other systems
    /// </summary>
    [UpdateInGroup(typeof(PhysicsPhase))]
    [UpdateAfter(typeof(SimpleMovementSystem))]
    [UpdateBefore(typeof(CollisionResponseSystem))]
    public partial struct CollisionDetectionSystem : ISystem
    {
        private EntityQuery dynamicCollidersQuery;
        private EntityQuery staticCollidersQuery;
        private EntityQuery layerMatrixQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // Query for dynamic entities (those that can move and collide)
            dynamicCollidersQuery = SystemAPI.QueryBuilder()
                .WithAll<FixTransformComponent, CollisionBoundsComponent, CollisionResponseComponent>()
                .WithNone<StaticColliderComponent>()
                .Build();

            // Query for static entities (environment, buildings, etc.)
            staticCollidersQuery = SystemAPI.QueryBuilder()
                .WithAll<FixTransformComponent, CollisionBoundsComponent>()
                .WithAll<StaticColliderComponent>()
                .Build();

            // Query for collision layer matrix (singleton)
            layerMatrixQuery = SystemAPI.QueryBuilder()
                .WithAll<CollisionLayerMatrix>()
                .Build();

            // Require collision layer matrix to exist
            state.RequireForUpdate(layerMatrixQuery);

            // Create default collision layer matrix if none exists
            if (layerMatrixQuery.IsEmpty)
            {
                var entity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(entity, CollisionLayerMatrix.CreateDefault());
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Clear all collision event buffers from previous frame
            ClearCollisionEvents(ref state);

            // Early exit if no dynamic colliders
            if (dynamicCollidersQuery.IsEmpty) return;

            // Get collision layer matrix
            var layerMatrix = SystemAPI.GetSingleton<CollisionLayerMatrix>();

            // Collect all potential collision candidates with sufficient initial capacity
            // Estimate: (dynamic entities * static entities) + (dynamic entities * dynamic entities / 2)
            int estimatedCollisions = dynamicCollidersQuery.CalculateEntityCount() *
                                     (staticCollidersQuery.CalculateEntityCount() + dynamicCollidersQuery.CalculateEntityCount());
            var collisionCandidates = new NativeList<CollisionCandidate>(estimatedCollisions + 10, Allocator.TempJob);

            // Prepare static entity data for jobs
            var staticEntities = staticCollidersQuery.ToEntityArray(Allocator.TempJob);
            var staticTransforms = staticCollidersQuery.ToComponentDataArray<FixTransformComponent>(Allocator.TempJob);
            var staticBounds = staticCollidersQuery.ToComponentDataArray<CollisionBoundsComponent>(Allocator.TempJob);

            // Get static entity layers (default to Environment if no response component)
            var staticLayers = new NativeArray<CollisionLayer>(staticEntities.Length, Allocator.TempJob);
            for (int i = 0; i < staticEntities.Length; i++)
            {
                if (SystemAPI.HasComponent<CollisionResponseComponent>(staticEntities[i]))
                {
                    var response = SystemAPI.GetComponent<CollisionResponseComponent>(staticEntities[i]);
                    staticLayers[i] = response.entityLayer;
                }
                else
                {
                    staticLayers[i] = CollisionLayer.Environment;
                }
            }

            // Job 1: Dynamic vs Static collision detection
            var dynamicVsStaticJob = new DynamicVsStaticCollisionJob
            {
                layerMatrix = layerMatrix,
                staticEntities = staticEntities,
                staticTransforms = staticTransforms,
                staticBounds = staticBounds,
                staticLayers = staticLayers,
                collisionCandidates = collisionCandidates.AsParallelWriter()
            };

            var dynamicVsStaticHandle = dynamicVsStaticJob.ScheduleParallel(dynamicCollidersQuery, state.Dependency);

            // Job 2: Dynamic vs Dynamic collision detection
            var dynamicTransforms = dynamicCollidersQuery.ToComponentDataArray<FixTransformComponent>(Allocator.TempJob);
            var dynamicBounds = dynamicCollidersQuery.ToComponentDataArray<CollisionBoundsComponent>(Allocator.TempJob);
            var dynamicResponses = dynamicCollidersQuery.ToComponentDataArray<CollisionResponseComponent>(Allocator.TempJob);
            var dynamicEntities = dynamicCollidersQuery.ToEntityArray(Allocator.TempJob);

            var dynamicVsDynamicJob = new DynamicVsDynamicCollisionJob
            {
                layerMatrix = layerMatrix,
                collisionCandidates = collisionCandidates.AsParallelWriter(),
                dynamicTransforms = dynamicTransforms,
                dynamicBounds = dynamicBounds,
                dynamicResponses = dynamicResponses,
                dynamicEntities = dynamicEntities
            };

            var dynamicVsDynamicHandle = dynamicVsDynamicJob.Schedule(dynamicVsStaticHandle);

            // Wait for collision detection to complete
            dynamicVsDynamicHandle.Complete();

            // Sort collision candidates for deterministic processing
            if (collisionCandidates.Length > 0)
            {
                collisionCandidates.Sort();

                // Distribute collision events to entity buffers
                DistributeCollisionEvents(ref state, collisionCandidates.AsArray());
            }

            // Cleanup
            collisionCandidates.Dispose();
            staticEntities.Dispose();
            staticTransforms.Dispose();
            staticBounds.Dispose();
            staticLayers.Dispose();
            dynamicTransforms.Dispose();
            dynamicBounds.Dispose();
            dynamicResponses.Dispose();
            dynamicEntities.Dispose();
        }

        /// <summary>
        /// Clear collision event buffers from all entities
        /// </summary>
        private void ClearCollisionEvents(ref SystemState state)
        {
            foreach (var buffer in SystemAPI.Query<DynamicBuffer<CollisionEventBuffer>>())
            {
                buffer.Clear();
            }
        }

        /// <summary>
        /// Distribute collision events to entity collision event buffers
        /// </summary>
        private void DistributeCollisionEvents(ref SystemState state, NativeArray<CollisionCandidate> candidates)
        {
            foreach (var candidate in candidates)
            {
                var collisionEvent = candidate.ToCollisionEvent();

                // Add event to both entities (if they have collision event buffers)
                if (SystemAPI.HasBuffer<CollisionEventBuffer>(candidate.entityA))
                {
                    var bufferA = SystemAPI.GetBuffer<CollisionEventBuffer>(candidate.entityA);
                    bufferA.Add(collisionEvent);
                }

                if (SystemAPI.HasBuffer<CollisionEventBuffer>(candidate.entityB))
                {
                    var bufferB = SystemAPI.GetBuffer<CollisionEventBuffer>(candidate.entityB);
                    bufferB.Add(collisionEvent);
                }
            }
        }
    }

    /// <summary>
    /// Burst-compiled collision utilities
    /// </summary>
    [BurstCompile]
    public static class CollisionUtility
    {
        /// <summary>
        /// Test AABB collision between two entities
        /// </summary>
        [BurstCompile]
        public static bool TestAABBCollision(
            in FixTransformComponent transformA, in CollisionBoundsComponent boundsA,
            in FixTransformComponent transformB, in CollisionBoundsComponent boundsB,
            out CollisionCandidate collision)
        {
            // Get world space bounds for both entities
            boundsA.GetWorldBounds(transformA.position, out fix3 minA, out fix3 maxA);
            boundsB.GetWorldBounds(transformB.position, out fix3 minB, out fix3 maxB);

            // AABB collision test
            bool colliding = minA.x <= maxB.x && maxA.x >= minB.x &&
                            minA.y <= maxB.y && maxA.y >= minB.y &&
                            minA.z <= maxB.z && maxA.z >= minB.z;

            collision = default;

            if (colliding)
            {
                // Calculate collision details
                fix3 overlapMin = fix3.Max(minA, minB);
                fix3 overlapMax = fix3.Min(maxA, maxB);
                fix3 overlap = overlapMax - overlapMin;

                // Find the axis with minimum penetration (separation axis)
                fix penetrationDepth;
                fix3 contactNormal;

                if (overlap.x <= overlap.y && overlap.x <= overlap.z)
                {
                    // X-axis has minimum penetration
                    penetrationDepth = overlap.x;
                    contactNormal = transformA.position.x < transformB.position.x ?
                        new fix3((fix)(-1), (fix)0, (fix)0) : new fix3((fix)1, (fix)0, (fix)0);
                }
                else if (overlap.y <= overlap.z)
                {
                    // Y-axis has minimum penetration
                    penetrationDepth = overlap.y;
                    contactNormal = transformA.position.y < transformB.position.y ?
                        new fix3((fix)0, (fix)(-1), (fix)0) : new fix3((fix)0, (fix)1, (fix)0);
                }
                else
                {
                    // Z-axis has minimum penetration
                    penetrationDepth = overlap.z;
                    contactNormal = transformA.position.z < transformB.position.z ?
                        new fix3((fix)0, (fix)0, (fix)(-1)) : new fix3((fix)0, (fix)0, (fix)1);
                }

                // Contact point is center of overlap region
                fix3 contactPoint = (overlapMin + overlapMax) * (fix)0.5f;

                collision = new CollisionCandidate
                {
                    contactPoint = contactPoint,
                    contactNormal = contactNormal,
                    penetrationDepth = penetrationDepth
                };

                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Job for detecting collisions between dynamic and static entities
    /// </summary>
    [BurstCompile]
    public partial struct DynamicVsStaticCollisionJob : IJobEntity
    {
        [ReadOnly] public CollisionLayerMatrix layerMatrix;
        [ReadOnly] public NativeArray<Entity> staticEntities;
        [ReadOnly] public NativeArray<FixTransformComponent> staticTransforms;
        [ReadOnly] public NativeArray<CollisionBoundsComponent> staticBounds;
        [ReadOnly] public NativeArray<CollisionLayer> staticLayers;
        public NativeList<CollisionCandidate>.ParallelWriter collisionCandidates;

        public void Execute(Entity dynamicEntity,
            in FixTransformComponent dynamicTransform,
            in CollisionBoundsComponent dynamicBounds,
            in CollisionResponseComponent dynamicResponse)
        {
            // Check collision against all static entities
            for (int i = 0; i < staticEntities.Length; i++)
            {
                var staticEntity = staticEntities[i];

                // Skip self (shouldn't happen but safety first)
                if (dynamicEntity == staticEntity) continue;

                var staticLayer = staticLayers[i];

                // Check if these layers can collide
                if (!layerMatrix.CanCollide(dynamicResponse.entityLayer, staticLayer))
                    continue;

                // Test AABB collision
                if (CollisionUtility.TestAABBCollision(
                    dynamicTransform, dynamicBounds,
                    staticTransforms[i], staticBounds[i],
                    out CollisionCandidate collision))
                {
                    // Fill in entity references and layers
                    collision.entityA = dynamicEntity;
                    collision.entityB = staticEntity;
                    collision.layerA = dynamicResponse.entityLayer;
                    collision.layerB = staticLayer;

                    collisionCandidates.AddNoResize(collision);
                }
            }
        }
    }

    /// <summary>
    /// Job for detecting collisions between dynamic entities
    /// Uses cached arrays for better performance in nested loops
    /// </summary>
    [BurstCompile]
    public struct DynamicVsDynamicCollisionJob : IJob
    {
        [ReadOnly] public CollisionLayerMatrix layerMatrix;
        public NativeList<CollisionCandidate>.ParallelWriter collisionCandidates;

        [ReadOnly] public NativeArray<FixTransformComponent> dynamicTransforms;
        [ReadOnly] public NativeArray<CollisionBoundsComponent> dynamicBounds;
        [ReadOnly] public NativeArray<CollisionResponseComponent> dynamicResponses;
        [ReadOnly] public NativeArray<Entity> dynamicEntities;

        public void Execute()
        {
            int entityCount = dynamicEntities.Length;

            // Test all pairs of dynamic entities (O(n²) but with layer filtering)
            for (int i = 0; i < entityCount; i++)
            {
                for (int j = i + 1; j < entityCount; j++)
                {
                    var entityA = dynamicEntities[i];
                    var entityB = dynamicEntities[j];
                    var responseA = dynamicResponses[i];
                    var responseB = dynamicResponses[j];

                    // Check if these layers can collide
                    if (!layerMatrix.CanCollide(responseA.entityLayer, responseB.entityLayer))
                        continue;

                    // Test AABB collision
                    if (CollisionUtility.TestAABBCollision(
                        dynamicTransforms[i], dynamicBounds[i],
                        dynamicTransforms[j], dynamicBounds[j],
                        out CollisionCandidate collision))
                    {
                        // Fill in entity references and layers
                        collision.entityA = entityA;
                        collision.entityB = entityB;
                        collision.layerA = responseA.entityLayer;
                        collision.layerB = responseB.entityLayer;

                        collisionCandidates.AddNoResize(collision);
                    }
                }
            }
        }
    }
}