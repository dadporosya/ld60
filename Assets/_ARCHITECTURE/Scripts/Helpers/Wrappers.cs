using UnityEngine;
using System;
using System.Collections.Generic;using Object = UnityEngine.Object;

[Serializable]
public class ListOfStrWrapper
{
    [TextArea(3, 7)]
    public List<string> values = new List<string>();
}

[Serializable]
public class UniversalWrapper<T>
{
    public T value;
    public List<T> values = new List<T>();
}