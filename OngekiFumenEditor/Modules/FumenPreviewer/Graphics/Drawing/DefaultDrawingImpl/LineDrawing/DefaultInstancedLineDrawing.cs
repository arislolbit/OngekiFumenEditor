﻿/*
Code copied and modified from https://github.com/mhalber/Lines/blob/master/instancing_lines.h
*/

using Caliburn.Micro;
using OpenTK.Graphics.OpenGL;
using SharpVectors.Dom.Svg;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using static OngekiFumenEditor.Modules.FumenPreviewer.Graphics.Drawing.ILineDrawing;

namespace OngekiFumenEditor.Modules.FumenPreviewer.Graphics.Drawing.DefaultDrawingImpl.LineDrawing
{
    //[Export(typeof(ILineDrawing))]
    [Export(typeof(ISimpleLineDrawing))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    internal class DefaultInstancedLineDrawing : CommonDrawingBase, ISimpleLineDrawing, IDisposable
    {
        public const int MAX_VERTS = 300000;
        /*
            typedef struct vertex
            {
                union
                {
                    struct { msh_vec3_t pos; float width; };
                    msh_vec4_t pos_width;
                };
                msh_vec4_t col;
            } vertex_t;
         */
        public const int VertexTBytesSize = sizeof(float) * (3 + 1 + 4);

        private int vao;
        private int line_vbo;
        private int quad_vbo;
        private int quad_ebo;

        private IPerfomenceMonitor perfomenceMonitor;
        private Shader shader;

        private int quad_pos;
        private int line_pos_width_a;
        private int line_col_a;
        private int line_pos_width_b;
        private int line_col_b;

        private int mvp;
        private int viewport_size;
        private int aa_radius;

        private float[] PostData { get; } = new float[MAX_VERTS];
        private int postDataFillIndex = 0;
        private int postDataFillCount = 0;
        private IFumenPreviewer target;
        private float lineWidth;

        public DefaultInstancedLineDrawing()
        {
            perfomenceMonitor = IoC.Get<IPerfomenceMonitor>();

            shader = new InstancedLineShader();
            shader.Compile();

            quad_pos = shader.GetAttribLocation("quad_pos");
            line_pos_width_a = shader.GetAttribLocation("line_pos_width_a");
            line_col_a = shader.GetAttribLocation("line_col_a");
            line_pos_width_b = shader.GetAttribLocation("line_pos_width_b");
            line_col_b = shader.GetAttribLocation("line_col_b");

            mvp = shader.GetUniformLocation("u_mvp");
            aa_radius = shader.GetUniformLocation("u_aa_radius");
            viewport_size = shader.GetUniformLocation("u_viewport_size");

            int binding_idx = 0;

            vao = GL.GenVertexArray();

            GL.BindVertexArray(vao);
            {
                line_vbo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, line_vbo);
                {
                    GL.NamedBufferStorage(line_vbo, PostData.Length * sizeof(float), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
                }
                GL.VertexArrayVertexBuffer(vao, binding_idx, line_vbo, IntPtr.Zero, VertexTBytesSize);
                GL.VertexArrayBindingDivisor(vao, binding_idx, 1);

                GL.EnableVertexArrayAttrib(vao, line_pos_width_a);
                GL.VertexArrayAttribFormat(vao, line_pos_width_a, 4, VertexAttribType.Float, false, 0);
                GL.VertexArrayAttribBinding(vao, line_pos_width_a, binding_idx);

                GL.EnableVertexArrayAttrib(vao, line_col_a);
                GL.VertexArrayAttribFormat(vao, line_col_a, 4, VertexAttribType.Float, false, 4 * sizeof(float));
                GL.VertexArrayAttribBinding(vao, line_col_a, binding_idx);

                GL.EnableVertexArrayAttrib(vao, line_pos_width_b);
                GL.VertexArrayAttribFormat(vao, line_pos_width_b, 4, VertexAttribType.Float, false, VertexTBytesSize);
                GL.VertexArrayAttribBinding(vao, line_pos_width_b, binding_idx);

                GL.EnableVertexArrayAttrib(vao, line_col_b);
                GL.VertexArrayAttribFormat(vao, line_col_b, 4, VertexAttribType.Float, false, VertexTBytesSize + 4 * sizeof(float));
                GL.VertexArrayAttribBinding(vao, line_col_b, binding_idx);

                binding_idx++;

                // Figure out this parametrization.
                var quad = new[]{
                    0.0f, -1.0f, 0.0f,
                    0.0f, 1.0f, 0.0f,
                    1.0f, 1.0f, 0.0f,
                    1.0f, -1.0f, 0.0f  };
                var ind = new ushort[] { 0, 1, 2, 0, 2, 3 };


                quad_vbo = GL.GenBuffer();
                quad_ebo = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, quad_vbo);
                {
                    GL.NamedBufferStorage(quad_vbo, quad.Length * sizeof(float), quad, BufferStorageFlags.DynamicStorageBit);
                }
                GL.BindBuffer(BufferTarget.ArrayBuffer, quad_ebo);
                {
                    GL.NamedBufferStorage(quad_ebo, ind.Length * sizeof(ushort), ind, BufferStorageFlags.DynamicStorageBit);
                }
                GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
                GL.VertexArrayVertexBuffer(vao, binding_idx, quad_vbo, IntPtr.Zero, 3 * sizeof(float));
                GL.VertexArrayElementBuffer(vao, quad_ebo);

                GL.EnableVertexArrayAttrib(vao, quad_pos);
                GL.VertexArrayAttribFormat(vao, quad_pos, 3, VertexAttribType.Float, false, 0);
                GL.VertexArrayAttribBinding(vao, quad_pos, binding_idx);
            }
            GL.BindVertexArray(0);
        }

        private void FlushDraw()
        {
            if (postDataFillCount > 1)
            {
                GL.NamedBufferSubData(line_vbo, IntPtr.Zero, postDataFillIndex, PostData);

                GL.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedShort, IntPtr.Zero, postDataFillCount - 1);
                perfomenceMonitor.CountDrawCall(this);
            }

            postDataFillIndex = postDataFillCount = 0;
        }

        public void Dispose()
        {
            shader.Dispose();

            GL.DeleteBuffer(line_vbo);
            GL.DeleteBuffer(quad_vbo);
            GL.DeleteBuffer(quad_ebo);
            GL.DeleteVertexArray(vao);
        }

        public void Begin(IFumenPreviewer target, float lineWidth)
        {
            perfomenceMonitor.OnBeginDrawing(this);
            this.target = target;
            this.lineWidth = lineWidth;


            shader.Begin();
            GL.BindVertexArray(vao);
            var mvpMatrix = GetOverrideModelMatrix() * target.ViewProjectionMatrix;
            shader.PassUniform(mvp, mvpMatrix);
            shader.PassUniform(viewport_size, new OpenTK.Mathematics.Vector2(target.ViewWidth, target.ViewHeight));
            shader.PassUniform(aa_radius, new OpenTK.Mathematics.Vector2(2, 2));

        }

        private void PostPointInternal(Vector2 Point, Vector4 Color)
        {
            var buffer = PostData.AsSpan().Slice(postDataFillIndex / sizeof(float), VertexTBytesSize / sizeof(float));

            buffer[0] = Point.X;
            buffer[1] = Point.Y;
            buffer[2] = 0;

            buffer[3] = lineWidth;

            buffer[4] = Color.X;
            buffer[5] = Color.Y;
            buffer[6] = Color.Z;
            buffer[7] = Color.W;

            postDataFillIndex += VertexTBytesSize;
            postDataFillCount++;
        }

        Vector2 prevPoint = default;
        Vector4 prevColor = default;

        public void PostPoint(Vector2 Point, Vector4 Color)
        {
            if (postDataFillIndex + VertexTBytesSize >= MAX_VERTS)
            {
                FlushDraw();
                PostPointInternal(prevPoint, prevColor);
            }

            PostPointInternal(Point, Color);
        }

        public void End()
        {
            FlushDraw();

            GL.BindVertexArray(0);
            shader.End();

            perfomenceMonitor.OnAfterDrawing(this);
        }

        public void Draw(IFumenPreviewer target, IEnumerable<LineVertex> points, float lineWidth)
        {
            Begin(target, lineWidth);
            foreach (var point in points)
                PostPoint(point.Point, point.Color);
            End();
        }
    }
}
