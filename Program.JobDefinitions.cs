using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        class Waypoints : List<IMyAutopilotWaypoint>
        {
            public struct WP : IMyAutopilotWaypoint
            {
                public MatrixD RelativeMatrix => MatrixD.CreateWorld(posOrient[0], posOrient[1], posOrient[2]);
                public MatrixD Matrix => MatrixD.CreateWorld(reference + posOrient[0], posOrient[1], posOrient[2]);
                public long RelatedEntityId => relatedEntityId;
                public string Name => name;
                string name;
                Vector3D[] posOrient;
                Vector3D reference;
                long relatedEntityId;

                public WP(string name, Vector3D[] position, Vector3D reference = default(Vector3D)) {
                    this.name = name;
                    posOrient = position;
                    this.reference = reference;
                    relatedEntityId = 0;
                }
                public static WP FromString(string line, Vector3D reference = default(Vector3D)) {
                    string name;
                    Vector3D[] matrix;
                    TryParse(line, out name, out matrix);
                    return new WP(name, matrix, reference);
                }
                public override string ToString() {
                    var relativeMatrix = RelativeMatrix;
                    var forward = Vector3D.Round(relativeMatrix.Forward, 3);
                    var up = Vector3D.Round(relativeMatrix.Up, 3);
                    var pos = relativeMatrix.Translation;
                    return string.Format("{0}@{1},{2},{3}", Name, pos, forward, up);
                }
            }

            Vector3D Reference = Vector3D.Zero;

            public void AddPoint(string name, MatrixD position, Vector3D reference) {
                Reference = reference;
                var wp = new WP(name, position.ToArray(reference), reference);
                Add(wp);
            }

            public override string ToString() {
                var sb = new StringBuilder();
                sb.AppendFormat("Reference:{0}{1}", Reference, "|");
                foreach (var wp in this) {
                    sb.AppendFormat("{0}{1}", wp.ToString(), "|");
                }
                return sb.ToString();
            }

            public static Waypoints FromString(string data) {
                var waypoints = new Waypoints();
                Vector3D reference = Vector3D.Zero;
                var lines = data.Split(new[] { "|" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines) {
                    if (line.StartsWith("Reference:")) {
                        var refStr = line.Substring("Reference:".Length);
                        Vector3D.TryParse(refStr, out reference);
                    }
                    else
                        waypoints.Add(WP.FromString(line, reference));
                }
                return waypoints;
            }

            static bool TryParse(string s, out string name, out Vector3D[] matrix) {
                matrix = default(Vector3D[]);
                name = null;

                var parts = s.Split('@');
                if (parts.Length < 2)
                    return false;

                name = parts[0];
                var success = true;
                var mat = parts[1].Split(',').Select(vec => {
                    Vector3D result = Vector3D.Zero;
                    success = success && Vector3D.TryParse(vec, out result);
                    return result;
                }).ToArray();
                if (!success || mat.Length < 3)
                    return false;

                matrix = mat;
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
            public Vector3D CurrentLocation;
            public bool Paused;
            public Waypoints Path;
            // public Vector3D PathReference;
            public bool HasPath => Path != null && Path.Count > 0;
            public JobType Type;
            public float Speed;
            public float WorkSpeed;
            public float MinAltitude;
            public Vector3 Dimensions;
            public DepthMode DepthMode;
            public StartPosition StartPosition;
            public IMyAutopilotWaypoint WorkLocation;
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

                CurrentLocation = Get(Name, "CurrentLocation").ToVector();

                Path = Waypoints.FromString(Get(Name, "Path").ToString(""));

                Type = (JobType)Get(Name, "Type").ToInt32((int)JobType.None);
                Speed = Get(Name, "Speed").ToSingle(30f);
                WorkSpeed = Get(Name, "WorkSpeed").ToSingle(1f);
                MinAltitude = Get(Name, "MinAltitude").ToSingle(10f);

                Dimensions = Get(Name, "Dimensions").ToVector();

                DepthMode = (DepthMode)Get(Name, "DepthMode").ToInt32((int)DepthMode.Depth);
                StartPosition = (StartPosition)Get(Name, "StartPosition").ToInt32((int)StartPosition.TopLeft);

                WorkLocation = Waypoints.WP.FromString(Get(Name, "WorkLocation").ToString(""));

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
                Set(Name, "CurrentLocation", CurrentLocation.ToString());
                Set(Name, "Paused", Paused);
                Set(Name, "Path", Path != null ? Path.ToString() : "");
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
                }
                catch (Exception ex) {
                    Util.Echo($"Error loading job definition {ex.Message}" + Environment.NewLine + ex.StackTrace);
                }
            }
        }

        JobDefinition CurrentJob;

        class JobStatus
        {
            public IMyAutopilotWaypoint Destination;
            public IMyAutopilotWaypoint Current;
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
    public static class MyIniValueExtension
    {
        public static Vector3D ToVector(this MyIniValue source, Vector3D def = default(Vector3D)) {
            Vector3D result;
            return Vector3D.TryParse(source.ToString("").Trim('{', '}'), out result) ? result : def;
        }

        public static Vector3D[] ToArray(this MatrixD source, Vector3D reference = default(Vector3D)) {
            return new[] { source.Translation - reference, source.Forward, source.Up };
        }
    }
}
