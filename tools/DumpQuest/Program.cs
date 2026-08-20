using System;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

var path = @"D:\Steam\steamapps\common\Escape from Duckov\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll";
var d = new CSharpDecompiler(path, new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });

var mgr = d.DecompileTypeAsString(new FullTypeName("Duckov.Modding.ModManager"));
foreach (string key in new[] { "displayName", "TryProcessModFolder", "SteamWorkshop", "GetItem" })
{
    int idx = mgr.IndexOf(key, StringComparison.Ordinal);
    Console.WriteLine($"===== {key} @ {idx} =====");
    if (idx >= 0)
    {
        int start = Math.Max(0, idx - 300);
        Console.WriteLine(mgr.Substring(start, Math.Min(3500, mgr.Length - start)));
    }
}
