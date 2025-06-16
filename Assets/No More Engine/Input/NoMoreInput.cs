using UnityEngine;

namespace NoMoreEngine.Input
{
    /// <summary>
    /// NoMoreInput - Universal input API for No More Engine
    /// Provides consistent input access for both menu and gameplay contexts
    /// </summary>
    public static class NoMoreInput
    {
        // Player-specific accessors for convenience
        public static PlayerInput Player1 => new PlayerInput(0);
        public static PlayerInput Player2 => new PlayerInput(1);
        public static PlayerInput Player3 => new PlayerInput(2);
        public static PlayerInput Player4 => new PlayerInput(3);

        /// <summary>
        /// Get input for a specific player by index
        /// </summary>
        public static PlayerInput GetPlayer(byte playerIndex)
        {
            return new PlayerInput(playerIndex);
        }

        /// <summary>
        /// Check if any player pressed a button
        /// </summary>
        public static bool AnyButtonDown(InputButton button)
        {
            for (byte i = 0; i < 4; i++)
            {
                if (GetButtonDown(button, i))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Check if any player is holding a button
        /// </summary>
        public static bool AnyButton(InputButton button)
        {
            for (byte i = 0; i < 4; i++)
            {
                if (GetButton(button, i))
                    return true;
            }
            return false;
        }

        // Direct access methods
        public static bool GetButton(InputButton button, byte playerIndex = 0)
        {
            var processor = InputProcessor.Instance;
            return processor != null && processor.GetButton(playerIndex, button);
        }

        public static bool GetButtonDown(InputButton button, byte playerIndex = 0)
        {
            var processor = InputProcessor.Instance;
            return processor != null && processor.GetButtonDown(playerIndex, button);
        }

        public static bool GetButtonUp(InputButton button, byte playerIndex = 0)
        {
            var processor = InputProcessor.Instance;
            return processor != null && processor.GetButtonUp(playerIndex, button);
        }

        public static Vector2 GetMotion(byte playerIndex = 0)
        {
            var processor = InputProcessor.Instance;
            return processor != null ? processor.GetMotion(playerIndex) : Vector2.zero;
        }

        public static Vector2 GetView(byte playerIndex = 0)
        {
            var processor = InputProcessor.Instance;
            return processor != null ? processor.GetView(playerIndex) : Vector2.zero;
        }

        public static Vector2 GetPad(byte playerIndex = 0)
        {
            var processor = InputProcessor.Instance;
            return processor != null ? processor.GetPad(playerIndex) : Vector2.zero;
        }
    }

    /// <summary>
    /// Player-specific input accessor
    /// </summary>
    public struct PlayerInput
    {
        private readonly byte playerIndex;

        public PlayerInput(byte playerIndex)
        {
            this.playerIndex = playerIndex;
        }

        // Button states
        public bool GetButton(InputButton button) => NoMoreInput.GetButton(button, playerIndex);
        public bool GetButtonDown(InputButton button) => NoMoreInput.GetButtonDown(button, playerIndex);
        public bool GetButtonUp(InputButton button) => NoMoreInput.GetButtonUp(button, playerIndex);

        // Axis states
        public Vector2 Motion => NoMoreInput.GetMotion(playerIndex);
        public Vector2 View => NoMoreInput.GetView(playerIndex);
        public Vector2 Pad => NoMoreInput.GetPad(playerIndex);

        // Convenience methods
        public bool IsPressingAny()
        {
            for (int i = 0; i < 12; i++)
            {
                if (GetButton((InputButton)i))
                    return true;
            }
            return false;
        }

        public bool PressedAny()
        {
            for (int i = 0; i < 12; i++)
            {
                if (GetButtonDown((InputButton)i))
                    return true;
            }
            return false;
        }

        // Common button combinations
        public bool Confirm => GetButtonDown(InputButton.Action1) || GetButtonDown(InputButton.Menu1);
        public bool Cancel => GetButtonDown(InputButton.Action2) || GetButtonDown(InputButton.Menu2);
        public bool NavigateUp => Motion.y > 0.5f || Pad.y > 0.5f;
        public bool NavigateDown => Motion.y < -0.5f || Pad.y < -0.5f;
        public bool NavigateLeft => Motion.x < -0.5f || Pad.x < -0.5f;
        public bool NavigateRight => Motion.x > 0.5f || Pad.x > 0.5f;
    }

    /// <summary>
    /// Input utility extensions
    /// </summary>
    public static class InputExtensions
    {
        /// <summary>
        /// Get friendly display name for a button
        /// </summary>
        public static string GetDisplayName(this InputButton button)
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

        /// <summary>
        /// Check if vector represents a direction
        /// </summary>
        public static bool IsMoving(this Vector2 input, float deadzone = 0.5f)
        {
            return input.magnitude > deadzone;
        }

        /// <summary>
        /// Get 8-way direction from input
        /// </summary>
        public static Vector2 GetDirection8Way(this Vector2 input, float deadzone = 0.5f)
        {
            if (input.magnitude < deadzone) return Vector2.zero;

            float angle = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg;
            angle = Mathf.Round(angle / 45f) * 45f;
            
            float rad = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }
    }
}