using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using NoMoreEngine.Simulation.Components;


namespace NoMoreEngine.Simulation.Systems
{
    /// <summary>
    /// Core time management system that controls the fixed timestep simulation
    /// Runs early in the frame to accumulate time and trigger simulation steps
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
    public partial class SimulationTimeSystem : SystemBase
    {
        private EntityQuery timeQuery;
        private EntityQuery accumulatorQuery;
        private SimulationStepSystemGroup fixedStepGroup;
        
        // Performance monitoring
        private float lastFpsUpdate = 0f;
        private int frameCount = 0;
        
        protected override void OnCreate()
        {
            // Create queries
            timeQuery = GetEntityQuery(typeof(SimulationTimeComponent));
            accumulatorQuery = GetEntityQuery(typeof(TimeAccumulatorComponent));
            
            // Ensure time singleton exists
            if (timeQuery.IsEmpty)
            {
                CreateTimeSingleton();
            }
            
            // Get reference to fixed step group
            fixedStepGroup = World.GetOrCreateSystemManaged<SimulationStepSystemGroup>();
            
            // Require time component to update
            RequireForUpdate<SimulationTimeComponent>();
            RequireForUpdate<TimeAccumulatorComponent>();
        }
        
        private void CreateTimeSingleton()
        {
            Debug.Log("[SimulationTimeSystem] Creating time singleton entity");
            
            var entity = EntityManager.CreateEntity();
            EntityManager.SetName(entity, "SimulationTime");
            
            // Add time components with default 60Hz configuration
            EntityManager.AddComponentData(entity, SimulationTimeComponent.Create60Hz());
            EntityManager.AddComponentData(entity, TimeAccumulatorComponent.Create60Hz());
        }
        
        protected override void OnUpdate()
        {
            // Get Unity frame time
            float unityDeltaTime = SystemAPI.Time.DeltaTime;
            
            // Get components
            var time = SystemAPI.GetSingletonRW<SimulationTimeComponent>();
            var accumulator = SystemAPI.GetSingletonRW<TimeAccumulatorComponent>();
            
            // Add frame time to accumulator
            accumulator.ValueRW.Accumulate(unityDeltaTime);
            
            // Track steps this frame
            int stepsThisFrame = 0;
            
            // Fixed timestep loop
            while (accumulator.ValueRO.ShouldStep() && 
                   stepsThisFrame < accumulator.ValueRO.maxCatchUpSteps)
            {
                // Advance simulation time
                time.ValueRW.Step();
                
                // Consume the timestep
                accumulator.ValueRW.ConsumeStep();
                
                // Run all simulation systems for this tick
                RunSimulationStep(time.ValueRO.currentTick);
                
                stepsThisFrame++;
            }
            
            // Update stats
            accumulator.ValueRW.stepsLastFrame = stepsThisFrame;
            UpdatePerformanceStats(ref time.ValueRW, unityDeltaTime);
            
            // Log warnings if we're falling behind
            if (stepsThisFrame >= accumulator.ValueRO.maxCatchUpSteps)
            {
                Debug.LogWarning($"[SimulationTimeSystem] Simulation falling behind! " +
                               $"Capped at {stepsThisFrame} steps this frame. " +
                               $"Remaining accumulator: {accumulator.ValueRO.accumulator:F3}");
            }
        }
        
        private void RunSimulationStep(uint currentTick)
        {
            // This is where we'll add rollback checking in the future
            // For now, just run the simulation forward
            
            // Update the fixed step simulation group
            fixedStepGroup.Update();
            
            // Debug log every second
            if (currentTick % 60 == 0)
            {
                var time = SystemAPI.GetSingleton<SimulationTimeComponent>();
                Debug.Log($"[SimulationTimeSystem] Tick {currentTick}, " +
                         $"Elapsed: {time.GetElapsedSeconds():F2}s");
            }
        }
        
        private void UpdatePerformanceStats(ref SimulationTimeComponent time, float unityDeltaTime)
        {
            // Update FPS counter every second
            frameCount++;
            if (UnityEngine.Time.time - lastFpsUpdate >= 1f)
            {
                float fps = frameCount / (UnityEngine.Time.time - lastFpsUpdate);
                float simTickRate = time.ticksThisSecond;
                
                // Reset counters
                time.ticksThisSecond = 0;
                frameCount = 0;
                lastFpsUpdate = UnityEngine.Time.time;
                
                // Log performance
                if (Mathf.Abs(simTickRate - time.tickRate) > 1)
                {
                    Debug.LogWarning($"[SimulationTimeSystem] Tick rate mismatch! " +
                                   $"Target: {time.tickRate}Hz, Actual: {simTickRate}Hz, " +
                                   $"FPS: {fps:F1}");
                }
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
                var accumulator = SystemAPI.GetSingletonRW<TimeAccumulatorComponent>();
                accumulator.ValueRW.Reset();
            }
            
            Debug.Log($"[SimulationTimeSystem] Simulation {(paused ? "paused" : "resumed")}");
        }
        
        /// <summary>
        /// Get current simulation tick (for external systems)
        /// </summary>
        public uint GetCurrentTick()
        {
            if (timeQuery.IsEmpty) return 0;
            var time = SystemAPI.GetSingleton<SimulationTimeComponent>();
            return time.currentTick;
        }
        
        /// <summary>
        /// Reset simulation time (useful for new matches)
        /// </summary>
        public void ResetSimulationTime()
        {
            Debug.Log("[SimulationTimeSystem] Resetting simulation time");
            
            var time = SystemAPI.GetSingletonRW<SimulationTimeComponent>();
            var accumulator = SystemAPI.GetSingletonRW<TimeAccumulatorComponent>();
            
            // Reset time
            time.ValueRW = SimulationTimeComponent.Create60Hz();
            
            // Reset accumulator
            accumulator.ValueRW.Reset();
        }
    }
}