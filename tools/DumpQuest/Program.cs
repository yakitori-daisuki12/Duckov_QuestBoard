using System;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

var path = @"D:\Steam\steamapps\common\Escape from Duckov\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll";
var d = new CSharpDecompiler(path, new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });

// QuestGiverView - full first 6000 chars to understand layout
var gv = d.DecompileTypeAsString(new FullTypeName("Duckov.Quests.UI.QuestGiverView"));
Console.WriteLine("=== QuestGiverView (first 6000) ===");
Console.WriteLine(gv.Substring(0, Math.Min(6000, gv.Length)));
