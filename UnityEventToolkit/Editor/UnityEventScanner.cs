using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace UnityEventToolkit.Editor
{
    public static class UnityEventScanner
    {
        public class ListenerInfo
        {
            public Object Target;
            public string MethodName;
            public bool IsBroken => !Target || string.IsNullOrEmpty(MethodName);
        }

        public class EventEntry
        {
            public Object OwnerObject;
            public string OwnerName;
            public Component Component;
            public string FieldName;
            public UnityEventBase EventInstance;
            public readonly List<ListenerInfo> Listeners = new();
        }

        public static List<EventEntry> ScanAllUnityEvents(bool includeScriptableObjects = false)
        {
            var events = new List<EventEntry>();

            // Scene GameObjects (including inactive ones)
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!go || !go.scene.IsValid() || EditorUtility.IsPersistent(go))
                    continue;

                foreach (var component in go.GetComponents<MonoBehaviour>())
                {
                    if (!component) continue;
                    ScanFields(component, component, go.name, events);
                }
            }

            if (includeScriptableObjects)
            {
                var guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (!so) continue;
                    ScanFields(so, null, so.name, events);
                }
            }

            return events;
        }

        private static void ScanFields(Object owner, Component component, string ownerName, List<EventEntry> events)
        {
            var seenFieldNames = new HashSet<string>();

            for (var type = owner.GetType();
                 type != null && type != typeof(MonoBehaviour) && type != typeof(ScriptableObject) && type != typeof(Object);
                 type = type.BaseType)
            {
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var field in fields)
                {
                    if (!IsSerializedField(field)) continue;
                    if (!typeof(UnityEventBase).IsAssignableFrom(field.FieldType)) continue;
                    if (!seenFieldNames.Add(field.Name)) continue;

                    if (field.GetValue(owner) is not UnityEventBase evt) continue;

                    var entry = new EventEntry
                    {
                        OwnerObject = owner,
                        OwnerName = ownerName,
                        Component = component,
                        FieldName = field.Name,
                        EventInstance = evt
                    };

                    for (var i = 0; i < evt.GetPersistentEventCount(); i++)
                    {
                        entry.Listeners.Add(new ListenerInfo
                        {
                            Target = evt.GetPersistentTarget(i),
                            MethodName = evt.GetPersistentMethodName(i)
                        });
                    }

                    events.Add(entry);
                }
            }
        }

        private static bool IsSerializedField(FieldInfo field)
        {
            if (field.IsDefined(typeof(NonSerializedAttribute), true))
                return false;

            return field.IsPublic || field.IsDefined(typeof(SerializeField), true);
        }
    }
}
