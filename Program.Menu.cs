using System;
using System.Collections.Generic;
using System.Linq;
using ParallelTasks;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        ControlMenu MainMenu;
        class ControlMenu : MenuManager
        {
            public ControlMenu(Program program) : base(program) {
                var p = program;
                var menu = CreateMenu("DPAM Control");
                menu.AddArray(new[] {
                    new Item("Record path & set home/work", () => {
                        ShowPathRecordMenu();
                    }),
                    new Item("Setup mining/grinding job", () => ShowMiningJobMenu("Mining/Grinding"), () => (!p.CurrentJob.HasPath && p.HasDrills) || !p.HasDrills),
                    new Item("Setup shuttle job", () => ShowShuttleJobMenu(), () => !p.CurrentJob.HasPath),
                    new Item("Start/Stop", () => p.ExecuteCommand("toggle"), () => !p.CurrentJob.HasPath),
                    new Item("Go to Home", () => p.ExecuteCommand("go_home"), () => !p.CurrentJob.HasPath),
                    new Item("Go to Work", () => p.ExecuteCommand("go_work"), () => !p.CurrentJob.HasPath),
                    new Item("Behavior settings", () => ShowBehaviorMenu()),
                    new Item("Help", () => { }),
                });
            }

            T UpDownValue<T>(T[] array, T currentValue, int down) {
                return array[(Array.IndexOf(array, currentValue) + down + (down < 0 ? array.Length : 0)) % array.Length];
            }

            Menu PathRecordMenu;
            public void ShowPathRecordMenu() {
                if (PathRecordMenu != null) {
                    if (!menuStack.Contains(PathRecordMenu))
                        menuStack.Push(PathRecordMenu);
                    return;
                }
                var p = program;
                var job = p.CurrentJob;
                PathRecordMenu = CreateMenu("Path Recording");
                PathRecordMenu.AddArray(new[] {
                    new Item("Recording", () => p.Recording ? "Yes":"No", null),
                    Item.Separator,
                    new Item("Start recording", () => p.ExecuteCommand("record -start")),
                    new Item("Stop recording", () => p.ExecuteCommand("record -stop")),
                    new Item("Home", () => job.HasPath ? "Was set" : "None", null),
                    new Item("Work", () => job.HasPath && !p.Recording ? "Was set" : "None", null),
                    new Item("Path", () => $"Count: {job.Path?.Count ?? 0}", null),
                });
            }

            void ShowBehaviorMenu() {
                var p = program;
                var menu = CreateMenu("Behavior settings");
                menu.AddArray(new[] {
                    new Item("Speed limit", () => $"{p.CurrentJob.Speed} m/s", (down) => {
                        p.CurrentJob.Speed = Math.Max(10, p.CurrentJob.Speed + down);
                    }),
                    new Item("Min. altitude", () => $"{p.CurrentJob.MinAltitude} m", (down) => {
                        p.CurrentJob.MinAltitude = Math.Max(0, p.CurrentJob.MinAltitude + down);
                    }),
                    new  Item("When done", () => { }),
                    new  Item("On damage", () => { }),
                    new  Item("Weight limit", () => { }),
                    new  Item("Toggle sorters", () => { }),
                    new  Item("  -Ejection pos", () => { }),
                });
            }

            void ShowMiningJobMenu(string jobType) {
                var p = program;
                var modes = (DepthMode[])Enum.GetValues(typeof(DepthMode));
                var positions = (StartPosition[])Enum.GetValues(typeof(StartPosition));
                var events = (EventEnum[])Enum.GetValues(typeof(EventEnum));
                var timers = new[] { "None" }.Concat(Util.GetBlocks<IMyTimerBlock>().Select(b => b.CustomName)).ToArray();
                var timerActions = Enum.GetValues(typeof(TimerAction)) as TimerAction[];
                var job = p.CurrentJob;
                var menu = CreateMenu($"{jobType} Job Setup");
                menu.AddArray(new[] {
                    new Item("Start job", () => {//1
                        p.CurrentJob.Type = JobType.MiningGrinding;
                        p.CurrentJob.WorkLocation = new Waypoint(p.MyMatrix, "WorkLocation");
                        p.ExecuteCommand("toggle -start");
                    }),
                    new Item("Reset!!!", () => {//2
                        p.ExecuteCommand("reset");
                    }),
                    new Item("Leave Home", () => $"{job.LeaveConnector1}", (down) => job.LeaveConnector1 = UpDownValue(events, job.LeaveConnector1, down)),//3
                    new Item("Timer: \"Docking Home\"", () => job.TimerDockingHome, (down) => {//4
                        job.TimerDockingHome = UpDownValue(timers, job.TimerDockingHome, down);
                        menu[5].Hidden = job.TimerDockingHome == "None";
                    }),
                    new Item("  -Action", () => $"{job.TimerDockingHomeAction}", (down) => job.TimerDockingHomeAction = UpDownValue(timerActions, job.TimerDockingHomeAction, down), true),//5
                    new Item("Timer: \"Leaving Home\"", () => job.TimerLeavingHome, (down) => {//6
                        job.TimerLeavingHome = UpDownValue(timers, job.TimerLeavingHome, down);
                        menu[7].Hidden = job.TimerLeavingHome == "None";
                    }),
                    new Item("  -Action", () => $"{job.TimerLeavingHomeAction}", (down) => job.TimerLeavingHomeAction = UpDownValue(timerActions, job.TimerLeavingHomeAction, down), true),//7
                    new Item("Width", () => $"{job.Dimensions.X}m", (down) => {//8
                        job.Dimensions.X = Math.Max(1, job.Dimensions.X + down);
                        p.SetSensorDimensions(job.Dimensions);
                    }),
                    new Item("Height", () => $"{job.Dimensions.Y}m", (down) => {
                        job.Dimensions.Y = Math.Max(1, job.Dimensions.Y + down);
                        p.SetSensorDimensions(job.Dimensions);
                    }),
                    new Item("Depth", () => $"{job.Dimensions.Z}m", (down) => {
                        job.Dimensions.Z = Math.Max(1, job.Dimensions.Z + down);
                        p.SetSensorDimensions(job.Dimensions);
                    }),
                    new Item("Balance drills inv.", () => job.BalanceDrills ? "Yes" : "No", (down) => job.BalanceDrills = !job.BalanceDrills),
                    new Item("Terrain Clear", () => job.TerrainClear ? "Yes" : "No", (down) => job.TerrainClear = !job.TerrainClear),
                    new Item("Depth Mode", () => $"{job.DepthMode}", (down) => job.DepthMode = UpDownValue(modes, job.DepthMode, down)),
                    new Item("Start Pos", () => $"{job.StartPosition}", (down) => job.StartPosition = UpDownValue(positions, job.StartPosition, down)),
                });
            }

            void ShowShuttleJobMenu() {
                var p = program;
                var events = (EventEnum[])Enum.GetValues(typeof(EventEnum));
                var timerActions = Enum.GetValues(typeof(TimerAction)) as TimerAction[];
                var timers = new[] { "None" }.Concat(Util.GetBlocks<IMyTimerBlock>().Select(b => b.CustomName)).ToArray();
                var job = p.CurrentJob;
                var menu = CreateMenu("Shuttle Job Setup");
                menu.AddArray(new[] {
                    new Item("Start job", () => {
                        p.CurrentJob.Type = JobType.Shuttle;
                        p.ExecuteCommand("toggle -start");
                    }),
                    new Item("Leave Home Connector", () => $"{job.LeaveConnector1}", (down) => job.LeaveConnector1 = UpDownValue(events, job.LeaveConnector1, down)),
                    new Item("Leave Work Connector", () => $"{job.LeaveConnector2}", (down) => job.LeaveConnector2 = UpDownValue(events, job.LeaveConnector2, down)),
                    new Item("Timer \"Docking Home\"", () => job.TimerDockingHome, (down) => {
                        job.TimerDockingHome = UpDownValue(timers, job.TimerDockingHome, down);
                        menu[5].Hidden = job.TimerDockingHome == "None";
                    }),
                    new Item("  -Action", () => $"{job.TimerDockingHomeAction}", (down) => job.TimerDockingHomeAction = UpDownValue(timerActions, job.TimerDockingHomeAction, down), true),
                    new Item("Timer \"Leaving Home\"", () => job.TimerLeavingHome, (down) => {
                        job.TimerLeavingHome = UpDownValue(timers, job.TimerLeavingHome, down);
                        menu[7].Hidden = job.TimerLeavingHome == "None";
                    }),
                    new Item("  -Action", () => $"{job.TimerLeavingHomeAction}", (down) => job.TimerLeavingHomeAction = UpDownValue(timerActions, job.TimerLeavingHomeAction, down), true),
                    new Item("Timer \"Docking Work\"", () => job.TimerDockingWork, (down) => {
                        job.TimerDockingWork = UpDownValue(timers, job.TimerDockingWork, down);
                        menu[10].Hidden = job.TimerDockingWork == "None";
                    }),
                    new Item("  -Action", () => $"{job.TimerDockingWorkAction}", (down) => job.TimerDockingWorkAction = UpDownValue(timerActions, job.TimerDockingWorkAction, down), true),
                    new Item("Timer \"Leaving Work\"", () => job.TimerLeavingWork, (down) => {
                        job.TimerLeavingWork = UpDownValue(timers, job.TimerLeavingWork, down);
                        menu[13].Hidden = job.TimerLeavingWork == "None";
                    }),
                    new Item("  -Action", () => $"{job.TimerLeavingWorkAction}", (down) => job.TimerLeavingWorkAction = UpDownValue(timerActions, job.TimerLeavingWorkAction, down), true),
                });
            }

            Menu TransitionMenu;
            public void ShowTransitionMenu() {
                if (TransitionMenu != null) {
                    if (!menuStack.Contains(TransitionMenu))
                        menuStack.Push(TransitionMenu);
                    return;
                }
                var p = program;
                var status = p.Status;
                TransitionMenu = CreateMenu("Transitioning...", false);
                TransitionMenu.AddArray(new[] {
                    new Item("Abort", () => {
                        p.ExecuteCommand("toggle -stop");
                        Back();
                    }),
                    new Item("Destination", () => $"{status.Destination?.Name ?? ""}", null),
                    new Item("Speed", () => $"{p.Velocities.LinearVelocity.Length():F1} m/s", null),
                    new Item("Distance", () => {
                        var distance = Vector3D.Distance(p.MyMatrix.Translation, status.Destination?.Matrix.Translation ?? Vector3D.Zero);
                        return $"{distance:F1} m";
                    }, null),
                    new Item("Min speed", () => $"{status.Speed} m/s", null),
                    new Item("Min distance", () => $"{status.MinDistance} m", null),
                    new Item("Cur. Waypoint", () => $"{status.Current?.Name ?? ""}", null),
                    new Item("Waypoints", () => $"{status.Count}", null),
                    new Item("Waypoints Left", () => $"{status.Left}", null),
                    new Item("Cargo", () => $"{p.FillLevel:F1} %", null),

                });
            }
        }
    }
}
