using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class Util
        {
            static Program p;

            public static void Init(Program p) {
                Util.p = p;
            }

            public static List<T> GetBlocks<T>(Func<T, bool> collect = null) where T : class, IMyTerminalBlock {
                List<T> blocks = new List<T>();
                p.GridTerminalSystem.GetBlocksOfType(blocks, b => b.IsSameConstructAs(p.Me) && (collect?.Invoke(b) ?? true));
                return blocks;
            }

            public static List<T> GetBlocks<T>(string blockTag) where T : class, IMyTerminalBlock {
                return GetBlocks<T>(b => IsTagged(b, blockTag));
            }

            public static List<T> GetGroup<T>(string name, Func<T, bool> collect = null) where T : class, IMyTerminalBlock {
                var groupBlocks = new List<T>();
                var group = p.GridTerminalSystem.GetBlockGroupWithName(name);
                group?.GetBlocksOfType(groupBlocks, b => b.IsSameConstructAs(p.Me) && (collect?.Invoke(b) ?? true));
                return groupBlocks;
            }

            public static List<T> GetGroupOrBlocks<T>(string name, Func<T, bool> collect = null) where T : class, IMyTerminalBlock {
                List<T> groupBlocks = GetGroup(name, collect);
                if (groupBlocks.Count() == 0) {
                    return GetBlocks<T>(b => b.CustomName == name && (collect?.Invoke(b) ?? true));
                }
                return groupBlocks;
            }

            public static List<IMyTextSurface> GetScreens(string screenTag = "") {
                return GetScreens(b => IsTagged(b, screenTag), screenTag);
            }

            public static List<IMyTextSurface> GetScreens(Func<IMyTerminalBlock, bool> collect = null, string screenTag = "") {
                var screens = GetBlocks<IMyTerminalBlock>(b => (b is IMyTextSurface || HasScreens(b)) && collect(b));
                return screens.Select(s => {
                    if (s is IMyTextSurface)
                        return s as IMyTextSurface;
                    var provider = s as IMyTextSurfaceProvider;
                    var regex = new System.Text.RegularExpressions.Regex(@"^(\S*)@(\d+)$", System.Text.RegularExpressions.RegexOptions.Multiline);
                    var match = regex.Match(s.CustomData);
                    if (match.Success && match.Groups[1].Value == screenTag) {
                        var screenIndex = int.Parse(match.Groups[2].Value) - 1;
                        return provider.GetSurface(screenIndex);
                    }
                    return provider.GetSurface(0);
                }).ToList();
            }

            public static int ScreenLines(IMyTextSurface screen, char symbol = 'S') {
                var symbolSize = screen.MeasureStringInPixels(new StringBuilder(symbol.ToString()), screen.Font, screen.FontSize);
                var paddingY = NormalizeValue(screen.TextPadding, 100, screen.SurfaceSize.Y);
                var screenY = screen.SurfaceSize.Y - paddingY;
                return (int)Math.Floor(screenY / (symbolSize.Y + 2) * (512 / screen.TextureSize.Y)) + 1;
            }

            public static int ScreenColumns(IMyTextSurface screen, char symbol = 'S') {
                var symbolSize = screen.MeasureStringInPixels(new StringBuilder(symbol.ToString()), screen.Font, screen.FontSize);
                var paddingX = NormalizeValue(screen.TextPadding, 100, screen.SurfaceSize.X);
                var screenX = screen.SurfaceSize.X - paddingX;
                return (int)Math.Floor(screenX / symbolSize.X * (512 / screen.TextureSize.X)) - 1;
            }

            public static double NormalizeValue(double value, double oldMin, double oldMax, double min, double max) {
                double originalRange = oldMax - oldMin;
                double newRange = max - min;
                double normalizedValue = (value - oldMin) * newRange / originalRange + min;
                return normalizedValue;
            }
            public static double NormalizeValue(double value, double oldMax, double max)
                => NormalizeValue(value, 0, oldMax, 0, max);
            public static double NormalizeClamp(double value, double oldMin, double oldMax, double min, double max)
                => MathHelper.Clamp(NormalizeValue(value, oldMin, oldMax, min, max), min > max ? max : min, max < min ? min : max);
            public static double NormalizeClamp(double value, double oldMax, double max)
                => MathHelper.Clamp(NormalizeValue(value, 0, oldMax, 0, max), 0, max);
            public static bool IsNotIgnored(IMyTerminalBlock block, string ignoreTag = "{Ignore}")
                => !(block.CustomName.Contains(ignoreTag) || block.CustomData.Contains(ignoreTag));
            public static bool IsTagged(IMyTerminalBlock block, string tag = "{DDAS}")
                => block.CustomName.Contains(tag) || block.CustomData.Contains(tag);
            public static bool IsBetween(double value, double min, double max)
                => value >= min && value <= max;
            public static bool HasScreens(IMyTerminalBlock block)
                => block is IMyTextSurfaceProvider && (block as IMyTextSurfaceProvider).SurfaceCount > 0;
            public static void ApplyGyroOverride(double pitchSpeed, double yawSpeed, double rollSpeed, float power, IMyGyro gyro, MatrixD worldMatrix)
                => ApplyGyroOverride(pitchSpeed, yawSpeed, rollSpeed, power, new[] { gyro }, worldMatrix);

            public static void ApplyGyroOverride(double pitchSpeed, double yawSpeed, double rollSpeed, float power, IEnumerable<IMyGyro> gyros, MatrixD worldMatrix) {
                var rotationVec = new Vector3D(pitchSpeed, yawSpeed, rollSpeed);
                var relativeRotationVec = Vector3D.TransformNormal(rotationVec, worldMatrix);

                foreach (var g in gyros) {
                    if (g.GyroPower != power)
                        g.GyroPower = power;
                    g.GyroOverride = true;
                    var transformedRotationVec = Vector3D.TransformNormal(relativeRotationVec, Matrix.Transpose(g.WorldMatrix));
                    g.Pitch = (float)transformedRotationVec.X;
                    g.Yaw = (float)transformedRotationVec.Y;
                    g.Roll = (float)transformedRotationVec.Z;
                }
            }

            public static IEnumerable DisplayLogo(string logo, IMyTextSurface screen) {
                var progress = (new char[] { '/', '-', '\\', '|' }).GetEnumerator();
                var pbLabel = $"{logo} - ";
                var screenLines = ScreenLines(screen);
                screen.Alignment = TextAlignment.CENTER;
                screen.ContentType = ContentType.TEXT_AND_IMAGE;

                while (true) {
                    if (!progress.MoveNext()) {
                        progress.Reset();
                        progress.MoveNext();
                    }

                    yield return screen.WriteText(
                        string.Join("", Enumerable.Repeat("\n", screenLines / 2))
                        + pbLabel
                        + progress.Current
                    );
                }
            }
            static readonly StringBuilder StatusText = new StringBuilder();
            public static void Echo(string text, bool keep = false) {
                if (!keep)
                    StatusText.Clear();
                StatusText.AppendLine(text);
            }
            public static IEnumerable StatusMonitorTask(Program p) {
                var runtimeText = new StringBuilder();
                var runtime = p.Runtime;
                var progress = (new char[] { '/', '-', '\\', '|' }).GetEnumerator();

                while (true) {
                    if (!progress.MoveNext()) {
                        progress.Reset();
                        progress.MoveNext();
                    }

                    runtimeText.Clear();
                    runtimeText.AppendLine($"Runtime Info - {progress.Current}");
                    runtimeText.AppendLine("----------------------------");
                    runtimeText.AppendLine($"Last Run: {runtime.LastRunTimeMs}ms");
                    runtimeText.AppendLine($"Time Since Last Run: {runtime.TimeSinceLastRun.TotalMilliseconds}ms");
                    runtimeText.AppendLine($"Instruction Count: {runtime.CurrentInstructionCount}/{runtime.MaxInstructionCount}");
                    runtimeText.AppendLine($"Call depth Count: {runtime.CurrentCallChainDepth}/{runtime.MaxCallChainDepth}");
                    runtimeText.AppendLine();
                    runtimeText.AppendStringBuilder(StatusText);
                    p.Echo(runtimeText.ToString());
                    yield return null;
                }
            }

            public static void SetSensorDimensions(IMySensorBlock sensor, float[] values) {
                for (int i = 0; i < values.Length && i < Base6Directions.EnumDirections.Length; i++) {
                    var value = MathHelper.Clamp(values[i], 0.1f, 50);
                    var dir = sensor.Orientation.TransformDirectionInverse(Base6Directions.EnumDirections[i]);
                    switch (dir) {
                        case Base6Directions.Direction.Forward:
                            sensor.FrontExtend = value;
                            break;
                        case Base6Directions.Direction.Backward:
                            sensor.BackExtend = value;
                            break;
                        case Base6Directions.Direction.Left:
                            sensor.LeftExtend = value;
                            break;
                        case Base6Directions.Direction.Right:
                            sensor.RightExtend = value;
                            break;
                        case Base6Directions.Direction.Up:
                            sensor.TopExtend = value;
                            break;
                        case Base6Directions.Direction.Down:
                            sensor.BottomExtend = value;
                            break;
                    }
                }
            }
            static Random Rand = new Random((int)DateTime.Now.Ticks & 0x0000FFFF);

            public static string GenerateRandomStr() {
                var suffix = "";
                for (int i = 0; i < 3; i++) {
                    suffix += (char)('A' + Rand.Next(0, 26));
                }
                return suffix;
            }

        }
    }
}
