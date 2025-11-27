using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
            JobDefinition Job => program.CurrentJob;
            JobStatus Stat => program.Status;
            Comms Comm => program.Comm;
            bool IsController;
            long CurrentShip;
            public ControlMenu(Program program, bool isController) : base(program) {
                IsController = isController;
                if (isController)
                    ShowControllerMenu();
                else
                    ShowMainMenu();
            }

            void ShowControllerMenu() {
                var p = program;
                var menu = CreateMenu("DPAM Controller");
                Task.SetInterval(_ => {
                    Comm.SendBroadcast(CmdGetShipNames, p.Me.CubeGrid.CustomName).Then<IDictionary<long, string>>(ships => {
                        var shipMenus = ships.Select(ship => new Item(ship.Value, () => {
                            CurrentShip = ship.Key;
                            Promise.All(new[] {
                                Comm.SendCommand(CurrentShip, CmdGetDef),
                                Comm.SendCommand(CurrentShip, CmdGetStat)
                            }).Then<IDictionary<string, string>[]>(res => {
                                Job.Deserialize(res[0]);
                                Stat.Deserialize(res[1]);

                                if (Stat.Shuttling) {
                                    ShowTransitionMenu();
                                }
                                else if (Stat.Mining) {
                                    ShowMiningMenu();
                                }
                                else
                                    ShowMainMenu();
                            });
                        }));
                        menu.Clear();
                        menu.AddRange(shipMenus);
                    });
                }, 120f);
            }

            void ShowMainMenu() {
                var p = program;
                var menu = CreateMenu("DPAM Control");
                Func<bool> hp = () => (IsController && !Stat.HasPath) || !Job.HasPath;
                menu.AddArray(new[] {
                    new Item("Record path & set home/work", () => ShowPathRecordMenu()),
                    new Item("Mining/Grinding job", () => ShowMiningConfigMenu("Mining/Grinding"), () => (IsController && !(Stat.HasDrills && Stat.HasPath)) || !(p.HasDrills && Job.HasPath)),
                    new Item("Shuttle job", () => ShowShuttleConfigMenu(), hp),
                    new Item("Go to Home", () => {
                        if (IsController){
                            Comm.SendCommand(CurrentShip, CmdGoHome);
                            ShowTransitionMenu();
                        } else
                            p.ExecuteCommand("go_home");
                    }, hp),
                    new Item("Go to Work", () => {
                        if (IsController) {
                            Comm.SendCommand(CurrentShip, CmdGoWork);
                            ShowTransitionMenu();
                        } else
                            p.ExecuteCommand("go_work");
                    }, hp),
                    new Item("Speed limit", () => $"{Job.Speed} m/s", (down) => {
                        Job.Speed = Math.Max(10, Job.Speed + down);
                    }),
                    new Item("Work Speed", () => $"{Job.WorkSpeed} m/s", (down) => {
                        Job.Speed = Math.Max(10, Job.WorkSpeed + down);
                    }),
                });
            }

            T UpDownValue<T>(T[] array, T currentValue, int down) {
                return array[(Array.IndexOf(array, currentValue) + down + (down < 0 ? array.Length : 0)) % array.Length];
            }

            public void ShowPathRecordMenu() {
                var p = program;

                string[] controllers = new[] { "None" };
                if (IsController)
                    Comm.SendCommand(CurrentShip, CmdGetControllers).Then<IList<string>>(res => controllers = Enumerable.Concat(controllers, res).ToArray());
                else
                    controllers = Enumerable.Concat(controllers, Comm.Controllers.Keys).ToArray();

                ITask statTask = null;

                var menu = CreateMenu("Path Recording", () => $"Recording: " + (Stat.Recording ? "Yes" : "No"));
                menu.AddArray(new[] {
                    new Item("Start recording", () => {
                        if (IsController) {
                            Comm.SendCommand(CurrentShip, CmdSetDef, Job.Serialize().ToImmutableDictionary());
                            Comm.SendCommand(CurrentShip, CmdRecStart);

                            statTask = Task.SetInterval(_ => {
                                Comm.SendCommand(CurrentShip, CmdGetStat).Then<IDictionary<string, string>>(res => Stat.Deserialize(res));
                            }, 1);
                        }
                        else
                            p.ExecuteCommand("record -start");
                    }),
                    new Item("Stop recording", () => {
                        if (IsController) {
                            Comm.SendCommand(CurrentShip, CmdRecStop);
                            Task.StopTask(statTask);
                        }
                        else
                            p.ExecuteCommand("record -stop");
                    }),
                    new Item("Relative to", () => $"{Job.RelativeGrid}", (down) => Job.RelativeGrid = UpDownValue(controllers, Job.RelativeGrid, down)),
                    Item.Separator,
                    new Item("Home", () => Stat.HasPath ? "Was set" : "None", null),
                    new Item("Work", () => Stat.HasPath && !Stat.Recording ? "Was set" : "None", null),
                    new Item("Path", () => $"Count: {Stat.PathCount}", null),
                });
            }

            void ShowMiningConfigMenu(string jobType) {
                var p = program;
                var modes = (DepthMode[])Enum.GetValues(typeof(DepthMode));
                var positions = (StartPosition[])Enum.GetValues(typeof(StartPosition));
                var events = (EventEnum[])Enum.GetValues(typeof(EventEnum));
                var timerActions = Enum.GetValues(typeof(TimerAction)) as TimerAction[];

                string[] timers = new[] { "None" };
                if (IsController) {
                    Comm.SendCommand(CurrentShip, CmdGetTimers).Then<IList<string>>(res => timers = Enumerable.Concat(timers, res).ToArray());
                    Comm.SendCommand(CurrentShip, CmdGetDef).Then<IDictionary<string, string>>(res => Job.Deserialize(res));
                }
                else
                    timers = Enumerable.Concat(timers, Util.GetBlocks<IMyTimerBlock>().Select(b => b.CustomName)).ToArray();

                var menu = CreateMenu($"{jobType} Job Setup");

                var action1 = new Item("  -Action", () => $"{Job.TimerDockingHomeAction}", (down) => Job.TimerDockingHomeAction = UpDownValue(timerActions, Job.TimerDockingHomeAction, down), true);
                var action2 = new Item("  -Action", () => $"{Job.TimerLeavingHomeAction}", (down) => Job.TimerLeavingHomeAction = UpDownValue(timerActions, Job.TimerLeavingHomeAction, down), true);
                menu.AddArray(new[] {
                    new Item("Start new job", () => {// 2
                        Job.Type = JobType.MiningGrinding;
                        Job.MiningJobStage = MiningJobStages.None;
                        Job.MiningJobProgress = 0;
                        if (IsController) {
                            Comm.SendCommand(CurrentShip, CmdSetDef, Job.Serialize().ToImmutableDictionary());
                            Comm.SendCommand(CurrentShip, CmdStartNew);
                            ShowMiningMenu();
                            return;
                        }
                        Job.WorkLocation = Waypoint.AddPoint(p.MyMatrix, "WorkLocation");
                        p.ExecuteCommand("start");
                    }),
                    new Item("Continue job", () => {// 3
                        if (IsController) {
                            Comm.SendCommand(CurrentShip, CmdSetDef, Job.Serialize().ToImmutableDictionary());
                            Comm.SendCommand(CurrentShip, CmdStart);
                            ShowMiningMenu();
                            return;
                        }
                        p.CurrentJob.Type = JobType.MiningGrinding;
                        p.ExecuteCommand("start");
                    }),
                    Item.Separator,// 4
                    new Item("Reset!!!", () => {// 4
                        if (IsController) {
                            Comm.SendCommand(CurrentShip, CmdReset);
                        }
                        p.ExecuteCommand("reset");
                    }),
                    new Item("Leave Home", () => $"{Job.LeaveConnector1}", (down) => Job.LeaveConnector1 = UpDownValue(events, Job.LeaveConnector1, down)),// 6
                    new Item("Timer: \"Docking Home\"", () => Job.TimerDockingHome, (down) => {// 7
                        Job.TimerDockingHome = UpDownValue(timers, Job.TimerDockingHome, down);
                        action1.Hidden = Job.TimerDockingHome == "None";
                    }),
                    action1,
                    new Item("Timer: \"Leaving Home\"", () => Job.TimerLeavingHome, (down) => {// 9
                        Job.TimerLeavingHome = UpDownValue(timers, Job.TimerLeavingHome, down);
                        action2.Hidden = Job.TimerLeavingHome == "None";
                    }),
                    action2,
                    new Item("Width", () => $"{Job.Dimensions.X}m", (down) => {// 11
                        Job.Dimensions.X = Math.Max(1, Job.Dimensions.X + down);
                        p.SetSensorDimensions(Job.Dimensions);
                    }),
                    new Item("Height", () => $"{Job.Dimensions.Y}m", (down) => {// 12
                        Job.Dimensions.Y = Math.Max(1, Job.Dimensions.Y + down);
                        p.SetSensorDimensions(Job.Dimensions);
                    }),
                    new Item("Depth", () => $"{Job.Dimensions.Z}m", (down) => {// 13
                        Job.Dimensions.Z = Math.Max(1, Job.Dimensions.Z + down);
                        p.SetSensorDimensions(Job.Dimensions);
                    }),
                    new Item("Balance drills inv.", () => Job.BalanceDrills ? "Yes" : "No", (down) => Job.BalanceDrills = !Job.BalanceDrills),
                    new Item("Terrain Clear", () => Job.TerrainClear ? "Yes" : "No", (down) => Job.TerrainClear = !Job.TerrainClear),
                    new Item("Depth Mode", () => $"{Job.DepthMode}", (down) => Job.DepthMode = UpDownValue(modes, Job.DepthMode, down)),
                    new Item("Start Pos", () => $"{Job.StartPosition}", (down) => Job.StartPosition = UpDownValue(positions, Job.StartPosition, down)),
                });
            }

            void ShowShuttleConfigMenu() {
                var p = program;
                var events = (EventEnum[])Enum.GetValues(typeof(EventEnum));
                var timerActions = Enum.GetValues(typeof(TimerAction)) as TimerAction[];

                string[] timers = new[] { "None" };
                if (IsController)
                    Comm.SendCommand(CurrentShip, CmdGetTimers, null).Then<IList<string>>(res => timers = Enumerable.Concat(timers, res).ToArray());
                else
                    timers = Enumerable.Concat(timers, Util.GetBlocks<IMyTimerBlock>().Select(b => b.CustomName)).ToArray();

                var menu = CreateMenu("Shuttle Job Setup");

                var action1 = new Item("  -Action", () => $"{Job.TimerDockingHomeAction}", (down) => Job.TimerDockingHomeAction = UpDownValue(timerActions, Job.TimerDockingHomeAction, down), true);
                var action2 = new Item("  -Action", () => $"{Job.TimerLeavingHomeAction}", (down) => Job.TimerLeavingHomeAction = UpDownValue(timerActions, Job.TimerLeavingHomeAction, down), true);
                var action3 = new Item("  -Action", () => $"{Job.TimerDockingWorkAction}", (down) => Job.TimerDockingWorkAction = UpDownValue(timerActions, Job.TimerDockingWorkAction, down), true);
                var action4 = new Item("  -Action", () => $"{Job.TimerLeavingWorkAction}", (down) => Job.TimerLeavingWorkAction = UpDownValue(timerActions, Job.TimerLeavingWorkAction, down), true);

                menu.AddArray(new[] {
                    new Item("Start job", () => {
                        p.CurrentJob.Type = JobType.Shuttle;
                        if (IsController) {
                            Comm.SendCommand(CurrentShip, CmdSetDef, Job.Serialize().ToImmutableDictionary());
                            Comm.SendCommand(CurrentShip, CmdStart);
                            ShowTransitionMenu();
                            return;
                        }
                        p.ExecuteCommand("start");
                    }),
                    new Item("Leave Home Connector", () => $"{Job.LeaveConnector1}", (down) => Job.LeaveConnector1 = UpDownValue(events, Job.LeaveConnector1, down)),
                    new Item("Leave Work Connector", () => $"{Job.LeaveConnector2}", (down) => Job.LeaveConnector2 = UpDownValue(events, Job.LeaveConnector2, down)),
                    new Item("Timer \"Docking Home\"", () => Job.TimerDockingHome, (down) => {
                        Job.TimerDockingHome = UpDownValue(timers, Job.TimerDockingHome, down);
                        action1.Hidden = Job.TimerDockingHome == "None";
                    }),
                    action1,
                    new Item("Timer \"Leaving Home\"", () => Job.TimerLeavingHome, (down) => {
                        Job.TimerLeavingHome = UpDownValue(timers, Job.TimerLeavingHome, down);
                        action2.Hidden = Job.TimerLeavingHome == "None";
                    }),
                    action2,
                    new Item("Timer \"Docking Work\"", () => Job.TimerDockingWork, (down) => {
                        Job.TimerDockingWork = UpDownValue(timers, Job.TimerDockingWork, down);
                        action3.Hidden = Job.TimerDockingWork == "None";
                    }),
                    action3,
                    new Item("Timer \"Leaving Work\"", () => Job.TimerLeavingWork, (down) => {
                        Job.TimerLeavingWork = UpDownValue(timers, Job.TimerLeavingWork, down);
                        action4.Hidden = Job.TimerLeavingWork == "None";
                    }),
                    action4,
                });
            }

            public void ShowMiningMenu() {
                var p = program;
                var menu = CreateMenu("Mining/Grinding...", false, () => $"Stage: {p.Stage}");

                var items = new List<Item>();
                if (IsController) {
                    var task = Task.SetInterval(_ => {
                        Promise.All(new[] {
                            Comm.SendCommand(CurrentShip, CmdGetDef),
                            Comm.SendCommand(CurrentShip, CmdGetStat)
                        }).Then<IDictionary<string, string>[]>(res => {
                            Job.Deserialize(res[0]);
                            Stat.Deserialize(res[1]);
                        });
                    }, 0.5f);
                    items.Insert(0, new Item("< Back", () => {
                        Task.StopTask(task);
                        Back();
                    }));
                }

                items.AddArray(new[] {
                    new Item("Abort", () => {
                        p.ExecuteCommand("stop");
                    }),
                    Item.Separator,
                    new Item("All Cargo", () => $"{Stat.FillLevel:F1} %", null),
                    new Item("Ore", () => $"{Stat.OreAmount:F1} kg", null),
                    new Item("Garbage", () => $"{Stat.GarbageAmount:F1} kg", null),
                    new Item("Battery Lvl", () => $"{Stat.BatteriesLevel:F1} %", null),
                    new Item("Progress", () => $"{Job.MiningJobProgress} / {Stat.MiningRouteCount} shafts", null),
                });

                menu.AddList(items);
            }

            public void ShowTransitionMenu() {
                var p = program;
                var menu = CreateMenu("Transitioning...", false, () => {
                    var stage = Job.ShuttleStage;
                    var connectorEvent = "";
                    if (new[] { TransitionStages.AtHome, TransitionStages.AtWork }.Contains(stage)) {
                        var conEvent = Job.ShuttleStage == TransitionStages.AtHome ? Job.LeaveConnector1 : Job.LeaveConnector2;
                        connectorEvent = $" waiting for {conEvent}";
                    }
                    return $"Status: {stage}{connectorEvent}";
                });

                var items = new List<Item>();
                if (IsController) {
                    var task = Task.SetInterval(_ => Comm.SendCommand(CurrentShip, CmdGetStat).Then<IDictionary<string, string>>(res => Stat.Deserialize(res)), 1);
                    items.Insert(0, new Item("< Back", () => {
                        Task.StopTask(task);
                        Back();
                    }));
                }

                items.AddArray(new[] {
                    new Item("Abort", () => {
                        if (IsController) {
                            Comm.SendCommand(CurrentShip, CmdStop);
                            menu[0].Action.Invoke();
                            return;
                        }
                        p.ExecuteCommand("stop");
                    }),
                    new Item("Destination", () => $"{Stat.DestinationName}", null),
                    new Item("Battery Lvl", () => $"{Stat.BatteriesLevel:F1} %", null),
                    new Item("Cur. Waypoint", () => $"{Stat.CurrentName}", null),
                    new Item("Waypoints", () => $"{Stat.Count}", null),
                    new Item("Waypoints Left", () => $"{Stat.Left}", null),
                    new Item("Cargo", () => $"{Stat.FillLevel:F1} %", null),
                });

                menu.AddList(items);
            }
        }
    }
}
