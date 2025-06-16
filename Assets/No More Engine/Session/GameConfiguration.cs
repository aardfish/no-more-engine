using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics.FixedPoint;


namespace NoMoreEngine.Session
{
    /// <summary>
    /// GameConfiguration - Holds all match setup data
    /// This is passed to the simulation initializer to create the match
    /// </summary>
    [System.Serializable]
    public class GameConfiguration
    {
        [Header("Player Configuration")]
        public PlayerSlot[] playerSlots = new PlayerSlot[4];
        public int maxActivePlayers = 4;

        [Header("Match Settings")]
        public string stageName = "TestArena";
        public GameMode gameMode = GameMode.Versus;
        public float timeLimit = 0f; // 0 = unlimited
        public int stockCount = 3; // lives per player

        [Header("Rules")]
        public WinCondition winCondition = WinCondition.LastManStanding;
        public bool friendlyFire = true;
        public DifficultyLevel difficulty = DifficultyLevel.Normal;

        public GameConfiguration()
        {
            // Initialize player slots
            for (int i = 0; i < playerSlots.Length; i++)
            {
                playerSlots[i] = new PlayerSlot((byte)i);
            }
        }

        /// <summary>
        /// Add a player to the next available slot
        /// </summary>
        public PlayerSlot AddPlayer(PlayerType type, int deviceIndex = 0)
        {
            for (int i = 0; i < maxActivePlayers; i++)
            {
                if (playerSlots[i].IsEmpty)
                {
                    playerSlots[i].AssignPlayer(type, deviceIndex);
                    return playerSlots[i];
                }
            }

            Debug.LogWarning("[GameConfig] No empty slots available");
            return null;
        }

        /// <summary>
        /// Remove a player from a specific slot
        /// </summary>
        public void RemovePlayer(int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < playerSlots.Length)
            {
                playerSlots[slotIndex].Clear();
            }
        }

        /// <summary>
        /// Clear all players
        /// </summary>
        public void ClearAllPlayers()
        {
            foreach (var slot in playerSlots)
            {
                slot.Clear();
            }
        }

        /// <summary>
        /// Get count of active players
        /// </summary>
        public int GetActivePlayerCount()
        {
            int count = 0;
            for (int i = 0; i < maxActivePlayers; i++)
            {
                if (!playerSlots[i].IsEmpty)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Check if all players are ready
        /// </summary>
        public bool AreAllPlayersReady()
        {
            bool hasPlayers = false;
            for (int i = 0; i < maxActivePlayers; i++)
            {
                if (!playerSlots[i].IsEmpty)
                {
                    hasPlayers = true;
                    if (!playerSlots[i].isReady)
                        return false;
                }
            }
            return hasPlayers;
        }

        /// <summary>
        /// Get spawn positions for the current stage
        /// </summary>
        public fp3[] GetSpawnPositions()
        {
            // For now, simple line formation
            // Later this would read from stage data
            var positions = new List<fp3>();
            int activeCount = GetActivePlayerCount();

            for (int i = 0; i < playerSlots.Length; i++)
            {
                if (!playerSlots[i].IsEmpty)
                {
                    positions.Add(new fp3(
                        (fp)(positions.Count * 3 - (activeCount - 1) * 1.5f),
                        (fp)2,
                        (fp)0
                    ));
                }
            }

            return positions.ToArray();
        }
    }

    /// <summary>
    /// Represents a single player slot
    /// </summary>
    [System.Serializable]
    public class PlayerSlot
    {
        public byte slotIndex;      // 0-3
        public PlayerType type;
        public int deviceIndex;     // Input device for local players
        public bool isReady;
        public string playerName;

        public bool IsEmpty => type == PlayerType.Empty;
        public bool IsLocal => type == PlayerType.Local;
        public bool IsBot => type == PlayerType.Bot;

        public PlayerSlot(byte index)
        {
            slotIndex = index;
            Clear();
        }

        public void AssignPlayer(PlayerType playerType, int device = 0)
        {
            type = playerType;
            deviceIndex = device;
            isReady = (playerType == PlayerType.Bot); // Bots are always ready
            playerName = $"Player {slotIndex + 1}";
        }

        public void Clear()
        {
            type = PlayerType.Empty;
            deviceIndex = 0;
            isReady = false;
            playerName = "";
        }
    }

    /// <summary>
    /// Player types
    /// </summary>
    public enum PlayerType
    {
        Empty,
        Local,
        Remote,
        Bot
    }

    /// <summary>
    /// Game modes
    /// </summary>
    public enum GameMode
    {
        Mission,
        Versus,
        Training,
        Tutorial
    }

    /// <summary>
    /// Win conditions
    /// </summary>
    public enum WinCondition
    {
        LastManStanding,
        TimeLimit,
        StockLimit,
        Custom
    }

    /// <summary>
    /// Difficulty levels
    /// </summary>
    public enum DifficultyLevel
    {
        Easy,
        Normal,
        Hard,
        Extreme
    }
}