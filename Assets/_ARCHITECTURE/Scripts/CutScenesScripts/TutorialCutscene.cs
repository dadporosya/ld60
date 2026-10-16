using UnityEngine;

/// <summary>
/// Cutscene controller managing tutorial sequence and drawing permissions.
/// </summary>
public class TutorialCutscene : CutSceneBase
{
    public static TutorialCutscene Instance;

    /// <summary>Whether the player is currently permitted to draw during the tutorial.</summary>
    public static bool PlayerMayDraw => Instance && Instance.MayDraw;

    [Tooltip("Whether drawing is currently allowed.")]
    [SerializeField] private bool mayDraw;
    public virtual bool MayDraw => mayDraw;

    protected virtual void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
    }
}
