using System.Reflection;

namespace Utilities;

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8602
#pragma warning disable CS8603

public static class ExtensionsHelper
{
    // The below code is from https://www.codeproject.com/Articles/80343/Accessing-private-members
    // and https://stackoverflow.com/questions/1548320/can-c-sharp-extension-methods-access-private-variables

    public static T GetPrivateField<T>(this object obj, string name)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = obj.GetType();
        // 2025-10-21 Infixo Code to walk the chain to find parents' private fields
        while (type != null)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field != null)
                return (T)field.GetValue(obj); // found the field
            type = type.BaseType; // step-up the chain
        }
        throw new MissingFieldException(obj.GetType().Name, name);
    }

    public static T GetPrivateProperty<T>(this object obj, string name)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = obj.GetType();
        PropertyInfo field = type.GetProperty(name, flags);
        return (T)field.GetValue(obj, null);
    }

    public static void SetPrivateField(this object obj, string name, object value)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = obj.GetType();
        FieldInfo field = type.GetField(name, flags);
        field.SetValue(obj, value);
    }

    public static void SetPrivateProperty(this object obj, string name, object value)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = obj.GetType();
        PropertyInfo field = type.GetProperty(name, flags);
        field.SetValue(obj, value, null); // 3rd param is only used for indexer properties
    }

    public static T CallPrivateMethod<T>(this object obj, string name, params object[] param)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = obj.GetType();
        MethodInfo method = type.GetMethod(name, flags);
        return (T)method.Invoke(obj, param);
    }

    // 2025-09-23 Infixo: Calling a method that returns void, cannot be done via <T>
    public static void CallPrivateMethodVoid(this object obj, string name, params object[] param)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = obj.GetType();
        MethodInfo method = type.GetMethod(name, flags);
        method.Invoke(obj, param);
    }

    // 2025-10-01 Infixo: Calling a method with its full signature
    public static void CallPrivateMethodTypesVoid(this object obj, string name, Type[] types, params object[] param)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = obj.GetType();
        MethodInfo method = type.GetMethod(name, flags, null, types, null);
        method.Invoke(obj, param);
    }

    // 2025-09-25 Infixo: Calling a private static method requires a bit different approach
    // Usage: typeof(MyClass).CallPrivateStaticMethod<string>("MethodName", [params]);
    public static T CallPrivateStaticMethod<T>(this Type type, string name, params object[] param)
    {
        BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        MethodInfo method = type.GetMethod(name, flags);
        return (T)method.Invoke(null, param); // null target for static
    }

    // 2025-10-06 Infixo: Calling a private static method requires a bit different approach
    // Usage: typeof(MyClass).CallPrivateStaticMethod<string>("MethodName", [params]);
    public static void CallPrivateStaticMethodVoid(this Type type, string name, params object[] param)
    {
        BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        MethodInfo method = type.GetMethod(name, flags);
        method.Invoke(null, param); // null target for static
    }

    // 2025-10-2 Infixo: Calling a private constructor
    // Usage: typeof(MyClass).CallPrivateConstructor<MyClass>([params]);
    public static T CallPrivateConstructor<T>(this Type type, Type[] args, params object[] param)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ConstructorInfo? ctor = type.GetConstructor(flags, null, args, null);
        if (ctor == null)
            throw new Exception("Constructor not found!");
        else
            return (T)ctor.Invoke(param);
    }

    // 2025-09-22 Infixo: Accessing a property that is public but its Setter is private
    public static void SetPublicProperty(this object obj, string name, object value)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly; // we need both public and non-public members
        Type type = obj.GetType();
        // 2025-10-02 Code to walk the chain to find private setter, if abstract class is used
        while (type != null)
        {
            PropertyInfo? prop = type.GetProperty(name, flags);
            // Now, get the private setter method. The 'true' argument is crucial, we're looking for a non-public method.
            var setter = prop?.GetSetMethod(true);
            if (setter != null)
            {
                setter.Invoke(obj, [value]);
                return;
            }
            type = type.BaseType;
        }
    }
}

#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning restore CS8602
#pragma warning restore CS8603
