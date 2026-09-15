using System;
using System.Collections.Generic;
using UnityEngine;

public enum FieldType
{
    Numeric,
    Dropdown
}

public struct Field
{
    public float name;
    public FieldType fieldType; 
    public Type dataType;
    public int height;
}

public class Window : MonoBehaviour
{
    public List<Field> fields;

    public Window(List<Field> fields)
    {
        this.fields = fields;
    }

    private void OnEnable()
    {

    }
}