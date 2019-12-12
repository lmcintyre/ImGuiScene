﻿using ImGuiNET;
using System;
using System.Collections.Generic;
using static SDL2.SDL;
using static SDL2.SDL_image;

namespace ImGuiScene
{
    /// <summary>
    /// Simple class to wrap everything necessary to use ImGui inside a window.
    /// Currently this always creates a new window rather than take ownership of an existing one.
    /// 
    /// Internally this uses SDL and DirectX 11 or OpenGL 3.2.  Rendering is tied to vsync.
    /// </summary>
    public class SimpleImGuiScene : IDisposable
    {
        /// <summary>
        /// The main application container window where we do all our rendering and input processing.
        /// </summary>
        public SimpleSDLWindow Window { get; private set; }

        /// <summary>
        /// The renderer backend being used to render into this window.
        /// </summary>
        public IRenderer Renderer { get; private set; }

        /// <summary>
        /// Whether the user application has requested the system to terminate.
        /// </summary>
        public bool ShouldQuit { get; set; } = false;

        public delegate void BuildUIDelegate();

        /// <summary>
        /// User methods invoked every ImGui frame to construct custom UIs.
        /// </summary>
        public BuildUIDelegate OnBuildUI;

        private List<IDisposable> _allocatedResources = new List<IDisposable>();

        /// <summary>
        /// Creates a new window and a new renderer of the specified type, and initializes ImGUI.
        /// </summary>
        /// <param name="backend">Which rendering backend to use.</param>
        /// <param name="createInfo">Creation details for the window.</param>
        /// <param name="enableRenderDebugging">Whether to enable debugging of the renderer internals.  This will likely greatly impact performance and is not usually recommended.</param>
        public SimpleImGuiScene(RendererFactory.RendererBackend backend, WindowCreateInfo createInfo, bool enableRenderDebugging = false)
        {
            Renderer = RendererFactory.CreateRenderer(backend, enableRenderDebugging);
            Window = WindowFactory.CreateForRenderer(Renderer, createInfo);

            ImGui.CreateContext();

            ImGui_Impl_SDL.Init(Window.Window);
            Renderer.ImGui_Init();

            Window.OnSDLEvent += ImGui_Impl_SDL.ProcessEvent;
        }

        /// <summary>
        /// Loads an image from a file and creates the corresponding DX texture
        /// </summary>
        /// <param name="path">The filepath to the image</param>
        /// <returns>A pointer associated with the loaded texture resource, suitable for direct use in ImGui, or IntPtr.Zero on failure.</returns>
        /// <remarks>Currently any textures created by this method are managed automatically and exist until this class object is Disposed.</remarks>
        public IntPtr LoadImage(string path)
        {
            var surface = IMG_Load(path);
            if (surface != IntPtr.Zero)
            {
                return LoadImage_Internal(surface);
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Loads an image from a byte array of image data and creates the corresponding texture resource.
        /// </summary>
        /// <param name="imageBytes">The raw image data</param>
        /// <returns>A pointer associated with the loaded texture resource, suitable for direct use in ImGui, or IntPtr.Zero on failure.</returns>
        /// <remarks>Currently any textures created by this method are managed automatically and exist until this class object is Disposed.</remarks>
        public IntPtr LoadImage(byte[] imageBytes)
        {
            unsafe
            {
                fixed (byte* mem = imageBytes)
                {
                    var rw = SDL_RWFromConstMem((IntPtr)mem, imageBytes.Length);
                    var surface = IMG_Load_RW(rw, 1);
                    if (surface != IntPtr.Zero)
                    {
                        return LoadImage_Internal(surface);
                    }
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Internal helper to create a texture resource from an existing SDL_Surface*
        /// </summary>
        /// <param name="surface">The existing SDL_Surface* representing the image</param>
        /// <returns>A pointer associated with the loaded texture resource, suitable for direct use in ImGui, or IntPtr.Zero on failure.</returns>
        private IntPtr LoadImage_Internal(IntPtr surface)
        {
            IntPtr ret = IntPtr.Zero;

            unsafe
            {
                SDL_Surface* surf = (SDL_Surface*)surface;
                var bytesPerPixel = ((SDL_PixelFormat*)surf->format)->BytesPerPixel;

                var texture = Renderer.CreateTexture((void*)surf->pixels, surf->w, surf->h, bytesPerPixel);
                if (texture != null)
                {
                    _allocatedResources.Add(texture);
                    ret = texture.ImGuiHandle();
                }
            }

            return ret;
        }

        /// <summary>
        /// Performs a single-frame update of ImGui and renders it to the window.
        /// This method does not check any quit conditions.
        /// </summary>
        public void Update()
        {
            Window.ProcessEvents();

            Renderer.ImGui_NewFrame();
            ImGui_Impl_SDL.NewFrame();

            ImGui.NewFrame();
                OnBuildUI?.Invoke();
            ImGui.Render();

            Renderer.Clear();

            Renderer.ImGui_RenderDrawData(ImGui.GetDrawData());

            Renderer.Present();
        }

        /// <summary>
        /// Simple method to run the scene in a loop until the window is closed or the application
        /// requests an exit (via <see cref="ShouldQuit"/>)
        /// </summary>
        public void Run()
        {
            // For now we consider the window closing to be a quit request
            // while ShouldQuit is used for external/application close requests
            while (!Window.WantsClose && !ShouldQuit)
            {
                Update();
            }
        }

        #region IDisposable Support
        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                Renderer.ImGui_Shutdown();
                ImGui_Impl_SDL.Shutdown();

                ImGui.DestroyContext();

                _allocatedResources.ForEach(res => res.Dispose());
                _allocatedResources.Clear();

                // Probably not necessary, but may as well be nice and try to clean up in case we used anything
                // This is safe even if nothing from the library was used
                // We also never call IMG_Load() for now since it is done automatically where needed
                IMG_Quit();

                Renderer?.Dispose();
                Renderer = null;

                Window?.Dispose();
                Window = null;

                disposedValue = true;
            }
        }

        ~SimpleImGuiScene()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
