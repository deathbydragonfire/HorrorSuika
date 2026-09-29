using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Surfaces eyes and lurkers out of the black fog at random places in view, keeps them from
/// overlapping, and pools them. Every spawn rolls a depth: near ones come out large and clear,
/// far ones small and half swallowed by the dark. Eyes sometimes come as a matched pair (a face
/// in the fog) or a swarm of tiny ones far back. A surge bursts extra eyes in at once.
/// </summary>
public class BackgroundApparitionSpawner : MonoBehaviour
{
    [Serializable]
    public class Channel
    {
        [Tooltip("Prefabs picked at random for this channel.")]
        public BackgroundApparition[] prefabs;

        [Min(0), Tooltip("Most of this channel alive at once.")]
        public int maxAlive = 5;

        [Tooltip("Seconds before the first spawn, randomised between x and y.")]
        public Vector2 firstDelay = new Vector2(0.5f, 2f);

        [Tooltip("Seconds between spawns, randomised between x and y.")]
        public Vector2 spawnDelay = new Vector2(1.5f, 5f);

        [Tooltip("Distance from the camera once fully out: x nearest, y farthest. Keep it between the fog and backdrop distances.")]
        public Vector2 distance = new Vector2(22f, 40f);

        [Tooltip("Uniform scale at the nearest distance (x) and the farthest (y). Near ones are bigger.")]
        public Vector2 scaleByDepth = new Vector2(9f, 2.5f);

        [Range(0f, 0.6f), Tooltip("Random spread applied on top of the depth scale, as a fraction.")]
        public float scaleJitter = 0.25f;

        [Min(0.1f), Tooltip("Depth bias. Above 1 favours far spawns, below 1 favours near ones.")]
        public float depthBias = 1.3f;

        [Tooltip("Roll in degrees about the view axis, randomised between -x and x.")]
        public float roll = 18f;

        [Range(0f, 1f), Tooltip("Chance of a hard 90 degree roll, for a vertical slit.")]
        public float sidewaysChance = 0.1f;

        [Range(0f, 1f), Tooltip("Chance a spawn is a matched pair that looks and blinks together, like a face in the dark. Eyes only.")]
        public float pairChance = 0f;

        [Range(0f, 1f), Tooltip("Chance a spawn is a swarm of small ones pushed far back.")]
        public float clusterChance = 0f;

        [Tooltip("Members in a swarm, randomised between x and y inclusive.")]
        public Vector2Int clusterSize = new Vector2Int(3, 5);

        [Range(0.1f, 1f), Tooltip("Scale multiplier for swarm members on top of their depth scale.")]
        public float clusterScale = 0.55f;

        [NonSerialized] public float timer;
        [NonSerialized] public readonly List<BackgroundApparition> alive = new List<BackgroundApparition>();
    }

    private const int PlacementAttempts = 18;

    [SerializeField, Tooltip("Backdrop that fits the camera and drives surges. Found in the scene when empty.")]
    private HorrorBackground background;

    [SerializeField] private Channel eyes = new Channel
    {
        pairChance = 0.2f,
        clusterChance = 0.15f
    };

    [SerializeField] private Channel lurkers = new Channel
    {
        maxAlive = 2,
        firstDelay = new Vector2(4f, 8f),
        spawnDelay = new Vector2(7f, 15f),
        distance = new Vector2(32f, 42f),
        scaleByDepth = new Vector2(1.7f, 0.9f),
        roll = 180f,
        depthBias = 1f
    };

    [SerializeField, Range(1, HorrorBackground.MaxEyes), Tooltip("Most apparitions of any kind on screen at once, eyes and lurkers combined. Surges respect it too.")]
    private int maxTotalAlive = 3;

    [SerializeField, Min(0f), Tooltip("World units kept clear of the view edge.")]
    private float edgeInset = 0.15f;

    [SerializeField, Min(0.5f), Tooltip("Minimum gap between apparitions as a multiple of their combined radii.")]
    private float spacing = 1.05f;

    [SerializeField, Range(0f, 1f), Tooltip("Chance a placement inside the container is rejected so eyes favour the margins. Tall screens have little margin, so it still falls back to the container.")]
    private float avoidPlayfield = 0.5f;

    [SerializeField, Min(0), Tooltip("Extra eyes forced out when a surge starts.")]
    private int surgeBurst = 3;

    [SerializeField, Min(1f), Tooltip("Gap between the two eyes of a pair, in eye radii centre to centre.")]
    private float pairSeparation = 2.7f;

    private readonly Dictionary<BackgroundApparition, Stack<BackgroundApparition>> pools = new Dictionary<BackgroundApparition, Stack<BackgroundApparition>>();
    private readonly Dictionary<BackgroundApparition, BackgroundApparition> prefabOf = new Dictionary<BackgroundApparition, BackgroundApparition>();
    private readonly Dictionary<BackgroundApparition, Channel> channelOf = new Dictionary<BackgroundApparition, Channel>();

    private void OnEnable()
    {
        if (background == null)
        {
            background = FindFirstObjectByType<HorrorBackground>();
        }

        if (background != null)
        {
            background.SurgeStarted += OnSurgeStarted;
        }

        eyes.timer = RandomRange(eyes.firstDelay);
        lurkers.timer = RandomRange(lurkers.firstDelay);
    }

    private void OnDisable()
    {
        if (background != null)
        {
            background.SurgeStarted -= OnSurgeStarted;
        }
    }

    private void Update()
    {
        Tick(eyes, Time.deltaTime);
        Tick(lurkers, Time.deltaTime);
    }

    private void Tick(Channel channel, float dt)
    {
        if (channel.prefabs == null || channel.prefabs.Length == 0)
        {
            return;
        }

        channel.timer -= dt;
        if (channel.timer > 0f)
        {
            return;
        }

        channel.timer = RandomRange(channel.spawnDelay);
        if (channel.alive.Count >= channel.maxAlive || TotalAlive() >= TotalCap)
        {
            return;
        }

        float roll = UnityEngine.Random.value;
        if (roll < channel.clusterChance)
        {
            SpawnCluster(channel);
        }
        else if (roll < channel.clusterChance + channel.pairChance
            && channel.alive.Count + 2 <= channel.maxAlive
            && TotalAlive() + 2 <= TotalCap)
        {
            SpawnPair(channel);
        }
        else
        {
            TrySpawn(channel, PickPrefab(channel), RollDepth(channel), 1f);
        }
    }

    private void OnSurgeStarted()
    {
        // Surge eyes favour the near half so the burst lands right in the player's face.
        for (int i = 0; i < surgeBurst && TotalAlive() < TotalCap; i++)
        {
            TrySpawn(eyes, PickPrefab(eyes), RollDepth(eyes) * 0.6f, 1f);
        }
    }

    private void SpawnCluster(Channel channel)
    {
        int count = UnityEngine.Random.Range(channel.clusterSize.x, channel.clusterSize.y + 1);
        float depth = UnityEngine.Random.Range(0.7f, 1f);
        BackgroundApparition prefab = PickPrefab(channel);
        BackgroundApparition first = TrySpawn(channel, prefab, depth, channel.clusterScale);
        if (first == null)
        {
            return;
        }

        Vector3 anchor = first.RestPosition;
        float swarmRadius = first.WorldRadius * 4.5f;
        // Swarm members count against the channel cap like any other spawn.
        for (int i = 1; i < count && channel.alive.Count < channel.maxAlive && TotalAlive() < TotalCap; i++)
        {
            // Members scatter in depth too, so the swarm has thickness.
            float memberDepth = Mathf.Clamp01(depth + UnityEngine.Random.Range(-0.15f, 0.1f));
            TrySpawn(channel, UnityEngine.Random.value < 0.7f ? prefab : PickPrefab(channel), memberDepth, channel.clusterScale, anchor, swarmRadius);
        }
    }

    private void SpawnPair(Channel channel)
    {
        BackgroundApparition prefab = PickPrefab(channel);
        BackgroundApparition first = TrySpawn(channel, prefab, RollDepth(channel), 1f);
        if (!(first is BackgroundEye firstEye))
        {
            return;
        }

        Transform frame = first.transform;
        float gap = first.WorldRadius * pairSeparation;
        Vector3 side = UnityEngine.Random.value < 0.5f ? frame.right : -frame.right;
        Vector3 partnerPosition = first.RestPosition + side * gap;
        if (!IsInView(partnerPosition, first.WorldRadius) || Overlaps(partnerPosition, first.WorldRadius, first))
        {
            partnerPosition = first.RestPosition - side * gap;
            if (!IsInView(partnerPosition, first.WorldRadius) || Overlaps(partnerPosition, first.WorldRadius, first))
            {
                return;
            }
        }

        BackgroundApparition partner = Rent(prefab);
        float roll = Vector3.SignedAngle(background.ViewCamera.transform.up, frame.up, background.ViewCamera.transform.forward);
        Launch(channel, partner, partnerPosition, first.WorldRadius / Mathf.Max(0.0001f, partner.BaseRadius), roll);
        if (partner is BackgroundEye partnerEye)
        {
            partnerEye.FollowGaze(firstEye);
        }
    }

    private BackgroundApparition TrySpawn(Channel channel, BackgroundApparition prefab, float depth, float scaleMultiplier, Vector3? anchor = null, float anchorRadius = 0f)
    {
        if (prefab == null || background == null)
        {
            return null;
        }

        Camera cam = background.ViewCamera;
        if (cam == null)
        {
            return null;
        }

        depth = Mathf.Clamp01(depth);
        float distance = Mathf.Lerp(channel.distance.x, channel.distance.y, depth);
        float scale = Mathf.Lerp(channel.scaleByDepth.x, channel.scaleByDepth.y, depth)
            * scaleMultiplier
            * (1f + UnityEngine.Random.Range(-channel.scaleJitter, channel.scaleJitter));

        BackgroundApparition instance = Rent(prefab);
        float radius = instance.BaseRadius * scale;
        if (!TryFindPlacement(cam, distance, radius, anchor, anchorRadius, out Vector3 position))
        {
            Return(instance);
            return null;
        }

        float roll = UnityEngine.Random.Range(-channel.roll, channel.roll);
        if (UnityEngine.Random.value < channel.sidewaysChance)
        {
            roll += UnityEngine.Random.value < 0.5f ? 90f : -90f;
        }

        Launch(channel, instance, position, scale, roll);
        return instance;
    }

    private void Launch(Channel channel, BackgroundApparition instance, Vector3 position, float scale, float roll)
    {
        channelOf[instance] = channel;
        channel.alive.Add(instance);
        instance.Begin(background, background.ViewCamera, position, scale, roll, OnApparitionFinished);
    }

    private bool TryFindPlacement(Camera cam, float distance, float radius, Vector3? anchor, float anchorRadius, out Vector3 position)
    {
        Transform view = cam.transform;
        Vector2 half = background.GetViewHalfExtents(distance);
        Vector3 centre = view.position + view.forward * distance;
        float marginX = Mathf.Max(0f, half.x - radius - edgeInset);
        float marginY = Mathf.Max(0f, half.y - radius - edgeInset);
        Rect calm = background.PlayfieldRect;

        for (int attempt = 0; attempt < PlacementAttempts; attempt++)
        {
            Vector3 candidate;
            if (anchor.HasValue)
            {
                Vector2 jitter = UnityEngine.Random.insideUnitCircle * anchorRadius;
                Vector3 anchorAtDepth = anchor.Value + view.forward * (distance - Vector3.Dot(anchor.Value - view.position, view.forward));
                candidate = anchorAtDepth + view.right * jitter.x + view.up * jitter.y;
            }
            else
            {
                candidate = centre
                    + view.right * UnityEngine.Random.Range(-marginX, marginX)
                    + view.up * UnityEngine.Random.Range(-marginY, marginY);
            }

            bool lastChance = attempt >= PlacementAttempts - 4;
            if (!lastChance && calm.width > 0f && calm.Contains(new Vector2(candidate.x, candidate.y))
                && UnityEngine.Random.value < avoidPlayfield)
            {
                continue;
            }

            if (Overlaps(candidate, radius, null))
            {
                continue;
            }

            position = candidate;
            return true;
        }

        position = default;
        return false;
    }

    private bool IsInView(Vector3 position, float radius)
    {
        Camera cam = background.ViewCamera;
        Transform view = cam.transform;
        Vector3 offset = position - view.position;
        Vector2 half = background.GetViewHalfExtents(Vector3.Dot(offset, view.forward));
        return Mathf.Abs(Vector3.Dot(offset, view.right)) <= half.x - radius
            && Mathf.Abs(Vector3.Dot(offset, view.up)) <= half.y - radius;
    }

    private bool Overlaps(Vector3 candidate, float radius, BackgroundApparition ignore)
    {
        var active = BackgroundApparition.Active;
        for (int i = 0; i < active.Count; i++)
        {
            BackgroundApparition other = active[i];
            if (other == null || other == ignore || !other.IsAlive)
            {
                continue;
            }

            Vector2 delta = (Vector2)(candidate - other.transform.position);
            float minGap = (radius + other.WorldRadius) * spacing;
            if (delta.sqrMagnitude < minGap * minGap)
            {
                return true;
            }
        }

        return false;
    }

    private void OnApparitionFinished(BackgroundApparition apparition)
    {
        if (channelOf.TryGetValue(apparition, out Channel channel))
        {
            channel.alive.Remove(apparition);
            channelOf.Remove(apparition);
        }

        Return(apparition);
    }

    private BackgroundApparition Rent(BackgroundApparition prefab)
    {
        if (pools.TryGetValue(prefab, out Stack<BackgroundApparition> pool) && pool.Count > 0)
        {
            return pool.Pop();
        }

        BackgroundApparition instance = Instantiate(prefab, transform);
        instance.gameObject.SetActive(false);
        prefabOf[instance] = prefab;
        return instance;
    }

    private void Return(BackgroundApparition instance)
    {
        instance.gameObject.SetActive(false);
        if (!prefabOf.TryGetValue(instance, out BackgroundApparition prefab))
        {
            Destroy(instance.gameObject);
            return;
        }

        if (!pools.TryGetValue(prefab, out Stack<BackgroundApparition> pool))
        {
            pool = new Stack<BackgroundApparition>();
            pools[prefab] = pool;
        }

        pool.Push(instance);
    }

    private static BackgroundApparition PickPrefab(Channel channel)
    {
        if (channel.prefabs == null || channel.prefabs.Length == 0)
        {
            return null;
        }

        return channel.prefabs[UnityEngine.Random.Range(0, channel.prefabs.Length)];
    }

    private static float RollDepth(Channel channel)
    {
        return Mathf.Pow(UnityEngine.Random.value, 1f / Mathf.Max(0.1f, channel.depthBias));
    }

    private int TotalCap => Mathf.Clamp(maxTotalAlive, 1, HorrorBackground.MaxEyes);

    private int TotalAlive()
    {
        return eyes.alive.Count + lurkers.alive.Count;
    }

    private static float RandomRange(Vector2 range)
    {
        return UnityEngine.Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }
}
