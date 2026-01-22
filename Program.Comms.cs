using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        Dictionary<long, string> ShipList = new Dictionary<long, string>();
        string LatestScreen;
        void InitCommController() {
            var channel = IGC.UnicastListener;
            Task.SetTimeout(RegRequest, 3);
            Task.SetInterval(RegRequest, 3 * 60);
            Task.SetInterval(() => {
                if (channel.HasPendingMessage) {
                    var msg = channel.AcceptMessage();
                    switch (msg.Tag) {
                        case "REG_RESP":
                            ShipList[msg.Source] = msg.As<string>();
                            break;
                        case "SCREEN_RESP":
                            LatestScreen = msg.As<string>();
                            break;
                    }
                }
            }, 0);
        }

        private void RegRequest() {
            var screen = Screens.First();
            var lines = Util.ScreenLines(screen);
            var cols = Util.ScreenLines(screen);
            IGC.SendBroadcastMessage("REG_REQ", $"{lines}|{cols}");
        }

        // Ship
        struct RemoteScreen
        {
            public int lines;
            public int cols;
        }

        RemoteScreen remoteScreen;

        void InitCommShip() {
            var regChannel = IGC.RegisterBroadcastListener("REG_REQ");
            var channel = IGC.UnicastListener;
            Task.SetInterval(() => {
                if (regChannel.HasPendingMessage) {
                    var msg = regChannel.AcceptMessage();
                    var parts = msg.As<string>().Split('|');
                    remoteScreen.lines = int.Parse(parts[0]);
                    remoteScreen.cols = int.Parse(parts[1]);

                    IGC.SendUnicastMessage(msg.Source, "REG_RESP", Me.CubeGrid.CustomName);
                }
                if (channel.HasPendingMessage) {
                    var msg = channel.AcceptMessage();
                    if (msg.Tag == "SCREEN_REQ") {
                        var render = MainMenu.Render(remoteScreen.lines, remoteScreen.cols);
                        IGC.SendUnicastMessage(msg.Source, "SCREEN_RESP", render);
                    }
                    if (msg.Tag.StartsWith("CMD_")) {
                        var cmd = msg.Tag.Substring(4).ToLower();
                        ExecuteCommand(cmd);
                    }
                }
            }, 0);
        }
    }
}