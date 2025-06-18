using Unity.Entities;
using UnityEngine;
using NoMoreEngine.Simulation.Components;
using System.Diagnostics;
using Unity.Mathematics.FixedPoint;

namespace NoMoreEngine.Simulation.Systems
{
    /// <summary>
    /// Enhanced time system with performance monitoring
    /// Keeps grace period functionality while adding metrics
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
    public partial class SimulationTimeSystem : SystemBase
    {
        private SimulationStepSystemGroup fixedStepGroup;

        // Performance monitoring
        private float lastFpsUpdate = 0f;
        private int frameCount = 0;

        // Grace period handling
        private uint gracePeriodEndTick = 0;
        private const uint STARTUP_GRACE_TICKS = 30;
        private const uint RESTORATION_GRACE_TICKS = 30;

        // NEW: Performance metrics
        private Stopwatch tickStopwatch;
        private double worstTickMs = 0;
        private double totalTickMs = 0;
        private int tickCount = 0;
        private int slowTickCount = 0;
        private const double TARGET_TICK_MS = 16.667;
        private const double WARNING_TICK_MS = 14.0;
        private const double CRITICAL_TICK_MS = 20.0;

        // NEW: Network readiness tracking
        [System.Serializable]
        public struct PerformanceMetrics
        {
            public double averageTickMs;
            public double worstTickMs;
            public int slowTickCount;
            public int totalTicks;
            public bool isNetworkReady;

            public void Reset()
            {
                averageTickMs = 0;
                worstTickMs = 0;
                slowTickCount = 0;
                totalTicks = 0;
                isNetworkReady = false;
            }
        }

        private PerformanceMetrics currentMetrics;

        protected override void OnCreate()
        {
            // Get reference to fixed step group
            fixedStepGroup = World.GetOrCreateSystemManaged<SimulationStepSystemGroup>();

            // Set initial grace period for startup
            gracePeriodEndTick = GetCurrentTick() + STARTUP_GRACE_TICKS;

            // Initialize performance tracking
            tickStopwatch = new Stopwatch();
            currentMetrics = new PerformanceMetrics();

            // The time singleton is created by SimulationWorldManager
            // We just need to require it exists
            RequireForUpdate<SimulationTimeComponent>();
        }

        protected override void OnUpdate()
        {
            // Get Unity frame time
            float unityDeltaTime = SystemAPI.Time.DeltaTime;

            // Use SystemAPI which handles structural changes automatically
            if (!SystemAPI.TryGetSingleton<SimulationTimeComponent>(out var time))
            {
                UnityEngine.Debug.LogError("[SimulationTimeSystem] SimulationTimeComponent not found!");
                return;
            }

            if (!SystemAPI.TryGetSingleton<TimeAccumulatorComponent>(out var accumulator))
            {
                UnityEngine.Debug.LogError("[SimulationTimeSystem] TimeAccumulatorComponent not found!");
                return;
            }

            // Check if we're in a grace period
            bool inGracePeriod = time.currentTick < gracePeriodEndTick;

            // During grace period, limit time accumulation to prevent catch-up spiral
            if (inGracePeriod)
            {
                // Clamp delta time to prevent large accumulation
                unityDeltaTime = Mathf.Min(unityDeltaTime, accumulator.fixedDeltaTime * 1.5f);
            }

            // Add frame time to accumulator
            accumulator.Accumulate(unityDeltaTime);

            // Track steps this frame
            int stepsThisFrame = 0;

            // Fixed timestep loop
            while (accumulator.ShouldStep() &&
                   stepsThisFrame < accumulator.maxCatchUpSteps)
            {
                // NEW: Start performance measurement
                tickStopwatch.Restart();

                // Advance simulation time
                time.Step();

                // Consume the timestep
                accumulator.ConsumeStep();

                // Run all simulation systems for this tick
                RunSimulationStep(time.currentTick);

                // NEW: End performance measurement
                tickStopwatch.Stop();
                MeasureTickPerformance(time.currentTick, tickStopwatch.Elapsed.TotalMilliseconds, inGracePeriod);

                stepsThisFrame++;
            }

            // Update stats
            accumulator.stepsLastFrame = stepsThisFrame;

            // Write back modified components
            SystemAPI.SetSingleton(time);
            SystemAPI.SetSingleton(accumulator);

            // Update performance stats (suppress warnings during grace period)
            UpdatePerformanceStats(time.currentTick, unityDeltaTime, !inGracePeriod);
        }

        public event System.Action<uint> OnSimulationTick;

        private void RunSimulationStep(uint currentTick)
        {
            // This is where we'll add rollback checking in the future
            // For now, just run the simulation forward

            // Notify any MonoBehaviours that need to sync
            OnSimulationTick?.Invoke(currentTick);

            // Update the fixed step simulation group
            fixedStepGroup.Update();

            // Debug log every second
            if (currentTick % 60 == 0 && currentTick > 0)
            {
                // NEW: Log performance summary
                if (currentMetrics.totalTicks > 0)
                {
                    //UnityEngine.Debug.Log($"[SimulationTimeSystem] Tick {currentTick}, " +
                    //$"Avg: {currentMetrics.averageTickMs:F2}ms, " +
                    //$"Worst: {currentMetrics.worstTickMs:F2}ms, " +
                    //$"Slow: {currentMetrics.slowTickCount}/{currentMetrics.totalTicks}");
                }
            }
        }

        // NEW: Measure individual tick performance
        private void MeasureTickPerformance(uint tick, double tickMs, bool inGracePeriod)
        {
            // Update metrics
            tickCount++;
            totalTickMs += tickMs;

            if (tickMs > worstTickMs)
            {
                worstTickMs = tickMs;
            }

            // Track slow ticks
            if (tickMs > WARNING_TICK_MS)
            {
                slowTickCount++;

                // Log warnings for slow ticks (unless in grace period)
                if (!inGracePeriod)
                {
                    if (tickMs > CRITICAL_TICK_MS)
                    {
                        UnityEngine.Debug.LogError($"[SimulationTimeSystem] CRITICAL: Tick {tick} took {tickMs:F2}ms!");
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[SimulationTimeSystem] Slow tick {tick}: {tickMs:F2}ms");
                    }
                }
            }

            // Update current metrics
            currentMetrics.totalTicks = tickCount;
            currentMetrics.averageTickMs = totalTickMs / tickCount;
            currentMetrics.worstTickMs = worstTickMs;
            currentMetrics.slowTickCount = slowTickCount;
            currentMetrics.isNetworkReady = currentMetrics.averageTickMs < WARNING_TICK_MS &&
                                          currentMetrics.worstTickMs < CRITICAL_TICK_MS;
        }

        private void UpdatePerformanceStats(uint currentTick, float unityDeltaTime, bool showWarnings)
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

                    // Log performance if there's a mismatch (only if not in grace period)
                    if (showWarnings && Mathf.Abs(simTickRate - timeComp.tickRate) > 1)
                    {
                        UnityEngine.Debug.LogWarning($"[SimulationTimeSystem] Tick rate mismatch! " +
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

            UnityEngine.Debug.Log($"[SimulationTimeSystem] Simulation {(paused ? "paused" : "resumed")}");
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
        /// Restore simulation time to a specific tick (for snapshot restoration)
        /// </summary>
        public void RestoreToTick(uint tick)
        {
            UnityEngine.Debug.Log($"[SimulationTimeSystem] Restoring time to tick {tick}");

            if (SystemAPI.TryGetSingleton<SimulationTimeComponent>(out var time) &&
                SystemAPI.TryGetSingleton<TimeAccumulatorComponent>(out var accumulator))
            {
                // Update time to restored tick
                time.currentTick = tick;
                time.lastConfirmedTick = tick > 0 ? tick - 1 : 0;
                time.elapsedTime = (fp)(tick / 60f); // Assuming 60Hz
                time.ticksThisSecond = 0; // Reset tick counter

                // Reset accumulator to prevent catch-up
                accumulator.Reset();

                // Write back modified components
                SystemAPI.SetSingleton(time);
                SystemAPI.SetSingleton(accumulator);

                // Set grace period to prevent catch-up issues
                SetGracePeriod(RESTORATION_GRACE_TICKS);

                // Reset performance metrics
                currentMetrics.Reset();
                tickCount = 0;
                totalTickMs = 0;
                worstTickMs = 0;
                slowTickCount = 0;
            }
            else
            {
                UnityEngine.Debug.LogError("[SimulationTimeSystem] Failed to restore time - components not found!");
            }
        }

        /// <summary>
        /// Reset simulation time (useful for new matches)
        /// </summary>
        public void ResetSimulationTime()
        {
            UnityEngine.Debug.Log("[SimulationTimeSystem] Resetting simulation time");

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

            // NEW: Reset performance metrics
            currentMetrics.Reset();
            tickCount = 0;
            totalTickMs = 0;
            worstTickMs = 0;
            slowTickCount = 0;
        }

        /// <summary>
        /// Set a grace period after heavy operations like snapshot restoration
        /// </summary>
        public void SetGracePeriod(uint duration)
        {
            gracePeriodEndTick = GetCurrentTick() + duration;

            // Also reset accumulator to prevent catch-up
            if (SystemAPI.TryGetSingleton<TimeAccumulatorComponent>(out var accumulator))
            {
                accumulator.Reset();
                SystemAPI.SetSingleton(accumulator);
            }

            UnityEngine.Debug.Log($"[SimulationTimeSystem] Grace period set for {duration}s");
        }

        // NEW: Public API for performance monitoring
        public PerformanceMetrics GetPerformanceMetrics() => currentMetrics;

        public bool IsNetworkReady() => currentMetrics.isNetworkReady;

        public void LogPerformanceReport()
        {
            UnityEngine.Debug.Log($"[SimulationTimeSystem] Performance Report:\n" +
                $"  Total Ticks: {currentMetrics.totalTicks}\n" +
                $"  Average Tick: {currentMetrics.averageTickMs:F2}ms\n" +
                $"  Worst Tick: {currentMetrics.worstTickMs:F2}ms\n" +
                $"  Slow Ticks: {currentMetrics.slowTickCount} ({(float)currentMetrics.slowTickCount / currentMetrics.totalTicks * 100:F1}%)\n" +
                $"  Network Ready: {currentMetrics.isNetworkReady}");
        }
    }
}