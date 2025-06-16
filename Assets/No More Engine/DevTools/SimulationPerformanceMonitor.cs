using UnityEngine;
using Unity.Entities;
using NoMoreEngine.Simulation.Systems;
using NoMoreEngine.Simulation.Components;
using NoMoreEngine.Input;

namespace NoMoreEngine.DevTools
{
    /// <summary>
    /// Visual performance monitor for simulation tick times
    /// Shows network readiness and performance warnings
    /// </summary>
    public class SimulationPerformanceMonitor : MonoBehaviour
    {
        [Header("Display Settings")]
        [SerializeField] private bool showMonitor = true;
        [SerializeField] private bool showDetailedMetrics = true;
        [SerializeField] private bool showNetworkReadiness = true;
        [SerializeField] private InputButton toggleButton = InputButton.Aux2; // L.Alt/RZ
        
        [Header("Warning Thresholds")]
        [SerializeField] private float warningThresholdMs = 14.0f;
        [SerializeField] private float criticalThresholdMs = 20.0f;
        
        private SimulationTimeSystem timeSystem;
        private InputProcessor inputProcessor;
        private GUIStyle titleStyle;
        private GUIStyle normalStyle;
        private GUIStyle warningStyle;
        private GUIStyle criticalStyle;
        private GUIStyle goodStyle;
        
        // Cache metrics to avoid repeated calls
        private SimulationTimeSystem.PerformanceMetrics lastMetrics;
        private float metricsUpdateInterval = 0.5f;
        private float lastMetricsUpdate;
        
        // Input handling
        private bool shouldToggle = false;
        
        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                timeSystem = world.GetExistingSystemManaged<SimulationTimeSystem>();
            }
            
            // Connect to input system
            inputProcessor = InputProcessor.Instance;
            if (inputProcessor != null)
            {
                inputProcessor.OnInputFramesReady += HandleInput;
            }
            else
            {
                Debug.LogWarning("[PerformanceMonitor] InputProcessor not found - toggle disabled");
            }
            
            InitializeStyles();
        }
        
        void OnDestroy()
        {
            // Unsubscribe from input system
            if (inputProcessor != null)
            {
                inputProcessor.OnInputFramesReady -= HandleInput;
            }
        }
        
        private void HandleInput(PlayerInputFrame[] frames)
        {
            // Check P1 input for toggle
            if (frames.Length > 0)
            {
                var input = frames[0];
                if (input.GetButtonDown(toggleButton))
                {
                    // Set flag to toggle in Update() - can't do GUI operations here
                    shouldToggle = true;
                }
            }
        }
        
        void Update()
        {
            // Process toggle request from input system
            if (shouldToggle)
            {
                showMonitor = !showMonitor;
                shouldToggle = false;
                Debug.Log($"[PerformanceMonitor] Toggled to: {showMonitor}");
            }
            
            // Update cached metrics periodically
            if (Time.time - lastMetricsUpdate > metricsUpdateInterval && timeSystem != null)
            {
                lastMetrics = timeSystem.GetPerformanceMetrics();
                lastMetricsUpdate = Time.time;
            }
        }
        
        void OnGUI()
        {
            if (!showMonitor || timeSystem == null) return;
            
            // Initialize styles if needed (first OnGUI call)
            if (titleStyle == null)
            {
                InitializeStyles();
            }
            
            // Draw background
            GUI.Box(new Rect(Screen.width - 310, 10, 300, showDetailedMetrics ? 280 : 150), "");
            
            GUILayout.BeginArea(new Rect(Screen.width - 305, 15, 290, showDetailedMetrics ? 270 : 140));
            
            // Title
            GUILayout.Label("SIMULATION PERFORMANCE", titleStyle);
            GUILayout.Space(5);
            
            // Network readiness indicator
            if (showNetworkReadiness)
            {
                DrawNetworkReadiness();
                GUILayout.Space(10);
            }
            
            // Basic metrics
            DrawBasicMetrics();
            
            // Detailed metrics
            if (showDetailedMetrics)
            {
                GUILayout.Space(10);
                DrawDetailedMetrics();
            }
            
            // Controls hint
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Press {GetButtonDisplayName(toggleButton)} to toggle", normalStyle);
            
            GUILayout.EndArea();
        }
        
        private void DrawNetworkReadiness()
        {
            bool isReady = lastMetrics.isNetworkReady;
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Network Ready:", normalStyle);
            
            if (isReady)
            {
                GUI.color = Color.green;
                GUILayout.Label("✓ YES", goodStyle);
            }
            else
            {
                GUI.color = Color.red;
                GUILayout.Label("✗ NO", criticalStyle);
            }
            GUI.color = Color.white;
            
            GUILayout.EndHorizontal();
            
            // Show why not ready
            if (!isReady)
            {
                if (lastMetrics.averageTickMs > warningThresholdMs)
                {
                    GUILayout.Label($"  Avg tick too slow: {lastMetrics.averageTickMs:F1}ms", warningStyle);
                }
                if (lastMetrics.worstTickMs > criticalThresholdMs)
                {
                    GUILayout.Label($"  Worst tick too slow: {lastMetrics.worstTickMs:F1}ms", criticalStyle);
                }
            }
        }
        
        private void DrawBasicMetrics()
        {
            // Average tick time
            DrawMetricBar("Avg Tick", (float)lastMetrics.averageTickMs, 0, 20);
            
            // Worst tick time
            DrawMetricBar("Worst Tick", (float)lastMetrics.worstTickMs, 0, 30);
            
            // Slow tick percentage
            float slowPercentage = lastMetrics.totalTicks > 0 
                ? (float)lastMetrics.slowTickCount / lastMetrics.totalTicks * 100 
                : 0;
            DrawMetricBar("Slow Ticks", slowPercentage, 0, 100, "%");
        }
        
        private void DrawDetailedMetrics()
        {
            GUILayout.Label("DETAILED STATS", titleStyle);
            
            // Total ticks
            GUILayout.Label($"Total Ticks: {lastMetrics.totalTicks:N0}", normalStyle);
            
            // Slow tick count
            GUI.color = lastMetrics.slowTickCount > 0 ? Color.yellow : Color.white;
            GUILayout.Label($"Slow Ticks: {lastMetrics.slowTickCount}", normalStyle);
            GUI.color = Color.white;
            
            // Target vs actual
            GUILayout.Space(5);
            GUILayout.Label("TARGET PERFORMANCE", titleStyle);
            DrawTargetComparison("60Hz Target", 16.67f, (float)lastMetrics.averageTickMs);
            DrawTargetComparison("Network Safe", warningThresholdMs, (float)lastMetrics.averageTickMs);
            DrawTargetComparison("Critical", criticalThresholdMs, (float)lastMetrics.worstTickMs);
        }
        
        private void DrawMetricBar(string label, float value, float min, float max, string suffix = "ms")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}:", normalStyle, GUILayout.Width(80));
            
            // Draw bar background
            Rect barRect = GUILayoutUtility.GetRect(150, 20);
            GUI.Box(barRect, "");
            
            // Calculate fill
            float normalized = Mathf.Clamp01((value - min) / (max - min));
            Rect fillRect = new Rect(barRect.x + 2, barRect.y + 2, (barRect.width - 4) * normalized, barRect.height - 4);
            
            // Choose color
            Color barColor = Color.green;
            if (suffix == "ms")
            {
                if (value > criticalThresholdMs) barColor = Color.red;
                else if (value > warningThresholdMs) barColor = Color.yellow;
            }
            else if (suffix == "%")
            {
                if (value > 10) barColor = Color.red;
                else if (value > 5) barColor = Color.yellow;
            }
            
            // Draw fill
            Color oldColor = GUI.color;
            GUI.color = barColor;
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
            GUI.color = oldColor;
            
            // Draw value
            GUILayout.Label($"{value:F1}{suffix}", GetStyleForValue(value, suffix), GUILayout.Width(50));
            
            GUILayout.EndHorizontal();
        }
        
        private void DrawTargetComparison(string label, float target, float actual)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {target:F1}ms", normalStyle, GUILayout.Width(150));
            
            if (actual <= target)
            {
                GUI.color = Color.green;
                GUILayout.Label($"✓ ({actual:F1}ms)", goodStyle);
            }
            else
            {
                GUI.color = Color.red;
                float overBy = ((actual / target) - 1) * 100;
                GUILayout.Label($"✗ (+{overBy:F0}%)", criticalStyle);
            }
            GUI.color = Color.white;
            
            GUILayout.EndHorizontal();
        }
        
        private GUIStyle GetStyleForValue(float value, string suffix)
        {
            if (suffix == "ms")
            {
                if (value > criticalThresholdMs) return criticalStyle;
                if (value > warningThresholdMs) return warningStyle;
                return goodStyle;
            }
            else if (suffix == "%")
            {
                if (value > 10) return criticalStyle;
                if (value > 5) return warningStyle;
                return goodStyle;
            }
            return normalStyle;
        }
        
        private void InitializeStyles()
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            
            normalStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Normal
            };
            
            warningStyle = new GUIStyle(normalStyle);
            warningStyle.normal.textColor = Color.yellow;
            
            criticalStyle = new GUIStyle(normalStyle);
            criticalStyle.normal.textColor = Color.red;
            criticalStyle.fontStyle = FontStyle.Bold;
            
            goodStyle = new GUIStyle(normalStyle);
            goodStyle.normal.textColor = Color.green;
            goodStyle.fontStyle = FontStyle.Bold;
        }
        
        // Context menu actions for testing
        [ContextMenu("Force Log Performance Report")]
        public void LogPerformanceReport()
        {
            timeSystem?.LogPerformanceReport();
        }
        
        [ContextMenu("Simulate Heavy Load")]
        public void SimulateHeavyLoad()
        {
            // Create many entities to stress test
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[PerformanceMonitor] No valid world for load simulation");
                return;
            }
            
            var entityManager = world.EntityManager;
            
            for (int i = 0; i < 100; i++)
            {
                var entity = entityManager.CreateEntity();
                entityManager.AddComponentData(entity, new SimEntityTypeComponent(SimEntityType.Projectile));
                // Add other components as needed
            }
            
            Debug.Log("[PerformanceMonitor] Created 100 test entities to simulate load");
        }
        
        private string GetButtonDisplayName(InputButton button)
        {
            return button switch
            {
                InputButton.Action1 => "A/Space",
                InputButton.Action2 => "B/F",
                InputButton.Action3 => "X/E",
                InputButton.Action4 => "Y/Q",
                InputButton.Action5 => "LB/Shift",
                InputButton.Action6 => "RB/C",
                InputButton.Trigger1 => "RT/LMouse",
                InputButton.Trigger2 => "LT/RMouse",
                InputButton.Aux1 => "LZ/L.Ctrl",
                InputButton.Aux2 => "RZ/L.Alt",
                InputButton.Menu1 => "Menu/Esc",
                InputButton.Menu2 => "View/Tab",
                _ => button.ToString()
            };
        }
    }
}