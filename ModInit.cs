using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Utilities;


public static class ModInit
{
    public static bool InitializeMod(string name)
    {
        DebugConsole.Show();
        string harmonyId = "Infixo." + name;
        Log.Write($"WWR mod {name} successfully started, harmonyId is {harmonyId}.");
        //EnsureMetadataLoaded();

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

                // IL hash (semantic): opcode + normalized operand list
                string ilHash = SemanticIlHasher.ComputeSemanticIlHash(patchedMethod);

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


    /// <summary>
    /// Attaches a force-load of metadata.dll to the resolver event. It loads metadata.dll that is shipped with the game.
    /// </summary>
    public static void EnsureMetadataLoaded()
    {
        Assembly currentAssembly = Assembly.GetExecutingAssembly();
        string gameDir = Path.GetDirectoryName(Path.GetDirectoryName(currentAssembly.Location)!)!;
        string path = Path.Combine(gameDir, "System.Reflection.Metadata.dll");
        AssemblyLoadContext alc = AssemblyLoadContext.GetLoadContext(currentAssembly)!;
        if (!File.Exists(path))
        {
            Log.Write("Warning! System.Reflection.Metadata.dll is not available.");
            return;
        }
        alc.Resolving += (context, assemblyName) =>
        {
            if (assemblyName.Name == "System.Reflection.Metadata")
            {
                Log.Write($"Loading {path}.");
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return null;
        };
    }
}


/// <summary>
/// Compute a stable "semantic" hash of a method's IL using BlobReader.
/// Normalizes metadata tokens into readable names (type.method/field/string),
/// ignores header/RVA/offsets and therefore is stable across builds when IL is unchanged.
/// </summary>
public static class SemanticIlHasher
{
    // Opcode lookup tables (build once)
    private static readonly OpCode[] SingleByteOpCodes = new OpCode[256];
    private static readonly OpCode[] MultiByteOpCodes = new OpCode[256];

    static SemanticIlHasher()
    {
        foreach (var fi in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (fi.FieldType != typeof(OpCode)) continue;
            var op = (OpCode)fi.GetValue(null)!;
            ushort value = (ushort)op.Value;
            if (value < 0x100)
                SingleByteOpCodes[value] = op;
            else if ((value & 0xFF00) == 0xFE00)
                MultiByteOpCodes[value & 0xFF] = op;
        }
    }

#if DEBUG
    public static unsafe string ComputeSemanticIlHash(MethodBase method)
    {
        try
        {
            MethodBody? body = method.GetMethodBody();
            if (body == null)
                return "<no-il>";

            byte[]? raw = body.GetILAsByteArray();
            if (raw == null || raw.Length == 0)
                return "<no-il>";

            fixed (byte* ptr = raw)
            {
                var reader = new BlobReader(ptr, raw.Length);

                var parts = new List<string>();

                while (reader.Offset < reader.Length)
                {
                    // Read opcode
                    byte first = reader.ReadByte();
                    OpCode op;
                    if (first == 0xFE)
                    {
                        byte second = reader.ReadByte();
                        op = MultiByteOpCodes[second];
                    }
                    else
                    {
                        op = SingleByteOpCodes[first];
                    }

                    string opname = op.Name ?? "<unknown>";
                    string operandText = string.Empty;

                    // Normalize operand by operand type, resolving tokens using reflection Module
                    switch (op.OperandType)
                    {
                        case OperandType.InlineNone:
                            operandText = "";
                            break;

                        case OperandType.ShortInlineI:
                            operandText = reader.ReadSByte().ToString();
                            break;

                        case OperandType.InlineI:
                            operandText = reader.ReadInt32().ToString();
                            break;

                        case OperandType.InlineI8:
                            operandText = reader.ReadInt64().ToString();
                            break;

                        case OperandType.ShortInlineR:
                            operandText = reader.ReadSingle().ToString(); // do not use R!
                            break;

                        case OperandType.InlineR:
                            operandText = reader.ReadDouble().ToString(); // do not use R!
                            break;

                        case OperandType.ShortInlineVar:
                            {
                                // local/arg index (1 byte)
                                byte idx = reader.ReadByte();
                                operandText = "var#" + idx;
                            }
                            break;

                        case OperandType.InlineVar:
                            {
                                // local/arg index (2 bytes)
                                ushort idx = reader.ReadUInt16();
                                operandText = "var#" + idx;
                            }
                            break;

                        case OperandType.InlineString:
                            {
                                int strToken = reader.ReadInt32();
                                try
                                {
                                    operandText = method.Module.ResolveString(strToken);
                                }
                                catch
                                {
                                    operandText = "<string-token>";
                                }
                            }
                            break;

                        case OperandType.InlineSig:
                            {
                                // signature token — normalize to hex token (can't easily resolve)
                                int sigToken = reader.ReadInt32();
                                //operandText = $"sig:0x{sigToken:X8}";
                                operandText = "<signature-token>";
                            }
                            break;

                        case OperandType.InlineMethod:
                            {
                                int mToken = reader.ReadInt32();
                                try
                                {
                                    var resolved = method.Module.ResolveMethod(mToken);
                                    operandText = NormalizeMethod(resolved);
                                }
                                catch
                                {
                                    operandText = "<method-token>";
                                }
                            }
                            break;

                        case OperandType.InlineField:
                            {
                                int fToken = reader.ReadInt32();
                                try
                                {
                                    var resolved = method.Module.ResolveField(fToken);
                                    operandText = NormalizeField(resolved);
                                }
                                catch
                                {
                                    operandText = "<field-token>";
                                }
                            }
                            break;

                        case OperandType.InlineType:
                            {
                                int tToken = reader.ReadInt32();
                                try
                                {
                                    var resolved = method.Module.ResolveType(tToken);
                                    operandText = NormalizeType(resolved);
                                }
                                catch
                                {
                                    operandText = "<type-token>";
                                }
                            }
                            break;

                        case OperandType.InlineTok:
                            {
                                // can be method/type/field token; try resolve in order
                                int tok = reader.ReadInt32();
                                //string tokText = $"tok:0x{tok:X8}";
                                try
                                {
                                    var m = method.Module.ResolveMethod(tok);
                                    operandText = NormalizeMethod(m);
                                    break;
                                }
                                catch { }
                                try
                                {
                                    var f = method.Module.ResolveField(tok);
                                    operandText = NormalizeField(f);
                                    break;
                                }
                                catch { }
                                try
                                {
                                    var t = method.Module.ResolveType(tok);
                                    operandText = NormalizeType(t);
                                    break;
                                }
                                catch
                                {
                                    operandText = "<ref-token>";
                                }
                            }
                            break;

                        case OperandType.InlineSwitch:
                            {
                                int count = reader.ReadInt32();
                                // skip the offsets (unstable) but include count
                                for (int i = 0; i < count; i++)
                                    reader.ReadInt32();
                                operandText = $"switch({count})";
                            }
                            break;

                        case OperandType.ShortInlineBrTarget:
                            {
                                sbyte delta = reader.ReadSByte();
                                operandText = "br(DELTA)"; // offsets are unstable, ignore actual value
                            }
                            break;

                        case OperandType.InlineBrTarget:
                            {
                                int delta = reader.ReadInt32();
                                operandText = "br(DELTA)";
                            }
                            break;

                        default:
                            operandText = "";
                            break;
                    }

                    parts.Add(opname + "|" + operandText);
                }

                string semantic = string.Join(";", parts);
                // Hash the semantic string
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(semantic)));
                return hash;
            }
        }
        catch
        {
            return "<error>";
        }
    }
#endif

    // ---------- Normalizers (use Reflection types) ----------
    private static string NormalizeMethod(MethodBase? m)
    {
        if (m == null) return "<null-method>";
        var decl = m.DeclaringType != null ? NormalizeType(m.DeclaringType) : "<null-type>";
        var paramList = string.Join(",", m.GetParameters().Select(p => NormalizeType(p.ParameterType)));
        return $"{decl}.{m.Name}({paramList})";
    }

    private static string NormalizeField(FieldInfo? f)
    {
        if (f == null) return "<null-field>";
        var decl = f.DeclaringType != null ? NormalizeType(f.DeclaringType) : "<null-type>";
        return $"{decl}.{f.Name}:{NormalizeType(f.FieldType)}";
    }

    private static string NormalizeType(Type? t)
    {
        if (t == null) return "<null-type>";

        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            string baseName = def.FullName?.Split('`')[0] ?? def.Name.Split('`')[0];
            string args = string.Join(",", t.GetGenericArguments().Select(NormalizeType));
            return $"{baseName}<{args}>";
        }

        if (t.IsArray)
        {
            return NormalizeType(t.GetElementType()) + "[]";
        }

        return t.FullName ?? t.Name;
    }
}
