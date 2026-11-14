using System.Collections.Generic;
using Eleven.Simulation.Maths;

namespace Eleven.Simulation.AI
{
    /// <summary>
    /// A CPU outfielder's brain (#83): pure and stateless, it decides on his
    /// <see cref="OutfieldObservation"/> alone (INV-9), in his own side's frame, attacking +x. It
    /// reads the ball's path with the simulation's own <see cref="BallFlight"/> (INV-13). Code in
    /// this namespace can't name the simulation's state (ELEVEN003). It asks for a plan; the
    /// simulation decides when he may make one and when it takes effect (INV-12), and his body does
    /// what any footballer's does with the same stick and kick. docs/simulation/cpu.md has the rule.
    /// </summary>
    public static class OutfieldBrain
    {
        // Technical constants of the basic outfielder, not feel values (docs/simulation/cpu.md).

        // m. The ball this close, and no opponent closer to it, is his.
        private const double HasBall = 1.6;

        // m. He shoots from within this of the goal he attacks.
        private const double ShootRange = 16;

        // m. How far inside a post he aims.
        private const double InsidePost = 0.7;

        // 0–1. A shot's strength and loft.
        private const double ShotStrength = 1, ShotLoft = 0.12;

        // m. An opponent this close ahead of him makes him carry the ball round.
        private const double Evade = 6;

        // m. Out of possession, he stands this far goal-side of the ball; within Press of it he goes for it.
        private const double GoalSide = 3, Press = 4;

        // s. How far ahead he reads the ball, and the reading beyond which he is seeking rather than approaching.
        private const double Horizon = 4, Near = 1;

        // m. Within Arrive of where he wants to be, he eases in rather than runs; within Stand he
        // stands. He acts on a stick decided a while ago, so a short ease would carry him past the
        // spot and back.
        private const double Arrive = 5.0, Stand = 0.5;

        // m. Where he waits while a keeper holds the ball: his own half when it is theirs, up the pitch when it is his side's.
        private static readonly Vec2 DropOff = new Vec2(-12, 0), ShowFor = new Vec2(8, 10);

        public static CpuPlan Decide(OutfieldObservation seen, SimConfig config)
        {
            Vec2 me = seen.Position;
            Vec2 ball = seen.BallPosition.Horizontal;
            var theirGoal = new Vec2(config.GoalLineX, 0);
            var ownGoal = new Vec2(-config.GoalLineX, 0);

            if (seen.Holder == SeenHolder.OpponentKeeper) return new CpuPlan(CpuMode.Recover, Ease(DropOff - me));
            if (seen.Holder == SeenHolder.OwnKeeper) return new CpuPlan(CpuMode.Recover, Ease(ShowFor - me));

            // The nearest opponent who isn't a keeper, if he sees one.
            bool opponentSeen = false;
            Vec2 opponent = Vec2.Zero;
            double nearest = double.PositiveInfinity;
            Vec2? theirKeeper = null;
            foreach (SeenPlayer other in seen.Others)
            {
                if (other.Teammate) continue;
                if (other.Keeper)
                {
                    theirKeeper = other.Position;
                    continue;
                }

                double d = (other.Position - ball).LengthSquared;
                if (d < nearest)
                {
                    nearest = d;
                    opponent = other.Position;
                    opponentSeen = true;
                }
            }

            double mine = Length(ball - me);
            double theirs = opponentSeen ? SimMath.Sqrt(nearest) : double.PositiveInfinity;

            // With the ball: shoot in range, otherwise carry it at the goal, round an opponent in the way.
            if (mine <= HasBall && mine <= theirs && seen.BallPosition.Z < 1)
            {
                if (Length(theirGoal - ball) <= ShootRange && !seen.Kicking)
                {
                    // Inside the post further from their keeper; the nearer post to the ball if he sees none.
                    double side = theirKeeper.HasValue ? (theirKeeper.Value.Y > 0 ? -1 : 1) : (ball.Y >= 0 ? 1 : -1);
                    var target = new Vec2(config.GoalLineX, side * (config.GoalWidth / 2 - InsidePost));
                    return new CpuPlan(CpuMode.Shoot, Unit(theirGoal - me), new KickIntent(target - ball, ShotStrength, ShotLoft));
                }

                Vec2 toGoal = Unit(theirGoal - ball);
                if (opponentSeen)
                {
                    Vec2 toOpponent = opponent - ball;
                    double ahead = toOpponent.X * toGoal.X + toOpponent.Y * toGoal.Y;
                    if (ahead > 0 && toOpponent.LengthSquared < Evade * Evade)
                    {
                        // Sideways, away from him: the side of the line to goal he isn't on.
                        double across = toGoal.X * toOpponent.Y - toGoal.Y * toOpponent.X;
                        var sideways = across > 0 ? new Vec2(toGoal.Y, -toGoal.X) : new Vec2(-toGoal.Y, toGoal.X);
                        toGoal = Unit(toGoal + sideways);
                    }
                }

                return new CpuPlan(CpuMode.Carry, toGoal);
            }

            // Where he can first meet the ball, by its own flight, and whether the opponent meets it sooner.
            Vec2 meet = Meet(seen, config, me, seen.MaxSpeed, out double myTime);
            double theirTime = double.PositiveInfinity;
            if (opponentSeen) Meet(seen, config, opponent, seen.MaxSpeed, out theirTime);

            if (myTime <= theirTime || mine <= Press)
            {
                return new CpuPlan(myTime > Near ? CpuMode.SeekBall : CpuMode.Approach, Unit(meet - me));
            }

            // Out of possession and second to the ball: goal-side of it, on the line to his own goal.
            Vec2 spot = ball + Unit(ownGoal - ball) * GoalSide;
            return new CpuPlan(CpuMode.Defend, Ease(spot - me));
        }

        // The first point on the ball's read path a runner from `from` at `speed` can be at as the
        // ball gets there; where the ball ends up if he can't within the horizon.
        private static Vec2 Meet(OutfieldObservation seen, SimConfig config, Vec2 from, double speed, out double seconds)
        {
            var path = new List<Vec3>();
            BallFlight.Predict(config, seen.BallPosition, seen.BallVelocity, seen.BallSpin, (int)(Horizon * config.TickRate), path, bothGoals: seen.BothGoals);
            Vec2 at = seen.BallPosition.Horizontal;
            for (int i = 0; i < path.Count; i++)
            {
                at = path[i].Horizontal;
                double time = (i + 1) * config.TickSeconds;
                if (path[i].Z < 1.5 && Length(at - from) <= speed * time)
                {
                    seconds = time;
                    return at;
                }
            }

            // Not within the horizon: where the ball is by then, and how long the run there takes.
            seconds = speed > 0 ? Length(at - from) / speed : double.PositiveInfinity;
            if (seconds < Horizon) seconds = Horizon;
            return at;
        }

        private static double Length(Vec2 v) => SimMath.Sqrt(v.LengthSquared);

        // Toward `to` at full speed beyond Arrive, easing in within it, standing once there.
        private static Vec2 Ease(Vec2 to)
        {
            double length = Length(to);
            return length > Arrive ? to * (1 / length) : length > Stand ? to * (1 / Arrive) : Vec2.Zero;
        }

        private static Vec2 Unit(Vec2 to)
        {
            double length = Length(to);
            return length > 0 ? to * (1 / length) : Vec2.Zero;
        }
    }
}
