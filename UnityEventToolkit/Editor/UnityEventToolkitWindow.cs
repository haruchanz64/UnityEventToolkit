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

        private List<GameObject> _gameObjectsWithEvents = new();
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
            EditorApplication.update += OnUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorApplication.update -= OnUpdate;
        }

        private void OnHierarchyChanged() => _isDirty = true;

        private void OnUpdate()
        {
            if (!_isDirty) return;
            RefreshEventList();
            _isDirty = false;
        }

        private void OnSceneOpened(Scene scene, OpenSceneMode mode) => RefreshEventList();

        private void RefreshEventList()
        {
            _allEventEntries = UnityEventScanner.ScanAllUnityEvents();
            ApplyFilterAndSearch();

            _gameObjectsWithEvents.Clear();
            var uniqueGameObjects = new HashSet<GameObject>(); // Use a HashSet to avoid duplicates

            // Collect all GameObjects with events and their parents
            foreach (var entry in _filteredEntries)
            {
                var go = entry.OwnerObject switch
                {
                    GameObject obj => obj,
                    Component comp => comp.gameObject,
                    _ => null
                };

                if (!go) continue;

                // Add the GameObject and all its parents
                var t = go.transform;
                while (t)
                {
                    uniqueGameObjects.Add(t.gameObject); // Add to HashSet
                    t = t.parent;
                }
            }

            // Now convert the HashSet to a List for _gameObjectsWithEvents
            _gameObjectsWithEvents = uniqueGameObjects.ToList();
            
            // Remove states that no longer apply
            _expandedStates.Keys.Where(go => !_gameObjectsWithEvents.Contains(go)).ToList()
                .ForEach(go => _expandedStates.Remove(go));

            _selectedGameObjects.RemoveWhere(go => !_gameObjectsWithEvents.Contains(go));
            if (_lastClickedGameObject && !_gameObjectsWithEvents.Contains(_lastClickedGameObject))
                _lastClickedGameObject = null;

            // Auto-expand parents if searching
            if (!string.IsNullOrEmpty(_searchQuery))
            {
                _gameObjectsWithEvents
                    .Where(go => go.name.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                    // Corrected line: Transform the IEnumerable<Transform> from GetParents
                    // into an IEnumerable<GameObject> before SelectMany flattens it.
                    .SelectMany(go => GetParents(go.transform).Select(parentTransform => parentTransform.gameObject))
                    .ToList()
                    .ForEach(parent => _expandedStates[parent] = true); // 'parent' is now definitely a GameObject
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
                ApplyFilterAndSearch();
                RefreshEventList();
            }

            GUILayout.Space(10);
            GUILayout.Label("Filter:", GUILayout.Width(40));
            var selectedIndex = EditorGUILayout.Popup(Array.IndexOf(_filterOptions, _selectedFilter), _filterOptions,
                GUILayout.Width(100));
            if (_filterOptions[selectedIndex] != _selectedFilter)
            {
                _selectedFilter = _filterOptions[selectedIndex];
                ApplyFilterAndSearch();
                RefreshEventList();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Clear Search", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                _searchQuery = "";
                ApplyFilterAndSearch();
                RefreshEventList();
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

            if (_gameObjectsWithEvents.Count == 0)
            {
                EditorGUILayout.LabelField("No GameObjects found.");
            }
            else
            {
                foreach (var root in GetRootGameObjects(_gameObjectsWithEvents))
                {
                    DrawGameObjectHierarchy(root, 0);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawGameObjectHierarchy(GameObject go, int indent)
        {
            // Add to flat list *here*, ensuring it's added only once per drawn item
            _flatGameObjectList.Add(go);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * 15);

            var hasChildrenInEvents = go.transform.Cast<Transform>()
                .Any(child => _gameObjectsWithEvents.Contains(child.gameObject));

            // Ensure the foldout state exists
            _expandedStates.TryAdd(go, true); // Default to expanded

            // If it has children that are part of the 'gameObjectsWithEvents' list, it should be foldable
            if (hasChildrenInEvents)
            {
                var foldRect = GUILayoutUtility.GetRect(new GUIContent(go.name), EditorStyles.foldout);
                _expandedStates[go] = EditorGUI.Foldout(foldRect, _expandedStates[go], GUIContent.none, false);

                var labelRect = foldRect;
                labelRect.x += 15;

                if (_selectedGameObjects.Contains(go))
                    EditorGUI.DrawRect(labelRect, new Color(0.24f, 0.48f, 0.90f, 0.3f));

                if (GUI.Button(labelRect, go.name, EditorStyles.label))
                    HandleSelection(go, Event.current);

                EditorGUILayout.EndHorizontal();

                // Only draw children if expanded
                if (!_expandedStates[go]) return;
                var children = go.transform.Cast<Transform>()
                    .Where(child => _gameObjectsWithEvents.Contains(child.gameObject))
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
                _expandedStates[go] = expanded;
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


        private static List<GameObject> GetRootGameObjects(List<GameObject> gameObjects)
        {
            var roots = gameObjects.Where(go => !go.transform.parent || !gameObjects.Contains(go.transform.parent.gameObject)).ToList();

            // Sort the root GameObjects by their sibling index
            roots.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

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
                    EditorGUILayout.LabelField(selected.name, EditorStyles.boldLabel);
                    var events = _filteredEntries.FindAll(e =>
                        e.OwnerObject == selected ||
                        (e.OwnerObject is Component comp && comp.gameObject == selected));

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

        private static void DrawEventEntry(UnityEventScanner.EventEntry entry)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"Event: {entry.FieldName}", EditorStyles.boldLabel);

            EditorGUILayout.Space(5);

            var so = new SerializedObject(entry.OwnerObject);
            var eventProp = so.FindProperty(entry.FieldName);
            if (eventProp != null)
            {
                EditorGUILayout.PropertyField(eventProp, true);
                so.ApplyModifiedProperties();
            }

            EditorGUILayout.Space(5);
            DrawListeners(entry);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8);
        }

        private static void DrawListeners(UnityEventScanner.EventEntry entry)
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

            if (GUILayout.Button("Remove Broken Listeners", GUILayout.Width(200)))
                EditorUtils.RemoveBrokenListeners(entry);
        }
    }
}