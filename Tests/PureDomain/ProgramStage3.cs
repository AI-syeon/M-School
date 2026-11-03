using System;
using MagicCampus.UnityAdapters.ContentIO;
public static class ProgramStage3
{
    public static int Main()
    {
        int failures = 0;
        void Run(string name, Action test) { try { test(); Console.WriteLine("PASS " + name); } catch(Exception e) { failures++; Console.WriteLine("FAIL " + name + " " + e); } }
        Run("ContentIO", () => { if (MagicCampus.Tests.ContentIO.ContentIOSpecs.Run() != 0) throw new Exception("ContentIO failures"); });
        var codec = new JsonDataCodec();
        Run("ContentValidation", () => MagicCampus.Tests.ContentValidation.ContentValidationSpecs.Run(codec, new StandardSchemaValidator(codec)));
        Run("Movement", MagicCampus.Tests.Movement.MovementSpecs.Run);
        Run("Effects", MagicCampus.Tests.Effects.EffectsSpecs.Run);
        Run("Time", MagicCampus.Tests.Time.TimeSpecs.Run);
        return failures == 0 ? 0 : 1;
    }
}
