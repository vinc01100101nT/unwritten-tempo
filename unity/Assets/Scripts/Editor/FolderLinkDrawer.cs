using UnityEditor;
using UnityEngine;

// Writes only inside the change check below, which fires when a person drops or picks a folder.
[CustomPropertyDrawer(typeof(FolderLinkAttribute))]
public class FolderLinkDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        EditorGUI.BeginProperty(position, label, property);
        string path = AssetDatabase.GUIDToAssetPath(property.stringValue);
        var folder = AssetDatabase.IsValidFolder(path) ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(path) : null;
        EditorGUI.BeginChangeCheck();
        var picked = EditorGUI.ObjectField(position, label, folder, typeof(DefaultAsset), false);
        if (EditorGUI.EndChangeCheck())
        {
            string pickedPath = picked != null ? AssetDatabase.GetAssetPath(picked) : "";
            if (picked == null || AssetDatabase.IsValidFolder(pickedPath))
                property.stringValue = picked != null ? AssetDatabase.AssetPathToGUID(pickedPath) : "";
        }
        EditorGUI.EndProperty();
    }
}
