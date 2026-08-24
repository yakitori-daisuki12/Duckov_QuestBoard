using System;
using System.IO;
using System.Linq;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

static void Scan(string dll)
{
    Console.WriteLine("\n##### " + dll);
    if (!File.Exists(dll)) { Console.WriteLine("missing"); return; }
    var d = new CSharpDecompiler(dll, new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });
    foreach (var t in d.TypeSystem.GetAllTypeDefinitions())
    {
        try
        {
            string src = d.DecompileTypeAsString(t.FullTypeName);
            bool hit =
                src.Contains("itemDescription") ||
                src.Contains("onSetupItem") ||
                src.Contains("onSetupMeta") ||
                src.Contains("ItemHoveringUI") ||
                (src.Contains("\"$\"") && (src.Contains("Value") || src.Contains("price") || src.Contains("Price")));
            if (!hit) continue;
            if (!(src.Contains("$") || src.Contains("ItemHovering") || src.Contains("onSetup")))
                continue;
            // tighten
            if (!(src.Contains("\"$") || src.Contains("$\"$") || src.Contains("'$'") ||
                  src.Contains("onSetupItem") || src.Contains("onSetupMeta") ||
                  src.Contains("itemDescription")))
                continue;
            Console.WriteLine("HIT " + t.FullName);
            foreach (var line in src.Split('\n'))
            {
                if (line.Contains("$") || line.Contains("itemDescription") || line.Contains("onSetup") ||
                    line.Contains("Value") || line.Contains("sell") || line.Contains("Sell") ||
                    line.Contains("price") || line.Contains("Price") || line.Contains("Convert"))
                    Console.WriteLine("  " + line.TrimEnd());
            }
            Console.WriteLine("---");
        }
        catch { }
    }
}

Scan(@"D:\Steam\steamapps\common\Escape from Duckov\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll");
foreach (var dir in Directory.GetDirectories(@"D:\Steam\steamapps\common\Escape from Duckov\Duckov_Data\Mods"))
{
    foreach (var dll in Directory.GetFiles(dir, "*.dll"))
        Scan(dll);
}
