﻿using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using IDKEngine.Render.Objects;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render
{
    class ModelSystem
    {
        public const int GLSL_UBO_MAX_MATERIALS = 384;

        public Model.GLSLDrawCommand[] DrawCommands;
        public BufferObject DrawCommandBuffer;

        public Model.GLSLMesh[] Meshes;
        public BufferObject MeshBuffer;

        public Model.GLSLMaterial[] Materials;
        public BufferObject MaterialBuffer;

        public Model.GLSLVertex[] Vertices;
        public BufferObject VertexBuffer;

        public uint[] Indices;
        public BufferObject ElementBuffer;

        public readonly VAO VAO;
        public unsafe ModelSystem()
        {
            DrawCommands = new Model.GLSLDrawCommand[0];
            DrawCommandBuffer = new BufferObject();

            Meshes = new Model.GLSLMesh[0];
            MeshBuffer = new BufferObject();

            Materials = new Model.GLSLMaterial[0];
            MaterialBuffer = new BufferObject();

            Vertices = new Model.GLSLVertex[0];
            VertexBuffer = new BufferObject();

            Indices = new uint[0];
            ElementBuffer = new BufferObject();

            VAO = new VAO();
            VAO.SetElementBuffer(ElementBuffer);
            VAO.AddSourceBuffer(VertexBuffer, 0, sizeof(Model.GLSLVertex));
            VAO.SetAttribFormat(0, 0, 3, VertexAttribType.Float, 0 * sizeof(float)); // Position
            VAO.SetAttribFormat(0, 1, 2, VertexAttribType.Float, 4 * sizeof(float)); // TexCoord
            VAO.SetAttribFormat(0, 2, 3, VertexAttribType.Float, 8 * sizeof(float)); // Normals
            VAO.SetAttribFormat(0, 3, 3, VertexAttribType.Float, 12 * sizeof(float)); // Tangent
            VAO.SetAttribFormat(0, 4, 3, VertexAttribType.Float, 16 * sizeof(float)); // BiTangent
        }
        public int MaterialCount => Materials.Length;
        public unsafe void Add(Model[] models)
        {
            int oldDrawCommandsLength = DrawCommands.Length;
            int oldMeshesLength = Meshes.Length;
            int oldMaterialsLength = Materials.Length;
            int oldVerticesLength = Vertices.Length;
            int oldIndicesLength = Indices.Length;

            int loadedDrawCommands = 0;
            int loadedMeshes = 0;
            int loadedMaterials = 0;
            int loadedVertices = 0;
            int loadedIndices = 0;

            Array.Resize(ref DrawCommands, DrawCommands.Length + models.Sum(m => m.DrawCommands.Length));
            Array.Resize(ref Meshes, Meshes.Length + models.Sum(m => m.Meshes.Length));
            Array.Resize(ref Materials, Materials.Length + models.Sum(m => m.Materials.Length));
            Array.Resize(ref Vertices, Vertices.Length + models.Sum(m => m.Vertices.Length));
            Array.Resize(ref Indices, Indices.Length + models.Sum(m => m.Indices.Length));

            for (int i = 0; i < models.Length; i++)
            {
                Model model = models[i];

                Array.Copy(model.DrawCommands, 0, DrawCommands, oldDrawCommandsLength + loadedDrawCommands, model.DrawCommands.Length);
                Array.Copy(model.Meshes, 0, Meshes, oldMeshesLength + loadedMeshes, model.Meshes.Length);
                Array.Copy(model.Materials, 0, Materials, oldMaterialsLength + loadedMaterials, model.Materials.Length);
                Array.Copy(model.Vertices, 0, Vertices, oldVerticesLength + loadedVertices, model.Vertices.Length);
                Array.Copy(model.Indices, 0, Indices, oldIndicesLength + loadedIndices, model.Indices.Length);

                for (int j = oldDrawCommandsLength + loadedDrawCommands; j < oldDrawCommandsLength + loadedDrawCommands + model.DrawCommands.Length; j++)
                {
                    DrawCommands[j].BaseVertex += oldVerticesLength + loadedVertices;
                    DrawCommands[j].FirstIndex += oldIndicesLength + loadedIndices;
                }

                for (int j = oldMeshesLength + loadedMeshes; j < oldMeshesLength + loadedMeshes + model.Meshes.Length; j++)
                {
                    Meshes[j].MaterialIndex += oldMaterialsLength + loadedMaterials;
                }

                loadedDrawCommands += model.DrawCommands.Length;
                loadedMeshes += model.Meshes.Length;
                loadedMaterials += model.Materials.Length;
                loadedVertices += model.Vertices.Length;
                loadedIndices += model.Indices.Length;
            }

            DrawCommandBuffer.MutableAllocate(DrawCommands.Length * sizeof(Model.GLSLDrawCommand), DrawCommands, BufferUsageHint.StaticDraw);
            MeshBuffer.MutableAllocate(Meshes.Length * sizeof(Model.GLSLMesh), Meshes, BufferUsageHint.StaticDraw);
            MaterialBuffer.MutableAllocate(Materials.Length * sizeof(Model.GLSLMaterial), Materials, BufferUsageHint.StaticDraw);
            VertexBuffer.MutableAllocate(Vertices.Length * sizeof(Model.GLSLVertex), Vertices, BufferUsageHint.StaticDraw);
            ElementBuffer.MutableAllocate(Indices.Length * sizeof(uint), Indices, BufferUsageHint.StaticDraw);
        }

        public void Draw()
        {
            VAO.Bind();
            DrawCommandBuffer.Bind(BufferTarget.DrawIndirectBuffer);

            DrawCommandBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 0, 0, DrawCommandBuffer.Size);
            MeshBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 2, 0, MeshBuffer.Size);
            VertexBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 3, 0, VertexBuffer.Size);
            ElementBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 4, 0, ElementBuffer.Size);
            MaterialBuffer.BindBufferRange(BufferRangeTarget.UniformBuffer, 1, 0, MaterialBuffer.Size);

            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, Meshes.Length, 0);
        }

        private static readonly ShaderProgram cullingProgram = new ShaderProgram(
            new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Culling/compute.glsl")));
        public void Cull()
        {
            cullingProgram.Use();
            cullingProgram.Upload(0, Meshes.Length);

            GL.DispatchCompute((Meshes.Length + 32 - 1) / 32, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit);
        }

        private static readonly ShaderProgram aabbProgram = new ShaderProgram(
            new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Fordward/AABB/vertex.glsl")),
            new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Fordward/AABB/fragment.glsl")));
        public void DrawAABB()
        {
            GL.Disable(EnableCap.CullFace);
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);

            aabbProgram.Use();

            GL.DrawArraysInstanced(PrimitiveType.Quads, 0, 24, Meshes.Length);

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            GL.Enable(EnableCap.CullFace);
        }

        public delegate void FuncUploadMesh(ref Model.GLSLMesh glslMesh);
        /// <summary>
        /// Applies a function over the specified range of <see cref="Meshes"/>
        /// </summary>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <param name="func"></param>
        public unsafe void ForEach(int start, int count, FuncUploadMesh func)
        {
            Debug.Assert(start >= 0 && (start + count) <= Meshes.Length);

            for (int i = start; i < start + count; i++)
                func(ref Meshes[i]);

            fixed (void* ptr = &Meshes[start].Model)
            {
                MeshBuffer.SubData(start * sizeof(Model.GLSLMesh), count * sizeof(Model.GLSLMesh), (IntPtr)ptr);
            }
        }


        public delegate void FuncUploadDrawCommand(ref Model.GLSLDrawCommand drawCommand);
        /// <summary>
        /// Applies a function over the specefied range of <see cref="DrawCommands"/>
        /// </summary>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <param name="func"></param>
        public unsafe void ForEach(int start, int count, FuncUploadDrawCommand func)
        {
            Debug.Assert(start >= 0 && (start + count) <= DrawCommands.Length);

            for (int i = start; i < start + count; i++)
                func(ref DrawCommands[i]);

            fixed (void* ptr = &DrawCommands[start])
            {
                DrawCommandBuffer.SubData(start * sizeof(Model.GLSLDrawCommand), count * sizeof(Model.GLSLDrawCommand), (IntPtr)ptr);
            }
        }
    }
}