namespace MagicCampus.Contracts
{
    // Task1 additive coordination correction found before independent Stage3 implementations start.
    public interface IWorldStateProjector
    {
        Outcome<ChangeSet> PlanProjection(StateSnapshot basis);
    }

    public interface IClockCostPlanner
    {
        Outcome<ChangeSet> PlanCost(StateSnapshot basis, double minutes);
    }

    public interface IEventCommitPlanner
    {
        Outcome<StatePlan> PlanCommit(CompiledEvent definition, EffectContext context);
    }

    public interface IPresentationLifetime
    {
        void CloseOwner(string ownerToken);
    }
}
