﻿using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Buffers;
using System.Diagnostics;

namespace Pellychan.GUI.Framework.Extensions.ImageExtensions;

public struct ReadOnlyPixelMemory<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
{
    private Image<TPixel>? image;
    private Memory<TPixel>? memory;
    private IMemoryOwner<TPixel>? owner;

    internal ReadOnlyPixelMemory(Image<TPixel> image)
    {
        this.image = image;

        if (image.DangerousTryGetSinglePixelMemory(out _))
        {
            owner = null;
            memory = null;
        }
        else
        {
            owner = image.CreateContiguousMemory();
            memory = owner.Memory;
        }
    }

    /// <summary>
    /// The span of pixels.
    /// </summary>
    public ReadOnlySpan<TPixel> Span
    {
        get
        {
            // Occurs when this struct has been default-initialised (the struct itself doesn't accept a nullable image).
            if (image == null)
                return Span<TPixel>.Empty;

            // If the image can be returned without extra contiguous memory allocation.
            if (image.DangerousTryGetSinglePixelMemory(out var pixelMemory))
                return pixelMemory.Span;

            Debug.Assert(memory != null);
            return memory.Value.Span;
        }
    }

    public void Dispose()
    {
        owner?.Dispose();
        image = null;
        memory = null;
        owner = null;
    }
}