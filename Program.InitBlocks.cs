using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        IMyShipController Controller;
        MyShipMass Mass => Controller.CalculateShipMass();
        MyShipVelocities Velocities => Controller.GetShipVelocities();
        MatrixD MyMatrix => Controller.WorldMatrix;
        Vector3D Gravity => Controller.GetTotalGravity();
        BoundingBox Dimensions;
        Dictionary<Base6Directions.Direction, IMyThrust[]> Thrusters;
        List<IMyShipConnector> Connectors;
        List<IMyShipDrill> Drills;
        bool HasDrills => Drills != null && Drills.Count > 0;
        List<IMyShipGrinder> Grinders;
        bool HasGrinders => Grinders != null && Grinders.Count > 0;
        List<IMyConveyorSorter> Sorters;
        List<IMyCargoContainer> CargoContainers;
        List<IMyGyro> Gyros;
        List<IMyInventory> Inventories;
        List<IMyInventory> DrillInventories;
        float FillLevel => Inventories.Sum(i => i.VolumeFillFactor) / Math.Max(1, Inventories.Count) * 100;
        IMySensorBlock Sensor;
        List<IMyTextSurface> Screens => Memo.Of("Screens", 10, () => Util.GetScreens(_tag));
        List<MyItemType> Garbage => Memo.Of("Garbage", TimeSpan.FromSeconds(5), () => Sorters.SelectMany(s => {
            var list = new List<MyInventoryItemFilter>();
            s.GetFilterList(list);
            return list.Select(i => i.ItemType).Distinct();
        }).ToList()
        );

        void InitController() {
            var controllers = Util.GetBlocks<IMyRemoteControl>();
            Controller = controllers.FirstOrDefault(b => Util.IsTagged(b, _tag)) ?? controllers.FirstOrDefault(b => Util.IsNotIgnored(b, _ignoreTag));

            var scale = (Me.CubeGrid.GridSizeEnum == MyCubeSize.Large) ? 2.5f : 0.5f; // meters
            var min = (Me.CubeGrid.Min - Vector3.Half) * scale;
            var max = (Me.CubeGrid.Max + Vector3.Half) * scale;
            Dimensions = new BoundingBox(min, max);
        }

        void InitBlocks() {
            Thrusters = Util.GetBlocks<IMyThrust>(b => Util.IsNotIgnored(b, _ignoreTag))
                .GroupBy(b => Controller.Orientation.TransformDirectionInverse(Base6Directions.GetOppositeDirection(b.Orientation.Forward)))
                .ToDictionary(k => k.Key, v => v.ToArray());
            Connectors = Util.GetBlocks<IMyShipConnector>(b => Util.IsNotIgnored(b, _ignoreTag) && !b.ThrowOut);
            Drills = Util.GetBlocks<IMyShipDrill>(b => Util.IsNotIgnored(b, _ignoreTag));
            Grinders = Util.GetBlocks<IMyShipGrinder>(b => Util.IsNotIgnored(b, _ignoreTag));
            Sorters = Util.GetBlocks<IMyConveyorSorter>(b => Util.IsNotIgnored(b, _ignoreTag));
            CargoContainers = Util.GetBlocks<IMyCargoContainer>(b => Util.IsNotIgnored(b, _ignoreTag));
            Gyros = Util.GetBlocks<IMyGyro>(b => Util.IsNotIgnored(b, _ignoreTag));
            Sensor = Util.GetBlocks<IMySensorBlock>(b => Util.IsNotIgnored(b, _ignoreTag)).FirstOrDefault();
            SetSensorDimensions(CurrentJob.Dimensions);
            DrillInventories = Drills.Select(d => d.GetInventory(0)).ToList();

            var cargoGroup = Util.GetGroupOrBlocks<IMyTerminalBlock>("Cargo");
            var cockPits = Util.GetBlocks<IMyCockpit>(b => Util.IsNotIgnored(b, _ignoreTag));
            Inventories = cargoGroup
                .Concat(cockPits)
                .Concat(Drills)
                .Concat(Connectors)
                .Concat(Grinders)
                .Concat(CargoContainers)
                .Distinct()
                .SelectMany(y => {
                    var result = new List<IMyInventory>();
                    for (int i = 0; i < y.InventoryCount; i++) {
                        var inv = y.GetInventory(i);
                        result.Add(inv);
                    }
                    return result;
                }).ToList();
        }

        float GetInventoryItemsAmountsWithoutGarbage() {
            float total = 0;
            foreach (var inv in Inventories) {
                var items = new List<MyInventoryItem>();
                inv.GetItems(items, item => !Garbage.Contains(item.Type));
                total += items.Sum(i => (float)i.Amount);
            }
            return total;
        }

        void SetSensorDimensions(Vector3 workArea) {
            if (Sensor == null)
                return;
            var scale = Me.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 2.5f : 0.5f;
            var sensorPos = Sensor.Position * scale;

            var offset = sensorPos - Dimensions.Center;
            var half = workArea / 2;

            var values = new[] {
                /* Forward */ workArea.Z + offset.Z,
                /* Backward */ 0 - offset.Z,
                /* Left */ half.X + offset.X,
                /* Right */ half.X - offset.X,
                /* Up */ half.Y - offset.Y,
                /* Down */ half.Y + offset.Y,
            };

            Util.SetSensorDimensions(Sensor, values);
        }
    }
}
