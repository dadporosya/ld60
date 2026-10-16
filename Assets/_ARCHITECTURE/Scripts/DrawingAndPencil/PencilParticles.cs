using System.Collections.Generic;
using Coffee.UIExtensions;
using UnityEngine;

/// <summary>
/// Spawns a particle effect on the pencil tip when a stroke starts, tinted with the paint under the tip.
///
/// <para>The effect is a <see cref="UIParticle"/> prefab (ParticleEffectForUGUI, <c>com.coffee.ui-particle</c>),
/// so a regular ParticleSystem renders inside the Canvas. Make one with GameObject > UI > Particle System,
/// author the look (emission, bursts, speed, gravity, size, lifetime…) on its ParticleSystem as usual, and
/// drag it into the project as a prefab. One instance is spawned per stroke: it rides the tip until the
/// stroke ends, then stops emitting and destroys itself once its last particle is gone.</para>
/// </summary>
public class PencilParticles : MonoBehaviour
{
    [SerializeField] private Pencil pencil;

    [Header("Effect")]
    [Tooltip("Prefab made with GameObject > UI > Particle System. Spawned on the tip when a stroke starts.")]
    [SerializeField] private UIParticle particlePrefab;
    [Tooltip("Where instances are spawned. Defaults to the board's root Canvas; they're put last so they " +
             "draw above the board.")]
    [SerializeField] private Transform particleParent;
    [Tooltip("On: the effect rides the tip and keeps emitting for the whole stroke, then stops. " +
             "Off: a one-shot — it plays once where the stroke started (looping is switched off).")]
    [SerializeField] private bool followTipWhileDrawing = true;
    [Tooltip("Safety net: an instance is destroyed this many seconds after its stroke ends, even if it " +
             "still has particles alive.")]
    [SerializeField, Min(0.1f)] private float maxLingerTime = 5f;

    [Header("Look")]
    [Tooltip("Tint the particles with the paint under the tip (overrides the prefab's Start Color).")]
    [SerializeField] private bool tintWithPaint = true;
    [Tooltip("Saturation is floored at this value so fresh strokes don't throw white particles on a white board.")]
    [SerializeField, Range(0f, 1f)] private float minColorSaturation = 0.3f;
    [Tooltip("Replace the particle material with the plain UI material: square, alpha-blended particles " +
             "(pixel look). The plugin's default UI/Additive material is invisible on a white board.")]
    [SerializeField] private bool usePixelMaterial = true;

    [Header("Emission")]
    [Tooltip("Scale emission (particles per second) with the saturation under the tip: the closer the cell " +
             "is to overdone, the more particles. Overrides the prefab's Rate over Time.")]
    [SerializeField] private bool emissionBySaturation = true;
    [Tooltip("Particles per second on a fresh (unsaturated) cell.")]
    [SerializeField, Min(0f)] private float minEmission = 5f;
    [Tooltip("Particles per second on a fully overdone cell (saturation 1).")]
    [SerializeField, Min(0f)] private float maxEmission = 60f;
    [Tooltip("Saturation (X, 0..1) → blend between min and max emission (Y, 0..1). The default stays low " +
             "below the Perfect zone and ramps up towards overdone. Match its keys to the zone boundaries " +
             "in the grid's SaturationSettings.")]
    [SerializeField] private AnimationCurve emissionCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.45f, 0.1f), // perfectMin: still low below this
        new Keyframe(0.75f, 0.5f), // tooMuchMin
        new Keyframe(1f, 1f));

    private class Instance
    {
        public UIParticle root;
        public ParticleSystem[] systems;
        public float releasedAt;
    }

    private Instance _current;
    private readonly List<Instance> _released = new List<Instance>();

    private void Awake()
    {
        if (!particlePrefab)
        {
            h.Out($"PencilParticles on '{name}': assign a UIParticle prefab — disabled.");
            enabled = false;
            return;
        }

        PixelGrid grid = pencil ? pencil.Grid : null;
        if (!particleParent && grid)
        {
            Canvas canvas = grid.GetComponentInParent<Canvas>();
            if (canvas) particleParent = canvas.rootCanvas.transform;
        }
    }

    private void OnEnable()
    {
        if (pencil) pencil.OnStrokeBegan += HandleStrokeBegan;
    }

    private void OnDisable()
    {
        if (pencil) pencil.OnStrokeBegan -= HandleStrokeBegan;
        ReleaseCurrent();
    }

    private void HandleStrokeBegan()
    {
        if (!pencil.Grid || !pencil.IsTipOnBoard) return;

        ReleaseCurrent();

        Instance spawned = Spawn();
        if (followTipWhileDrawing) _current = spawned;
        else Release(spawned, stopEmitting: false);
    }

    private void Update()
    {
        if (_current != null)
        {
            if (!_current.root) _current = null;
            else if (pencil.IsDrawing)
            {
                if (pencil.IsTipOnBoard)
                {
                    FollowTip(_current);
                    Tint(_current);
                }
            }
            else ReleaseCurrent();
        }

        for (int i = _released.Count - 1; i >= 0; i--)
        {
            Instance instance = _released[i];
            if (instance.root && IsAlive(instance) && Time.time - instance.releasedAt < maxLingerTime) continue;

            if (instance.root) Destroy(instance.root.gameObject);
            _released.RemoveAt(i);
        }
    }

    private Instance Spawn()
    {
        UIParticle root = Instantiate(particlePrefab, particleParent, false);
        root.transform.SetAsLastSibling();

        var instance = new Instance { root = root, systems = root.GetComponentsInChildren<ParticleSystem>(true) };

        // Emit from the system's real on-screen position and leave emitted particles where they were born
        // instead of dragging them along with the tip.
        root.positionMode = UIParticle.PositionMode.Absolute;

        foreach (ParticleSystem system in instance.systems)
        {
            ParticleSystem.MainModule main = system.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            if (!followTipWhileDrawing) main.loop = false;

            if (usePixelMaterial && system.TryGetComponent(out ParticleSystemRenderer psRenderer))
                psRenderer.sharedMaterial = Canvas.GetDefaultCanvasMaterial();
        }

        FollowTip(instance);
        Tint(instance);

        root.RefreshParticles();
        foreach (ParticleSystem system in instance.systems) system.Play(false);
        return instance;
    }

    private void FollowTip(Instance instance) => instance.root.transform.position = pencil.TipWorldPosition;

    private void Tint(Instance instance)
    {
        // The pencil's current board: the one this stroke is on.
        PixelGrid grid = pencil.Grid;
        float saturation = grid.GetCell(pencil.TipCell)?.saturation ?? 0f;

        if (tintWithPaint)
        {
            Color color = grid.settings.ColorOf(Mathf.Max(saturation, minColorSaturation));
            foreach (ParticleSystem system in instance.systems)
            {
                ParticleSystem.MainModule main = system.main;
                main.startColor = color;
            }
        }

        if (emissionBySaturation)
        {
            float rate = Mathf.Lerp(minEmission, maxEmission,
                emissionCurve.Evaluate(Mathf.Clamp01(saturation)));
            foreach (ParticleSystem system in instance.systems)
            {
                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTime = rate;
            }
        }
    }

    private void ReleaseCurrent()
    {
        if (_current == null) return;

        if (_current.root) Release(_current, stopEmitting: true);
        _current = null;
    }

    private void Release(Instance instance, bool stopEmitting)
    {
        if (stopEmitting)
            foreach (ParticleSystem system in instance.systems)
                if (system) system.Stop(false, ParticleSystemStopBehavior.StopEmitting);

        instance.releasedAt = Time.time;
        _released.Add(instance);
    }

    private static bool IsAlive(Instance instance)
    {
        foreach (ParticleSystem system in instance.systems)
            if (system && system.IsAlive(false)) return true;
        return false;
    }
}
