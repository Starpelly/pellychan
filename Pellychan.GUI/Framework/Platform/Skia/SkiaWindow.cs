﻿using Pellychan.GUI.Framework.Platform.SDL3;
using Pellychan.GUI.Framework.Platform.Windows;
using Pellychan.GUI.Framework.Utils;
using Pellychan.GUI.Widgets;
using SDL;
using SkiaSharp;
using System.Diagnostics;
using static SDL.SDL3;

namespace Pellychan.GUI.Framework.Platform.Skia;

internal unsafe class SkiaWindow
{
    internal Widget ParentWidget { get; private set; }
    internal readonly SkiaWindow? ParentWindow;

    internal IWindow Window { get; private set; }

    internal SDL_Window* SDLWindowHandle => ((SDL3Window)Window).SDLWindowHandle;
    internal SDL_WindowID SDLWindowID => ((SDL3Window)Window).SDLWindowID;

    #region Hardware Acceleration

    internal SDL_GLContextState* SDLGLContext { get; private set; }
    internal GRGlInterface? InterfaceGL { get; private set; }
    internal GRContext? GRContext { get; private set; }
    internal GRBackendRenderTarget? RenderTarget { get; private set; }

    #endregion

    #region Software Rendering

    internal SDL_Renderer* SDLRenderer { get; private set; }
    internal SDL_Texture* SDLTexture { get; private set; }
    internal SDL_Surface* SDLSurface { get; private set; }

    internal SKImageInfo ImageInfo { get; private set; }

    #endregion

    private MouseCursor.CursorType? m_currentCursor = null;
    private MouseCursor.CursorType? m_lastCursorShape = null;

    static GRGlInterface m_createdInterface;
    static bool m_createdBaseGLContext;

    public SkiaWindow(Widget parent, string title, WindowFlags flags, SkiaWindow? parentWindow)
    {
        switch (RuntimeInfo.OS)
        {
            case RuntimeInfo.Platform.Windows:
                Debug.Assert(OperatingSystem.IsWindows());
                Window = new SDL3WindowsWindow();
                break;
            default:
                throw new InvalidOperationException($"Could not find a suitable window for the selected operating system ({RuntimeInfo.OS})");
        }

        ParentWindow = parentWindow;

        Window.Create(parentWindow?.Window ?? null, flags);

        Center();

        Window.ExitRequested += delegate ()
        {
            ParentWidget?.RequestWindowClose();
        };

        ParentWidget = parent;

        Window.Title = title;

        if (Config.HardwareAccel)
        {
            if (Config.SHARE_GL_CONTEXTS)
            {
                SDLGLContext = SDL_GL_CreateContext(SDLWindowHandle);

                if (!m_createdBaseGLContext)
                {
                    m_createdBaseGLContext = true;
                    m_createdInterface = GRGlInterface.Create();
                    SDL.SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_SHARE_WITH_CURRENT_CONTEXT, 1);
                }

                InterfaceGL = m_createdInterface;
                GRContext = GRContext.CreateGl(m_createdInterface);
            }
            else
            {
                SDLGLContext = SDL_GL_CreateContext(SDLWindowHandle);
                // SDL_GL_MakeCurrent(SDLWindowHandle, SDLGLContext);

                InterfaceGL = GRGlInterface.Create();
                GRContext = GRContext.CreateGl(InterfaceGL);
            }
        }
        else
        {
            SDLRenderer = SDL_CreateRenderer(SDLWindowHandle, (byte*)null);
        }

        WindowRegistry.Register(this);
    }

    public void CreateFrameBuffer(int w, int h)
    {
        ImageInfo = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());

        if (Config.HardwareAccel)
        {
            RenderTarget?.Dispose();

            // SDL_GetWindowSizeInPixels(SDLWindowHandle, &w, &h);
            // SDL_GL_GetIntegerv(SDL.SDL.GLAttribute.FramebufferBinding, out int framebuffer);

            var glInfo = new GRGlFramebufferInfo(0, SKColorType.Rgba8888.ToGlSizedFormat());
            RenderTarget = new GRBackendRenderTarget(w, h, 0, 0, glInfo);
        }
        else
        {
            if (SDLTexture != null)
            {
                SDL_DestroyTexture(SDLTexture);
            }

            // Create SDL texture as the drawing target
            SDLTexture = SDL_CreateTexture(SDLRenderer,
                SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888,
                SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING,
                w, h);

            if (SDLSurface != null)
            {
                SDL_DestroySurface(SDLSurface);
            }
            // SDLSurface = SDL_CreateSurface(w, h, SDL_GetPixelFormatForMasks(32, 0, 0, 0, 0));
            SDLSurface = SDL_CreateSurface(w, h, SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888);
        }
    }

    public void Dispose()
    {
        WindowRegistry.Remove(this);

        if (!Config.SHARE_GL_CONTEXTS)
        {
            GRContext?.Dispose();
            InterfaceGL?.Dispose();
        }

        if (SDLSurface != null)
        {
            SDL_DestroySurface(SDLSurface);
        }
        if (SDLTexture != null)
        {
            SDL_DestroyTexture(SDLTexture);
        }
        if (SDLGLContext != null)
        {
            SDL_GL_DestroyContext(SDLGLContext);
        }
        if (SDLRenderer != null)
        {
            SDL_DestroyRenderer(SDLRenderer);
        }

        Window.Dispose();
    }

    public void BeginPresent()
    {
        if (Config.HardwareAccel)
        {
            SDL_GL_MakeCurrent(SDLWindowHandle, SDLGLContext);
        }
    }

    public void EndPresent()
    {
        if (Config.HardwareAccel)
        {
            SDL_GL_SwapWindow(SDLWindowHandle);
        }
        else
        {
            SDL_RenderTexture(SDLRenderer, SDLTexture, null, null);
            SDL_RenderPresent(SDLRenderer);
            // SDL_RenderPresent(popupRenderer);
        }
    }

    internal static void PollEvents()
    {
        SDL3Window.pollSDLEvents();
    }

    internal void RunCommands()
    {
        (Window as SDL3Window)!.RunCommands();
    }

    /// <summary>
    /// Centers the window.
    /// </summary>
    public void Center()
    {
        // Get the window's current display index
        var displayIndex = SDL_GetDisplayForWindow(SDLWindowHandle);
        if (displayIndex < 0)
        {
            throw new InvalidOperationException($"Failed to get window display index: {SDL_GetError()}");
        }

        // Get the bounds of the display
        SDL_Rect displayBounds;
        if (SDL_GetDisplayBounds(displayIndex, &displayBounds) != true)
        {
            throw new InvalidOperationException($"Failed to get display bounds: {SDL_GetError()}");
        }

        // Get the window size
        int windowWidth, windowHeight;
        SDL_GetWindowSize(SDLWindowHandle, &windowWidth, &windowHeight);

        // Calculate the centered position
        int centeredX = displayBounds.x + (displayBounds.w - windowWidth) / 2;
        int centeredY = displayBounds.y + (displayBounds.h - windowHeight) / 2;

        // Set the window position
        // SDL_SetWindowPosition(SDLWindowHandle, centeredX, centeredY);
        (Window as SDL3Window)!.Position = new(centeredX, centeredY);
    }
}