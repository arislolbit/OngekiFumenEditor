﻿using Caliburn.Micro;
using OngekiFumenEditor.Base;
using OngekiFumenEditor.Base.OngekiObjects.ConnectableObject;
using OngekiFumenEditor.Modules.FumenVisualEditor;
using OngekiFumenEditor.Utils;
using OngekiFumenEditor.Utils.ObjectPool;
using OpenTK.Graphics.OpenGL;
using Polyline2DCSharp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using static OngekiFumenEditor.Modules.FumenPreviewer.Graphics.Drawing.ILineDrawing;

namespace OngekiFumenEditor.Modules.FumenPreviewer.Graphics.Drawing.TargetImpl
{
    public abstract class CommonLinesDrawTargetBase<T> : CommonBatchDrawTargetBase<T> where T : ConnectableStartObject
    {
        public virtual int LineWidth { get; } = 2;
        private ISimpleLineDrawing lineDrawing;
        TGrid shareTGrid = new TGrid();
        XGrid shareXGrid = new XGrid();
        private TGrid previewMinTGrid;
        private TGrid previewMaxTGrid;

        public CommonLinesDrawTargetBase()
        {
            lineDrawing = IoC.Get<ISimpleLineDrawing>();
        }

        public abstract Vector4 GetLanePointColor(ConnectableObjectBase obj);

        public void FillLine(IFumenPreviewer target, T obj)
        {
            var color = GetLanePointColor(obj);
            var resT = obj.TGrid.ResT;
            var resX = obj.XGrid.ResX;

            [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
            void PostPoint(TGrid tGrid, XGrid xGrid)
            {
                var x = (float)XGridCalculator.ConvertXGridToX(xGrid, 30, target.ViewWidth, 1);
                var y = (float)TGridCalculator.ConvertTGridToY(tGrid, target.Fumen.BpmList, 1.0, 240);

                lineDrawing.PostPoint(new(x, y), color);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            bool isVisible(TGrid tGrid)
            {
                return previewMinTGrid <= tGrid || tGrid <= previewMaxTGrid;
            }

            var prevVisible = isVisible(obj.TGrid);
            var alwaysDrawing = isVisible(obj.MinTGrid) && isVisible(obj.MaxTGrid);

            PostPoint(obj.TGrid, obj.XGrid);

            foreach (var childObj in obj.Children)
            {
                var visible = alwaysDrawing || isVisible(childObj.TGrid);

                if (visible || prevVisible)
                {
                    if (childObj.IsCurvePath)
                    {
                        foreach (var item in childObj.GenPath())
                        {
                            shareTGrid.Unit = item.pos.Y / resT;
                            shareXGrid.Unit = item.pos.X / resX;
                            PostPoint(shareTGrid, shareXGrid);
                        }
                    }
                    else
                        PostPoint(childObj.TGrid, childObj.XGrid);
                }

                prevVisible = visible;
            }
        }

        public override void DrawBatch(IFumenPreviewer target, IEnumerable<T> starts)
        {
            previewMinTGrid = TGridCalculator.ConvertYToTGrid(target.CurrentPlayTime, target.Fumen.BpmList, 1, 240);
            previewMaxTGrid = TGridCalculator.ConvertYToTGrid(target.CurrentPlayTime + target.ViewHeight, target.Fumen.BpmList, 1, 240);

            foreach (var laneStart in starts)
            {
                lineDrawing.Begin(target, LineWidth);
                FillLine(target, laneStart);
                lineDrawing.End();
            }
        }
    }
}
