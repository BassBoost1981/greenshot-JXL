/*
 * Greenshot - a free and open source screenshot tool
 * Copyright (C) 2007-2026 Thomas Braun, Jens Klingen, Robin Krom
 *
 * For more information see: https://getgreenshot.org/
 * The Greenshot project is hosted on GitHub https://github.com/greenshot/greenshot
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Greenshot.Base.Core.FileFormat;
using Greenshot.Base.Core.FileFormatHandlers;
using Greenshot.Base.Interfaces;
using Greenshot.Base.Interfaces.Plugin;
using Greenshot.Plugin.Jxl.FileFormatHandlers;
using Greenshot.Plugin.Jxl.Native;
using Xunit;

namespace Greenshot.Tests.Plugins;

public class JxlFileFormatHandlerTests
{
    [Fact]
    public void RegisterFileFormats_RegistersSaveableJxl()
    {
        Assert.True(LibJxl.IsAvailable);
        var registry = new FileFormatRegistry();
        var handler = new JxlFileFormatHandler();

        handler.RegisterFileFormats(registry);

        var format = registry.GetByExtension(".jxl");
        Assert.NotNull(format);
        Assert.Equal(JxlFileFormatHandler.FormatId, format.Id);
        Assert.Equal("image/jxl", format.MimeType);
        Assert.True(format.CanSave);
        Assert.False(format.CanOpen);
        Assert.True(handler.Supports(FileFormatHandlerActions.SaveToFile, ".jxl"));
        Assert.False(handler.Supports(FileFormatHandlerActions.LoadFromFile, ".jxl"));
    }

    [Fact]
    public void TrySaveToStream_Quality100_IsLossless()
    {
        TestEnvironment.EnsureInitialized();
        using var bitmap = CreateNoiseBitmap(PixelFormat.Format24bppRgb, 64, 40);

        byte[] jxl = Save(bitmap, 100);

        AssertJxlCodestream(jxl);
        Assert.Equal(ReadRgba(bitmap, false), Decode(jxl, 3, out int width, out int height));
        Assert.Equal(64, width);
        Assert.Equal(40, height);
    }

    [Fact]
    public void TrySaveToStream_Quality100_KeepsAlpha()
    {
        TestEnvironment.EnsureInitialized();
        using var bitmap = CreateNoiseBitmap(PixelFormat.Format32bppArgb, 33, 17);

        byte[] jxl = Save(bitmap, 100);

        AssertJxlCodestream(jxl);
        Assert.Equal(ReadRgba(bitmap, true), Decode(jxl, 4, out _, out _));
    }

    [Fact]
    public void TrySaveToStream_LowerQuality_IsLossyAndSmaller()
    {
        TestEnvironment.EnsureInitialized();
        using var bitmap = CreateNoiseBitmap(PixelFormat.Format24bppRgb, 128, 128);

        byte[] lossless = Save(bitmap, 100);
        byte[] lossy = Save(bitmap, 50);

        AssertJxlCodestream(lossy);
        Assert.True(lossy.Length < lossless.Length, $"lossy {lossy.Length} bytes, lossless {lossless.Length} bytes");
        Assert.NotEqual(ReadRgba(bitmap, false), Decode(lossy, 3, out _, out _));
    }

    [Fact]
    public void TrySaveToStream_OtherExtension_ReturnsFalse()
    {
        TestEnvironment.EnsureInitialized();
        using var bitmap = new Bitmap(4, 4);
        using var stream = new MemoryStream();

        Assert.False(new JxlFileFormatHandler().TrySaveToStream(bitmap, stream, ".png", null, new SurfaceOutputSettings(JxlFileFormatHandler.FormatId, 100)));
        Assert.Equal(0, stream.Length);
    }

    private static byte[] Save(Bitmap bitmap, int quality)
    {
        using var stream = new MemoryStream();
        Assert.True(new JxlFileFormatHandler().TrySaveToStream(bitmap, stream, ".jxl", null, new SurfaceOutputSettings(JxlFileFormatHandler.FormatId, quality)));
        return stream.ToArray();
    }

    /// <summary>
    /// A bare JPEG XL codestream starts with FF 0A
    /// </summary>
    private static void AssertJxlCodestream(byte[] jxl)
    {
        Assert.True(jxl.Length > 2);
        Assert.Equal(0xFF, jxl[0]);
        Assert.Equal(0x0A, jxl[1]);
    }

    private static Bitmap CreateNoiseBitmap(PixelFormat pixelFormat, int width, int height)
    {
        var random = new Random(42);
        var bitmap = new Bitmap(width, height, pixelFormat);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int alpha = pixelFormat == PixelFormat.Format32bppArgb ? random.Next(256) : 255;
                bitmap.SetPixel(x, y, Color.FromArgb(alpha, random.Next(256), random.Next(256), random.Next(256)));
            }
        }

        return bitmap;
    }

    private static byte[] ReadRgba(Bitmap bitmap, bool withAlpha)
    {
        return Enumerable.Range(0, bitmap.Height)
            .SelectMany(y => Enumerable.Range(0, bitmap.Width).Select(x => bitmap.GetPixel(x, y)))
            .SelectMany(c => withAlpha ? new[] { c.R, c.G, c.B, c.A } : new[] { c.R, c.G, c.B })
            .ToArray();
    }

    /// <summary>
    /// Decode with libjxl, Greenshot itself leaves decoding to WIC
    /// </summary>
    private static byte[] Decode(byte[] jxl, int channels, out int width, out int height)
    {
        Assert.True(LibJxl.IsAvailable);
        IntPtr decoder = JxlDecoderCreate(IntPtr.Zero);
        var input = GCHandle.Alloc(jxl, GCHandleType.Pinned);
        GCHandle output = default;
        byte[] pixels = null;
        width = height = 0;
        try
        {
            Assert.Equal(0, JxlDecoderSubscribeEvents(decoder, BasicInfoEvent | FullImageEvent));
            Assert.Equal(0, JxlDecoderSetInput(decoder, input.AddrOfPinnedObject(), (UIntPtr)jxl.Length));
            JxlDecoderCloseInput(decoder);
            var pixelFormat = new JxlPixelFormat { NumChannels = (uint)channels, DataType = JxlDataType.UInt8 };
            while (true)
            {
                int status = JxlDecoderProcessInput(decoder);
                if (status == BasicInfoEvent)
                {
                    Assert.Equal(0, JxlDecoderGetBasicInfo(decoder, out var info));
                    width = (int)info.XSize;
                    height = (int)info.YSize;
                }
                else if (status == NeedImageOutBuffer)
                {
                    pixels = new byte[width * height * channels];
                    output = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                    Assert.Equal(0, JxlDecoderSetImageOutBuffer(decoder, ref pixelFormat, output.AddrOfPinnedObject(), (UIntPtr)pixels.Length));
                }
                else if (status == FullImageEvent)
                {
                    return pixels;
                }
                else
                {
                    throw new InvalidOperationException($"JxlDecoderProcessInput returned {status}");
                }
            }
        }
        finally
        {
            if (output.IsAllocated)
            {
                output.Free();
            }
            input.Free();
            JxlDecoderDestroy(decoder);
        }
    }

    private const int NeedImageOutBuffer = 5;
    private const int BasicInfoEvent = 0x40;
    private const int FullImageEvent = 0x1000;

    [DllImport("jxl.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr JxlDecoderCreate(IntPtr memoryManager);

    [DllImport("jxl.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void JxlDecoderDestroy(IntPtr decoder);

    [DllImport("jxl.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int JxlDecoderSubscribeEvents(IntPtr decoder, int eventsWanted);

    [DllImport("jxl.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int JxlDecoderSetInput(IntPtr decoder, IntPtr data, UIntPtr size);

    [DllImport("jxl.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void JxlDecoderCloseInput(IntPtr decoder);

    [DllImport("jxl.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int JxlDecoderProcessInput(IntPtr decoder);

    [DllImport("jxl.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int JxlDecoderGetBasicInfo(IntPtr decoder, out JxlBasicInfo info);

    [DllImport("jxl.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int JxlDecoderSetImageOutBuffer(IntPtr decoder, ref JxlPixelFormat format, IntPtr buffer, UIntPtr size);
}
