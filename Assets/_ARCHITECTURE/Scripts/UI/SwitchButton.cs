using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// A two-state button: <b>on</b> or <b>off</b>, with a sprite for each.
///
/// <para><b>Operating principle.</b> Put it on a UI <see cref="Button"/> and give it the two sprites.
/// Every click flips the state, the matching sprite is written into the image, and
/// <see cref="onValueChanged"/> fires with the new value — that event is the whole point of the
/// component, and what anything that cares about the setting listens to:</para>
///
/// <code>
/// switchButton.onValueChanged.AddListener(on => manager.something = on); // react to the player
/// switchButton.SetOnWithoutNotify(manager.something);                    // show the current value
/// switchButton.Toggle();                                                 // flip it from code
/// </code>
///
/// <para><b>The sprite.</b> Written into <see cref="targetImage"/> — the image on this object unless
/// another one is assigned, so the switch can just as well drive a child (a lamp, a knob) while the
/// button keeps its own background. A world-space <see cref="SpriteRenderer"/> works too. Leaving
/// <see cref="offSprite"/> empty means "whatever sprite the object already has" — the usual setup, where
/// the scene is authored in the off state and only the on sprite has to be assigned.</para>
///
/// <para><b>State vs. events.</b> <see cref="SetOn"/> tells listeners; <see cref="SetOnWithoutNotify"/>
/// does not. Use the second one when the switch is being brought in line with a value it did not change
/// itself (a settings window opening), so putting the current state on screen does not read as the
/// player having just pressed it.</para>
///
/// <para>Do not put this on the same object as a <see cref="ButtonImageSwapper"/>: both write the same
/// image's sprite and the last one to run wins. The hover/press sprites belong to the swapper, the
/// on/off sprites here — one object, one owner of the sprite.</para>
/// </summary>
[DisallowMultipleComponent]
public class SwitchButton : MonoBehaviour
{
    // ================================================================ Inspector

    [Header("State")]
    [Tooltip("The state the switch starts in. Flipping it in the inspector while the game is not running " +
             "updates the sprite too, so the scene shows what it will look like.")]
    [SerializeField] private bool isOn = false;

    [Header("Sprites")]
    [Tooltip("Sprite shown while the switch is OFF. Empty = the sprite the target already has when the " +
             "game starts, which is the usual setup: author the scene switched off and only fill in the " +
             "on sprite.")]
    public Sprite offSprite;

    [Tooltip("Sprite shown while the switch is ON.")]
    public Sprite onSprite;

    [Tooltip("Optional. The image the sprite is written into. Empty = the Image (or SpriteRenderer) on " +
             "this object. Assign a child to let the switch drive a lamp or a knob while the button " +
             "keeps its own background.")]
    public Image targetImage;

    [Tooltip("Resize the image to the sprite's native size after every swap. Only for sprites of " +
             "different sizes — a normal on/off pair is the same size and needs this off.")]
    public bool setNativeSize = false;

    [Header("Behaviour")]
    [Tooltip("Flip the state on every click of the Button on this object. Off = the state is only ever " +
             "changed from code (SetOn / Toggle), and the button is free to do something else.")]
    public bool toggleOnClick = true;

    [Tooltip("Optional. Sound played on every flip that came from a click. Empty = silent.")]
    public AudioClip clickSfx;

    // ================================================================ Events

    [Header("Events")]
    [Tooltip("Fired with the new state whenever it CHANGES — from a click or from SetOn. Not fired by " +
             "SetOnWithoutNotify, and not fired when the state is set to what it already was.")]
    public UnityEvent<bool> onValueChanged;

    [Tooltip("Fired when the switch goes on. The half of 'onValueChanged' that needs no argument, for " +
             "wiring straight into a UnityEvent target in the inspector.")]
    public UnityEvent onTurnedOn;

    [Tooltip("Fired when the switch goes off.")]
    public UnityEvent onTurnedOff;

    /// <summary>Raised with the new state whenever it changes. The C# side of <see cref="onValueChanged"/>.</summary>
    public event System.Action<bool> OnValueChanged;

    // ================================================================ State

    /// <summary>
    /// The current state. Assigning it is <see cref="SetOn"/> — listeners hear about it. Use
    /// <see cref="SetOnWithoutNotify"/> to move the switch silently.
    /// </summary>
    public bool IsOn
    {
        get => isOn;
        set => SetOn(value);
    }

    /// <summary>True while the switch is off. Reads better than <c>!IsOn</c> in a condition.</summary>
    public bool IsOff => !isOn;

    private Button button;
    private SpriteRenderer spriteRenderer;

    // The sprite the target was authored with — the off sprite when none was assigned.
    private bool resolved;

    // ================================================================ Lifecycle

    private void Awake()
    {
        Resolve();

        button = GetComponent<Button>();
        if (button && !ButtonAlreadyCallsThis()) button.onClick.AddListener(OnClick);

        ApplySprite();
    }

    private void OnDestroy()
    {
        if (button) button.onClick.RemoveListener(OnClick);
    }

    // A switch that was moved while its window was closed still has to come back showing the right
    // sprite: the sprite is only ever written by this component, so re-applying on enable is enough.
    private void OnEnable()
    {
        if (resolved) ApplySprite();
    }

    /// <summary>
    /// Keeps the editor honest: ticking the state in the inspector shows the matching sprite right away,
    /// so a switch is authored by looking at it rather than by pressing play.
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying) return;

        // Only ever swaps between two ASSIGNED sprites here — the "empty = keep what is there" fallback
        // is a runtime idea, and resolving it at edit time would bake the current sprite into the field.
        Sprite target = isOn ? onSprite : offSprite;
        if (!target) return;

        Image image = targetImage ? targetImage : GetComponent<Image>();
        if (image) image.sprite = target;
    }

    // Caches the target and fills in the off sprite from whatever the scene was authored with.
    private void Resolve()
    {
        if (resolved) return;
        resolved = true;

        if (!targetImage) targetImage = GetComponent<Image>();
        if (!targetImage) spriteRenderer = GetComponent<SpriteRenderer>();

        if (!offSprite) offSprite = CurrentSprite;
    }

    // ================================================================ Public API

    /// <summary>
    /// Sets the state and, when it actually changed, tells everyone listening.
    /// </summary>
    /// <param name="value">True = on.</param>
    /// <returns>True when this was a change; false when the switch was already in that state.</returns>
    public bool SetOn(bool value)
    {
        if (isOn == value)
        {
            // Still worth re-applying: something else may have written over the sprite.
            ApplySprite();
            return false;
        }

        isOn = value;
        ApplySprite();

        onValueChanged?.Invoke(isOn);
        OnValueChanged?.Invoke(isOn);

        if (isOn) onTurnedOn?.Invoke();
        else onTurnedOff?.Invoke();

        return true;
    }

    /// <summary>
    /// Moves the switch without firing anything — how a settings window shows the value that is already
    /// in effect without that looking like the player just changed it.
    /// </summary>
    public void SetOnWithoutNotify(bool value)
    {
        isOn = value;
        ApplySprite();
    }

    /// <summary>Flips the state. Shaped for UI buttons and UnityEvents.</summary>
    [ContextMenu("Toggle")]
    public void Toggle() => SetOn(!isOn);

    /// <summary>Switches it on. Shaped for UI buttons and UnityEvents.</summary>
    public void TurnOn() => SetOn(true);

    /// <summary>Switches it off. Shaped for UI buttons and UnityEvents.</summary>
    public void TurnOff() => SetOn(false);

    /// <summary>Re-writes the sprite that belongs to the current state.</summary>
    public void ApplySprite()
    {
        Resolve();

        Sprite target = isOn ? onSprite : offSprite;
        if (!target || target == CurrentSprite) return;

        CurrentSprite = target;
        if (setNativeSize && targetImage) targetImage.SetNativeSize();
    }

    // ================================================================ Internals

    // The Button on this object may already be wired to this component in the inspector (OnClick ->
    // SwitchButton.Toggle). Subscribing on top of that would flip the switch twice per click, which looks
    // exactly like a switch that does nothing. Whoever wired it by hand wins; the automatic hook steps aside.
    private bool ButtonAlreadyCallsThis()
    {
        int count = button.onClick.GetPersistentEventCount();
        for (int i = 0; i < count; i++)
            if (button.onClick.GetPersistentTarget(i) == this) return true;

        return false;
    }

    private void OnClick()
    {
        if (!toggleOnClick) return;

        Toggle();

        if (clickSfx && SFXManager.Instance)
            SFXManager.Instance.PlayClip(clickSfx, is3D: false, playIndependently: true);
    }

    private Sprite CurrentSprite
    {
        get => targetImage ? targetImage.sprite : (spriteRenderer ? spriteRenderer.sprite : null);
        set
        {
            if (targetImage) targetImage.sprite = value;
            else if (spriteRenderer) spriteRenderer.sprite = value;
        }
    }
}
