using Unity.Entities;
using Unity.Burst;
using UnityEngine;
using NoMoreEngine.Simulation.Components;
using NoMoreEngine.Input;


namespace NoMoreEngine.Simulation.Systems
{
    /// <summary>
    /// Simple system that connects input to player movement
    /// Demonstrates the complete flow from input to simulation
    /// </summary>
    [UpdateInGroup(typeof(InputProcessingPhase))]
    public partial class PlayerInputMovementSystem : SystemBase
    {
        private InputSerializer inputSerializer;
        private InputPacket latestInput;
        private bool hasInput = false;

        protected override void OnCreate()
        {
            // Don't try to find InputSerializer here - it might not exist yet
        }

        protected override void OnStartRunning()
        {
            // Try to connect when system starts running
            TryConnectToInputSerializer();
        }

        private void TryConnectToInputSerializer()
        {
            if (inputSerializer != null) return; // Already connected

            inputSerializer = GameObject.FindAnyObjectByType<InputSerializer>();

            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady += OnInputReceived;
                Debug.Log("[PlayerInputSystem] Connected to InputSerializer");
            }
            else
            {
                Debug.LogWarning("[PlayerInputSystem] InputSerializer not found - will retry");
            }
        }

        protected override void OnDestroy()
        {
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady -= OnInputReceived;
            }
        }

        private void OnInputReceived(InputPacket[] packets)
        {
            // For now, just handle P1 input
            if (packets.Length > 0)
            {
                latestInput = packets[0];
                hasInput = true;
            }
        }

        protected override void OnUpdate()
        {
            // Retry connection if needed
            if (inputSerializer == null)
            {
                TryConnectToInputSerializer();
            }

            if (!hasInput) return;

            // Get input data for this frame
            var currentInput = latestInput;
            hasInput = false; // Consume the input

            // Convert input to movement
            Vector2 motionVector = currentInput.GetMotionVector();
            fix3 movementDirection = new fix3((fix)motionVector.x, (fix)0, (fix)motionVector.y);

            // Normalize diagonal movement
            if (fixMath.lengthsq(movementDirection) > fix.Epsilon)
            {
                movementDirection = fixMath.normalize(movementDirection);
            }

            // Fixed movement speed
            fix moveSpeed = (fix)5;

            // Apply movement to player-controlled entities
            Entities
                .WithAll<PlayerControlledTag>()
                .ForEach((ref SimpleMovementComponent movement) =>
                {
                    // Only modify horizontal velocity, preserve vertical (gravity)
                    fix3 currentVelocity = movement.velocity;
                    fix3 horizontalVelocity = movementDirection * moveSpeed;

                    // Preserve Y velocity (gravity), only change X and Z
                    movement.velocity = new fix3(
                        horizontalVelocity.x,
                        currentVelocity.y,  // Keep existing Y velocity
                        horizontalVelocity.z
                    );

                    movement.isMoving = fixMath.lengthsq(movementDirection) > fix.Epsilon;

                    // Handle jump with Action1 button
                    if (currentInput.GetButton(InputButton.Action1))
                    {
                        // Simple jump impulse (if we had vertical velocity)
                        // For now, just log it
                        Debug.Log("[PlayerInputSystem] Jump pressed!");
                    }
                })
                .WithoutBurst() // Disable Burst for Debug.Log
                .Run();
        }
    }
}