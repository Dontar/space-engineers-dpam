using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Timers;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Library.Utils;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        #region mdk preserve
        string _tag = "{DPAM}";
        string _ignoreTag = "{Ignore}";
        #endregion

        public Program() {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            Util.Init(this);
            CurrentJob = new JobDefinition("Default", Storage);
            MainMenu = new ControlMenu(this);
            InitController();
            Task.RunTask(Util.StatusMonitorTask(this));
            _LogoTask = Task.RunTask(Util.DisplayLogo("DPAM", Me.GetSurface(0))).Every(1.5f);
            Task.SetInterval((_) => RenderMenu(), 1.7f);
            _LocationTask = Task.SetInterval((_) => CurrentJob.CurrentLocation = new Waypoint(MyMatrix, "Current"), 2).Pause();
            ToggleMainTask(!CurrentJob.Paused);
        }

        ITask _LogoTask;
        ITask _MainTask;
        ITask _TransitionTask;
        ITask _LocationTask;
        ITask _AlignTask;
        MyCommandLine Cmd = new MyCommandLine();
        Queue<string> commandQueue = new Queue<string>();

        public void Main(string argument, UpdateType updateSource) {
            if (Controller == null) {
                Util.Echo("No controller found");
                return;
            }

            if (!string.IsNullOrEmpty(argument))
                try {
                    ExecuteCommand(argument);
                }
                catch (Exception e) {
                    Util.Echo($"Error executing command: {e.Message}" + Environment.NewLine + e.StackTrace);
                }

            if (!updateSource.HasFlag(UpdateType.Update10))
                return;

            Memo.Of("OnBaseMassChange", Mass.BaseMass, (OldBaseMass) => {
                if (OldBaseMass != 0)
                    InitController();
                InitBlocks();
            });

            Task.Tick(Runtime.TimeSinceLastRun);
            Memo.Tick(Runtime.TimeSinceLastRun);
        }

        public void Save() {
            Storage = CurrentJob.Save();
        }

        void ExecuteCommand(string argument) {
            var cmd = Cmd;
            if (!cmd.TryParse(argument))
                return;

            switch (cmd.Argument(0).ToLower()) {
                case "record":
                    if (cmd.Switches.Count > 0) {
                        if (cmd.Switch("start") && !Recording)
                            Task.RunTask(RecordPathTask()).Once().Every(1.7f);
                        if (cmd.Switch("stop"))
                            Recording = false;
                        return;
                    }
                    if (!Recording)
                        Task.RunTask(RecordPathTask()).Once().Every(1f);
                    else
                        Recording = false;
                    break;
                case "reset":
                    CurrentJob.Reset();
                    break;
                case "toggle":
                    if (cmd.Switches.Count > 0) {
                        if (cmd.Switch("start")) {
                            ToggleMainTask(true);
                            ToggleTransitionTask(null, false);
                        }
                        if (cmd.Switch("stop")) {
                            ToggleMainTask(false);
                            ToggleTransitionTask(null, false);
                        }
                    }
                    else {
                        if (Task.IsRunning(_TransitionTask)) {
                            ToggleTransitionTask(null, false);
                        }
                        else
                            ToggleMainTask(!Task.IsRunning(_MainTask));
                    }
                    break;
                case "undock":
                    commandQueue.Enqueue("Undock");
                    break;
                case "go_home":
                    ExecuteCommand("toggle -stop");
                    ToggleTransitionTask("Home", true);
                    break;
                case "go_work":
                    ExecuteCommand("toggle -stop");
                    ToggleTransitionTask("Work", true);
                    break;
                case "balance":
                    BalanceDrillInventories();
                    break;
                case "align":
                    if (!Task.IsRunning(_AlignTask))
                        _AlignTask = Task.RunTask(AlignToGravity()).Once().OnDone(() => ResetGyros());
                    else
                        Task.StopTask(_AlignTask);
                    break;
                case "debug":
                    var result = new StringBuilder(Storage);
                    result.AppendLine("---");
                    result.Append(PathToGps());
                    Me.CustomData = result.ToString();
                    break;
                default:
                    if (MainMenu.ProcessMenuCommands(Cmd))
                        RenderMenu();
                    break;
            }
        }

        IEnumerable MainTask() {
            switch (CurrentJob.Type) {
                case JobType.None:
                    yield break;
                case JobType.Shuttle:
                    var task = GotoPosition(
                        CurrentJob.CurrentDestination,
                        WaitForUndock,
                        WaitForDock
                    );

                    foreach (var _ in task)
                        yield return null;

                    CurrentJob.CurrentDestination = CurrentJob.CurrentPosition;
                    break;
                case JobType.MiningGrinding:
                    foreach (var _ in MiningTask()) {
                        yield return null;
                    }
                    break;
            }
        }

        IEnumerable MiningTask() {
            var matrix = CurrentJob.WorkLocation.Matrix;
            var pos = matrix.Translation;
            // Calculate the work area based on CurrentJob.Dimensions
            var forward = matrix.Forward;
            var workArea = ProjectBoxFrontOfShip(matrix, CurrentJob.Dimensions);
            // Calculate Grid coordinates
            var coordGrid = GenerateWorkGridArray(matrix, workArea.Min, workArea.Max, Dimensions.Size);
            var sizeY = coordGrid.GetLength(0);
            var sizeX = coordGrid.GetLength(1);
            var route = CurrentJob.StartPosition == StartPosition.Center ? SpiralRoute(sizeY, sizeX) : RasterRoute(sizeY, sizeX);
            Status.MiningRouteCount = route.Count();
            var path = route.Skip(CurrentJob.MiningJobProgress - 1).GetEnumerator();
            var start = Vector3D.Zero;
            var end = Vector3D.Zero;
            var isLast = !path.MoveNext();
            Sorters.ForEach(s => s.Enabled = false);
            ITask timer = null;

            var condition = new[] { MiningJobStages.TransitionToHome, MiningJobStages.TransitionToWork, MiningJobStages.None, MiningJobStages.Done };
            if (!condition.Contains(Stage) && CurrentJob.CurrentLocation != null) {
                var lastPos = CurrentJob.CurrentLocation.Matrix.Translation;
                var refDistance = Vector3D.Distance(lastPos, pos);
                var distance = Vector3D.Distance(MyMatrix.Translation, pos);
                if (distance > refDistance + 2) {
                    Stage = MiningJobStages.None;
                }
            }

            while (true) {
                if (!isLast) {
                    start = coordGrid[path.Current[0], path.Current[1]];
                    end = start + forward * (CurrentJob.DepthMode == DepthMode.Depth ? CurrentJob.Dimensions.Z : 50);
                }
                switch (Stage) {
                    case MiningJobStages.None:
                        Stage = MiningJobStages.TransitionToWork;
                        continue;
                    case MiningJobStages.TransitionToWork:
                        var goToWorkLocation = GotoPosition("Work", WaitForUndock);
                        foreach (var _ in goToWorkLocation)
                            yield return null;
                        Stage = MiningJobStages.TransitionToWorkLocation;
                        continue;
                    case MiningJobStages.TransitionToWorkLocation:
                        while (!MoveGridToPosition(pos, 5, 0.5f)) {
                            OrientGridToMatrix(Gyros, matrix);
                            yield return null;
                        }
                        Stage = MiningJobStages.TransitionToShaftStart;
                        continue;
                    case MiningJobStages.TransitionToShaftStart:
                        // go to start
                        while (!MoveGridToPosition(start, 5, 0.5f)) {
                            OrientGridToMatrix(Gyros, matrix);
                            yield return null;
                        }
                        Stage = MiningJobStages.DigShaft;
                        continue;
                    case MiningJobStages.DigShaft:

                        // start drills
                        Drills.ForEach(d => {
                            d.TerrainClearingMode = CurrentJob.TerrainClear;
                            d.Enabled = true;
                        });
                        // then start digging shaft
                        float previousCargo = 0;

                        if (!CurrentJob.TerrainClear && CurrentJob.DepthMode == DepthMode.Auto) {
                            previousCargo = OreAmount;
                            timer = Task.SetTimeout(() => { }, 5);
                        }

                        while (!MoveGridToPosition(end, CurrentJob.WorkSpeed, 0.25f)) {
                            OrientGridToMatrix(Gyros, matrix);

                            if (BatteriesLevel < 15) {
                                Stage = MiningJobStages.ThrowGarbage;
                                break;
                            }

                            if (CurrentJob.BalanceDrills)
                                BalanceDrillInventories();

                            if (!CurrentJob.TerrainClear) {
                                if (FillLevel > 98) {
                                    Stage = MiningJobStages.ThrowGarbage;
                                    break;
                                }

                                if (CurrentJob.DepthMode == DepthMode.Auto) {
                                    var currentCargo = OreAmount;
                                    if (timer != null && timer.Await()) {
                                        if (currentCargo != previousCargo) {
                                            timer = Task.SetTimeout(() => { }, 5);
                                            previousCargo = currentCargo;
                                        }
                                        else
                                            break;
                                    }
                                }
                            }
                            yield return null;
                        }
                        while (!MoveGridToPosition(start, 5, 0.5f)) {
                            OrientGridToMatrix(Gyros, matrix);
                            yield return null;
                        }
                        if (Stage == MiningJobStages.ThrowGarbage)
                            continue;
                        isLast = !path.MoveNext();
                        CurrentJob.MiningJobProgress++;
                        Stage = MiningJobStages.TransitionToShaftStart;
                        if (isLast)
                            Stage = MiningJobStages.Done;
                        continue;
                    case MiningJobStages.ThrowGarbage:
                        Drills.ForEach(d => d.Enabled = false);
                        while (!MoveGridToPosition(pos, 5, 0.5f)) {
                            OrientGridToMatrix(Gyros, matrix);
                            yield return null;
                        }
                        while (!OrientGridToMatrix(Gyros, GetAlignedMatrix()))
                            yield return null;
                        ResetGyros();

                        if (BatteriesLevel < 15) {
                            Stage = MiningJobStages.TransitionToHome;
                            continue;
                        }

                        if (Sorters.Count > 0) {
                            // Throw garbage
                            Sorters.ForEach(s => s.Enabled = true);
                            // Wait for garbage to be 0
                            var garbageAmount = GarbageAmount;
                            while (garbageAmount > 0) {
                                garbageAmount = GarbageAmount;
                                yield return null;
                            }
                            Sorters.ForEach(s => s.Enabled = false);
                        }
                        if (FillLevel > 98) {
                            Stage = MiningJobStages.TransitionToHome;
                            Sorters.ForEach(s => s.Enabled = false);
                            continue;
                        }
                        Stage = MiningJobStages.TransitionToShaftStart;
                        continue;
                    case MiningJobStages.TransitionToHome:
                        Drills.ForEach(d => d.Enabled = false);
                        var goToHome = GotoPosition("Home", null, WaitForDock);
                        foreach (var _ in goToHome)
                            yield return null;
                        Stage = MiningJobStages.None;
                        continue;
                    case MiningJobStages.Done:
                        Drills.ForEach(d => d.Enabled = false);
                        while (!MoveGridToPosition(pos, 5, 0.5f)) {
                            OrientGridToMatrix(Gyros, matrix);
                            yield return null;
                        }
                        foreach (var _ in GotoPosition("Home", null, WaitForDock))
                            yield return null;

                        ToggleMainTask(false);
                        Stage = MiningJobStages.None;
                        CurrentJob.MiningJobProgress = 0;
                        yield break;
                }
            }
        }

        void RenderMenu() {
            var pbScreen = Me.GetSurface(0);
            foreach (var s in Screens) {
                if (s == pbScreen)
                    _LogoTask.Pause();
                MainMenu.Render(s);
            }
        }

        bool Recording;
        IEnumerable RecordPathTask() {
            var minDistance = Me.CubeGrid.WorldVolume.Radius * 2;
            CurrentJob.Path.Clear();
            var counter = 0;
            MainMenu.ShowPathRecordMenu();
            Recording = true;

            var matrix = MyMatrix;
            CurrentJob.Path.Add(new Waypoint(matrix, $"Home"));
            var previous = matrix.Translation;

            while (Recording) {
                matrix = MyMatrix;
                var position = matrix.Translation;
                if (Vector3D.Distance(position, previous) > minDistance) {
                    CurrentJob.Path.Add(new Waypoint(matrix, $"Waypoint#{counter++}"));
                }
                previous = position;
                yield return null;
            }

            CurrentJob.Path.Add(new Waypoint(MyMatrix, $"Work"));
            CurrentJob.CurrentDestination = "Home";
        }

        void ToggleMainTask(bool start) {
            if (start) {
                if (!Task.IsRunning(_MainTask) && CurrentJob.Type != JobType.None) {
                    ToggleTransitionTask("", false);
                    _LocationTask.Pause(false);
                    if (CurrentJob.Type == JobType.Shuttle)
                        MainMenu.ShowTransitionMenu();
                    else
                        MainMenu.ShowMiningMenu();
                    _MainTask = Task.RunTask(MainTask()).OnDone(() => {
                        MainMenu.Back();
                        if (CurrentJob.Type == JobType.MiningGrinding)
                            Drills.ForEach(d => d.Enabled = false);
                        ResetThrusters(Thrusters.Values.SelectMany(t => t));
                        ResetGyros();
                        _LocationTask.Pause();
                    });
                }
            }
            else
                Task.StopTask(_MainTask);
            CurrentJob.Paused = !Task.IsRunning(_MainTask);
        }

        void ToggleTransitionTask(string name, bool start) {
            if (start) {
                if (!Task.IsRunning(_TransitionTask)) {
                    ToggleMainTask(false);
                    CurrentJob.CurrentDestination = name;
                    MainMenu.ShowTransitionMenu();
                    _TransitionTask = Task.RunTask(GotoPosition(name, (pos) => {
                        IMyShipConnector connector;
                        if (IsConnectedOrReady(out connector)) {
                            connector.Disconnect();
                            OnUnDockTimers(pos);
                        }
                        return true;
                    }, WaitForDock)).Once()
                        .OnDone(() => {
                            CurrentJob.CurrentDestination = name == "Home" ? "Work" : "Home";
                            MainMenu.Back();
                            ResetThrusters(Thrusters.Values.SelectMany(t => t));
                            ResetGyros();
                        });
                }
            }
            else
                Task.StopTask(_TransitionTask);
        }
    }
}
