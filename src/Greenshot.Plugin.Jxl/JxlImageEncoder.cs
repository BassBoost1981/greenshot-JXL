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
using System.Runtime.InteropServices;
using Greenshot.Plugin.Jxl.Native;

namespace Greenshot.Plugin.Jxl;

/// <summary>
/// Encodes a bitmap as JPEG XL with libjxl
/// </summary>
internal static class JxlImageEncoder
{
    /// <summary>
    /// The quality from which on the image is saved lossless
    /// </summary>
    public const int LosslessQuality = 100;

    private const int OutputChunkSize = 64 * 1024;

    /// <summary>
    /// Encode the bitmap into the stream
    /// </summary>
    /// <param name="bitmap">Bitmap</param>
    /// <param name="destination">Stream</param>
    /// <param name="quality">0-100, 100 (LosslessQuality) is lossless</param>
    public static void Encode(Bitmap bitmap, Stream destination, int quality)
    {
        if (!LibJxl.IsAvailable)
        {
            throw new InvalidOperationException("libjxl is not available.");
        }

        bool hasAlpha = bitmap.PixelFormat == PixelFormat.Format32bppArgb || bitmap.PixelFormat == PixelFormat.Format32bppPArgb;
        int channels = hasAlpha ? 4 : 3;
        byte[] pixels = ReadPixels(bitmap, hasAlpha);
        bool lossless = quality >= LosslessQuality;

        IntPtr encoder = LibJxl.JxlEncoderCreate(IntPtr.Zero);
        if (encoder == IntPtr.Zero)
        {
            throw new InvalidOperationException("JxlEncoderCreate failed.");
        }

        IntPtr runner = IntPtr.Zero;
        var pixelsHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            runner = LibJxl.JxlThreadParallelRunnerCreate(IntPtr.Zero, LibJxl.JxlThreadParallelRunnerDefaultNumWorkerThreads());
            Check(encoder, LibJxl.JxlEncoderSetParallelRunner(encoder, LibJxl.ThreadParallelRunnerFunction, runner), "JxlEncoderSetParallelRunner");

            LibJxl.JxlEncoderInitBasicInfo(out var basicInfo);
            basicInfo.XSize = (uint)bitmap.Width;
            basicInfo.YSize = (uint)bitmap.Height;
            basicInfo.BitsPerSample = 8;
            basicInfo.NumColorChannels = 3;
            // Lossless needs the original color space, lossy uses XYB
            basicInfo.UsesOriginalProfile = lossless ? 1 : 0;
            if (hasAlpha)
            {
                basicInfo.NumExtraChannels = 1;
                basicInfo.AlphaBits = 8;
            }
            Check(encoder, LibJxl.JxlEncoderSetBasicInfo(encoder, ref basicInfo), "JxlEncoderSetBasicInfo");

            LibJxl.JxlColorEncodingSetToSRGB(out var colorEncoding, 0);
            Check(encoder, LibJxl.JxlEncoderSetColorEncoding(encoder, ref colorEncoding), "JxlEncoderSetColorEncoding");

            IntPtr frameSettings = LibJxl.JxlEncoderFrameSettingsCreate(encoder, IntPtr.Zero);
            if (lossless)
            {
                Check(encoder, LibJxl.JxlEncoderSetFrameLossless(frameSettings, 1), "JxlEncoderSetFrameLossless");
            }
            else
            {
                Check(encoder, LibJxl.JxlEncoderSetFrameDistance(frameSettings, LibJxl.JxlEncoderDistanceFromQuality(quality)), "JxlEncoderSetFrameDistance");
            }

            var pixelFormat = new JxlPixelFormat
            {
                NumChannels = (uint)channels,
                DataType = JxlDataType.UInt8,
                Endianness = 0,
                Align = UIntPtr.Zero
            };
            Check(encoder, LibJxl.JxlEncoderAddImageFrame(frameSettings, ref pixelFormat, pixelsHandle.AddrOfPinnedObject(), (UIntPtr)pixels.Length), "JxlEncoderAddImageFrame");
            LibJxl.JxlEncoderCloseInput(encoder);

            WriteOutput(encoder, destination);
        }
        finally
        {
            pixelsHandle.Free();
            if (runner != IntPtr.Zero)
            {
                LibJxl.JxlThreadParallelRunnerDestroy(runner);
            }
            LibJxl.JxlEncoderDestroy(encoder);
        }
    }

    /// <summary>
    /// The pixels as RGB or RGBA without row padding, which is what libjxl expects
    /// </summary>
    private static byte[] ReadPixels(Bitmap bitmap, bool hasAlpha)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        int channels = hasAlpha ? 4 : 3;
        var pixels = new byte[width * height * channels];
        var bitmapData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[width * 4];
            int target = 0;
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(bitmapData.Scan0 + y * bitmapData.Stride, row, 0, row.Length);
                // GDI+ stores BGRA
                for (int source = 0; source < row.Length; source += 4)
                {
                    pixels[target++] = row[source + 2];
                    pixels[target++] = row[source + 1];
                    pixels[target++] = row[source];
                    if (hasAlpha)
                    {
                        pixels[target++] = row[source + 3];
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        return pixels;
    }

    private static void WriteOutput(IntPtr encoder, Stream destination)
    {
        var buffer = new byte[OutputChunkSize];
        var bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            JxlEncoderStatus status;
            do
            {
                IntPtr nextOut = bufferHandle.AddrOfPinnedObject();
                var availableOut = (UIntPtr)buffer.Length;
                status = LibJxl.JxlEncoderProcessOutput(encoder, ref nextOut, ref availableOut);
                destination.Write(buffer, 0, buffer.Length - (int)availableOut);
            } while (status == JxlEncoderStatus.NeedMoreOutput);

            Check(encoder, status, "JxlEncoderProcessOutput");
        }
        finally
        {
            bufferHandle.Free();
        }
    }

    private static void Check(IntPtr encoder, JxlEncoderStatus status, string function)
    {
        if (status != JxlEncoderStatus.Success)
        {
            throw new InvalidOperationException($"{function} failed with status {status}, encoder error {LibJxl.JxlEncoderGetError(encoder)}.");
        }
    }
}
