using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// Codex verification harness; each delegated task owns its independent specifications.
public static class Program
{
    public static int Main(string[] args)
    {
        var suites = new (string Name, Action Run)[] {
            ("Parsing", MagicCampus.Tests.Parsing.ParsingSpecs.Run),
            ("State", MagicCampus.Tests.State.StateSpecs.Run),
            ("Runtime", MagicCampus.Tests.Runtime.RuntimeSpecs.Run),
            ("Storage", MagicCampus.Tests.Storage.StorageSpecs.Run),
            ("CrossModule", MagicCampus.Tests.CrossModule.Stage2IntegrationSpecs.Run)
        };
        var results = new List<object>();
        var failures = 0;
        foreach (var suite in suites)
        {
            if (args.Length > 0 && Array.IndexOf(args, suite.Name) < 0) continue;
            try { suite.Run(); results.Add(new { suite = suite.Name, passed = true }); Console.WriteLine("PASS " + suite.Name); }
            catch (Exception ex) { failures++; results.Add(new { suite = suite.Name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + suite.Name + ": " + ex); }
        }
        Directory.CreateDirectory("Tests/Results");
        var suffix = args.Length == 0 ? "pure" : string.Join("-", args);
        File.WriteAllText("Tests/Results/stage2-" + suffix + ".json", JsonSerializer.Serialize(new { scope = "pure domain and real disk; Unity adapters separate", failures, results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
