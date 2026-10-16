using UnityEngine;

/// <summary>
/// Holds the one number the rest of the game reads when it wants to get harder over time: a
/// <see cref="Difficulty"/> from 0 (easiest) to 100 (hardest).
///
/// <para>Nothing here decides what "harder" means — callers do, by asking <see cref="Scale"/> for the
/// point between their own easy and hard values. That keeps the tuning next to the thing being tuned:
/// see <see cref="PopupManagerBase{TPopup}.CurrentSpawnRate"/>, which reads its gap as
/// <c>Scale(spawnRateAtMinDifficulty, spawnRateAtMaxDifficulty)</c> before every spawn, so a difficulty
/// change lands on the very next one.</para>
///
/// <para>The scale runs 0..100 rather than 0..1 because it is authored by hand in the inspector and read
/// in the log — <c>60</c> is easier to reason about mid-playtest than <c>0.6</c>.</para>
///
/// <para>Entirely optional: everything that reads difficulty is written to cope with no manager in the
/// scene (see the fallbacks at each call site), so a scene that does not scale difficulty simply does
/// not have one.</para>
/// </summary>
public class DifficultyManager : MonoBehaviour
{
    public static DifficultyManager Instance;

    /// <summary>Easiest end of the scale.</summary>
    public const float MinDifficulty = 0f;

    /// <summary>Hardest end of the scale.</summary>
    public const float MaxDifficulty = 100f;

    [Tooltip("Current difficulty, 0 (easiest) to 100 (hardest). Drive it from a curve, a timer or a " +
             "cutscene — everything that scales with difficulty re-reads it continuously.")]
    [SerializeField, Range(MinDifficulty, MaxDifficulty)]
    private float difficulty = MinDifficulty;

    /// <summary>Raised with the new difficulty whenever it changes.</summary>
    public event System.Action<float> OnDifficultyChanged;

    /// <summary>The current difficulty, always clamped to 0..100.</summary>
    public float Difficulty
    {
        get => difficulty;
        set => SetDifficulty(value);
    }

    /// <summary>The current difficulty as a 0..1 fraction — what <see cref="Scale"/> interpolates by.</summary>
    public float Normalized => Mathf.InverseLerp(MinDifficulty, MaxDifficulty, difficulty);

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance, false);
    }

    /// <summary>
    /// The point between <paramref name="atMinDifficulty"/> and <paramref name="atMaxDifficulty"/> that
    /// the current difficulty sits at. Nothing is assumed about which end is the bigger number, so a
    /// value that shrinks as the game gets harder (a spawn gap) reads exactly like one that grows.
    /// </summary>
    public float Scale(float atMinDifficulty, float atMaxDifficulty) =>
        Mathf.Lerp(atMinDifficulty, atMaxDifficulty, Normalized);

    /// <summary>Sets the difficulty, clamped to the scale. Fires <see cref="OnDifficultyChanged"/>.</summary>
    public void SetDifficulty(float value)
    {
        float clamped = Mathf.Clamp(value, MinDifficulty, MaxDifficulty);
        if (Mathf.Approximately(clamped, difficulty)) return;

        difficulty = clamped;
        OnDifficultyChanged?.Invoke(difficulty);
    }

    /// <summary>Moves the difficulty by <paramref name="delta"/>, clamped to the scale.</summary>
    public void ChangeDifficulty(float delta) => SetDifficulty(difficulty + delta);

#if UNITY_EDITOR
    // So dragging the slider while playing takes effect the same way a scripted change does.
    private void OnValidate()
    {
        difficulty = Mathf.Clamp(difficulty, MinDifficulty, MaxDifficulty);

        if (Application.isPlaying) OnDifficultyChanged?.Invoke(difficulty);
    }
#endif
}
