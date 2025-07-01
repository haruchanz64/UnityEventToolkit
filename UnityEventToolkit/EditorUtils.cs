using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace UnityEventToolkit
{
    public static class EditorUtils
    {
        public static void RemoveBrokenListeners(UnityEventScanner.EventEntry entry)
        {
            var evt = entry.EventInstance;

            for (var i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                var target = evt.GetPersistentTarget(i);
                var method = evt.GetPersistentMethodName(i);

                if (!target || string.IsNullOrEmpty(method))
                {
                    evt.SetPersistentListenerState(i, UnityEventCallState.Off);
                }
            }

            EditorUtility.SetDirty(entry.OwnerObject);
            Debug.Log($"Cleaned up broken listeners for: {entry.OwnerName}.{entry.FieldName}");
        }
    }
}