using System;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

var path = @"D:\Steam\steamapps\common\Escape from Duckov\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll";
var d = new CSharpDecompiler(path, new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });

var quest = d.DecompileTypeAsString(new FullTypeName("Duckov.Quests.Quest"));
foreach (string key in new[] { "TryComplete()", "NotifyActivated", "onQuestStatusChanged", "AreTasksFinished()" })
{
    int idx = quest.IndexOf(key, StringComparison.Ordinal);
    Console.WriteLine($"=== {key} @ {idx} ===");
    if (idx >= 0)
    {
        int start = Math.Max(0, idx - 200);
        Console.WriteLine(quest.Substring(start, Math.Min(1600, quest.Length - start)));
    }
}
