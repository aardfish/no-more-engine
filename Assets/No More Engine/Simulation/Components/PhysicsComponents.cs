using System.Net.NetworkInformation;
using NoMoreEngine.Simulation.Snapshot;
using Unity.Entities;
using Unity.Mathematics.FixedPoint;


namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Global gravity settings for the simulation world
    /// Singleton component that controls world-wide gravity behavior
    /// </summary>
    public struct GlobalGravityComponent : IComponentData
    {
        public fp3 gravity;            // Base gravity vector (direction and strength)
        public fp gravityScale;        // Global multiplier for all gravity effects
        public bool enabled;            // Master on/off switch for gravity

        public GlobalGravityComponent(fp3 gravity, fp gravityScale = default, bool enabled = true)
        {
            this.gravity = gravity;
            this.gravityScale = gravityScale == default ? (fp)1 : gravityScale;
            this.enabled = enabled;
        }

        /// <summary>
        /// Get the final gravity vector with scale applied
        /// </summary>
        public fp3 FinalGravity => gravity * gravityScale;

        /// <summary>
        /// Standard Earth gravity configuration
        /// </summary>
        public static GlobalGravityComponent EarthGravity =>
            new GlobalGravityComponent(
                new fp3((fp)0, (fp)(-9.81f), (fp)0),
                gravityScale: (fp)1,
                enabled: true
            );

        /// <summary>
        /// Low gravity configuration (like moon)
        /// </summary>
        public static GlobalGravityComponent LowGravity =>
            new GlobalGravityComponent(
                new fp3((fp)0, (fp)(-1.62f), (fp)0),
                gravityScale: (fp)1,
                enabled: true
            );

        /// <summary>
        /// Zero gravity configuration
        /// </summary>
        public static GlobalGravityComponent ZeroGravity =>
            new GlobalGravityComponent(
                fp3.zero,
                gravityScale: (fp)0,
                enabled: false
            );
    }

    /// <summary>
    /// Per-entity physics properties
    /// Controls how individual entities respond to gravity and physics
    /// </summary>

    [Snapshotable(Priority = 2)]
    public struct PhysicsComponent : IComponentData, ISnapshotableComponent<PhysicsComponent>
    {
        public fp mass;                    // Entity mass (affects gravity acceleration)
        public fp gravityScale;            // Individual gravity multiplier
        public fp3 gravityOverride;        // Custom gravity direction/strength
        public bool useGlobalGravity;       // Use world gravity or custom override
        public bool affectedByGravity;      // Master switch for gravity effects
        public fp terminalVelocity;        // Maximum fall speed (0 = no limit)

        public PhysicsComponent(
            fp mass = default,
            fp gravityScale = default,
            bool useGlobalGravity = true,
            bool affectedByGravity = true,
            fp terminalVelocity = default)
        {
            this.mass = mass == default ? (fp)1 : mass;
            this.gravityScale = gravityScale == default ? (fp)1 : gravityScale;
            this.gravityOverride = fp3.zero;
            this.useGlobalGravity = useGlobalGravity;
            this.affectedByGravity = affectedByGravity;
            this.terminalVelocity = terminalVelocity; // 0 = no terminal velocity
        }

        /// <summary>
        /// Calculate final gravity acceleration for this entity
        /// </summary>
        public fp3 CalculateGravityAcceleration(GlobalGravityComponent globalGravity)
        {
            if (!affectedByGravity) return fp3.zero;

            // Choose gravity source
            fp3 baseGravity = useGlobalGravity ? globalGravity.FinalGravity : gravityOverride;

            // Apply entity-specific scaling: a = (g * gravityScale) * mass
            // Note: In real physics, mass cancels out (F=ma, F=mg, so a=g regardless of mass)
            // But for gameplay, we allow mass to affect acceleration
            return baseGravity * gravityScale * mass;
        }

        /// <summary>
        /// Apply terminal velocity limiting to a velocity vector
        /// </summary>
        public fp3 ApplyTerminalVelocity(fp3 velocity)
        {
            if (terminalVelocity <= (fp)0) return velocity; // No limit

            fp currentSpeed = fpmath.length(velocity);
            if (currentSpeed > terminalVelocity)
            {
                return fpmath.normalize(velocity) * terminalVelocity;
            }
            return velocity;
        }

        public int GetSnapshotSize() => System.Runtime.InteropServices.Marshal.SizeOf<PhysicsComponent>();
        public bool ValidateSnapshot() => mass > (fp)0;

        /// <summary>
        /// Preset for normal gameplay entities
        /// </summary>
        public static PhysicsComponent Normal => new PhysicsComponent(
            mass: (fp)1,
            gravityScale: (fp)1,
            useGlobalGravity: true,
            affectedByGravity: true,
            terminalVelocity: (fp)50 // Reasonable terminal velocity
        );

        /// <summary>
        /// Preset for heavy entities (fall faster due to mass)
        /// </summary>
        public static PhysicsComponent Heavy => new PhysicsComponent(
            mass: (fp)3,
            gravityScale: (fp)1,
            useGlobalGravity: true,
            affectedByGravity: true,
            terminalVelocity: (fp)60
        );

        /// <summary>
        /// Preset for light entities (fall slower, lower terminal velocity)
        /// </summary>
        public static PhysicsComponent Light => new PhysicsComponent(
            mass: (fp)0.3f,
            gravityScale: (fp)1,
            useGlobalGravity: true,
            affectedByGravity: true,
            terminalVelocity: (fp)20
        );

        /// <summary>
        /// Preset for floating/flying entities
        /// </summary>
        public static PhysicsComponent Floating => new PhysicsComponent(
            mass: (fp)1,
            gravityScale: (fp)0.1f,
            useGlobalGravity: true,
            affectedByGravity: true,
            terminalVelocity: (fp)10
        );

        /// <summary>
        /// Preset for zero-gravity entities
        /// </summary>
        public static PhysicsComponent ZeroG => new PhysicsComponent(
            mass: (fp)1,
            gravityScale: (fp)0,
            useGlobalGravity: false,
            affectedByGravity: false,
            terminalVelocity: (fp)0
        );


    }
}