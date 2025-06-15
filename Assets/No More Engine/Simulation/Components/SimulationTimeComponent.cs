using Unity.Entities;


namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Core time tracking component for deterministic simulation
    /// Singleton component that manages simulation ticks and timing
    /// </summary>
    public struct SimulationTimeComponent : IComponentData
    {
        // Core timing
        public uint currentTick;           // Deterministic frame counter
        public uint tickRate;              // Ticks per second (default 60)
        public fix deltaTime;              // Fixed timestep (1/tickRate)
        public fix elapsedTime;            // Total simulation time in seconds
        
        // Rollback support (prepare for future implementation)
        public uint lastConfirmedTick;     // Last tick where all inputs were confirmed
        public uint maxRollbackWindow;     // Maximum ticks we can rollback (typically 8-10)
        
        // Performance tracking
        public uint ticksThisSecond;       // For monitoring tick rate
        public float lastTickRateCheck;    // Unity time when we last checked tick rate
        
        /// <summary>
        /// Create a standard 60Hz simulation time configuration
        /// </summary>
        public static SimulationTimeComponent Create60Hz()
        {
            return new SimulationTimeComponent
            {
                currentTick = 0,
                tickRate = 60,
                deltaTime = fix.One / (fix)60,  // 0.01666... seconds
                elapsedTime = fix.Zero,
                lastConfirmedTick = 0,
                maxRollbackWindow = 8,
                ticksThisSecond = 0,
                lastTickRateCheck = 0f
            };
        }
        
        /// <summary>
        /// Create a custom tick rate simulation time
        /// </summary>
        public static SimulationTimeComponent CreateCustom(uint tickRate, uint rollbackWindow = 8)
        {
            return new SimulationTimeComponent
            {
                currentTick = 0,
                tickRate = tickRate,
                deltaTime = fix.One / (fix)tickRate,
                elapsedTime = fix.Zero,
                lastConfirmedTick = 0,
                maxRollbackWindow = rollbackWindow,
                ticksThisSecond = 0,
                lastTickRateCheck = 0f
            };
        }
        
        /// <summary>
        /// Advance the simulation by one tick
        /// </summary>
        public void Step()
        {
            currentTick++;
            elapsedTime += deltaTime;
            ticksThisSecond++;
        }
        
        /// <summary>
        /// Check if a tick is within the valid rollback window
        /// </summary>
        public bool IsTickInRollbackWindow(uint tick)
        {
            if (tick > currentTick) return false;
            return (currentTick - tick) <= maxRollbackWindow;
        }
        
        /// <summary>
        /// Get the current simulation time in seconds (as float for display)
        /// </summary>
        public float GetElapsedSeconds()
        {
            return (float)elapsedTime;
        }
    }
    
    /// <summary>
    /// Component for accumulating Unity time and determining when to step simulation
    /// Handles the fixed timestep loop
    /// </summary>
    public struct TimeAccumulatorComponent : IComponentData
    {
        public float accumulator;          // Accumulated Unity deltaTime
        public float fixedDeltaTime;       // Target timestep in seconds (1/60)
        public int maxCatchUpSteps;        // Max steps per frame to prevent spiral
        public int stepsLastFrame;         // How many steps we took last frame
        
        public static TimeAccumulatorComponent Create60Hz()
        {
            return new TimeAccumulatorComponent
            {
                accumulator = 0f,
                fixedDeltaTime = 1f / 60f,
                maxCatchUpSteps = 3,
                stepsLastFrame = 0
            };
        }
        
        /// <summary>
        /// Check if we have accumulated enough time for a simulation step
        /// </summary>
        public bool ShouldStep()
        {
            return accumulator >= fixedDeltaTime;
        }
        
        /// <summary>
        /// Consume one timestep worth of accumulated time
        /// </summary>
        public void ConsumeStep()
        {
            accumulator -= fixedDeltaTime;
        }
        
        /// <summary>
        /// Add Unity frame time to the accumulator
        /// </summary>
        public void Accumulate(float unityDeltaTime)
        {
            accumulator += unityDeltaTime;
            
            // Clamp to prevent spiral of death
            float maxAccumulation = fixedDeltaTime * maxCatchUpSteps;
            if (accumulator > maxAccumulation)
            {
                accumulator = maxAccumulation;
            }
        }
        
        /// <summary>
        /// Reset accumulator (useful after loading or long pauses)
        /// </summary>
        public void Reset()
        {
            accumulator = 0f;
            stepsLastFrame = 0;
        }
    }
}