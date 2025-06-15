using NoMoreEngine.Simulation.Snapshot;
using Unity.Entities;
using Unity.Mathematics;


namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Core collision bounds using AABB (Axis-Aligned Bounding Box)
    /// </summary>
    [Snapshotable(Priority = 3)]
    public struct CollisionBoundsComponent : IComponentData, ISnapshotable<CollisionBoundsComponent>
    {
        public fix3 size;           // AABB dimensions (full size, not half-extents)
        public fix3 offset;         // Offset from transform center
        public fix tolerance;       // Small tolerance for collision precision (optional)

        public CollisionBoundsComponent(fix3 size, fix3 offset = default, fix tolerance = default)
        {
            this.size = size;
            this.offset = offset;
            this.tolerance = tolerance == default ? (fix)0.001f : tolerance;
        }

        /// <summary>
        /// Get AABB min/max bounds in world space
        /// </summary>
        public void GetWorldBounds(fix3 worldPosition, out fix3 min, out fix3 max)
        {
            fix3 center = worldPosition + offset;
            fix3 halfSize = size * (fix)0.5f;
            min = center - halfSize;
            max = center + halfSize;
        }

        public int GetSnapshotSize() => System.Runtime.InteropServices.Marshal.SizeOf<CollisionBoundsComponent>();
        public bool ValidateSnapshot() => size.x > (fix)0 && size.y > (fix)0 && size.z > (fix)0;
    }

    /// <summary>
    /// Collision layer system using bit flags for flexible layer interactions
    /// </summary>
    [System.Flags]
    public enum CollisionLayer : uint
    {
        None = 0,
        Player = 1 << 0,        // 1
        Enemy = 1 << 1,         // 2  
        Projectile = 1 << 2,    // 4
        Environment = 1 << 3,   // 8
        Pickup = 1 << 4,        // 16
        Trigger = 1 << 5,       // 32
        // Add more as needed, up to 32 layers
        All = ~0u               // Everything
    }

    /// <summary>
    /// Defines how an entity responds to collisions
    /// </summary>
    [Snapshotable(Priority = 4)]
    public struct CollisionResponseComponent : IComponentData, ISnapshotable<CollisionResponseComponent>
    {
        public CollisionResponse responseType;
        public fix bounciness;          // 0-1, how much velocity is retained on bounce
        public fix friction;            // 0-1, how much velocity is lost on slide
        public CollisionLayer entityLayer;     // What layer this entity is on
        public CollisionLayer collidesWith;    // What layers this entity collides with

        public CollisionResponseComponent(CollisionResponse responseType,
            CollisionLayer entityLayer, CollisionLayer collidesWith,
            fix bounciness = default, fix friction = default)
        {
            this.responseType = responseType;
            this.entityLayer = entityLayer;
            this.collidesWith = collidesWith;
            this.bounciness = bounciness == default ? (fix)0.5f : bounciness;
            this.friction = friction == default ? (fix)0.1f : friction;
        }

        public int GetSnapshotSize() => System.Runtime.InteropServices.Marshal.SizeOf<CollisionResponseComponent>();
        public bool ValidateSnapshot() => true;
    }

    /// <summary>
    /// Types of collision response behaviors
    /// </summary>
    public enum CollisionResponse : byte
    {
        None = 0,       // No response (trigger-like)
        Stop = 1,       // Stop movement at collision point
        Slide = 2,      // Slide along collision surface
        Bounce = 3,     // Bounce off collision surface
        Destroy = 4     // Destroy entity on collision (for projectiles)
    }

    /// <summary>
    /// Marks entities as static for collision optimization
    /// Static entities don't move and can be cached/optimized
    /// </summary>
    [Snapshotable(Priority = 11)]
    public struct StaticColliderComponent : IComponentData, ISnapshotable<StaticColliderComponent>
    {
        public bool isStatic;

        public StaticColliderComponent(bool isStatic = true)
        {
            this.isStatic = isStatic;
        }

        public int GetSnapshotSize() => System.Runtime.InteropServices.Marshal.SizeOf<StaticColliderComponent>();
        public bool ValidateSnapshot() => true; // Static state is always valid
    }

    /// <summary>
    /// Collision event data generated during collision detection
    /// </summary>
    public struct CollisionEvent
    {
        public Entity entityA;
        public Entity entityB;
        public fix3 contactPoint;      // World space contact point
        public fix3 contactNormal;     // Normal pointing from A to B
        public fix penetrationDepth;   // How deep the collision is
        public CollisionLayer layerA;
        public CollisionLayer layerB;

        public CollisionEvent(Entity entityA, Entity entityB, fix3 contactPoint, 
            fix3 contactNormal, fix penetrationDepth, CollisionLayer layerA, CollisionLayer layerB)
        {
            this.entityA = entityA;
            this.entityB = entityB;
            this.contactPoint = contactPoint;
            this.contactNormal = contactNormal;
            this.penetrationDepth = penetrationDepth;
            this.layerA = layerA;
            this.layerB = layerB;
        }
    }

    /// <summary>
    /// Buffer component to store collision events for an entity
    /// Events are cleared each frame after processing
    /// </summary>
    [BufferSnapshotable(Priority = 20, MaxElements = 16)]
    [InternalBufferCapacity(8)]
    public struct CollisionEventBuffer : IBufferElementData
    {
        public CollisionEvent collisionEvent;

        public static implicit operator CollisionEvent(CollisionEventBuffer buffer)
        {
            return buffer.collisionEvent;
        }

        public static implicit operator CollisionEventBuffer(CollisionEvent collisionEvent)
        {
            return new CollisionEventBuffer { collisionEvent = collisionEvent };
        }
    }

    /// <summary>
    /// Buffer to store collision contacts from previous frame
    /// Must be snapshotted to maintain collision continuity
    /// </summary>
    [BufferSnapshotable(Priority = 21, MaxElements = 32, RequiresEntityRemapping = true)]
    [InternalBufferCapacity(4)]
    public struct CollisionContactBuffer : IBufferElementData
    {
        public Entity otherEntity;
        public fix3 contactPoint;
        public fix3 contactNormal;
        public fix penetration;
        public CollisionLayer otherLayer;
        
        public CollisionContactBuffer(Entity other, fix3 point, fix3 normal, fix depth, CollisionLayer layer)
        {
            otherEntity = other;
            contactPoint = point;
            contactNormal = normal;
            penetration = depth;
            otherLayer = layer;
        }
    }

    /// <summary>
    /// Deterministic collision candidate for sorting before resolution
    /// </summary>
    public struct CollisionCandidate : System.IComparable<CollisionCandidate>
    {
        public Entity entityA;
        public Entity entityB;
        public fix3 contactPoint;
        public fix3 contactNormal;
        public fix penetrationDepth;
        public CollisionLayer layerA;
        public CollisionLayer layerB;

        /// <summary>
        /// Deterministic comparison for consistent collision resolution order
        /// Sort by: entity A index, then entity B index, then penetration depth (deepest first)
        /// </summary>
        public int CompareTo(CollisionCandidate other)
        {
            // First sort by entityA index
            int entityComparison = entityA.Index.CompareTo(other.entityA.Index);
            if (entityComparison != 0) return entityComparison;

            // Then by entityB index
            entityComparison = entityB.Index.CompareTo(other.entityB.Index);
            if (entityComparison != 0) return entityComparison;

            // Finally by penetration depth (deeper collisions first for stability)
            return other.penetrationDepth.CompareTo(penetrationDepth);
        }

        public CollisionEvent ToCollisionEvent()
        {
            return new CollisionEvent(entityA, entityB, contactPoint, contactNormal,
                penetrationDepth, layerA, layerB);
        }
    }
    
    /// <summary>
    /// Tracks collision state that must be preserved across snapshots
    /// This ensures entities maintain proper physics state after restore
    /// </summary>
    [Snapshotable(Priority = 2, IncludeInHash = true)] // High priority, include in determinism hash
    public struct CollisionStateComponent : IComponentData, ISnapshotable<CollisionStateComponent>
    {
        // Ground contact state
        public bool isGrounded;
        public fix3 groundNormal;
        public fix3 groundContactPoint;
        public fix timeSinceLastGrounded;
        
        // Collision resolution state
        public fix3 lastResolvedPosition;
        public fix3 lastResolvedVelocity;
        public uint lastResolvedTick;
        
        // Penetration state
        public bool wasResolvingPenetration;
        public fix penetrationDepth;
        public fix3 penetrationNormal;
        
        public static CollisionStateComponent Default => new CollisionStateComponent
        {
            isGrounded = false,
            groundNormal = new fix3(fix.Zero, fix.One, fix.Zero),
            groundContactPoint = fix3.zero,
            timeSinceLastGrounded = fix.Zero,
            lastResolvedPosition = fix3.zero,
            lastResolvedVelocity = fix3.zero,
            lastResolvedTick = 0,
            wasResolvingPenetration = false,
            penetrationDepth = fix.Zero,
            penetrationNormal = fix3.zero
        };
        
        public int GetSnapshotSize() => System.Runtime.InteropServices.Marshal.SizeOf<CollisionStateComponent>();
        public bool ValidateSnapshot() => true;
    }

    /// <summary>
    /// Global collision layer interaction matrix
    /// Singleton component that defines which layers can collide with which
    /// </summary>

    [Snapshotable(Priority = -5, IncludeInHash = true)]
    public struct CollisionLayerMatrix : IComponentData, ISnapshotable<CollisionLayerMatrix>
    {
        // We'll use a simple approach: each layer has a mask of what it can collide with
        // This is more cache-friendly than a full NxN matrix for small layer counts
        public CollisionLayer playerCollidesWith;
        public CollisionLayer enemyCollidesWith;
        public CollisionLayer projectileCollidesWith;
        public CollisionLayer environmentCollidesWith;
        public CollisionLayer pickupCollidesWith;
        public CollisionLayer triggerCollidesWith;

        /// <summary>
        /// Check if two layers can collide with each other
        /// </summary>
        public bool CanCollide(CollisionLayer layerA, CollisionLayer layerB)
        {
            CollisionLayer maskA = GetCollisionMask(layerA);
            CollisionLayer maskB = GetCollisionMask(layerB);

            // Check if A can collide with B, or B can collide with A
            return (maskA & layerB) != 0 || (maskB & layerA) != 0;
        }

        private CollisionLayer GetCollisionMask(CollisionLayer layer)
        {
            return layer switch
            {
                CollisionLayer.Player => playerCollidesWith,
                CollisionLayer.Enemy => enemyCollidesWith,
                CollisionLayer.Projectile => projectileCollidesWith,
                CollisionLayer.Environment => environmentCollidesWith,
                CollisionLayer.Pickup => pickupCollidesWith,
                CollisionLayer.Trigger => triggerCollidesWith,
                _ => CollisionLayer.None
            };
        }

        /// <summary>
        /// Default collision matrix for typical game setup
        /// </summary>
        public static CollisionLayerMatrix CreateDefault()
        {
            return new CollisionLayerMatrix
            {
                // Players collide with enemies, environment, projectiles, pickups, triggers
                playerCollidesWith = CollisionLayer.Enemy | CollisionLayer.Environment |
                                   CollisionLayer.Projectile | CollisionLayer.Pickup | CollisionLayer.Trigger,

                // Enemies collide with players, environment, projectiles
                enemyCollidesWith = CollisionLayer.Player | CollisionLayer.Environment | CollisionLayer.Projectile,

                // Projectiles collide with players, enemies, environment (but not other projectiles)
                projectileCollidesWith = CollisionLayer.Player | CollisionLayer.Enemy | CollisionLayer.Environment,

                // Environment collides with everything (static objects)
                environmentCollidesWith = CollisionLayer.All,

                // Pickups only collide with players
                pickupCollidesWith = CollisionLayer.Player,

                // Triggers collide with players and enemies (but don't stop movement)
                triggerCollidesWith = CollisionLayer.Player | CollisionLayer.Enemy
            };
        }

        public int GetSnapshotSize() => System.Runtime.InteropServices.Marshal.SizeOf<CollisionLayerMatrix>();
        public bool ValidateSnapshot() => true; // Always valid, just defines layer interactions
    }

    /// <summary>
    /// Spatial partitioning component for future broad-phase collision optimization
    /// Currently just scaffolding for future implementation
    /// </summary>
    public struct SpatialHashComponent : IComponentData
    {
        public int2 gridCell;       // Which grid cell this entity is in
        public uint cellHash;       // Hashed cell coordinate for fast lookup
        public bool isDirty;        // Needs spatial update this frame

        public SpatialHashComponent(int2 gridCell)
        {
            this.gridCell = gridCell;
            this.cellHash = (uint)(gridCell.x * 73856093 ^ gridCell.y * 19349663); // Simple hash
            this.isDirty = true;
        }
    }
}