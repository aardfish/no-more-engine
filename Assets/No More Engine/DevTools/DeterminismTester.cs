using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;
using NoMoreEngine.Input;
using NoMoreEngine.Simulation.Snapshot;
using NoMoreEngine.Simulation.Components;
using NoMoreEngine.Simulation.Systems;
using NoMoreEngine.Session;

namespace NoMoreEngine.DevTools
{
    /// <summary>
    /// Advanced determinism tester using InputRecorder and SnapshotSystem
    /// FIXED: Now properly integrates with session flow instead of trying to control simulation directly
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
        private SessionCoordinator sessionCoordinator;
        
        // Test state
        private DeterminismTest currentTest;
        private List<TestResult> testResults = new List<TestResult>();
        private bool waitingForSimulation = false;
        private int currentRunNumber = 0;
        
        // Events
        public event System.Action<DeterminismTest> OnTestStarted;
        public event System.Action<TestResult> OnTestCompleted;
        
        void Awake()
        {
            recorder = InputRecorder.Instance;
            sessionCoordinator = SessionCoordinator.Instance;
            
            // Create replayer if needed
            replayer = GetComponent<InputReplayer>();
            if (replayer == null)
            {
                replayer = gameObject.AddComponent<InputReplayer>();
            }
        }
        
        void Start()
        {
            // Subscribe to session state changes
            if (sessionCoordinator != null)
            {
                sessionCoordinator.OnStateChanged += OnSessionStateChanged;
            }
        }
        
        void OnDestroy()
        {
            if (sessionCoordinator != null)
            {
                sessionCoordinator.OnStateChanged -= OnSessionStateChanged;
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
            currentRunNumber = 1;
            StartRunThroughSession();
            
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
        
        private void StartRunThroughSession()
        {
            Debug.Log($"[DeterminismTester] Starting run {currentRunNumber} through session flow");
            
            // Set up context for replay
            var context = sessionCoordinator.GetState<ReplayModeState>()?.GetContext();
            if (context != null)
            {
                context.isReplayActive = true;
                context.replayToPlay = currentTest.recording;
                context.captureForDeterminism = true;
                
                // Mark that we're waiting for simulation to start
                waitingForSimulation = true;
                
                // Transition to InGame
                sessionCoordinator.TransitionTo(SessionStateType.InGame);
            }
            else
            {
                Debug.LogError("[DeterminismTester] Could not get session context!");
            }
        }
        
        private void OnSessionStateChanged(SessionStateType from, SessionStateType to)
        {
            // When we transition to InGame and we're waiting, set up the test
            if (to == SessionStateType.InGame && waitingForSimulation)
            {
                waitingForSimulation = false;
                
                // Small delay to ensure simulation is fully initialized
                Invoke(nameof(SetupTestRun), 0.5f);
            }
            
            // When we transition from InGame back to Results/ReplayMode, check if we need another run
            if (from == SessionStateType.InGame && (to == SessionStateType.Results || to == SessionStateType.ReplayMode))
            {
                if (currentTest != null && currentRunNumber == 1 && currentTest.testType == TestType.TwiceReplay)
                {
                    // First run complete, start second run
                    Debug.Log("[DeterminismTester] First run complete, starting second run...");
                    currentRunNumber = 2;
                    
                    // Small delay before starting next run
                    Invoke(nameof(StartRunThroughSession), 1f);
                }
                else if (currentTest != null)
                {
                    // Test complete
                    CompleteTest();
                }
            }
        }
        
        private void SetupTestRun()
        {
            Debug.Log($"[DeterminismTester] Setting up test for run {currentRunNumber}");
            
            // Get system references from the now-initialized world
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                snapshotSystem = world.GetExistingSystemManaged<SnapshotSystem>();
                timeSystem = world.GetExistingSystemManaged<SimulationTimeSystem>();
            }
            
            // Configure snapshot capture
            if (snapshotSystem != null)
            {
                snapshotSystem.SetAutoSnapshot(true);
                snapshotSystem.SetSnapshotInterval((uint)snapshotInterval);
            }
            
            // Initialize snapshot storage for this run
            if (currentRunNumber == 1)
            {
                currentTest.run1Snapshots = new Dictionary<uint, SnapshotInfo>();
            }
            else
            {
                currentTest.run2Snapshots = new Dictionary<uint, SnapshotInfo>();
            }
            
            // Start capturing snapshots
            InvokeRepeating(nameof(CaptureSnapshot), 0.5f, 1f);
        }
        
        private void CaptureSnapshot()
        {
            // Check if replay is still running
            var inGameState = sessionCoordinator.GetState<InGameState>();
            if (inGameState == null || sessionCoordinator.GetCurrentStateType() != SessionStateType.InGame)
            {
                CancelInvoke(nameof(CaptureSnapshot));
                return;
            }
            
            if (snapshotSystem == null || timeSystem == null)
            {
                return;
            }
            
            uint currentTick = timeSystem.GetCurrentTick();
            var info = snapshotSystem.GetSnapshotInfo(currentTick);
            
            if (currentRunNumber == 1)
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
        
        private void CompleteTest()
        {
            CancelInvoke(nameof(CaptureSnapshot));
            
            currentTest.endTime = Time.time;
            
            // Generate result
            var result = new TestResult
            {
                recording = currentTest.recording,
                testType = currentTest.testType,
                passed = currentTest.divergences.Count == 0,
                divergenceCount = currentTest.divergences.Count,
                duration = currentTest.endTime - currentTest.startTime,
                ticksTested = currentTest.run1Snapshots?.Count ?? 0
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
            currentRunNumber = 0;
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