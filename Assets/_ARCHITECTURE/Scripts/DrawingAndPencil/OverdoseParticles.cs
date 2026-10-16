using System.Collections.Generic;
using Coffee.UIExtensions;
using UnityEngine;

/// <summary>
/// Spawns a particle effect on every cell of one <see cref="PixelGrid"/> that becomes overdone
/// (<see cref="SaturationZone.TooMuch"/>), however it got there — painted over the threshold, or crossed
/// over an existing line (<see cref="PixelGrid.Overdose"/>).
///
/// <para>Each effect sits on its cell and emits for a random time between <see cref="minLifetime"/> and
/// <see cref="maxLifetime"/> seconds, then stops emitting and destroys itself once its last particle is gone.
/// It stops early if the cell stops being overdone (e.g. the board is wiped). While it runs, the more overdone
/// its cell, the more it emits (towards <see cref="maxEmission"/>). Put one of these next to each board, each
/// with its own prefab, to give every board its own overdose look.</para>
///
/// <para>The effect is a <see cref="UIParticle"/> prefab (GameObject > UI > Particle System, saved as a
/// prefab) — see <see cref="PencilParticles"/>. With the prefab's Auto Scaling Mode on Transform, UIParticle
/// drives the instance's scale to cancel out the canvas scale, so an instance shows a scale of ~100 in the
/// inspector under a Screen Space - Camera canvas; that is expected and doesn't change its on-screen size.</para>
/// </summary>
public class OverdoseParticles : MonoBehaviour
{
    [SerializeField] private PixelGrid grid;

    [Header("Effect")]
    [Tooltip("UIParticle prefab spawned on each cell of this board that becomes overdone.")]
    [SerializeField] private UIParticle particlePrefab;
    [Tooltip("Where effects are spawned. Defaults to the board, so they draw above its pixels.")]
    [SerializeField] private Transform particleParent;
    [Tooltip("Replace the particle material with the plain UI material (square, alpha-blended particles).")]
    [SerializeField] private bool usePixelMaterial = true;
    [Tooltip("Tint the particles with the cell's colour (overrides the prefab's Start Color).")]
    [SerializeField] private bool tintWithPaint = true;

    [Header("Lifetime")]
    [Tooltip("Shortest time (seconds) an effect keeps emitting after its cell becomes overdone.")]
    [SerializeField, Min(0f)] private float minLifetime = 3f;
    [Tooltip("Longest time (seconds) an effect keeps emitting after its cell becomes overdone.")]
    [SerializeField, Min(0f)] private float maxLifetime = 5f;
    [Tooltip("Safety net: an effect is destroyed this many seconds after it stops emitting, even if it " +
             "still has particles alive.")]
    [SerializeField, Min(0.1f)] private float maxLingerTime = 5f;
    [Tooltip("Most effects alive at once on this board; cells overdone past that get none.")]
    [SerializeField, Min(1)] private int maxAlive = 40;

    [Header("Scaling by overdose (tooMuchMin → 1)")]
    [Tooltip("Particles per second right at the overdose threshold.")]
    [SerializeField, Min(0f)] private float minEmission = 3f;
    [Tooltip("Particles per second on a fully saturated cell.")]
    [SerializeField, Min(0f)] private float maxEmission = 30f;
    [Tooltip("Overdose (X, 0..1) → blend between min and max emission (Y, 0..1).")]
    [SerializeField] private AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    private class Instance
    {
        public UIParticle root;
        public ParticleSystem[] systems;
        public float stopAt;
        public float releasedAt;
    }

    // Emitting effects by cell, and effects that stopped emitting and wait for their particles to die.
    private readonly Dictionary<Vector2Int, Instance> _emitting = new Dictionary<Vector2Int, Instance>();
    private readonly List<Instance> _released = new List<Instance>();
    private readonly List<Vector2Int> _toRelease = new List<Vector2Int>();

    private void Awake()
    {
        if (!particlePrefab || !grid)
        {
            h.Out($"OverdoseParticles on '{name}': assign the grid and a UIParticle prefab — disabled.");
            enabled = false;
            return;
        }

        if (!particleParent) particleParent = grid.transform;
    }

    private void OnEnable()
    {
        if (grid) grid.OnCellZoneChanged += HandleCellZoneChanged;
    }

    private void OnDisable()
    {
        if (grid) grid.OnCellZoneChanged -= HandleCellZoneChanged;
        foreach (Vector2Int cell in new List<Vector2Int>(_emitting.Keys)) Release(cell);
    }

    private void OnDestroy()
    {
        foreach (Instance instance in _emitting.Values)
            if (instance.root) Destroy(instance.root.gameObject);
        foreach (Instance instance in _released)
            if (instance.root) Destroy(instance.root.gameObject);
        _emitting.Clear();
        _released.Clear();
    }

    private void HandleCellZoneChanged(Vector2Int cell, SaturationZone from, SaturationZone to)
    {
        if (to != SaturationZone.TooMuch || from == SaturationZone.TooMuch) return;

        // Already running on this cell (it left the overdose and came back, e.g. after a wipe): start over.
        if (_emitting.TryGetValue(cell, out Instance running) && running.root)
        {
            running.stopAt = Time.time + RandomLifetime();
            return;
        }

        if (_emitting.Count + _released.Count >= maxAlive) return;
        _emitting[cell] = Spawn(cell);
    }

    private void Update()
    {
        _toRelease.Clear();
        foreach (KeyValuePair<Vector2Int, Instance> pair in _emitting)
        {
            Instance instance = pair.Value;
            PixelCell cell = grid.GetCell(pair.Key);
            if (!instance.root || Time.time >= instance.stopAt || cell == null || cell.zone != SaturationZone.TooMuch)
            {
                _toRelease.Add(pair.Key);
                continue;
            }

            Apply(instance, cell.saturation);
        }

        foreach (Vector2Int cell in _toRelease) Release(cell);

        for (int i = _released.Count - 1; i >= 0; i--)
        {
            Instance instance = _released[i];
            if (instance.root && IsAlive(instance) && Time.time - instance.releasedAt < maxLingerTime) continue;

            if (instance.root) Destroy(instance.root.gameObject);
            _released.RemoveAt(i);
        }
    }

    private Instance Spawn(Vector2Int cell)
    {
        UIParticle root = Instantiate(particlePrefab, particleParent, false);
        root.transform.SetAsLastSibling();
        root.transform.position = grid.CellToWorld(cell);
        // Emit from the system's real on-screen position.
        root.positionMode = UIParticle.PositionMode.Absolute;

        var instance = new Instance
        {
            root = root,
            systems = root.GetComponentsInChildren<ParticleSystem>(true),
            stopAt = Time.time + RandomLifetime(),
        };

        foreach (ParticleSystem system in instance.systems)
        {
            // Loops for as long as the effect lives; Release stops it. World space keeps particles where they
            // were born.
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            if (usePixelMaterial && system.TryGetComponent(out ParticleSystemRenderer psRenderer))
                psRenderer.sharedMaterial = Canvas.GetDefaultCanvasMaterial();
        }

        Apply(instance, grid.GetCell(cell)?.saturation ?? grid.settings.tooMuchMin);

        root.RefreshParticles();
        foreach (ParticleSystem system in instance.systems) system.Play(false);
        return instance;
    }

    /// <summary>Emission and tint from the cell's current saturation.</summary>
    private void Apply(Instance instance, float saturation)
    {
        float rate = Mathf.Lerp(minEmission, maxEmission, Blend(saturation));
        Color color = grid.settings.ColorOf(saturation);
        foreach (ParticleSystem system in instance.systems)
        {
            if (!system) continue;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = rate;

            if (!tintWithPaint) continue;
            ParticleSystem.MainModule main = system.main;
            main.startColor = color;
        }
    }

    private void Release(Vector2Int cell)
    {
        if (!_emitting.Remove(cell, out Instance instance) || !instance.root) return;

        foreach (ParticleSystem system in instance.systems)
            if (system) system.Stop(false, ParticleSystemStopBehavior.StopEmitting);

        instance.releasedAt = Time.time;
        _released.Add(instance);
    }

    private float RandomLifetime() => Random.Range(Mathf.Min(minLifetime, maxLifetime), Mathf.Max(minLifetime, maxLifetime));

    private float Blend(float saturation)
    {
        float min = grid.settings.tooMuchMin;
        float t = min >= 1f ? 1f : Mathf.InverseLerp(min, 1f, saturation);
        return Mathf.Clamp01(curve.Evaluate(t));
    }

    private static bool IsAlive(Instance instance)
    {
        foreach (ParticleSystem system in instance.systems)
            if (system && system.IsAlive(false)) return true;
        return false;
    }
}
