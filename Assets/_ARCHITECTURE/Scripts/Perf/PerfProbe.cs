using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Drop-in runtime profiler dump. Auto-spawns on play (no scene edit needed), samples Unity's
/// built-in profiler counters and markers every frame, and writes:
///   PerfLogs/perf_&lt;stamp&gt;.csv       - per-frame raw samples
///   PerfLogs/perf_&lt;stamp&gt;_summary.txt - avg/median/p95 per column + marker cost ranking
///   PerfLogs/scene_census_&lt;stamp&gt;.txt - one-shot scene composition dump
/// Files land in the PROJECT ROOT (next to Assets/), so they are easy to read and are gitignored-able.
///
/// Controls: F9 toggles the on-screen HUD. F10 forces a flush + new capture.
/// Disable entirely by setting <see cref="AutoSpawn"/> to false or deleting this file.
/// </summary>
[DefaultExecutionOrder(-32000)]
public class PerfProbe : MonoBehaviour
{
    // ---------------------------------------------------------------- config
    public const bool AutoSpawn = false;

    [Header("Capture")]
    [Tooltip("Seconds to ignore at the start (scene warm-up, shader compilation).")]
    public float warmupSeconds = 3f;

    [Tooltip("How long to record after warm-up. 0 = record until play mode stops.")]
    public float captureSeconds = 40f;

    [Tooltip("Write the per-frame CSV. Turn off to only get the summary.")]
    public bool writeCsv = true;

    [Tooltip("Dump a one-shot census of the scene (component counts, renderers, Update() users).")]
    public bool writeSceneCensus = true;

    [Header("HUD")]
    [Tooltip("On-screen overlay. Off by default so it never costs you a frame or blocks the view; " +
             "press F9 with the Game view focused to toggle it on.")]
    public bool showHud = false;

    // ---------------------------------------------------------------- state
    private readonly List<Rec> _recorders = new List<Rec>();
    private readonly List<float[]> _rows = new List<float[]>();
    private StringBuilder _csv;
    private string _stamp;
    private string _dir;
    private float _t;
    private bool _capturing;
    private bool _finished;
    private int _frames;

    // HUD smoothing
    private float _hudTimer;
    private string _hudText = "";

    private struct Rec
    {
        public string Name;
        public ProfilerRecorder Recorder;
        public bool IsTimeNs;   // marker timings come back in nanoseconds
        public bool IsBytes;
    }

    // ---------------------------------------------------------------- boot
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (!AutoSpawn) return;
        GameObject go = new GameObject("~PerfProbe");
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<PerfProbe>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        _stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PerfLogs"));
        Directory.CreateDirectory(_dir);

        AddCounter(ProfilerCategory.Internal, "Main Thread", timeNs: true);
        AddCounter(ProfilerCategory.Internal, "Render Thread", timeNs: true);

        // ---- markers: where main-thread time actually goes
        AddMarker(ProfilerCategory.Scripts, "BehaviourUpdate");
        AddMarker(ProfilerCategory.Scripts, "LateBehaviourUpdate");
        AddMarker(ProfilerCategory.Scripts, "FixedBehaviourUpdate");
        AddMarker(ProfilerCategory.Scripts, "CoroutinesDelayedCalls");
        AddMarker(ProfilerCategory.Render, "RenderPipelineManager.DoRenderLoop_Internal");
        AddMarker(ProfilerCategory.Render, "Camera.Render");
        AddMarker(ProfilerCategory.Render, "Culling");
        AddMarker(ProfilerCategory.Render, "Shadows.Draw");
        AddMarker(ProfilerCategory.Physics, "Physics.Processing");
        AddMarker(ProfilerCategory.Animation, "Animators.Update");
        AddMarker(ProfilerCategory.Particles, "ParticleSystem.Update");
        AddMarker(ProfilerCategory.Gui, "UGUI.Rendering.RenderOverlays");
        AddMarker(ProfilerCategory.Gui, "Canvas.SendWillRenderCanvases");
        AddMarker(ProfilerCategory.Gui, "Canvas.BuildBatch");
        AddMarker(ProfilerCategory.Ai, "NavMesh.Update");
        AddMarker(ProfilerCategory.Vr, "WaitForTargetFPS");
        AddMarker(ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread");
        AddMarker(ProfilerCategory.Render, "Gfx.PresentFrame");

        // ---- render counters
        AddCounter(ProfilerCategory.Render, "SetPass Calls Count");
        AddCounter(ProfilerCategory.Render, "Draw Calls Count");
        AddCounter(ProfilerCategory.Render, "Batches Count");
        AddCounter(ProfilerCategory.Render, "Triangles Count");
        AddCounter(ProfilerCategory.Render, "Vertices Count");
        AddCounter(ProfilerCategory.Render, "Shadow Casters Count");
        AddCounter(ProfilerCategory.Render, "Render Textures Count");
        AddCounter(ProfilerCategory.Render, "Render Textures Bytes", bytes: true);
        AddCounter(ProfilerCategory.Render, "Used Textures Count");
        AddCounter(ProfilerCategory.Render, "Used Textures Bytes", bytes: true);
        AddCounter(ProfilerCategory.Render, "Dynamic Batched Draw Calls Count");
        AddCounter(ProfilerCategory.Render, "Instanced Batched Draw Calls Count");

        // ---- memory / GC
        AddCounter(ProfilerCategory.Memory, "GC Allocated In Frame", bytes: true);
        AddCounter(ProfilerCategory.Memory, "GC Used Memory", bytes: true);
        AddCounter(ProfilerCategory.Memory, "GC Reserved Memory", bytes: true);
        AddCounter(ProfilerCategory.Memory, "System Used Memory", bytes: true);
        AddCounter(ProfilerCategory.Memory, "Total Used Memory", bytes: true);

        AddCounter(ProfilerCategory.Scripts, "GC.Alloc", bytes: true);

        Application.logMessageReceived += OnLogMessage;

        _csv = new StringBuilder(1 << 20);
        _csv.Append("frame,time,dt_ms,fps");
        foreach (Rec r in _recorders) _csv.Append(',').Append(Sanitize(r.Name));
        _csv.AppendLine();

        Debug.Log("[PerfProbe] armed. " + _recorders.Count + " live recorders. Output -> " + _dir);
    }

    // Resolve recorders by name. Some markers are not reachable through the category overload
    // (Unity files them under an internal category), so we fall back to scanning every available
    // recorder handle by name.
    private static Dictionary<string, ProfilerRecorderHandle> _handleByName;

    private static void BuildHandleMap()
    {
        if (_handleByName != null) return;
        _handleByName = new Dictionary<string, ProfilerRecorderHandle>();
        var list = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(list);
        foreach (ProfilerRecorderHandle h in list)
        {
            ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
            if (!_handleByName.ContainsKey(d.Name)) _handleByName[d.Name] = h;
        }
    }

    private void AddCounter(ProfilerCategory cat, string name, bool bytes = false, bool timeNs = false)
    {
        Register(cat, name, bytes, timeNs);
    }

    private void AddMarker(ProfilerCategory cat, string name)
    {
        Register(cat, name, false, true);
    }

    private void Register(ProfilerCategory cat, string name, bool bytes, bool timeNs)
    {
        ProfilerRecorder rec = ProfilerRecorder.StartNew(cat, name);
        if (!rec.Valid)
        {
            rec.Dispose();
            BuildHandleMap();
            if (!_handleByName.TryGetValue(name, out ProfilerRecorderHandle h)) return;
            rec = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default);
            rec.Start();
            if (!rec.Valid) { rec.Dispose(); return; }
        }

        // Trust Unity's own unit metadata when we can get it.
        try
        {
            BuildHandleMap();
            if (_handleByName.TryGetValue(name, out ProfilerRecorderHandle hh))
            {
                ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(hh);
                if (d.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds) { timeNs = true; bytes = false; }
                else if (d.UnitType == ProfilerMarkerDataUnit.Bytes) { bytes = true; timeNs = false; }
            }
        }
        catch { /* metadata is best-effort */ }

        _recorders.Add(new Rec { Name = name, Recorder = rec, IsBytes = bytes, IsTimeNs = timeNs });
    }

    // ---------------------------------------------------------------- log census
    // Console logging shows up as StackTraceUtility + LogStringToConsole in the profiler but the
    // profiler will not tell you WHICH message. Count them here so the summary names the culprits.
    private readonly Dictionary<string, int> _logCounts = new Dictionary<string, int>();
    private readonly Dictionary<string, string> _logFirstStack = new Dictionary<string, string>();
    private int _totalLogs;

    private void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        _totalLogs++;

        // Collapse near-identical messages so a per-frame log with a changing number still groups.
        string key = type + " | " + (condition.Length > 120 ? condition.Substring(0, 120) : condition);
        _logCounts.TryGetValue(key, out int n);
        _logCounts[key] = n + 1;

        if (n == 0 && !string.IsNullOrEmpty(stackTrace))
        {
            string[] lines = stackTrace.Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length && i < 4; i++)
            {
                if (lines[i].Length == 0) continue;
                sb.Append("        ").Append(lines[i].TrimEnd()).Append('\n');
            }
            _logFirstStack[key] = sb.ToString();
        }
    }

    // ---------------------------------------------------------------- loop
    private void Start()
    {
        if (writeSceneCensus) Invoke(nameof(WriteSceneCensus), 1.5f);
    }

    private void Update()
    {
        _t += Time.unscaledDeltaTime;

        if (!_capturing && !_finished && _t >= warmupSeconds) _capturing = true;
        if (_capturing && !_finished && captureSeconds > 0f && _t >= warmupSeconds + captureSeconds) Finish();

        if (_capturing && !_finished) Sample();

        HandleKeys();
        UpdateHud();
    }

    private void Sample()
    {
        _frames++;
        float dt = Time.unscaledDeltaTime * 1000f;
        float[] row = new float[_recorders.Count];

        if (writeCsv)
        {
            _csv.Append(Time.frameCount).Append(',')
                .Append(_t.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(dt.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append((1000f / Mathf.Max(dt, 0.0001f)).ToString("F1", CultureInfo.InvariantCulture));
        }

        for (int i = 0; i < _recorders.Count; i++)
        {
            Rec r = _recorders[i];
            long v = r.Recorder.Valid ? r.Recorder.LastValue : 0;
            float f = r.IsTimeNs ? v * 1e-6f : v;   // ns -> ms
            row[i] = f;
            if (writeCsv) _csv.Append(',').Append(f.ToString("F4", CultureInfo.InvariantCulture));
        }

        if (writeCsv) _csv.AppendLine();
        _rows.Add(row);

        // keep memory bounded on very long runs
        if (_rows.Count > 20000) Finish();
    }

    private void HandleKeys()
    {
        bool hud = false, flush = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            hud = Keyboard.current.f9Key.wasPressedThisFrame;
            flush = Keyboard.current.f10Key.wasPressedThisFrame;
        }
#else
        hud = Input.GetKeyDown(KeyCode.F9);
        flush = Input.GetKeyDown(KeyCode.F10);
#endif
        if (hud) showHud = !showHud;
        if (flush && !_finished) Finish();
    }

    // ---------------------------------------------------------------- output
    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        _capturing = false;

        try
        {
            if (writeCsv)
            {
                string csvPath = Path.Combine(_dir, "perf_" + _stamp + ".csv");
                File.WriteAllText(csvPath, _csv.ToString());
                Debug.Log("[PerfProbe] wrote " + csvPath);
            }

            string sumPath = Path.Combine(_dir, "perf_" + _stamp + "_summary.txt");
            File.WriteAllText(sumPath, BuildSummary());
            Debug.Log("[PerfProbe] wrote " + sumPath);
        }
        catch (Exception e)
        {
            Debug.LogError("[PerfProbe] write failed: " + e);
        }
    }

    private string BuildSummary()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("PerfProbe summary  " + _stamp);
        sb.AppendLine("Unity " + Application.unityVersion + " | platform " + Application.platform +
                      " | editor=" + Application.isEditor);
        sb.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + ")");
        sb.AppendLine("CPU: " + SystemInfo.processorType + " x" + SystemInfo.processorCount +
                      " | RAM " + SystemInfo.systemMemorySize + "MB | VRAM " + SystemInfo.graphicsMemorySize + "MB");
        sb.AppendLine("Screen: " + Screen.width + "x" + Screen.height + " fullscreen=" + Screen.fullScreenMode +
                      " | targetFrameRate=" + Application.targetFrameRate + " vSync=" + QualitySettings.vSyncCount);
        sb.AppendLine("Quality level: " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
        sb.AppendLine("Frames sampled: " + _frames + " over " + (_t - warmupSeconds).ToString("F1") + "s");
        sb.AppendLine();

        if (_rows.Count == 0) { sb.AppendLine("(no samples)"); return sb.ToString(); }

        sb.AppendLine("COLUMN                                          avg        median     p95        max");
        sb.AppendLine("--------------------------------------------------------------------------------------");
        for (int i = 0; i < _recorders.Count; i++)
        {
            Rec r = _recorders[i];
            float[] col = new float[_rows.Count];
            for (int j = 0; j < _rows.Count; j++) col[j] = _rows[j][i];
            Array.Sort(col);
            float avg = 0f; for (int j = 0; j < col.Length; j++) avg += col[j];
            avg /= col.Length;
            float med = col[col.Length / 2];
            float p95 = col[Mathf.Min(col.Length - 1, (int)(col.Length * 0.95f))];
            float max = col[col.Length - 1];
            string unit = r.IsTimeNs ? "ms" : (r.IsBytes ? "MB" : "");
            float scale = r.IsBytes ? 1f / (1024f * 1024f) : 1f;
            sb.AppendLine(Pad(r.Name + " (" + unit + ")", 46) +
                          Pad((avg * scale).ToString("F3"), 11) +
                          Pad((med * scale).ToString("F3"), 11) +
                          Pad((p95 * scale).ToString("F3"), 11) +
                          (max * scale).ToString("F3"));
        }

        sb.AppendLine();
        sb.AppendLine("TOP TIME COSTS (median ms, descending)");
        sb.AppendLine("--------------------------------------------------------------------------------------");
        var costs = new List<KeyValuePair<string, float>>();
        for (int i = 0; i < _recorders.Count; i++)
        {
            if (!_recorders[i].IsTimeNs) continue;
            float[] col = new float[_rows.Count];
            for (int j = 0; j < _rows.Count; j++) col[j] = _rows[j][i];
            Array.Sort(col);
            costs.Add(new KeyValuePair<string, float>(_recorders[i].Name, col[col.Length / 2]));
        }
        costs.Sort((a, b) => b.Value.CompareTo(a.Value));
        foreach (var c in costs) sb.AppendLine("  " + Pad(c.Key, 50) + c.Value.ToString("F3") + " ms");

        sb.AppendLine();
        sb.AppendLine("CONSOLE LOG CENSUS");
        sb.AppendLine("--------------------------------------------------------------------------------------");
        sb.AppendLine("Total log calls during capture: " + _totalLogs +
                      "  (" + (_frames > 0 ? (_totalLogs / (float)_frames).ToString("F2") : "0") + " per frame)");
        sb.AppendLine("Each one costs a managed stack trace unless Stack Trace is set to None in Player Settings.");
        sb.AppendLine();
        foreach (var kv in _logCounts.OrderByDescending(k => k.Value).Take(25))
        {
            sb.AppendLine("  " + kv.Value + "x  " + kv.Key.Replace("\n", " "));
            if (_logFirstStack.TryGetValue(kv.Key, out string st)) sb.Append(st);
        }

        return sb.ToString();
    }

    private void WriteSceneCensus()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Scene census  " + _stamp);
            sb.AppendLine("Active scene: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            sb.AppendLine();

            Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            int active = 0;
            foreach (Transform t in all) if (t.gameObject.activeInHierarchy) active++;
            sb.AppendLine("GameObjects: " + all.Length + " total, " + active + " active-in-hierarchy");

            var comps = new Dictionary<string, int>();
            var activeComps = new Dictionary<string, int>();
            var updateUsers = new Dictionary<string, int>();
            int rendererCount = 0, activeRenderers = 0;
            var uniqueMats = new HashSet<int>();
            var uniqueMeshes = new HashSet<int>();

            var buf = new List<Component>(16);
            foreach (Transform t in all)
            {
                buf.Clear();
                t.GetComponents(buf);
                bool live = t.gameObject.activeInHierarchy;
                foreach (Component c in buf)
                {
                    if (c == null) continue;
                    Type ty = c.GetType();
                    string n = ty.Name;
                    comps.TryGetValue(n, out int cur); comps[n] = cur + 1;
                    if (live) { activeComps.TryGetValue(n, out int a); activeComps[n] = a + 1; }

                    if (live && c is MonoBehaviour mb && mb.enabled && HasUnityLoopMethod(ty))
                    {
                        updateUsers.TryGetValue(n, out int u); updateUsers[n] = u + 1;
                    }

                    if (c is Renderer rend)
                    {
                        rendererCount++;
                        if (live && rend.enabled)
                        {
                            activeRenderers++;
                            foreach (Material m in rend.sharedMaterials)
                                if (m != null) uniqueMats.Add(m.GetInstanceID());
                        }
                    }
                    if (c is MeshFilter mf && mf.sharedMesh != null) uniqueMeshes.Add(mf.sharedMesh.GetInstanceID());
                }
            }

            sb.AppendLine("Renderers: " + rendererCount + " total, " + activeRenderers + " enabled+active");
            sb.AppendLine("Unique shared materials on active renderers: " + uniqueMats.Count +
                          "   (this is roughly your SetPass floor)");
            sb.AppendLine("Unique meshes: " + uniqueMeshes.Count);
            sb.AppendLine();

            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int shadowLights = 0;
            foreach (Light l in lights) if (l.shadows != LightShadows.None) shadowLights++;
            sb.AppendLine("Active lights: " + lights.Length + " (" + shadowLights + " casting shadows)");

            Camera[] cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            sb.AppendLine("Active cameras: " + cams.Length);
            foreach (Camera c in cams)
                sb.AppendLine("   - " + c.name + "  depth=" + c.depth + " clear=" + c.clearFlags +
                              " culling=0x" + c.cullingMask.ToString("X") + " ortho=" + c.orthographic +
                              " targetTex=" + (c.targetTexture ? c.targetTexture.name : "screen") +
                              " HDR=" + c.allowHDR + " MSAA=" + c.allowMSAA);

            Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            sb.AppendLine();
            sb.AppendLine("Active canvases: " + canvases.Length + "  (each root canvas rebuilds as one batch)");
            foreach (Canvas c in canvases)
                if (c.isRootCanvas)
                    sb.AppendLine("   - " + c.name + " mode=" + c.renderMode +
                                  " children=" + c.GetComponentsInChildren<Transform>(true).Length +
                                  " graphics=" + c.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Length +
                                  " raycasters=" + c.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true).Length);

            ParticleSystem[] ps = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int maxParticles = 0;
            foreach (ParticleSystem p in ps) maxParticles += p.main.maxParticles;
            sb.AppendLine();
            sb.AppendLine("Active particle systems: " + ps.Length + " (sum of maxParticles = " + maxParticles + ")");

            sb.AppendLine();
            sb.AppendLine("TOP MonoBehaviours WITH Update/LateUpdate/FixedUpdate (enabled + active)");
            sb.AppendLine("  -> every one of these is a managed->native call every frame");
            sb.AppendLine("-----------------------------------------------------------------------");
            foreach (var kv in updateUsers.OrderByDescending(k => k.Value).Take(40))
                sb.AppendLine("  " + Pad(kv.Key, 50) + kv.Value);
            int totalUpdates = updateUsers.Values.Sum();
            sb.AppendLine("  TOTAL per-frame script callbacks: " + totalUpdates);

            sb.AppendLine();
            sb.AppendLine("TOP COMPONENT TYPES (active-in-hierarchy count / total count)");
            sb.AppendLine("-----------------------------------------------------------------------");
            foreach (var kv in comps.OrderByDescending(k => k.Value).Take(50))
            {
                activeComps.TryGetValue(kv.Key, out int a);
                sb.AppendLine("  " + Pad(kv.Key, 50) + a + " / " + kv.Value);
            }

            string path = Path.Combine(_dir, "scene_census_" + _stamp + ".txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[PerfProbe] wrote " + path);
        }
        catch (Exception e)
        {
            Debug.LogError("[PerfProbe] census failed: " + e);
        }
    }

    private static readonly Dictionary<Type, bool> LoopMethodCache = new Dictionary<Type, bool>();

    private static bool HasUnityLoopMethod(Type t)
    {
        if (LoopMethodCache.TryGetValue(t, out bool cached)) return cached;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                               BindingFlags.DeclaredOnly;
        bool found = false;
        for (Type cur = t; cur != null && cur != typeof(MonoBehaviour); cur = cur.BaseType)
        {
            if (cur.GetMethod("Update", F) != null || cur.GetMethod("LateUpdate", F) != null ||
                cur.GetMethod("FixedUpdate", F) != null)
            { found = true; break; }
        }
        LoopMethodCache[t] = found;
        return found;
    }

    // ---------------------------------------------------------------- hud
    private void UpdateHud()
    {
        _hudTimer += Time.unscaledDeltaTime;
        if (_hudTimer < 0.25f) return;
        _hudTimer = 0f;

        StringBuilder sb = new StringBuilder();
        float dt = Time.unscaledDeltaTime * 1000f;
        sb.Append("FPS ").Append((1f / Mathf.Max(Time.unscaledDeltaTime, 1e-5f)).ToString("F1"))
          .Append("   frame ").Append(dt.ToString("F1")).Append(" ms\n");
        sb.Append(_finished ? "capture: DONE (F10 written)\n" : (_capturing ? "capture: RECORDING\n" : "capture: warmup\n"));
        for (int i = 0; i < _recorders.Count; i++)
        {
            Rec r = _recorders[i];
            if (!r.Recorder.Valid) continue;
            long v = r.Recorder.LastValue;
            if (r.IsTimeNs)
            {
                float ms = v * 1e-6f;
                if (ms < 0.05f) continue;
                sb.Append(r.Name).Append(": ").Append(ms.ToString("F2")).Append(" ms\n");
            }
            else if (r.IsBytes)
            {
                if (v <= 0) continue;
                sb.Append(r.Name).Append(": ").Append((v / (1024f * 1024f)).ToString("F1")).Append(" MB\n");
            }
            else
            {
                if (v <= 0) continue;
                sb.Append(r.Name).Append(": ").Append(v).Append('\n');
            }
        }
        _hudText = sb.ToString();
    }

    private void OnGUI()
    {
        if (!showHud) return;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(8, 8, 430, 640), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(16, 12, 420, 630), _hudText);
    }

    private void OnApplicationQuit() { Finish(); }

    private void OnDestroy()
    {
        Application.logMessageReceived -= OnLogMessage;
        Finish();
        foreach (Rec r in _recorders) if (r.Recorder.Valid) r.Recorder.Dispose();
        _recorders.Clear();
    }

    // ---------------------------------------------------------------- util
    private static string Pad(string s, int n) => s.Length >= n ? s.Substring(0, n - 1) + " " : s.PadRight(n);
    private static string Sanitize(string s) => s.Replace(',', ';');
}
