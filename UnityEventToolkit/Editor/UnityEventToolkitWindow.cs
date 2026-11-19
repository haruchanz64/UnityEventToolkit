using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEventToolkit.Editor
{
    public class UnityEventToolkitWindow : EditorWindow
    {
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;

        private string _searchQuery = "";
        private readonly string[] _filterOptions = { "All", "Valid", "Broken" };
        private string _selectedFilter = "All";

        private List<UnityEventScanner.EventEntry> _allEventEntries = new();
        private List<UnityEventScanner.EventEntry> _filteredEntries = new();

        private readonly HashSet<GameObject> _gameObjectsWithEvents = new();
        private readonly HashSet<GameObject> _selectedGameObjects = new();

        private readonly Dictionary<GameObject, bool> _expandedStates = new();

        private bool _isDirty;

        private GameObject _lastClickedGameObject;
        private readonly List<GameObject> _flatGameObjectList = new();

        [MenuItem("Tools/Unity Event Toolkit")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnityEventToolkitWindow>("Unity Event Toolkit");
            window.minSize = new Vector2(800, 400);
        }

        private void OnEnable()
        {
            RefreshEventList();
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorApplication.update += OnUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            EditorApplication.update -= OnUpdate;
        }

        private void OnHierarchyChanged() => _isDirty = true;

        private void OnUndoRedoPerformed() => _isDirty = true;

        private void OnUpdate()
        {
            if (!_isDirty) return;
            _isDirty = false;
            RefreshEventList();
            Repaint();
        }

        private void OnSceneOpened(Scene scene, OpenSceneMode mode) => RefreshEventList();

        private void OnSceneClosed(Scene scene) => RefreshEventList();

        private void RefreshEventList()
        {
            _allEventEntries = UnityEventScanner.ScanAllUnityEvents();
            RebuildFilteredView();
        }

        private void RebuildFilteredView()
        {
            ApplyFilterAndSearch();

            _gameObjectsWithEvents.Clear();
            var matchedGameObjects = new HashSet<GameObject>();

            // Collect all matched GameObjects and their ancestor chain
            foreach (var entry in _filteredEntries)
            {
                var go = GetEntryGameObject(entry);
                if (!go) continue;

                matchedGameObjects.Add(go);

                var t = go.transform;
                while (t)
                {
                    _gameObjectsWithEvents.Add(t.gameObject);
                    t = t.parent;
                }
            }

            // Remove states that no longer apply or reference destroyed objects
            var staleKeys = _expandedStates.Keys.Where(go => !go || !_gameObjectsWithEvents.Contains(go)).ToList();
            foreach (var key in staleKeys)
                _expandedStates.Remove(key);

            _selectedGameObjects.RemoveWhere(go => !go || !_gameObjectsWithEvents.Contains(go));
            if (!_lastClickedGameObject || !_gameObjectsWithEvents.Contains(_lastClickedGameObject))
                _lastClickedGameObject = null;

            // Auto-expand parents of all matched GameObjects when searching (by GameObject name or event field name)
            if (!string.IsNullOrEmpty(_searchQuery))
            {
                foreach (var go in matchedGameObjects)
                {
                    foreach (var parentTransform in GetParents(go.transform))
                        _expandedStates[parentTransform.gameObject] = true;
                }
            }

            return;

            static IEnumerable<Transform> GetParents(Transform transform)
            {
                var parent = transform.parent;
                while (parent)
                {
                    yield return parent;
                    parent = parent.parent;
                }
            }
        }

        private static GameObject GetEntryGameObject(UnityEventScanner.EventEntry entry)
        {
            if (entry == null || !entry.OwnerObject)
                return null;

            return entry.OwnerObject switch
            {
                GameObject obj => obj,
                Component comp => comp.gameObject,
                _ => null
            };
        }

        private void ApplyFilterAndSearch()
        {
            _filteredEntries = string.IsNullOrEmpty(_searchQuery)
                ? new List<UnityEventScanner.EventEntry>(_allEventEntries)
                : _allEventEntries.FindAll(e =>
                    e.OwnerName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.FieldName.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0);

            switch (_selectedFilter)
            {
                case "Valid":
                    _filteredEntries.RemoveAll(e => e.Listeners.Exists(l => l.IsBroken));
                    break;
                case "Broken":
                    _filteredEntries.RemoveAll(e => !e.Listeners.Exists(l => l.IsBroken));
                    break;
            }
        }

        private void OnGUI()
        {
            DrawHeader();
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
                RefreshEventList();

            GUILayout.Space(10);
            GUILayout.Label("Search:", GUILayout.Width(50));
            var newQuery = GUILayout.TextField(_searchQuery, GUILayout.Width(200));
            if (newQuery != _searchQuery)
            {
                _searchQuery = newQuery;
                RebuildFilteredView();
            }

            GUILayout.Space(10);
            GUILayout.Label("Filter:", GUILayout.Width(40));
            var selectedIndex = EditorGUILayout.Popup(Array.IndexOf(_filterOptions, _selectedFilter), _filterOptions,
                GUILayout.Width(100));
            if (selectedIndex >= 0 && _filterOptions[selectedIndex] != _selectedFilter)
            {
                _selectedFilter = _filterOptions[selectedIndex];
                RebuildFilteredView();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Clear Search", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                _searchQuery = "";
                RebuildFilteredView();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical("box", GUILayout.Width(300));
            GUILayout.Label("GameObjects With Events", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Expand All", GUILayout.Width(100)))
                SetAllExpanded(true);
            if (GUILayout.Button("Collapse All", GUILayout.Width(100)))
                SetAllExpanded(false);
            EditorGUILayout.EndHorizontal();

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

            // Clear and rebuild flatGameObjectList each frame before drawing
            _flatGameObjectList.Clear();

            var roots = GetRootGameObjects(_gameObjectsWithEvents);
            if (roots.Count == 0)
            {
                EditorGUILayout.LabelField("No GameObjects found.");
            }
            else
            {
                foreach (var root in roots)
                {
                    DrawGameObjectHierarchy(root, 0);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawGameObjectHierarchy(GameObject go, int indent)
        {
            if (!go) return;

            // Add to flat list *here*, ensuring it's added only once per drawn item
            _flatGameObjectList.Add(go);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * 15);

            var hasChildrenInEvents = go.transform.Cast<Transform>()
                .Any(child => child && _gameObjectsWithEvents.Contains(child.gameObject));

            // Ensure the foldout state exists
            _expandedStates.TryAdd(go, true); // Default to expanded

            // If it has children that are part of the 'gameObjectsWithEvents' list, it should be foldable
            if (hasChildrenInEvents)
            {
                var rowRect = GUILayoutUtility.GetRect(new GUIContent(go.name), EditorStyles.foldout);
                const float foldoutWidth = 15f;
                var foldRect = new Rect(rowRect.x, rowRect.y, foldoutWidth, rowRect.height);
                var labelRect = new Rect(rowRect.x + foldoutWidth, rowRect.y, Mathf.Max(0f, rowRect.width - foldoutWidth), rowRect.height);

                _expandedStates[go] = EditorGUI.Foldout(foldRect, _expandedStates[go], GUIContent.none, false);

                if (_selectedGameObjects.Contains(go))
                    EditorGUI.DrawRect(labelRect, new Color(0.24f, 0.48f, 0.90f, 0.3f));

                if (GUI.Button(labelRect, go.name, EditorStyles.label))
                    HandleSelection(go, Event.current);

                EditorGUILayout.EndHorizontal();

                // Only draw children if expanded
                if (!_expandedStates[go]) return;
                var children = go.transform.Cast<Transform>()
                    .Where(child => child && _gameObjectsWithEvents.Contains(child.gameObject))
                    .ToList();
                children.Sort((a, b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()));
                foreach (var child in children)
                {
                    DrawGameObjectHierarchy(child.gameObject, indent + 1);
                }
            }
            else // No children in the event list, so just draw as a label
            {
                var rect = GUILayoutUtility.GetRect(new GUIContent(go.name), EditorStyles.label);
                if (_selectedGameObjects.Contains(go))
                    EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.90f, 0.3f));

                if (GUI.Button(rect, go.name, EditorStyles.label))
                    HandleSelection(go, Event.current);

                EditorGUILayout.EndHorizontal();
            }
        }

        private void SetAllExpanded(bool expanded)
        {
            foreach (var go in _gameObjectsWithEvents)
            {
                if (go)
                    _expandedStates[go] = expanded;
            }
        }

        private void HandleSelection(GameObject go, Event e)
        {
            if (e.shift && _lastClickedGameObject && _flatGameObjectList.Contains(_lastClickedGameObject))
            {
                var lastIndex = _flatGameObjectList.IndexOf(_lastClickedGameObject);
                var newIndex = _flatGameObjectList.IndexOf(go);

                if (lastIndex != -1 && newIndex != -1)
                {
                    var min = Math.Min(lastIndex, newIndex);
                    var max = Math.Max(lastIndex, newIndex);

                    for (var i = min; i <= max; i++)
                    {
                        _selectedGameObjects.Add(_flatGameObjectList[i]);
                        EditorGUIUtility.PingObject(_flatGameObjectList[i]);
                    }
                }
            }
            else if (e.control || e.command)
            {
                if (!_selectedGameObjects.Add(go))
                    _selectedGameObjects.Remove(go);

                _lastClickedGameObject = go;
                EditorGUIUtility.PingObject(go);
            }
            else
            {
                _selectedGameObjects.Clear();
                _selectedGameObjects.Add(go);
                _lastClickedGameObject = go;
                EditorGUIUtility.PingObject(go);
            }

            Repaint();
        }

        private static List<GameObject> GetRootGameObjects(HashSet<GameObject> gameObjects)
        {
            var roots = gameObjects
                .Where(go => go && (!go.transform.parent || !gameObjects.Contains(go.transform.parent.gameObject)))
                .ToList();

            // Sort deterministically by scene handle, then sibling index
            roots.Sort((a, b) =>
            {
                var sceneCompare = a.scene.handle.CompareTo(b.scene.handle);
                return sceneCompare != 0
                    ? sceneCompare
                    : a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
            });

            return roots;
        }

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("Events for Selected GameObject(s)", EditorStyles.boldLabel);

            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

            if (_selectedGameObjects.Count == 0)
            {
                EditorGUILayout.LabelField("Select GameObject(s) to view UnityEvents.", EditorStyles.helpBox);
            }
            else
            {
                foreach (var selected in _selectedGameObjects)
                {
                    if (!selected) continue;

                    EditorGUILayout.LabelField(selected.name, EditorStyles.boldLabel);
                    var events = _filteredEntries.FindAll(e => GetEntryGameObject(e) == selected);

                    if (events.Count == 0)
                    {
                        EditorGUILayout.LabelField("No events found.", EditorStyles.helpBox);
                    }
                    else
                    {
                        foreach (var entry in events)
                            DrawEventEntry(entry);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawEventEntry(UnityEventScanner.EventEntry entry)
        {
            if (entry == null || !entry.OwnerObject)
                return;

            EditorGUILayout.BeginVertical("box");

            var title = entry.Component
                ? $"Event: {entry.Component.GetType().Name}.{entry.FieldName}"
                : $"Event: {entry.FieldName}";
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            EditorGUILayout.Space(5);

            using (var so = new SerializedObject(entry.OwnerObject))
            {
                so.Update();
                var eventProp = so.FindProperty(entry.FieldName);
                if (eventProp != null)
                {
                    EditorGUILayout.PropertyField(eventProp, true);
                    if (so.ApplyModifiedProperties())
                        _isDirty = true;
                }
            }

            EditorGUILayout.Space(5);
            DrawListeners(entry);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8);
        }

        private void DrawListeners(UnityEventScanner.EventEntry entry)
        {
            if (entry.Listeners.Count == 0)
            {
                EditorGUILayout.HelpBox("No listeners attached.", MessageType.Info);
            }
            else
            {
                foreach (var listener in entry.Listeners)
                {
                    var label = $" - {(listener.Target ? listener.Target.name : "NULL")}::{listener.MethodName}";
                    if (listener.IsBroken)
                        EditorGUILayout.HelpBox("Broken Listener: " + label, MessageType.Warning);
                    else
                        EditorGUILayout.LabelField(label, EditorStyles.helpBox);
                }
            }

            var hasBroken = entry.Listeners.Exists(l => l.IsBroken);
            using (new EditorGUI.DisabledScope(!hasBroken))
            {
                if (GUILayout.Button("Remove Broken Listeners", GUILayout.Width(200)))
                {
                    if (EditorUtils.RemoveBrokenListeners(entry) > 0)
                    {
                        _isDirty = true;
                        Repaint();
                    }
                }
            }
        }
    }
}
