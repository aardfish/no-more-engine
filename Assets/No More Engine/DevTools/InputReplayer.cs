using UnityEngine;
using NoMoreEngine.Input;

namespace NoMoreEngine.DevTools
{
    /// <summary>
    /// Handles replay playback for determinism testing
    /// </summary>
    public class InputReplayer : MonoBehaviour
    {
        private InputRecording currentRecording;
        private int currentFrameIndex = 0;
        private bool isReplaying = false;
        
        private InputSerializer inputSerializer;
        
        public bool IsReplaying => isReplaying;
        public float Progress => currentRecording != null && currentRecording.FrameCount > 0 ? 
            (float)currentFrameIndex / currentRecording.FrameCount : 0f;
        
        void Start()
        {
            inputSerializer = FindAnyObjectByType<InputSerializer>();
        }
        
        public void StartReplay(InputRecording recording)
        {
            currentRecording = recording;
            currentFrameIndex = 0;
            isReplaying = true;
            
            // Hook into input system
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady += ProvideReplayPackets;
            }
        }
        
        public void StopReplay()
        {
            isReplaying = false;
            
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady -= ProvideReplayPackets;
            }
        }
        
        private void ProvideReplayPackets(InputPacket[] livePackets)
        {
            if (!isReplaying || currentFrameIndex >= currentRecording.FrameCount)
            {
                // End of replay
                StopReplay();
                GetComponent<DeterminismTester>()?.OnReplayFinished();
                return;
            }
            
            // Get replay frame
            var frame = currentRecording.frames[currentFrameIndex];
            
            // Override live packets
            System.Array.Copy(frame.packets, livePackets, frame.packets.Length);
            
            currentFrameIndex++;
        }
    }
}