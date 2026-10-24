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
using System.IO;
using System.Runtime.InteropServices;
using log4net;

namespace Greenshot.Plugin.Jxl.Native;

/// <summary>
/// The part of the libjxl encoder API (jxl/encode.h, jxl/thread_parallel_runner.h of libjxl 0.12) Greenshot uses
/// </summary>
internal static class LibJxl
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(LibJxl));
    private const string JxlDll = "jxl.dll";
    private const string JxlThreadsDll = "jxl_threads.dll";
    private const uint LoadWithAlteredSearchPath = 0x00000008;

    private static readonly Lazy<IntPtr> ThreadParallelRunner = new(LoadNativeLibraries);

    /// <summary>
    /// True when the libjxl DLLs next to the plugin could be loaded: they are x64 only and need the Visual C++ runtime
    /// </summary>
    public static bool IsAvailable => ThreadParallelRunner.Value != IntPtr.Zero;

    /// <summary>
    /// The JxlThreadParallelRunner function, for JxlEncoderSetParallelRunner
    /// </summary>
    public static IntPtr ThreadParallelRunnerFunction => ThreadParallelRunner.Value;

    /// <summary>
    /// Load the DLLs with their full path (their dependencies come from the same directory), the DllImports below then use the loaded modules
    /// </summary>
    /// <returns>the address of JxlThreadParallelRunner, IntPtr.Zero if libjxl can't be used</returns>
    private static IntPtr LoadNativeLibraries()
    {
        if (!Environment.Is64BitProcess)
        {
            Log.Warn("libjxl is only available for 64-bit processes, JPEG XL can't be saved.");
            return IntPtr.Zero;
        }

        string directory = Path.GetDirectoryName(typeof(LibJxl).Assembly.Location) ?? string.Empty;
        IntPtr threadsModule = IntPtr.Zero;
        foreach (var dll in new[] { JxlThreadsDll, JxlDll })
        {
            var dllPath = Path.Combine(directory, dll);
            var module = LoadLibraryEx(dllPath, IntPtr.Zero, LoadWithAlteredSearchPath);
            if (module == IntPtr.Zero)
            {
                Log.WarnFormat("Couldn't load {0} (error {1}), JPEG XL can't be saved.", dllPath, Marshal.GetLastWin32Error());
                return IntPtr.Zero;
            }

            if (dll == JxlThreadsDll)
            {
                threadsModule = module;
            }
        }

        var runner = GetProcAddress(threadsModule, "JxlThreadParallelRunner");
        if (runner == IntPtr.Zero)
        {
            Log.Warn("jxl_threads.dll has no JxlThreadParallelRunner, JPEG XL can't be saved.");
            return IntPtr.Zero;
        }

        uint version = JxlEncoderVersion();
        Log.InfoFormat("Using libjxl {0}.{1}.{2}", version / 1000000, version / 1000 % 1000, version % 1000);
        return runner;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, BestFitMapping = false)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint JxlEncoderVersion();

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr JxlEncoderCreate(IntPtr memoryManager);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void JxlEncoderDestroy(IntPtr encoder);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlEncoderStatus JxlEncoderSetParallelRunner(IntPtr encoder, IntPtr parallelRunner, IntPtr parallelRunnerOpaque);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void JxlEncoderInitBasicInfo(out JxlBasicInfo info);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlEncoderStatus JxlEncoderSetBasicInfo(IntPtr encoder, ref JxlBasicInfo info);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void JxlColorEncodingSetToSRGB(out JxlColorEncoding colorEncoding, int isGray);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlEncoderStatus JxlEncoderSetColorEncoding(IntPtr encoder, ref JxlColorEncoding colorEncoding);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr JxlEncoderFrameSettingsCreate(IntPtr encoder, IntPtr source);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlEncoderStatus JxlEncoderSetFrameLossless(IntPtr frameSettings, int lossless);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlEncoderStatus JxlEncoderSetFrameDistance(IntPtr frameSettings, float distance);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern float JxlEncoderDistanceFromQuality(float quality);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlEncoderStatus JxlEncoderAddImageFrame(IntPtr frameSettings, ref JxlPixelFormat pixelFormat, IntPtr buffer, UIntPtr size);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void JxlEncoderCloseInput(IntPtr encoder);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern JxlEncoderStatus JxlEncoderProcessOutput(IntPtr encoder, ref IntPtr nextOut, ref UIntPtr availableOut);

    [DllImport(JxlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int JxlEncoderGetError(IntPtr encoder);

    [DllImport(JxlThreadsDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr JxlThreadParallelRunnerCreate(IntPtr memoryManager, UIntPtr numWorkerThreads);

    [DllImport(JxlThreadsDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void JxlThreadParallelRunnerDestroy(IntPtr runner);

    [DllImport(JxlThreadsDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr JxlThreadParallelRunnerDefaultNumWorkerThreads();
}

internal enum JxlEncoderStatus
{
    Success = 0,
    Error = 1,
    NeedMoreOutput = 2,
}

/// <summary>
/// JxlBasicInfo, only the fields up to intrinsic_ysize are needed, Size covers the padding behind them
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 204)]
internal struct JxlBasicInfo
{
    public int HaveContainer;
    public uint XSize;
    public uint YSize;
    public uint BitsPerSample;
    public uint ExponentBitsPerSample;
    public float IntensityTarget;
    public float MinNits;
    public int RelativeToMaxDisplay;
    public float LinearBelow;
    public int UsesOriginalProfile;
    public int HavePreview;
    public int HaveAnimation;
    public int Orientation;
    public uint NumColorChannels;
    public uint NumExtraChannels;
    public uint AlphaBits;
    public uint AlphaExponentBits;
    public int AlphaPremultiplied;
    public uint PreviewXSize;
    public uint PreviewYSize;
    public uint AnimationTpsNumerator;
    public uint AnimationTpsDenominator;
    public uint AnimationNumLoops;
    public int AnimationHaveTimecodes;
    public uint IntrinsicXSize;
    public uint IntrinsicYSize;
}

/// <summary>
/// JxlColorEncoding, only filled by JxlColorEncodingSetToSRGB and handed back to libjxl
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 104)]
internal struct JxlColorEncoding
{
    public int ColorSpace;
}

internal enum JxlDataType
{
    UInt8 = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct JxlPixelFormat
{
    public uint NumChannels;
    public JxlDataType DataType;
    public int Endianness;
    public UIntPtr Align;
}
