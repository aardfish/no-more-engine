using NoMoreEngine.Simulation.Components;
using Unity.Entities;
using Unity.Mathematics.FixedPoint;
using UnityEngine;


namespace NoMoreEngine.Simulation.Authoring
{
    /// <summary>
    /// Authoring component for creating SimEntities with optional collision and physics support
    /// </summary>
    public class SimEntityAuthoring : MonoBehaviour
    {
        [Header("SimEntity Setup")]
        public SimEntityType simEntityType = SimEntityType.Environment;

        [Header("Fixed-Point Transform")]
        public fp3 startPosition = fp3.zero;
        public fpquaternion startRotation = fpquaternion.identity;
        public fp3 startScale = new fp3(fp.one);

        [Header("Simple Movement (Optional)")]
        [SerializeField] private bool enableMovement = false;
        [SerializeField] private fp3 velocity = fp3.zero;

        [Header("Collision Settings")]
        [SerializeField] private bool enableCollision = false;
        [SerializeField] private bool isStaticCollider = false;

        [Header("Collision Bounds")]
        [SerializeField] private fp3 collisionSize = new fp3(fp.one);
        [SerializeField] private fp3 collisionOffset = fp3.zero;
        [SerializeField] private fp collisionTolerance = (fp)0.001f;

        [Header("Collision Response")]
        [SerializeField] private CollisionResponse collisionResponse = CollisionResponse.Stop;
        [SerializeField] private CollisionLayer entityLayer = CollisionLayer.Environment;
        [SerializeField] private CollisionLayer collidesWith = CollisionLayer.All;
        [SerializeField] private fp bounciness = (fp)0.5f;
        [SerializeField] private fp friction = (fp)0.1f;

        [Header("Collision Events")]
        [SerializeField] private bool generateCollisionEvents = false;

        [Header("Physics Settings")]
        [SerializeField] private bool enablePhysics = false;

        [Header("Mass & Gravity")]
        [SerializeField] private fp mass = (fp)1.0f;
        [SerializeField] private fp gravityScale = (fp)1.0f;
        [SerializeField] private bool affectedByGravity = true;
        [SerializeField] private fp terminalVelocity = (fp)50.0f; // 0 = no limit

        [Header("Custom Gravity (Optional)")]
        [SerializeField] private bool useCustomGravity = false;
        [SerializeField] private fp3 customGravity = new fp3((fp)0, (fp)(-9.81f), (fp)0);

        [Header("Physics Presets")]
        [SerializeField] private PhysicsPreset physicsPreset = PhysicsPreset.Custom;

        public enum PhysicsPreset
        {
            Custom,
            Normal,
            Heavy,
            Light,
            Floating,
            ZeroG
        }

        // Public accessors for inspector and debugging
        public SimEntityType EntityType => simEntityType;
        public fp3 StartPosition => startPosition;
        public bool EnableMovement => enableMovement;
        public bool EnableCollision => enableCollision;
        public bool IsStaticCollider => isStaticCollider;
        public CollisionResponse CollisionResponse => collisionResponse;
        public CollisionLayer EntityLayer => entityLayer;
        public bool EnablePhysics => enablePhysics;
        public fp Mass => mass;
        public bool AffectedByGravity => affectedByGravity;

        /// <summary>
        /// Baker converts this GameObject into ECS Entity during subscene conversion
        /// Handles basic SimEntity functionality, collision components, and physics components
        /// </summary>
        public class Baker : Baker<SimEntityAuthoring>
        {
            public override void Bake(SimEntityAuthoring authoring)
            {
                // Entity has no transform usage since we handle transforms manually
                var entity = GetEntity(TransformUsageFlags.None);

                // Add core components (basic SimEntity functionality)
                AddComponent(entity, new FixTransformComponent(
                    authoring.startPosition,
                    authoring.startRotation,
                    authoring.startScale
                ));

                AddComponent(entity, new SimEntityTypeComponent(authoring.simEntityType));

                // Add movement component if enabled
                if (authoring.enableMovement)
                {
                    AddComponent(entity, new SimpleMovementComponent(authoring.velocity, true));
                }

                // Add collision components if enabled
                if (authoring.enableCollision)
                {
                    BakeCollisionComponents(entity, authoring);
                }

                // Add physics components if enabled
                if (authoring.enablePhysics)
                {
                    BakePhysicsComponents(entity, authoring);
                }

                // TODO:
                // * add NetworkedSimEntity component when networking is implemented
                // * add other components based on entity type
            }

            /// <summary>
            /// Add collision components to the entity during baking
            /// </summary>
            private void BakeCollisionComponents(Entity entity, SimEntityAuthoring authoring)
            {
                // Add collision bounds
                AddComponent(entity, new CollisionBoundsComponent(
                    authoring.collisionSize,
                    authoring.collisionOffset,
                    authoring.collisionTolerance
                ));

                // Create collision response with default settings, then apply overrides
                var collisionResponse = new CollisionResponseComponent(
                    authoring.collisionResponse,
                    authoring.entityLayer,
                    authoring.collidesWith,
                    authoring.bounciness,
                    authoring.friction
                );

                // Apply default collision settings based on entity type
                collisionResponse = ApplyDefaultCollisionSettings(collisionResponse, authoring);

                // Add the final collision response component
                AddComponent(entity, collisionResponse);

                // Add static collider component if needed
                if (authoring.isStaticCollider)
                {
                    AddComponent(entity, new StaticColliderComponent(true));
                }

                // Add collision event buffer if needed
                if (authoring.generateCollisionEvents)
                {
                    AddBuffer<CollisionEventBuffer>(entity);
                }

                // Add additional components based on entity type
                ApplyEntityTypeSpecificComponents(entity, authoring);
            }

            /// <summary>
            /// Add physics components to the entity during baking
            /// </summary>
            private void BakePhysicsComponents(Entity entity, SimEntityAuthoring authoring)
            {
                // Apply preset if not using custom settings
                PhysicsComponent physicsComponent;
                if (authoring.physicsPreset != PhysicsPreset.Custom)
                {
                    physicsComponent = authoring.physicsPreset switch
                    {
                        PhysicsPreset.Normal => PhysicsComponent.Normal,
                        PhysicsPreset.Heavy => PhysicsComponent.Heavy,
                        PhysicsPreset.Light => PhysicsComponent.Light,
                        PhysicsPreset.Floating => PhysicsComponent.Floating,
                        PhysicsPreset.ZeroG => PhysicsComponent.ZeroG,
                        _ => PhysicsComponent.Normal
                    };
                }
                else
                {
                    // Use custom settings from inspector
                    physicsComponent = new PhysicsComponent(
                        mass: authoring.mass,
                        gravityScale: authoring.gravityScale,
                        useGlobalGravity: !authoring.useCustomGravity,
                        affectedByGravity: authoring.affectedByGravity,
                        terminalVelocity: authoring.terminalVelocity
                    );

                    // Set custom gravity if specified
                    if (authoring.useCustomGravity)
                    {
                        physicsComponent.gravityOverride = authoring.customGravity;
                    }
                }

                // Add the physics component
                AddComponent(entity, physicsComponent);

                // Apply entity type specific physics defaults
                ApplyEntityTypePhysicsDefaults(entity, authoring, physicsComponent);
            }

            /// <summary>
            /// Apply default collision settings based on SimEntity type
            /// </summary>
            private CollisionResponseComponent ApplyDefaultCollisionSettings(CollisionResponseComponent collisionResponse, SimEntityAuthoring authoring)
            {
                // Only override if using default layer (Environment)
                if (authoring.entityLayer != CollisionLayer.Environment) return collisionResponse;

                switch (authoring.simEntityType)
                {
                    case SimEntityType.Player:
                        collisionResponse.entityLayer = CollisionLayer.Player;
                        collisionResponse.collidesWith = CollisionLayer.Enemy | CollisionLayer.Environment |
                                                       CollisionLayer.Projectile | CollisionLayer.Pickup | CollisionLayer.Trigger;
                        // Players typically slide along walls
                        if (authoring.collisionResponse == CollisionResponse.Stop)
                            collisionResponse.responseType = CollisionResponse.Slide;
                        break;

                    case SimEntityType.Enemy:
                        collisionResponse.entityLayer = CollisionLayer.Enemy;
                        collisionResponse.collidesWith = CollisionLayer.Player | CollisionLayer.Environment | CollisionLayer.Projectile;
                        // Enemies typically slide along walls
                        if (authoring.collisionResponse == CollisionResponse.Stop)
                            collisionResponse.responseType = CollisionResponse.Slide;
                        break;

                    case SimEntityType.Projectile:
                        collisionResponse.entityLayer = CollisionLayer.Projectile;
                        collisionResponse.collidesWith = CollisionLayer.Player | CollisionLayer.Enemy | CollisionLayer.Environment;
                        // Projectiles typically get destroyed on collision
                        if (authoring.collisionResponse == CollisionResponse.Stop)
                            collisionResponse.responseType = CollisionResponse.Destroy;
                        break;

                    case SimEntityType.Environment:
                        collisionResponse.entityLayer = CollisionLayer.Environment;
                        collisionResponse.collidesWith = CollisionLayer.All;
                        break;
                }

                return collisionResponse;
            }

            /// <summary>
            /// Apply sensible physics defaults based on entity type
            /// </summary>
            private void ApplyEntityTypePhysicsDefaults(Entity entity, SimEntityAuthoring authoring, PhysicsComponent physicsComponent)
            {
                // Only apply defaults if using Custom preset
                if (authoring.physicsPreset != PhysicsPreset.Custom) return;

                // Modify physics based on entity type
                switch (authoring.simEntityType)
                {
                    case SimEntityType.Player:
                        // Players typically have normal physics
                        if (physicsComponent.mass == (fp)1 && physicsComponent.gravityScale == (fp)1)
                        {
                            physicsComponent.mass = (fp)1.2f; // Slightly heavier for stability
                            physicsComponent.terminalVelocity = (fp)40; // Reasonable fall speed
                        }
                        break;

                    case SimEntityType.Enemy:
                        // Enemies can vary, but default to normal
                        if (physicsComponent.mass == (fp)1 && physicsComponent.gravityScale == (fp)1)
                        {
                            physicsComponent.mass = (fp)1.0f;
                            physicsComponent.terminalVelocity = (fp)45;
                        }
                        break;

                    case SimEntityType.Projectile:
                        // Projectiles are typically light and fast
                        if (physicsComponent.mass == (fp)1 && physicsComponent.gravityScale == (fp)1)
                        {
                            physicsComponent.mass = (fp)0.1f; // Very light
                            physicsComponent.gravityScale = (fp)0.5f; // Less affected by gravity
                            physicsComponent.terminalVelocity = (fp)100; // Can move very fast
                        }
                        break;

                    case SimEntityType.Environment:
                        // Environment entities should not be affected by gravity
                        physicsComponent.affectedByGravity = false;
                        physicsComponent.mass = (fp)999; // Very heavy (won't move anyway)
                        break;
                }

                // Update the component with modified values
                SetComponent(entity, physicsComponent);
            }

            /// <summary>
            /// Add entity type specific components
            /// </summary>
            private void ApplyEntityTypeSpecificComponents(Entity entity, SimEntityAuthoring authoring)
            {
                switch (authoring.simEntityType)
                {
                    case SimEntityType.Projectile:
                        // Projectiles always need collision events
                        AddBuffer<CollisionEventBuffer>(entity);
                        break;

                    case SimEntityType.Environment:
                        // Environment is typically static - only add if not already added
                        if (!authoring.isStaticCollider)
                        {
                            AddComponent(entity, new StaticColliderComponent(true));
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Validate collision and physics settings in the inspector
        /// </summary>
        void OnValidate()
        {
            // Validate collision settings
            if (enableCollision)
            {
                // Ensure collision size is positive
                if (collisionSize.x <= (fp)0) collisionSize.x = (fp)0.1f;
                if (collisionSize.y <= (fp)0) collisionSize.y = (fp)0.1f;
                if (collisionSize.z <= (fp)0) collisionSize.z = (fp)0.1f;

                // Clamp bounciness and friction to valid ranges
                bounciness = fpmath.clamp(bounciness, (fp)0, (fp)1);
                friction = fpmath.clamp(friction, (fp)0, (fp)1);

                // Auto-enable collision events for certain cases
                if (isStaticCollider && entityLayer == CollisionLayer.Trigger)
                {
                    generateCollisionEvents = true;
                }

                if (simEntityType == SimEntityType.Projectile)
                {
                    generateCollisionEvents = true;
                }
            }

            // Validate physics settings
            if (enablePhysics)
            {
                // Ensure mass is positive
                if (mass <= (fp)0) mass = (fp)0.1f;

                // Ensure gravity scale is reasonable
                gravityScale = fpmath.clamp(gravityScale, (fp)(-10), (fp)10);

                // Ensure terminal velocity is non-negative
                if (terminalVelocity < (fp)0) terminalVelocity = (fp)0;

                // Auto-configure based on preset selection
                if (physicsPreset != PhysicsPreset.Custom)
                {
                    ApplyPhysicsPreset();
                }
            }
        }

        /// <summary>
        /// Apply values from selected physics preset
        /// </summary>
        private void ApplyPhysicsPreset()
        {
            var preset = physicsPreset switch
            {
                PhysicsPreset.Normal => PhysicsComponent.Normal,
                PhysicsPreset.Heavy => PhysicsComponent.Heavy,
                PhysicsPreset.Light => PhysicsComponent.Light,
                PhysicsPreset.Floating => PhysicsComponent.Floating,
                PhysicsPreset.ZeroG => PhysicsComponent.ZeroG,
                _ => PhysicsComponent.Normal
            };

            mass = preset.mass;
            gravityScale = preset.gravityScale;
            affectedByGravity = preset.affectedByGravity;
            terminalVelocity = preset.terminalVelocity;
            useCustomGravity = !preset.useGlobalGravity;
        }
    }
}