using UnityEngine;

/// <summary>
/// Biblia de arte del juego: low-poly de "cartografia arcana" (vacio indigo, piedra fria y brillos
/// emisivos). Paleta fija de seis colores; HUD, menus, luces y particulas salen de aqui.
///   Cian = tiempo, cristales e interfaz principal.   Oro = seleccion, recompensas y Era Pirata.
///   Magenta = grieta y Era Samurai.   Carmesi = vida y dano.   Tinta = fondos.   Pergamino = texto.
/// </summary>
public static class CartoArt
{
    public static readonly Color Ink = new Color(0.055f, 0.039f, 0.141f);         // #0E0A24
    public static readonly Color Parchment = new Color(0.949f, 0.918f, 0.827f);   // #F2EAD3
    public static readonly Color Cyan = new Color(0.302f, 0.902f, 1f);            // #4DE6FF
    public static readonly Color Gold = new Color(1f, 0.824f, 0.302f);            // #FFD24D
    public static readonly Color Magenta = new Color(0.902f, 0.302f, 0.722f);     // #E64DB8
    public static readonly Color Crimson = new Color(0.878f, 0.259f, 0.247f);     // #E0423F

    /// <summary>El mismo color con otra opacidad.</summary>
    public static Color A(Color c, float alpha)
    {
        c.a = alpha;
        return c;
    }
}
