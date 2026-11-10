using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Estado global de la partida. Es estatico para que sobreviva al cambiar de escena
/// (Nexo -> Era Pirata -> Era Samurai).
/// </summary>
public static class CartoGameState
{
    public const string SceneHub = "Carto_Hub";
    public const string ScenePirate = "Carto_Piratas";
    public const string SceneSamurai = "Carto_Samurai";
    public const string StartWeapon = "catalejo";
    public const int MaxWeaponLevel = 5;

    public static int gold;
    public static int xp;
    public static int level;
    public static int kills;
    public static float maxHealth;
    public static float health;
    public static bool[] crystals = new bool[2];   // 0 = pirata, 1 = samurai
    public static List<string> owned = new List<string>();
    public static Dictionary<string, int> weaponLevels = new Dictionary<string, int>();
    public static string meleeId;
    public static string rangedId;
    public static int vitalityLevel;
    public static bool introShown;
    public static bool victoryShown;
    public static float playTime;

    static CartoGameState()
    {
        Reset();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void DomainReset()
    {
        Reset();
    }

    public static void Reset()
    {
        gold = 0;
        xp = 0;
        level = 1;
        kills = 0;
        maxHealth = 100f;
        health = 100f;
        crystals = new bool[2];
        owned = new List<string>();
        owned.Add(StartWeapon);
        weaponLevels = new Dictionary<string, int>();
        meleeId = StartWeapon;
        rangedId = "";
        vitalityLevel = 0;
        introShown = false;
        victoryShown = false;
        playTime = 0f;
    }

    public static int XpToNext
    {
        get { return 40 + (level - 1) * 30; }
    }

    public static int CrystalCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < crystals.Length; i++) if (crystals[i]) n++;
            return n;
        }
    }

    public static bool AllCrystals
    {
        get { return CrystalCount >= crystals.Length; }
    }

    public static bool HasCrystal(int era)
    {
        return era >= 0 && era < crystals.Length && crystals[era];
    }

    /// <summary>Suma experiencia. Devuelve true si subio de nivel.</summary>
    public static bool AddXp(int amount)
    {
        xp += amount;
        bool leveled = false;
        while (xp >= XpToNext)
        {
            xp -= XpToNext;
            level++;
            maxHealth += 10f;
            health = maxHealth;
            leveled = true;
        }
        return leveled;
    }

    public static bool Owns(string id)
    {
        return owned.Contains(id);
    }

    public static bool AddWeapon(string id)
    {
        if (owned.Contains(id)) return false;
        owned.Add(id);
        return true;
    }

    public static int WeaponLevel(string id)
    {
        int lvl;
        if (!string.IsNullOrEmpty(id) && weaponLevels.TryGetValue(id, out lvl)) return lvl;
        return 0;
    }

    public static void UpgradeWeapon(string id)
    {
        weaponLevels[id] = WeaponLevel(id) + 1;
    }

    public static float DamageMultiplier(string id)
    {
        return (1f + 0.25f * WeaponLevel(id)) * (1f + 0.06f * (level - 1));
    }

    public static int WeaponUpgradeCost(string id)
    {
        return 30 + 30 * WeaponLevel(id);
    }

    public static int VitalityCost
    {
        get { return 40 + 35 * vitalityLevel; }
    }
}
