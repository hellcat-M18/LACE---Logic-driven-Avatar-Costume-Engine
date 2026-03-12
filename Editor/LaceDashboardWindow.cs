using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Lace.Runtime;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Lace.Editor
{
    /// <summary>
    /// LACE ダッシュボード。アバター内の全 CostumeItem を一覧表示し、
    /// 全設定のインライン編集・新規作成・削除を行える管理ウィンドウ。
    /// </summary>
    public class LaceDashboardWindow : EditorWindow
    {
        private const string ParentName = "LACE Menu Items";

        // ─── アバター選択 ───
        private VRCAvatarDescriptor[] _avatarsInScene;
        private int _avatarIndex;
        private VRCAvatarDescriptor _selectedAvatar;

        // ─── アイテム一覧 ───
        private CostumeItem[] _items;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private CostumeItem _expandedItem;

        // ─── 展開中アイテムの SerializedObject ───
        private SerializedObject _expandedSO;
        private SerializedProperty _sp_parameterName;
        private SerializedProperty _sp_generateMenuItem;
        private SerializedProperty _sp_defaultValue;
        private SerializedProperty _sp_parameterSynced;
        private SerializedProperty _sp_parameterSaved;
        private SerializedProperty _sp_autoCreateMenuPath;
        private SerializedProperty _sp_menuPath;
        private SerializedProperty _sp_installTargetMenu;
        private SerializedProperty _sp_menuFolder;
        private SerializedProperty _sp_icon;
        private SerializedProperty _sp_targetObjects;
        private SerializedProperty _sp_target;
        private SerializedProperty _sp_blendShapeNames;
        private SerializedProperty _sp_matchValue;
        private SerializedProperty _sp_unmatchValue;

        // ─── 条件エディタの UIステート ───
        private List<List<CondClause>> _conditionGroups;
        private CostumeItem _conditionGroupsOwner;
        private bool _showAdditionalConditions;

        // ─── 新規作成フォーム ───
        private bool _showCreateForm;
        private string _newItemName = "";
        private FolderEntry _newCreateEntry;

        // ─── フォルダピッカー ───
        private List<FolderEntry> _folderEntries;
        private string _newFolderInputKey;
        private string _newFolderName = "";
        private int _newFolderParentIndex;

        private struct FolderEntry
        {
            public string Path;
            public LaceMenuFolder Folder;           // 非 null = LACE フォルダ
            public VRCExpressionsMenu ExistingMenu; // 非 null = 既存メニュー
            // 両方 null = ルート
        }

        [MenuItem("Tools/LACE/Dashboard", false, 0)]
        private static void OpenWindow()
        {
            var win = GetWindow<LaceDashboardWindow>("LACE Dashboard");
            win.minSize = new Vector2(520, 340);
            win.Show();
        }

        private void OnEnable()
        {
            RefreshAvatars();
            RefreshItems();
        }

        private void OnFocus()
        {
            RefreshAvatars();
            RefreshItems();
        }

        private void OnHierarchyChange()
        {
            RefreshAvatars();
            RefreshItems();
            _folderEntries = null;
            Repaint();
        }

        private void OnSelectionChange()
        {
            // Hierarchy で VRCAvatarDescriptor を含む GO を選択した場合、自動でアバターを切り替え
            var sel = Selection.activeGameObject;
            if (sel != null)
            {
                var desc = sel.GetComponentInParent<VRCAvatarDescriptor>();
                if (desc != null && desc != _selectedAvatar)
                    SetAvatar(desc);
            }

            Repaint();
        }

        // ─── データ更新 ───

        private void RefreshAvatars()
        {
            _avatarsInScene = FindObjectsByType<VRCAvatarDescriptor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (_selectedAvatar == null && _avatarsInScene.Length > 0)
                SetAvatar(_avatarsInScene[0]);

            // 選択中のアバターが消えた場合
            if (_selectedAvatar != null && Array.IndexOf(_avatarsInScene, _selectedAvatar) < 0)
                SetAvatar(_avatarsInScene.Length > 0 ? _avatarsInScene[0] : null);
        }

        private void SetAvatar(VRCAvatarDescriptor avatar)
        {
            _selectedAvatar = avatar;
            _avatarIndex = _selectedAvatar != null
                ? Mathf.Max(0, Array.IndexOf(_avatarsInScene, _selectedAvatar))
                : 0;
            _folderEntries = null;
            RefreshItems();
        }

        private void RefreshItems()
        {
            if (_selectedAvatar != null)
                _items = _selectedAvatar.GetComponentsInChildren<CostumeItem>(true);
            else
                _items = Array.Empty<CostumeItem>();

            if (_expandedItem != null && Array.IndexOf(_items, _expandedItem) < 0)
            {
                _expandedItem = null;
                _expandedSO = null;
                _conditionGroups = null;
                _conditionGroupsOwner = null;
            }

            if (_expandedItem == null && _items.Length > 0)
                _expandedItem = _items[0];
        }

        /// <summary>展開中アイテムの SerializedObject とプロパティ群を確保する。</summary>
        private void EnsureExpandedSO()
        {
            if (_expandedItem == null) { _expandedSO = null; return; }
            if (_expandedSO != null && _expandedSO.targetObject == _expandedItem) return;

            _expandedSO = new SerializedObject(_expandedItem);
            _sp_parameterName     = _expandedSO.FindProperty("parameterName");
            _sp_generateMenuItem  = _expandedSO.FindProperty("generateMenuItem");
            _sp_defaultValue      = _expandedSO.FindProperty("defaultValue");
            _sp_parameterSynced   = _expandedSO.FindProperty("parameterSynced");
            _sp_parameterSaved    = _expandedSO.FindProperty("parameterSaved");
            _sp_autoCreateMenuPath = _expandedSO.FindProperty("autoCreateMenuPath");
            _sp_menuPath          = _expandedSO.FindProperty("menuPath");
            _sp_installTargetMenu = _expandedSO.FindProperty("installTargetMenu");
            _sp_menuFolder        = _expandedSO.FindProperty("menuFolder");
            _sp_icon              = _expandedSO.FindProperty("icon");
            _sp_targetObjects     = _expandedSO.FindProperty("targetObjects");
            _sp_target            = _expandedSO.FindProperty("target");
            _sp_blendShapeNames   = _expandedSO.FindProperty("blendShapeNames");
            _sp_matchValue        = _expandedSO.FindProperty("matchValue");
            _sp_unmatchValue      = _expandedSO.FindProperty("unmatchValue");
        }

        // ─── GUI ───

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(4);

            if (_selectedAvatar == null)
            {
                EditorGUILayout.HelpBox("シーンにアバターが見つかりません。", MessageType.Info);
                return;
            }

            DrawMainLayout();
        }

        private void DrawMainLayout()
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.Width(272));
            DrawItemList();
            EditorGUILayout.Space(4);
            DrawCreateForm();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginVertical();
            DrawDetailPane();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // アバター選択ドロップダウン
            if (_avatarsInScene != null && _avatarsInScene.Length > 0)
            {
                var labels = new string[_avatarsInScene.Length];
                for (int i = 0; i < _avatarsInScene.Length; i++)
                    labels[i] = _avatarsInScene[i] != null ? _avatarsInScene[i].gameObject.name : "(null)";

                int newIdx = EditorGUILayout.Popup(_avatarIndex, labels, EditorStyles.toolbarPopup);
                if (newIdx != _avatarIndex && newIdx >= 0 && newIdx < _avatarsInScene.Length)
                    SetAvatar(_avatarsInScene[newIdx]);
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("更新", EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                RefreshAvatars();
                RefreshItems();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawItemList()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("CostumeItem Tabs", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            if (_items == null || _items.Length == 0)
            {
                EditorGUILayout.LabelField("　CostumeItem がありません。",
                    EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            // 同名パラメータが複数ある場合のインデックス付け用カウンタ
            var paramNameCount = new Dictionary<string, int>();
            var paramNameSeen  = new Dictionary<string, int>();
            foreach (var it in _items)
            {
                if (it == null) continue;
                var pn = string.IsNullOrEmpty(it.parameterName) ? "(未設定)" : it.parameterName;
                paramNameCount[pn] = paramNameCount.TryGetValue(pn, out int c) ? c + 1 : 1;
            }

            CostumeItem toDelete = null;

            foreach (var item in _items)
            {
                if (item == null) continue;

                bool isSelected = _expandedItem == item;

                var rowStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fixedHeight = 0,
                    wordWrap = true,
                    richText = true,
                    fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal,
                    padding = new RectOffset(7, 7, 5, 5),
                    margin = new RectOffset(0, 0, 0, 4),
                };

                if (isSelected)
                {
                    rowStyle.normal.textColor = Color.white;
                    rowStyle.hover.textColor = Color.white;
                }

                var pnKey = string.IsNullOrEmpty(item.parameterName) ? "(未設定)" : item.parameterName;
                paramNameSeen[pnKey] = paramNameSeen.TryGetValue(pnKey, out int seen) ? seen + 1 : 1;
                var paramLabel = paramNameCount[pnKey] > 1
                    ? $"{pnKey} [{paramNameSeen[pnKey]}]"
                    : pnKey;

                var typeLabel = item.target == RuleTarget.GameObject ? "GameObject" : "BlendShape";
                var cond = AnimatorGenerator.GetEffectiveCondition(item);
                var summary = cond != null ? ConditionToStringPlain(cond) : "常に動作";
                var buttonLabel = isSelected
                    ? $"<size=13><b>{paramLabel}</b></size>\n{typeLabel}\n{summary}"
                    : $"<size=13><b>{paramLabel}</b></size>\n{typeLabel}\n{summary}";
                const float tabHeight = 54f;

                EditorGUILayout.BeginHorizontal();

                var accentColor = isSelected
                    ? new Color(0.24f, 0.55f, 0.95f, 1f)
                    : new Color(0.28f, 0.28f, 0.28f, 1f);
                var previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = accentColor;
                GUILayout.Box(GUIContent.none, GUILayout.Width(4), GUILayout.Height(tabHeight));
                GUI.backgroundColor = previousBackground;

                previousBackground = GUI.backgroundColor;
                if (isSelected)
                    GUI.backgroundColor = new Color(0.28f, 0.48f, 0.78f, 1f);
                else
                    GUI.backgroundColor = new Color(0.85f, 0.85f, 0.85f, 1f);

                if (GUILayout.Button(buttonLabel, rowStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(tabHeight)))
                {
                    _expandedItem = item;
                    _expandedSO = null;
                    _conditionGroups = null;
                    _conditionGroupsOwner = null;
                    Selection.activeGameObject = item.gameObject;
                    EditorGUIUtility.PingObject(item.gameObject);
                }

                GUI.backgroundColor = previousBackground;

                if (GUILayout.Button("×", GUILayout.Width(22)))
                    toDelete = item;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // 削除実行（ループ外）
            if (toDelete != null)
            {
                if (EditorUtility.DisplayDialog("削除確認",
                    $"CostumeItem「{toDelete.name}」を削除しますか？\n（GameObject ごと削除されます）",
                    "削除", "キャンセル"))
                {
                    Undo.DestroyObjectImmediate(toDelete.gameObject);
                    if (_expandedItem == toDelete) { _expandedItem = null; _expandedSO = null; _conditionGroups = null; _conditionGroupsOwner = null; }
                    RefreshItems();
                }
            }
        }

        private void DrawDetailPane()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_expandedItem == null)
            {
                EditorGUILayout.LabelField("設定", EditorStyles.boldLabel);
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("左の一覧から CostumeItem を選択してください。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"設定: {_expandedItem.gameObject.name}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("選択", GUILayout.Width(56)))
            {
                Selection.activeGameObject = _expandedItem.gameObject;
                EditorGUIUtility.PingObject(_expandedItem.gameObject);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            DrawExpandedItemEditor(_expandedItem);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        // ─── 新規作成フォーム ───

        private void DrawCreateForm()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _showCreateForm = EditorGUILayout.Foldout(_showCreateForm, "新規作成", true);

            if (_showCreateForm)
            {
                EditorGUILayout.Space(2);

                var sel = Selection.gameObjects;
                int targetCount = 0;
                var selectedNames = new List<string>();
                if (sel != null)
                {
                    foreach (var go in sel)
                    {
                        if (go != null && IsDescendantOf(go.transform, _selectedAvatar.transform))
                        {
                            targetCount++;
                            selectedNames.Add(go.name);
                        }
                    }
                }

                EditorGUILayout.LabelField("選択中の対象", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinHeight(Mathf.Max(38f, selectedNames.Count * 18f)));
                if (selectedNames.Count > 0)
                {
                    foreach (var selectedName in selectedNames)
                        EditorGUILayout.LabelField("- " + selectedName, EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField("(選択なし)", EditorStyles.centeredGreyMiniLabel);
                }
                EditorGUILayout.EndVertical();

                _newItemName = EditorGUILayout.TextField(
                    new GUIContent("メニュー名", "空欄なら選択オブジェクトの名前を使用します。"),
                    _newItemName);

                _newCreateEntry = DrawFolderPickerValue("メニュー階層", _newCreateEntry, "create");

                EditorGUILayout.Space(4);

                // バリデーション
                string error = null;
                if (targetCount == 0)
                    error = "Hierarchy でアバター配下のオブジェクトを選択してください。";

                if (!string.IsNullOrEmpty(error))
                    EditorGUILayout.HelpBox(error, MessageType.Info);

                using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(error)))
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("作成", GUILayout.Width(90)))
                    {
                        CreateItem(sel, targetCount);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void CreateItem(GameObject[] selection, int targetCount)
        {
            if (_selectedAvatar == null || targetCount == 0) return;

            // アバター配下のオブジェクトだけフィルタ
            var targets = new List<GameObject>();
            foreach (var sel in selection)
            {
                if (sel != null && IsDescendantOf(sel.transform, _selectedAvatar.transform))
                    targets.Add(sel);
            }

            var itemName = string.IsNullOrWhiteSpace(_newItemName)
                ? GuessName(targets)
                : _newItemName.Trim();

            var parent = EnsureParent(_selectedAvatar.transform);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create LACE Costume Item");

            var go = new GameObject(itemName);
            Undo.RegisterCreatedObjectUndo(go, "Create LACE Costume Item");
            go.transform.SetParent(parent, false);

            var item = Undo.AddComponent<CostumeItem>(go);
            item.parameterName = go.name;
            item.menuFolder = _newCreateEntry.Folder;
            item.installTargetMenu = _newCreateEntry.ExistingMenu;

            // 制御対象
            item.target = RuleTarget.GameObject;
            item.targetObjects = targets;

            Undo.CollapseUndoOperations(group);

            // リセット
            _newItemName = "";
            _newCreateEntry = default;
            RefreshItems();

            EditorGUIUtility.PingObject(go);
        }

        // ─── 展開エリア: 全設定エディタ ───

        private void DrawExpandedItemEditor(CostumeItem item)
        {
            EnsureExpandedSO();
            if (_expandedSO == null) return;

            _expandedSO.Update();

            EditorGUILayout.Space(4);
            EditorGUI.indentLevel++;

            DrawSection_Menu();
            DrawSection_Target(item);

            // ApplyModifiedProperties before condition section because
            // condition editing uses Undo.RecordObject + direct field write.
            _expandedSO.ApplyModifiedProperties();

            DrawSection_Condition(item);
            _expandedSO.ApplyModifiedProperties();

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(2);
        }

        // ─ セクション1: メニュー設定 ─

        private void DrawSection_Menu()
        {
            EditorGUILayout.LabelField("1. メニュー設定", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(_sp_parameterName, new GUIContent("パラメータ名"));
            EditorGUILayout.PropertyField(_sp_generateMenuItem, new GUIContent("メニュー生成"));

            if (_sp_generateMenuItem.boolValue)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("パラメータ");
                _sp_defaultValue.boolValue    = GUILayout.Toggle(_sp_defaultValue.boolValue,    "初期ON", "Button", GUILayout.Width(56));
                _sp_parameterSynced.boolValue = GUILayout.Toggle(_sp_parameterSynced.boolValue, "同期",   "Button", GUILayout.Width(40));
                _sp_parameterSaved.boolValue  = GUILayout.Toggle(_sp_parameterSaved.boolValue,  "保持",   "Button", GUILayout.Width(40));
                EditorGUILayout.EndHorizontal();

                DrawFolderPicker("メニュー階層", _sp_menuFolder, _sp_installTargetMenu, "detail");
                EditorGUILayout.PropertyField(_sp_icon, new GUIContent("アイコン"));
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        // ─ セクション2: 制御対象 ─

        private void DrawSection_Target(CostumeItem item)
        {
            EditorGUILayout.LabelField("2. 制御対象", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);
            int newTargetIdx = GUILayout.Toolbar(
                _sp_target.enumValueIndex,
                new[] { "GameObject", "BlendShape" });
            if (newTargetIdx != _sp_target.enumValueIndex)
                _sp_target.enumValueIndex = newTargetIdx;
            EditorGUILayout.EndHorizontal();

            DrawTargetObjectsList();

            if ((RuleTarget)_sp_target.enumValueIndex == RuleTarget.BlendShape)
                DrawBlendShapeSelectorInline(item);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        private void DrawTargetObjectsList()
        {
            int count = _sp_targetObjects.arraySize;
            for (int i = 0; i < count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var elem = _sp_targetObjects.GetArrayElementAtIndex(i);
                EditorGUILayout.PropertyField(elem, GUIContent.none);
                if (GUILayout.Button("×", GUILayout.Width(22)))
                {
                    _sp_targetObjects.DeleteArrayElementAtIndex(i);
                    // ObjectReference 削除は null 化→再削除が必要な場合がある
                    if (_sp_targetObjects.arraySize == count)
                        _sp_targetObjects.DeleteArrayElementAtIndex(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ オブジェクト追加", GUILayout.Width(130)))
            {
                _sp_targetObjects.InsertArrayElementAtIndex(_sp_targetObjects.arraySize);
                _sp_targetObjects.GetArrayElementAtIndex(_sp_targetObjects.arraySize - 1)
                    .objectReferenceValue = null;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBlendShapeSelectorInline(CostumeItem item)
        {
            var renderers = item.GetTargetRenderers();
            if (renderers.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "オブジェクトに SkinnedMeshRenderer があるとシェイプキーを選択できます。",
                    MessageType.Info);
                return;
            }

            var allShapes = new HashSet<string>();
            foreach (var smr in renderers)
            {
                if (smr.sharedMesh == null) continue;
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                    allShapes.Add(smr.sharedMesh.GetBlendShapeName(i));
            }

            if (allShapes.Count == 0)
            {
                EditorGUILayout.HelpBox("シェイプキーがありません。", MessageType.Info);
                return;
            }

            int selectedCount = _sp_blendShapeNames.arraySize;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("シェイプキー");
            if (GUILayout.Button($"選択... ({selectedCount}/{allShapes.Count})", GUILayout.MinWidth(100)))
                BlendShapePickerWindow.Show(_expandedSO, _sp_blendShapeNames, renderers);
            EditorGUILayout.EndHorizontal();

            if (selectedCount > 0)
            {
                var names = new List<string>();
                for (int i = 0; i < selectedCount; i++)
                    names.Add(_sp_blendShapeNames.GetArrayElementAtIndex(i).stringValue);
                EditorGUILayout.LabelField(string.Join(", ", names),
                    EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("値の設定", EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("条件に合致するとき");
            _sp_matchValue.floatValue = EditorGUILayout.Slider(_sp_matchValue.floatValue, 0f, 100f);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("条件に合致しないとき");
            _sp_unmatchValue.floatValue = EditorGUILayout.Slider(_sp_unmatchValue.floatValue, 0f, 100f);
            EditorGUILayout.EndHorizontal();
        }

        // ─ セクション3: 条件（自然言語ビルダー） ─

        private void DrawSection_Condition(CostumeItem item)
        {
            EditorGUILayout.LabelField("3. 切り替え条件", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            // 条件サマリー（自然な日本語・リッチテキスト）
            var effectiveCond = AnimatorGenerator.GetEffectiveCondition(item);
            if (effectiveCond == null)
            {
                EditorGUILayout.LabelField("このアイテムは常に動作します（条件なし）", EditorStyles.miniLabel);
            }
            else
            {
                var summary = ConditionToStringJapanese(effectiveCond);
                var trimmed = summary.TrimEnd();
                if (trimmed.EndsWith("の")) trimmed = trimmed.Substring(0, trimmed.Length - 1);
                var richStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { richText = true };
                EditorGUILayout.LabelField(trimmed, richStyle);
            }

            EditorGUILayout.Space(4);

            if (item.generateMenuItem && !string.IsNullOrEmpty(item.parameterName))
            {
                EditorGUILayout.LabelField("固定条件", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.Popup(1, new[] { "(選択...)", item.parameterName }, GUILayout.Width(130));
                    GUILayout.Label("が", GUILayout.Width(14));
                    GUILayout.Toolbar(0, new[] { "ON", "OFF" }, GUILayout.Width(80));
                    GUILayout.Label("のとき", GUILayout.Width(40));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("この条件はこのアイテム自身の ON/OFF によって自動で決まります。",
                    EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.Space(4);
            }

            _showAdditionalConditions = EditorGUILayout.Foldout(
                _showAdditionalConditions,
                "追加条件",
                true);

            if (!_showAdditionalConditions)
            {
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
                return;
            }

            EditorGUILayout.LabelField("他のアイテムとの組み合わせ条件を設定します。",
                EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space(4);

            // 他アイテムのパラメータ名一覧を収集（ドロップダウン用）
            var otherParams = new List<string>();
            foreach (var other in _items)
            {
                if (other == null || other == item) continue;
                if (!other.generateMenuItem || string.IsNullOrEmpty(other.parameterName)) continue;
                if (other.parameterName == item.parameterName) continue;
                if (!otherParams.Contains(other.parameterName))
                    otherParams.Add(other.parameterName);
            }
            otherParams.Sort();

            // DNF グループリストをキャッシュ（展開アイテムが変わった時だけ再構築）
            if (_conditionGroups == null || _conditionGroupsOwner != item)
            {
                _conditionGroups = DecomposeToDNF(item.condition);
                _conditionGroupsOwner = item;
            }
            var groups = _conditionGroups;
            bool changed = false;

            for (int gi = 0; gi < groups.Count; gi++)
            {
                // グループ間セパレータ
                if (gi > 0)
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(EditorGUI.indentLevel * 15f);
                    EditorGUILayout.LabelField("── いずれかを満たす ──",
                        EditorStyles.centeredGreyMiniLabel);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(2);
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                var group = groups[gi];

                EditorGUILayout.LabelField("すべて満たす", EditorStyles.miniBoldLabel);
                EditorGUILayout.Space(2);

                // グループ内の各条件行を自然言語で表示
                for (int ci = 0; ci < group.Count; ci++)
                {
                    if (ci > 0)
                    {
                        EditorGUILayout.LabelField("── かつ ──",
                            EditorStyles.centeredGreyMiniLabel);
                    }

                    var clause = group[ci];

                    EditorGUILayout.BeginHorizontal();

                    // パラメータ選択ドロップダウン
                    var dropdownItems = new List<string> { "(条件を選択)" };
                    dropdownItems.AddRange(otherParams);
                    // 未登録パラメータの場合も表示
                    if (!string.IsNullOrEmpty(clause.parameterName)
                        && !otherParams.Contains(clause.parameterName))
                        dropdownItems.Add(clause.parameterName);

                    int selIdx = 0;
                    for (int di = 1; di < dropdownItems.Count; di++)
                    {
                        if (dropdownItems[di] == clause.parameterName) { selIdx = di; break; }
                    }

                    GUILayout.Label("  ", GUILayout.Width(8));
                    int newSelIdx = EditorGUILayout.Popup(selIdx, dropdownItems.ToArray(),
                        GUILayout.Width(130));
                    if (newSelIdx != selIdx && newSelIdx > 0)
                    {
                        clause.parameterName = dropdownItems[newSelIdx];
                        changed = true;
                    }

                    // 「が」
                    GUILayout.Label("が", GUILayout.Width(14));

                    // ON/OFF トグル
                    int valIdx = clause.expectedValue ? 0 : 1;
                    int newValIdx = GUILayout.Toolbar(valIdx, new[] { "ON", "OFF" },
                        GUILayout.Width(80));
                    if (newValIdx != valIdx)
                    {
                        clause.expectedValue = (newValIdx == 0);
                        changed = true;
                    }

                    // 「のとき」（行末ラベル）
                    //GUILayout.Label("のとき", GUILayout.Width(38));

                    // 削除ボタン（1件だけでも削除可能）
                    if (GUILayout.Button("×", GUILayout.Width(22)))
                    {
                        if (group.Count > 1)
                        {
                            group.RemoveAt(ci);
                        }
                        else if (groups.Count > 1)
                        {
                            groups.RemoveAt(gi);
                        }
                        else
                        {
                            group.Clear();
                        }
                        changed = true;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                // グループ内に条件を追加
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ パターンに条件を追加", EditorStyles.miniButton, GUILayout.Width(150)))
                {
                    group.Add(new CondClause { parameterName = "", expectedValue = true });
                    changed = true;
                }
                // グループ削除（複数グループ時のみ + グループ全体を消す）
                if (groups.Count > 1)
                {
                    if (GUILayout.Button("パターンを削除", EditorStyles.miniButton, GUILayout.Width(120)))
                    {
                        groups.RemoveAt(gi);
                        changed = true;
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();

                // changed で要素を消した場合ループを抜ける
                if (changed) break;
            }

            // 「または」グループ追加ボタン
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ 別パターンを追加", EditorStyles.miniButton, GUILayout.Width(130)))
            {
                groups.Add(new List<CondClause>
                {
                    new CondClause { parameterName = "", expectedValue = true }
                });
                changed = true;
            }
            EditorGUILayout.EndHorizontal();

            // 変更があれば Condition に書き戻し（空の clause はスキップされるが UI には残る）
            if (changed)
            {
                Undo.RecordObject(item, "Edit LACE Condition");
                item.condition = RecomposeFromDNF(groups);
                EditorUtility.SetDirty(item);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        // ─── DNF 分解/再構成 ───

        /// <summary>条件1つ分の一時構造。</summary>
        private class CondClause
        {
            public string parameterName = "";
            public bool expectedValue = true;
        }

        /// <summary>
        /// Condition ツリーを DNF（OR of ANDs）に分解する。
        /// 結果は List of groups。各 group は List of CondClause（AND結合）。
        /// </summary>
        private static List<List<CondClause>> DecomposeToDNF(Condition cond)
        {
            var result = new List<List<CondClause>>();

            if (cond == null || IsEmptyCondition(cond))
            {
                // 空条件 → 空の1グループ（UI上 何もない状態から始められる）
                result.Add(new List<CondClause>());
                return result;
            }

            if (cond.type == ConditionType.Param)
            {
                // 単一 Param → 1グループ1条件
                result.Add(new List<CondClause>
                {
                    new CondClause { parameterName = cond.parameterName, expectedValue = cond.expectedValue }
                });
                return result;
            }

            if (cond.type == ConditionType.AND)
            {
                // AND(params...) → 1グループ複数条件
                var group = new List<CondClause>();
                if (cond.children != null)
                {
                    foreach (var child in cond.children)
                    {
                        if (child == null) continue;
                        if (child.type == ConditionType.Param)
                            group.Add(new CondClause { parameterName = child.parameterName, expectedValue = child.expectedValue });
                        else if (child.type == ConditionType.NOT && child.children != null && child.children.Count > 0
                            && child.children[0].type == ConditionType.Param)
                        {
                            // NOT(Param) → Param の反転
                            var inner = child.children[0];
                            group.Add(new CondClause { parameterName = inner.parameterName, expectedValue = !inner.expectedValue });
                        }
                    }
                }
                result.Add(group);
                return result;
            }

            if (cond.type == ConditionType.OR)
            {
                // OR(children...) → 各子を1グループとして分解
                if (cond.children != null)
                {
                    foreach (var child in cond.children)
                    {
                        if (child == null) continue;
                        // 各子を再帰的に分解して最初のグループだけ取る
                        var sub = DecomposeToDNF(child);
                        if (sub.Count > 0)
                            result.Add(sub[0]);
                    }
                }
                if (result.Count == 0)
                    result.Add(new List<CondClause>());
                return result;
            }

            // NOT 単体など → そのまま1条件として扱う（フォールバック）
            if (cond.type == ConditionType.NOT && cond.children != null && cond.children.Count > 0
                && cond.children[0].type == ConditionType.Param)
            {
                var inner = cond.children[0];
                result.Add(new List<CondClause>
                {
                    new CondClause { parameterName = inner.parameterName, expectedValue = !inner.expectedValue }
                });
                return result;
            }

            result.Add(new List<CondClause>());
            return result;
        }

        /// <summary>
        /// DNF グループリストから Condition ツリーを再構築する。
        /// </summary>
        private static Condition RecomposeFromDNF(List<List<CondClause>> groups)
        {
            // 有効な条件を持つグループだけ抽出
            var validGroups = new List<Condition>();
            foreach (var group in groups)
            {
                var validClauses = new List<Condition>();
                foreach (var clause in group)
                {
                    if (!string.IsNullOrEmpty(clause.parameterName))
                        validClauses.Add(Condition.Param(clause.parameterName, clause.expectedValue));
                }

                if (validClauses.Count == 0) continue;
                if (validClauses.Count == 1)
                    validGroups.Add(validClauses[0]);
                else
                    validGroups.Add(Condition.And(validClauses.ToArray()));
            }

            if (validGroups.Count == 0)
                return new Condition(); // 空

            if (validGroups.Count == 1)
                return validGroups[0];

            return Condition.Or(validGroups.ToArray());
        }

        private static bool IsEmptyCondition(Condition cond)
        {
            if (cond == null) return true;
            if (cond.type == ConditionType.Param && string.IsNullOrEmpty(cond.parameterName))
                return true;
            return false;
        }

        // ─── ヘルパー ───

        private static string GuessName(List<GameObject> targets)
        {
            if (targets == null || targets.Count == 0) return "LACE Item";
            foreach (var t in targets)
            {
                if (t != null) return t.name;
            }
            return "LACE Item";
        }

        private static bool IsDescendantOf(Transform child, Transform parent)
        {
            if (child == null || parent == null) return false;
            var t = child;
            while (t != null)
            {
                if (t == parent) return true;
                t = t.parent;
            }
            return false;
        }

        private static Transform EnsureParent(Transform avatarRoot)
        {
            if (avatarRoot == null) return null;

            for (int i = 0; i < avatarRoot.childCount; i++)
            {
                var c = avatarRoot.GetChild(i);
                if (c != null && c.name == ParentName)
                    return c;
            }

            var parentGo = new GameObject(ParentName);
            Undo.RegisterCreatedObjectUndo(parentGo, "Create LACE Parent");
            parentGo.transform.SetParent(avatarRoot, false);
            return parentGo.transform;
        }

        /// <summary>Condition ツリーを自然な日本語（リッチテキスト）に変換する。</summary>
        private static string ConditionToStringJapanese(Condition cond)
        {
            if (cond == null) return "?";

            switch (cond.type)
            {
                case ConditionType.Param:
                    if (string.IsNullOrEmpty(cond.parameterName)) return "（パラメータ未設定）";
                    return cond.expectedValue
                        ? $"<b>{cond.parameterName}がON</b>"
                        : $"<b>{cond.parameterName}がOFF</b>";

                case ConditionType.AND:
                    if (cond.children == null || cond.children.Count == 0) return "（条件なし）";
                    var andParts = new List<string>();
                    foreach (var child in cond.children)
                        andParts.Add(WrapJapanese(child, ConditionType.AND));
                    return string.Join(" かつ ", andParts);

                case ConditionType.OR:
                    if (cond.children == null || cond.children.Count == 0) return "（条件なし）";
                    var orParts = new List<string>();
                    foreach (var child in cond.children)
                        orParts.Add(WrapJapanese(child, ConditionType.OR));
                    return string.Join(" または ", orParts);

                case ConditionType.NOT:
                    if (cond.children == null || cond.children.Count == 0) return "（条件なし）";
                    var notChild = cond.children[0];
                    if (notChild != null && notChild.type == ConditionType.Param
                        && !string.IsNullOrEmpty(notChild.parameterName))
                    {
                        return notChild.expectedValue
                            ? $"<b>{notChild.parameterName}がOFF</b>"
                            : $"<b>{notChild.parameterName}がON</b>";
                    }
                    return $"<b>{ConditionToStringJapanese(notChild)}ではない</b>";

                default:
                    return "?";
            }
        }

        private static string WrapJapanese(Condition child, ConditionType parentType)
        {
            var s = ConditionToStringJapanese(child);
            if (parentType == ConditionType.AND && child.type == ConditionType.OR)
                return $"( {s} )";
            return s;
        }

        /// <summary>条件式をプレーンテキストに変換（リッチテキストなし）。一覧行用。</summary>
        private static string ConditionToStringPlain(Condition cond)
        {
            if (cond == null) return "?";

            switch (cond.type)
            {
                case ConditionType.Param:
                    if (string.IsNullOrEmpty(cond.parameterName)) return "(未設定)";
                    return cond.expectedValue
                        ? $"{cond.parameterName}=ON"
                        : $"{cond.parameterName}=OFF";

                case ConditionType.AND:
                    if (cond.children == null || cond.children.Count == 0) return "(なし)";
                    var andParts = new List<string>();
                    foreach (var child in cond.children)
                        andParts.Add(WrapPlain(child, ConditionType.AND));
                    return string.Join(" & ", andParts);

                case ConditionType.OR:
                    if (cond.children == null || cond.children.Count == 0) return "(なし)";
                    var orParts = new List<string>();
                    foreach (var child in cond.children)
                        orParts.Add(WrapPlain(child, ConditionType.OR));
                    return string.Join(" | ", orParts);

                case ConditionType.NOT:
                    if (cond.children == null || cond.children.Count == 0) return "(なし)";
                    var nc = cond.children[0];
                    if (nc != null && nc.type == ConditionType.Param
                        && !string.IsNullOrEmpty(nc.parameterName))
                    {
                        return nc.expectedValue
                            ? $"{nc.parameterName}=OFF"
                            : $"{nc.parameterName}=ON";
                    }
                    return $"!({ConditionToStringPlain(nc)})";

                default:
                    return "?";
            }
        }

        private static string WrapPlain(Condition child, ConditionType parentType)
        {
            var s = ConditionToStringPlain(child);
            if (parentType == ConditionType.AND && child.type == ConditionType.OR)
                return $"({s})";
            return s;
        }

        // ─── フォルダピッカー ───

        private List<FolderEntry> GetFolderEntries()
        {
            if (_folderEntries != null) return _folderEntries;
            _folderEntries = new List<FolderEntry>();
            _folderEntries.Add(new FolderEntry { Path = "(ルート)" });
            if (_selectedAvatar != null)
            {
                // LACE フォルダ
                var folders = _selectedAvatar.GetComponentsInChildren<LaceMenuFolder>(true);
                foreach (var folder in folders)
                {
                    _folderEntries.Add(new FolderEntry
                    {
                        Path = "[LACE] " + BuildFolderPath(folder),
                        Folder = folder,
                    });
                }
                // 既存メニュー (VRCExpressionsMenu)
                if (_selectedAvatar.expressionsMenu != null)
                    CollectExistingMenuEntries(_selectedAvatar.expressionsMenu, "", _folderEntries);
            }
            return _folderEntries;
        }

        private string BuildFolderPath(LaceMenuFolder folder)
        {
            var segments = new List<string>();
            var current = folder.transform;
            while (current != null && current != _selectedAvatar.transform)
            {
                if (current.GetComponent<LaceMenuFolder>() != null)
                    segments.Add(current.name);
                current = current.parent;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static void CollectExistingMenuEntries(
            VRCExpressionsMenu menu, string parentPath, List<FolderEntry> entries, int depth = 0)
        {
            if (menu == null || depth > 10) return;
            foreach (var control in menu.controls)
            {
                if (control.type != VRCExpressionsMenu.Control.ControlType.SubMenu) continue;
                if (control.subMenu == null) continue;
                var path = string.IsNullOrEmpty(parentPath)
                    ? control.name
                    : $"{parentPath}/{control.name}";
                entries.Add(new FolderEntry
                {
                    Path = "[既存] " + path,
                    ExistingMenu = control.subMenu,
                });
                CollectExistingMenuEntries(control.subMenu, path, entries, depth + 1);
            }
        }

        /// <summary>SerializedProperty 版: 詳細パネル用</summary>
        private void DrawFolderPicker(
            string label, SerializedProperty folderProp, SerializedProperty installTargetProp, string pickerKey)
        {
            var entries = GetFolderEntries();
            var currentFolder = (LaceMenuFolder)folderProp.objectReferenceValue;
            var currentMenu   = (VRCExpressionsMenu)installTargetProp.objectReferenceValue;
            int selectedIndex = FindEntryIndex(entries, currentFolder, currentMenu);

            var labels = BuildFolderLabels(entries);

            EditorGUILayout.BeginHorizontal();
            int newIndex = EditorGUILayout.Popup(label, selectedIndex, labels);
            if (newIndex != selectedIndex)
            {
                var e = entries[newIndex];
                folderProp.objectReferenceValue       = e.Folder;
                installTargetProp.objectReferenceValue = e.ExistingMenu;
            }

            if (GUILayout.Button("+", GUILayout.Width(24)))
            {
                _newFolderInputKey = pickerKey;
                _newFolderName = "";
                _newFolderParentIndex = newIndex;
            }
            EditorGUILayout.EndHorizontal();

            if (_newFolderInputKey == pickerKey)
            {
                var created = DrawNewFolderInput();
                if (created != null)
                {
                    folderProp.objectReferenceValue        = created;
                    installTargetProp.objectReferenceValue = null;
                }
            }
        }

        /// <summary>値返却版: 新規作成フォーム用</summary>
        private FolderEntry DrawFolderPickerValue(string label, FolderEntry current, string pickerKey)
        {
            var entries = GetFolderEntries();
            int selectedIndex = FindEntryIndex(entries, current.Folder, current.ExistingMenu);
            var labels = BuildFolderLabels(entries);

            var result = current;

            EditorGUILayout.BeginHorizontal();
            int newIndex = EditorGUILayout.Popup(label, selectedIndex, labels);
            if (newIndex != selectedIndex)
                result = entries[newIndex];

            if (GUILayout.Button("+", GUILayout.Width(24)))
            {
                _newFolderInputKey = pickerKey;
                _newFolderName = "";
                _newFolderParentIndex = newIndex;
            }
            EditorGUILayout.EndHorizontal();

            if (_newFolderInputKey == pickerKey)
            {
                var created = DrawNewFolderInput();
                if (created != null)
                    result = new FolderEntry { Path = "[LACE] " + BuildFolderPath(created), Folder = created };
            }

            return result;
        }

        private static int FindEntryIndex(
            List<FolderEntry> entries, LaceMenuFolder folder, VRCExpressionsMenu existingMenu)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (folder != null && entries[i].Folder == folder) return i;
                if (existingMenu != null && entries[i].ExistingMenu == existingMenu) return i;
            }
            return 0;
        }

        private static string[] BuildFolderLabels(List<FolderEntry> entries)
        {
            var labels = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++) labels[i] = entries[i].Path;
            return labels;
        }

        /// <summary>
        /// インライン新規フォルダ作成UI。作成されたら LaceMenuFolder を返す。それ以外は null。
        /// </summary>
        private LaceMenuFolder DrawNewFolderInput()
        {
            LaceMenuFolder created = null;

            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _newFolderName = EditorGUILayout.TextField("フォルダ名", _newFolderName);

            var entries = GetFolderEntries();
            var parentLabels = BuildFolderLabels(entries);
            _newFolderParentIndex = EditorGUILayout.Popup("親フォルダ",
                Mathf.Clamp(_newFolderParentIndex, 0, entries.Count - 1), parentLabels);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newFolderName)))
            {
                if (GUILayout.Button("作成", GUILayout.Width(56)))
                {
                    var parentFolder = entries[Mathf.Clamp(_newFolderParentIndex, 0, entries.Count - 1)].Folder;
                    created = CreateMenuFolder(_newFolderName.Trim(), parentFolder);
                    _newFolderInputKey = null;
                    _folderEntries = null;
                }
            }

            if (GUILayout.Button("×", GUILayout.Width(24)))
                _newFolderInputKey = null;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;

            return created;
        }

        private LaceMenuFolder CreateMenuFolder(string folderName, LaceMenuFolder parentFolder)
        {
            Transform parent;
            if (parentFolder != null)
                parent = parentFolder.transform;
            else
                parent = EnsureParent(_selectedAvatar.transform);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create LACE Menu Folder");

            var go = new GameObject(folderName);
            Undo.RegisterCreatedObjectUndo(go, "Create LACE Menu Folder");
            go.transform.SetParent(parent, false);

            var folder = Undo.AddComponent<LaceMenuFolder>(go);

            Undo.CollapseUndoOperations(group);
            return folder;
        }
    }
}
