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

using System.Threading;
using System.Threading.Tasks;
using Greenshot.Base.Interfaces;
using Greenshot.Base.Interfaces.Plugin;
using Greenshot.Plugin.Jxl.FileFormatHandlers;

namespace Greenshot.Plugin.Jxl;

/// <summary>
/// Saves screenshots as JPEG XL with libjxl, loading .jxl stays with WIC (the JPEG XL extension of Windows)
/// </summary>
public sealed class JxlPlugin : IGreenshotPlugin
{
    public string Name => "JPEG XL";

    public ValueTask DisposeAsync()
    {
        return default;
    }

    public void ConfigureServices(IPluginServices services)
    {
        services.AddService<IFileFormatHandler>(new JxlFileFormatHandler());
    }

    public Task StartAsync(System.IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
