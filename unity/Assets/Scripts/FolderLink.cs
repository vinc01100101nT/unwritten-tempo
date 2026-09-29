using UnityEngine;

/// <summary>Marks a string field that holds Unity's own id for a folder, so renaming or moving the folder never
/// breaks it. The inspector draws it as a folder box (see the drawer in Editor); in a build it is only text.</summary>
public class FolderLinkAttribute : PropertyAttribute { }
