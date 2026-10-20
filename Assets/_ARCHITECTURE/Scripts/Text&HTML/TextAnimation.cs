using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Attach to any GameObject that has a TMP_Text component.
/// Set Global Animation in the Inspector to animate all characters.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class TextAnimation : MonoBehaviour
{
    
    // ─────────────────────────────────────────────────────────────
    //  Enum
    // ─────────────────────────────────────────────────────────────
    
    /// <summary>
    /// None - no animation at all
    /// WholeText - Animate whole text with assigned in animations
    /// Tags - Animate only tagged animation using html custom tags (like: <anim:shake>)
    /// Both - both types (WholeText and tags)
    /// </summary>
    public enum AnimationTarget
    {
        None,
        WholeText,
        Tags,
        Both
    }
    
    public enum AnimationType
    {
        None,
        Shake,
        Wave,
        Bounce,
        Fade,
        Rainbow
    }

    // ─────────────────────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────────────────────
    private bool sleepUpdate = false;
    
    [Header("Animation Target")]
    public AnimationTarget animationTarget = AnimationTarget.Both;
    
    [Header("Animation Types")]
    public List<AnimationType> animations = new List<AnimationType>();
    private AnimationType currentAnimation = AnimationType.None;
    [HideInInspector] public Dictionary<int, AnimationType> animationsById = new Dictionary<int, AnimationType>();
    [HideInInspector] public bool cleaned = false;

    [Header("Shake")]
    public float shakeStrength = 3f;
    public float shakeSpeed    = 25f;

    [Header("Wave")]
    public float waveAmplitude = 6f;
    public float waveFrequency = 2f;
    public float waveSpread    = 0.3f;

    [Header("Bounce")]
    public float bounceHeight    = 8f;
    public float bounceFrequency = 2f;
    public float bounceSpread    = 0.2f;

    [Header("Fade")]
    [Range(0f, 1f)] public float fadeMinAlpha = 0f;
    [Range(0f, 1f)] public float fadeMaxAlpha = 1f;
    public float fadeSpeed = 1.5f;
    

    // ─────────────────────────────────────────────────────────────
    //  Private state
    // ─────────────────────────────────────────────────────────────

    [HideInInspector] public TMP_Text _tmp;

    // Cached BASE vertex positions per mesh, rebuilt only when text changes.
    // Key = materialReferenceIndex, Value = unmodified vertex array copy.
    private readonly Dictionary<int, Vector3[]> _baseVertices = new();
    private readonly Dictionary<int, Color32[]> _baseColors   = new();

    // Stable per-character noise seeds for smooth shake.
    private readonly Dictionary<int, Vector2> _noiseSeed = new();

    // Per-character alpha override set externally (e.g. by InvisibleText).
    // Applied after all animations every frame so external visibility always wins.
    // Key = character index, Value = alpha (0 = hidden, 255 = fully visible).
    private readonly Dictionary<int, byte> _alphaOverride = new();

    [HideInInspector] public bool _isDirty = true; // force cache on first frame

    // ─────────────────────────────────────────────────────────────
    //  Appear tremble  (one-shot per character, e.g. the typewriter's)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// How a character trembles when it appears: jerky, hand-drawn-looking jumps in position, angle and
    /// size that die down over <see cref="duration"/> until the letter settles in place.
    /// </summary>
    [System.Serializable]
    public class AppearTremble
    {
        [Tooltip("Seconds a letter trembles before settling (real time, so it also runs over a paused game).")]
        [Min(0.01f)] public float duration = 0.35f;
        [Tooltip("How far a letter jumps around, as a fraction of its font size (0.05 = 5% of the letter's size).")]
        [Min(0f)] public float positionAmplitude = 0.05f;
        [Tooltip("How far a letter tilts either way, in degrees.")]
        [Min(0f)] public float rotationAmplitude = 7f;
        [Tooltip("Size a letter appears at (1 = its own size), easing back to 1 as the tremble dies down.")]
        [Min(0f)] public float startScale = 1.15f;
        [Tooltip("How many times a second a letter jumps to a new random pose — low values look choppy and " +
                 "hand-animated, like drawings on twos. 0: a new pose every frame.")]
        [Min(0f)] public float stepsPerSecond = 18f;
        [Tooltip("Tremble strength over the tremble's lifetime (x: 0 = appeared → 1 = settled, y: strength).")]
        public AnimationCurve strength = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
    }

    private struct AppearState
    {
        public float start;
        public int seed;
        public AppearTremble settings;
    }

    // Key = character index.
    private readonly Dictionary<int, AppearState> _appear = new();
    private readonly List<int> _appearDone = new();

    /// <summary>Makes character <paramref name="ci"/> tremble from now on, per <paramref name="settings"/>.</summary>
    public void PlayAppear(int ci, AppearTremble settings)
    {
        if (settings == null) return;
        _appear[ci] = new AppearState { start = Time.unscaledTime, seed = Random.Range(int.MinValue, int.MaxValue), settings = settings };
    }

    /// <summary>Stops every appear tremble; the letters snap to their resting place on the next update.</summary>
    public void ClearAppear() => _appear.Clear();

    // ─────────────────────────────────────────────────────────────
    //  Alpha override API  (used by InvisibleText)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Force a specific character's alpha every frame, applied after all animations.</summary>
    public void SetCharacterAlpha(int ci, byte alpha)
    {
        _alphaOverride[ci] = alpha;
    }

    /// <summary>Remove the per-character override; animations control alpha normally again.</summary>
    public void ClearCharacterAlpha(int ci)
    {
        _alphaOverride.Remove(ci);
    }

    /// <summary>Remove all per-character alpha overrides.</summary>
    public void ClearAllAlphaOverrides()
    {
        _alphaOverride.Clear();
    }

    // ─────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake() => _tmp = GetComponent<TMP_Text>();

    private void OnEnable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        _isDirty = true;
    }

    private void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
    }

    private void OnTextChanged(Object obj)
    {
        if (obj == _tmp) _isDirty = true;
    }

    public void ForceUpdate(string text="")
    {
        if (text != "") _tmp.text = text;
        _tmp.ForceMeshUpdate();
        CacheBaseVertices();
        Update();
    }

    public void SetSleep()
    {
        sleepUpdate = true;
    }

    public void SetAwake()
    {
        sleepUpdate = false;
    }
    
    private void Update()
    {
        if (_tmp.textInfo == null || _tmp.textInfo.characterCount == 0) return;
        string cleanText = _tmp.text;
        
        // Rebuild base cache only when text actually changed
        if (_isDirty)
        {
            _tmp.ForceMeshUpdate();
            CacheBaseVertices();
            _isDirty = false;
            
            if (!cleaned && !sleepUpdate)
            {
                // h.Out("clear");
                animationsById = new Dictionary<int, AnimationType>();
                string dirtText = _tmp.text;
                cleanText = "";

                int dirtCi = 0;
                int gap = 0;
                string tag;
                for (dirtCi = 0; dirtCi < dirtText.Length; dirtCi++)
                {
                    tag = ProcessTag(dirtCi, dirtText);

                    if (tag == "")
                    {
                        // Any other rich-text tag (<i>, <color>, ...) stays in the text but is no
                        // TMP character, so it must not shift the animation indices after it
                        // (a <sprite> is one character).
                        int tagEnd = dirtText[dirtCi] == '<' ? dirtText.IndexOf('>', dirtCi) : -1;
                        if (tagEnd != -1)
                        {
                            string richTag = dirtText.Substring(dirtCi, tagEnd - dirtCi + 1);
                            cleanText += richTag;
                            gap += richTag.StartsWith("<sprite") ? richTag.Length - 1 : richTag.Length;
                            dirtCi = tagEnd;
                            continue;
                        }

                        cleanText += dirtText[dirtCi];
                        continue;
                    }
                
                    AnimationType chosenAnimation;
                    switch (tag)
                    {
                        case "shake":   chosenAnimation = AnimationType.Shake;   break;
                        case "wave":    chosenAnimation = AnimationType.Wave;    break;
                        case "bounce":  chosenAnimation = AnimationType.Bounce;  break;
                        case "fade":    chosenAnimation = AnimationType.Fade;    break;
                        case "rainbow": chosenAnimation = AnimationType.Rainbow; break;
                        default:        chosenAnimation = AnimationType.None;    break;
                    }
                
                    animationsById.Add(dirtCi - gap, chosenAnimation);
                    int d = tag.Length + 2 + HTML.ANIMATION_TAG.Length;
                    gap += d;
                    dirtCi += d - 1;
                }

                _tmp.text = cleanText;
                cleaned = true;
            }
            else
            {
                cleaned = false;
            }
        }
        
        AnimateCharacters();
    }
    
    private string ProcessTag(int ci, string text)
    {
        char c = text[ci];
        if (c != '<') return "";

        int tagLen = HTML.ANIMATION_TAG.Length;
        if (ci + tagLen >= text.Length) return "";

        string nextChars = "";
        for (int i = 1; i <= tagLen; i++)
            nextChars += text[ci + i];

        if (nextChars != HTML.ANIMATION_TAG) return "";

        string tag = "";
        int k = ci + 1 + tagLen;
        while (k < text.Length && text[k] != '>')
        {
            tag += text[k];
            k++;
        }

        if (!HTML.ANIMATION_TYPES.Contains(tag)) return "";

        return tag;
    }

    // ─────────────────────────────────────────────────────────────
    //  Cache
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot TMP's clean vertex/color data so we can restore it each frame
    /// before applying animation offsets. Called once per text-change, not every frame.
    /// </summary>
    public void CacheBaseVertices()
    {
        _baseVertices.Clear();
        _baseColors.Clear();

        TMP_TextInfo info = _tmp.textInfo;
        for (int m = 0; m < UsedMeshCount(info); m++)
        {
            // Deep copy — TMP reuses the same arrays, so we must clone.
            _baseVertices[m] = (Vector3[])info.meshInfo[m].vertices.Clone();
            _baseColors[m]   = (Color32[])info.meshInfo[m].colors32.Clone();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Animation loop
    // ─────────────────────────────────────────────────────────────

    private void AnimateCharacters()
    {
        currentAnimation = AnimationType.None;
        TMP_TextInfo info = _tmp.textInfo;
        float        t    = Time.time;

        // Step 1: Reset working arrays to base values for this frame.
        int meshCount = UsedMeshCount(info);
        for (int m = 0; m < meshCount; m++)
        {
            if (!_baseVertices.ContainsKey(m)) continue;
            _baseVertices[m].CopyTo(info.meshInfo[m].vertices, 0);
            _baseColors[m].CopyTo(info.meshInfo[m].colors32,   0);
        }

        void ProcessCharacterAnimation(int ci, AnimationType animationIn = AnimationType.None, List<AnimationType> animationsIn = null)
        {
            TMP_CharacterInfo charInfo = info.characterInfo[ci];
            if (!charInfo.isVisible) return;

            int       mi    = charInfo.materialReferenceIndex;
            int       vi    = charInfo.vertexIndex;
            Vector3[] verts = info.meshInfo[mi].vertices;
            Color32[] cols  = info.meshInfo[mi].colors32;
            
            if (animationsIn == null) animationsIn = new List<AnimationType>();
            if (animationIn != AnimationType.None) animationsIn.Add(animationIn);
            
            foreach (var at in animationsIn)
            {
                switch (at)
                {
                    case AnimationType.Shake:   ApplyShake(verts, vi, ci, t);  break;
                    case AnimationType.Wave:    ApplyWave(verts, vi, ci, t);   break;
                    case AnimationType.Bounce:  ApplyBounce(verts, vi, ci, t); break;
                    case AnimationType.Fade:    ApplyFade(cols, vi, ci, t);    break;
                    case AnimationType.Rainbow: ApplyRainbow(cols, vi, ci, t); break;
                    case AnimationType.None: default:                           break;
                }
            }
        }

        void ProcessWholeTextTarget(int ci)
        {
            ProcessCharacterAnimation(ci, animationsIn: animations);
        }

        void ProcessTagsTarget(int ci)
        {
            if (animationsById.TryGetValue(ci, out var value)) currentAnimation = value;
            ProcessCharacterAnimation(ci, animationIn: currentAnimation);
        }
        
        // Step 2: Apply animation offsets on top of clean base.
        for (int ci = 0; ci < info.characterCount; ci++)
        {
            switch (animationTarget)
            {
                case AnimationTarget.None:      default: break;
                case AnimationTarget.WholeText: ProcessWholeTextTarget(ci); break;
                case AnimationTarget.Tags:      ProcessTagsTarget(ci);      break;
                case AnimationTarget.Both:
                    ProcessWholeTextTarget(ci);
                    ProcessTagsTarget(ci);
                    break;
            }
        }

        // Step 2.2: One-shot appear trembles, on top of the looping animations.
        ApplyAppear(info);

        // Step 2.5: Apply external alpha overrides on top of all animations.
        // This runs last so InvisibleText (or any caller) always wins over animation alpha.
        foreach (var (ci, alpha) in _alphaOverride)
        {
            if (ci < 0 || ci >= info.characterCount) continue;

            TMP_CharacterInfo charInfo = info.characterInfo[ci];
            if (!charInfo.isVisible) continue;

            Color32[] cols = info.meshInfo[charInfo.materialReferenceIndex].colors32;
            int vi = charInfo.vertexIndex;
            cols[vi].a = cols[vi + 1].a = cols[vi + 2].a = cols[vi + 3].a = alpha;
        }

        // Step 3: Push modified data back to the mesh. Only the meshes this text uses: the ones past
        // materialCount belong to an earlier text (TMP hides them but keeps their geometry), and pushing them
        // would bring that text back on top of this one.
        for (int m = 0; m < meshCount; m++)
        {
            info.meshInfo[m].mesh.vertices = info.meshInfo[m].vertices;
            _tmp.UpdateGeometry(info.meshInfo[m].mesh, m);
        }

        // Colors need their own update call.
        _tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    /// <summary>
    /// The meshes the current text draws with: the main one and a sub mesh per extra font / sprite asset (e.g. a
    /// fallback font for Cyrillic). meshInfo itself never shrinks, so it can hold more.
    /// </summary>
    private static int UsedMeshCount(TMP_TextInfo info) => Mathf.Min(info.materialCount, info.meshInfo.Length);

    // ─────────────────────────────────────────────────────────────
    //  Animation implementations
    // ─────────────────────────────────────────────────────────────

    private void ApplyShake(Vector3[] verts, int vi, int ci, float t)
    {
        if (!_noiseSeed.TryGetValue(ci, out Vector2 seed))
        {
            seed = new Vector2(Random.value * 100f, Random.value * 100f);
            _noiseSeed[ci] = seed;
        }

        float dx     = (Mathf.PerlinNoise(seed.x + t * shakeSpeed, 0f) - 0.5f) * 2f * shakeStrength;
        float dy     = (Mathf.PerlinNoise(0f, seed.y + t * shakeSpeed) - 0.5f) * 2f * shakeStrength;
        var   offset = new Vector3(dx, dy, 0f);

        for (int v = 0; v < 4; v++) verts[vi + v] += offset;
    }

    private void ApplyWave(Vector3[] verts, int vi, int ci, float t)
    {
        float dy     = Mathf.Sin(t * waveFrequency * Mathf.PI * 2f + ci * waveSpread) * waveAmplitude;
        var   offset = new Vector3(0f, dy, 0f);
        for (int v = 0; v < 4; v++) verts[vi + v] += offset;
    }

    private void ApplyBounce(Vector3[] verts, int vi, int ci, float t)
    {
        float raw    = Mathf.Sin(t * bounceFrequency * Mathf.PI * 2f + ci * bounceSpread);
        var   offset = new Vector3(0f, Mathf.Abs(raw) * bounceHeight, 0f);
        for (int v = 0; v < 4; v++) verts[vi + v] += offset;
    }

    private void ApplyFade(Color32[] colors, int vi, int ci, float t)
    {
        float wave  = (Mathf.Sin(t * fadeSpeed * Mathf.PI * 2f + ci * 0.4f) + 1f) * 0.5f;
        float alpha = Mathf.Lerp(fadeMinAlpha, fadeMaxAlpha, wave);

        for (int v = 0; v < 4; v++)
        {
            // Multiply against the character's current base alpha so that
            // a hidden character (alpha=0) stays hidden even under Fade.
            byte a = (byte)(alpha * (colors[vi + v].a / 255f) * 255f);
            colors[vi + v] = new Color32(colors[vi + v].r, colors[vi + v].g, colors[vi + v].b, a);
        }
    }

    private void ApplyAppear(TMP_TextInfo info)
    {
        if (_appear.Count == 0) return;

        float now = Time.unscaledTime;
        _appearDone.Clear();

        foreach (var (ci, state) in _appear)
        {
            AppearTremble s = state.settings;
            float elapsed = now - state.start;
            if (elapsed >= s.duration || ci < 0 || ci >= info.characterCount)
            {
                _appearDone.Add(ci);
                continue;
            }

            TMP_CharacterInfo charInfo = info.characterInfo[ci];
            if (!charInfo.isVisible) continue;

            float k = s.strength.Evaluate(elapsed / s.duration);

            // The pose only changes once per step, so it jerks from pose to pose instead of sliding.
            int step = s.stepsPerSecond > 0f ? Mathf.FloorToInt(elapsed * s.stepsPerSecond) : Time.frameCount;

            float size   = charInfo.pointSize;
            var   offset = new Vector3(SignedHash(state.seed, step, 0), SignedHash(state.seed, step, 1), 0f)
                           * (s.positionAmplitude * size * k);
            var   rot    = Quaternion.Euler(0f, 0f, SignedHash(state.seed, step, 2) * s.rotationAmplitude * k);
            float scale  = Mathf.LerpUnclamped(1f, s.startScale, k);

            Vector3[] verts  = info.meshInfo[charInfo.materialReferenceIndex].vertices;
            int       vi     = charInfo.vertexIndex;
            Vector3   center = (verts[vi] + verts[vi + 2]) * 0.5f; // bottom-left + top-right

            for (int v = 0; v < 4; v++)
                verts[vi + v] = center + rot * ((verts[vi + v] - center) * scale) + offset;
        }

        foreach (int ci in _appearDone) _appear.Remove(ci);
    }

    /// <summary>A stable pseudo-random value in [-1, 1] for the given inputs.</summary>
    private static float SignedHash(int seed, int step, int channel)
    {
        unchecked
        {
            uint x = (uint)seed * 374761393u + (uint)step * 668265263u + (uint)channel * 2246822519u;
            x = (x ^ (x >> 13)) * 1274126177u;
            x ^= x >> 16;
            return (x & 0xFFFFFF) / (float)0x1000000 * 2f - 1f;
        }
    }

    private void ApplyRainbow(Color32[] colors, int vi, int ci, float t)
    {
        Color   c   = Color.HSVToRGB(Mathf.Repeat(t * 0.3f + ci * 0.07f, 1f), 1f, 1f);
        Color32 c32 = c;
        for (int v = 0; v < 4; v++)
            colors[vi + v] = new Color32(c32.r, c32.g, c32.b, colors[vi + v].a);
    }
}