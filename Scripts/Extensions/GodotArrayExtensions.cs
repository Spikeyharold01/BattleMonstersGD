using System;
using Godot.Collections;

public static class GodotArrayExtensions
{
    public static T Find<T>(this Array<T> source, Predicate<T> match)
    {
        if (source == null || match == null)
        {
            return default;
        }

        foreach (T item in source)
        {
            if (match(item))
            {
                return item;
            }
        }

        return default;
    }

    public static bool Exists<T>(this Array<T> source, Predicate<T> match)
    {
        if (source == null || match == null)
        {
            return false;
        }

        foreach (T item in source)
        {
            if (match(item))
            {
                return true;
            }
        }

        return false;
    }
}
