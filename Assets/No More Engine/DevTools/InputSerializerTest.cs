using NoMoreEngine.Input;
using UnityEngine;


namespace NoMoreEngine.DevTools
{
    /// <summary>
    /// Simple test component to verify Input Serializer is working
    /// Shows input state in inspector instead of cluttering game view
    /// </summary>
    public class InputSerializerTest : MonoBehaviour
    {
        [Header("Input Monitoring")]
        [SerializeField] private bool enableInspectorMonitoring = true;
        [SerializeField] private bool showDebugGUI = false;

        [Header("Current Input State")]
        [SerializeField] private uint currentFrame;
        [SerializeField] private byte motionAxis = 5;
        [SerializeField] private Vector2 motionVector;
        [SerializeField] private short viewAxisX;
        [SerializeField] private short viewAxisY;
        [SerializeField] private Vector2 viewVector;
        [SerializeField] private byte padAxis = 5;
        [SerializeField] private Vector2 padVector;
        [SerializeField] private ushort buttonBits;
        [SerializeField] private string[] pressedButtons = new string[0];

        [Header("Controls")]
        [SerializeField] private InputContext currentContext = InputContext.InGame;

        private InputSerializer inputSerializer;

        void Start()
        {
            inputSerializer = GetComponent<InputSerializer>();
            if (inputSerializer != null)
            {
                // Subscribe to input packets
                inputSerializer.OnInputPacketsReady += OnInputReceived;
                Debug.Log("[InputTest] Subscribed to input packets - monitoring via inspector");
            }
            else
            {
                Debug.LogError("[InputTest] No InputSerializer found!");
            }
        }

        void OnDestroy()
        {
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady -= OnInputReceived;
            }
        }

        void Update()
        {
            // Update inspector display if monitoring is enabled
            if (enableInspectorMonitoring && inputSerializer != null)
            {
                var latestInput = inputSerializer.GetLatestInputForPlayer(0);
                UpdateInspectorDisplay(latestInput);
            }
        }

        private void UpdateInspectorDisplay(InputPacket packet)
        {
            currentFrame = packet.frameNumber;
            motionAxis = packet.motionAxis;
            motionVector = packet.GetMotionVector();
            viewAxisX = packet.viewAxisX;
            viewAxisY = packet.viewAxisY;
            viewVector = packet.GetViewVector();
            padAxis = packet.pad;
            padVector = packet.GetPadVector();
            buttonBits = packet.buttons;

            // Update pressed buttons list
            var buttonList = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 12; i++)
            {
                if (packet.GetButton((InputButton)i))
                {
                    buttonList.Add(((InputButton)i).ToString());
                }
            }
            pressedButtons = buttonList.ToArray();
        }

        private void OnInputReceived(InputPacket[] packets)
        {
            foreach (var packet in packets)
            {
                // Only log if there's actual input (not just idle state)
                if (HasActiveInput(packet))
                {
                    Debug.Log($"[InputTest] Frame {packet.frameNumber}, Player {packet.playerIndex}: " +
                             $"Motion={packet.motionAxis}, View=({packet.viewAxisX},{packet.viewAxisY}), " +
                             $"Pad={packet.pad}, Buttons={packet.buttons:X3}");
                }
            }
        }

        private bool HasActiveInput(InputPacket packet)
        {
            return packet.motionAxis != 5 || // 5 = no input in numpad notation
                   packet.viewAxisX != 0 ||
                   packet.viewAxisY != 0 ||
                   packet.pad != 5 ||
                   packet.buttons != 0;
        }

        // Context switching via inspector buttons
        [ContextMenu("Switch to InGame Context")]
        public void SwitchToInGameContext()
        {
            if (inputSerializer != null)
            {
                currentContext = InputContext.InGame;
                inputSerializer.SetInputContext(InputContext.InGame);
                Debug.Log("[InputTest] Switched to InGame context");
            }
        }

        [ContextMenu("Switch to Menu Context")]
        public void SwitchToMenuContext()
        {
            if (inputSerializer != null)
            {
                currentContext = InputContext.Menu;
                inputSerializer.SetInputContext(InputContext.Menu);
                Debug.Log("[InputTest] Switched to Menu context");
            }
        }

        void OnGUI()
        {
            if (!showDebugGUI || inputSerializer == null) return;

            GUILayout.BeginArea(new Rect(10, 10, 400, 200));
            GUILayout.BeginVertical("box");

            GUILayout.Label("=== Input Serializer Test ===", GUI.skin.box);

            if (GUILayout.Button("Switch to InGame Context"))
            {
                SwitchToInGameContext();
            }
            if (GUILayout.Button("Switch to Menu Context"))
            {
                SwitchToMenuContext();
            }

            GUILayout.Label($"Current Context: {currentContext}");
            GUILayout.Label($"Frame: {currentFrame}");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}