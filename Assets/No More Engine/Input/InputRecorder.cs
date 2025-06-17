using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System;

namespace NoMoreEngine.Input
{
    /// <summary>
    /// Lightweight input recorder that runs in the background during simulation
    /// Integrates directly with InputSerializer's packet flow
    /// </summary>
    public class InputRecorder : MonoBehaviour
    {
        [Header("Recording Settings")]
        [SerializeField] private bool enableRecording = true;
        [SerializeField] private int preallocatedFrames = 216000; // 60 minutes at 60fps
        
        // Lightweight recording buffer
        private List<InputFrame> recordingBuffer;
        private bool isRecording = false;
        private uint recordingStartTick = 0;
        
        // Integration references
        private InputSerializer inputSerializer;
        private Simulation.Systems.SimulationTimeSystem timeSystem;
        
        // Singleton
        private static InputRecorder instance;
        public static InputRecorder Instance => instance;
        
        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            
            // Preallocate buffer to avoid allocations during gameplay
            recordingBuffer = new List<InputFrame>(preallocatedFrames);
        }
        
        void Start()
        {
            // Get references
            inputSerializer = FindAnyObjectByType<InputSerializer>();
            if (inputSerializer == null)
            {
                Debug.LogError("[InputRecorder] InputSerializer not found!");
                enabled = false;
                return;
            }
            
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                timeSystem = world.GetExistingSystemManaged<Simulation.Systems.SimulationTimeSystem>();
            }
        }
        
        void OnEnable()
        {
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady += RecordPackets;
            }
        }
        
        void OnDisable()
        {
            if (inputSerializer != null)
            {
                inputSerializer.OnInputPacketsReady -= RecordPackets;
            }
        }
        
        /// <summary>
        /// Start recording - called automatically when entering InGame state
        /// </summary>
        public void StartRecording()
        {
            if (!enableRecording || isRecording)
                return;
            
            recordingBuffer.Clear();
            recordingStartTick = timeSystem?.GetCurrentTick() ?? 0;
            isRecording = true;
            
            Debug.Log($"[InputRecorder] Started recording at tick {recordingStartTick}");
        }
        
        /// <summary>
        /// Stop recording - called automatically when leaving InGame state
        /// Returns the recording data for save/discard decision
        /// </summary>
        public InputRecording StopRecording()
        {
            if (!isRecording)
                return null;
            
            isRecording = false;
            
            // Create recording object
            var recording = new InputRecording
            {
                startTick = recordingStartTick,
                endTick = timeSystem?.GetCurrentTick() ?? 0,
                frames = new List<InputFrame>(recordingBuffer), // Copy the buffer
                recordDate = DateTime.Now,
                engineVersion = Application.version
            };
            
            Debug.Log($"[InputRecorder] Stopped recording: {recording.FrameCount} frames");
            
            // Clear buffer for next recording
            recordingBuffer.Clear();
            
            return recording;
        }
        
        /// <summary>
        /// Lightweight packet recording - just store the data
        /// </summary>
        private void RecordPackets(InputPacket[] packets)
        {
            if (!isRecording || !enableRecording)
                return;
            
            // Check buffer capacity
            if (recordingBuffer.Count >= preallocatedFrames)
            {
                Debug.LogWarning("[InputRecorder] Recording buffer full, stopping recording");
                isRecording = false;
                return;
            }
            
            // Simply store the packets with minimal overhead
            recordingBuffer.Add(new InputFrame(packets));
        }
        
        /// <summary>
        /// Save recording to disk
        /// </summary>
        public static void SaveRecording(InputRecording recording, string filename)
        {
            string directory = Path.Combine(Application.persistentDataPath, "Replays");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            string fullPath = Path.Combine(directory, filename + ".nmr");
            
            try
            {
                using (var stream = File.Create(fullPath))
                using (var writer = new BinaryWriter(stream))
                {
                    recording.Serialize(writer);
                }
                
                Debug.Log($"[InputRecorder] Saved replay to: {fullPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[InputRecorder] Failed to save replay: {e.Message}");
            }
        }
        
        /// <summary>
        /// Get list of available replays
        /// </summary>
        public static string[] GetAvailableReplays()
        {
            string directory = Path.Combine(Application.persistentDataPath, "Replays");
            if (!Directory.Exists(directory))
                return new string[0];
            
            var files = Directory.GetFiles(directory, "*.nmr");
            var names = new string[files.Length];
            
            for (int i = 0; i < files.Length; i++)
            {
                names[i] = Path.GetFileNameWithoutExtension(files[i]);
            }
            
            return names;
        }
    }
    
    /// <summary>
    /// Lightweight recording data structure
    /// </summary>
    [Serializable]
    public class InputRecording
    {
        public uint startTick;
        public uint endTick;
        public DateTime recordDate;
        public string engineVersion;
        public List<InputFrame> frames;
        
        public int FrameCount => frames?.Count ?? 0;
        public float DurationSeconds => (endTick - startTick) / 60f; // Assuming 60Hz
        
        public void Serialize(BinaryWriter writer)
        {
            // Header
            writer.Write("NMR1"); // Format identifier
            writer.Write(1); // Version
            
            // Metadata
            writer.Write(startTick);
            writer.Write(endTick);
            writer.Write(recordDate.ToBinary());
            writer.Write(engineVersion);
            
            // Frame data
            writer.Write(frames.Count);
            foreach (var frame in frames)
            {
                frame.Serialize(writer);
            }
        }
        
        public static InputRecording Deserialize(BinaryReader reader)
        {
            // Verify header
            string format = reader.ReadString();
            if (format != "NMR1")
                throw new Exception("Invalid replay format");
            
            int version = reader.ReadInt32();
            if (version != 1)
                throw new Exception($"Unsupported replay version: {version}");
            
            // Read metadata
            var recording = new InputRecording
            {
                startTick = reader.ReadUInt32(),
                endTick = reader.ReadUInt32(),
                recordDate = DateTime.FromBinary(reader.ReadInt64()),
                engineVersion = reader.ReadString()
            };
            
            // Read frames
            int frameCount = reader.ReadInt32();
            recording.frames = new List<InputFrame>(frameCount);
            
            for (int i = 0; i < frameCount; i++)
            {
                recording.frames.Add(InputFrame.Deserialize(reader));
            }
            
            return recording;
        }
    }
    
    /// <summary>
    /// Single frame of input - minimal overhead
    /// </summary>
    [Serializable]
    public struct InputFrame
    {
        public InputPacket[] packets;
        
        public InputFrame(InputPacket[] sourcePackets)
        {
            // Quick array copy
            packets = new InputPacket[sourcePackets.Length];
            Array.Copy(sourcePackets, packets, sourcePackets.Length);
        }
        
        public void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)packets.Length);
            
            foreach (var packet in packets)
            {
                // Direct binary write - no allocations
                writer.Write(packet.motionAxis);
                writer.Write(packet.viewAxisX);
                writer.Write(packet.viewAxisY);
                writer.Write(packet.pad);
                writer.Write(packet.buttons);
                writer.Write(packet.frameNumber);
                writer.Write(packet.playerIndex);
            }
        }
        
        public static InputFrame Deserialize(BinaryReader reader)
        {
            int count = reader.ReadByte();
            var packets = new InputPacket[count];
            
            for (int i = 0; i < count; i++)
            {
                packets[i] = new InputPacket(
                    reader.ReadByte(),
                    reader.ReadInt16(),
                    reader.ReadInt16(),
                    reader.ReadByte(),
                    reader.ReadUInt16(),
                    reader.ReadUInt32(),
                    reader.ReadByte()
                );
            }
            
            return new InputFrame(packets);
        }
    }
}