using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Records the player's pencil into a <see cref="PencilRecording"/> asset, for <see cref="PencilPlayback"/> to
/// replay (the <see cref="TutorialCutscene"/>'s demo drawing).
///
/// <para>In Play mode press <see cref="toggleKey"/> to start, draw, press it again to stop. The mouse is
/// sampled every frame, relative to <see cref="board"/>. On stop the samples are written into
/// <see cref="target"/> (overwriting it), or into a new asset in <see cref="folder"/> when no target is set —
/// saving works in the Editor only.</para>
/// </summary>
public class PencilRecorder : MonoBehaviour
{
    [Tooltip("Positions are recorded relative to this board. Empty: the GameManager's main board.")]
    [SerializeField] private PixelGrid board;
    [SerializeField] private Key toggleKey = Key.F9;

    [Header("Saving (Editor only)")]
    [Tooltip("Overwritten on every stop. Empty: each recording becomes a new asset in the folder below.")]
    [SerializeField] private PencilRecording target;
    [SerializeField] private string folder = "Assets/_PROJECT/recordings";

    [Header("Trimming")]
    [Tooltip("Start the clock at the first press after the toggle, dropping the idle time before it.")]
    [SerializeField] private bool startOnFirstPress = true;
    [Tooltip("Drop what comes after the last release.")]
    [SerializeField] private bool trimAfterLastRelease = true;

    [Header("Rules")]
    [Tooltip("Suspend the GameManager's win/lose rules while recording, so the take can't hurt, win or lose.")]
    [SerializeField] private bool suspendRulesWhileRecording = true;

    public bool IsRecording { get; private set; }

    /// <summary>The last finished take (also kept in builds, where it can't be saved).</summary>
    public PencilRecording LastRecording { get; private set; }

    private readonly List<PencilRecording.Sample> _samples = new List<PencilRecording.Sample>();
    private bool _waitingForPress;
    private float _startTime;
    private bool _suspendedRules;

    private void OnDisable()
    {
        if (IsRecording) StopRecording();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && DebugHotkeys.Enabled && toggleKey != Key.None && keyboard[toggleKey].wasPressedThisFrame)
        {
            if (IsRecording) StopRecording();
            else StartRecording();
        }

        if (IsRecording) Sample();
    }

    public void StartRecording()
    {
        if (IsRecording) return;

        if (!board && GameManager.Instance) board = GameManager.Instance.MainBoard;
        if (!board)
        {
            h.Out("PencilRecorder: no board to record on.");
            return;
        }

        _samples.Clear();
        _waitingForPress = startOnFirstPress;
        _startTime = Time.unscaledTime;
        IsRecording = true;

        if (suspendRulesWhileRecording && GameManager.Instance)
        {
            GameManager.Instance.SuspendRules();
            _suspendedRules = true;
        }

        h.Out(startOnFirstPress
            ? $"PencilRecorder: armed — recording starts at the first press. {toggleKey} to stop."
            : $"PencilRecorder: recording. {toggleKey} to stop.");
    }

    public void StopRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;

        if (_suspendedRules && GameManager.Instance) GameManager.Instance.ResumeRules();
        _suspendedRules = false;

        if (trimAfterLastRelease) TrimAfterLastRelease();
        if (_samples.Count == 0)
        {
            h.Out("PencilRecorder: nothing recorded.");
            return;
        }

        LastRecording = Save();
        h.Out($"PencilRecorder: recorded {_samples.Count} samples, {_samples[_samples.Count - 1].time:0.00}s " +
              $"→ '{(LastRecording ? LastRecording.name : "?")}'.");
    }

    private void Sample()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        bool pressed = mouse.leftButton.isPressed;
        if (_waitingForPress)
        {
            if (!pressed) return;
            _waitingForPress = false;
            _startTime = Time.unscaledTime;
        }

        if (!PencilRecording.ScreenToNormalized(board, mouse.position.ReadValue(), out Vector2 position)) return;

        // Unscaled, like the pencil tip the take is drawn with.
        _samples.Add(new PencilRecording.Sample(Time.unscaledTime - _startTime, position, pressed));
    }

    private void TrimAfterLastRelease()
    {
        // Keep the release sample itself, so the replay ends with the button up.
        for (int i = _samples.Count - 1; i > 0; i--)
        {
            if (_samples[i - 1].pressed && !_samples[i].pressed)
            {
                _samples.RemoveRange(i + 1, _samples.Count - i - 1);
                return;
            }
        }
    }

    private PencilRecording Save()
    {
        PencilRecording recording = target ? target : ScriptableObject.CreateInstance<PencilRecording>();
        recording.samples = new List<PencilRecording.Sample>(_samples);

#if UNITY_EDITOR
        if (target)
        {
            EditorUtility.SetDirty(target);
        }
        else
        {
            EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder.TrimEnd('/')}/PencilRecording.asset");
            AssetDatabase.CreateAsset(recording, path);
            EditorGUIUtility.PingObject(recording);
        }
        AssetDatabase.SaveAssets();
#else
        h.Out("PencilRecorder: recordings can only be saved in the Editor; this one is kept in LastRecording.");
#endif
        return recording;
    }

#if UNITY_EDITOR
    private static void EnsureFolder(string path)
    {
        path = path.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(path)) return;

        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }
#endif
}
