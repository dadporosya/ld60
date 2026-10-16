using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Exports whatever the Unity Profiler currently holds into a plain-text report that can be read
/// outside the editor. Workflow:
///   1. Window &gt; Analysis &gt; Profiler, make sure Record is on, press Play, let it run.
///   2. Stop play mode (the frames stay in the profiler buffer).
///   3. Tools &gt; Perf &gt; Export Profiler Frames to Text.
/// Output lands in &lt;project&gt;/PerfLogs/profiler_export_&lt;stamp&gt;.txt
/// </summary>
public static class PerfProfilerExport
{
    private const int MaxFramesToScan = 400;

    private static string OutDir
    {
        get
        {
            string d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PerfLogs"));
            Directory.CreateDirectory(d);
            return d;
        }
    }

    [MenuItem("Tools/Perf/Export Profiler Frames to Text %#e")]
    public static void ExportFrames()
    {
        int first = ProfilerDriver.firstFrameIndex;
        int last = ProfilerDriver.lastFrameIndex;

        if (last < 0 || last < first)
        {
            EditorUtility.DisplayDialog("Perf export",
                "The profiler has no frames.\n\nOpen Window > Analysis > Profiler, enable Record, " +
                "press Play for a while, stop, then run this again.", "OK");
            return;
        }

        int start = Mathf.Max(first, last - MaxFramesToScan + 1);
        var self = new Dictionary<string, Agg>(512);
        var total = new Dictionary<string, Agg>(512);
        var frameTimes = new List<float>();
        var gpuTimes = new List<float>();
        int scanned = 0;

        for (int f = start; f <= last; f++)
        {
            HierarchyFrameDataView v = null;
            try
            {
                v = ProfilerDriver.GetHierarchyFrameDataView(
                    f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                    HierarchyFrameDataView.columnTotalTime, false);

                if (v == null || !v.valid) continue;
                scanned++;
                frameTimes.Add(v.frameTimeMs);
                try { gpuTimes.Add(v.frameGpuTimeMs); } catch { }

                Walk(v, v.GetRootItemID(), self, total, 0);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PerfExport] frame " + f + " skipped: " + e.Message);
            }
            finally
            {
                v?.Dispose();
            }
        }

        if (scanned == 0)
        {
            EditorUtility.DisplayDialog("Perf export", "Could not read any profiler frames.", "OK");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Unity Profiler export");
        sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Unity " + Application.unityVersion);
        sb.AppendLine("Frames scanned: " + scanned + "  (profiler buffer " + first + ".." + last + ")");
        sb.AppendLine("Deep profiling: " + ProfilerDriver.deepProfiling);
        sb.AppendLine();

        AppendStats(sb, "CPU frame time (main thread)", frameTimes);
        if (gpuTimes.Count > 0 && gpuTimes.Any(g => g > 0f)) AppendStats(sb, "GPU frame time", gpuTimes);
        sb.AppendLine();

        sb.AppendLine("======================================================================================");
        sb.AppendLine("TOP 60 BY SELF TIME  (avg ms per frame - this is where the CPU actually burns)");
        sb.AppendLine("======================================================================================");
        sb.AppendLine(Pad("MARKER", 58) + Pad("avg ms", 10) + Pad("max ms", 10) + Pad("calls/f", 10) + "GC KB/f");
        foreach (var kv in self.OrderByDescending(k => k.Value.Sum).Take(60))
            AppendRow(sb, kv.Key, kv.Value, scanned);

        sb.AppendLine();
        sb.AppendLine("======================================================================================");
        sb.AppendLine("TOP 60 BY TOTAL TIME  (avg ms per frame, inclusive of children)");
        sb.AppendLine("======================================================================================");
        sb.AppendLine(Pad("MARKER", 58) + Pad("avg ms", 10) + Pad("max ms", 10) + Pad("calls/f", 10) + "GC KB/f");
        foreach (var kv in total.OrderByDescending(k => k.Value.Sum).Take(60))
            AppendRow(sb, kv.Key, kv.Value, scanned);

        sb.AppendLine();
        sb.AppendLine("======================================================================================");
        sb.AppendLine("TOP 40 GC ALLOCATORS  (avg KB allocated per frame)");
        sb.AppendLine("======================================================================================");
        foreach (var kv in self.Where(k => k.Value.Gc > 0).OrderByDescending(k => k.Value.Gc).Take(40))
            sb.AppendLine("  " + Pad(kv.Key, 58) + (kv.Value.Gc / 1024.0 / scanned).ToString("F2") + " KB/f");

        string path = Path.Combine(OutDir, "profiler_export_" +
                                   DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
        File.WriteAllText(path, sb.ToString());
        Debug.Log("[PerfExport] wrote " + path);
        EditorUtility.RevealInFinder(path);
    }

    [MenuItem("Tools/Perf/Load Saved Profiler Capture (.raw/.data)...")]
    public static void LoadCapture()
    {
        string p = EditorUtility.OpenFilePanel("Pick a saved profiler capture", OutDir, "raw,data");
        if (string.IsNullOrEmpty(p)) return;
        if (ProfilerDriver.LoadProfile(p, false)) ExportFrames();
        else EditorUtility.DisplayDialog("Perf export", "Unity refused to load that capture.", "OK");
    }

    [MenuItem("Tools/Perf/Dump Resolved Render Settings")]
    public static void DumpRenderSettings()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Resolved render settings  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("Unity " + Application.unityVersion);
        sb.AppendLine();
        sb.AppendLine("Active quality level: " + QualitySettings.names[QualitySettings.GetQualityLevel()] +
                      " (index " + QualitySettings.GetQualityLevel() + ")");
        sb.AppendLine("vSyncCount: " + QualitySettings.vSyncCount +
                      " | targetFrameRate: " + Application.targetFrameRate);
        sb.AppendLine("Anti-aliasing (quality): " + QualitySettings.antiAliasing);
        sb.AppendLine("Shadow distance: " + QualitySettings.shadowDistance +
                      " | cascades: " + QualitySettings.shadowCascades);
        sb.AppendLine();

        var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (rp == null) { sb.AppendLine("No URP asset active!"); }
        else
        {
            sb.AppendLine("URP asset: " + rp.name);
            sb.AppendLine("  renderScale            : " + rp.renderScale);
            sb.AppendLine("  msaaSampleCount        : " + rp.msaaSampleCount);
            sb.AppendLine("  supportsHDR            : " + rp.supportsHDR);
            sb.AppendLine("  supportsCameraDepthTex : " + rp.supportsCameraDepthTexture +
                          "   <- forces an extra depth pass");
            sb.AppendLine("  supportsCameraOpaqueTex: " + rp.supportsCameraOpaqueTexture +
                          "   <- forces a full-screen colour copy every frame");
            sb.AppendLine("  opaqueDownsampling     : " + rp.opaqueDownsampling);
            sb.AppendLine("  mainLightShadows       : " + rp.supportsMainLightShadows +
                          " res=" + rp.mainLightShadowmapResolution);
            sb.AppendLine("  additionalLightShadows : " + rp.supportsAdditionalLightShadows +
                          " res=" + rp.additionalLightsShadowmapResolution +
                          " perObjectLimit=" + rp.maxAdditionalLightsCount);
            sb.AppendLine("  softShadows            : " + rp.supportsSoftShadows);
            sb.AppendLine("  shadowDistance         : " + rp.shadowDistance +
                          " cascades=" + rp.shadowCascadeCount);
            sb.AppendLine("  useSRPBatcher          : " + rp.useSRPBatcher);
            sb.AppendLine("  supportsDynamicBatching: " + rp.supportsDynamicBatching);
            sb.AppendLine("  colorGradingMode       : " + rp.colorGradingMode +
                          " lutSize=" + rp.colorGradingLutSize);
        }

        sb.AppendLine();
        sb.AppendLine("Renderer features on the active renderer(s):");
        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(p);
            if (data == null) continue;
            sb.AppendLine("  " + data.name + "  (" + p + ")");
            foreach (ScriptableRendererFeature feat in data.rendererFeatures)
            {
                if (feat == null) { sb.AppendLine("     - <missing feature>"); continue; }
                sb.AppendLine("     - " + Pad(feat.name, 40) + "type=" + Pad(feat.GetType().Name, 34) +
                              (feat.isActive ? "ACTIVE" : "off"));
            }
        }

        string path = Path.Combine(OutDir, "render_settings_" +
                                   DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
        File.WriteAllText(path, sb.ToString());
        Debug.Log("[PerfExport] wrote " + path);
    }

    // ------------------------------------------------------------------ internals
    private class Agg
    {
        public double Sum;
        public float Max;
        public double Calls;
        public double Gc;
    }

    private static void Walk(HierarchyFrameDataView v, int id,
                             Dictionary<string, Agg> self, Dictionary<string, Agg> total, int depth)
    {
        if (depth > 64) return;

        string name = v.GetItemName(id);
        if (!string.IsNullOrEmpty(name))
        {
            float s = v.GetItemColumnDataAsSingle(id, HierarchyFrameDataView.columnSelfTime);
            float t = v.GetItemColumnDataAsSingle(id, HierarchyFrameDataView.columnTotalTime);
            float c = v.GetItemColumnDataAsSingle(id, HierarchyFrameDataView.columnCalls);
            float g = v.GetItemColumnDataAsSingle(id, HierarchyFrameDataView.columnGcMemory);
            Bump(self, name, s, c, g);
            Bump(total, name, t, c, g);
        }

        if (!v.HasItemChildren(id)) return;
        var children = new List<int>();
        v.GetItemChildren(id, children);
        for (int i = 0; i < children.Count; i++) Walk(v, children[i], self, total, depth + 1);
    }

    private static void Bump(Dictionary<string, Agg> d, string k, float ms, float calls, float gc)
    {
        if (!d.TryGetValue(k, out Agg a)) { a = new Agg(); d[k] = a; }
        a.Sum += ms;
        if (ms > a.Max) a.Max = ms;
        a.Calls += calls;
        a.Gc += gc;
    }

    private static void AppendRow(StringBuilder sb, string name, Agg a, int frames)
    {
        sb.AppendLine("  " + Pad(name, 58) +
                      Pad((a.Sum / frames).ToString("F3", CultureInfo.InvariantCulture), 10) +
                      Pad(a.Max.ToString("F3", CultureInfo.InvariantCulture), 10) +
                      Pad((a.Calls / frames).ToString("F1", CultureInfo.InvariantCulture), 10) +
                      (a.Gc / 1024.0 / frames).ToString("F2", CultureInfo.InvariantCulture));
    }

    private static void AppendStats(StringBuilder sb, string label, List<float> values)
    {
        if (values.Count == 0) return;
        var v = values.ToArray();
        Array.Sort(v);
        double avg = v.Average();
        sb.AppendLine(label + ":");
        sb.AppendLine("   avg " + avg.ToString("F2") + " ms  (" + (1000.0 / Math.Max(avg, 0.001)).ToString("F1") + " fps)" +
                      "   median " + v[v.Length / 2].ToString("F2") +
                      "   p95 " + v[Mathf.Min(v.Length - 1, (int)(v.Length * 0.95f))].ToString("F2") +
                      "   max " + v[v.Length - 1].ToString("F2"));
    }

    private static string Pad(string s, int n) => s.Length >= n ? s.Substring(0, n - 1) + " " : s.PadRight(n);
}
