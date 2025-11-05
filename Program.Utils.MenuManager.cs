using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    partial class Program
    {
        class MenuManager
        {
            protected enum ItemType
            {
                Item, Separator
            }
            protected class Item
            {
                public ItemType Type;
                public string Label;
                bool _Hidden;
                public bool Hidden {
                    get {
                        return UpdateHidden?.Invoke() ?? _Hidden;
                    }
                    set {
                        _Hidden = value;
                    }
                }
                public Func<bool> UpdateHidden;
                public Func<string> Value;
                public Action Action;
                public Action<int> IncDec;

                public Item(string label, Func<string> value, Action<int> incDec, Action action, bool hidden, Func<bool> updateHidden) {
                    Type = ItemType.Item;
                    Label = label;
                    Value = value;
                    IncDec = incDec;
                    Action = action;
                    _Hidden = hidden;
                    UpdateHidden = updateHidden;
                }
                public Item(string label, Func<string> value, Action<int> incDec, bool hidden = false) : this(label, value, incDec, null, hidden, null) { }
                public Item(string label, Action action, bool hidden = false) : this(label, null, null, action, hidden, null) { }

                public Item(string label, Func<string> value, Action<int> incDec, Func<bool> updateHidden) : this(label, value, incDec, null, false, updateHidden) { }
                public Item(string label, Action action, Func<bool> updateHidden) : this(label, null, null, action, false, updateHidden) { }
                public static Item Separator => new Item("", null) { Type = ItemType.Separator };
            }

            protected class Menu : List<Item>
            {
                int _selectedOption = 0;
                int _activeOption = -1;
                string _title;
                Func<string> _footer;
                Item Item => this[_selectedOption];
                Item AItem => this[_activeOption];

                public Menu(string title, Func<string> footer = null) : base() {
                    _title = title;
                    _footer = footer;
                }

                public void Up() {
                    if (_activeOption > -1) {
                        this[_activeOption].IncDec?.Invoke(-1);
                        return;
                    }
                    do {
                        _selectedOption = (_selectedOption - 1 + Count) % Count;
                    } while (Item.Type == ItemType.Separator || Item.Hidden);
                }

                public void Down() {
                    if (_activeOption > -1) {
                        AItem.IncDec?.Invoke(1);
                        return;
                    }
                    do {
                        _selectedOption = (_selectedOption + 1) % Count;
                    } while (Item.Type == ItemType.Separator || Item.Hidden);
                }

                public void Apply() {
                    _activeOption = _activeOption == _selectedOption ? -1 : Item.IncDec != null ? _selectedOption : -1;
                    Item.Action?.Invoke();
                }

                public void Render(IMyTextSurface screen) {
                    screen.ContentType = ContentType.TEXT_AND_IMAGE;
                    screen.Alignment = TextAlignment.LEFT;
                    screen.Font = "Monospace";
                    var screenLines = Util.ScreenLines(screen);
                    var screenColumns = Util.ScreenColumns(screen, '=');

                    var output = new List<string> {
                        _title,
                        string.Join("", Enumerable.Repeat("=", screenColumns))
                    };

                    if (Item.Hidden)
                        Down();

                    var pageSize = screenLines - 3;
                    var start = Math.Max(0, _selectedOption - pageSize / 2);

                    for (int i = start; i < Math.Min(Count, start + pageSize); i++) {
                        var item = this[i];
                        if (item.Hidden) {
                            continue;
                        }
                        var label = item.Type == ItemType.Separator ? string.Join("", Enumerable.Repeat("-", screenColumns)) : item.Label;
                        var value = item.Value?.Invoke();
                        var sep = value != null ? ":" : "";
                        var activeInd = i == _activeOption ? "-" : "";
                        var selectInd = i == _selectedOption ? "> " : "  ";
                        var labelWidth = screenColumns / 3 * 2;
                        output.Add(string.Format($"{{0,-{labelWidth}}}{{1}}", activeInd + selectInd + label + sep, value ?? ""));
                    }

                    var footer = new List<string>();
                    if (_footer != null) {
                        footer.Add(string.Join("", Enumerable.Repeat("-", screenColumns)));
                        footer.Add(_footer.Invoke());
                    }

                    output.AddRange(Enumerable.Repeat("", screenLines - output.Count - footer.Count));
                    screenColumns = Util.ScreenColumns(screen, '-');
                    output.AddRange(footer);
                    output.Add(string.Join("", Enumerable.Repeat("-", screenColumns)));
                    screen.WriteText(string.Join(Environment.NewLine, output));
                }
            }

            protected Stack<Menu> menuStack = new Stack<Menu>();

            protected Program program;

            public MenuManager(Program program) {
                this.program = program;
            }
            public void Up() => menuStack.Peek().Up();
            public void Down() => menuStack.Peek().Down();
            public void Apply() => menuStack.Peek().Apply();
            public void Back() {
                if (menuStack.Count > 1)
                    menuStack.Pop();
            }
            public void Render(IMyTextSurface screen) => menuStack.Peek().Render(screen);

            protected Menu CreateMenu(string title) => CreateMenu(title, true, null);
            protected Menu CreateMenu(string title, bool createBack) => CreateMenu(title, createBack, null);
            protected Menu CreateMenu(string title, Func<string> footer = null) => CreateMenu(title, true, footer);
            protected Menu CreateMenu(string title, bool createBack, Func<string> footer) {
                var menu = new Menu(title, footer);
                if (menuStack.Count > 0 && createBack) {
                    menu.Add(new Item("< Back", Back));
                }
                menuStack.Push(menu);
                return menu;
            }

            public bool ProcessMenuCommands(MyCommandLine cmd) {
                var command = cmd.Argument(0);
                switch (command.ToLower()) {
                    case "up":
                        Up();
                        break;
                    case "apply":
                        Apply();
                        break;
                    case "down":
                        Down();
                        break;
                    case "back":
                        Back();
                        break;
                    default:
                        return false;
                }
                return true;
            }
        }
    }
}
