using UnityEngine;
using System.Collections.Generic;
using NoMoreEngine.Input;
using NoMoreEngine.Simulation.Systems;
using Unity.Entities;

namespace NoMoreEngine.DevTools
{
    /// <summary>
    /// Fighting game style input history display
    /// Shows inputs per simulation tick with button states and directional inputs
    /// </summary>
    public class InputHistoryDisplay : MonoBehaviour
    {
        [Header("Display Settings")]
        [SerializeField] private bool showDisplay = true;
        [SerializeField] private int maxHistoryLines = 20;
        [SerializeField] private float displayWidth = 400f;
        [SerializeField] private InputButton toggleKey = InputButton.Aux2;
        
        [Header("Display Options")]
        [SerializeField] private bool showTickNumber = true;
        [SerializeField] private bool showMotionNumpad = true;
        [SerializeField] private bool showButtonNames = true;
        [SerializeField] private bool showRawValues = false;
        [SerializeField] private Color activeInputColor = Color.cyan;
        [SerializeField] private Color inactiveColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        
        // Input tracking
        private struct InputHistoryEntry
        {
            public uint tick;
            public byte playerIndex;
            public InputPacket packet;
            public float timestamp;
        }
        
        private Queue<InputHistoryEntry> inputHistory = new Queue<InputHistoryEntry>();
        private InputSerializer inputSerializer;
        private SimulationTimeSystem timeSystem;
        
        // Display
        private GUIStyle boxStyle;
        private GUIStyle headerStyle;
        private GUIStyle tickStyle;
        private GUIStyle inputStyle;
        private GUIStyle buttonStyle;
        private bool stylesInitialized = false;
        
        // Button display mapping
        private readonly string[] buttonSymbols = new string[]
        {
            "A", "B", "X", "Y",      // Action 1-4
            "LB", "RB",              // Action 5-6
            "LT", "RT",              // Trigger 2,1
            "LS", "RS",              // Aux 1-2
            "M", "V"                 // Menu 1-2
        };
        
        // Directional display (numpad notation)
        private readonly string[] directionSymbols = new string[]
        {
            "⬋", "⬇", "⬊",  // 1,2,3
            "⬅", "●", "➡",  // 4,5,6
            "⬉", "⬆", "⬈"   // 7,8,9
        };

        void Start()
        {
            // Find InputSerializer
            inputSerializer = FindAnyObjectByType<InputSerializer>();
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady += OnInputPacketsReceived;
                Debug.Log("[InputHistoryDisplay] Connected to InputSerializer");
            }
            
            // Get time system reference
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                timeSystem = world.GetExistingSystemManaged<SimulationTimeSystem>();
            }
        }
        
        void OnDestroy()
        {
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady -= OnInputPacketsReceived;
            }
        }
        
        void Update()
        {
            // Toggle display
            if (NoMoreInput.GetButtonDown(toggleKey))
            {
                showDisplay = !showDisplay;
            }
        }
        
        private void OnInputPacketsReceived(InputPacket[] packets)
        {
            uint currentTick = timeSystem?.GetCurrentTick() ?? 0;
            
            foreach (var packet in packets)
            {
                var entry = new InputHistoryEntry
                {
                    tick = currentTick,
                    playerIndex = packet.playerIndex,
                    packet = packet,
                    timestamp = Time.time
                };
                
                inputHistory.Enqueue(entry);
                
                // Limit history size
                while (inputHistory.Count > maxHistoryLines)
                {
                    inputHistory.Dequeue();
                }
            }
        }
        
        void OnGUI()
        {
            if (!showDisplay) return;
            
            if (!stylesInitialized)
            {
                InitializeStyles();
            }
            
            // Calculate position (right side of screen)
            float x = Screen.width - displayWidth - 10;
            float y = 10;
            float height = maxHistoryLines * 25 + 60;
            
            // Background
            GUI.Box(new Rect(x - 5, y - 5, displayWidth + 10, height + 10), "", boxStyle);
            
            GUILayout.BeginArea(new Rect(x, y, displayWidth, height));
            
            // Header
            GUILayout.Label($"INPUT HISTORY (F1 to toggle)", headerStyle);
            GUILayout.Space(5);
            
            // Column headers
            GUILayout.BeginHorizontal();
            if (showTickNumber)
                GUILayout.Label("TICK", tickStyle, GUILayout.Width(50));
            GUILayout.Label("P#", tickStyle, GUILayout.Width(25));
            GUILayout.Label("DIR", tickStyle, GUILayout.Width(40));
            GUILayout.Label("BUTTONS", tickStyle);
            GUILayout.EndHorizontal();
            
            // Draw history (newest at top)
            var entries = new List<InputHistoryEntry>(inputHistory);
            entries.Reverse();
            
            foreach (var entry in entries)
            {
                DrawInputLine(entry);
            }
            
            GUILayout.EndArea();
            
            // Legend at bottom
            if (showButtonNames)
            {
                DrawLegend(x, y + height + 15);
            }
        }
        
        private void DrawInputLine(InputHistoryEntry entry)
        {
            GUILayout.BeginHorizontal();
            
            // Tick number
            if (showTickNumber)
            {
                GUI.color = Color.gray;
                GUILayout.Label(entry.tick.ToString(), tickStyle, GUILayout.Width(50));
            }
            
            // Player index
            GUI.color = GetPlayerColor(entry.playerIndex);
            GUILayout.Label($"P{entry.playerIndex + 1}", tickStyle, GUILayout.Width(25));
            
            // Direction
            GUI.color = entry.packet.motionAxis != 5 ? activeInputColor : inactiveColor;
            string direction = GetDirectionDisplay(entry.packet.motionAxis);
            GUILayout.Label(direction, inputStyle, GUILayout.Width(40));
            
            // Buttons
            string buttonDisplay = GetButtonDisplay(entry.packet);
            GUI.color = entry.packet.buttons != 0 ? activeInputColor : inactiveColor;
            GUILayout.Label(buttonDisplay, buttonStyle);
            
            // Raw values (debug)
            if (showRawValues)
            {
                GUI.color = Color.gray;
                GUILayout.Label($"[{entry.packet.buttons:X3}]", tickStyle);
            }
            
            GUILayout.EndHorizontal();
            
            GUI.color = Color.white;
        }
        
        private string GetDirectionDisplay(byte motionAxis)
        {
            if (showMotionNumpad)
            {
                return motionAxis.ToString();
            }
            else
            {
                return motionAxis >= 1 && motionAxis <= 9 
                    ? directionSymbols[motionAxis - 1] 
                    : "●";
            }
        }
        
        private string GetButtonDisplay(InputPacket packet)
        {
            if (packet.buttons == 0)
                return "-";
            
            var pressed = new List<string>();
            
            for (int i = 0; i < 12; i++)
            {
                if (packet.GetButton((InputButton)i))
                {
                    pressed.Add(buttonSymbols[i]);
                }
            }
            
            return string.Join("+", pressed);
        }
        
        private void DrawLegend(float x, float y)
        {
            GUI.color = Color.gray;
            GUILayout.BeginArea(new Rect(x, y, displayWidth, 60));
            
            GUILayout.Label("LEGEND:", tickStyle);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("A/B/X/Y = Action1-4", tickStyle);
            GUILayout.Label("LB/RB = Action5-6", tickStyle);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("LT/RT = Trigger2/1", tickStyle);
            GUILayout.Label("LS/RS = Aux1-2", tickStyle);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("M/V = Menu1-2", tickStyle);
            GUILayout.Label("Dir: 5=Neutral", tickStyle);
            GUILayout.EndHorizontal();
            
            GUILayout.EndArea();
            GUI.color = Color.white;
        }
        
        private Color GetPlayerColor(byte playerIndex)
        {
            return playerIndex switch
            {
                0 => Color.cyan,
                1 => Color.red,
                2 => Color.green,
                3 => Color.yellow,
                _ => Color.white
            };
        }
        
        private void InitializeStyles()
        {
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = Texture2D.whiteTexture;
            
            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 14;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.alignment = TextAnchor.MiddleCenter;
            
            tickStyle = new GUIStyle(GUI.skin.label);
            tickStyle.fontSize = 10;
            tickStyle.fontStyle = FontStyle.Bold;
            
            inputStyle = new GUIStyle(GUI.skin.label);
            inputStyle.fontSize = 16;
            inputStyle.fontStyle = FontStyle.Bold;
            inputStyle.alignment = TextAnchor.MiddleCenter;
            
            buttonStyle = new GUIStyle(GUI.skin.label);
            buttonStyle.fontSize = 12;
            buttonStyle.fontStyle = FontStyle.Bold;
            
            stylesInitialized = true;
        }
    }
}