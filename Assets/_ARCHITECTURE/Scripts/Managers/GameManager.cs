using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Core game manager handling game events, damage notifications, drawing state, and game rules.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Board")]
    [Tooltip("The primary PixelGrid board for drawing in the scene.")]
    [SerializeField] private PixelGrid mainBoard;
    public virtual PixelGrid MainBoard => mainBoard;

    [Header("Events")]
    [Tooltip("Fired when the player takes damage, passing the damage amount.")]
    public UnityEvent<float> onDamaged = new UnityEvent<float>();

    [Header("State")]
    [SerializeField] private bool isOver;
    [SerializeField] private bool isShaking;
    [SerializeField] private bool drawingThroughDialogue;

    public virtual bool IsOver => isOver;
    public virtual bool IsShaking => isShaking;
    public virtual bool DrawingThroughDialogue => drawingThroughDialogue;

    public virtual bool MayDrawAfterWin(PixelGrid board) => false;
    public virtual void SuspendRules() {}
    public virtual void ResumeRules() {}
    public virtual void StartAfkTimer() {}

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
    }
}
