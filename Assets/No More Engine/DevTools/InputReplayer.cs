using UnityEngine;
using NoMoreEngine.Input;

namespace NoMoreEngine.DevTools
{
    /// <summary>
    /// Handles replay playback for determinism testing
    /// FIXED: Removed direct dependency on DeterminismTester
    /// </summary>
    public class InputReplayer : MonoBehaviour
    {
        private InputRecording currentRecording;
        private int currentFrameIndex = 0;
        private bool isReplaying = false;
        
        private InputSerializer inputSerializer;
        
        // Events
        public event System.Action OnReplayStarted;
        public event System.Action OnReplayFinished;
        
        public bool IsReplaying => isReplaying;
        public float Progress => currentRecording != null && currentRecording.FrameCount > 0 ? 
            (float)currentFrameIndex / currentRecording.FrameCount : 0f;
        public int CurrentFrame => currentFrameIndex;
        public int TotalFrames => currentRecording?.FrameCount ?? 0;
        
        void Start()
        {
            inputSerializer = FindAnyObjectByType<InputSerializer>();
        }
        
        public void StartReplay(InputRecording recording)
        {
            if (recording == null)
            {
                Debug.LogError("[InputReplayer] Cannot start replay with null recording!");
                return;
            }
            
            currentRecording = recording;
            currentFrameIndex = 0;
            isReplaying = true;
            
            Debug.Log($"[InputReplayer] Starting replay with {recording.FrameCount} frames");
            
            // Hook into input system
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady += ProvideReplayPackets;
            }
            
            // Notify listeners
            OnReplayStarted?.Invoke();
        }
        
        public void StopReplay()
        {
            if (!isReplaying)
                return;
                
            isReplaying = false;
            
            Debug.Log($"[InputReplayer] Stopped replay at frame {currentFrameIndex}/{currentRecording?.FrameCount ?? 0}");
            
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady -= ProvideReplayPackets;
            }
            
            // Notify listeners
            OnReplayFinished?.Invoke();
        }
        
        private void ProvideReplayPackets(InputPacket[] livePackets)
        {
            if (!isReplaying || currentRecording == null)
            {
                return;
            }
            
            if (currentFrameIndex >= currentRecording.FrameCount)
            {
                // End of replay
                Debug.Log($"[InputReplayer] Reached end of replay at frame {currentFrameIndex}");
                StopReplay();
                return;
            }
            
            // Get replay frame
            var frame = currentRecording.frames[currentFrameIndex];
            
            // Override live packets with recorded packets
            if (frame.packets != null && livePackets != null && livePackets.Length > 0)
            {
                // Clear all live packets first (set to neutral state)
                for (int i = 0; i < livePackets.Length; i++)
                {
                    livePackets[i] = new InputPacket(0, 5, 0, 0, 5, 0, (byte)i);
                }
                
                // Then apply recorded packets
                int packetsToReplace = Mathf.Min(frame.packets.Length, livePackets.Length);
                for (int i = 0; i < packetsToReplace; i++)
                {
                    if (i < frame.packets.Length)
                    {
                        var recordedPacket = frame.packets[i];
                        // Ensure frame number matches current tick
                        recordedPacket.frameNumber = livePackets[i].frameNumber;
                        livePackets[i] = recordedPacket;
                    }
                }
                
                if (currentFrameIndex == 0 || currentFrameIndex % 60 == 0) // Log first frame and every second
                {
                    Debug.Log($"[InputReplayer] Frame {currentFrameIndex}/{currentRecording.FrameCount} - " +
                             $"Replaced {packetsToReplace} packets (live array size: {livePackets.Length})");
                }
            }
            else if (livePackets == null || livePackets.Length == 0)
            {
                Debug.LogWarning($"[InputReplayer] Frame {currentFrameIndex} - No live packets to override!");
            }
            
            currentFrameIndex++;
        }
        
        void OnDestroy()
        {
            // Ensure we unhook from input system
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady -= ProvideReplayPackets;
            }
        }
    }
}