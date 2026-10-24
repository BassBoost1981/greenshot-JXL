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
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Greenshot.Base.Core.FileFormat;
using Greenshot.Base.Interfaces;
using Greenshot.Base.Interfaces.Plugin;
using Greenshot.Editor.FileFormatHandlers;
using Greenshot.Plugin.Jxl.Native;
using log4net;

namespace Greenshot.Plugin.Jxl.FileFormatHandlers;

/// <summary>
/// Saves JPEG XL, loading is left to the WpfFileFormatHandler (WIC), which finds the decoder of the JPEG XL extension of Windows
/// </summary>
public sealed class JxlFileFormatHandler : AbstractFileFormatHandler
{
    public const string FormatId = WellKnownFileFormats.Jxl;
    private static readonly ILog Log = LogManager.GetLogger(typeof(JxlFileFormatHandler));
    private static readonly IReadOnlyCollection<string> JxlExtensions = [".jxl"];

    public JxlFileFormatHandler()
    {
        SupportedExtensions[FileFormatHandlerActions.SaveToStream] = JxlExtensions;
        SupportedExtensions[FileFormatHandlerActions.SaveToFile] = JxlExtensions;
    }

    public override void RegisterFileFormats(IFileFormatRegistry registry)
    {
        // Without libjxl (e.g. no Visual C++ runtime) .jxl isn't offered at all
        if (!LibJxl.IsAvailable)
        {
            return;
        }
        RegisterFileFormat(registry, FormatId, [], JxlExtensions, "jxl", "image/jxl", null, "JPEG XL");
    }

    public override bool TrySaveToStream(Bitmap bitmap, Stream destination, string extension, ISurface surface = null, SurfaceOutputSettings surfaceOutputSettings = null)
    {
        if (bitmap == null || destination == null || !string.Equals(extension, ".jxl", StringComparison.OrdinalIgnoreCase) || !LibJxl.IsAvailable)
        {
            return false;
        }

        surfaceOutputSettings ??= new SurfaceOutputSettings();
        try
        {
            JxlImageEncoder.Encode(bitmap, destination, surfaceOutputSettings.JPGQuality);
            return true;
        }
        catch (Exception exception)
        {
            Log.Error("Couldn't save image as JPEG XL: ", exception);
            return false;
        }
    }

    public override bool TryLoadFromStream(Stream stream, string extension, out Bitmap bitmap)
    {
        bitmap = null;
        return false;
    }
}
