using System;
using UnityEngine;

/// <summary>Vida de cualquier personaje (jugador o enemigo).</summary>
public class CartoHealth : MonoBehaviour
{
    public int team = 1;              // 0 = jugador, 1 = enemigos
    public float maxHealth = 50f;
    public float current = 50f;
    public string displayName = "";
    public bool isBoss = false;

    [NonSerialized] public bool invulnerable;
    public event Action<float, Vector3> Damaged;
    public event Action Died;

    public bool IsDead { get { return current <= 0f; } }
    public float Percent { get { return maxHealth <= 0f ? 0f : Mathf.Clamp01(current / maxHealth); } }

    void Awake()
    {
        if (team != 0) current = maxHealth;
    }

    public void TakeDamage(float amount, Vector3 from)
    {
        if (IsDead || invulnerable || amount <= 0f) return;
        current = Mathf.Max(0f, current - amount);
        if (Damaged != null) Damaged(amount, from);
        if (current <= 0f && Died != null) Died();
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        current = Mathf.Min(maxHealth, current + amount);
    }
}
