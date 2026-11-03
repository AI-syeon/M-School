using System;
public static class ProgramStage4
{
    public static int Main(string[] args)
    {
        int failures = 0;
        void Run(string name, Action test) { try { test(); Console.WriteLine("PASS " + name); } catch(Exception e) { failures++; Console.WriteLine("FAIL " + name + " " + e); } }
        Run("Persistence", MagicCampus.Tests.PureDomain.PersistenceTests.PersistenceSpecs.Run);
        Run("Schedules", MagicCampus.Tests.PureDomain.Schedules.SchedulesSpecs.Run);
        Run("Events", MagicCampus.Tests.Events.EventRunnerSpecs.Run);
        Run("FirstConnectionData", MagicCampus.Tests.FirstConnectionData.FirstConnectionDataSpecs.Run);
        if(Array.IndexOf(args,"--real-disk")>=0){Run("ProductionDisk",MagicCampus.Tests.FirstConnectionData.FirstConnectionDataSpecs.RunDisk);Run("RunnerGraph",MagicCampus.Tests.FirstConnectionData.FirstConnectionDataSpecs.RunGraph);}
        return failures == 0 ? 0 : 1;
    }
}
