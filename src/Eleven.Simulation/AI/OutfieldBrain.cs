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

        // m. A supporter's place (#92): this far ahead of his teammate with the ball and this far
        // across, toward the middle; marking, this far goal-side of the opponent he marks.
        private const double SupportAhead = 6, SupportWide = 10, MarkGoalSide = 2.5;

        // m. A supporter alongside the carrier breaks wide aiming this far ahead of where he is (#94).
        private const double SupportBreak = 4;

        // m. A teammate with no opponent this close to him is open for a pass (#92).
        private const double Open = 4;

        // rad. A pass goes only to a teammate within this of his heading: his quick pass's own cone.
        private const double PassCone = 0.9;

        // m. A header or volley goes at goal from this much further out than a shot on the ground (#94).
        private const double AirShotExtra = 6;

        // 0–1. A header cleared up the pitch is lofted this much.
        private const double ClearLoft = 0.5;

        // Fraction of reach. Within this much of his reach, at the moment his kick would connect, he goes for a ball in the air.
        private const double AirReach = 0.8;

        // The shape (#96), in m in his own frame with the ball at the centre spot: the back row's,
        // the middle row's and the forward's lines, and the rows' half-width; how much of the ball's
        // x and y the shape follows, how far it steps up when his side has the ball, how far inside
        // the lines it stays, and how far goal-side of the ball the cover stands.
        private const double BackLine = -24, MiddleLine = -10, ForwardLine = 4, ShapeWidth = 14;
        private const double ShapeFollow = 0.55, ShapeDrift = 0.3, ShapeStepUp = 6, ShapeInside = 6, CoverGoalSide = 6;

        // The slide (#98): within this share of the slide's range he slides, only with the ball
        // this much nearer him than the man, and beyond this many standing challenge ranges.
        private const double SlideFrom = 0.7, SlideClear = 0.4, SlideOver = 1.5;

        // m. Where he waits while a keeper holds the ball: his own half when it is theirs, up the pitch when it is his side's.
        private static readonly Vec2 DropOff = new Vec2(-12, 0), ShowFor = new Vec2(8, 10);

        public static CpuPlan Decide(OutfieldObservation seen, SimConfig config)
        {
            Vec2 me = seen.Position;
            Vec2 ball = seen.BallPosition.Horizontal;
            var theirGoal = new Vec2(config.GoalLineX, 0);
            var ownGoal = new Vec2(-config.GoalLineX, 0);

            if (seen.Holder != SeenHolder.None)
            {
                // Each to his own side of the waiting spot (#92): the one a teammate is nearer to
                // goes to the teammate, and he takes the mirrored one.
                Vec2 wait = seen.Holder == SeenHolder.OpponentKeeper ? DropOff : ShowFor;
                var mirrored = new Vec2(wait.X, -wait.Y);
                bool withMates = false;
                foreach (SeenPlayer other in seen.Others) withMates |= other.Teammate && !other.Keeper;
                if (withMates && wait.Y == 0)
                {
                    wait = new Vec2(wait.X, 8);
                    mirrored = new Vec2(wait.X, -8);
                }

                foreach (SeenPlayer other in seen.Others)
                {
                    if (other.Teammate && !other.Keeper && (other.Position - wait).LengthSquared < (me - wait).LengthSquared)
                    {
                        Vec2 swap = wait;
                        wait = mirrored;
                        mirrored = swap;
                        break;
                    }
                }

                return new CpuPlan(CpuMode.Recover, Ease(wait - me));
            }

            // The nearest opponent who isn't a keeper, if he sees one.
            bool opponentSeen = false;
            Vec2 opponent = Vec2.Zero, opponentVelocity = Vec2.Zero;
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
                    opponentVelocity = other.Velocity;
                    opponentSeen = true;
                }
            }

            double mine = Length(ball - me);
            double theirs = opponentSeen ? SimMath.Sqrt(nearest) : double.PositiveInfinity;

            // His teammates who aren't keepers (#92): the one nearest the ball, and whether he is open.
            bool mateSeen = false;
            Vec2 mate = Vec2.Zero;
            double mateNearest = double.PositiveInfinity;
            foreach (SeenPlayer other in seen.Others)
            {
                if (!other.Teammate || other.Keeper) continue;
                double d = (other.Position - ball).LengthSquared;
                if (d < mateNearest)
                {
                    mateNearest = d;
                    mate = other.Position;
                    mateSeen = true;
                }
            }

            double mates = mateSeen ? SimMath.Sqrt(mateNearest) : double.PositiveInfinity;

            // A ball coming to him in the air (#94): if his kick, made on this plan, would meet it
            // at a height his body can play, he heads or volleys it: at goal within range, up the
            // pitch otherwise. The same kick intent as on the ground.
            // Only one of a side goes up for it: a teammate nearer where it comes down leaves it to him.
            if (!seen.Kicking && AirContact(seen, config, out Vec3 contact)
                && (!mateSeen || (mate - contact.Horizontal).LengthSquared >= (me - contact.Horizontal).LengthSquared))
            {
                Vec2 from = contact.Horizontal;
                bool header = contact.Z >= config.HeaderMinHeight;
                if (Length(theirGoal - from) <= ShootRange + AirShotExtra)
                {
                    double side = from.Y >= 0 ? 1 : -1;
                    var target = new Vec2(config.GoalLineX, side * (config.GoalWidth / 2 - InsidePost));
                    return new CpuPlan(CpuMode.Shoot, Unit(from - me), new KickIntent(target - from, ShotStrength, header ? 0 : ShotLoft));
                }

                return new CpuPlan(CpuMode.Carry, Unit(from - me), new KickIntent(theirGoal - from, ShotStrength, header ? ClearLoft : ShotLoft));
            }

            // With the ball: shoot in range, otherwise carry it at the goal, round an opponent in the way.
            // Only the nearer of two teammates has it (#92); on a tie, the one further toward −y.
            bool mineNotMates = !mateSeen || mine < mates || (mine == mates && me.Y <= mate.Y);
            if (mine <= HasBall && mine <= theirs && mineNotMates && seen.BallPosition.Z < 1)
            {
                // Only with the ball within his kick's reach, or the kick misses (#85).
                if (Length(theirGoal - ball) <= ShootRange && !seen.Kicking && mine <= config.KickReach)
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

                // Pressed, with an open teammate in his quick pass's cone: he passes (#92).
                if (opponentSeen && mateSeen && !seen.Kicking && Pressed(ball, opponent, theirGoal) && IsOpen(seen, mate) && InCone(seen.Heading, mate - me))
                {
                    return new CpuPlan(CpuMode.Carry, Unit(mate - me), quickPass: true);
                }

                return new CpuPlan(CpuMode.Carry, toGoal);
            }

            // A teammate is nearer the ball (#92): he supports. In a passing lane ahead and across
            // when the teammate has it; marking the other opponent, goal-side, when he hasn't.
            // Only one of a side goes for the ball: the one who reaches it first, by its own flight, so
            // the receiver of a pass goes to meet it; on a tie, the one further toward +y.
            // A side of three or more holds a shape (#96): the first of it to the ball goes for it;
            // when the opponents have it, the second covers, goal-side; everyone else takes his
            // place from the back, shifted with the ball, further up when his side has it.
            if (seen.SideSize >= 3 && seen.Slot > 0)
            {
                Meet(seen, config, me, seen.MaxSpeed, out double myFirst);
                int sooner = 0;
                foreach (SeenPlayer other in seen.Others)
                {
                    if (!other.Teammate || other.Keeper) continue;
                    Meet(seen, config, other.Position, seen.MaxSpeed, out double theirFirst);
                    if (theirFirst + 0.05 < myFirst || (theirFirst <= myFirst + 0.05 && other.Position.Y > me.Y)) sooner++;
                }

                bool ours = mates <= HasBall && mates <= theirs;
                if (sooner == 1 && !ours)
                {
                    Vec2 cover = ball + Unit(ownGoal - ball) * CoverGoalSide;
                    return new CpuPlan(CpuMode.Defend, Ease(cover - me));
                }

                if (sooner >= 1) return new CpuPlan(CpuMode.Support, Ease(ShapePlace(seen, config, ball, ours) - me));
            }

            bool second = false;
            if (mateSeen)
            {
                Meet(seen, config, me, seen.MaxSpeed, out double mineFirst);
                Meet(seen, config, mate, seen.MaxSpeed, out double mateFirst);
                second = mateFirst + 0.05 < mineFirst || (mateFirst <= mineFirst + 0.05 && mate.Y > me.Y);
            }

            if (mateSeen && second)
            {
                Vec2 place;
                if (mates <= HasBall && mates <= theirs)
                {
                    double across = mate.Y >= 0 ? -1 : 1;
                    double y = mate.Y + across * SupportWide;
                    double most = config.TouchlineY - 4;
                    // Alongside him, he breaks wide on a diagonal first: making for a place ahead of
                    // the carrier at the carrier's own speed, he would stay at his shoulder (#94's trials).
                    double ahead = Length(mate - me) < SupportWide / 2 ? me.X - mate.X + SupportBreak : SupportAhead;
                    place = new Vec2(mate.X + ahead, y > most ? most : y < -most ? -most : y);
                }
                else if (SecondOpponent(seen, opponent, out Vec2 marked))
                {
                    place = marked + Unit(ownGoal - marked) * MarkGoalSide;
                }
                else
                {
                    place = ball + Unit(ownGoal - ball) * SupportWide;
                }

                Vec2 toSupport = place - me;
                return new CpuPlan(CpuMode.Support, Length(toSupport) > Stand * 4 ? Unit(toSupport) : Vec2.Zero);
            }

            // Where he can first meet the ball, by its own flight, and whether the opponent meets it sooner.
            Vec2 meet = Meet(seen, config, me, seen.MaxSpeed, out double myTime);
            double theirTime = double.PositiveInfinity;
            if (opponentSeen) Meet(seen, config, opponent, seen.MaxSpeed, out theirTime);

            // On an opponent's ball he goes at where it will be as his challenge would reach it
            // (#84): the challenge goes the way he is running.
            // Beyond a standing challenge's range, with the ball nearer him than the man (#98): he
            // slides at where it will be partway through, from the side or in front, never through him.
            if (theirs <= HasBall && !seen.Kicking && mine > SlideOver * config.ChallengeRange && mine <= SlideFrom * config.SlideRange)
            {
                Vec2 away = opponent - me;
                if (mine + SlideClear < Length(away))
                {
                    Vec2 at = ball + opponentVelocity * (config.SlideTime * 0.5) - me;
                    return new CpuPlan(CpuMode.Approach, Unit(at), new KickIntent(at, 1, 0));
                }
            }

            if (theirs <= HasBall && mine <= Press)
            {
                Vec2 ahead = ball + seen.BallVelocity.Horizontal * config.ChallengeCommit;
                return new CpuPlan(CpuMode.Approach, Unit(ahead - me));
            }

            if (myTime <= theirTime || mine <= Press)
            {
                return new CpuPlan(myTime > Near ? CpuMode.SeekBall : CpuMode.Approach, Unit(meet - me));
            }

            // Out of possession and second to the ball: goal-side of it, on the line to his own goal.
            Vec2 spot = ball + Unit(ownGoal - ball) * GoalSide;
            // At full speed until he is there: the spot moves with the ball.
            Vec2 toSpot = spot - me;
            return new CpuPlan(CpuMode.Defend, Length(toSpot) > Stand * 2 ? Unit(toSpot) : Vec2.Zero);
        }

        // Whether the opponent nearest the ball is in front of it, toward the goal, within Evade.
        private static bool Pressed(Vec2 ball, Vec2 opponent, Vec2 goal)
        {
            Vec2 toGoal = Unit(goal - ball), toOpponent = opponent - ball;
            return toOpponent.X * toGoal.X + toOpponent.Y * toGoal.Y > -1 && toOpponent.LengthSquared < Evade * Evade;
        }

        private static bool IsOpen(OutfieldObservation seen, Vec2 mate)
        {
            foreach (SeenPlayer other in seen.Others)
            {
                if (!other.Teammate && (other.Position - mate).LengthSquared < Open * Open) return false;
            }

            return true;
        }

        private static bool InCone(double heading, Vec2 to)
        {
            double angle = SimMath.Atan2(to.Y, to.X) - heading;
            while (angle > System.Math.PI) angle -= 2 * System.Math.PI;
            while (angle < -System.Math.PI) angle += 2 * System.Math.PI;
            return (angle < 0 ? -angle : angle) <= PassCone;
        }

        // The opponent, not a keeper, other than the one nearest the ball.
        private static bool SecondOpponent(OutfieldObservation seen, Vec2 nearest, out Vec2 other)
        {
            other = Vec2.Zero;
            foreach (SeenPlayer p in seen.Others)
            {
                if (p.Teammate || p.Keeper || p.Position.Equals(nearest)) continue;
                other = p.Position;
                return true;
            }

            return false;
        }

        // His place in his side's shape (#96), in his own frame: a back row of half the rest, a middle
        // row of the others and one forward, each row spread across the pitch. It follows the ball
        // up and down and a little across, and steps up when his side has the ball.
        private static Vec2 ShapePlace(OutfieldObservation seen, SimConfig config, Vec2 ball, bool ours)
        {
            int n = seen.SideSize, slot = seen.Slot;
            int back = n / 2, middle = n - 1 - back;
            double x, y;
            if (slot >= n)
            {
                x = ForwardLine;
                y = 0;
            }
            else
            {
                bool inBack = slot <= back;
                int count = inBack ? back : middle, index = inBack ? slot - 1 : slot - back - 1;
                x = inBack ? BackLine : MiddleLine;
                y = count <= 1 ? 0 : (-1 + 2.0 * index / (count - 1)) * ShapeWidth;
            }

            x += ShapeFollow * ball.X + (ours ? ShapeStepUp : 0);
            y += ShapeDrift * ball.Y;
            double long_ = config.GoalLineX - ShapeInside, wide = config.TouchlineY - ShapeInside;
            return new Vec2(x > long_ ? long_ : x < -long_ ? -long_ : x, y > wide ? wide : y < -wide ? -wide : y);
        }

        // Where the ball will be, on his read of its flight, when a kick he decided on now would
        // connect: his reaction, then the wind-up. Whether that is in a band his body plays, the
        // feet's excepted, and within reach of where his run takes him by then (#94).
        private static bool AirContact(OutfieldObservation seen, SimConfig config, out Vec3 contact)
        {
            contact = seen.BallPosition;
            if (seen.BallPosition.Z <= config.KickMaxBallHeight && seen.BallVelocity.Z <= 0) return false;
            int lead = (int)System.Math.Round(seen.ReactionDelay * config.TickRate);
            if (lead < 1) lead = 1;
            lead += config.KickWindUpTicks;
            var path = new List<Vec3>();
            BallFlight.Predict(config, seen.BallPosition, seen.BallVelocity, seen.BallSpin, lead, path, bothGoals: seen.BothGoals);
            if (path.Count < lead - 1 || lead < 2) return false;
            contact = path[lead - 2];
            double z = contact.Z;
            bool header = z >= config.HeaderMinHeight && z <= config.HeaderMaxHeight;
            bool volley = !header && z > config.KickMaxBallHeight && z <= config.VolleyMaxHeight;
            if (!header && !volley) return false;
            Vec2 there = seen.Position + seen.Velocity * (lead * config.TickSeconds);
            double reach = AirReach * (header ? config.HeaderReach : config.KickReach);
            return (contact.Horizontal - there).LengthSquared <= reach * reach;
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
