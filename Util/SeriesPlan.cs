using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Freznel.FzAdditions.Util
{
    //A series laid out step by step the way it runs. Before each jump, during the recharge, the course is corrected
    //to point at the aim point, then the ship coasts until its cells hold the jump. Positions and velocities are
    //against the aim point and the target's velocity, in AU and AU per second, the frame a jump and its coasting move
    //the ship in. Gravity pulling the ship and its target differently isn't modelled, the corrections take it out.
    public class SeriesPlan
    {
        public enum StepKind { Coast, Turn, Burn, Jump }

        //Why the plan ends where it does
        public enum StopReason { OutOfEnergy, Arrived, NoCharge, NoThrust, NoFuel, JumpCap }

        public struct Step
        {
            public StepKind Kind;
            public double Duration; //Game seconds, none for a jump
            public double StartX;
            public double StartY;
            public double EndX;
            public double EndY;
            public double VelocityX; //At the start
            public double VelocityY;
            public double AccelerationX; //Of a burn, so its curve can be drawn
            public double AccelerationY;
            public bool Torch;
        }

        //Everything the plan starts from, read once so the plot and a running series work it out the same way
        public struct Input
        {
            public Ship Ship;
            public double RelativeX; //Of the ship from the aim point
            public double RelativeY;
            public double VelocityX; //Of the ship against the target
            public double VelocityY;
            public double AimX; //Where the aim point is now, to place burns for the no wake zones
            public double AimY;
            public double TargetVelocityX;
            public double TargetVelocityY;
            public double StopDistance;
            public double Energy;
            public double Need;
            public double Capacity;
            public double Held;
            public double FullMargin;
            public double FillRate;
            public double Strength;
            public double RcsAcceleration;
            public double RcsDeltaV; //What the RCS propellant has left
            public double TorchAcceleration; //0 without a torch
            public bool Torch;
            public double Rotation;
        }

        public readonly List<Step> Steps = new List<Step>();
        public int Jumps;
        public int FullJumps;
        public double JumpDistance; //In AU, along the jumps
        public double CoastDistance; //In AU, recharging and turning
        public double PathDistance; //In AU, the whole of it
        public double Time; //Game seconds until the last jump
        public double CorrectionDeltaV; //In AU per second
        public double RcsFuel; //What the corrections take from the RCS propellant
        public bool TorchUsed;
        public StopReason Stop;

        private double x;
        private double y;
        private double velocityX;
        private double velocityY;
        private double rotation;
        private double rcsLeft;
        private double sinceJump; //Game seconds of recharge since the last jump

        public static SeriesPlan Build(Input input)
        {
            SeriesPlan plan = new SeriesPlan
            {
                x = input.RelativeX,
                y = input.RelativeY,
                velocityX = input.VelocityX,
                velocityY = input.VelocityY,
                rotation = input.Rotation,
                rcsLeft = input.RcsDeltaV
            };
            plan.Run(input);
            return plan;
        }

        private void Run(Input input)
        {
            double energy = input.Energy;
            double held = input.Held;

            while (true)
            {
                if (energy < input.Need || energy <= 0.0) { Stop = StopReason.OutOfEnergy; return; }
                if (Jumps >= Constants.MaxSeriesJumps) { Stop = StopReason.JumpCap; return; }

                double jumpEnergy = Math.Min(energy, input.Capacity);
                if (!Correct(input)) return;

                double charge = Math.Min(held + input.FillRate * sinceJump, input.Capacity);
                double missing = Math.Max(jumpEnergy - input.FullMargin - charge, 0.0);
                if (missing > 0.0)
                {
                    if (input.FillRate <= 0.0) { Stop = StopReason.NoCharge; return; }
                    Coast(missing / input.FillRate, StepKind.Coast);
                }

                Jump(input, jumpEnergy);
                held = Math.Max(Math.Min(held + input.FillRate * sinceJump, input.Capacity) - jumpEnergy, 0.0);
                sinceJump = 0.0;
                energy -= jumpEnergy;
                Jumps++;
                if (jumpEnergy >= input.Capacity) FullJumps++;

                //A jump only moves forwards, so one that has gone past the aim point has arrived as near as it can
                if (JumpCorrection.HasArrived(x, y, velocityX, velocityY, input.StopDistance, afterJump: true)) { Stop = StopReason.Arrived; return; }
            }
        }

        //False if a correction is needed and can't be made, which ends the plan
        private bool Correct(Input input)
        {
            JumpCorrection.GetDeltaV(x, y, velocityX, velocityY, out double deltaX, out double deltaY);
            double deltaV = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (!JumpCorrection.IsNeeded(deltaV, Math.Sqrt(velocityX * velocityX + velocityY * velocityY))) return true;

            double nowX = input.AimX + input.TargetVelocityX * Time + x;
            double nowY = input.AimY + input.TargetVelocityY * Time + y;
            //The torch needs RCS to turn the ship
            if (input.RcsAcceleration <= 0.0) { Stop = StopReason.NoThrust; return false; }
            bool torch = input.Torch && input.TorchAcceleration > 0.0 && !JumpDrive.IsInNoWakeZone(nowX, nowY);

            double acceleration;
            if (torch)
            {
                double heading = JumpCorrection.GetHeading(deltaX, deltaY);
                Coast(JumpCorrection.GetTurnTime(Math.Abs(JumpCorrection.WrapAngle(heading - rotation))), StepKind.Turn);
                rotation = heading;
                acceleration = JumpCorrection.GetTorchBurnAcceleration(input.TorchAcceleration, deltaV);
                TorchUsed = true;
            }
            else
            {
                if (deltaV > rcsLeft) { Stop = StopReason.NoFuel; return false; }
                rcsLeft -= deltaV;
                RcsFuel += input.Ship.CalculateRCSFuelConsumption(deltaV);
                acceleration = input.RcsAcceleration;
            }

            Burn(deltaX, deltaY, deltaV, acceleration, torch);
            return true;
        }

        private void Coast(double seconds, StepKind kind)
        {
            if (seconds <= 0.0) return;

            Step step = Begin(kind, seconds);
            x += velocityX * seconds;
            y += velocityY * seconds;
            Finish(step);
            CoastDistance += Math.Sqrt(velocityX * velocityX + velocityY * velocityY) * seconds;
        }

        private void Burn(double deltaX, double deltaY, double deltaV, double acceleration, bool torch)
        {
            double seconds = deltaV / acceleration;
            Step step = Begin(StepKind.Burn, seconds);
            step.AccelerationX = deltaX / deltaV * acceleration;
            step.AccelerationY = deltaY / deltaV * acceleration;
            step.Torch = torch;

            x += velocityX * seconds + 0.5 * step.AccelerationX * seconds * seconds;
            y += velocityY * seconds + 0.5 * step.AccelerationY * seconds * seconds;
            velocityX += deltaX;
            velocityY += deltaY;
            Finish(step);
            CorrectionDeltaV += deltaV;
        }

        private void Jump(Input input, double energy)
        {
            double speed = Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
            double distance = JumpDrive.GetDistance(speed, JumpDrive.GetJumpTime(energy - input.Need, input.Strength));
            double directionX = speed > 0.0 ? velocityX / speed : 0.0;
            double directionY = speed > 0.0 ? velocityY / speed : 0.0;

            Step step = Begin(StepKind.Jump, 0.0);
            x += directionX * distance;
            y += directionY * distance;
            Finish(step);
            JumpDistance += distance;
        }

        private Step Begin(StepKind kind, double seconds)
        {
            Time += seconds;
            sinceJump += seconds;
            return new Step { Kind = kind, Duration = seconds, StartX = x, StartY = y, VelocityX = velocityX, VelocityY = velocityY };
        }

        private void Finish(Step step)
        {
            step.EndX = x;
            step.EndY = y;
            PathDistance += Math.Sqrt((step.EndX - step.StartX) * (step.EndX - step.StartX) + (step.EndY - step.StartY) * (step.EndY - step.StartY));
            Steps.Add(step);
        }
    }
}
