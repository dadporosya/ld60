using System;

/// <summary>
/// Marks a serialized string (or list of strings) field as text the player reads, so the Translations window's
/// scan (Tools > Localization) puts its value into the translation table. Works in components, ScriptableObjects
/// and the [Serializable] classes and lists inside them. The field itself still holds English: whatever shows it
/// passes it through <see cref="Loc.T"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class LocalizedAttribute : Attribute
{
}
