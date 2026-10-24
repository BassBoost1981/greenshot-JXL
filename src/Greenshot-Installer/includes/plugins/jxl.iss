[Components]
Name: "plugins\jxl"; Description: {cm:jxl}; Types: default full custom; Flags: disablenouninstallwarning

[Files]
; The plugin with libjxl (x64 only, like the full version) and the licenses of libjxl and the libraries in it
Source: {#PluginDir}\Greenshot.Plugin.Jxl\*.dll; DestDir: {app}\Plugins\Greenshot.Plugin.Jxl; Components: plugins\jxl; Flags: {#DefaultInstallFlags};
Source: {#SolutionDir}\Greenshot.Plugin.Jxl\Native\licenses\LICENSE.*; DestDir: {app}\Plugins\Greenshot.Plugin.Jxl\licenses; Components: plugins\jxl; Flags: {#DefaultInstallFlags};

[CustomMessages]
jxl=JPEG XL export plug-in
en.jxl=JPEG XL export plug-in
de.jxl=JPEG-XL-Export-Plug-in
