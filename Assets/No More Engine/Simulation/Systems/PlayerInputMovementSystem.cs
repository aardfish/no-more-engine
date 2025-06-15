using Unity.Entities;
using Unity.Burst;
using UnityEngine;
using NoMoreEngine.Simulation.Components;
using NoMoreEngine.Input;
using Unity.Collections;


namespace NoMoreEngine.Simulation.Systems
{
    /// <summary>
    /// Improved system that connects input to player movement
    /// Now properly handles disconnection/reconnection after snapshot restore
    /// </summary>
    [UpdateInGroup(typeof(InputProcessingPhase))]
    public partial class PlayerInputMovementSystem : SystemBase
    {
        private InputSerializer inputSerializer;
        private InputPacket[] currentFramePackets;
        private bool hasInput = false;
        
        // Track if we need to reconnect
        private bool needsReconnection = false;
        private int reconnectionAttempts = 0;
        private const int MAX_RECONNECTION_ATTEMPTS = 5;
        
        // Track last known player entity count for detecting changes
        private int lastKnownPlayerCount = -1;

        protected override void OnCreate()
        {
            // Require player entities to exist
            RequireForUpdate<PlayerControlledTag>();
        }

        protected override void OnStartRunning()
        {
            // Try to connect when system starts running
            TryConnectToInputSerializer();
            
            // Mark that we need to check for player entities
            needsReconnection = true;
        }

        protected override void OnStopRunning()
        {
            // Disconnect when system stops
            DisconnectFromInputSerializer();
        }

        private void TryConnectToInputSerializer()
        {
            if (inputSerializer != null && inputSerializer.enabled) return; // Already connected

            inputSerializer = GameObject.FindAnyObjectByType<InputSerializer>();

            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady += OnInputReceived;
                Debug.Log("[PlayerInputSystem] Connected to InputSerializer");
                reconnectionAttempts = 0;
            }
            else
            {
                reconnectionAttempts++;
                if (reconnectionAttempts < MAX_RECONNECTION_ATTEMPTS)
                {
                    Debug.LogWarning($"[PlayerInputSystem] InputSerializer not found - attempt {reconnectionAttempts}/{MAX_RECONNECTION_ATTEMPTS}");
                }
            }
        }

        private void DisconnectFromInputSerializer()
        {
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady -= OnInputReceived;
                inputSerializer = null;
                Debug.Log("[PlayerInputSystem] Disconnected from InputSerializer");
            }
        }

        protected override void OnDestroy()
        {
            DisconnectFromInputSerializer();
        }

        private void OnInputReceived(InputPacket[] packets)
        {
            currentFramePackets = packets;
            hasInput = true;
        }

        protected override void OnUpdate()
        {
            // Check if player entity count has changed (indicates potential snapshot restore)
            int currentPlayerCount = GetPlayerEntityCount();
            if (lastKnownPlayerCount != -1 && currentPlayerCount != lastKnownPlayerCount)
            {
                Debug.Log($"[PlayerInputSystem] Player entity count changed from {lastKnownPlayerCount} to {currentPlayerCount} - marking for reconnection");
                needsReconnection = true;
            }
            lastKnownPlayerCount = currentPlayerCount;

            // Handle reconnection if needed
            if (needsReconnection)
            {
                HandleReconnection();
                needsReconnection = false;
            }

            // Ensure we have a valid connection
            if (inputSerializer == null || !inputSerializer.enabled)
            {
                TryConnectToInputSerializer();
                if (inputSerializer == null) return; // Still no connection
            }

            if (!hasInput || currentFramePackets == null || currentFramePackets.Length == 0) return;

            // Process input for each player
            ProcessPlayerInput();
            
            // Clear input for next frame
            hasInput = false;
        }

        private void ProcessPlayerInput()
        {
            // Create a lookup for input packets by player index
            var inputByPlayer = new NativeHashMap<byte, InputPacket>(4, Allocator.Temp);
            
            foreach (var packet in currentFramePackets)
            {
                inputByPlayer[packet.playerIndex] = packet;
            }

            // Apply movement to player-controlled entities based on their player index
            Entities
                .WithAll<PlayerControlledTag>()
                .ForEach((Entity entity, ref SimpleMovementComponent movement, in PlayerControlComponent playerControl) =>
                {
                    // Try to get input for this player
                    if (inputByPlayer.TryGetValue(playerControl.playerIndex, out InputPacket input))
                    {
                        // Convert input to movement
                        Vector2 motionVector = input.GetMotionVector();
                        fix3 movementDirection = new fix3((fix)motionVector.x, (fix)0, (fix)motionVector.y);

                        // Normalize diagonal movement
                        if (fixMath.lengthsq(movementDirection) > fix.Epsilon)
                        {
                            movementDirection = fixMath.normalize(movementDirection);
                        }

                        // Fixed movement speed
                        fix moveSpeed = (fix)5;

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
                        if (input.GetButton(InputButton.Action1))
                        {
                            // TODO: Implement proper jump mechanics
                            // For now, just log it (disabled in Burst)
                        }
                    }
                    else
                    {
                        // No input for this player - stop horizontal movement
                        movement.velocity = new fix3(fix.Zero, movement.velocity.y, fix.Zero);
                        movement.isMoving = false;
                    }
                })
                .WithBurst()
                .WithDisposeOnCompletion(inputByPlayer)
                .Run();
        }

        private int GetPlayerEntityCount()
        {
            var query = GetEntityQuery(
                ComponentType.ReadOnly<PlayerControlledTag>(),
                ComponentType.ReadOnly<PlayerControlComponent>()
            );
            
            return query.CalculateEntityCount();
        }

        private void HandleReconnection()
        {
            Debug.Log("[PlayerInputSystem] Handling reconnection after potential snapshot restore");
            
            // First, ensure we're connected to the input serializer
            DisconnectFromInputSerializer();
            TryConnectToInputSerializer();
            
            // Verify player entities exist and have proper components
            var query = GetEntityQuery(
                ComponentType.ReadOnly<PlayerControlledTag>(),
                ComponentType.ReadOnly<PlayerControlComponent>()
            );
            
            var playerCount = query.CalculateEntityCount();
            Debug.Log($"[PlayerInputSystem] Found {playerCount} player entities after reconnection");
            
            // Clear any stale input
            hasInput = false;
            currentFramePackets = null;
        }
        
        /// <summary>
        /// Public method to force reconnection (can be called by SnapshotSystem)
        /// </summary>
        public void ForceReconnection()
        {
            Debug.Log("[PlayerInputSystem] Force reconnection requested");
            needsReconnection = true;
        }
    }
}