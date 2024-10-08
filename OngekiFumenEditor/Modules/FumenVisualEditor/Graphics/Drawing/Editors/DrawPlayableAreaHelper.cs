﻿using Advanced.Algorithms.Geometry;
using Caliburn.Micro;
using OngekiFumenEditor.Base.OngekiObjects;
using OngekiFumenEditor.Base;
using OngekiFumenEditor.Kernel.Graphics;
using System;
using System.Collections.Generic;
using static OngekiFumenEditor.Kernel.Graphics.ILineDrawing;
using static OngekiFumenEditor.Utils.MathUtils;
using System.Linq;
using OngekiFumenEditor.Utils;
using System.Numerics;
using OngekiFumenEditor.Utils.ObjectPool;
using EarcutNet;
using System.Drawing;
using Microsoft.CodeAnalysis;
using System.Diagnostics;
using OngekiFumenEditor.Base.OngekiObjects.BulletPalleteEnums;
using Xv2CoreLib.HslColor;

namespace OngekiFumenEditor.Modules.FumenVisualEditor.Graphics.Drawing.Editors
{
    public class DrawPlayableAreaHelper
    {
        private ILineDrawing lineDrawing;
        private IPolygonDrawing polygonDrawing;
        private ICircleDrawing circleDrawing;

        private Vector4 playFieldForegroundColor;
        private bool enablePlayFieldDrawing;

        LineVertex[] vertices = new LineVertex[2];

        public DrawPlayableAreaHelper()
        {
            lineDrawing = IoC.Get<ISimpleLineDrawing>();
            polygonDrawing = IoC.Get<IPolygonDrawing>();
            circleDrawing = IoC.Get<ICircleDrawing>();

            UpdateProps();
            Properties.EditorGlobalSetting.Default.PropertyChanged += Default_PropertyChanged;
        }

        private void Default_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Properties.EditorGlobalSetting.EnablePlayFieldDrawing):
                case nameof(Properties.EditorGlobalSetting.PlayFieldForegroundColor):
                    UpdateProps();
                    break;
                default:
                    break;
            }
        }

        private void UpdateProps()
        {
            enablePlayFieldDrawing = Properties.EditorGlobalSetting.Default.EnablePlayFieldDrawing;
            playFieldForegroundColor = Color.FromArgb(Properties.EditorGlobalSetting.Default.PlayFieldForegroundColor).ToVector4();
        }

        public void Draw(IFumenEditorDrawingContext target)
        {
            if (target.Editor.IsDesignMode)
                DrawAudioDuration(target);
        }

        private void DrawAudioDuration(IFumenEditorDrawingContext target)
        {
            var y = (float)target.Editor.TotalDurationHeight;

            var color = new Vector4(1, 0, 0, 1);
            vertices[0] = new(new(0, y), color, VertexDash.Solider);
            vertices[1] = new(new(target.ViewWidth, y), color, VertexDash.Solider);

            lineDrawing.Draw(target, vertices, 3);
        }

        public void DrawPlayField(IFumenEditorDrawingContext target, TGrid minTGrid, TGrid maxTGrid)
        {
            if (target.Editor.IsDesignMode || !enablePlayFieldDrawing)
                return;

            /*
             画游戏(黑色可移动)区域
                1. 计算一组轨道，每个轨道的节点都算一个point，如果存在轨道相交，那么相交点也算point
                   如果一个水平面(即y相同)存在多个轨道头尾节点，那么就会分别算point
                2. 排列 point集合, 然后简化point和补全point
                3. 将 points集合两两成线，得到线的range[minY, maxY] , 得到Y对应的轨道以及在范围range内轨道所有节点
                4. 将左右所有的节点合并成一个多边形，渲染
             */

            const long defaultLeftX = -24 * XGrid.DEFAULT_RES_X;
            const long defaultRightX = 24 * XGrid.DEFAULT_RES_X;

            var fumen = target.Editor.Fumen;
            var currentTGrid = target.Editor.GetCurrentTGrid();

            void EnumeratePoints(bool isRight, List<Vector2> result)
            {
                var defaultX = isRight ? defaultRightX : defaultLeftX;
                var type = isRight ? LaneType.WallRight : LaneType.WallLeft;
                var ranges = CombinableRange<int>.CombineRanges(fumen.Lanes.GetVisibleStartObjects(minTGrid, maxTGrid)
                    .Where(x => x.LaneType == type)
                    .Select(x => new CombinableRange<int>(x.MinTGrid.TotalGrid, x.MaxTGrid.TotalGrid)))
                    .OrderBy(x => isRight ? x.Max : x.Min).ToArray();

                var points = ObjectPool<HashSet<float>>.Get();
                points.Clear();

                var prevX = (float)Random();
                var prevY = (float)Random();

                void appendPoint2(List<Vector2> list, float totalXGrid, float totalTGrid)
                {
                    var px = (float)XGridCalculator.ConvertXGridToX(totalXGrid / XGrid.DEFAULT_RES_X, target.Editor);
                    var py = (float)target.ConvertToY(totalTGrid / TGrid.DEFAULT_RES_T);

                    appendPoint3(list, px, py, list.Count);
                }

                void appendPoint3(List<Vector2> list, float px, float py, int insertIdx)
                {
                    var po = list.ElementAtOrDefault(insertIdx);
                    if (po.X == px && po.Y == py)
                        return;

                    var p = new Vector2(px, py);
                    list.Insert(insertIdx, p);

                    //DebugPrintPoint(p, isRight, false, 10);
                }

                void appendPoint(List<Vector2> list, XGrid xGrid, float y)
                {
                    if (xGrid is null)
                        return;
                    appendPoint2(list, xGrid.TotalGrid, y);
                }

                for (int i = 0; i < ranges.Length; i++)
                {
                    var curRange = ranges[i];
                    var nextRange = ranges.ElementAtOrDefault(i + 1);

                    var lanes = fumen.Lanes
                        .GetVisibleStartObjects(TGrid.FromTotalGrid(curRange.Min), TGrid.FromTotalGrid(curRange.Max))
                        .Where(x => x.LaneType == type)
                        .ToArray();

                    var polylines = lanes.Select(x => x.GenAllPath().Where(x => minTGrid.TotalGrid <= x.pos.Y && x.pos.Y <= maxTGrid.TotalGrid).Select(x => x.pos).SequenceConsecutivelyWrap(2).Select(x => (x.FirstOrDefault(), x.LastOrDefault())).ToArray())
                        .ToArray();

                    for (int r = 0; r < polylines.Length; r++)
                    {
                        var polylineA = polylines[r];
                        for (int t = r + 1; t < polylines.Length; t++)
                        {
                            var polylineB = polylines[t];

                            for (int ai = 0; ai < polylineA.Length; ai++)
                            {
                                for (int bi = 0; bi < polylineB.Length; bi++)
                                {
                                    var a = polylineA[ai];
                                    var b = polylineB[bi];

                                    if (a == b)
                                        continue;

                                    var lineA = new Line(new(a.Item1.X, a.Item1.Y), new(a.Item2.X, a.Item2.Y));
                                    var lineB = new Line(new(b.Item1.X, b.Item1.Y), new(b.Item2.X, b.Item2.Y));

                                    var point = LineIntersection.Find(lineA, lineB);
                                    if (point is not null)
                                        points.Add((float)point.Y);
                                }
                            }
                        }
                    }

                    points.AddRange(lanes
                        .Select(x => (float)x.TGrid.TotalGrid)
                        .Concat(lanes.Select(x => x.Children.LastOrDefault())
                        .FilterNull()
                        .Select(x => (float)x.TGrid.TotalGrid))
                        .Where(x => curRange.Min <= x && x <= curRange.Max)
                        );
                }

                var sortedPoints = points.Where(x => minTGrid.TotalGrid < x && x < maxTGrid.TotalGrid).OrderBy(x => x).ToList();
                /*
                if (sortedPoints.Count == 0 || sortedPoints.FirstOrDefault() > minTGrid.TotalGrid)
                    sortedPoints.Insert(0, minTGrid.TotalGrid);
                */
                sortedPoints.InsertBySortBy(minTGrid.TotalGrid, x => x);
                sortedPoints.InsertBySortBy(maxTGrid.TotalGrid, x => x);

                var segments = sortedPoints.SequenceConsecutivelyWrap(2).Select(x => (x.FirstOrDefault(), x.LastOrDefault())).ToArray();

                foreach ((var fromY, var toY) in segments)
                {
                    var midY = ((fromY + toY) / 2);
                    var midTGrid = TGrid.FromTotalGrid((int)midY);

                    //获取这个segement范围内要选取的轨道
                    var pickables = fumen.Lanes
                            .GetVisibleStartObjects(midTGrid, midTGrid)
                            .Where(x => x.LaneType == type)
                            .Select(x => (x.CalulateXGrid(midTGrid), x))
                            .FilterNullBy(x => x.Item1)
                            .ToArray();

                    //选取轨道，如果存在多个轨道(即轨道交叉冲突了)，那么就按左右边规则选取一个
                    (_, var pickLane) = pickables.IsEmpty() ? default : (isRight ? pickables.MaxBy(x => x.Item1) : pickables.MinBy(x => x.Item1));
                    if (pickLane is not null)
                    {
                        var fromTGrid = TGrid.FromTotalGrid((int)fromY);
                        appendPoint(result, pickLane.CalulateXGrid(fromTGrid), fromY);

                        foreach (var pos in pickLane.GenAllPath().Select(x => x.pos).SkipWhile(x => x.Y < fromY).TakeWhile(x => x.Y < toY))
                            appendPoint2(result, pos.X, pos.Y);

                        var toTGrid = TGrid.FromTotalGrid((int)toY);
                        appendPoint(result, pickLane.CalulateXGrid(toTGrid), toY);
                    }
                    else
                    {
                        //选取不到轨道，表示这个segement是两个轨道之间的空白区域，那么直接填上空白就行
                        appendPoint2(result, defaultX, fromY);
                        appendPoint2(result, defaultX, toY);
                    }
                }

                //todo 解决变速过快导致的精度丢失问题
                Vector2? interpolate(TGrid tGrid, float actualY, out bool isPickLane)
                {
                    isPickLane = false;
                    var pickables = fumen.Lanes
                            .GetVisibleStartObjects(tGrid, tGrid)
                            .Where(x => x.LaneType == type)
                            .Where(x =>
                            {
                                var laneMinY = target.ConvertToY(x.MinTGrid);
                                var laneMaxY = target.ConvertToY(x.MaxTGrid);

                                return laneMinY <= actualY && actualY <= laneMaxY;
                            })
                            .Select(x => (x.CalulateXGrid(tGrid), x))
                            .FilterNullBy(x => x.Item1)
                            .ToArray();

                    (_, var pickLane) = pickables.IsEmpty() ? default : (isRight ? pickables.MaxBy(x => x.Item1) : pickables.MinBy(x => x.Item1));

                    if (pickLane is not null)
                    {
                        var itor = pickLane.GenAllPath().GetEnumerator();
                        var prevOpt = default(OpenTK.Mathematics.Vector2?);

                        while (itor.MoveNext())
                        {
                            var cur = itor.Current.pos;

                            if (cur.Y > tGrid.TotalGrid)
                            {
                                // prev ------------ cur
                                //           ^
                                //         tGrid

                                if (prevOpt is OpenTK.Mathematics.Vector2 prev)
                                {
                                    var curPx = (float)XGridCalculator.ConvertXGridToX(cur.X / XGrid.DEFAULT_RES_X, target.Editor);
                                    var curPy = (float)target.ConvertToY(cur.Y / TGrid.DEFAULT_RES_T);
                                    var prevPx = (float)XGridCalculator.ConvertXGridToX(prev.X / XGrid.DEFAULT_RES_X, target.Editor);
                                    var prevPy = (float)target.ConvertToY(prev.Y / TGrid.DEFAULT_RES_T);

                                    var nowPy = actualY;
                                    var nowPx = (float)MathUtils.CalculateXFromTwoPointFormFormula(nowPy, prevPx, prevPy, curPx, curPy);
                                    isPickLane = true;
                                    return new(nowPx, nowPy);
                                }
                            }

                            prevOpt = cur;
                        }
                    }
                    else
                    {
                        var defaultPx = (float)XGridCalculator.ConvertXGridToX(defaultX / XGrid.DEFAULT_RES_X, target.Editor);
                        return new(defaultPx, actualY);
                    }

                    return default;
                }

                if (minTGrid <= currentTGrid && currentTGrid <= maxTGrid)
                {
                    var maxY = target.ConvertToY(maxTGrid);
                    var actualMaxY = target.Rect.TopLeft.Y;

                    var maxDiff = maxY - actualMaxY;
                    if (Math.Abs(maxDiff) >= 1)
                    {
                        if (interpolate(maxTGrid, (float)Math.Max(actualMaxY, maxY), out var isPickLane) is Vector2 pp)
                        {
                            if (!isPickLane)
                                appendPoint3(result, (float)XGridCalculator.ConvertXGridToX(defaultX / XGrid.DEFAULT_RES_X, target.Editor), result.LastOrDefault().Y, result.Count);
                            appendPoint3(result, pp.X, pp.Y, result.Count);
                        }
                    }

                    var minY = target.ConvertToY(minTGrid);
                    var actualMinY = target.Rect.ButtomRight.Y;

                    var minDiff = minY - actualMinY;
                    if (Math.Abs(minDiff) >= 1)
                    {
                        if (interpolate(minTGrid, (float)Math.Max(actualMinY, minY), out var isPickLane) is Vector2 pp)
                        {
                            if (!isPickLane)
                                appendPoint3(result, (float)XGridCalculator.ConvertXGridToX(defaultX / XGrid.DEFAULT_RES_X, target.Editor), result.FirstOrDefault().Y, 0);
                            appendPoint3(result, pp.X, pp.Y, 0);
                        }
                    }
                }

                ObjectPool<HashSet<float>>.Return(points);
            }

            using var d3 = ObjectPool<List<double>>.GetWithUsingDisposable(out var points, out _);
            points.Clear();
            using var d4 = ObjectPool<List<int>>.GetWithUsingDisposable(out var idxList, out _);
            idxList.Clear();

            void FillPoints(List<Vector2> ps, bool isRight)
            {
                var s = points.Count / 2;

                for (var i = 0; i < ps.Count; i++)
                {
                    var cur = ps[i];

                    //remove dup
                    if (points.Count >= 2)
                    {
                        var prevY = points[^1];
                        var prevX = points[^2];

                        if (prevY == cur.Y && prevX == cur.X)
                            continue;
                    }

                    //optimze prev point if able
                    if (points.Count >= 4)
                    {
                        var prevY = points[^1];
                        var prevX = points[^2];

                        if ((prevY == cur.Y && prevY == points[^3]) || (prevX == cur.X && prevX == points[^4]))
                        {
                            //remove
                            points.RemoveAt(points.Count - 1);
                            points.RemoveAt(points.Count - 1);
                        }
                    }

                    points.Add(cur.X);
                    points.Add(cur.Y);
                }

                if (isRight)
                {
                    for (var j = s; j < points.Count / 2; j++)
                        DebugPrintPoint(new((float)points[2 * j], (float)points[2 * j + 1]), isRight, true, 10);
                }
            }

            using var d = ObjectPool<List<Vector2>>.GetWithUsingDisposable(out var leftPoints, out _);
            leftPoints.Clear();
            using var d2 = ObjectPool<List<Vector2>>.GetWithUsingDisposable(out var rightPoints, out _);
            rightPoints.Clear();

            BeginDebugPrint(target);

            //计算左边墙的点
            EnumeratePoints(false, leftPoints);
            FillPoints(leftPoints, false);
            //计算右边墙的点，因为要组合成一个多边形，所以右半部分得翻转一下顺序
            EnumeratePoints(true, rightPoints);
            rightPoints.Reverse();
            FillPoints(rightPoints, true);
            //todo 解决左右墙交叉处理问题

            EndDebugPrint();

            var tessellateList = ObjectPool<List<int>>.Get();
            tessellateList.Clear();
            Earcut.Tessellate(points, idxList, tessellateList);

            var r = string.Join(Environment.NewLine, points.SequenceWrap(2).Select(x => $"{x.FirstOrDefault(),-20}{x.LastOrDefault()}"));

            polygonDrawing.Begin(target, OpenTK.Graphics.OpenGL.PrimitiveType.Triangles);

            var i = 0;
            foreach (var seq in tessellateList.SequenceWrap(3))
            {
                var normal = i * 1.0f / tessellateList.Count / 2 + 0.5;
                var hslColor = new HslColor(0.55, normal, 1);

                var rgb = hslColor.ToRgb();
                var color = new Vector4((float)rgb.R, (float)rgb.G, (float)rgb.B, 0.25f);

                foreach (var idx in seq)
                {
                    var x = (float)points[idx * 2 + 0];
                    var y = (float)points[idx * 2 + 1];
                    polygonDrawing.PostPoint(new(x, y), playFieldForegroundColor);
                }
                i += 3;
            }
            polygonDrawing.End();
            /*
            i = 0;
            foreach (var seq in tessellateList.SequenceWrap(3))
            {
                var normal = i * 1.0f / tessellateList.Count / 2 + 0.5;
                var hslColor = new HslColor(0.55, normal, 1);

                var rgb = hslColor.ToRgb();
                var color = new Vector4((float)rgb.R, (float)rgb.G, (float)rgb.B, 1);
                lineDrawing.Draw(target, seq.Append(seq.FirstOrDefault()).Select(idx => new LineVertex(new((float)points[idx * 2 + 0], (float)points[idx * 2 + 1]), color, new(2, 2))), 3);
                i += 3;
            }
            */
            ObjectPool<List<int>>.Return(tessellateList);
        }

        [Conditional("DEBUG")]
        private void DebugPrintPoint(Vector2 p, bool isRight, bool v1, int v2)
        {
            var color = isRight ? new Vector4(1, 0, 0, 0.5f) : new Vector4(0, 1, 0, 0.5f);
            circleDrawing.Post(p, color, false, v2);
        }

        [Conditional("DEBUG")]
        private void BeginDebugPrint(IFumenEditorDrawingContext target)
        {
            circleDrawing.Begin(target);
        }

        [Conditional("DEBUG")]
        private void EndDebugPrint()
        {
            circleDrawing.End();
        }
    }
}
