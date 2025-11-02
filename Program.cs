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
            Task.RunTask(RenderMenuTask()).Every(1.7f);
            if (!CurrentJob.Paused) {
                _MainTask = Task.RunTask(MainTask());
            }
        }

        ITask _LogoTask;
        ITask _MainTask;
        ITask _TransitionTask;
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
                    Util.Echo($"Error executing command: {e.Message}");
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
                    ToggleTransitionTask("Home", true);
                    break;
                case "go_work":
                    ToggleTransitionTask("Work", true);
                    break;
                case "debug":
                    var result = new StringBuilder(Storage);
                    result.AppendLine("---");
                    result.Append(PathToGps());
                    Me.CustomData = result.ToString();
                    break;
                case "test_1":
                    if (!Task.IsRunning(_TestTask))
                        _TestTask = Task.RunTask(TestTask(CurrentJob.Path[0].Matrix, false));
                    else {
                        Task.StopTask(_TestTask);
                        ResetGyros();
                    }
                    break;
                case "test_2":
                    if (!Task.IsRunning(_TestTask))
                        _TestTask = Task.RunTask(TestTask(CurrentJob.Path[0].Matrix, true));
                    else {
                        Task.StopTask(_TestTask);
                        ResetGyros();
                    }
                    break;
                default:
                    if (MainMenu.ProcessMenuCommands(Cmd))
                        RenderMenu();
                    break;
            }
        }

        ITask _TestTask;
        IEnumerable TestTask(MatrixD matrix, bool reverse) {
            while (true) {
                OrientGridToMatrix(Gyros, matrix, reverse);
                yield return null;
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
            var path = (CurrentJob.StartPosition == StartPosition.Center ? SpiralRoute(sizeY, sizeX) : RasterRoute(sizeY, sizeX)).GetEnumerator();
            var start = Vector3D.Zero;
            var end = Vector3D.Zero;
            var isLast = false;
            while (true) {
                if (!isLast) {
                    start = coordGrid[path.Current[0], path.Current[1]];
                    end = start + forward * (CurrentJob.DepthMode == DepthMode.Depth ? CurrentJob.Dimensions.Z : 50);
                }
                if (CurrentJob.BalanceDrills)
                    BalanceDrillInventories();
                switch (Stage) {
                    case MiningJobStages.None:
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
                        var previousCargo = GetInventoryItemsAmountsWithoutGarbage();
                        var unchangedTicks = 0;
                        const int maxUnchangedTicks = 20; // 2 seconds at Update10

                        while (!MoveGridToPosition(end, 2, 0.5f)) {
                            OrientGridToMatrix(Gyros, matrix);

                            var currentCargo = GetInventoryItemsAmountsWithoutGarbage();
                            if (FillLevel > 98) {
                                Stage = MiningJobStages.ThrowGarbage;
                                break;
                            }

                            if (CurrentJob.DepthMode == DepthMode.Auto) {
                                if (Math.Abs(currentCargo - previousCargo) < 0.01f) {
                                    unchangedTicks++;
                                    if (unchangedTicks >= maxUnchangedTicks) {
                                        // Shaft exhausted, move to next
                                        break;
                                    }
                                }
                                else {
                                    unchangedTicks = 0;
                                }
                                previousCargo = currentCargo;
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
                        // Throw garbage
                        Sorters.ForEach(s => s.Enabled = true);
                        // if there is no some free space after 5 sec go to home to unload.
                        var timer = Task.SetTimeout(() => { }, 5);
                        while (Task.IsRunning(timer))
                            yield return null;
                        if (FillLevel > 98) {
                            Stage = MiningJobStages.TransitionToHome;
                            Sorters.ForEach(s => s.Enabled = false);
                            continue;
                        }
                        // else wait until cargo is same for 2 sec
                        timer = Task.SetTimeout(() => { }, 2);
                        var previousFill = FillLevel;
                        while (Task.IsRunning(timer)) {
                            if (Math.Abs(FillLevel - previousFill) > 0.1f) {
                                previousFill = FillLevel;
                                timer = Task.SetTimeout(() => { }, 2);
                            }
                            yield return null;
                        }
                        Sorters.ForEach(s => s.Enabled = false);
                        Stage = MiningJobStages.TransitionToShaftStart;
                        continue;
                    case MiningJobStages.TransitionToHome:
                        Drills.ForEach(d => d.Enabled = false);
                        var goToHome = GotoPosition("Home", null, WaitForDock);
                        foreach (var _ in goToHome)
                            yield return null;
                        Stage = MiningJobStages.TransitionToWork;
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
        IEnumerable RenderMenuTask() {
            while (true) {
                RenderMenu();
                yield return null;
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
                if (!Task.IsRunning(_MainTask)) {
                    ToggleTransitionTask("", false);
                    _MainTask = Task.RunTask(MainTask());
                }
            }
            else {
                Task.StopTask(_MainTask);
                ResetThrusters(Thrusters.Values.SelectMany(t => t));
                ResetGyros();
            }
            CurrentJob.Paused = !Task.IsRunning(_MainTask);
        }

        void ToggleTransitionTask(string name, bool start) {
            if (start) {
                if (!Task.IsRunning(_TransitionTask)) {
                    ToggleMainTask(false);
                    CurrentJob.CurrentDestination = name;
                    _TransitionTask = Task.RunTask(GotoPosition(name)).Once()
                        .OnDone(() => CurrentJob.CurrentDestination = name == "Home" ? "Work" : "Home");
                }
            }
            else {
                Task.StopTask(_TransitionTask);
                ResetThrusters(Thrusters.Values.SelectMany(t => t));
                ResetGyros();
            }
        }
    }
}
