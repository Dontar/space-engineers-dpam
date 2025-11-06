using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        float CalcRequiredGyroForce(IEnumerable<IMyGyro> gyros, float roll, int magnitude = 10) {
            if (gyros.Count() == 0)
                return 0;

            var gravityMagnitude = Gravity.Length();

            var blockSize = (Me.CubeGrid.GridSizeEnum == MyCubeSize.Large) ? 2.5f : 0.5f; // meters
            var min = Me.CubeGrid.Min;
            var max = Me.CubeGrid.Max;
            var size = (max - min + Vector3I.One) * blockSize;

            var width = size.X;
            var height = size.Y;
            // var length = size.Z;

            var pivotPoint = width / 2;
            var maxForce = gyros.Sum(g => g.CubeGrid.GridSizeEnum == MyCubeSize.Small ? 448000 : 3.36E+07) * pivotPoint;

            var force = Mass.PhysicalMass * gravityMagnitude * pivotPoint;

            var inertia = 1.0 / 12.0 * Mass.PhysicalMass * (width * width + height * height);
            var torqueAccel = inertia * Math.Abs(roll) * magnitude;

            return (float)(Math.Min(force + torqueAccel, maxForce) / maxForce);
        }

        bool OrientGridToMatrix(IEnumerable<IMyGyro> gyros, MatrixD alignTo, bool revert = false) {
            var myMatrix = Controller.WorldMatrix;
            var down = alignTo.Down;
            var forward = revert ? alignTo.Backward : alignTo.Forward;
            var roll = Math.Atan2(down.Dot(myMatrix.Right), down.Dot(myMatrix.Down));
            var pitch = Math.Atan2(down.Dot(myMatrix.Backward), down.Dot(myMatrix.Down));
            var yaw = Math.Atan2(forward.Dot(myMatrix.Right), forward.Dot(myMatrix.Forward));

            var power = CalcRequiredGyroForce(gyros, 30 * MathHelper.RPMToRadiansPerSecond, 5);

            Util.ApplyGyroOverride(pitch, yaw, -roll, power, gyros, Controller.WorldMatrix);
            return Math.Abs(roll) < 0.01 && Math.Abs(pitch) < 0.01 && Math.Abs(yaw) < 0.01;
        }

        MatrixD GetAlignedMatrix() {
            var forward = MyMatrix.Forward * 5;
            var gravity = -Gravity;
            return MatrixD.CreateWorld(MyMatrix.Translation, Vector3D.ProjectOnPlane(ref forward, ref gravity), gravity);
        }

        IEnumerable AlignToGravity() {
            var matrix = GetAlignedMatrix();
            while (!OrientGridToMatrix(Gyros, matrix))
                yield return null;
        }

        void ResetGyros() {
            foreach (var gyro in Gyros) {
                gyro.GyroOverride = false;
                gyro.GyroPower = 1;
                gyro.Yaw = 0;
                gyro.Pitch = 0;
                gyro.Roll = 0;
            }
        }

        bool MoveGridToPosition(Vector3D position, double speed, float minDistance, bool reset = true) {
            var toTarget = position - MyMatrix.Translation;
            var distance = toTarget.Length();
            if (distance < minDistance) {
                if (reset)
                    ResetThrusters(Thrusters.Values.SelectMany(v => v));
                return true;
            }

            var direction = Vector3D.Normalize(toTarget);
            var velocity = Velocities.LinearVelocity;

            var desiredVelocity = direction * speed;
            var velocityError = desiredVelocity - velocity;

            // Proportional gain (tune this value)
            double k = Util.NormalizeClamp(speed, 0.1, 30, 13, 1); // Try 1.0 to 5.0 for your grid

            // Subtract gravity to get the net required thrust
            var thrustVec = (velocityError * k - Gravity) * Mass.PhysicalMass;

            Controller.DampenersOverride = Thrusters.ContainsKey(Base6Directions.Direction.Down);

            foreach (var dir in Thrusters.Keys) {
                var thrusterArray = Thrusters[dir];
                var thrustDir = thrusterArray[0].WorldMatrix.Backward;

                var force = thrustVec.Dot(thrustDir);

                var totalMaxThrust = thrusterArray.Sum(t => t.MaxThrust);
                var thrustToApply = MathHelper.Clamp((float)force, 0, totalMaxThrust);

                foreach (var thruster in thrusterArray) {
                    var portion = thruster.MaxThrust / totalMaxThrust;
                    thruster.ThrustOverride = thrustToApply * portion;
                }
            }
            return false;
        }

        void ResetThrusters(IEnumerable<IMyThrust> thrusterArray) {
            foreach (var thruster in thrusterArray) {
                thruster.ThrustOverridePercentage = 0;
            }
            Controller.DampenersOverride = true;
        }

        Vector3D[,] GenerateWorkGridArray(MatrixD matrix, Vector3D min, Vector3D max, Vector3 cellSize) {
            var right = matrix.Right;
            var up = matrix.Up;
            var forward = matrix.Forward;

            cellSize += 1f;
            var T = MatrixD.Transpose(matrix);
            var localMin = Vector3D.TransformNormal(min - matrix.Translation, T);
            var localMax = Vector3D.TransformNormal(max - matrix.Translation, T);
            var sizeX = (int)Math.Floor((localMax.X - localMin.X) / cellSize.X);
            var sizeY = (int)Math.Floor((localMax.Y - localMin.Y) / cellSize.Y);

            var cells = new Vector3D[sizeY, sizeX];

            for (int iy = 0; iy < sizeY; iy++) {
                for (int ix = 0; ix < sizeX; ix++) {
                    var offset = right * (ix * cellSize.X + cellSize.X / 2)
                               + up * (iy * cellSize.Y + cellSize.Y / 2)
                               + forward * (cellSize.Z / 2);
                    cells[iy, ix] = min + offset;
                }
            }
            return cells;
        }

        IEnumerable<int[]> SpiralRoute(int sizeY, int sizeX) {
            var visited = new bool[sizeY, sizeX];

            int cy = sizeY % 2 == 1 ? sizeY / 2 : sizeY / 2 - 1;
            int cx = sizeX % 2 == 1 ? sizeX / 2 : sizeX / 2 - 1;

            // BFS spiral out
            var queue = new Queue<int[]>();
            queue.Enqueue(new[] { cy, cx });
            visited[cy, cx] = true;

            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };

            while (queue.Count > 0) {
                var point = queue.Dequeue();
                int iy = point[0], ix = point[1];
                yield return point;
                for (int d = 0; d < 4; d++) {
                    int nx = ix + dx[d], ny = iy + dy[d];
                    if (nx >= 0 && nx < sizeX && ny >= 0 && ny < sizeY && !visited[ny, nx]) {
                        visited[ny, nx] = true;
                        queue.Enqueue(new[] { ny, nx });
                    }
                }
            }
        }

        IEnumerable<int[]> RasterRoute(int sizeY, int sizeX) {
            for (int iy = sizeY - 1; iy >= 0; iy--) {
                for (int ix = 0; ix < sizeX; ix++) {
                    yield return new[] { iy, ix };
                }
            }
        }

        BoundingBox ProjectBoxFrontOfShip(MatrixD position, Vector3 size) {
            var forward = position.Forward;
            var up = position.Up;
            var right = position.Right;
            var pos = position.Translation;

            var halfSize = size / 2;

            var min = pos - right * halfSize.X - up * halfSize.Y;
            var max = pos + right * halfSize.X + up * halfSize.Y + forward * size.Z;

            return new BoundingBox(min, max);
        }

        IEnumerable GotoPosition(TransitionStages stage, Func<string, bool> waitForUndock = null, Func<string, bool> waitForDock = null) {
            CurrentJob.ShuttleStage = stage;
            return GotoPosition(waitForUndock, waitForDock);
        }

        IEnumerable GotoPosition(Func<string, bool> waitForUndock = null, Func<string, bool> waitForDock = null) {
            var currentPosition = MyMatrix.Translation;
            var job = CurrentJob;
            if ((CurrentJob.Path?.Count ?? 0) == 0)
                yield break;
            var path = new List<Waypoint>(CurrentJob.Path);

            if (new[] { TransitionStages.AtWork, TransitionStages.TransitionToHome }.Contains(job.ShuttleStage))
                path.Reverse();

            var last = path[path.Count - 1];
            Waypoint previous = path[0];
            Status.Destination = last;
            Status.Count = path.Count;

            var closestWaypoint = path.MinBy(w => (float)Vector3D.DistanceSquared(w.Matrix.Translation, MyMatrix.Translation));
            var pathIdx = path.IndexOf(closestWaypoint);
            if (pathIdx > 0) {
                var direction = Vector3D.Normalize(path[pathIdx].Matrix.Translation - currentPosition);
                var gravity = -Gravity;
                previous = new Waypoint(MatrixD.CreateWorld(currentPosition, Vector3D.ProjectOnPlane(ref direction, ref gravity), gravity), "Previous");
            }
            else
                pathIdx = 1;

            while (!waitForUndock?.Invoke(path[0].Name) ?? false)
                yield return null;

            Status.MinDistance = 0.25f;
            Status.Speed = 2;
            job.ShuttleStage = last.Name == "Work" ? TransitionStages.TransitionToWork : TransitionStages.TransitionToHome;
            for (var i = pathIdx; i < path.Count; i++) {
                var currentWaypoint = path[i];
                Status.Current = currentWaypoint;
                Status.Left = path.Count - 1 - i;
                while (!MoveGridToPosition(currentWaypoint.Matrix.Translation, Status.Speed, Status.MinDistance, false)) {
                    currentPosition = MyMatrix.Translation;
                    var distanceToLast = Vector3D.Distance(currentPosition, last.Matrix.Translation);
                    var distance = Math.Min(Vector3D.Distance(currentPosition, path[0].Matrix.Translation), distanceToLast);
                    if (distance < 200) {
                        Status.Speed = (float)Math.Max(2, Util.NormalizeValue(distance, 200, CurrentJob.Speed));
                        Status.MinDistance = (float)Math.Max(0.25f, Util.NormalizeValue(distance, 100, Dimensions.Width / 2));
                    }
                    var reverse = job.ShuttleStage == TransitionStages.TransitionToHome && distanceToLast > 50 && previous != path[0];
                    OrientGridToMatrix(Gyros, distanceToLast < 50 ? last.Matrix : previous.Matrix, reverse);
                    yield return null;
                }
                previous = currentWaypoint;
            }
            ResetGyros();
            ResetThrusters(Thrusters.Values.SelectMany(v => v));
            job.ShuttleStage = job.ShuttleStage == TransitionStages.TransitionToHome ? TransitionStages.AtHome : TransitionStages.AtWork;
            while (!waitForDock?.Invoke(last.Name) ?? false)
                yield return null;
        }

        bool WaitForUndock(string pos) {
            IMyShipConnector connector;
            if (IsConnectedOrReady(out connector)) {
                if (CheckConnectorCondition(pos)) {
                    connector.Disconnect();
                    OnUnDockTimers(pos);
                    return true;
                }
                return false;
            }
            return true;
        }

        bool WaitForDock(string pos) {
            IMyShipConnector connector;
            if (IsConnectedOrReady(out connector)) {
                connector.Connect();
                OnDockTimers(pos);
                return true;
            }
            return false;
        }

        bool CheckConnectorCondition(string pos) {
            if (BatteriesLevel < 15)
                return false;
            var condition = pos == "Home" ? CurrentJob.LeaveConnector1 : CurrentJob.LeaveConnector2;
            switch (condition) {
                case EventEnum.ShipIsEmpty:
                    return FillLevel < 0.1;
                case EventEnum.ShipIsFull:
                    return FillLevel > 98;
                case EventEnum.UndockCommand:
                    if (commandQueue.Peek() == "Undock") {
                        commandQueue.Dequeue();
                        return true;
                    }
                    break;
            }
            return false;
        }

        bool IsConnectedOrReady(out IMyShipConnector connector) {
            var connectable = new[] { MyShipConnectorStatus.Connectable, MyShipConnectorStatus.Connected };
            connector = Connectors.FirstOrDefault(c => connectable.Contains(c.Status));
            return connector != null;
        }

        void OnDockTimers(string pos) {
            var timer = pos == "Home" ? CurrentJob.TimerDockingHome : CurrentJob.TimerDockingWork;
            var action = pos == "Home" ? CurrentJob.TimerDockingHomeAction : CurrentJob.TimerDockingWorkAction;
            if (timer != "None") {
                var timerBlock = (IMyTimerBlock)GridTerminalSystem.GetBlockWithName(timer);
                if (timerBlock != null) {
                    if (action == TimerAction.Start)
                        timerBlock.StartCountdown();
                    else
                        timerBlock.Trigger();
                }
            }
        }

        void OnUnDockTimers(string pos) {
            var timer = pos == "Home" ? CurrentJob.TimerLeavingHome : CurrentJob.TimerLeavingWork;
            var action = pos == "Home" ? CurrentJob.TimerLeavingHomeAction : CurrentJob.TimerLeavingWorkAction;
            if (timer != "None") {
                var timerBlock = (IMyTimerBlock)GridTerminalSystem.GetBlockWithName(timer);
                if (timerBlock != null) {
                    if (action == TimerAction.Start)
                        timerBlock.StartCountdown();
                    else
                        timerBlock.Trigger();
                }
            }
        }

        void BalanceDrillInventories() {
            var targetMass = DrillInventories.Average(i => (float)i.CurrentMass);
            foreach (var inv in DrillInventories) {
                if ((float)inv.CurrentMass > targetMass) {
                    var toTransfer = (float)inv.CurrentMass - targetMass;
                    if (toTransfer <= 0)
                        continue;
                    List<MyInventoryItem> items = new List<MyInventoryItem>();
                    inv.GetItems(items);
                    foreach (var item in items) {
                        var amountToTransfer = MathHelper.Min((float)item.Amount, toTransfer);
                        var otherInv = DrillInventories.FirstOrDefault(i => i != inv && i.CanItemsBeAdded((MyFixedPoint)amountToTransfer, item.Type));
                        if (otherInv != null && inv.TransferItemTo(otherInv, item, (MyFixedPoint)amountToTransfer)) {
                            toTransfer -= amountToTransfer;
                            if (toTransfer <= 0)
                                break;
                        }
                    }
                }
            }
        }

        string PathToGps() {
            var gpsList = new List<string>();
            if ((CurrentJob.Path?.Count ?? 0) == 0)
                return "";
            foreach (var waypoint in CurrentJob.Path) {
                var pos = waypoint.Matrix.Translation;
                var gps = $"GPS:{waypoint.Name}:{pos.X}:{pos.Y}:{pos.Z}:";
                gpsList.Add(gps);
            }
            return string.Join(Environment.NewLine, gpsList);
        }
    }
}