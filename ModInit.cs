using HarmonyLib;
using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;

namespace Utilities;


public static class ModInit
{
    public static bool InitializeMod(string name)
    {
        DebugConsole.Show();
        string harmonyId = "Infixo." + name;
        Log.Write($"WWR mod {name} successfully started, harmonyId is {harmonyId}.");

        // Harmony
        var harmony = new Harmony(harmonyId);
        try
        {
            harmony.PatchAll();
        }
        catch (Exception ex)
        {
            Log.Write("HARMONY EXCEPTION");
            Log.Write(ex.ToString());
            return false;
        }
        var patchedMethods = harmony.GetPatchedMethods().ToArray();
        Log.Write($"Plugin {harmonyId} made patches! Patched methods: {patchedMethods.Length}");

        var dumpList = new List<object>();

        foreach (var patchedMethod in patchedMethods)
        {
            try
            {
                // Basic info
                var moduleName = patchedMethod.Module?.Name ?? "<unknown module>";
                string declaringTypeName = CleanTypeName(patchedMethod.DeclaringType);
                var methodName = patchedMethod.Name;

                // Parameters
                var parameters = patchedMethod.GetParameters()
                    .Select(p => CleanTypeName(p.ParameterType))
                    .ToArray();

                // Is static
                var isStatic = patchedMethod.IsStatic;

                // Return type (only MethodInfo has ReturnType; constructors -> void)
                string returnType;
                if (patchedMethod is MethodInfo mi)
                    returnType = CleanTypeName(mi.ReturnType);
                else
                    returnType = "System.Void";

                // Full readable signature
                // var fullSignature = $"{returnType} {declaringTypeName}.{methodName}({string.Join(", ", parameters)})"; // redundant

                // IL bytes (may be null for abstract/extern/pinvoke)
                byte[]? ilBytes = null;
                try
                {
                    var body = patchedMethod.GetMethodBody();
                    ilBytes = body?.GetILAsByteArray();
                }
                catch
                {
                    // In some scenarios (dynamic methods, etc.) GetMethodBody may throw; treat as no IL
                    ilBytes = null;
                }

                // IL hash (SHA-256) or marker
                string ilHash;
                if (ilBytes != null && ilBytes.Length > 0)
                {
                    ilHash = Convert.ToHexString(SHA256.HashData(ilBytes));
                }
                else
                {
                    ilHash = "<no-il>";
                }

                // Optionally include a hex dump of IL (commented out to keep file small)
                // string ilHexDump = ilBytes != null ? BitConverter.ToString(ilBytes).Replace("-", "") : null;

                var entry = new
                {
                    module = moduleName,
                    type = declaringTypeName,
                    method = methodName,
                    //fullSignature = fullSignature, // redundant
                    parameters = parameters,
                    returnType = returnType,
                    isStatic = isStatic,
                    ilHash = ilHash,
                    // ilHex = ilHexDump  // uncomment if you want the raw IL hex in the JSON
                };

                dumpList.Add(entry);
                Log.Write($"{declaringTypeName}.{methodName}");
            }
            catch (Exception ex)
            {
                Log.Write($"EXCEPTION Failed to dump patched method {patchedMethod?.Name}.");
                Log.Write(ex.ToString());
            }
        }

        // Write JSON to %TEMP%\MyMod_patches.json
        try
        {
            var outPath = Path.Combine(Path.GetTempPath(), $"{name}_patches.json");
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(dumpList, jsonOptions);
            File.WriteAllText(outPath, json);
            Log.Write($"Patch dump written to: {outPath}");
        }
        catch (Exception ex)
        {
            Log.Write($"EXCEPTION Failed to write patch dump {name}_patches.json.");
            Log.Write(ex.ToString());
        }

        return true;
    }


    public static string CleanTypeName(Type? type)
    {
        if (type == null)
            return "<unknown>";

        // Constructed generic (ExplorerUI<IExplorerItem>)
        if (type.IsGenericType)
        {
            string baseName = type.GetGenericTypeDefinition().FullName!;
            int tick = baseName.IndexOf('`');
            if (tick >= 0)
                baseName = baseName.Substring(0, tick);

            var args = type.GetGenericArguments()
                           .Select(arg => CleanTypeName(arg));

            return $"{baseName}<{string.Join(", ", args)}>";
        }

        // Array
        if (type.IsArray)
        {
            return $"{CleanTypeName(type.GetElementType()!)}[]";
        }

        // Nullable<T>
        if (Nullable.GetUnderlyingType(type) is Type inner)
        {
            return $"{CleanTypeName(inner)}?";
        }

        // Normal type — strip assembly info manually when type.FullName contains it
        string full = type.FullName ?? type.Name;

        // Remove assembly information after commas
        int commaIndex = full.IndexOf(',');
        if (commaIndex >= 0)
            full = full.Substring(0, commaIndex);

        return full;
    }
}
