using NoMoreEngine.Simulation.Snapshot;
using Unity.Entities;
using Unity.Mathematics.FixedPoint;


namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Tag component to identify player-controlled entities
    /// </summary>

    [Snapshotable(Priority = 10)] // Higher priority for player control
    public struct PlayerControlledTag : IComponentData, ISnapshotableComponent<PlayerControlledTag>
    {
        // Just a tag, no data needed
        public int GetSnapshotSize() => 0; // No data to serialize
        public bool ValidateSnapshot() => true; // Always valid since it's just a tag
    }

    /// <summary>
    /// Component to track which player index controls an entity
    /// </summary>

    [Snapshotable(Priority = 11)]
    public struct PlayerControlComponent : IComponentData, ISnapshotableComponent<PlayerControlComponent>
    {
        public byte playerIndex; // 0-3 for P1-P4

        public PlayerControlComponent(byte index)
        {
            playerIndex = index;
        }

        public int GetSnapshotSize() => sizeof(byte);
        public bool ValidateSnapshot() => true;
    }

    /// <summary>
    /// Component for AI-controlled entities
    /// </summary>
    public struct AIControlledTag : IComponentData
    {
        // Just a tag for now
        // TODO: Add AI behavior type, difficulty, etc.
    }

    /// <summary>
    /// Health component for damageable entities
    /// </summary>
    public struct HealthComponent : IComponentData
    {
        public fp currentHealth;
        public fp maxHealth;

        public HealthComponent(fp health)
        {
            currentHealth = health;
            maxHealth = health;
        }

        public bool IsDead => currentHealth <= fp.zero;
        public fp HealthPercentage => maxHealth > fp.zero ? currentHealth / maxHealth : fp.zero;
    }

    /// <summary>
    /// Component to track player stats during a match
    /// </summary>
    public struct PlayerStatsComponent : IComponentData
    {
        public int stocks; // Lives remaining
        public int score;
        public int eliminations;
        public int falls;

        public PlayerStatsComponent(int initialStocks)
        {
            stocks = initialStocks;
            score = 0;
            eliminations = 0;
            falls = 0;
        }
    }

    /// <summary>
    /// Component for respawning entities
    /// </summary>
    public struct RespawnComponent : IComponentData
    {
        public float respawnTimer;
        public fp3 respawnPosition;
        public bool isRespawning;

        public RespawnComponent(fp3 spawnPos)
        {
            respawnTimer = 0f;
            respawnPosition = spawnPos;
            isRespawning = false;
        }
    }

    /// <summary>
    /// Component for entities that can deal damage
    /// </summary>
    public struct DamageComponent : IComponentData
    {
        public fp damageAmount;
        public fp knockbackForce;
        public DamageType damageType;

        public DamageComponent(fp damage, fp knockback = default, DamageType type = DamageType.Normal)
        {
            damageAmount = damage;
            knockbackForce = knockback == default ? damage * (fp)0.1f : knockback;
            damageType = type;
        }
    }

    /// <summary>
    /// Types of damage
    /// </summary>
    public enum DamageType : byte
    {
        Normal,
        Fire,
        Electric,
        Explosive,
        Environmental
    }

    /// <summary>
    /// Component for team identification
    /// </summary>
    public struct TeamComponent : IComponentData
    {
        public byte teamId;

        public TeamComponent(byte team)
        {
            teamId = team;
        }

        public static readonly TeamComponent NoTeam = new TeamComponent(255);
    }
}