using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        class Waypoint : IMyAutopilotWaypoint
        {
            public MatrixD RelativeMatrix => _Matrix;
            public MatrixD Matrix => _Matrix;
            public long RelatedEntityId => 0;
            public string Name => _Name;
            MatrixD _Matrix;
            string _Name;

            public Waypoint(MatrixD matrix, string name) {
                _Name = name;
                _Matrix = matrix;
            }

            public override string ToString() {
                return $"{_Name}@{_Matrix}";
            }

            public static Waypoint FromString(string data) {
                string name;
                MatrixD matrix;
                if (TryParse(data, out name, out matrix)) {
                    return new Waypoint(matrix, name);
                }
                return null;
            }

            public static string ToData(IEnumerable<Waypoint> route, string delimiter = null) {
                delimiter = string.IsNullOrEmpty(delimiter) ? Environment.NewLine : delimiter;
                var sb = new StringBuilder();
                foreach (var wp in route) {
                    sb.Append(wp.ToString() + delimiter);
                }
                return sb.ToString();
            }

            public static List<Waypoint> FromData(string data, string delimiter = null) {
                var result = new List<Waypoint>();
                if (string.IsNullOrEmpty(data))
                    return result;
                delimiter = string.IsNullOrEmpty(delimiter) ? Environment.NewLine : delimiter;
                var parts = data.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts) {
                    result.Add(FromString(part));
                }
                return result;
            }

            static bool TryParse(string s, out string name, out MatrixD matrix) {
                name = null;
                matrix = default(MatrixD);

                int atIdx = s.IndexOf('@');
                if (atIdx < 0)
                    return false;

                name = s.Substring(0, atIdx);
                var matStr = s.Substring(atIdx + 1);

                // Remove outer braces and split into row groups
                var rows = matStr.Trim('{', '}', ' ').Split(new[] { "} {" }, StringSplitOptions.None);
                if (rows.Length != 4)
                    return false;

                var values = new double[16];
                int idx = 0;
                foreach (var row in rows) {
                    var parts = row.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts) {
                        var colonIdx = part.IndexOf(':');
                        if (colonIdx < 0)
                            continue;
                        var valStr = part.Substring(colonIdx + 1);
                        double val;
                        if (!double.TryParse(valStr, out val))
                            return false;
                        if (idx >= 16)
                            return false;
                        values[idx++] = val;
                    }
                }
                if (idx != 16)
                    return false;

                matrix = new MatrixD(
                    values[0], values[1], values[2], values[3],
                    values[4], values[5], values[6], values[7],
                    values[8], values[9], values[10], values[11],
                    values[12], values[13], values[14], values[15]
                );
                return true;
            }
        }
        #region mdk preserve
        enum DepthMode
        {
            Auto, Depth
        }
        enum StartPosition
        {
            TopLeft, Center
        }
        enum EventEnum
        {
            UndockCommand, ShipIsFull, ShipIsEmpty
        }
        enum TimerAction
        {
            Trigger, Start
        }
        enum JobType
        {
            None, MiningGrinding, Shuttle
        }
        enum MiningJobStages
        {
            None,
            TransitionToWork,
            TransitionToWorkLocation,
            TransitionToShaftStart,
            DigShaft,
            TransitionToHome,
            ThrowGarbage,
            Done
        }
        enum ShuttleStages
        {
            None,
            TransitionToHome,
            AtHome,
            TransitionToWork,
            AtWork
        }
        #endregion

        class JobDefinition : MyIni
        {
            public string Name;
            public Waypoint CurrentLocation;
            public bool Paused;
            public List<Waypoint> Path;
            public bool HasPath => Path != null && Path.Count > 0;
            public JobType Type;
            public float Speed;
            public float WorkSpeed;
            public float MinAltitude;
            public Vector3 Dimensions;
            public DepthMode DepthMode;
            public StartPosition StartPosition;
            public Waypoint WorkLocation;
            public MiningJobStages MiningJobStage;
            public int MiningJobProgress;
            public bool TerrainClear;
            public bool BalanceDrills;
            public ShuttleStages ShuttleStage;
            public EventEnum LeaveConnector1;
            public EventEnum LeaveConnector2;
            public string TimerDockingHome;
            public string TimerLeavingHome;
            public string TimerDockingWork;
            public string TimerLeavingWork;
            public TimerAction TimerDockingHomeAction;
            public TimerAction TimerLeavingHomeAction;
            public TimerAction TimerDockingWorkAction;
            public TimerAction TimerLeavingWorkAction;

            public void Load(string data, string name = "Default") {
                if (!TryParse(data))
                    return;
                Name = name;
                Paused = Get(Name, "Paused").ToBoolean(true);

                CurrentLocation = Waypoint.FromString(Get(Name, "CurrentLocation").ToString(""));

                var pathData = Get(Name, "Path").ToString("");
                Path = Waypoint.FromData(pathData, "|");

                Type = (JobType)Get(Name, "Type").ToInt32((int)JobType.None);
                Speed = Get(Name, "Speed").ToSingle(30f);
                WorkSpeed = Get(Name, "WorkSpeed").ToSingle(1f);
                MinAltitude = Get(Name, "MinAltitude").ToSingle(10f);

                var dimStr = Get(Name, "Dimensions").ToString("{X:10 Y:10 Z:10}");
                Vector3D dimVec;
                Vector3D.TryParse(dimStr.Trim('{', '}'), out dimVec);
                Dimensions = dimVec;

                DepthMode = (DepthMode)Get(Name, "DepthMode").ToInt32((int)DepthMode.Depth);
                StartPosition = (StartPosition)Get(Name, "StartPosition").ToInt32((int)StartPosition.TopLeft);

                var workLocStr = Get(Name, "WorkLocation").ToString("");
                WorkLocation = Waypoint.FromString(workLocStr);

                MiningJobStage = (MiningJobStages)Get(Name, "MiningJobStage").ToInt32((int)MiningJobStages.None);

                MiningJobProgress = Get(Name, "MiningJobProgress").ToInt32(0);

                TerrainClear = Get(Name, "TerrainClear").ToBoolean(false);
                BalanceDrills = Get(Name, "BalanceDrills").ToBoolean(true);

                ShuttleStage = (ShuttleStages)Get(Name, "ShuttleStage").ToInt32((int)ShuttleStages.None);

                LeaveConnector1 = (EventEnum)Get(Name, "LeaveConnector1").ToInt32((int)EventEnum.UndockCommand);
                LeaveConnector2 = (EventEnum)Get(Name, "LeaveConnector2").ToInt32((int)EventEnum.UndockCommand);

                TimerDockingHome = Get(Name, "TimerDockingHome").ToString("None");
                TimerLeavingHome = Get(Name, "TimerLeavingHome").ToString("None");
                TimerDockingWork = Get(Name, "TimerDockingWork").ToString("None");
                TimerLeavingWork = Get(Name, "TimerLeavingWork").ToString("None");

                TimerDockingHomeAction = (TimerAction)Get(Name, "TimerDockingHomeAction").ToInt32((int)TimerAction.Trigger);
                TimerLeavingHomeAction = (TimerAction)Get(Name, "TimerLeavingHomeAction").ToInt32((int)TimerAction.Trigger);
                TimerDockingWorkAction = (TimerAction)Get(Name, "TimerDockingWorkAction").ToInt32((int)TimerAction.Trigger);
                TimerLeavingWorkAction = (TimerAction)Get(Name, "TimerLeavingWorkAction").ToInt32((int)TimerAction.Trigger);
            }

            public string Save() {
                Set(Name, "CurrentLocation", CurrentLocation != null ? CurrentLocation.ToString() : "");
                Set(Name, "Paused", Paused);
                Set(Name, "Path", Path != null ? Waypoint.ToData(Path, "|") : "");
                Set(Name, "Type", (int)Type);
                Set(Name, "Speed", Speed);
                Set(Name, "WorkSpeed", WorkSpeed);
                Set(Name, "MinAltitude", MinAltitude);
                Set(Name, "Dimensions", Dimensions.ToString());
                Set(Name, "DeptMode", (int)DepthMode);
                Set(Name, "StartPosition", (int)StartPosition);
                Set(Name, "WorkLocation", WorkLocation?.ToString() ?? "");
                Set(Name, "MiningJobStage", (int)MiningJobStage);
                Set(Name, "MiningJobProgress", MiningJobProgress);
                Set(Name, "TerrainClear", TerrainClear);
                Set(Name, "BalanceDrills", BalanceDrills);
                Set(Name, "ShuttleStage", (int)ShuttleStage);
                Set(Name, "LeaveConnector1", (int)LeaveConnector1);
                Set(Name, "LeaveConnector2", (int)LeaveConnector2);
                Set(Name, "TimerDockingHome", TimerDockingHome);
                Set(Name, "TimerLeavingHome", TimerLeavingHome);
                Set(Name, "TimerDockingWork", TimerDockingWork);
                Set(Name, "TimerLeavingWork", TimerLeavingWork);
                Set(Name, "TimerDockingHomeAction", (int)TimerDockingHomeAction);
                Set(Name, "TimerLeavingHomeAction", (int)TimerLeavingHomeAction);
                Set(Name, "TimerDockingWorkAction", (int)TimerDockingWorkAction);
                Set(Name, "TimerLeavingWorkAction", (int)TimerLeavingWorkAction);
                return ToString();
            }

            public void Reset() {
                Load("", "Default");
            }

            public JobDefinition(string name, string data) {
                Load(data, name);
            }
        }

        JobDefinition CurrentJob;

        class JobStatus
        {
            public Waypoint Destination;
            public Waypoint Current;
            public int Count;
            public int Left;
            public double Speed;
            public float MinDistance;
            public int MiningRouteCount;
        }
        JobStatus Status = new JobStatus();

        MiningJobStages Stage {
            get {
                return CurrentJob.MiningJobStage;
            }
            set {
                CurrentJob.MiningJobStage = value;
            }
        }
    }
}
