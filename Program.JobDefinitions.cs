using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Windows.Markup;
using Sandbox.Game.Entities;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        class Waypoint : IMyAutopilotWaypoint
        {
            public MatrixD RelativeMatrix => MatrixD.CreateWorld(_RelativeMatrix[0], _RelativeMatrix[1], _RelativeMatrix[2]);
            public MatrixD Matrix => MatrixD.CreateWorld(Reference + _RelativeMatrix[0], _RelativeMatrix[1], _RelativeMatrix[2]);
            public long RelatedEntityId => _RelatedEntityId;
            public string Name => _Name;
            internal Vector3D Reference;
            long _RelatedEntityId = 0;
            string _Name;
            Vector3D[] _RelativeMatrix;

            public static IMyAutopilotWaypoint AddPoint(List<IMyAutopilotWaypoint> list, MatrixD point, string name) {
                var wp = new Waypoint(new[] { point.Translation, point.Forward, point.Up }, name);
                list.Add(wp);
                return wp;
            }

            public static IMyAutopilotWaypoint AddPoint(List<IMyAutopilotWaypoint> list, MatrixD point, Vector3D reference, string name, long relatedEntityId = 0) {
                var wp = new Waypoint(new[] { point.Translation - reference, point.Forward, point.Up }, name, reference, relatedEntityId);
                list.Add(wp);
                return wp;
            }

            public static IMyAutopilotWaypoint AddPoint(MatrixD point, string name) =>
                new Waypoint(new[] { point.Translation, point.Forward, point.Up }, name);

            public static IMyAutopilotWaypoint AddPoint(MatrixD point, Vector3D reference, string name, long relatedEntityId = 0) =>
                new Waypoint(new[] { point.Translation - reference, point.Forward, point.Up }, name, reference, relatedEntityId);

            Waypoint(Vector3D[] point, string name, Vector3D _reference = default(Vector3D), long relatedEntityId = 0) {
                _RelativeMatrix = point;
                Reference = _reference;
                _Name = name;
                _RelatedEntityId = relatedEntityId;
            }

            public static void SetReference(IEnumerable<IMyAutopilotWaypoint> list, Vector3D reference) {
                foreach (var wp in list) {
                    var w = wp as Waypoint;
                    w.Reference = reference;
                }
            }

            public override string ToString() {
                var forward = Vector3D.Round(_RelativeMatrix[1], 3);
                var up = Vector3D.Round(_RelativeMatrix[2], 3);
                var pos = _RelativeMatrix[0];
                return string.Format("{0}@{1},{2},{3}", _Name, pos, forward, up);
            }

            public static Waypoint FromString(string data, Vector3D reference = default(Vector3D)) {
                string name;
                Vector3D[] matrix;
                if (TryParse(data, out name, out matrix)) {
                    return new Waypoint(matrix, name, reference);
                }
                return null;
            }

            public static string ToData(IEnumerable<IMyAutopilotWaypoint> list, string delimiter = null) {
                delimiter = string.IsNullOrEmpty(delimiter) ? Environment.NewLine : delimiter;
                var sb = new StringBuilder();
                foreach (var wp in list) {
                    sb.Append(wp.ToString() + delimiter);
                }
                return sb.ToString();
            }

            public static List<IMyAutopilotWaypoint> FromData(string data, string delimiter = null, Vector3D reference = default(Vector3D)) {
                if (string.IsNullOrEmpty(data))
                    return new List<IMyAutopilotWaypoint>();
                delimiter = string.IsNullOrEmpty(delimiter) ? Environment.NewLine : delimiter;
                return new List<IMyAutopilotWaypoint>(data
                    .Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => FromString(part, reference))
                    .Where(w => w != null)
                );
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
            // Path related
            public string Name;
            public bool Paused;
            public List<IMyAutopilotWaypoint> Path;
            public string RelativeGrid;
            public bool HasPath => Path != null && Path.Count > 0;
            public float Speed;
            // mining
            public Vector3D CurrentLocation;
            public JobType Type;
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
            // shuttle
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

            public void Load() {
                Paused = Get(Name, "Paused").ToBoolean(true);

                CurrentLocation = Get(Name, "CurrentLocation").ToVector();

                Path = Waypoint.FromData(Get(Name, "Path").ToString(""), "|");

                RelativeGrid = Get(Name, "RelativeGrid").ToString("None");

                Type = (JobType)Get(Name, "Type").ToInt32((int)JobType.None);
                Speed = Get(Name, "Speed").ToSingle(30f);
                WorkSpeed = Get(Name, "WorkSpeed").ToSingle(1f);
                MinAltitude = Get(Name, "MinAltitude").ToSingle(10f);

                Dimensions = Get(Name, "Dimensions").ToVector();

                DepthMode = (DepthMode)Get(Name, "DepthMode").ToInt32((int)DepthMode.Depth);
                StartPosition = (StartPosition)Get(Name, "StartPosition").ToInt32((int)StartPosition.TopLeft);

                WorkLocation = Waypoint.FromString(Get(Name, "WorkLocation").ToString(""));

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
                Set(Name, "Path", Path != null ? Waypoint.ToData(Path, "|") : "");
                Set(Name, "RelativeGrid", RelativeGrid);
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
                Clear();
                Load();
            }

            public JobDefinition(string name, string data) {
                try {
                    Name = name;
                    TryParse(data);
                    Load();
                }
                catch (Exception ex) {
                    Util.Echo($"Error loading job definition {ex.Message}" + Environment.NewLine + ex.StackTrace);
                }
            }

            public IDictionary<string, string> Serialize() {
                Save();
                var toIgnore = new[] { "CurrentLocation", "Path", "WorkLocation", };
                var list = new List<MyIniKey>();
                GetKeys(Name, list);
                return list.Where(k => !toIgnore.Contains(k.Name)).ToDictionary(k => k.Name, k => Get(k).ToString());
            }

            public void Deserialize(IDictionary<string, string> data) {
                Save();
                foreach (var item in data) {
                    Set(Name, item.Key, item.Value);
                }
                Load();
            }
        }

        JobDefinition CurrentJob;

        class JobStatus
        {
            public IMyAutopilotWaypoint Destination;
            public IMyAutopilotWaypoint Current;
            public Vector3D Relation;
            public bool Recording;
            public bool HasDrills;
            public int PathCount;
            public bool HasPath;
            public int Count;
            public int Left;
            public double Speed;
            public float MinDistance;
            public int MiningRouteCount;

            public IDictionary<string, string> Serialize() {
                return new Dictionary<string, string>() {
                    { "Recording", Recording.ToString() },
                    { "HasDrills" , HasDrills.ToString() },
                    { "PathCount", PathCount.ToString() },
                    { "HasPath", HasPath.ToString() },
                    { "Count", Count.ToString() },
                    { "Left", Left.ToString() },
                    { "Speed", Speed.ToString() },
                    { "MinDistance", MinDistance.ToString() },
                    { "MiningRouteCount", MiningRouteCount.ToString() },
                };
            }
            public void Deserialize(IDictionary<string, string> data) {
                Recording = bool.Parse(data["Recording"]);
                HasDrills = bool.Parse(data["HasDrills"]);
                PathCount = int.Parse(data["PathCount"]);
                HasPath = bool.Parse(data["HasPath"]);
                Count = int.Parse(data["Count"]);
                Left = int.Parse(data["Left"]);
                Speed = double.Parse(data["Speed"]);
                MinDistance = float.Parse(data["MinDistance"]);
                MiningRouteCount = int.Parse(data["MiningRouteCount"]);
            }
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
            Vector3D result = def;
            Vector3D.TryParse(source.ToString("").Trim('{', '}'), out result);
            return result;
        }
    }
}
