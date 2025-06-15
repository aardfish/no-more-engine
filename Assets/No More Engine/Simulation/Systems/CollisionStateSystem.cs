using Unity.Entities;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using NoMoreEngine.Simulation.Components;

namespace NoMoreEngine.Simulation.Systems
{
    /// <summary>
    /// Manages collision state for deterministic snapshots
    /// Handles both component initialization and state tracking
    /// </summary>
    [UpdateInGroup(typeof(PhysicsPhase))]
    [UpdateAfter(typeof(CollisionResponseSystem))]
    [UpdateBefore(typeof(SimEntityTransformSystem))]
    public partial struct CollisionStateSystem : ISystem
    {
        private EntityQuery uninitializedEntitiesQuery;
        private EntityQuery needsContactBufferQuery;
        
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationTimeComponent>();
            
            // Query for entities that need collision state components
            uninitializedEntitiesQuery = SystemAPI.QueryBuilder()
                .WithAll<SimpleMovementComponent, CollisionBoundsComponent>()
                .WithNone<CollisionStateComponent>()
                .Build();
                
            needsContactBufferQuery = SystemAPI.QueryBuilder()
                .WithAll<CollisionStateComponent, CollisionBoundsComponent>()
                .WithNone<CollisionContactBuffer>()
                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // First, ensure all entities have required components
            InitializeCollisionComponents(ref state);
            
            // Then update collision states
            var time = SystemAPI.GetSingleton<SimulationTimeComponent>();
            var deltaTime = time.deltaTime;
            var currentTick = time.currentTick;
            
            // Update collision states based on current frame's collision events
            UpdateCollisionStates(ref state, deltaTime, currentTick);
            
            // Copy current collision events to contact buffer for next frame
            StoreCollisionContacts(ref state);
        }
        
        [BurstCompile]
        private void InitializeCollisionComponents(ref SystemState state)
        {
            // Add collision state component to entities that need it
            if (!uninitializedEntitiesQuery.IsEmpty)
            {
                state.EntityManager.AddComponent<CollisionStateComponent>(uninitializedEntitiesQuery);
                
                // Initialize with default values
                foreach (var collisionState in 
                    SystemAPI.Query<RefRW<CollisionStateComponent>>()
                    .WithNone<StaticColliderComponent>()) // Only for dynamic entities
                {
                    collisionState.ValueRW = CollisionStateComponent.Default;
                }
            }
            
            // Add contact buffer to entities that need it
            if (!needsContactBufferQuery.IsEmpty)
            {
                state.EntityManager.AddComponent<CollisionContactBuffer>(needsContactBufferQuery);
            }
        }
        
        [BurstCompile]
        private void UpdateCollisionStates(ref SystemState state, fix deltaTime, uint currentTick)
        {
            // Process entities with collision events
            foreach (var (collisionState, transform, movement, collisionEvents, entity) in 
                SystemAPI.Query<RefRW<CollisionStateComponent>, RefRO<FixTransformComponent>,
                    RefRO<SimpleMovementComponent>, DynamicBuffer<CollisionEventBuffer>>()
                .WithEntityAccess())
            {
                bool foundGround = false;
                fix3 bestGroundNormal = fix3.zero;
                fix3 bestContactPoint = fix3.zero;
                fix maxNormalY = fix.Zero;
                
                // Reset penetration state
                collisionState.ValueRW.wasResolvingPenetration = false;
                collisionState.ValueRW.penetrationDepth = fix.Zero;
                
                // Check all collision events for ground contact
                for (int i = 0; i < collisionEvents.Length; i++)
                {
                    var collision = collisionEvents[i].collisionEvent;
                    
                    // Determine which entity we are and get the correct normal
                    bool isEntityA = entity == collision.entityA;
                    fix3 normal = isEntityA ? collision.contactNormal : -collision.contactNormal;
                    
                    // Ground detection: normal pointing up (y > 0.7 ~= 45 degrees)
                    if (normal.y > (fix)0.7f && normal.y > maxNormalY)
                    {
                        foundGround = true;
                        maxNormalY = normal.y;
                        bestGroundNormal = normal;
                        bestContactPoint = collision.contactPoint;
                    }
                    
                    // Track if we're resolving penetration
                    if (collision.penetrationDepth > fix.Zero)
                    {
                        collisionState.ValueRW.wasResolvingPenetration = true;
                        if (collision.penetrationDepth > collisionState.ValueRO.penetrationDepth)
                        {
                            collisionState.ValueRW.penetrationDepth = collision.penetrationDepth;
                            collisionState.ValueRW.penetrationNormal = normal;
                        }
                    }
                }
                
                // Update ground state
                if (foundGround)
                {
                    collisionState.ValueRW.isGrounded = true;
                    collisionState.ValueRW.groundNormal = bestGroundNormal;
                    collisionState.ValueRW.groundContactPoint = bestContactPoint;
                    collisionState.ValueRW.timeSinceLastGrounded = fix.Zero;
                    
                    // Store resolved position/velocity when grounded
                    collisionState.ValueRW.lastResolvedPosition = transform.ValueRO.position;
                    collisionState.ValueRW.lastResolvedVelocity = movement.ValueRO.velocity;
                    collisionState.ValueRW.lastResolvedTick = currentTick;
                }
                else
                {
                    // Not grounded this frame
                    collisionState.ValueRW.isGrounded = false;
                    collisionState.ValueRW.timeSinceLastGrounded += deltaTime;
                    
                    // But if we were grounded very recently and have minimal downward velocity,
                    // keep the ground contact info for one more frame (helps with bumpy surfaces)
                    if (collisionState.ValueRO.timeSinceLastGrounded > deltaTime * (fix)2)
                    {
                        collisionState.ValueRW.groundNormal = new fix3(fix.Zero, fix.One, fix.Zero);
                        collisionState.ValueRW.groundContactPoint = fix3.zero;
                    }
                }
            }
            
            // Process entities without collision events (not touching anything)
            foreach (var (collisionState, transform, movement) in 
                SystemAPI.Query<RefRW<CollisionStateComponent>, RefRO<FixTransformComponent>,
                    RefRO<SimpleMovementComponent>>()
                .WithNone<CollisionEventBuffer>())
            {
                // No collisions means not grounded
                collisionState.ValueRW.isGrounded = false;
                collisionState.ValueRW.timeSinceLastGrounded += deltaTime;
                collisionState.ValueRW.wasResolvingPenetration = false;
                collisionState.ValueRW.penetrationDepth = fix.Zero;
                
                // Clear ground contact info after a short delay
                if (collisionState.ValueRO.timeSinceLastGrounded > deltaTime * (fix)2)
                {
                    collisionState.ValueRW.groundNormal = new fix3(fix.Zero, fix.One, fix.Zero);
                    collisionState.ValueRW.groundContactPoint = fix3.zero;
                }
            }
        }
        
        [BurstCompile]
        private void StoreCollisionContacts(ref SystemState state)
        {
            // Store current frame's contacts for next frame reference
            foreach (var (contactBuffer, collisionEvents, entity) in 
                SystemAPI.Query<DynamicBuffer<CollisionContactBuffer>, DynamicBuffer<CollisionEventBuffer>>()
                .WithEntityAccess())
            {
                contactBuffer.Clear();
                
                // Store up to buffer capacity
                int contactsToStore = math.min(collisionEvents.Length, contactBuffer.Capacity);
                
                for (int i = 0; i < contactsToStore; i++)
                {
                    var collision = collisionEvents[i].collisionEvent;
                    bool isEntityA = entity == collision.entityA;
                    
                    contactBuffer.Add(new CollisionContactBuffer(
                        isEntityA ? collision.entityB : collision.entityA,
                        collision.contactPoint,
                        isEntityA ? collision.contactNormal : -collision.contactNormal,
                        collision.penetrationDepth,
                        isEntityA ? collision.layerB : collision.layerA
                    ));
                }
            }
            
            // Clear contact buffer for entities with no collisions
            foreach (var contactBuffer in 
                SystemAPI.Query<DynamicBuffer<CollisionContactBuffer>>()
                .WithNone<CollisionEventBuffer>())
            {
                contactBuffer.Clear();
            }
        }
    }
}