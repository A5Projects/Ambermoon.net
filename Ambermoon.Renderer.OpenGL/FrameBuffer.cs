#if GLES
using Silk.NET.OpenGLES;
#else
using Silk.NET.OpenGL;
#endif
using System;

namespace Ambermoon.Renderer.OpenGL;

public class FrameBuffer(State state, InternalFormat colorFormat = InternalFormat.Rgba8) : IDisposable
{
    uint index = state.Gl.GenFramebuffer();
    uint depthBuffer = state.Gl.GenRenderbuffer();
    uint renderTexture = state.Gl.GenTexture();
    bool disposed = false;
    readonly State state = state;
    readonly Size size = new(0, 0);
    InternalFormat activeColorFormat = colorFormat;

    void EnsureSize(int width, int height)
    {
        if (size.Width != width || size.Height != height)
        {
            size.Width = width;
            size.Height = height;
            var gl = state.Gl;

            gl.BindFramebuffer(GLEnum.Framebuffer, index);

            void AllocateColorTexture(InternalFormat format)
            {
                GLEnum pixelType = format == InternalFormat.Rgba16f ? GLEnum.HalfFloat : GLEnum.UnsignedByte;
                gl.BindTexture(GLEnum.Texture2D, renderTexture);
                unsafe
                {
                    gl.TexImage2D(GLEnum.Texture2D, 0, format, (uint)width, (uint)height, 0, GLEnum.Rgba, pixelType, (void*)0);
                }
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                gl.BindTexture(GLEnum.Texture2D, 0);
                Texture.InvalidateActiveTexture();
                gl.FramebufferTexture2D(GLEnum.Framebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, renderTexture, 0);
            }

            AllocateColorTexture(activeColorFormat);

            gl.BindRenderbuffer(GLEnum.Renderbuffer, depthBuffer);
            gl.RenderbufferStorage(GLEnum.Renderbuffer, GLEnum.Depth24Stencil8, (uint)width, (uint)height);
            gl.BindRenderbuffer(GLEnum.Renderbuffer, 0);

            gl.FramebufferRenderbuffer(GLEnum.Framebuffer, GLEnum.DepthStencilAttachment, GLEnum.Renderbuffer, depthBuffer);

            if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            {
                if (activeColorFormat != InternalFormat.Rgba8)
                {
                    // Fall back cleanly if the active GPU/driver cannot render to RGBA16F.
                    while (gl.GetError() != GLEnum.NoError) { }
                    activeColorFormat = InternalFormat.Rgba8;
                    AllocateColorTexture(activeColorFormat);
                }

                if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
                {
                    gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                    throw new AmbermoonException(ExceptionScope.Render, "Unable to setup framebuffer");
                }
            }

            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }
    }

    public void Bind(int width, int height)
    {
        if (disposed)
            return;

        EnsureSize(width, height);

        state.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, index);
    }

    public void BindAsTexture()
    {
        if (!disposed)
        {
            state.Gl.ActiveTexture(GLEnum.Texture0);
            state.Gl.BindTexture(GLEnum.Texture2D, renderTexture);
        }
    }

    public void Dispose()
    {
        if (!disposed)
        {
            state.Gl.DeleteTexture(renderTexture);
            state.Gl.DeleteRenderbuffer(depthBuffer);
            state.Gl.DeleteFramebuffer(index);

            renderTexture = 0;
            depthBuffer = 0;
            index = 0;

            disposed = true;
        }
    }
}
