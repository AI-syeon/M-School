namespace MagicCampus.Tests.Time
{
    public static class TimeSpecs
    {
        /// <summary>Runs every Task 3-5 spec; throws InvalidOperationException on the first failure.</summary>
        public static void Run()
        {
            CalendarSpecs.Run();
            ClockServiceSpecs.Run();
            ClockParticipantSpecs.Run();
        }
    }
}
