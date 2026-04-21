using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;

namespace IngameScript
{
    partial class Program
    {
        partial class MenuManager
        {
            // ─── IGC channel tags ───────────────────────────────────────────────────
            const string RMENU_CMD_TAG = "RMENU_CMD";
            const string RMENU_RENDER_TAG = "RMENU_RENDER";

            // Host-side state
            readonly List<long> _remoteViewers = new List<long>();

            // Client-side state
            long _remoteHostId = -1;
            RemoteMenu _activeRemoteMenu;

            // ─── Host side ──────────────────────────────────────────────────────────

            /// <summary>Register a remote block that will receive menu renders.</summary>
            public void AddRemoteViewer(long entityId) {
                if (!_remoteViewers.Contains(entityId))
                    _remoteViewers.Add(entityId);
            }

            public void RemoveRemoteViewer(long entityId) => _remoteViewers.Remove(entityId);

            /// <summary>Send the current rendered menu to a specific remote viewer.</summary>
            public void SendMenuRenderTo(long entityId, int lines, int cols) {
                try {
                    program.IGC.SendUnicastMessage(entityId, RMENU_RENDER_TAG, Render(lines, cols));
                }
                catch (Exception e) {
                    program.Echo($"[RemoteMenu] SendRender failed: {e.Message}");
                }
            }

            /// <summary>Send the rendered menu to a viewer using the provided screen for size calculations.</summary>
            public void SendMenuTo(long entityId, IMyTextSurface screen) {
                SendMenuRenderTo(entityId, Util.ScreenLines(screen), Util.ScreenColumns(screen, '='));
            }

            // ─── Client side ────────────────────────────────────────────────────────

            /// <summary>Connect to a remote host's menu and push a RemoteMenu onto the stack.</summary>
            public void ConnectToRemoteMenu(long hostEntityId, int requestLines = 20, int requestCols = 40) {
                _remoteHostId = hostEntityId;
                _activeRemoteMenu = new RemoteMenu(this);
                menuStack.Push(_activeRemoteMenu);
                SendRemoteCommand($"req:{requestLines}:{requestCols}");
            }

            internal void SendRemoteCommand(string command) {
                if (_remoteHostId < 0)
                    return;
                try {
                    program.IGC.SendUnicastMessage(_remoteHostId, RMENU_CMD_TAG, command);
                }
                catch (Exception e) {
                    program.Echo($"[RemoteMenu] SendCommand failed: {e.Message}");
                }
            }

            // ─── Message handling ────────────────────────────────────────────────────

            /// <summary>
            /// Poll the unicast listener and handle all pending remote menu messages.
            /// Any messages with unrecognised tags are silently ignored.
            /// </summary>
            public void HandleRemoteMessages() {
                var listener = program.IGC.UnicastListener;
                while (listener.HasPendingMessage) {
                    HandleRemoteMessage(listener.AcceptMessage());
                }
            }

            /// <summary>
            /// Handle a single IGC message. Returns true if the message was consumed
            /// by the remote menu system, false if it should be handled elsewhere.
            /// </summary>
            public bool HandleRemoteMessage(MyIGCMessage msg) {
                try {
                    if (msg.Tag == RMENU_CMD_TAG) {
                        ReceiveMenuCommand(msg.As<string>(), msg.Source);
                        return true;
                    }
                    if (msg.Tag == RMENU_RENDER_TAG) {
                        ReceiveMenuRender(msg.As<string>());
                        return true;
                    }
                }
                catch (Exception e) {
                    program.Echo($"[RemoteMenu] Message error: {e.Message}");
                }
                return false;
            }


            int lines = 20, cols = 40;
            void ReceiveMenuCommand(string command, long sourceId) {
                // Handle render-request from a newly connected viewer
                if (command.StartsWith("req:")) {
                    var parts = command.Split(':');
                    if (parts.Length >= 3) {
                        int.TryParse(parts[1], out lines);
                        int.TryParse(parts[2], out cols);
                    }
                    AddRemoteViewer(sourceId);
                    SendMenuRenderTo(sourceId, lines, cols);
                    return;
                }

                switch (command.ToLower()) {
                    case "up":
                        Up();
                        break;
                    case "down":
                        Down();
                        break;
                    case "apply":
                        Apply();
                        break;
                    case "back":
                        Back();
                        break;
                    default:
                        return;
                }

                // Push the updated render back to the viewer that issued the command.
                // We don't know the screen size here, so use the last-requested size
                // stored on the viewer entry (default 20x40 if unknown).
                SendMenuRenderTo(sourceId, lines, cols);
            }

            void ReceiveMenuRender(string content) => _activeRemoteMenu?.UpdateContent(content);

            public void Disconnect() {
                var menu = menuStack.Peek();
                if (menu is RemoteMenu) {
                    menuStack.Pop();
                    _remoteHostId = -1;
                    _activeRemoteMenu = null;
                }
            }

            // ─── RemoteMenu class ────────────────────────────────────────────────────

            /// <summary>
            /// A Menu that displays content rendered on a remote host and forwards
            /// navigation commands back to that host via IGC unicast.
            /// </summary>
            protected class RemoteMenu : Menu
            {
                readonly MenuManager _manager;
                string _content = "(Connecting to remote menu...)";

                public RemoteMenu(MenuManager manager) : base("Remote Menu") {
                    _manager = manager;
                }

                public void UpdateContent(string content) {
                    _content = content;
                }

                // Navigation commands are forwarded to the remote host instead of
                // being handled locally.
                public override void Up() => _manager.SendRemoteCommand("up");
                public override void Down() => _manager.SendRemoteCommand("down");
                public override void Apply() => _manager.SendRemoteCommand("apply");
                public override bool Back() {
                    _manager.SendRemoteCommand("back");
                    return true;
                }

                public override string Render(int screenLines, int screenColumns) => _content;

                public override void Render(IMyTextSurface screen) {
                    screen.ContentType = ContentType.TEXT_AND_IMAGE;
                    screen.Alignment = TextAlignment.LEFT;
                    screen.Font = "Monospace";
                    screen.WriteText(_content);
                }
            }
        }
    }
}
