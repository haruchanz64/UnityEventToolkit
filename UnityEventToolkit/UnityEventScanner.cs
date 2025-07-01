using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace UnityEventToolkit
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

        public static List<EventEntry> ScanAllUnityEvents()
        {
            var events = new List<EventEntry>();

            // Scene GameObjects
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                foreach (var component in go.GetComponents<MonoBehaviour>())
                {
                    if (!component) continue;
                    ScanFields(component, component, go.name, events);
                }
            }
            
            // ScriptableObjects
            var guids = AssetDatabase.FindAssets("t:ScriptableObject");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (!so) continue;
                ScanFields(so, null, so.name, events);
            }

            return events;
        }

        private static void ScanFields(Object owner, Component component, string ownerName, List<EventEntry> events)
        {
            var fields = owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var field in fields)
            {
                if (!typeof(UnityEventBase).IsAssignableFrom(field.FieldType)) continue;

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
}
