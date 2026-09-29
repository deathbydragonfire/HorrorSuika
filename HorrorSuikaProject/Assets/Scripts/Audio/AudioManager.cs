using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Persistent owner of every sound: the looping music bed, pooled flesh SFX with per-hit
/// variation, and UI hover/click. Spawned from Resources before the first scene loads, so any
/// scene can be played directly in the editor.
/// WebGL has no AudioMixer or filter DSP, so the chorus and room reverb are baked into the flesh
/// clips (Tools/Audio/BakeGrossFx.ps1) and channel volumes are plain multipliers.
/// </summary>
public class AudioManager : MonoBehaviour
{
    [Serializable]
    private struct Variation
    {
        [Tooltip("Random linear volume range, before the channel volume.")]
        public Vector2 Volume;

        [Tooltip("Random pitch range, multiplied into the tier pitch.")]
        public Vector2 Pitch;

        public Variation(float minVolume, float maxVolume, float minPitch, float maxPitch)
        {
            Volume = new Vector2(minVolume, maxVolume);
            Pitch = new Vector2(minPitch, maxPitch);
        }

        public float RandomVolume() => UnityEngine.Random.Range(Volume.x, Volume.y);

        public float RandomPitch() => UnityEngine.Random.Range(Pitch.x, Pitch.y);
    }

    private readonly struct PendingPlay
    {
        public readonly float FireTime;
        public readonly AudioClip Clip;
        public readonly float Volume;
        public readonly float Pitch;
        public readonly float Pan;

        public PendingPlay(float fireTime, AudioClip clip, float volume, float pitch, float pan)
        {
            FireTime = fireTime;
            Clip = clip;
            Volume = volume;
            Pitch = pitch;
            Pan = pan;
        }
    }

    private const string ResourcePath = "Audio/AudioManager";
    private const float MinAudiblePitch = 0.1f;

    [Header("Flesh")]
    [Tooltip("Baked (chorus + room reverb) flesh hits shared by drops and merges.")]
    [SerializeField] private RandomClipBag fleshClips = new RandomClipBag();

    [Tooltip("Released item. Deliberately quieter than a merge.")]
    [SerializeField] private Variation drop = new Variation(0.2f, 0.3f, 0.9f, 1.12f);

    [Tooltip("Main hit when two items fuse.")]
    [SerializeField] private Variation merge = new Variation(0.65f, 0.85f, 0.9f, 1.1f);

    [Tooltip("Second, pitched-down clip layered under each merge for wet, meaty weight.")]
    [SerializeField] private Variation mergeUnderlayer = new Variation(0.28f, 0.4f, 0.55f, 0.68f);

    [Tooltip("Seconds the underlayer trails the main hit; the offset smears the two into one squelch.")]
    [SerializeField] private Vector2 mergeUnderlayerDelay = new Vector2(0.015f, 0.045f);

    [Tooltip("Two max-tier items annihilating.")]
    [SerializeField] private Variation topTierPop = new Variation(0.9f, 1f, 0.58f, 0.7f);

    [Tooltip("Pitch at the smallest (x) and largest (y) tier: big blobs sound heavier.")]
    [SerializeField] private Vector2 tierPitch = new Vector2(1.15f, 0.72f);

    [Tooltip("How far left/right a hit at the screen edge pans (0 = mono, 1 = hard).")]
    [SerializeField, Range(0f, 1f)] private float stereoSpread = 0.4f;

    [Header("Impacts")]
    [Tooltip("Volume for the softest (x) and hardest (y) floor/wall hit; each hit also gets a small random spread.")]
    [SerializeField] private Vector2 impactVolume = new Vector2(0.1f, 0.55f);

    [Tooltip("Random pitch spread on floor/wall hits.")]
    [SerializeField] private Vector2 impactPitch = new Vector2(0.9f, 1.1f);

    [Tooltip("Item radius mapped onto the tier pitch range: x plays at tierPitch.x, y at tierPitch.y.")]
    [SerializeField] private Vector2 impactRadiusRange = new Vector2(0.18f, 1.43f);

    [Tooltip("Floor/wall hits allowed per cascade window; a pile landing at once must not become a roar.")]
    [SerializeField] private int impactVoicesPerWindow = 4;

    [Header("Cascades")]
    [Tooltip("Window, in seconds, used to detect chain reactions.")]
    [SerializeField] private float cascadeWindow = 0.15f;

    [Tooltip("Merges inside the window that play at full weight; later ones are ducked, then culled.")]
    [SerializeField] private int cascadeFullVoices = 3;

    [SerializeField, Range(0f, 1f)] private float cascadeDuck = 0.55f;

    [Header("Background Apparitions")]
    [Tooltip("Wet flesh clips, pitched far down, for eyes and lurkers surfacing out of the fog.")]
    [SerializeField] private RandomClipBag apparitionClips = new RandomClipBag();

    [Tooltip("Short, dry clips pitched up into a tiny wet click for background blinks.")]
    [SerializeField] private RandomClipBag apparitionBlinkClips = new RandomClipBag();

    [Tooltip("Slow meaty stretch as something pushes out of the dark. Barely there.")]
    [SerializeField] private Variation apparitionEmerge = new Variation(0.03f, 0.05f, 0.38f, 0.52f);

    [Tooltip("Lids unsticking, or a mouth peeling open.")]
    [SerializeField] private Variation apparitionOpen = new Variation(0.018f, 0.03f, 0.68f, 0.85f);

    [Tooltip("Sinking back into the fog.")]
    [SerializeField] private Variation apparitionRetreat = new Variation(0.015f, 0.028f, 0.33f, 0.45f);

    [Tooltip("A background blink. Should be felt more than heard.")]
    [SerializeField] private Variation apparitionBlink = new Variation(0.008f, 0.016f, 1.65f, 2.1f);

    [Tooltip("Volume multiplier for the farthest apparitions; the nearest play at full volume.")]
    [SerializeField, Range(0f, 1f)] private float apparitionFarVolume = 0.35f;

    [Tooltip("Minimum seconds between two apparition sounds of the same kind, so a swarm surfacing at once stays a murmur.")]
    [SerializeField] private float apparitionMinInterval = 0.3f;

    [Header("Heartbeat")]
    [Tooltip("First, heavier beat. Triggered by HorrorBackground in time with the backdrop pulse.")]
    [SerializeField] private AudioClip heartbeatLub;

    [Tooltip("Second, softer beat.")]
    [SerializeField] private AudioClip heartbeatDub;

    [SerializeField] private Variation heartbeatLubVariation = new Variation(0.16f, 0.2f, 0.97f, 1.03f);
    [SerializeField] private Variation heartbeatDubVariation = new Variation(0.1f, 0.13f, 0.98f, 1.04f);

    [Tooltip("Volume multiplier at the peak of a surge, when the heart races.")]
    [SerializeField, Min(1f)] private float heartbeatSurgeBoost = 1.8f;

    [Header("UI")]
    [SerializeField] private AudioClip hoverClip;
    [SerializeField] private AudioClip clickClip;
    [SerializeField] private Variation hover = new Variation(0.4f, 0.48f, 0.96f, 1.04f);
    [SerializeField] private Variation click = new Variation(0.75f, 0.85f, 0.97f, 1.03f);

    [Tooltip("Sweeping the pointer across a list must not machine-gun the hover sound.")]
    [SerializeField] private float hoverMinInterval = 0.06f;

    [Header("Music")]
    [SerializeField] private AudioClip musicClip;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.6f;
    [SerializeField] private float musicFadeInSeconds = 4f;

    [Tooltip("Music level while the game is paused, so the menu feels set apart from play.")]
    [SerializeField, Range(0f, 1f)] private float pausedMusicLevel = 0.45f;

    [SerializeField] private float pauseDuckSeconds = 0.35f;

    [Header("Voices")]
    [SerializeField] private int voiceCount = 20;

    private readonly float[] lastApparitionTimes = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
    private readonly List<PendingPlay> pendingPlays = new List<PendingPlay>(8);
    private readonly Queue<float> recentMergeTimes = new Queue<float>(16);
    private readonly Queue<float> recentImpactTimes = new Queue<float>(16);

    private AudioSource[] voices;
    private float[] voiceStartTimes;
    private AudioSource musicSource;
    private AudioSource heartbeatSource;
    private float musicFade;
    private float musicDuck = 1f;
    private float lastHoverTime = float.NegativeInfinity;

    /// <summary>The live manager, or null before bootstrap and in edit mode.</summary>
    public static AudioManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
        {
            return;
        }

        AudioManager prefab = Resources.Load<AudioManager>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"{nameof(AudioManager)}: no prefab at Resources/{ResourcePath}; the game will be silent.");
            return;
        }

        Instantiate(prefab).name = prefab.name;
    }

    /// <summary>Quiet squelch for an item leaving the dropper.</summary>
    public static void PlayDrop(Vector3 worldPosition, int tierIndex, int maxTierIndex)
    {
        if (Instance != null)
        {
            Instance.PlayDropInternal(worldPosition, tierIndex, maxTierIndex);
        }
    }

    /// <summary>Layered flesh hit for two items fusing into <paramref name="resultTierIndex"/>.</summary>
    public static void PlayMerge(Vector3 worldPosition, int resultTierIndex, int maxTierIndex)
    {
        if (Instance != null)
        {
            Instance.PlayMergeInternal(worldPosition, resultTierIndex, maxTierIndex);
        }
    }

    /// <summary>
    /// Flesh slapping into the floor or a wall. <paramref name="strength"/> is 0 (barely touching)
    /// to 1 (full-speed hit); <paramref name="radius"/> sets how heavy it sounds.
    /// </summary>
    public static void PlayImpact(Vector3 worldPosition, float strength, float radius)
    {
        if (Instance != null)
        {
            Instance.PlayImpactInternal(worldPosition, strength, radius);
        }
    }

    /// <summary>The heaviest hit: two max-tier items annihilating.</summary>
    public static void PlayTopTierPop(Vector3 worldPosition)
    {
        if (Instance != null)
        {
            Instance.PlayTopTierPopInternal(worldPosition);
        }
    }

    /// <summary>
    /// Very quiet flesh sound for a background apparition. <paramref name="depth01"/> is 0 for the
    /// nearest and 1 for the farthest; farther ones are quieter. Never steals a gameplay voice.
    /// </summary>
    public static void PlayApparition(ApparitionSound sound, Vector3 worldPosition, float depth01)
    {
        if (Instance != null)
        {
            Instance.PlayApparitionInternal(sound, worldPosition, depth01);
        }
    }

    /// <summary>
    /// One half of the heartbeat: the heavy "lub" or the softer "dub". <paramref name="surge"/> 0..1
    /// swells it as the heart races. Plays on its own source so it never competes with gameplay.
    /// </summary>
    public static void PlayHeartbeat(bool isLub, float surge)
    {
        if (Instance != null)
        {
            Instance.PlayHeartbeatInternal(isLub, surge);
        }
    }

    /// <summary>UI hover or keyboard/gamepad navigation onto a control.</summary>
    public static void PlayUiHover()
    {
        if (Instance != null)
        {
            Instance.PlayUiHoverInternal();
        }
    }

    /// <summary>UI press.</summary>
    public static void PlayUiClick()
    {
        if (Instance != null)
        {
            Instance.PlayUi(Instance.clickClip, Instance.click);
        }
    }

    /// <summary>Plays a representative sound so a volume slider can be heard at its new level.</summary>
    public static void PreviewChannel(AudioChannel channel)
    {
        if (Instance == null)
        {
            return;
        }

        switch (channel)
        {
            case AudioChannel.Ui:
                PlayUiClick();
                break;
            case AudioChannel.Game:
                Instance.PlayVoice(Instance.fleshClips.Next(), Instance.merge.RandomVolume() * AudioVolumeSettings.GetGain(AudioChannel.Game), Instance.merge.RandomPitch(), 0f, true);
                break;
        }
    }

    /// <summary>Adds hover/click hooks to every Selectable under <paramref name="root"/>, for UI spawned at runtime.</summary>
    public static void RegisterUi(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Selectable[] selectables = root.GetComponentsInChildren<Selectable>(true);
        for (int i = 0; i < selectables.Length; i++)
        {
            EnsureHooks(selectables[i]);
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildVoices();
        heartbeatSource = CreateSource("Heartbeat");
        heartbeatSource.priority = 64;
        StartMusic();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    private void OnApplicationQuit()
    {
        AudioVolumeSettings.Save();
    }

    private void Update()
    {
        UpdateMusic();
        FlushPendingPlays();
    }

    private void BuildVoices()
    {
        voices = new AudioSource[Mathf.Max(1, voiceCount)];
        voiceStartTimes = new float[voices.Length];
        for (int i = 0; i < voices.Length; i++)
        {
            voices[i] = CreateSource($"Voice {i}");
            voiceStartTimes[i] = float.NegativeInfinity;
        }
    }

    private AudioSource CreateSource(string sourceName)
    {
        var child = new GameObject(sourceName);
        child.transform.SetParent(transform, false);

        AudioSource source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        return source;
    }

    private void StartMusic()
    {
        if (musicClip == null)
        {
            return;
        }

        musicSource = CreateSource("Music");
        musicSource.clip = musicClip;
        musicSource.loop = true;
        musicSource.priority = 0;
        // Keeps playing under a future pause menu that uses AudioListener.pause.
        musicSource.ignoreListenerPause = true;
        musicSource.volume = 0f;
        musicFade = musicFadeInSeconds > 0f ? 0f : 1f;
        musicSource.Play();
    }

    private void UpdateMusic()
    {
        if (musicSource == null)
        {
            return;
        }

        if (musicFade < 1f)
        {
            musicFade = Mathf.MoveTowards(musicFade, 1f, Time.unscaledDeltaTime / musicFadeInSeconds);
        }

        // The pause menu pauses the listener; the music opts out of that, so it ducks here instead.
        float duckTarget = AudioListener.pause ? pausedMusicLevel : 1f;
        musicDuck = Mathf.MoveTowards(musicDuck, duckTarget, Time.unscaledDeltaTime / Mathf.Max(0.01f, pauseDuckSeconds));

        // Applied every frame so the music slider is live while dragging. Squared fade reads as a smooth swell.
        musicSource.volume = musicVolume * musicFade * musicFade * musicDuck * AudioVolumeSettings.GetGain(AudioChannel.Music);
    }

    private void PlayDropInternal(Vector3 worldPosition, int tierIndex, int maxTierIndex)
    {
        float gain = AudioVolumeSettings.GetGain(AudioChannel.Game);
        float pitch = TierPitch(tierIndex, maxTierIndex) * drop.RandomPitch();
        PlayVoice(fleshClips.Next(), drop.RandomVolume() * gain, pitch, PanFor(worldPosition), false);
    }

    private void PlayImpactInternal(Vector3 worldPosition, float strength, float radius)
    {
        if (CountRecent(recentImpactTimes) > impactVoicesPerWindow)
        {
            return;
        }

        strength = Mathf.Clamp01(strength);
        float gain = AudioVolumeSettings.GetGain(AudioChannel.Game);
        float volume = Mathf.Lerp(impactVolume.x, impactVolume.y, strength) * UnityEngine.Random.Range(0.85f, 1f);

        float size = Mathf.InverseLerp(impactRadiusRange.x, impactRadiusRange.y, radius);
        // Harder hits drop a touch in pitch: the flesh flattens out and sounds heavier.
        float pitch = Mathf.Lerp(tierPitch.x, tierPitch.y, size)
            * UnityEngine.Random.Range(impactPitch.x, impactPitch.y)
            * Mathf.Lerp(1.04f, 0.94f, strength);

        PlayVoice(fleshClips.Next(), volume * gain, pitch, PanFor(worldPosition), false);
    }

    private void PlayMergeInternal(Vector3 worldPosition, int resultTierIndex, int maxTierIndex)
    {
        int mergesInWindow = CountRecent(recentMergeTimes);
        if (mergesInWindow > cascadeFullVoices * 3)
        {
            // Deep chain reaction: the pool is already full of squelches, more just smears into noise.
            return;
        }

        bool ducked = mergesInWindow > cascadeFullVoices;
        float gain = AudioVolumeSettings.GetGain(AudioChannel.Game) * (ducked ? cascadeDuck : 1f);
        float basePitch = TierPitch(resultTierIndex, maxTierIndex);
        float pan = PanFor(worldPosition);

        AudioClip main = fleshClips.Next();
        PlayVoice(main, merge.RandomVolume() * gain, basePitch * merge.RandomPitch(), pan, false);

        if (ducked)
        {
            return;
        }

        AudioClip under = fleshClips.Next();
        if (under == main)
        {
            return;
        }

        float delay = UnityEngine.Random.Range(mergeUnderlayerDelay.x, mergeUnderlayerDelay.y);
        pendingPlays.Add(new PendingPlay(
            Time.unscaledTime + delay,
            under,
            mergeUnderlayer.RandomVolume() * gain,
            basePitch * mergeUnderlayer.RandomPitch(),
            -pan * 0.5f));
    }

    private void PlayTopTierPopInternal(Vector3 worldPosition)
    {
        float gain = AudioVolumeSettings.GetGain(AudioChannel.Game);
        float pan = PanFor(worldPosition);

        PlayVoice(fleshClips.Next(), topTierPop.RandomVolume() * gain, topTierPop.RandomPitch(), pan, false);
        PlayVoice(fleshClips.Next(), topTierPop.RandomVolume() * 0.7f * gain, topTierPop.RandomPitch() * 0.8f, -pan, false);
        pendingPlays.Add(new PendingPlay(
            Time.unscaledTime + mergeUnderlayerDelay.y * 2f,
            fleshClips.Next(),
            mergeUnderlayer.RandomVolume() * gain,
            mergeUnderlayer.RandomPitch() * 0.85f,
            0f));
    }

    private void PlayApparitionInternal(ApparitionSound sound, Vector3 worldPosition, float depth01)
    {
        int kind = (int)sound;
        float now = Time.unscaledTime;
        float minInterval = sound == ApparitionSound.Blink ? apparitionMinInterval * 0.5f : apparitionMinInterval;
        if (now - lastApparitionTimes[kind] < minInterval)
        {
            return;
        }

        Variation variation;
        AudioClip clip;
        switch (sound)
        {
            case ApparitionSound.Open:
                variation = apparitionOpen;
                clip = apparitionClips.Next();
                break;
            case ApparitionSound.Retreat:
                variation = apparitionRetreat;
                clip = apparitionClips.Next();
                break;
            case ApparitionSound.Blink:
                variation = apparitionBlink;
                clip = apparitionBlinkClips.Next();
                break;
            default:
                variation = apparitionEmerge;
                clip = apparitionClips.Next();
                break;
        }

        if (clip == null)
        {
            return;
        }

        lastApparitionTimes[kind] = now;
        float distance = Mathf.Lerp(1f, apparitionFarVolume, Mathf.Clamp01(depth01));
        float gain = AudioVolumeSettings.GetGain(AudioChannel.Game);
        PlayVoice(clip, variation.RandomVolume() * distance * gain, variation.RandomPitch(), PanFor(worldPosition), false, true);
    }

    private void PlayHeartbeatInternal(bool isLub, float surge)
    {
        AudioClip clip = isLub ? heartbeatLub : heartbeatDub;
        if (clip == null || heartbeatSource == null)
        {
            return;
        }

        Variation variation = isLub ? heartbeatLubVariation : heartbeatDubVariation;
        float boost = Mathf.Lerp(1f, heartbeatSurgeBoost, Mathf.Clamp01(surge));
        float volume = variation.RandomVolume() * boost * AudioVolumeSettings.GetGain(AudioChannel.Game);
        heartbeatSource.pitch = variation.RandomPitch();
        heartbeatSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private void PlayUiHoverInternal()
    {
        float now = Time.unscaledTime;
        if (now - lastHoverTime < hoverMinInterval)
        {
            return;
        }

        lastHoverTime = now;
        PlayUi(hoverClip, hover);
    }

    private void PlayUi(AudioClip clip, Variation variation)
    {
        float gain = AudioVolumeSettings.GetGain(AudioChannel.Ui);
        PlayVoice(clip, variation.RandomVolume() * gain, variation.RandomPitch(), 0f, true);
    }

    private void FlushPendingPlays()
    {
        if (pendingPlays.Count == 0)
        {
            return;
        }

        float now = Time.unscaledTime;
        for (int i = pendingPlays.Count - 1; i >= 0; i--)
        {
            PendingPlay pending = pendingPlays[i];
            if (pending.FireTime > now)
            {
                continue;
            }

            pendingPlays.RemoveAt(i);
            PlayVoice(pending.Clip, pending.Volume, pending.Pitch, pending.Pan, false);
        }
    }

    private void PlayVoice(AudioClip clip, float volume, float pitch, float pan, bool isUi, bool isAmbient = false)
    {
        if (clip == null || volume <= 0.0001f || voices == null)
        {
            return;
        }

        int index = AcquireVoiceIndex(!isAmbient);
        if (index < 0)
        {
            return;
        }

        AudioSource voice = voices[index];
        voice.Stop();
        voice.clip = clip;
        voice.volume = Mathf.Clamp01(volume);
        voice.pitch = Mathf.Max(MinAudiblePitch, pitch);
        voice.panStereo = pan;
        voice.ignoreListenerPause = isUi;
        voice.priority = isUi ? 32 : isAmbient ? 220 : 128;
        voice.Play();
        voiceStartTimes[index] = Time.unscaledTime;
    }

    /// <summary>A free voice, else the oldest when <paramref name="allowSteal"/> is set, else -1.</summary>
    private int AcquireVoiceIndex(bool allowSteal = true)
    {
        int oldest = 0;
        for (int i = 0; i < voices.Length; i++)
        {
            if (!voices[i].isPlaying)
            {
                return i;
            }

            if (voiceStartTimes[i] < voiceStartTimes[oldest])
            {
                oldest = i;
            }
        }

        // Stealing the oldest cuts a tail that is already mostly decayed.
        return allowSteal ? oldest : -1;
    }

    /// <summary>Records an event now and returns how many landed inside the cascade window, this one included.</summary>
    private int CountRecent(Queue<float> times)
    {
        float now = Time.unscaledTime;
        while (times.Count > 0 && now - times.Peek() > cascadeWindow)
        {
            times.Dequeue();
        }

        times.Enqueue(now);
        return times.Count;
    }

    private float TierPitch(int tierIndex, int maxTierIndex)
    {
        float t = maxTierIndex > 0 ? Mathf.Clamp01(tierIndex / (float)maxTierIndex) : 0f;
        return Mathf.Lerp(tierPitch.x, tierPitch.y, t);
    }

    private float PanFor(Vector3 worldPosition)
    {
        Camera camera = Camera.main;
        if (camera == null || stereoSpread <= 0f)
        {
            return 0f;
        }

        float viewportX = camera.WorldToViewportPoint(worldPosition).x;
        return Mathf.Clamp(((viewportX * 2f) - 1f) * stereoSpread, -1f, 1f);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        pendingPlays.Clear();
        HookSceneUi();
        // Views that build their buttons in Start (level select) are only there a frame later.
        StartCoroutine(HookSceneUiNextFrame());
    }

    private IEnumerator HookSceneUiNextFrame()
    {
        yield return null;
        HookSceneUi();
    }

    private static void HookSceneUi()
    {
        Selectable[] selectables = FindObjectsByType<Selectable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < selectables.Length; i++)
        {
            EnsureHooks(selectables[i]);
        }
    }

    private static void EnsureHooks(Selectable selectable)
    {
        if (selectable != null && !selectable.TryGetComponent(out UiSoundHooks _))
        {
            selectable.gameObject.AddComponent<UiSoundHooks>();
        }
    }
}
