using Unity.Entities;


namespace NoMoreEngine.Simulation.Components
{
    /// <summary>
    /// Tag component to identify player-controlled entities
    /// </summary>
    public struct PlayerControlledTag : IComponentData
    {
        // Just a tag, no data needed
    }

    /// <summary>
    /// Component to track which player index controls an entity
    /// </summary>
    public struct PlayerControlComponent : IComponentData
    {
        public byte playerIndex; // 0-3 for P1-P4

        public PlayerControlComponent(byte index)
        {
            playerIndex = index;
        }
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
        public fix currentHealth;
        public fix maxHealth;

        public HealthComponent(fix health)
        {
            currentHealth = health;
            maxHealth = health;
        }

        public bool IsDead => currentHealth <= fix.Zero;
        public fix HealthPercentage => maxHealth > fix.Zero ? currentHealth / maxHealth : fix.Zero;
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
        public fix3 respawnPosition;
        public bool isRespawning;

        public RespawnComponent(fix3 spawnPos)
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
        public fix damageAmount;
        public fix knockbackForce;
        public DamageType damageType;

        public DamageComponent(fix damage, fix knockback = default, DamageType type = DamageType.Normal)
        {
            damageAmount = damage;
            knockbackForce = knockback == default ? damage * (fix)0.1f : knockback;
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