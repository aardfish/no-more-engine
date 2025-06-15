using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using NoMoreEngine.Simulation.Components;

namespace NoMoreEngine.Simulation.Systems
{
    /// <summary>
    /// Core time management system that controls the fixed timestep simulation
    /// Fixed to handle structural changes properly
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
    public partial class SimulationTimeSystem : SystemBase
    {
        private SimulationStepSystemGroup fixedStepGroup;
        
        // Performance monitoring
        private float lastFpsUpdate = 0f;
        private int frameCount = 0;
        
        // Track if we've initialized
        private bool isInitialized = false;
        
        protected override void OnCreate()
        {
            // Get reference to fixed step group
            fixedStepGroup = World.GetOrCreateSystemManaged<SimulationStepSystemGroup>();
            
            // Don't create entities here - wait until first update
            // This avoids structural change issues
        }
        
        protected override void OnStartRunning()
        {
            // Ensure singleton exists when system starts running
            EnsureTimeSingleton();
        }
        
        private void EnsureTimeSingleton()
        {
            if (isInitialized) return;
            
            // Check if singleton already exists
            var timeQuery = GetEntityQuery(typeof(SimulationTimeComponent));
            var accumulatorQuery = GetEntityQuery(typeof(TimeAccumulatorComponent));
            
            if (timeQuery.IsEmpty || accumulatorQuery.IsEmpty)
            {
                Debug.Log("[SimulationTimeSystem] Creating time singleton entity");
                
                var entity = EntityManager.CreateEntity();
                EntityManager.SetName(entity, "SimulationTime");
                
                // Add time components with default 60Hz configuration
                EntityManager.AddComponentData(entity, SimulationTimeComponent.Create60Hz());
                EntityManager.AddComponentData(entity, TimeAccumulatorComponent.Create60Hz());
            }
            
            isInitialized = true;
        }
        
        protected override void OnUpdate()
        {
            // Ensure initialization
            if (!isInitialized)
            {
                EnsureTimeSingleton();
                return; // Skip this frame to let structural changes complete
            }
            
            // Get Unity frame time
            float unityDeltaTime = SystemAPI.Time.DeltaTime;
            
            // Use SystemAPI which handles structural changes automatically
            if (!SystemAPI.TryGetSingleton<SimulationTimeComponent>(out var time))
            {
                Debug.LogError("[SimulationTimeSystem] SimulationTimeComponent not found!");
                return;
            }
            
            if (!SystemAPI.TryGetSingleton<TimeAccumulatorComponent>(out var accumulator))
            {
                Debug.LogError("[SimulationTimeSystem] TimeAccumulatorComponent not found!");
                return;
            }
            
            // Add frame time to accumulator
            accumulator.Accumulate(unityDeltaTime);
            
            // Track steps this frame
            int stepsThisFrame = 0;
            
            // Fixed timestep loop
            while (accumulator.ShouldStep() && 
                   stepsThisFrame < accumulator.maxCatchUpSteps)
            {
                // Advance simulation time
                time.Step();
                
                // Consume the timestep
                accumulator.ConsumeStep();
                
                // Run all simulation systems for this tick
                RunSimulationStep(time.currentTick);
                
                stepsThisFrame++;
            }
            
            // Update stats
            accumulator.stepsLastFrame = stepsThisFrame;
            
            // Write back modified components
            SystemAPI.SetSingleton(time);
            SystemAPI.SetSingleton(accumulator);
            
            // Update performance stats
            UpdatePerformanceStats(time.currentTick, unityDeltaTime);
        }
        
        private void RunSimulationStep(uint currentTick)
        {
            // This is where we'll add rollback checking in the future
            // For now, just run the simulation forward
            
            // Update the fixed step simulation group
            fixedStepGroup.Update();
            
            // Debug log every second
            if (currentTick % 60 == 0 && currentTick > 0)
            {
                //Debug.Log($"[SimulationTimeSystem] Tick {currentTick}, " +
                         //$"Elapsed: {currentTick / 60f:F2}s");
            }
        }
        
        private void UpdatePerformanceStats(uint currentTick, float unityDeltaTime)
        {
            // Update FPS counter every second
            frameCount++;
            if (UnityEngine.Time.time - lastFpsUpdate >= 1f)
            {
                float fps = frameCount / (UnityEngine.Time.time - lastFpsUpdate);
                
                // Get current time component to check tick rate
                if (SystemAPI.TryGetSingleton<SimulationTimeComponent>(out var timeComp))
                {
                    float simTickRate = timeComp.ticksThisSecond;
                    
                    // Log performance if there's a mismatch
                    if (Mathf.Abs(simTickRate - timeComp.tickRate) > 1)
                    {
                        Debug.LogWarning($"[SimulationTimeSystem] Tick rate mismatch! " +
                                       $"Target: {timeComp.tickRate}Hz, Actual: {simTickRate}Hz, " +
                                       $"FPS: {fps:F1}");
                    }
                    
                    // Reset tick counter
                    timeComp.ticksThisSecond = 0;
                    SystemAPI.SetSingleton(timeComp);
                }
                
                // Reset counters
                frameCount = 0;
                lastFpsUpdate = UnityEngine.Time.time;
            }
        }
        
        /// <summary>
        /// Public API to pause/unpause simulation
        /// </summary>
        public void SetSimulationPaused(bool paused)
        {
            fixedStepGroup?.SetEnabled(!paused);
            
            if (paused)
            {
                // Reset accumulator when pausing to prevent time buildup
                if (SystemAPI.TryGetSingleton<TimeAccumulatorComponent>(out var accumulator))
                {
                    accumulator.Reset();
                    SystemAPI.SetSingleton(accumulator);
                }
            }
            
            Debug.Log($"[SimulationTimeSystem] Simulation {(paused ? "paused" : "resumed")}");
        }
        
        /// <summary>
        /// Get current simulation tick (for external systems)
        /// </summary>
        public uint GetCurrentTick()
        {
            if (SystemAPI.TryGetSingleton<SimulationTimeComponent>(out var time))
            {
                return time.currentTick;
            }
            return 0;
        }
        
        /// <summary>
        /// Reset simulation time (useful for new matches)
        /// </summary>
        public void ResetSimulationTime()
        {
            Debug.Log("[SimulationTimeSystem] Resetting simulation time");
            
            if (SystemAPI.TryGetSingleton<SimulationTimeComponent>(out var time) &&
                SystemAPI.TryGetSingleton<TimeAccumulatorComponent>(out var accumulator))
            {
                // Reset time
                time = SimulationTimeComponent.Create60Hz();
                SystemAPI.SetSingleton(time);
                
                // Reset accumulator
                accumulator.Reset();
                SystemAPI.SetSingleton(accumulator);
            }
        }
    }
}