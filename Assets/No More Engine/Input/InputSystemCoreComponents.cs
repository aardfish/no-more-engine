using UnityEngine;


namespace NoMoreEngine.Input
{
    /// <summary>
    /// Deterministic input packet for 60fps simulation
    /// Designed to be as compact as possible while maintaining precision
    /// </summary>
    [System.Serializable]
    public struct InputPacket
    {
        // Motion Axis: 1-9 (numpad notation, 5 = no input)
        public byte motionAxis;

        // View Axis: Fixed precision 2D vector for deterministic aiming
        public short viewAxisX;
        public short viewAxisY;

        // Pad: 1-9 (numpad notation, 5 = no input) 
        public byte pad;

        // 12 buttons packed into a single ushort (only 12 bits used)
        public ushort buttons;

        // Frame number this input is for
        public uint frameNumber;

        // Player index (0-3 for local players)
        public byte playerIndex;

        public InputPacket(byte motionAxis, short viewAxisX, short viewAxisY,
            byte pad, ushort buttons, uint frameNumber, byte playerIndex)
        {
            this.motionAxis = motionAxis;
            this.viewAxisX = viewAxisX;
            this.viewAxisY = viewAxisY;
            this.pad = pad;
            this.buttons = buttons;
            this.frameNumber = frameNumber;
            this.playerIndex = playerIndex;
        }

        /// <summary>
        /// Check if a specific button is pressed
        /// </summary>
        public bool GetButton(InputButton button)
        {
            return (buttons & (1 << (int)button)) != 0;
        }

        /// <summary>
        /// Set a specific button state
        /// </summary>
        public void SetButton(InputButton button, bool pressed)
        {
            if (pressed)
                buttons |= (ushort)(1 << (int)button);
            else
                buttons &= (ushort)~(1 << (int)button);
        }

        /// <summary>
        /// Get motion as Unity Vector2 (for conversion back to simulation)
        /// </summary>
        public Vector2 GetMotionVector()
        {
            return NumpadToVector2(motionAxis);
        }

        /// <summary>
        /// Get pad input as Unity Vector2
        /// </summary>
        public Vector2 GetPadVector()
        {
            return NumpadToVector2(pad);
        }

        /// <summary>
        /// Get view axis as normalized Vector2 (-1 to 1 range)
        /// </summary>
        public Vector2 GetViewVector()
        {
            const float maxValue = 32767f;
            return new Vector2(viewAxisX / maxValue, viewAxisY / maxValue);
        }

        /// <summary>
        /// Convert numpad notation (1-9) to Vector2
        /// 7 8 9
        /// 4 5 6  (5 = no input/center)
        /// 1 2 3
        /// </summary>
        public static Vector2 NumpadToVector2(byte numpadValue)
        {
            return numpadValue switch
            {
                1 => new Vector2(-1, -1), // Down-Left
                2 => new Vector2(0, -1),  // Down
                3 => new Vector2(1, -1),  // Down-Right
                4 => new Vector2(-1, 0),  // Left
                5 => new Vector2(0, 0),   // No input/Center
                6 => new Vector2(1, 0),   // Right
                7 => new Vector2(-1, 1),  // Up-Left
                8 => new Vector2(0, 1),   // Up
                9 => new Vector2(1, 1),   // Up-Right
                _ => new Vector2(0, 0)    // Default to no input
            };
        }

        /// <summary>
        /// Convert Vector2 to numpad notation
        /// </summary>
        public static byte Vector2ToNumpad(Vector2 input)
        {
            // Normalize to -1, 0, 1 for each axis
            int x = input.x > 0.5f ? 1 : (input.x < -0.5f ? -1 : 0);
            int y = input.y > 0.5f ? 1 : (input.y < -0.5f ? -1 : 0);

            return (x, y) switch
            {
                (-1, -1) => 1, // Down-Left
                (0, -1) => 2,  // Down
                (1, -1) => 3,  // Down-Right
                (-1, 0) => 4,  // Left
                (0, 0) => 5,   // No input/Center
                (1, 0) => 6,   // Right
                (-1, 1) => 7,  // Up-Left
                (0, 1) => 8,   // Up
                (1, 1) => 9,   // Up-Right
                _ => 5         // Default to no input
            };
        }
    }

    /// <summary>
    /// Button definitions matching the provided mapping document
    /// </summary>
    public enum InputButton : byte
    {
        Action1 = 0,    // A / Space
        Action2 = 1,    // B / F  
        Action3 = 2,    // X / E
        Action4 = 3,    // Y / Q
        Action5 = 4,    // LB / L.Shift
        Action6 = 5,    // RB / C
        Trigger2 = 6,   // LT / Right Mouse
        Trigger1 = 7,   // RT / Left Mouse
        Aux1 = 8,       // LZ / L.Ctrl
        Aux2 = 9,       // RZ / L.Alt
        Menu1 = 10,     // Menu / Esc
        Menu2 = 11      // View / Tab
    }

    /// <summary>
    /// Input context for switching between in-game and menu mappings
    /// </summary>
    public enum InputContext
    {
        InGame,
        Menu
    }

    /// <summary>
    /// Timestamped input sample for buffering
    /// </summary>
    public struct InputSample
    {
        public InputPacket packet;
        public float timestamp;
        public bool consumed;

        public InputSample(InputPacket packet, float timestamp)
        {
            this.packet = packet;
            this.timestamp = timestamp;
            this.consumed = false;
        }
    }
}