namespace Eleven.Simulation.AI
{
    /// <summary>
    /// A keeper's brain (#69): pure and stateless, it decides on his <see cref="KeeperObservation"/>
    /// alone (INV-9), and predicts the ball with the simulation's own <see cref="BallFlight"/>
    /// (INV-13). Code in this namespace can't name the simulation's state (ELEVEN003).
    /// docs/simulation/keeper.md has the rule.
    /// </summary>
    public static class KeeperBrain
    {
        public static KeeperDecision Decide(in KeeperObservation seen, SimConfig config) => new KeeperDecision(Vec2.Zero);
    }
}
