using UnityEngine;

/// <summary>
/// Sonido del juego sin archivos de audio: cada efecto se calcula muestra a muestra al arrancar
/// (AudioClip.Create) y se reproduce en 2D. Uso:  CartoAudio.Play(CartoAudio.Sfx.Hit);
/// El volumen general (CartoAudio.Volume, 0-1) se ajusta en los menus y se recuerda entre partidas.
/// </summary>
public class CartoAudio : MonoBehaviour
{
    public enum Sfx { Hit, Hurt, Shoot, Skill, Coin, Crystal, UiMove, UiClick, Phase }

    const int Rate = 44100;
    const float Gain = 0.5f;            // volumen moderado de los efectos, antes del ajuste del jugador
    const float MinGap = 0.045f;        // el mismo efecto no se repite dentro de este tiempo (varios golpes a la vez)
    const int Voices = 8;
    const string VolumeKey = "CartoGame.Volumen";
    const float TwoPi = 6.2831853f;

    static CartoAudio instance;
    static AudioClip[] clips;
    static float[] lastPlayed;
    static float volume = -1f;

    AudioSource[] sources;
    int next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void DomainReset()
    {
        instance = null;
        clips = null;
        lastPlayed = null;
        volume = -1f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplySavedVolume()
    {
        LoadVolume();
    }

    static void LoadVolume()
    {
        if (volume >= 0f) return;
        volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 0.6f));
        AudioListener.volume = volume;
    }

    /// <summary>Volumen general del juego, de 0 a 1.</summary>
    public static float Volume
    {
        get
        {
            LoadVolume();
            return volume;
        }
        set
        {
            volume = Mathf.Clamp01(value);
            AudioListener.volume = volume;
            PlayerPrefs.SetFloat(VolumeKey, volume);
        }
    }

    /// <summary>Sube (o baja) el volumen en pasos del 10 %; al pasar del 100 % vuelve a 0 si 'wrap'.</summary>
    public static void StepVolume(int steps, bool wrap)
    {
        int tenths = Mathf.RoundToInt(Volume * 10f) + steps;
        if (wrap && tenths > 10) tenths = 0;
        Volume = Mathf.Clamp(tenths, 0, 10) / 10f;
    }

    static void Ensure()
    {
        if (instance != null) return;
        LoadVolume();
        GameObject go = new GameObject("_CartoAudio");
        DontDestroyOnLoad(go);   // que un sonido no se corte al cambiar de escena
        instance = go.AddComponent<CartoAudio>();
        instance.sources = new AudioSource[Voices];
        for (int i = 0; i < Voices; i++)
        {
            AudioSource a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = 0f;
            a.loop = false;
            instance.sources[i] = a;
        }
        if (clips == null) BuildClips();
    }

    /// <summary>Reproduce un efecto. 'volumeScale' y 'pitch' permiten variarlo (mas grave, mas flojo...).</summary>
    public static void Play(Sfx sfx, float volumeScale = 1f, float pitch = 1f)
    {
        if (!Application.isPlaying) return;
        Ensure();
        int i = (int)sfx;
        float now = Time.unscaledTime;
        if (now - lastPlayed[i] < MinGap) return;
        lastPlayed[i] = now;
        AudioSource src = instance.sources[instance.next];
        instance.next = (instance.next + 1) % Voices;
        src.pitch = Mathf.Clamp(pitch, 0.3f, 3f);
        src.PlayOneShot(clips[i], Gain * volumeScale);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // ------------------------------------------------------------------ sintesis

    static float[] Buffer(float seconds)
    {
        return new float[Mathf.CeilToInt(seconds * Rate)];
    }

    static AudioClip Clip(string name, float[] data)
    {
        // Fundido corto al principio y al final para que no haga "clic".
        int fade = Mathf.Min(220, data.Length / 4);
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            data[i] *= k;
            data[data.Length - 1 - i] *= k;
        }
        for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
        AudioClip c = AudioClip.Create(name, data.Length, 1, Rate, false);
        c.SetData(data, 0);
        return c;
    }

    /// <summary>Nota de campana: seno con un armonico, que nace en 'start' y se apaga con 'decay'.</summary>
    static void Bell(float[] d, float start, float freq, float amp, float decay)
    {
        int from = Mathf.FloorToInt(start * Rate);
        for (int i = from; i < d.Length; i++)
        {
            float t = (i - from) / (float)Rate;
            float env = Mathf.Exp(-t * decay);
            d[i] += (Mathf.Sin(TwoPi * freq * t) + 0.3f * Mathf.Sin(TwoPi * freq * 2.01f * t)) * amp * env;
        }
    }

    static void BuildClips()
    {
        int count = System.Enum.GetValues(typeof(Sfx)).Length;
        clips = new AudioClip[count];
        lastPlayed = new float[count];
        for (int i = 0; i < count; i++) lastPlayed[i] = -10f;
        System.Random rnd = new System.Random(7);
        float[] d;
        float lp, ph;

        // Golpe: chasquido de ruido y un "tum" grave que cae.
        d = Buffer(0.15f);
        lp = 0f;
        ph = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            lp += ((float)rnd.NextDouble() * 2f - 1f - lp) * 0.35f;
            ph += TwoPi * Mathf.Lerp(190f, 65f, t / 0.15f) / Rate;
            d[i] = lp * 0.75f * Mathf.Exp(-t * 38f) + Mathf.Sin(ph) * 0.8f * Mathf.Exp(-t * 22f);
        }
        clips[(int)Sfx.Hit] = Clip("Carto_Golpe", d);

        // Dano recibido: golpe sordo, mas grave y largo.
        d = Buffer(0.24f);
        lp = 0f;
        ph = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            lp += ((float)rnd.NextDouble() * 2f - 1f - lp) * 0.12f;
            ph += TwoPi * Mathf.Lerp(130f, 48f, t / 0.24f) / Rate;
            d[i] = lp * 0.5f * Mathf.Exp(-t * 20f) + Mathf.Sin(ph) * 0.9f * Mathf.Exp(-t * 13f);
        }
        clips[(int)Sfx.Hurt] = Clip("Carto_Dano", d);

        // Disparo: estallido de ruido y un tono que cae en picado.
        d = Buffer(0.2f);
        ph = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)rnd.NextDouble() * 2f - 1f;
            ph += TwoPi * Mathf.Lerp(720f, 130f, Mathf.Sqrt(t / 0.2f)) / Rate;
            d[i] = noise * 0.7f * Mathf.Exp(-t * 30f) + Mathf.Sign(Mathf.Sin(ph)) * 0.22f * Mathf.Exp(-t * 17f);
        }
        clips[(int)Sfx.Shoot] = Clip("Carto_Disparo", d);

        // Habilidad: barrido que sube con un soplo de aire.
        d = Buffer(0.55f);
        lp = 0f;
        ph = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float k = i / (float)d.Length;
            lp += ((float)rnd.NextDouble() * 2f - 1f - lp) * 0.2f;
            ph += TwoPi * Mathf.Lerp(260f, 1250f, k * k) / Rate;
            float env = Mathf.Sin(Mathf.PI * k);
            d[i] = (Mathf.Sin(ph) * 0.45f + Mathf.Sin(ph * 1.5f) * 0.22f + Mathf.Sin(ph * 2f) * 0.12f + lp * 0.3f) * env;
        }
        clips[(int)Sfx.Skill] = Clip("Carto_Habilidad", d);

        // Oro: dos notas agudas, "ti-lin".
        d = Buffer(0.24f);
        Bell(d, 0f, 1318.5f, 0.5f, 30f);
        Bell(d, 0.07f, 1760f, 0.5f, 16f);
        clips[(int)Sfx.Coin] = Clip("Carto_Oro", d);

        // Cristal: arpegio de campanas que queda sonando.
        d = Buffer(1.1f);
        Bell(d, 0f, 1046.5f, 0.3f, 4.5f);
        Bell(d, 0.09f, 1318.5f, 0.3f, 4.5f);
        Bell(d, 0.18f, 1568f, 0.3f, 4.5f);
        Bell(d, 0.27f, 2093f, 0.32f, 4f);
        clips[(int)Sfx.Crystal] = Clip("Carto_Cristal", d);

        // Menu: mover la seleccion (tic) y aceptar (dos notas).
        d = Buffer(0.05f);
        Bell(d, 0f, 980f, 0.45f, 70f);
        clips[(int)Sfx.UiMove] = Clip("Carto_MenuMover", d);

        d = Buffer(0.13f);
        Bell(d, 0f, 740f, 0.45f, 40f);
        Bell(d, 0.045f, 1110f, 0.45f, 28f);
        clips[(int)Sfx.UiClick] = Clip("Carto_MenuAceptar", d);

        // Cambio de fase: acorde grave y disonante que tiembla, con una caida al principio.
        d = Buffer(1.35f);
        lp = 0f;
        ph = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float k = i / (float)d.Length;
            lp += ((float)rnd.NextDouble() * 2f - 1f - lp) * 0.06f;
            float drone = 0f;
            for (int h = 1; h <= 5; h++)
                drone += (Mathf.Sin(TwoPi * 73.4f * h * t) + Mathf.Sin(TwoPi * 103.8f * h * t)) / h;
            float tremolo = 0.7f + 0.3f * Mathf.Sin(TwoPi * 9f * t);
            ph += TwoPi * Mathf.Lerp(620f, 70f, Mathf.Clamp01(t / 0.4f)) / Rate;
            float drop = t < 0.4f ? Mathf.Sin(ph) * 0.35f * (1f - t / 0.4f) : 0f;
            float env = Mathf.Clamp01(t / 0.04f) * (1f - k) * (1f - k * 0.5f);
            d[i] = (drone * 0.14f * tremolo + lp * 0.9f + drop) * env;
        }
        clips[(int)Sfx.Phase] = Clip("Carto_Fase", d);
    }
}
