using System.Net.NetworkInformation;
using NoMoreEngine.Simulation.Snapshot;
using Unity.Entities;


namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Global gravity settings for the simulation world
    /// Singleton component that controls world-wide gravity behavior
    /// </summary>
    public struct GlobalGravityComponent : IComponentData
    {
        public fix3 gravity;            // Base gravity vector (direction and strength)
        public fix gravityScale;        // Global multiplier for all gravity effects
        public bool enabled;            // Master on/off switch for gravity

        public GlobalGravityComponent(fix3 gravity, fix gravityScale = default, bool enabled = true)
        {
            this.gravity = gravity;
            this.gravityScale = gravityScale == default ? (fix)1 : gravityScale;
            this.enabled = enabled;
        }

        /// <summary>
        /// Get the final gravity vector with scale applied
        /// </summary>
        public fix3 FinalGravity => gravity * gravityScale;

        /// <summary>
        /// Standard Earth gravity configuration
        /// </summary>
        public static GlobalGravityComponent EarthGravity =>
            new GlobalGravityComponent(
                new fix3((fix)0, (fix)(-9.81f), (fix)0),
                gravityScale: (fix)1,
                enabled: true
            );

        /// <summary>
        /// Low gravity configuration (like moon)
        /// </summary>
        public static GlobalGravityComponent LowGravity =>
            new GlobalGravityComponent(
                new fix3((fix)0, (fix)(-1.62f), (fix)0),
                gravityScale: (fix)1,
                enabled: true
            );

        /// <summary>
        /// Zero gravity configuration
        /// </summary>
        public static GlobalGravityComponent ZeroGravity =>
            new GlobalGravityComponent(
                fix3.zero,
                gravityScale: (fix)0,
                enabled: false
            );
    }

    /// <summary>
    /// Per-entity physics properties
    /// Controls how individual entities respond to gravity and physics
    /// </summary>

    [Snapshotable(Priority = 2)]
    public struct PhysicsComponent : IComponentData, ISnapshotable<PhysicsComponent>
    {
        public fix mass;                    // Entity mass (affects gravity acceleration)
        public fix gravityScale;            // Individual gravity multiplier
        public fix3 gravityOverride;        // Custom gravity direction/strength
        public bool useGlobalGravity;       // Use world gravity or custom override
        public bool affectedByGravity;      // Master switch for gravity effects
        public fix terminalVelocity;        // Maximum fall speed (0 = no limit)

        public PhysicsComponent(
            fix mass = default,
            fix gravityScale = default,
            bool useGlobalGravity = true,
            bool affectedByGravity = true,
            fix terminalVelocity = default)
        {
            this.mass = mass == default ? (fix)1 : mass;
            this.gravityScale = gravityScale == default ? (fix)1 : gravityScale;
            this.gravityOverride = fix3.zero;
            this.useGlobalGravity = useGlobalGravity;
            this.affectedByGravity = affectedByGravity;
            this.terminalVelocity = terminalVelocity; // 0 = no terminal velocity
        }

        /// <summary>
        /// Calculate final gravity acceleration for this entity
        /// </summary>
        public fix3 CalculateGravityAcceleration(GlobalGravityComponent globalGravity)
        {
            if (!affectedByGravity) return fix3.zero;

            // Choose gravity source
            fix3 baseGravity = useGlobalGravity ? globalGravity.FinalGravity : gravityOverride;

            // Apply entity-specific scaling: a = (g * gravityScale) * mass
            // Note: In real physics, mass cancels out (F=ma, F=mg, so a=g regardless of mass)
            // But for gameplay, we allow mass to affect acceleration
            return baseGravity * gravityScale * mass;
        }

        /// <summary>
        /// Apply terminal velocity limiting to a velocity vector
        /// </summary>
        public fix3 ApplyTerminalVelocity(fix3 velocity)
        {
            if (terminalVelocity <= (fix)0) return velocity; // No limit

            fix currentSpeed = fixMath.length(velocity);
            if (currentSpeed > terminalVelocity)
            {
                return fixMath.normalize(velocity) * terminalVelocity;
            }
            return velocity;
        }

        public int GetSnapshotSize() => System.Runtime.InteropServices.Marshal.SizeOf<PhysicsComponent>();
        public bool ValidateSnapshot() => mass > (fix)0;

        /// <summary>
        /// Preset for normal gameplay entities
        /// </summary>
        public static PhysicsComponent Normal => new PhysicsComponent(
            mass: (fix)1,
            gravityScale: (fix)1,
            useGlobalGravity: true,
            affectedByGravity: true,
            terminalVelocity: (fix)50 // Reasonable terminal velocity
        );

        /// <summary>
        /// Preset for heavy entities (fall faster due to mass)
        /// </summary>
        public static PhysicsComponent Heavy => new PhysicsComponent(
            mass: (fix)3,
            gravityScale: (fix)1,
            useGlobalGravity: true,
            affectedByGravity: true,
            terminalVelocity: (fix)60
        );

        /// <summary>
        /// Preset for light entities (fall slower, lower terminal velocity)
        /// </summary>
        public static PhysicsComponent Light => new PhysicsComponent(
            mass: (fix)0.3f,
            gravityScale: (fix)1,
            useGlobalGravity: true,
            affectedByGravity: true,
            terminalVelocity: (fix)20
        );

        /// <summary>
        /// Preset for floating/flying entities
        /// </summary>
        public static PhysicsComponent Floating => new PhysicsComponent(
            mass: (fix)1,
            gravityScale: (fix)0.1f,
            useGlobalGravity: true,
            affectedByGravity: true,
            terminalVelocity: (fix)10
        );

        /// <summary>
        /// Preset for zero-gravity entities
        /// </summary>
        public static PhysicsComponent ZeroG => new PhysicsComponent(
            mass: (fix)1,
            gravityScale: (fix)0,
            useGlobalGravity: false,
            affectedByGravity: false,
            terminalVelocity: (fix)0
        );


    }
}