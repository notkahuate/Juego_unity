using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class CartoWeaponDef
{
    public string id;
    public string displayName;
    public string era;
    public string prefabName;      // nombre del prefab en el package de assets
    public string prefabHint;      // parte de la ruta, para desambiguar
    public bool ranged;
    public bool twoHanded;
    public float damage = 10f;
    public float range = 2.2f;         // alcance (cuerpo a cuerpo)
    public float cooldown = 0.7f;      // tiempo entre ataques
    public float hitDelay = 0.28f;     // momento del golpe / disparo
    public float projectileSpeed = 30f;
    public bool projectileUsesModel;   // el proyectil es el propio modelo (kunai)
    public Vector3 projectileEuler;
    public GameObject prefab;          // lo rellena el constructor de escenas
    public Vector3 gripEuler;
    public Vector3 gripOffset;
    public float modelScale = 1f;
}

/// <summary>Catalogo de armas. Hay uno en cada escena (objeto "_CartoGame").</summary>
public class CartoWeaponLibrary : MonoBehaviour
{
    public static CartoWeaponLibrary Instance;
    public List<CartoWeaponDef> weapons = new List<CartoWeaponDef>();

    void Awake()
    {
        Instance = this;
    }

    public CartoWeaponDef Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < weapons.Count; i++)
            if (weapons[i].id == id) return weapons[i];
        return null;
    }

    /// <summary>Definiciones por defecto. Cambia aqui dano, alcance, etc.</summary>
    public static List<CartoWeaponDef> CreateDefaults()
    {
        var list = new List<CartoWeaponDef>();

        // Arma inicial del cartografo: su catalejo (modelo con el largo en +Z).
        list.Add(new CartoWeaponDef
        {
            id = "catalejo", displayName = "Catalejo", era = "Cartografo",
            prefabName = "Telescope", prefabHint = "Pirate Weapons",
            damage = 12f, range = 2.1f, cooldown = 0.62f, hitDelay = 0.26f,
            gripEuler = new Vector3(-90f, 0f, 0f), gripOffset = new Vector3(0f, -0.12f, 0f)
        });

        // --- Era pirata ---
        list.Add(new CartoWeaponDef
        {
            id = "sable", displayName = "Sable pirata", era = "Pirata",
            prefabName = "Culass1", prefabHint = "Pirate Weapons",
            damage = 22f, range = 2.4f, cooldown = 0.58f, hitDelay = 0.25f
        });
        list.Add(new CartoWeaponDef
        {
            id = "pistola", displayName = "Pistola de chispa", era = "Pirata",
            prefabName = "Flintlock", prefabHint = "Pirate Weapons",
            ranged = true, damage = 30f, cooldown = 1.1f, hitDelay = 0.16f, projectileSpeed = 45f
        });

        // --- Era samurai ---
        list.Add(new CartoWeaponDef
        {
            id = "katana", displayName = "Katana", era = "Samurai",
            prefabName = "Katana", prefabHint = "Samurai Warrior",
            twoHanded = true, damage = 36f, range = 2.8f, cooldown = 0.8f, hitDelay = 0.34f
        });
        list.Add(new CartoWeaponDef
        {
            id = "kunai", displayName = "Kunai", era = "Samurai",
            prefabName = "Kunai", prefabHint = "Samurai Warrior",
            ranged = true, damage = 15f, cooldown = 0.4f, hitDelay = 0.12f, projectileSpeed = 32f,
            projectileUsesModel = true, projectileEuler = new Vector3(90f, 0f, 0f)
        });
        return list;
    }
}
