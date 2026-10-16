using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

/// <summary>
/// One window for authoring dialogues.
///
/// Left: a browser listing every DialogueContainer asset in the project (including
/// DialogueContainerWithModels, which derives from it). Selecting a dialogue asset in the
/// Project window loads it here too; double-clicking one opens this window.
///
/// Right: the node list. Every node shows an inline speaker preview (resolved portrait +
/// name) next to its text, so you can read a dialogue top-to-bottom without expanding
/// anything. Expanding a node reveals the full speaker/atlas/voice/event fields.
///
/// When the selected asset is a DialogueContainerWithModels, each node additionally shows
/// its row of the per-node x per-object grid (enable / model / eyes / talk, plus the
/// speaking object), and node add/remove/reorder keeps that grid in sync. A
/// DialogueContainerWithArmature gets the same grid with enable / animation / loop / talk.
/// A VisualNovelDialogueContainer instead gets a collapsible "Visual novel" panel under each
/// node (background, transition, music, ambience, SFX, effects) and its dialogue start/end
/// settings in the header.
///
/// Editing goes through SerializedObject, so undo, dirtying and prefab overrides behave
/// the way they do in the inspector.
/// </summary>
public class DialogueEditorWindow : EditorWindow
{
    private const float SIDEBAR_WIDTH = 250f;
    private const float PORTRAIT_SIZE = 48f;
    private const double ASSET_REFRESH_INTERVAL = 2.0;

    // Grid column widths.
    private const float COL_OBJECT = 130f;
    private const float COL_TOGGLE = 46f;
    private const float COL_MODEL = 140f;

    [SerializeField] private DialogueContainer target;

    [Tooltip("Optional scene controller. Only used to label the object columns and turn " +
             "model ids into dropdowns of real model names - never written to the asset.")]
    [SerializeField] private TalkableWModelsController previewController;

    [Tooltip("Optional scene controller for armature dialogues. Only used to label the object " +
             "columns and turn animation ids into dropdowns of real clip names - never written " +
             "to the asset.")]
    [SerializeField] private TalkableWithArmatureController previewArmatureController;

    [SerializeField] private Vector2 sidebarScroll;
    [SerializeField] private Vector2 mainScroll;
    [SerializeField] private string search = "";
    [SerializeField] private bool showSidebar = true;
    [SerializeField] private bool followSelection = true;

    private SerializedObject so;
    private SerializedProperty nodesProp;
    private SerializedProperty nodeSettingsProp; // multi-object and visual novel containers only
    private SerializedProperty objectsProp;      // multi-object containers only
    private SerializedProperty speakerIdsProp;
    private SerializedProperty speakerPrefabsProp;

    private readonly List<string> assetPaths = new List<string>();
    private readonly HashSet<int> expanded = new HashSet<int>();
    private readonly HashSet<int> vnExpanded = new HashSet<int>(); // nodes with their VN panel open

    private bool speakersFoldout;
    private bool objectsFoldout = true;
    private bool vnDialogueFoldout = true;
    private double lastAssetRefresh;

    // Structural edits are queued while drawing and applied on the next Layout event.
    // Resizing the node list mid-frame would leave the Repaint pass with a different
    // control count than the Layout pass, which Unity reports as a layout mismatch.
    private int pendingMoveFrom = -1;
    private int pendingMoveTo = -1;
    private int pendingInsertAt = -1;
    private int pendingDeleteAt = -1;
    private int pendingObjectCount = -1;

    private DialogueContainer pendingTarget;
    private bool hasPendingTarget;

    private GUIStyle nodeBoxStyle;
    private GUIStyle textAreaStyle;
    private GUIStyle previewNameStyle;
    private GUIStyle previewTextStyle;
    private GUIStyle indexStyle;

    private bool IsWithModels => target is DialogueContainerWithModels;
    private bool IsWithArmature => target is DialogueContainerWithArmature;

    /// <summary>Either container type that drives several objects through a per-node grid.</summary>
    private bool IsMultiObject => IsWithModels || IsWithArmature;

    /// <summary>Visual novel container: one settings row per node, no object slots.</summary>
    private bool IsVisualNovel => target is VisualNovelDialogueContainer;

    // Dialogue-level fields of VisualNovelDialogueContainer, drawn in the header.
    private static readonly string[] VisualNovelDialogueFields =
    {
        "startCovered", "startColor",
        "fadeToColorOnEnd", "endColor", "endFadeDuration",
        "musicOnEnd", "endMusicFade", "stopAmbienceOnEnd", "endAmbienceFade",
        "releaseEffectsOnEnd",
    };

    // ------------------------------------------------------------------
    // Opening
    // ------------------------------------------------------------------

    [MenuItem("Window/Dialogues/Dialogue Editor")]
    public static DialogueEditorWindow Open()
    {
        DialogueEditorWindow window = GetWindow<DialogueEditorWindow>("Dialogues");
        window.minSize = new Vector2(640f, 320f);
        window.Show();
        return window;
    }

    /// <summary>Double-clicking a dialogue asset opens it in this window.</summary>
    [OnOpenAsset]
    private static bool OnOpenDialogueAsset(int instanceID, int line)
    {
        DialogueContainer container = EditorUtility.InstanceIDToObject(instanceID) as DialogueContainer;
        if (!container) return false;

        Open().SetTarget(container);
        return true;
    }

    /// <summary>
    /// Switches the edited asset. Safe to call from inside OnGUI: the swap is deferred to
    /// the next Layout event so the rest of the current pass keeps drawing the old asset
    /// (rebinding mid-pass would change the control count).
    /// </summary>
    public void SetTarget(DialogueContainer container)
    {
        if (target == container && so != null) return;

        pendingTarget = container;
        hasPendingTarget = true;
        Repaint();
    }

    private void ApplyPendingTarget()
    {
        if (!hasPendingTarget) return;
        hasPendingTarget = false;

        FlushTarget();

        target = pendingTarget;
        pendingTarget = null;
        expanded.Clear();
        vnExpanded.Clear();
        BindSerializedObject();
    }

    private void OnEnable()
    {
        RefreshAssetList();
        BindSerializedObject();
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        SaveTarget();
    }

    private void OnUndoRedo()
    {
        so?.Update();
        Repaint();
    }

    private void OnSelectionChange()
    {
        if (!followSelection) return;

        DialogueContainer selected = Selection.activeObject as DialogueContainer;
        if (selected) SetTarget(selected);
    }

    private void OnFocus()
    {
        RefreshAssetList();
    }

    private void BindSerializedObject()
    {
        so = null;
        nodesProp = nodeSettingsProp = objectsProp = null;
        speakerIdsProp = speakerPrefabsProp = null;
        pendingMoveFrom = pendingMoveTo = pendingInsertAt = pendingDeleteAt = -1;
        pendingObjectCount = -1;

        if (!target) return;

        so = new SerializedObject(target);
        nodesProp = so.FindProperty("nodes");
        speakerIdsProp = so.FindProperty("speakerIds");
        speakerPrefabsProp = so.FindProperty("speakerPrefabs");

        // Both multi-object containers use the same field names for their grid.
        if (IsMultiObject)
        {
            nodeSettingsProp = so.FindProperty("nodeSettings");
            objectsProp = so.FindProperty("objects");
        }
        else if (IsVisualNovel)
        {
            nodeSettingsProp = so.FindProperty("nodeSettings");
        }
    }

    private void RefreshAssetList()
    {
        // "t:DialogueContainer" also matches DialogueContainerWithModels (it derives from it).
        assetPaths.Clear();
        assetPaths.AddRange(
            AssetDatabase.FindAssets("t:DialogueContainer")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(p => p));

        lastAssetRefresh = EditorApplication.timeSinceStartup;
    }

    /// <summary>Commits pending edits and marks the asset dirty, without hitting the disk.</summary>
    private void FlushTarget()
    {
        if (!target) return;

        so?.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
    }

    /// <summary>Commits pending edits and writes the asset to disk.</summary>
    private void SaveTarget()
    {
        if (!target) return;

        FlushTarget();
        AssetDatabase.SaveAssets();
    }

    // ------------------------------------------------------------------
    // GUI
    // ------------------------------------------------------------------

    private void OnGUI()
    {
        InitStyles();

        if (Event.current.type == EventType.Layout)
        {
            ApplyPendingTarget();

            if (EditorApplication.timeSinceStartup - lastAssetRefresh > ASSET_REFRESH_INTERVAL)
                RefreshAssetList();
        }

        DrawToolbar();

        EditorGUILayout.BeginHorizontal();
        {
            if (showSidebar) DrawSidebar();
            DrawMainPane();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void InitStyles()
    {
        if (nodeBoxStyle != null) return;

        nodeBoxStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(8, 8, 6, 6) };

        textAreaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };

        previewNameStyle = new GUIStyle(EditorStyles.boldLabel);

        previewTextStyle = new GUIStyle(EditorStyles.label)
        {
            wordWrap = true,
            fontSize = 11,
            richText = false
        };

        indexStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter
        };
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            showSidebar = GUILayout.Toggle(showSidebar, "Browser", EditorStyles.toolbarButton,
                GUILayout.Width(64f));

            followSelection = GUILayout.Toggle(followSelection, "Follow selection",
                EditorStyles.toolbarButton, GUILayout.Width(110f));

            GUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(!target))
            {
                if (GUILayout.Button("Ping", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                    EditorGUIUtility.PingObject(target);

                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                    SaveTarget();
            }

            GUILayout.FlexibleSpace();

            if (target)
                GUILayout.Label(target.GetType().Name, EditorStyles.miniLabel);
        }
        EditorGUILayout.EndHorizontal();
    }

    // ------------------------------------------------------------------
    // Sidebar
    // ------------------------------------------------------------------

    private void DrawSidebar()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(SIDEBAR_WIDTH));
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("↻", EditorStyles.toolbarButton, GUILayout.Width(22f)))
                    RefreshAssetList();
            }
            EditorGUILayout.EndHorizontal();

            sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll,
                GUILayout.Width(SIDEBAR_WIDTH));
            {
                string filter = search != null ? search.Trim() : "";
                string lastFolder = null;
                int shown = 0;

                foreach (string path in assetPaths)
                {
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(path);

                    if (filter.Length > 0
                        && fileName.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    string folder = System.IO.Path.GetDirectoryName(path);
                    if (folder != lastFolder)
                    {
                        lastFolder = folder;
                        EditorGUILayout.LabelField(ShortenFolder(folder), EditorStyles.miniBoldLabel);
                    }

                    DrawSidebarEntry(path, fileName);
                    shown++;
                }

                if (shown == 0)
                    EditorGUILayout.LabelField("No dialogue assets found.", EditorStyles.centeredGreyMiniLabel);
            }
            EditorGUILayout.EndScrollView();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawSidebarEntry(string path, string fileName)
    {
        bool isCurrent = target && AssetDatabase.GetAssetPath(target) == path;

        // Loading every asset just to badge it would be wasteful; the type name in the
        // asset's main type is cheap enough via the AssetDatabase's cached type.
        System.Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
        string badge =
            assetType == typeof(DialogueContainerWithModels) ? "models" :
            assetType == typeof(DialogueContainerWithArmature) ? "armature" :
            null;

        Rect rect = EditorGUILayout.GetControlRect(false, 20f);

        if (isCurrent)
            EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.90f, 0.35f));

        const float badgeWidth = 48f;
        Rect labelRect = new Rect(rect.x + 6f, rect.y, rect.width - badgeWidth - 8f, rect.height);
        GUI.Label(labelRect, new GUIContent(fileName, path));

        if (badge != null)
        {
            Rect badgeRect = new Rect(rect.xMax - badgeWidth - 2f, rect.y + 2f, badgeWidth, rect.height - 4f);
            GUI.Label(badgeRect, new GUIContent(badge, assetType.Name), EditorStyles.miniLabel);
        }

        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            DialogueContainer asset = AssetDatabase.LoadAssetAtPath<DialogueContainer>(path);
            if (asset)
            {
                SetTarget(asset);
                if (Event.current.clickCount > 1) EditorGUIUtility.PingObject(asset);
            }
            Event.current.Use();
        }
    }

    private static string ShortenFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return "/";
        folder = folder.Replace('\\', '/');
        const string assets = "Assets/";
        return folder.StartsWith(assets) ? folder.Substring(assets.Length) : folder;
    }

    // ------------------------------------------------------------------
    // Main pane
    // ------------------------------------------------------------------

    private void DrawMainPane()
    {
        EditorGUILayout.BeginVertical();
        {
            if (!target)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "Select a DialogueContainer in the browser, or pick one in the Project window.",
                    MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            so.Update();

            // Resize work happens only between frames, never mid-layout: changing an array
            // size while drawing would desync the Layout and Repaint control counts.
            if (Event.current.type == EventType.Layout)
            {
                ApplyPendingEdits();
                EnsureGridSize();
            }

            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
            {
                DrawAssetHeader();
                DrawDefaultVoice();
                DrawSpeakersSection();
                if (IsMultiObject) DrawObjectsSection();
                DrawNodesSection();
                EditorGUILayout.Space(20f);
            }
            EditorGUILayout.EndScrollView();

            so.ApplyModifiedProperties();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawAssetHeader()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(target.name, EditorStyles.largeLabel);

        if (IsWithModels)
        {
            EditorGUI.BeginChangeCheck();
            previewController = (TalkableWModelsController)EditorGUILayout.ObjectField(
                new GUIContent("Preview controller",
                    "Optional scene controller. Used only to name the object columns and to " +
                    "turn model ids into dropdowns of real model names. Never saved to the asset."),
                previewController, typeof(TalkableWModelsController), true);
            if (EditorGUI.EndChangeCheck()) Repaint();
        }
        else if (IsWithArmature)
        {
            EditorGUI.BeginChangeCheck();
            previewArmatureController = (TalkableWithArmatureController)EditorGUILayout.ObjectField(
                new GUIContent("Preview controller",
                    "Optional scene controller. Used only to name the object columns and to " +
                    "turn animation ids into dropdowns of real clip names. Never saved to the asset."),
                previewArmatureController, typeof(TalkableWithArmatureController), true);
            if (EditorGUI.EndChangeCheck()) Repaint();
        }
        else if (IsVisualNovel)
        {
            vnDialogueFoldout = EditorGUILayout.Foldout(vnDialogueFoldout,
                "Visual novel: dialogue start / end", true);
            if (vnDialogueFoldout)
            {
                EditorGUI.indentLevel++;
                foreach (string field in VisualNovelDialogueFields)
                {
                    SerializedProperty prop = so.FindProperty(field);
                    if (prop != null) EditorGUILayout.PropertyField(prop);
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4f);
            }
        }
    }

    private void DrawDefaultVoice()
    {
        SerializedProperty prop = so.FindProperty("defaultVoiceClips");
        if (prop == null) return;
        EditorGUILayout.PropertyField(prop, true);
        EditorGUILayout.Space(4f);
    }

    private void DrawSpeakersSection()
    {
        speakersFoldout = EditorGUILayout.Foldout(speakersFoldout, "Speaker id lookup", true);
        if (!speakersFoldout) return;

        EditorGUI.indentLevel++;
        EditorGUILayout.HelpBox(
            "Optional. A node with a speakerId is re-initialised at runtime from the prefab " +
            "registered under the same id.", MessageType.None);
        EditorGUILayout.PropertyField(speakerIdsProp, true);
        EditorGUILayout.PropertyField(speakerPrefabsProp, true);
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4f);
    }

    private void DrawObjectsSection()
    {
        objectsFoldout = EditorGUILayout.Foldout(objectsFoldout,
            $"Objects ({objectsProp.arraySize})", true);
        if (!objectsFoldout) return;

        EditorGUI.indentLevel++;
        EditorGUILayout.HelpBox(
            "Scene objects can't be stored in an asset - these slots normally stay empty and " +
            "just define how many objects the dialogue drives. The controller binds the real " +
            "objects by index at runtime.", MessageType.None);

        EditorGUI.BeginChangeCheck();
        int newCount = EditorGUILayout.DelayedIntField("Object slots", objectsProp.arraySize);
        if (EditorGUI.EndChangeCheck() && newCount >= 0 && newCount != objectsProp.arraySize)
        {
            // Queued like the node edits: EnsureGridSize widens every row next Layout.
            pendingObjectCount = newCount;
            Repaint();
        }

        for (int i = 0; i < objectsProp.arraySize; i++)
        {
            EditorGUILayout.PropertyField(objectsProp.GetArrayElementAtIndex(i),
                new GUIContent($"Slot {i}  ({ObjectLabel(i)})"));
        }

        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4f);
    }

    // ------------------------------------------------------------------
    // Nodes
    // ------------------------------------------------------------------

    private void DrawNodesSection()
    {
        EditorGUILayout.Space(4f);

        EditorGUILayout.BeginHorizontal();
        {
            EditorGUILayout.LabelField($"Nodes ({nodesProp.arraySize})", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Expand all", EditorStyles.miniButtonLeft, GUILayout.Width(74f)))
                for (int i = 0; i < nodesProp.arraySize; i++) { expanded.Add(i); vnExpanded.Add(i); }

            if (GUILayout.Button("Collapse all", EditorStyles.miniButtonRight, GUILayout.Width(80f)))
            {
                expanded.Clear();
                vnExpanded.Clear();
            }
        }
        EditorGUILayout.EndHorizontal();

        if (nodesProp.arraySize == 0)
            EditorGUILayout.HelpBox("This dialogue has no nodes yet.", MessageType.Info);

        int moveFrom = -1, moveTo = -1, insertAt = -1, deleteAt = -1;

        for (int i = 0; i < nodesProp.arraySize; i++)
        {
            NodeAction action = DrawNode(i);

            switch (action)
            {
                case NodeAction.MoveUp: moveFrom = i; moveTo = i - 1; break;
                case NodeAction.MoveDown: moveFrom = i; moveTo = i + 1; break;
                case NodeAction.InsertAfter: insertAt = i + 1; break;
                case NodeAction.Delete: deleteAt = i; break;
            }
        }

        EditorGUILayout.Space(6f);
        if (GUILayout.Button("Add node", GUILayout.Height(24f)))
            insertAt = nodesProp.arraySize;

        // Queue only - the list is resized on the next Layout event, never mid-draw.
        if (moveFrom >= 0 && moveTo >= 0 && moveTo < nodesProp.arraySize)
        {
            pendingMoveFrom = moveFrom;
            pendingMoveTo = moveTo;
            Repaint();
        }
        else if (insertAt >= 0)
        {
            pendingInsertAt = insertAt;
            Repaint();
        }
        else if (deleteAt >= 0)
        {
            pendingDeleteAt = deleteAt;
            Repaint();
        }
    }

    /// <summary>Applies whatever structural edit the previous frame queued.</summary>
    private void ApplyPendingEdits()
    {
        if (so == null || nodesProp == null) return;

        if (pendingObjectCount >= 0 && objectsProp != null)
        {
            objectsProp.arraySize = pendingObjectCount;
            pendingObjectCount = -1;

            so.ApplyModifiedProperties();
            SyncGrid();
            so.Update();
        }

        if (pendingMoveFrom >= 0 && pendingMoveTo >= 0)
        {
            int from = pendingMoveFrom, to = pendingMoveTo;
            pendingMoveFrom = pendingMoveTo = -1;

            if (from < nodesProp.arraySize && to < nodesProp.arraySize) MoveNode(from, to);
        }
        else if (pendingInsertAt >= 0)
        {
            int at = pendingInsertAt;
            pendingInsertAt = -1;
            InsertNode(at);
        }
        else if (pendingDeleteAt >= 0)
        {
            int at = pendingDeleteAt;
            pendingDeleteAt = -1;

            if (at < nodesProp.arraySize) DeleteNode(at);
        }
    }

    private enum NodeAction { None, MoveUp, MoveDown, InsertAfter, Delete }

    private NodeAction DrawNode(int index)
    {
        NodeAction action = NodeAction.None;

        SerializedProperty node = nodesProp.GetArrayElementAtIndex(index);
        bool isExpanded = expanded.Contains(index);

        EditorGUILayout.BeginVertical(nodeBoxStyle);
        {
            // ---- header row: index, portrait, speaker name, text preview, buttons ----
            EditorGUILayout.BeginHorizontal();
            {
                Rect foldRect = GUILayoutUtility.GetRect(14f, PORTRAIT_SIZE, GUILayout.Width(14f));
                bool newExpanded = EditorGUI.Foldout(
                    new Rect(foldRect.x, foldRect.y + 2f, 14f, 16f), isExpanded, GUIContent.none);
                if (newExpanded != isExpanded)
                {
                    // Record it but keep drawing this pass at the old size - the next frame
                    // lays out the new one. Growing/shrinking mid-pass desyncs the layout.
                    if (newExpanded) expanded.Add(index);
                    else expanded.Remove(index);
                    Repaint();
                }

                Rect idxRect = GUILayoutUtility.GetRect(24f, PORTRAIT_SIZE, GUILayout.Width(24f));
                GUI.Label(idxRect, index.ToString(), indexStyle);

                Rect portraitRect = GUILayoutUtility.GetRect(
                    PORTRAIT_SIZE, PORTRAIT_SIZE, GUILayout.Width(PORTRAIT_SIZE));
                DrawSpritePreview(portraitRect, ResolvePortrait(node, index));

                EditorGUILayout.BeginVertical();
                {
                    GUILayout.Space(2f);
                    EditorGUILayout.LabelField(ResolveSpeakerName(node, index), previewNameStyle);

                    if (!isExpanded)
                        EditorGUILayout.LabelField(Preview(node.FindPropertyRelative("text").stringValue),
                            previewTextStyle);
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical(GUILayout.Width(70f));
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        using (new EditorGUI.DisabledScope(index == 0))
                            if (GUILayout.Button(new GUIContent("▲", "Move up"),
                                    EditorStyles.miniButtonLeft, GUILayout.Width(24f)))
                                action = NodeAction.MoveUp;

                        using (new EditorGUI.DisabledScope(index == nodesProp.arraySize - 1))
                            if (GUILayout.Button(new GUIContent("▼", "Move down"),
                                    EditorStyles.miniButtonMid, GUILayout.Width(24f)))
                                action = NodeAction.MoveDown;

                        if (GUILayout.Button(new GUIContent("+", "Insert a copy below"),
                                EditorStyles.miniButtonRight, GUILayout.Width(24f)))
                            action = NodeAction.InsertAfter;
                    }
                    EditorGUILayout.EndHorizontal();

                    if (GUILayout.Button(new GUIContent("Delete", "Remove this node"),
                            EditorStyles.miniButton))
                        action = NodeAction.Delete;
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();

            // ---- text ----
            SerializedProperty text = node.FindPropertyRelative("text");
            EditorGUI.BeginChangeCheck();
            string newText = EditorGUILayout.TextArea(text.stringValue, textAreaStyle,
                GUILayout.MinHeight(isExpanded ? 60f : 36f));
            if (EditorGUI.EndChangeCheck()) text.stringValue = newText;

            // ---- per-node object grid ----
            if (IsMultiObject) DrawNodeGrid(index);
            else if (IsVisualNovel) DrawVisualNovelRow(index);

            // ---- advanced ----
            if (isExpanded) DrawNodeAdvanced(node);
        }
        EditorGUILayout.EndVertical();

        return action;
    }

    private void DrawNodeAdvanced(SerializedProperty node)
    {
        EditorGUILayout.Space(4f);
        EditorGUI.indentLevel++;

        SerializedProperty speakerIsThis = node.FindPropertyRelative("speakerIsThis");
        EditorGUILayout.PropertyField(speakerIsThis,
            new GUIContent("Speaker is the talkable",
                "The object that started the dialogue speaks this node."));
        EditorGUILayout.PropertyField(node.FindPropertyRelative("overWriteValues"),
            new GUIContent("Overwrite values",
                "Re-initialise the fields below from the speaker source instead of keeping " +
                "whatever is authored here."));

        using (new EditorGUI.DisabledScope(speakerIsThis.boolValue))
        {
            EditorGUILayout.PropertyField(node.FindPropertyRelative("speakerId"));
            EditorGUILayout.PropertyField(node.FindPropertyRelative("initFromScriptableObject"));
            EditorGUILayout.PropertyField(node.FindPropertyRelative("speaker"));
            EditorGUILayout.PropertyField(node.FindPropertyRelative("speakerGameObject"));
            EditorGUILayout.PropertyField(node.FindPropertyRelative("speakerName"));
            EditorGUILayout.PropertyField(node.FindPropertyRelative("speakerPortrait"));
        }

        SerializedProperty useAtlas = node.FindPropertyRelative("useSpriteAtlas");
        EditorGUILayout.PropertyField(useAtlas);
        if (useAtlas.boolValue)
        {
            EditorGUILayout.PropertyField(node.FindPropertyRelative("spriteAtlas"), true);
            EditorGUILayout.PropertyField(node.FindPropertyRelative("spriteId"));
        }

        EditorGUILayout.PropertyField(node.FindPropertyRelative("material"));

        SerializedProperty useTextColor = node.FindPropertyRelative("useCustomTextColor");
        EditorGUILayout.PropertyField(useTextColor,
            new GUIContent("Custom text color",
                "Tint the dialogue text for this node. Overwritten by the speaking " +
                "Talkable's color when that talkable defines one."));
        if (useTextColor.boolValue)
            EditorGUILayout.PropertyField(node.FindPropertyRelative("textColor"),
                new GUIContent("Text color"));

        SerializedProperty autoSkip = node.FindPropertyRelative("autoSkip");
        EditorGUILayout.PropertyField(autoSkip,
            new GUIContent("Auto skip",
                "Move on to the next node by itself once the text has typed out. Off: the " +
                "DialogueManager's own auto-skip setting decides."));
        if (autoSkip.boolValue)
            EditorGUILayout.PropertyField(node.FindPropertyRelative("autoSkipDelay"),
                new GUIContent("Auto skip delay", "Seconds after the text finishes typing."));

        EditorGUILayout.PropertyField(node.FindPropertyRelative("onNodeStart"));
        EditorGUILayout.PropertyField(node.FindPropertyRelative("onNodeEnd"));

        EditorGUI.indentLevel--;
    }

    /// <summary>
    /// Draws one node's row of the per-object grid: which object speaks it, and for every
    /// object whether it is enabled, which model it uses, whether its eyes are on and
    /// whether it talks.
    /// </summary>
    private void DrawNodeGrid(int nodeIndex)
    {
        if (nodeSettingsProp == null) return;
        if (nodeIndex >= nodeSettingsProp.arraySize) return;

        SerializedProperty row = nodeSettingsProp.GetArrayElementAtIndex(nodeIndex);
        SerializedProperty objectSettings = row.FindPropertyRelative("objectSettings");
        SerializedProperty speakerIndex = row.FindPropertyRelative("speakerObjectIndex");

        int objectCount = objectsProp.arraySize;
        if (objectCount == 0)
        {
            EditorGUILayout.LabelField(
                "No object slots - set the count in the Objects section above.",
                EditorStyles.centeredGreyMiniLabel);
            return;
        }

        EditorGUILayout.Space(2f);

        // Speaking object popup: -1 means "leave the node's own speaker fields alone".
        string[] speakerOptions = new string[objectCount + 1];
        speakerOptions[0] = "(node's own speaker)";
        for (int i = 0; i < objectCount; i++) speakerOptions[i + 1] = $"{i}: {ObjectLabel(i)}";

        EditorGUI.BeginChangeCheck();
        int picked = EditorGUILayout.Popup(
            new GUIContent("Speaking object",
                "Whose name / portrait / voice fills the dialogue window on this node."),
            Mathf.Clamp(speakerIndex.intValue + 1, 0, objectCount),
            speakerOptions);
        if (EditorGUI.EndChangeCheck()) speakerIndex.intValue = picked - 1;

        // Column headers.
        EditorGUILayout.BeginHorizontal();
        {
            EditorGUILayout.LabelField("Object", EditorStyles.miniBoldLabel, GUILayout.Width(COL_OBJECT));
            EditorGUILayout.LabelField(new GUIContent("On", "Object is active on this node"),
                EditorStyles.miniBoldLabel, GUILayout.Width(COL_TOGGLE));
            if (IsWithArmature)
            {
                EditorGUILayout.LabelField(new GUIContent("Animation", "Clip played when the node starts"),
                    EditorStyles.miniBoldLabel, GUILayout.Width(COL_MODEL));
                EditorGUILayout.LabelField(new GUIContent("Loop",
                        "Loop the clip until the next node. Off: play once, then a random idle loops"),
                    EditorStyles.miniBoldLabel, GUILayout.Width(COL_TOGGLE));
                EditorGUILayout.LabelField(new GUIContent("Talk", "Plays the talking SFX and node animation"),
                    EditorStyles.miniBoldLabel, GUILayout.Width(COL_TOGGLE));
            }
            else
            {
                EditorGUILayout.LabelField("Model", EditorStyles.miniBoldLabel, GUILayout.Width(COL_MODEL));
                EditorGUILayout.LabelField(new GUIContent("Eyes", "Enable the model's eyes"),
                    EditorStyles.miniBoldLabel, GUILayout.Width(COL_TOGGLE));
                EditorGUILayout.LabelField(new GUIContent("Talk", "Plays the talking animation on this node"),
                    EditorStyles.miniBoldLabel, GUILayout.Width(COL_TOGGLE));
            }
            GUILayout.FlexibleSpace();
        }
        EditorGUILayout.EndHorizontal();

        int rows = Mathf.Min(objectCount, objectSettings.arraySize);
        for (int i = 0; i < rows; i++)
        {
            SerializedProperty settings = objectSettings.GetArrayElementAtIndex(i);
            SerializedProperty enableObject = settings.FindPropertyRelative("enableObject");
            SerializedProperty talk = settings.FindPropertyRelative("talk");

            // Armature rows: animation + loop. Models rows: model + eyes.
            SerializedProperty idProp = settings.FindPropertyRelative(IsWithArmature ? "animationId" : "modelId");
            SerializedProperty toggleProp = settings.FindPropertyRelative(IsWithArmature ? "loop" : "enableEyes");

            EditorGUILayout.BeginHorizontal();
            {
                bool speaks = speakerIndex.intValue == i;
                EditorGUILayout.LabelField(
                    new GUIContent((speaks ? "▸ " : "   ") + ObjectLabel(i)),
                    speaks ? EditorStyles.boldLabel : EditorStyles.label,
                    GUILayout.Width(COL_OBJECT));

                enableObject.boolValue = EditorGUILayout.Toggle(
                    enableObject.boolValue, GUILayout.Width(COL_TOGGLE));

                using (new EditorGUI.DisabledScope(!enableObject.boolValue))
                {
                    if (IsWithArmature) DrawAnimationField(i, idProp);
                    else DrawModelField(i, idProp);

                    toggleProp.boolValue = EditorGUILayout.Toggle(
                        toggleProp.boolValue, GUILayout.Width(COL_TOGGLE));

                    talk.boolValue = EditorGUILayout.Toggle(
                        talk.boolValue, GUILayout.Width(COL_TOGGLE));
                }

                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>
    /// Draws one node's visual novel row: a one-line summary (background thumbnail, transition,
    /// music, ambience, SFX and effects) that expands into every VisualNovelNodeSettings field.
    /// </summary>
    private void DrawVisualNovelRow(int nodeIndex)
    {
        if (nodeSettingsProp == null || nodeIndex >= nodeSettingsProp.arraySize) return;

        SerializedProperty row = nodeSettingsProp.GetArrayElementAtIndex(nodeIndex);
        bool isOpen = vnExpanded.Contains(nodeIndex);

        EditorGUILayout.Space(2f);
        EditorGUILayout.BeginHorizontal();
        {
            Rect foldRect = GUILayoutUtility.GetRect(96f, 18f, GUILayout.Width(96f));
            bool newOpen = EditorGUI.Foldout(foldRect, isOpen, "Visual novel", true);
            if (newOpen != isOpen)
            {
                // Same as the node foldout: applied next frame, never mid-pass.
                if (newOpen) vnExpanded.Add(nodeIndex);
                else vnExpanded.Remove(nodeIndex);
                Repaint();
            }

            Sprite background = VisualNovelBackgroundOf(row);
            Rect thumb = GUILayoutUtility.GetRect(32f, 18f, GUILayout.Width(32f));
            DrawSpritePreview(thumb, background);

            EditorGUILayout.LabelField(VisualNovelSummary(row, background), EditorStyles.miniLabel);
        }
        EditorGUILayout.EndHorizontal();

        if (!isOpen) return;

        EditorGUI.indentLevel++;
        DrawChildren(row);
        EditorGUI.indentLevel--;
    }

    /// <summary>The sprite a row switches to, or null when it keeps / covers the background.</summary>
    private static Sprite VisualNovelBackgroundOf(SerializedProperty row)
    {
        VisualNovelTransition t = (VisualNovelTransition)row.FindPropertyRelative("transition").enumValueIndex;
        if (t == VisualNovelTransition.KeepPrevious || t == VisualNovelTransition.FadeToColor) return null;
        return row.FindPropertyRelative("background").objectReferenceValue as Sprite;
    }

    private static string VisualNovelSummary(SerializedProperty row, Sprite background)
    {
        List<string> parts = new List<string>();

        VisualNovelTransition t = (VisualNovelTransition)row.FindPropertyRelative("transition").enumValueIndex;
        switch (t)
        {
            case VisualNovelTransition.KeepPrevious: parts.Add("same background"); break;
            case VisualNovelTransition.FadeToColor: parts.Add("fade to color"); break;
            default: parts.Add($"{ObjectNames.NicifyVariableName(t.ToString())} to {(background ? background.name : "none")}"); break;
        }

        string music = AudioSummary(row, "music", "bgm", "bgmClip");
        if (music != null) parts.Add("music: " + music);

        string ambience = AudioSummary(row, "ambience", "ambienceClip", null);
        if (ambience != null) parts.Add("ambience: " + ambience);

        VisualNovelVoice voice = (VisualNovelVoice)row.FindPropertyRelative("voice").enumValueIndex;
        if (voice == VisualNovelVoice.Silent) parts.Add("voice: silent");
        else if (voice == VisualNovelVoice.Custom)
            parts.Add($"voice: custom x{row.FindPropertyRelative("voiceClips").arraySize}");

        int sfx = row.FindPropertyRelative("sfx").arraySize;
        if (sfx > 0) parts.Add($"SFX x{sfx}");

        string start = EffectNames(row.FindPropertyRelative("startEffects"));
        if (start != null) parts.Add("FX: " + start);

        string held = EffectNames(row.FindPropertyRelative("sustainedEffects"));
        if (held != null) parts.Add("during: " + held);

        return string.Join("  |  ", parts);
    }

    /// <summary>"name" for Play, "stop" for Stop, null for Keep.</summary>
    private static string AudioSummary(SerializedProperty row, string changeField, string field, string fallbackField)
    {
        VisualNovelAudioChange change = (VisualNovelAudioChange)row.FindPropertyRelative(changeField).enumValueIndex;
        if (change == VisualNovelAudioChange.Keep) return null;
        if (change == VisualNovelAudioChange.Stop) return "stop";

        Object clip = row.FindPropertyRelative(field).objectReferenceValue;
        if (!clip && fallbackField != null) clip = row.FindPropertyRelative(fallbackField).objectReferenceValue;
        return clip ? clip.name : "(none)";
    }

    private static string EffectNames(SerializedProperty cues)
    {
        List<string> names = new List<string>();
        for (int i = 0; i < cues.arraySize; i++)
        {
            Object effect = cues.GetArrayElementAtIndex(i).FindPropertyRelative("effect").objectReferenceValue;
            if (effect) names.Add(effect.name);
        }
        return names.Count > 0 ? string.Join(", ", names) : null;
    }

    /// <summary>Draws every visible child of <paramref name="parent"/>, headers included.</summary>
    private static void DrawChildren(SerializedProperty parent)
    {
        SerializedProperty it = parent.Copy();
        SerializedProperty end = parent.GetEndProperty();
        if (!it.NextVisible(true)) return;

        while (!SerializedProperty.EqualContents(it, end))
        {
            EditorGUILayout.PropertyField(it, true);
            if (!it.NextVisible(false)) break;
        }
    }

    /// <summary>
    /// Model id as a dropdown of real model names when a preview controller is bound,
    /// falling back to a plain int field otherwise.
    /// </summary>
    private void DrawModelField(int objectIndex, SerializedProperty modelId)
    {
        List<GameObject> models = ModelsOf(objectIndex);

        if (models == null || models.Count == 0)
        {
            modelId.intValue = EditorGUILayout.IntField(modelId.intValue, GUILayout.Width(COL_MODEL));
            return;
        }

        string[] names = new string[models.Count];
        for (int i = 0; i < models.Count; i++)
            names[i] = $"{i}: {(models[i] ? models[i].name : "<missing>")}";

        int current = Mathf.Clamp(modelId.intValue, 0, models.Count - 1);
        int picked = EditorGUILayout.Popup(current, names, GUILayout.Width(COL_MODEL));
        if (picked != modelId.intValue) modelId.intValue = picked;
    }

    /// <summary>
    /// Animation id as a dropdown of real clip names (plus "-1: idle") when the object's clips
    /// are known, falling back to a plain int field otherwise.
    /// </summary>
    private void DrawAnimationField(int objectIndex, SerializedProperty animationId)
    {
        List<AnimationClip> clips = AnimationsOf(objectIndex);

        if (clips == null || clips.Count == 0)
        {
            animationId.intValue = Mathf.Max(-1,
                EditorGUILayout.IntField(animationId.intValue, GUILayout.Width(COL_MODEL)));
            return;
        }

        // Option 0 is "no clip" (-1); option i + 1 is clip i.
        string[] names = new string[clips.Count + 1];
        names[0] = "-1: (idle)";
        for (int i = 0; i < clips.Count; i++)
            names[i + 1] = $"{i}: {(clips[i] ? clips[i].name : "<missing>")}";

        int current = Mathf.Clamp(animationId.intValue + 1, 0, clips.Count);
        int picked = EditorGUILayout.Popup(current, names, GUILayout.Width(COL_MODEL)) - 1;
        if (picked != animationId.intValue) animationId.intValue = picked;
    }

    // ------------------------------------------------------------------
    // Structural edits (keep the grid in sync with the node list)
    // ------------------------------------------------------------------

    private void InsertNode(int index)
    {
        // No explicit Undo.RecordObject here: ApplyModifiedProperties registers the undo
        // step itself, and recording as well would push a duplicate entry onto the stack.
        index = Mathf.Clamp(index, 0, nodesProp.arraySize);

        // Unity copies the preceding element, which is what you want when authoring a
        // sequence: the speaker setup carries over and only the text needs replacing.
        if (nodesProp.arraySize == 0) nodesProp.InsertArrayElementAtIndex(0);
        else nodesProp.InsertArrayElementAtIndex(Mathf.Max(0, index - 1));

        int newIndex = nodesProp.arraySize == 1 ? 0 : index;
        if (newIndex >= nodesProp.arraySize) newIndex = nodesProp.arraySize - 1;

        SerializedProperty inserted = nodesProp.GetArrayElementAtIndex(newIndex);
        inserted.FindPropertyRelative("text").stringValue = "";
        inserted.FindPropertyRelative("initialized").boolValue = false;

        if (nodeSettingsProp != null)
        {
            if (nodeSettingsProp.arraySize == 0) nodeSettingsProp.InsertArrayElementAtIndex(0);
            else nodeSettingsProp.InsertArrayElementAtIndex(
                Mathf.Clamp(index - 1, 0, nodeSettingsProp.arraySize - 1));
        }

        so.ApplyModifiedProperties();
        SyncGrid();
        AssignNodeIds();

        // Unity copied the neighbour's VN row too. A copied row would replay that node's SFX and
        // effects, so a new node starts clean instead: same background, nothing fired.
        if (target is VisualNovelDialogueContainer vn && newIndex < vn.nodeSettings.Count)
        {
            Undo.RecordObject(vn, "Insert dialogue node");
            vn.nodeSettings[newIndex] = new VisualNovelNodeSettings();
            EditorUtility.SetDirty(vn);
        }

        expanded.Add(newIndex);
        so.Update();
    }

    private void DeleteNode(int index)
    {
        nodesProp.DeleteArrayElementAtIndex(index);

        if (nodeSettingsProp != null && index < nodeSettingsProp.arraySize)
            nodeSettingsProp.DeleteArrayElementAtIndex(index);

        expanded.Remove(index);
        vnExpanded.Remove(index);

        so.ApplyModifiedProperties();
        SyncGrid();
        AssignNodeIds();
        so.Update();
    }

    private void MoveNode(int from, int to)
    {
        nodesProp.MoveArrayElement(from, to);

        if (nodeSettingsProp != null
            && from < nodeSettingsProp.arraySize
            && to < nodeSettingsProp.arraySize)
            nodeSettingsProp.MoveArrayElement(from, to);

        bool fromWasExpanded = expanded.Contains(from);
        bool toWasExpanded = expanded.Contains(to);
        if (toWasExpanded) expanded.Add(from); else expanded.Remove(from);
        if (fromWasExpanded) expanded.Add(to); else expanded.Remove(to);

        bool fromVnOpen = vnExpanded.Contains(from);
        bool toVnOpen = vnExpanded.Contains(to);
        if (toVnOpen) vnExpanded.Add(from); else vnExpanded.Remove(from);
        if (fromVnOpen) vnExpanded.Add(to); else vnExpanded.Remove(to);

        so.ApplyModifiedProperties();
        AssignNodeIds();
        so.Update();
    }

    private void SyncGrid()
    {
        (target as DialogueContainerWithModels)?.SyncNodeSettings();
        (target as DialogueContainerWithArmature)?.SyncNodeSettings();
        (target as VisualNovelDialogueContainer)?.SyncNodeSettings();
        EditorUtility.SetDirty(target);
    }

    /// <summary>
    /// Makes sure the grid is exactly nodes x objects. Called once per frame on the Layout
    /// event, so a hand-edited or newly grown asset repairs itself without the user having
    /// to touch anything.
    /// </summary>
    private void EnsureGridSize()
    {
        if (nodeSettingsProp == null) return;

        bool mismatched = nodeSettingsProp.arraySize != nodesProp.arraySize;

        // Visual novel rows have no per-object entries - only the row count matters.
        if (!mismatched && objectsProp != null)
        {
            for (int i = 0; i < nodeSettingsProp.arraySize; i++)
            {
                SerializedProperty settings = nodeSettingsProp
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("objectSettings");

                if (settings.arraySize == objectsProp.arraySize) continue;

                mismatched = true;
                break;
            }
        }

        if (!mismatched) return;

        so.ApplyModifiedProperties();
        SyncGrid();
        so.Update();
    }

    private void AssignNodeIds()
    {
        if (!target) return;
        target.AssignNodeIds();
        EditorUtility.SetDirty(target);
    }

    // ------------------------------------------------------------------
    // Preview resolution
    // ------------------------------------------------------------------

    /// <summary>Name of object slot <paramref name="index"/>, preferring the bound scene controller.</summary>
    private string ObjectLabel(int index)
    {
        Talkable bound = BoundObject(index);
        if (bound) return string.IsNullOrEmpty(bound.speakerName) ? bound.name : bound.speakerName;

        if (objectsProp != null && index < objectsProp.arraySize)
        {
            UnityEngine.Object slot = objectsProp.GetArrayElementAtIndex(index).objectReferenceValue;
            if (slot) return slot.name;
        }

        return $"Slot {index}";
    }

    /// <summary>Scene object bound to slot <paramref name="index"/> by the preview controller matching the asset type.</summary>
    private Talkable BoundObject(int index)
    {
        if (IsWithArmature)
        {
            if (!previewArmatureController || previewArmatureController.objects == null) return null;
            if (index < 0 || index >= previewArmatureController.objects.Count) return null;
            return previewArmatureController.objects[index];
        }

        if (!previewController || previewController.objects == null) return null;
        if (index < 0 || index >= previewController.objects.Count) return null;
        return previewController.objects[index];
    }

    private List<AnimationClip> AnimationsOf(int index)
    {
        TalkableWithArmature bound = BoundObject(index) as TalkableWithArmature;
        if (bound) return bound.animations;

        if (objectsProp != null && index < objectsProp.arraySize)
        {
            TalkableWithArmature slot =
                objectsProp.GetArrayElementAtIndex(index).objectReferenceValue as TalkableWithArmature;
            if (slot) return slot.animations;
        }

        return null;
    }

    private List<GameObject> ModelsOf(int index)
    {
        TalkableWithModels bound = BoundObject(index) as TalkableWithModels;
        if (bound) return bound.models;

        if (objectsProp != null && index < objectsProp.arraySize)
        {
            TalkableWithModels slot =
                objectsProp.GetArrayElementAtIndex(index).objectReferenceValue as TalkableWithModels;
            if (slot) return slot.models;
        }

        return null;
    }

    /// <summary>
    /// Best-effort portrait for the preview, following the same precedence the runtime uses:
    /// sprite atlas entry, then the explicit portrait, then the speaker asset, then the
    /// speaker GameObject's Talkable, then the object that speaks this node.
    /// </summary>
    private Sprite ResolvePortrait(SerializedProperty node, int nodeIndex)
    {
        bool useAtlas = node.FindPropertyRelative("useSpriteAtlas").boolValue;
        SerializedProperty atlas = node.FindPropertyRelative("spriteAtlas");
        int spriteId = node.FindPropertyRelative("spriteId").intValue;

        if (useAtlas && atlas.arraySize > 0 && spriteId >= 0 && spriteId < atlas.arraySize)
        {
            Sprite fromAtlas = atlas.GetArrayElementAtIndex(spriteId).objectReferenceValue as Sprite;
            if (fromAtlas) return fromAtlas;
        }

        Sprite direct = node.FindPropertyRelative("speakerPortrait").objectReferenceValue as Sprite;
        if (direct) return direct;

        DialogueSpeaker speaker =
            node.FindPropertyRelative("speaker").objectReferenceValue as DialogueSpeaker;
        if (speaker && speaker.portrait) return speaker.portrait;

        GameObject go = node.FindPropertyRelative("speakerGameObject").objectReferenceValue as GameObject;
        Sprite fromGo = PortraitOf(go, spriteId);
        if (fromGo) return fromGo;

        Talkable speaking = SpeakingObject(nodeIndex);
        if (speaking) return PortraitOf(speaking.gameObject, spriteId);

        return null;
    }

    /// <summary>Portrait of a Talkable GameObject without touching the runtime-only getters.</summary>
    private static Sprite PortraitOf(GameObject go, int spriteId)
    {
        if (!go) return null;

        if (go.TryGetComponent(out Talkable talkable))
        {
            if (talkable.portraitAtlas != null
                && spriteId >= 0
                && spriteId < talkable.portraitAtlas.Length
                && talkable.portraitAtlas[spriteId])
                return talkable.portraitAtlas[spriteId];

            if (talkable.portrait) return talkable.portrait;
        }

        SpriteRenderer renderer = go.GetComponentInChildren<SpriteRenderer>(true);
        return renderer ? renderer.sprite : null;
    }

    private string ResolveSpeakerName(SerializedProperty node, int nodeIndex)
    {
        Talkable speaking = SpeakingObject(nodeIndex);
        if (speaking)
            return string.IsNullOrEmpty(speaking.speakerName) ? speaking.name : speaking.speakerName;

        string authored = node.FindPropertyRelative("speakerName").stringValue;
        if (!string.IsNullOrEmpty(authored)) return authored;

        DialogueSpeaker speaker =
            node.FindPropertyRelative("speaker").objectReferenceValue as DialogueSpeaker;
        if (speaker && !string.IsNullOrEmpty(speaker.speakerName)) return speaker.speakerName;

        GameObject go = node.FindPropertyRelative("speakerGameObject").objectReferenceValue as GameObject;
        if (go)
            return go.TryGetComponent(out Talkable talkable) && !string.IsNullOrEmpty(talkable.speakerName)
                ? talkable.speakerName
                : go.name;

        string id = node.FindPropertyRelative("speakerId").stringValue;
        if (!string.IsNullOrEmpty(id)) return $"id: {id}";

        if (node.FindPropertyRelative("speakerIsThis").boolValue)
            return IsVisualNovel ? "(narrator)" : "(the talkable that starts this dialogue)";

        return "(no speaker)";
    }

    /// <summary>The object driving this node's speaker override, if the row sets one.</summary>
    private Talkable SpeakingObject(int nodeIndex)
    {
        if (!IsMultiObject) return null;
        if (nodeSettingsProp == null || nodeIndex >= nodeSettingsProp.arraySize) return null;

        SerializedProperty row = nodeSettingsProp.GetArrayElementAtIndex(nodeIndex);
        int index = row.FindPropertyRelative("speakerObjectIndex").intValue;
        return index < 0 ? null : BoundObject(index);
    }

    private static string Preview(string text)
    {
        if (string.IsNullOrEmpty(text)) return "<empty>";

        text = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return text.Length <= 120 ? text : text.Substring(0, 117) + "...";
    }

    /// <summary>
    /// Draws a sprite into <paramref name="rect"/>, preserving aspect and honouring the
    /// sprite's rect inside its texture (so sheet-sliced sprites show the right frame).
    /// </summary>
    private static void DrawSpritePreview(Rect rect, Sprite sprite)
    {
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.15f));
        if (!sprite) return;

        Texture2D texture = sprite.texture;
        if (!texture) return;

        Rect uv;
        try
        {
            Rect textureRect = sprite.textureRect;
            uv = new Rect(
                textureRect.x / texture.width,
                textureRect.y / texture.height,
                textureRect.width / texture.width,
                textureRect.height / texture.height);
        }
        catch
        {
            // Packed sprites can refuse textureRect outside of play mode.
            uv = new Rect(0f, 0f, 1f, 1f);
        }

        float aspect = uv.height > 0f
            ? (uv.width * texture.width) / (uv.height * texture.height)
            : 1f;

        Rect fitted = rect;
        if (aspect > 1f)
        {
            fitted.height = rect.width / aspect;
            fitted.y += (rect.height - fitted.height) * 0.5f;
        }
        else if (aspect > 0f)
        {
            fitted.width = rect.height * aspect;
            fitted.x += (rect.width - fitted.width) * 0.5f;
        }

        GUI.DrawTextureWithTexCoords(fitted, texture, uv);
    }
}
