﻿using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.Render;
using IDKEngine.Render.Objects;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace IDKEngine
{
    /// <summary>
    /// This class represents the engine which can be run inside of an OpenGL context
    /// </summary>
    class Application : GameWindowBase
    {
        public Application(int width, int height, string title)
            : base(width, height, title)
        {

        }
        public const float EPSILON = 0.001f;
        public const float NEAR_PLANE = 0.01f, FAR_PLANE = 500.0f;

        public bool IsPathTracing = false, IsVolumetricLighting = true, IsSSAO = true, IsSSR = false;
        public int FPS;

        private int fps;
        protected override unsafe void OnRender(float dT)
        {
            basicDataUBO.SubData(0, sizeof(GLSLBasicData), GLSLBasicData);

            if (!IsPathTracing)
            {
                // Compute last frames SSAO
                if (IsSSAO)
                    SSAO.Compute(ForwardRenderer.Depth, ForwardRenderer.NormalSpec);

                GL.ColorMask(false, false, false, false);
                for (int i = 0; i < pointShadows.Length; i++)
                {
                    pointShadows[i].CreateDepthMap(ModelSystem);
                }
                GL.ColorMask(true, true, true, true);

                ModelSystem.ViewCull(ref GLSLBasicData.ProjView);

                GL.Viewport(0, 0, Size.X, Size.Y);
                ForwardRenderer.Render(ModelSystem, AtmosphericScatterer.Result, IsSSAO ? SSAO.Result : null);
                
                GL.BlendEquation(BlendEquationMode.FuncAdd);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                ParticleSimulator.Render(dT);

                if (IsVolumetricLighting)
                    VolumetricLight.Compute(ForwardRenderer.Depth);

                if (IsSSR)
                    SSR.Compute(ForwardRenderer.Result, ForwardRenderer.NormalSpec, ForwardRenderer.Depth, AtmosphericScatterer.Result);

                PostCombine.Compute(ForwardRenderer.Result, IsVolumetricLighting ? VolumetricLight.Result : null, IsSSR ? SSR.Result : null);
            }
            else
            {
                PathTracer.Render();
                Texture.UnbindFromUnit(1);
                Texture.UnbindFromUnit(2);

                PostCombine.Compute(PathTracer.Result, null, null);
            }
            PostCombine.Result.BindToUnit(0);

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);

            GL.Viewport(0, 0, Size.X, Size.Y);
            Framebuffer.Bind(0);
            finalProgram.Use();

            GL.DrawArrays(PrimitiveType.Quads, 0, 4);
            Gui.Render(this, (float)dT);

            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);

            fps++;
            GLSLBasicData.FrameCount++;
        }

        private readonly Stopwatch fpsTimer = Stopwatch.StartNew();
        protected override void OnUpdate(float dT)
        {
            if (fpsTimer.ElapsedMilliseconds >= 1000)
            {
                FPS = fps;
                Title = $"FPS: {FPS}; Position {camera.Position};";
                fps = 0;
                fpsTimer.Restart();
            }

            if (IsFocused)
            {
                if (KeyboardState[Keys.Escape] == InputState.Pressed)
                    ShouldClose();
                
                if (KeyboardState[Keys.V] == InputState.Touched)
                    IsVSync = !IsVSync;

                if (KeyboardState[Keys.F11] == InputState.Touched)
                    IsFullscreen = !IsFullscreen;

                if (KeyboardState[Keys.E] == InputState.Touched && !ImGuiNET.ImGui.GetIO().WantCaptureKeyboard)
                {
                    if (MouseState.CursorMode == CursorModeValue.CursorDisabled)
                    {
                        MouseState.CursorMode = CursorModeValue.CursorNormal;
                        Gui.ImGuiController.IsIgnoreMouseInput = false;
                        camera.Velocity = Vector3.Zero;
                    }
                    else
                    {
                        MouseState.CursorMode = CursorModeValue.CursorDisabled;
                        Gui.ImGuiController.IsIgnoreMouseInput = true;
                    }
                }

                ParticleSimulator.ProcessInputs(this, camera, GLSLBasicData);
                if (MouseState.CursorMode == CursorModeValue.CursorDisabled)
                {
                    camera.ProcessInputs(KeyboardState, MouseState, dT, out bool hadCameraInputs);
                    if (hadCameraInputs && IsPathTracing)
                        GLSLBasicData.FrameCount = 0;
                }

                Gui.Update(this);

                GLSLBasicData.PrevProjView = GLSLBasicData.View * GLSLBasicData.Projection;
                GLSLBasicData.ProjView = camera.View * GLSLBasicData.Projection;
                GLSLBasicData.View = camera.View;
                GLSLBasicData.InvView = camera.View.Inverted();
                GLSLBasicData.CameraPos = camera.Position;
                GLSLBasicData.InvProjView = (GLSLBasicData.View * GLSLBasicData.Projection).Inverted();
            }
        }

        private Camera camera;
        private ShaderProgram finalProgram;
        private BufferObject basicDataUBO;
        private PointShadow[] pointShadows;
        public ModelSystem ModelSystem;
        public ParticleSimulator ParticleSimulator;
        public Forward ForwardRenderer;
        public SSR SSR;
        public SSAO SSAO;
        public PostCombine PostCombine;
        public BVH Bvh;
        public VolumetricLighter VolumetricLight;
        public GaussianBlur GaussianBlur;
        public AtmosphericScatterer AtmosphericScatterer;
        public PathTracer PathTracer;
        public GLSLBasicData GLSLBasicData;
        protected override unsafe void OnStart()
        {
            Console.WriteLine($"API: {GL.GetString(StringName.Version)}");
            Console.WriteLine($"GPU: {GL.GetString(StringName.Renderer)}\n\n");
            // Necessary extensions without fallback
            // I don't think I have to test for <4.4 extensions if the system already has bindless and all
            if (!Helper.IsExtensionsAvailable("GL_ARB_bindless_texture"))
                throw new NotSupportedException("Your system does not support GL_ARB_bindless_texture");

            if (!Helper.IsCoreExtensionAvailable("GL_ARB_shader_draw_parameters", 4.6))
                throw new NotSupportedException("Your system does not support GL_ARB_shader_draw_parameters");

            if (!Helper.IsCoreExtensionAvailable("GL_ARB_direct_state_access", 4.5))
                throw new NotSupportedException("Your system does not support GL_ARB_direct_state_access");

            if (!Helper.IsCoreExtensionAvailable("GL_ARB_buffer_storage", 4.4))
                throw new NotSupportedException("Your system does not support GL_ARB_buffer_storage");

            GL.PointSize(1.3f);
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
#if DEBUG
            GL.Enable(EnableCap.DebugOutput);
            GL.DebugMessageCallback(Helper.DebugCallback, IntPtr.Zero);
#endif
            IsVSync = true;
            MouseState.CursorMode = CursorModeValue.CursorDisabled;
            Gui.ImGuiController.IsIgnoreMouseInput = true;

            camera = new Camera(new Vector3(0.0f, 5.0f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f), -90.0f, 0.0f, 0.1f, 0.25f);

            Model sponza = new Model("res/models/OBJSponza/sponza.obj");
            for (int i = 0; i < sponza.Meshes.Length; i++) // 0.0145f
                sponza.Meshes[i].Model = Matrix4.CreateScale(5.0f) * Matrix4.CreateTranslation(0.0f, -1.0f, 0.0f);

            Model horse = new Model("res/models/Horse/horse.gltf");
            for (int i = 0; i < horse.Meshes.Length; i++)
                horse.Meshes[i].Model = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(120.0f)) * Matrix4.CreateScale(25.0f) * Matrix4.CreateTranslation(-12.0f, -1.05f, 0.5f);

            ModelSystem = new ModelSystem();
            ModelSystem.Add(new Model[] { sponza, horse });

            GLSLLight[] lights = new GLSLLight[2];
            lights[0] = new GLSLLight(new Vector3(-6.0f, 21.0f, 2.95f), new Vector3(4.585f, 4.725f, 2.56f) * 10.0f, 1.0f);
            //lights[0] = new GLSLLight(new Vector3(-6.0f, 21.0f, -0.95f), new Vector3(4.585f, 4.725f, 2.56f) * 900.0f, 0.2f);
            lights[1] = new GLSLLight(new Vector3(-13.5f, 4.7f, 1.0f), new Vector3(0.5f, 0.8f, 0.9f) * 3.0f, 0.5f);


            Random rng = new Random();
            GLSLParticle[] particles = new GLSLParticle[1000];
            for (int i = 0; i < particles.Length; i++)
                particles[i].Position = new Vector3((float)rng.NextDouble() * 40 - 20, (float)rng.NextDouble() * 40 - 20, (float)rng.NextDouble() * 40 - 20);
            ParticleSimulator = new ParticleSimulator(particles);
            ForwardRenderer = new Forward(new Lighter(20, 20), Size.X, Size.Y);
            ForwardRenderer.LightingContext.Add(lights);
            SSR = new SSR(Size.X, Size.Y, 30, 8, 50.0f);
            VolumetricLight = new VolumetricLighter(Size.X, Size.Y, 20, 0.758f, 50.0f, new Vector3(0.025f));
            GaussianBlur = new GaussianBlur(Size.X, Size.Y);
            SSAO = new SSAO(Size.X, Size.Y, 16, 0.25f, 2.0f);
            PostCombine = new PostCombine(Size.X, Size.Y);
            AtmosphericScatterer = new AtmosphericScatterer(256);
            AtmosphericScatterer.Compute();

            Bvh = new BVH(ModelSystem);
            PathTracer = new PathTracer(Bvh, ModelSystem, AtmosphericScatterer.Result, Size.X, Size.Y);
            /// Driver bug: Global seamless cubemap feature may be ignored when sampling from uniform samplerCube
            /// in Compute Shader with ARB_bindless_texture activated. So try switching to seamless_cubemap_per_texture
            /// More info: https://stackoverflow.com/questions/68735879/opengl-using-bindless-textures-on-sampler2d-disables-texturecubemapseamless
            if (Helper.IsExtensionsAvailable("GL_AMD_seamless_cubemap_per_texture") || Helper.IsExtensionsAvailable("GL_ARB_seamless_cubemap_per_texture"))
                AtmosphericScatterer.Result.SetSeamlessCubeMapPerTextureARB_AMD(true);

            pointShadows = new PointShadow[2];
            pointShadows[0] = new PointShadow(ForwardRenderer.LightingContext, 0, 1536, 1.0f, 60.0f);
            pointShadows[1] = new PointShadow(ForwardRenderer.LightingContext, 1, 256, 0.5f, 60.0f);

            
            pointShadows[0].CreateDepthMap(ModelSystem);
            pointShadows[1].CreateDepthMap(ModelSystem);

            basicDataUBO = new BufferObject();
            basicDataUBO.ImmutableAllocate(sizeof(GLSLBasicData), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            basicDataUBO.BindBufferRange(BufferRangeTarget.UniformBuffer, 0, 0, basicDataUBO.Size);

            Image<Rgba32> img = SixLabors.ImageSharp.Image.Load<Rgba32>("res/textures/blueNoise/LDR_RGBA_1024.png");
            Texture blueNoise = new Texture(TextureTarget2d.Texture2D);
            blueNoise.ImmutableAllocate(img.Width, img.Height, 1, SizedInternalFormat.Rgba8);
            fixed (void* ptr = img.GetPixelRowSpan(0))
            {
                blueNoise.SubTexture2D(img.Width, img.Height, PixelFormat.Rgba, PixelType.UnsignedByte, (IntPtr)ptr);
            }
            BufferObject blueNoiseUBO = new BufferObject();
            blueNoiseUBO.ImmutableAllocate(sizeof(long), blueNoise.MakeHandleResidentARB(), BufferStorageFlags.DynamicStorageBit);
            blueNoiseUBO.BindBufferRange(BufferRangeTarget.UniformBuffer, 4, 0, blueNoiseUBO.Size);

            finalProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/fragment.glsl")));

            Gui.ImGuiController.WindowResized(Size.X, Size.Y);

            GLSLBasicData.Projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(102.0f), Size.X / (float)Size.Y, NEAR_PLANE, FAR_PLANE);
            GLSLBasicData.InvProjection = GLSLBasicData.Projection.Inverted();
            GLSLBasicData.NearPlane = NEAR_PLANE;
            GLSLBasicData.FarPlane = FAR_PLANE;
        }

        protected override void OnResize()
        {
            Gui.ImGuiController.WindowResized(Size.X, Size.Y);

            GLSLBasicData.Projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(102.0f), Size.X / (float)Size.Y, NEAR_PLANE, FAR_PLANE);
            GLSLBasicData.InvProjection = GLSLBasicData.Projection.Inverted();
            GLSLBasicData.NearPlane = NEAR_PLANE;
            GLSLBasicData.FarPlane = FAR_PLANE;
            ForwardRenderer.SetSize(Size.X, Size.Y);
            VolumetricLight.SetSize(Size.X, Size.Y);
            GaussianBlur.SetSize(Size.X, Size.Y);
            SSR.SetSize(Size.X, Size.Y);
            SSAO.SetSize(Size.X, Size.Y);
            PostCombine.SetSize(Size.X, Size.Y);
            if (IsPathTracing)
            {
                PathTracer.SetSize(Size.X, Size.Y);
                GLSLBasicData.FrameCount = 0;
            }
        }

        protected override void OnFocusChanged()
        {
            
        }
        protected override void OnEnd()
        {

        }
    }
}
