using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interfaz del juego dibujada con IMGUI (no necesita Canvas ni paquetes extra):
/// vida, XP, oro, cristales, armas, barras de enemigos, altar de mejoras y pantallas.
/// </summary>
public class CartoHUD : MonoBehaviour
{
    public enum Overlay { None, Intro, Pause, Death, Victory }

    struct Floater
    {
        public Vector3 pos;
        public string text;
        public Color color;
        public float born;
    }

    static Overlay overlay = Overlay.None;
    static string toast = "";
    static float toastUntil;
    static string prompt = "";
    static int promptFrame = -10;
    static float flashUntil;
    static readonly List<Floater> floaters = new List<Floater>();

    GUIStyle style;
    float scale = 1f;

    public static bool OverlayActive { get { return overlay != Overlay.None; } }
    public static bool Blocking { get { return overlay != Overlay.None || CartoShop.IsOpen; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void DomainReset()
    {
        overlay = Overlay.None;
        toast = "";
        toastUntil = 0f;
        prompt = "";
        promptFrame = -10;
        flashUntil = 0f;
        floaters.Clear();
    }

    // ------------------------------------------------------------ API estatica

    public static void Toast(string text, float seconds)
    {
        toast = text;
        toastUntil = Time.unscaledTime + seconds;
    }

    public static void SetPrompt(string text)
    {
        prompt = text;
        promptFrame = Time.frameCount;
    }

    public static void DamageFlash()
    {
        flashUntil = Time.unscaledTime + 0.35f;
    }

    public static void AddFloater(Vector3 worldPos, string text, Color color)
    {
        Floater f = new Floater();
        f.pos = worldPos;
        f.text = text;
        f.color = color;
        f.born = Time.time;
        floaters.Add(f);
        if (floaters.Count > 40) floaters.RemoveAt(0);
    }

    public static void ShowOverlay(Overlay o)
    {
        overlay = o;
        if (o == Overlay.Intro || o == Overlay.Pause || o == Overlay.Victory) Time.timeScale = 0f;
        else Time.timeScale = 1f;
        bool free = o == Overlay.Pause;
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = free;
    }

    // ------------------------------------------------------------ logica

    void Awake()
    {
        floaters.Clear();
        CartoShop.IsOpen = false;
        if (overlay == Overlay.Pause || overlay == Overlay.Death) overlay = Overlay.None;
    }

    void Update()
    {
        switch (overlay)
        {
            case Overlay.None:
                if (CartoInput.PauseDown)
                {
                    if (CartoShop.IsOpen) CartoShop.Close();
                    else ShowOverlay(Overlay.Pause);
                }
                else if (Cursor.lockState != CursorLockMode.Locked && CartoInput.AttackDown)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                break;

            case Overlay.Intro:
                if (CartoInput.ConfirmDown)
                {
                    CartoGameState.introShown = true;
                    ShowOverlay(Overlay.None);
                    Toast("Nexo del Tiempo  —  Cruza un portal para viajar a una era", 5f);
                }
                break;

            case Overlay.Pause:
                if (CartoInput.PauseDown || CartoInput.ConfirmDown) ShowOverlay(Overlay.None);
                else if (CartoInput.NewGameDown)
                {
                    ShowOverlay(Overlay.None);
                    CartoGame.NewGame();
                }
                break;

            case Overlay.Death:
                if (CartoInput.ConfirmDown)
                {
                    ShowOverlay(Overlay.None);
                    CartoGame.RespawnAtHub();
                }
                break;

            case Overlay.Victory:
                if (CartoInput.ConfirmDown) ShowOverlay(Overlay.None);
                else if (CartoInput.NewGameDown)
                {
                    ShowOverlay(Overlay.None);
                    CartoGame.NewGame();
                }
                break;
        }
    }

    // ------------------------------------------------------------ dibujo

    void DrawRect(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    void Bar(Rect r, float pct, Color fill)
    {
        DrawRect(r, new Color(0f, 0f, 0f, 0.65f));
        float b = Mathf.Max(1f, 2f * scale);
        DrawRect(new Rect(r.x + b, r.y + b, (r.width - 2f * b) * Mathf.Clamp01(pct), r.height - 2f * b), fill);
    }

    void Label(Rect r, string text, float size, Color color, TextAnchor anchor)
    {
        style.fontSize = Mathf.Max(9, Mathf.RoundToInt(size * scale));
        style.alignment = anchor;
        float o = Mathf.Max(1f, 2f * scale);
        style.normal.textColor = new Color(0f, 0f, 0f, 0.85f * color.a);
        GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, style);
        style.normal.textColor = color;
        GUI.Label(r, text, style);
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label);
            style.fontStyle = FontStyle.Bold;
            style.wordWrap = true;
            style.richText = false;
            style.clipping = TextClipping.Overflow;
        }
        scale = Screen.height / 1080f;
        float W = Screen.width, H = Screen.height;
        float s = scale;
        Color white = Color.white;
        Color gold = new Color(1f, 0.85f, 0.3f);

        DrawWorld(W, H);

        // Destello rojo al recibir dano
        float flash = flashUntil - Time.unscaledTime;
        if (flash > 0f) DrawRect(new Rect(0, 0, W, H), new Color(0.8f, 0f, 0f, 0.3f * Mathf.Clamp01(flash / 0.35f)));

        // --- Arriba a la izquierda: vida y experiencia ---
        CartoPlayer player = CartoPlayer.Instance;
        float hp = player != null ? player.health.current : CartoGameState.health;
        float maxHp = CartoGameState.maxHealth;
        Rect hpRect = new Rect(30 * s, 28 * s, 400 * s, 34 * s);
        Bar(hpRect, maxHp > 0f ? hp / maxHp : 0f, new Color(0.85f, 0.18f, 0.2f));
        Label(hpRect, "Vida  " + Mathf.CeilToInt(hp) + " / " + Mathf.RoundToInt(maxHp), 20, white, TextAnchor.MiddleCenter);
        Rect xpRect = new Rect(30 * s, 68 * s, 400 * s, 22 * s);
        Bar(xpRect, (float)CartoGameState.xp / CartoGameState.XpToNext, new Color(0.3f, 0.6f, 1f));
        Label(xpRect, "Nivel " + CartoGameState.level + "   XP " + CartoGameState.xp + " / " + CartoGameState.XpToNext, 15, white, TextAnchor.MiddleCenter);

        // --- Arriba a la derecha: oro y cristales ---
        Label(new Rect(W - 430 * s, 24 * s, 400 * s, 40 * s), "Oro: " + CartoGameState.gold, 30, gold, TextAnchor.MiddleRight);
        Label(new Rect(W - 430 * s, 64 * s, 400 * s, 34 * s), "Cristales: " + CartoGameState.CrystalCount + " / 2", 24, new Color(0.6f, 0.95f, 1f), TextAnchor.MiddleRight);

        // --- Objetivo ---
        CartoGame game = CartoGame.Instance;
        if (game != null && !string.IsNullOrEmpty(game.objective))
            Label(new Rect(W * 0.5f - 500 * s, 20 * s, 1000 * s, 30 * s), game.objective, 19, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter);

        // --- Abajo a la derecha: armas ---
        CartoWeaponLibrary lib = CartoWeaponLibrary.Instance;
        if (lib != null)
        {
            CartoWeaponDef m = lib.Get(CartoGameState.meleeId);
            CartoWeaponDef r = lib.Get(CartoGameState.rangedId);
            string mt = m != null ? m.displayName + "  Nv." + (CartoGameState.WeaponLevel(m.id) + 1) : "—";
            string rt = r != null ? r.displayName + "  Nv." + (CartoGameState.WeaponLevel(r.id) + 1) : "(ninguna)";
            Label(new Rect(W - 630 * s, H - 100 * s, 600 * s, 34 * s), "Clic izq.  " + mt, 24, white, TextAnchor.MiddleRight);
            Label(new Rect(W - 630 * s, H - 64 * s, 600 * s, 34 * s), "Clic der.  " + rt, 24, white, TextAnchor.MiddleRight);
        }
        Label(new Rect(30 * s, H - 50 * s, 1100 * s, 30 * s),
            "WASD mover   Shift correr   Espacio esquivar   Q / R cambiar arma   E interactuar   Esc pausa",
            16, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft);

        // --- Barra del jefe ---
        CartoEnemy boss = null;
        for (int i = 0; i < CartoEnemy.All.Count; i++)
        {
            CartoEnemy e = CartoEnemy.All[i];
            if (e != null && e.health.isBoss && e.Aggro && !e.IsDead) { boss = e; break; }
        }
        if (boss != null)
        {
            Rect br = new Rect(W * 0.5f - 380 * s, 96 * s, 760 * s, 30 * s);
            Label(new Rect(br.x, br.y - 36 * s, br.width, 34 * s), boss.health.displayName, 26, new Color(1f, 0.8f, 0.5f), TextAnchor.MiddleCenter);
            Bar(br, boss.health.Percent, new Color(0.75f, 0.2f, 0.85f));
        }

        // --- Mensajes ---
        if (Time.unscaledTime < toastUntil && !string.IsNullOrEmpty(toast) && overlay == Overlay.None)
        {
            Rect tr = new Rect(W * 0.5f - 600 * s, H * 0.24f, 1200 * s, 60 * s);
            DrawRect(new Rect(tr.x + 100 * s, tr.y, tr.width - 200 * s, tr.height), new Color(0f, 0f, 0f, 0.45f));
            Label(tr, toast, 28, gold, TextAnchor.MiddleCenter);
        }
        if (Time.frameCount - promptFrame <= 1 && overlay == Overlay.None && !CartoShop.IsOpen)
        {
            Rect pr = new Rect(W * 0.5f - 450 * s, H * 0.72f, 900 * s, 54 * s);
            DrawRect(new Rect(pr.x + 120 * s, pr.y, pr.width - 240 * s, pr.height), new Color(0f, 0f, 0f, 0.55f));
            Label(pr, prompt, 26, white, TextAnchor.MiddleCenter);
        }

        if (CartoShop.IsOpen && overlay == Overlay.None) DrawShop(W, H);
        if (overlay != Overlay.None) DrawOverlay(W, H);
    }

    void DrawWorld(float W, float H)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float s = scale;

        // Barras de vida de los enemigos
        for (int i = 0; i < CartoEnemy.All.Count; i++)
        {
            CartoEnemy e = CartoEnemy.All[i];
            if (e == null || e.IsDead || e.health.isBoss) continue;
            if (!e.Aggro && e.health.Percent >= 1f) continue;
            Vector3 sp = cam.WorldToScreenPoint(e.transform.position + Vector3.up * (e.Height + 0.35f));
            if (sp.z <= 0f || sp.z > 40f) continue;
            float w = 110f * s, h = 12f * s;
            Bar(new Rect(sp.x - w * 0.5f, H - sp.y - h, w, h), e.health.Percent, new Color(0.9f, 0.25f, 0.2f));
        }

        // Numeros flotantes
        for (int i = floaters.Count - 1; i >= 0; i--)
        {
            Floater f = floaters[i];
            float age = Time.time - f.born;
            if (age > 1f || age < 0f)
            {
                floaters.RemoveAt(i);
                continue;
            }
            Vector3 sp = cam.WorldToScreenPoint(f.pos + Vector3.up * (age * 1.4f));
            if (sp.z <= 0f) continue;
            Color c = f.color;
            c.a = Mathf.Clamp01(1.6f - age * 1.6f);
            Label(new Rect(sp.x - 150 * s, H - sp.y - 20 * s, 300 * s, 40 * s), f.text, 26, c, TextAnchor.MiddleCenter);
        }
    }

    void DrawShop(float W, float H)
    {
        float s = scale;
        Rect box = new Rect(W * 0.5f - 520 * s, H * 0.5f - 230 * s, 1040 * s, 460 * s);
        DrawRect(box, new Color(0.05f, 0.03f, 0.12f, 0.92f));
        DrawRect(new Rect(box.x, box.y, box.width, 6 * s), new Color(0.6f, 0.9f, 1f));
        Label(new Rect(box.x, box.y + 22 * s, box.width, 50 * s), "ALTAR DE MEJORAS", 38, new Color(0.6f, 0.95f, 1f), TextAnchor.MiddleCenter);
        Label(new Rect(box.x, box.y + 76 * s, box.width, 36 * s), "Oro disponible: " + CartoGameState.gold, 26, new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleCenter);
        for (int n = 1; n <= 3; n++)
        {
            Rect row = new Rect(box.x + 50 * s, box.y + (140 + (n - 1) * 70) * s, box.width - 100 * s, 56 * s);
            DrawRect(row, new Color(1f, 1f, 1f, 0.07f));
            Label(new Rect(row.x + 20 * s, row.y, row.width - 40 * s, row.height), CartoShop.OptionText(n), 21, Color.white, TextAnchor.MiddleLeft);
        }
        Label(new Rect(box.x, box.yMax - 84 * s, box.width, 34 * s),
            "Pulsa 1, 2 o 3 para comprar.  Las mejoras de arma se aplican al arma equipada (Q / R para cambiarla).",
            18, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter);
        Label(new Rect(box.x, box.yMax - 48 * s, box.width, 34 * s), "[E] o [Esc]  Cerrar", 20, Color.white, TextAnchor.MiddleCenter);
    }

    void DrawOverlay(float W, float H)
    {
        float s = scale;
        DrawRect(new Rect(0, 0, W, H), new Color(0.02f, 0.01f, 0.06f, overlay == Overlay.Death ? 0.6f : 0.82f));
        Rect title = new Rect(0, H * 0.14f, W, 90 * s);
        Rect body = new Rect(W * 0.5f - 620 * s, H * 0.30f, 1240 * s, H * 0.48f);
        Rect foot = new Rect(0, H * 0.84f, W, 50 * s);
        Color cyan = new Color(0.6f, 0.95f, 1f);
        Color gold = new Color(1f, 0.85f, 0.3f);

        switch (overlay)
        {
            case Overlay.Intro:
                Label(title, "EL CARTÓGRAFO DEL TIEMPO", 64, cyan, TextAnchor.MiddleCenter);
                Label(body,
                    "Un desastre temporal ha mezclado todas las eras.\n" +
                    "Tú, el último cartógrafo, debes viajar a cada época y recuperar los cristales del tiempo " +
                    "para devolver el mundo a la normalidad.\n\n" +
                    "WASD  moverse          Ratón  cámara          Shift  correr          Espacio  esquivar\n" +
                    "Clic izquierdo  atacar          Clic derecho  disparar          Q / R  cambiar arma\n" +
                    "E  interactuar (portales, altar, cristales)          Esc  pausa\n\n" +
                    "Derrota enemigos para ganar oro y experiencia, consigue las armas de cada era\n" +
                    "y mejóralas en el Altar del Nexo.",
                    25, Color.white, TextAnchor.UpperCenter);
                Label(foot, "Pulsa ENTER para comenzar", 32, gold, TextAnchor.MiddleCenter);
                break;

            case Overlay.Pause:
                Label(title, "PAUSA", 64, cyan, TextAnchor.MiddleCenter);
                Label(body,
                    "WASD  moverse          Ratón  cámara          Shift  correr          Espacio  esquivar\n" +
                    "Clic izquierdo  atacar          Clic derecho  disparar          Q / R  cambiar arma\n" +
                    "E  interactuar          Rueda del ratón  zoom\n\n" +
                    "Nivel " + CartoGameState.level + "     Oro " + CartoGameState.gold +
                    "     Cristales " + CartoGameState.CrystalCount + " / 2     Enemigos derrotados " + CartoGameState.kills,
                    25, Color.white, TextAnchor.UpperCenter);
                Label(foot, "ESC o ENTER  continuar          N  nueva partida", 28, gold, TextAnchor.MiddleCenter);
                break;

            case Overlay.Death:
                Label(title, "HAS CAÍDO", 70, new Color(1f, 0.3f, 0.3f), TextAnchor.MiddleCenter);
                Label(body, "El tiempo te devuelve al Nexo.\nPierdes una cuarta parte de tu oro, pero conservas armas, nivel y cristales.",
                    27, Color.white, TextAnchor.UpperCenter);
                Label(foot, "Pulsa ENTER para volver al Nexo", 32, gold, TextAnchor.MiddleCenter);
                break;

            case Overlay.Victory:
                Label(title, "¡EL TIEMPO HA SIDO RESTAURADO!", 60, cyan, TextAnchor.MiddleCenter);
                int total = Mathf.FloorToInt(CartoGameState.playTime);
                Label(body,
                    "Has reunido los cristales de la Era Pirata y de la Era Samurái.\n" +
                    "Las épocas vuelven a su lugar gracias al cartógrafo.\n\n" +
                    "Nivel alcanzado: " + CartoGameState.level + "\n" +
                    "Enemigos derrotados: " + CartoGameState.kills + "\n" +
                    "Oro: " + CartoGameState.gold + "\n" +
                    "Tiempo de juego: " + (total / 60) + " min " + (total % 60) + " s",
                    28, Color.white, TextAnchor.UpperCenter);
                Label(foot, "ENTER  seguir explorando          N  nueva partida", 28, gold, TextAnchor.MiddleCenter);
                break;
        }
    }
}
