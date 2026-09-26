using UnityEditor;
using UnityEngine;

// Writes only inside the change check below, which fires when a person edits one of the three boxes.
[CustomPropertyDrawer(typeof(ShownCoordsAttribute))]
public class ShownCoordsDrawer : PropertyDrawer
{
    static readonly GUIContent[] Labels = { new GUIContent("X"), new GUIContent("Y"), new GUIContent("Z") };

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.Vector3)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        EditorGUI.BeginProperty(position, label, property);
        var shown = Coords.ToShown(property.vector3Value);
        float[] values = { shown.x, shown.y, shown.z };
        EditorGUI.BeginChangeCheck();
        EditorGUI.MultiFloatField(position, label, Labels, values);
        if (EditorGUI.EndChangeCheck())
            property.vector3Value = Coords.FromShown(new Vector3(values[0], values[1], values[2]));
        EditorGUI.EndProperty();
    }
}
