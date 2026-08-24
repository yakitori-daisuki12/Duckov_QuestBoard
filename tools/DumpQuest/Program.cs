using System;
using System.Linq;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

var path = @"D:\Steam\steamapps\common\Escape from Duckov\Duckov_Data\Mods\DuckovFishingInfo\DuckovFishingInfo.dll";
var d = new CSharpDecompiler(path, new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });
foreach (var t in d.TypeSystem.GetAllTypeDefinitions())
{
    Console.WriteLine("TYPE " + t.FullName);
    try
    {
        Console.WriteLine(d.DecompileTypeAsString(t.FullTypeName));
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.Message);
    }
}
