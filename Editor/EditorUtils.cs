using UnityEditor;
using UnityEngine;

namespace UnityEventToolkit.Editor
{
    public static class EditorUtils
    {
        public static int RemoveBrokenListeners(UnityEventScanner.EventEntry entry)
        {
            if (entry == null || !entry.OwnerObject)
                return 0;

            using var serializedObject = new SerializedObject(entry.OwnerObject);
            serializedObject.Update();

            var eventProp = serializedObject.FindProperty(entry.FieldName);
            if (eventProp == null)
                return 0;

            var callsProp = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
            if (callsProp == null || !callsProp.isArray)
                return 0;

            var removedCount = 0;
            for (var i = callsProp.arraySize - 1; i >= 0; i--)
            {
                var callProp = callsProp.GetArrayElementAtIndex(i);
                var targetProp = callProp.FindPropertyRelative("m_Target");
                var methodProp = callProp.FindPropertyRelative("m_MethodName");

                var target = targetProp?.objectReferenceValue;
                var method = methodProp?.stringValue;

                if (!target || string.IsNullOrEmpty(method))
                {
                    callsProp.DeleteArrayElementAtIndex(i);
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(entry.OwnerObject);
                Debug.Log($"Removed {removedCount} broken listener(s) from: {entry.OwnerName}.{entry.FieldName}");
            }

            return removedCount;
        }
    }
}
