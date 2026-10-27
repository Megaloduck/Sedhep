// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Sedhep.Infrastructure;

namespace Sedhep.Infrastructure;

public static class PortInspector
{
    private static readonly ICoreEnvironment DefaultEnv = new DesktopCoreEnvironment();

    public static IReadOnlySet<int> GetListeningTcpPorts()
    {
        var ports = new HashSet<int>();

        var exe = DefaultEnv.FindTool("ss") ?? DefaultEnv.FindTool("netstat");
        if (exe == null) return ports;

        try
        {
            var startInfo = DefaultEnv.CreateProcessStartInfo(exe, BuildPortInspectArgs(exe));
            using var process = DefaultEnv.StartProcess(startInfo);
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1500);

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var port = ParseListeningPort(line, exe);
                if (port != null) ports.Add(port.Value);
            }
        }
        catch
        {
            return ports;
        }

        return ports;
    }

    private static int? ParseListeningPort(string line, string tool)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        if (tool.EndsWith("ss", StringComparison.Ordinal))
        {
            var idx = line.LastIndexOf(':');
            if (idx < 0) return null;
            var after = line.Substring(idx + 1).Trim();
            var spaceIdx = after.IndexOf(' ');
            if (spaceIdx >= 0) after = after.Substring(0, spaceIdx);
            if (int.TryParse(after, out var p)) return p;
            return null;
        }

        return null;
    }

    private static string BuildPortInspectArgs(string exe)
    {
        return exe.EndsWith("ss", StringComparison.Ordinal)
            ? "-tlnp 2>/dev/null"
            : "-an -p tcp 2>nul";
    }
}
