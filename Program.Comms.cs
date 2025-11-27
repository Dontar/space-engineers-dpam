using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        const string CmdGetShipNames = "GSN";
        const string CmdGetPos = "GPOS";
        const string CmdGetDef = "GDEF";
        const string CmdSetDef = "DEF_SET";
        const string CmdGetStat = "GSTAT";
        const string CmdGetTimers = "GTIME";
        const string CmdGetControllers = "GCTRL";
        const string CmdRecord = "RECORD";
        const string CmdRecStart = "RECSTART";
        const string CmdRecStop = "RECSTOP";
        const string CmdStop = "STOP";
        const string CmdStart = "START";
        const string CmdStartNew = "STARTNEW";
        const string CmdToggle = "TOGGLE";
        const string CmdGoHome = "GOHOME";
        const string CmdGoWork = "GOWORK";
        const string CmdReset = "RESET";

        Comms Comm;
        class Comms : ClientServer
        {
            Program p;
            public Dictionary<string, long> Controllers;
            public Comms(Program program, bool isController) : base(program) {
                p = program;
                Controllers = new Dictionary<string, long>();
                if (!isController) {
                    RegisterBrListener(CmdGetShipNames);
                    RequestHandlers.Add(CmdGetShipNames, msg => {
                        if (!Controllers.ContainsKey(msg.As<string>()))
                            Controllers.Add(msg.As<string>(), msg.Source);
                        RespondToCmdMsg(msg, p.Me.CubeGrid.CustomName);
                    });
                    RequestHandlers.Add(CmdGetDef, msg => RespondToCmdMsg(msg, p.CurrentJob.Serialize().ToImmutableDictionary()));
                    RequestHandlers.Add(CmdSetDef, msg => p.CurrentJob.Deserialize(msg.As<ImmutableDictionary<string, string>>()));
                    RequestHandlers.Add(CmdGetStat, msg => RespondToCmdMsg(msg, p.Status.Serialize().ToImmutableDictionary()));
                    RequestHandlers.Add(CmdGetTimers, msg => RespondToCmdMsg(msg, Util.GetBlocks<IMyTimerBlock>().Select(t => t.CustomName).ToImmutableList()));
                    RequestHandlers.Add(CmdGetControllers, msg => RespondToCmdMsg(msg, Controllers.Keys.ToImmutableList()));
                    RequestHandlers.Add(CmdRecord, msg => p.ExecuteCommand("record"));
                    RequestHandlers.Add(CmdRecStart, msg => p.ExecuteCommand("record -start"));
                    RequestHandlers.Add(CmdRecStop, msg => p.ExecuteCommand("record -stop"));
                    RequestHandlers.Add(CmdToggle, msg => p.ExecuteCommand("toggle"));
                    RequestHandlers.Add(CmdStart, msg => p.ExecuteCommand("start"));
                    RequestHandlers.Add(CmdStartNew, msg => {
                        p.CurrentJob.WorkLocation = Waypoint.AddPoint(p.MyMatrix, "WorkLocation");
                        p.ExecuteCommand("start");
                    });
                    RequestHandlers.Add(CmdStop, msg => p.ExecuteCommand("stop"));
                    RequestHandlers.Add(CmdGoHome, msg => p.ExecuteCommand("go_home"));
                    RequestHandlers.Add(CmdGoWork, msg => p.ExecuteCommand("go_work"));
                    RequestHandlers.Add(CmdReset, msg => p.ExecuteCommand("reset"));
                }
                RequestHandlers.Add(CmdGetPos, msg => RespondToCmdMsg(msg, p.MyMatrix.Translation));
            }
        }

        class ClientServer
        {
            IMyIntergridCommunicationSystem igc;
            IMyBroadcastListener bListener;
            IMyUnicastListener uListener;
            Dictionary<long, Queue<MyIGCMessage>> Responses;
            Dictionary<long, Queue<MyIGCMessage>> Requests;
            protected Dictionary<string, Action<MyIGCMessage>> RequestHandlers;

            const string ResponseSuffix = "_R";

            public ClientServer(Program program) {
                igc = program.IGC;
                Responses = new Dictionary<long, Queue<MyIGCMessage>>();
                Requests = new Dictionary<long, Queue<MyIGCMessage>>();
                RequestHandlers = new Dictionary<string, Action<MyIGCMessage>>();

                uListener = igc.UnicastListener;
                uListener.SetMessageCallback();

                Task.SetInterval(_ => ProcessRequest(), 0);
            }

            public Promise SendCommand(long ship, string command, object data = null) {
                var sent = igc.SendUnicastMessage(ship, command, data);
                return new Promise(res => {
                    if (!sent) {
                        res(null);
                        return;
                    }
                    if (!Responses.ContainsKey(ship) || Responses[ship].Count == 0)
                        return;
                    var msg = Responses[ship].Peek();
                    if (msg.Tag == command + ResponseSuffix) {
                        Responses[ship].Dequeue();
                        res(msg.Data);
                    }
                });
            }

            public Promise SendBroadcast(string command, object data = null) {
                igc.SendBroadcastMessage(command, data);
                var result = new Dictionary<long, object>();
                return new Promise(res => {
                    foreach (var keyVal in Responses) {
                        if (keyVal.Value.Count == 0)
                            continue;
                        var msg = keyVal.Value.Peek();
                        if (msg.Tag == command + ResponseSuffix) {
                            keyVal.Value.Dequeue();
                            result.Add(msg.Source, msg.Data);
                        }
                    }
                    if (result.Count > 0) {
                        res(result);
                    }
                });
            }

            public void RegisterBrListener(string command) {
                bListener = igc.RegisterBroadcastListener(command);
                bListener.SetMessageCallback();
            }

            public void RespondToCmdMsg(MyIGCMessage msg, object data)
                => igc.SendUnicastMessage(msg.Source, msg.Tag + ResponseSuffix, data);

            public void ProcessRequest() {
                var requests = new Queue<MyIGCMessage>(Requests.Values.SelectMany(v => v));
                while (requests.Count > 0) {
                    var msg = requests.Dequeue();
                    if (RequestHandlers.ContainsKey(msg.Tag)) {
                        RequestHandlers[msg.Tag]?.Invoke(msg);
                    }
                }
            }

            public void ReceiveMessages() {
                while (bListener != null && bListener.HasPendingMessage) {
                    var bMsg = bListener.AcceptMessage();
                    if (!Requests.ContainsKey(bMsg.Source))
                        Requests[bMsg.Source] = new Queue<MyIGCMessage>();
                    Requests[bMsg.Source].Enqueue(bMsg);
                }
                while (uListener.HasPendingMessage) {
                    var msg = uListener.AcceptMessage();
                    var isResponse = msg.Tag.EndsWith(ResponseSuffix);

                    if (isResponse) {
                        if (!Responses.ContainsKey(msg.Source))
                            Responses[msg.Source] = new Queue<MyIGCMessage>();
                        Responses[msg.Source].Enqueue(msg);
                    }
                    else {
                        if (!Requests.ContainsKey(msg.Source))
                            Requests[msg.Source] = new Queue<MyIGCMessage>();
                        Requests[msg.Source].Enqueue(msg);
                    }
                }
            }
        }
    }
}
