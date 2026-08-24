using System;
using System.Linq;
using System.Reflection;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

var path = @"D:\Steam\steamapps\common\Escape from Duckov\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll";
var module = new PEFile(path);
var settings = new DecompilerSettings { ThrowOnAssemblyResolveErrors = false };
var d = new CSharpDecompiler(path, settings);

foreach (var t in d.TypeSystem.GetAllTypeDefinitions().Where(t => t.FullName.Contains("Duckov.Quests")))
{
    string src;
    try { src = d.DecompileTypeAsString(t.FullTypeName); }
    catch { continue; }
    if (src.Contains("SetEverInspected"))
    {
        Console.WriteLine($"FOUND in {t.FullName}");
        int p = 0;
        int n = 0;
        while ((p = src.IndexOf("SetEverInspected", p, StringComparison.Ordinal)) >= 0 && n < 5)
        {
            Console.WriteLine(src.Substring(Math.Max(0, p - 200), Math.Min(500, src.Length - Math.Max(0, p - 200))));
            Console.WriteLine("====");
            p += 16;
            n++;
        }
    }
}

Console.WriteLine("----- ActivateQuest snippet -----");
string mgr = d.DecompileTypeAsString(new FullTypeName("Duckov.Quests.QuestManager"));
int a = mgr.IndexOf("ActivateQuest", StringComparison.Ordinal);
Console.WriteLine(mgr.Substring(a, Math.Min(2500, mgr.Length - a)));
