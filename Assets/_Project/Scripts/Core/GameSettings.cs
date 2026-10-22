using UnityEngine;

/// <summary>
/// 玩家设置（目前是音量）的读写与生效。
///
/// 现在工程里一个音频文件都还没有（Assets/_Project/Audio 下只有 .gitkeep），
/// 所以调音量暂时听不出差别 —— 但存档和入口先做好，等音效素材进来就自动生效。
/// 「试听」按钮会现场合成一声 880Hz 的短音，让你立刻听出音量变化，不用等美术。
///
/// 存档位置：PlayerPrefs（Windows 上落在注册表 HKCU\Software\...\Unity\...，
/// 换成 SetMasterVolume 之外的方案之前，这里够用，也不污染工程目录）。
/// </summary>
public static class GameSettings
{
    private const string KeyMaster = "settings.audio.master";
    private const string KeyMusic = "settings.audio.music";
    private const string KeySfx = "settings.audio.sfx";

    private static bool _loaded;

    private static float _master = 0.8f;
    private static float _music = 0.7f;
    private static float _sfx = 0.9f;

    private static AudioSource _toneSource;
    private static AudioClip _toneClip;

    /// <summary>主音量（同时控制 AudioListener 的总输出）</summary>
    public static float Master
    {
        get => _master;
        set
        {
            _master = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyMaster, _master);
            Apply();
        }
    }

    /// <summary>音乐音量：留给将来的 BGM</summary>
    public static float Music
    {
        get => _music;
        set
        {
            _music = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyMusic, _music);
        }
    }

    /// <summary>音效音量：留给将来的打击音/技能音</summary>
    public static float Sfx
    {
        get => _sfx;
        set
        {
            _sfx = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeySfx, _sfx);
        }
    }

    /// <summary>从 PlayerPrefs 读回设置并生效。重复调用无副作用</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        _master = PlayerPrefs.GetFloat(KeyMaster, _master);
        _music = PlayerPrefs.GetFloat(KeyMusic, _music);
        _sfx = PlayerPrefs.GetFloat(KeySfx, _sfx);

        Apply();
    }

    /// <summary>把设置真正交给引擎（目前只有总音量）</summary>
    public static void Apply()
    {
        AudioListener.volume = Mathf.Clamp01(_master);
    }

    /// <summary>把设置写回磁盘。PlayerPrefs 本身会在退出时自动存，这里只是让「立刻关进程」也不丢</summary>
    public static void Save()
    {
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 试听：现场合成一段 0.2 秒的 880Hz 正弦波（带淡入淡出，避免爆音）。
    /// 没有音频素材也能听出音量大小，方便调滑块时判断。
    /// </summary>
    public static void PlayTestTone()
    {
        AudioSource source = GetToneSource();
        if (source == null) return;

        source.volume = Mathf.Clamp01(_sfx);
        source.PlayOneShot(GetToneClip());
    }

    private static AudioClip GetToneClip()
    {
        if (_toneClip != null) return _toneClip;

        const int sampleRate = 44100;
        const float duration = 0.2f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);

        float[] data = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;

            // 前 10ms 淡入、后 50ms 淡出：不处理包络的话首尾会有「咔」的一声
            float attack = Mathf.Clamp01(i / (sampleRate * 0.01f));
            float release = Mathf.Clamp01((sampleCount - i) / (sampleRate * 0.05f));
            float envelope = Mathf.Min(attack, release);

            data[i] = Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.35f * envelope;
        }

        _toneClip = AudioClip.Create("MenuTestTone", sampleCount, 1, sampleRate, false);
        _toneClip.SetData(data, 0);
        return _toneClip;
    }

    private static AudioSource GetToneSource()
    {
        if (_toneSource != null) return _toneSource;

        GameObject go = new GameObject("__MenuTestTone");
        Object.DontDestroyOnLoad(go);

        _toneSource = go.AddComponent<AudioSource>();
        _toneSource.playOnAwake = false;
        _toneSource.loop = false;
        _toneSource.spatialBlend = 0f;

        return _toneSource;
    }
}
