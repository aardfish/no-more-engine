using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;
using NoMoreEngine.Input;
using NoMoreEngine.Simulation.Snapshot;
using NoMoreEngine.Simulation.Components;
using NoMoreEngine.Simulation.Systems;

namespace NoMoreEngine.DevTools
{
    /// <summary>
    /// Advanced determinism tester using InputRecorder and SnapshotSystem
    /// </summary>
    public class DeterminismTester : MonoBehaviour
    {
        // Test configuration
        [SerializeField] private int snapshotInterval = 60; // Every second at 60fps
        [SerializeField] private bool verboseLogging = false;
        
        // Systems
        private InputRecorder recorder;
        private InputReplayer replayer;
        private SnapshotSystem snapshotSystem;
        private SimulationTimeSystem timeSystem;
        
        // Test state
        private DeterminismTest currentTest;
        private List<TestResult> testResults = new List<TestResult>();
        
        // Events
        public event System.Action<DeterminismTest> OnTestStarted;
        public event System.Action<TestResult> OnTestCompleted;
        
        void Awake()
        {
            recorder = InputRecorder.Instance;
            
            // Create replayer if needed
            replayer = GetComponent<InputReplayer>();
            if (replayer == null)
            {
                replayer = gameObject.AddComponent<InputReplayer>();
            }
        }
        
        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                snapshotSystem = world.GetExistingSystemManaged<SnapshotSystem>();
                timeSystem = world.GetExistingSystemManaged<SimulationTimeSystem>();
            }
        }
        
        /// <summary>
        /// Start determinism test - play replay twice and compare
        /// </summary>
        public void StartDeterminismTest(InputRecording recording)
        {
            currentTest = new DeterminismTest
            {
                recording = recording,
                testType = TestType.TwiceReplay,
                startTime = Time.time
            };
            
            Debug.Log($"[DeterminismTester] Starting determinism test with {recording.FrameCount} frames");
            
            // Start first run
            StartRun(1);
            
            OnTestStarted?.Invoke(currentTest);
        }
        
        /// <summary>
        /// Start comparison test - play once live, once replay
        /// </summary>
        public void StartComparisonTest(InputRecording recording)
        {
            currentTest = new DeterminismTest
            {
                recording = recording,
                testType = TestType.LiveVsReplay,
                startTime = Time.time
            };
            
            Debug.Log("[DeterminismTester] Starting live vs replay comparison test");
            
            // First run will be live play
            OnTestStarted?.Invoke(currentTest);
        }
        
        private void StartRun(int runNumber)
        {
            Debug.Log($"[DeterminismTester] Starting run {runNumber}");
            
            // Reset simulation to clean state
            ResetSimulation();
            
            // Configure snapshot capture
            if (snapshotSystem != null)
            {
                snapshotSystem.SetAutoSnapshot(true);
                snapshotSystem.SetSnapshotInterval((uint)snapshotInterval);
            }
            
            // Start replay
            replayer.StartReplay(currentTest.recording);
            
            // Subscribe to capture snapshots
            if (runNumber == 1)
            {
                currentTest.run1Snapshots = new Dictionary<uint, SnapshotInfo>();
            }
            else
            {
                currentTest.run2Snapshots = new Dictionary<uint, SnapshotInfo>();
            }
            
            // Start capturing
            InvokeRepeating(nameof(CaptureSnapshot), 0.5f, 1f);
        }
        
        private void CaptureSnapshot()
        {
            if (snapshotSystem == null || !replayer.IsReplaying)
            {
                CancelInvoke(nameof(CaptureSnapshot));
                return;
            }
            
            uint currentTick = timeSystem?.GetCurrentTick() ?? 0;
            var info = snapshotSystem.GetSnapshotInfo(currentTick);
            
            if (currentTest.run2Snapshots == null)
            {
                // First run
                currentTest.run1Snapshots[currentTick] = info;
            }
            else
            {
                // Second run
                currentTest.run2Snapshots[currentTick] = info;
                
                // Compare immediately
                if (currentTest.run1Snapshots.TryGetValue(currentTick, out var run1Info))
                {
                    CompareSnapshots(currentTick, run1Info, info);
                }
            }
        }
        
        private void CompareSnapshots(uint tick, SnapshotInfo snapshot1, SnapshotInfo snapshot2)
        {
            if (snapshot1.hash != snapshot2.hash)
            {
                var divergence = new DeterminismDivergence
                {
                    tick = tick,
                    run1Hash = snapshot1.hash,
                    run2Hash = snapshot2.hash,
                    entityCountDiff = snapshot1.entityCount - snapshot2.entityCount
                };
                
                currentTest.divergences.Add(divergence);
                
                if (verboseLogging)
                {
                    Debug.LogError($"[DeterminismTester] DIVERGENCE at tick {tick}! " +
                                 $"Hash1: {snapshot1.hash:X16}, Hash2: {snapshot2.hash:X16}");
                }
            }
            else if (verboseLogging)
            {
                Debug.Log($"[DeterminismTester] Tick {tick} matches perfectly");
            }
        }
        
        public void OnReplayFinished()
        {
            CancelInvoke(nameof(CaptureSnapshot));
            
            if (currentTest.run2Snapshots == null && currentTest.testType == TestType.TwiceReplay)
            {
                // First run complete, start second run
                Debug.Log("[DeterminismTester] First run complete, starting second run...");
                
                // Small delay to let systems settle
                Invoke(nameof(StartSecondRun), 0.5f);
            }
            else
            {
                // Test complete
                CompleteTest();
            }
        }
        
        private void StartSecondRun()
        {
            StartRun(2);
        }
        
        private void CompleteTest()
        {
            currentTest.endTime = Time.time;
            
            // Generate result
            var result = new TestResult
            {
                recording = currentTest.recording,
                testType = currentTest.testType,
                passed = currentTest.divergences.Count == 0,
                divergenceCount = currentTest.divergences.Count,
                duration = currentTest.endTime - currentTest.startTime,
                ticksTested = currentTest.run1Snapshots.Count
            };
            
            testResults.Add(result);
            
            Debug.Log($"[DeterminismTester] Test complete! Result: {(result.passed ? "PASSED" : "FAILED")}");
            if (!result.passed)
            {
                Debug.LogError($"[DeterminismTester] Found {result.divergenceCount} divergences");
            }
            
            OnTestCompleted?.Invoke(result);
            
            // Clean up
            currentTest = null;
        }
        
        private void ResetSimulation()
        {
            Debug.Log("[DeterminismTester] Resetting simulation...");
            
            // Reset time
            timeSystem?.ResetSimulationTime();
            
            // Clear all entities
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                var entityManager = world.EntityManager;
                entityManager.DestroyEntity(entityManager.UniversalQuery);
            }
            
            // TODO: Properly reset to clean state
        }
        
        // Test data structures
        public class DeterminismTest
        {
            public InputRecording recording;
            public TestType testType;
            public float startTime;
            public float endTime;
            public Dictionary<uint, SnapshotInfo> run1Snapshots;
            public Dictionary<uint, SnapshotInfo> run2Snapshots;
            public List<DeterminismDivergence> divergences = new List<DeterminismDivergence>();
        }
        
        public struct DeterminismDivergence
        {
            public uint tick;
            public ulong run1Hash;
            public ulong run2Hash;
            public int entityCountDiff;
        }
        
        public struct TestResult
        {
            public InputRecording recording;
            public TestType testType;
            public bool passed;
            public int divergenceCount;
            public float duration;
            public int ticksTested;
        }
        
        public enum TestType
        {
            TwiceReplay,    // Play same replay twice
            LiveVsReplay,   // Compare live play vs replay
        }
    }
}