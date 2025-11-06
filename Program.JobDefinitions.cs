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
                var forward = Vector3.Round(_Matrix.Forward, 2);
                var up = Vector3.Round(_Matrix.Up, 2);
                var pos = _Matrix.Translation;
                var str = string.Format("{{{0}}}{1}{2}", pos, forward, up);
                return $"{_Name}@{str}";
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
                if (string.IsNullOrEmpty(data))
                    return new List<Waypoint>();
                delimiter = string.IsNullOrEmpty(delimiter) ? Environment.NewLine : delimiter;
                return data.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries).Select(part => FromString(part)).Where(w => w != null).ToList();
            }

            static bool TryParse(string s, out string name, out MatrixD matrix) {
                matrix = default(MatrixD);
                name = null;

                var parts = s.Split('@');
                if (parts.Length < 2)
                    return false;

                name = parts[0];
                var success = true;
                var mat = parts[1].Trim('{', '}').Split(new[] { "}{" }, StringSplitOptions.None).Select(vec => {
                    Vector3D result = Vector3D.Zero;
                    success = success && Vector3D.TryParse(vec, out result);
                    return result;
                }).ToArray();
                if (!success || mat.Length < 3)
                    return false;

                matrix = MatrixD.CreateWorld(mat[0], mat[1], mat[2]);
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
        enum TransitionStages
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
            public TransitionStages ShuttleStage;
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

                ShuttleStage = (TransitionStages)Get(Name, "ShuttleStage").ToInt32((int)TransitionStages.None);

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
                try {
                    Load(data, name);
                } catch (Exception ex) {
                    Util.Echo($"Error loading job definition {ex.Message}" + Environment.NewLine + ex.StackTrace);
                }
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
