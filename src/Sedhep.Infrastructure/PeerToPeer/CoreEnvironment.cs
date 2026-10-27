// Licensed under the MIT license. See LICENSE in the project root.

using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Sedhep.Infrastructure;

public interface ICoreEnvironment
{
    string ThisDeviceId { get; }
    int? PreferredListenPort { get; }
    IEnumerable<string> LocalIpv4Addresses();
    bool IsDesktop { get; }
    string? FindTool(string name);
    ProcessStartInfo CreateProcessStartInfo(string fileName, string arguments = "");
    Process StartProcess(ProcessStartInfo startInfo);
}

public sealed class DesktopCoreEnvironment : ICoreEnvironment
{
    private readonly string _deviceId;

    public DesktopCoreEnvironment()
    {
        _deviceId = Environment.GetEnvironmentVariable("SEDHEP_DEVICE_ID")
                    ?? Guid.NewGuid().ToString("N");
    }

    public string ThisDeviceId => _deviceId;
    public int? PreferredListenPort => null;
    public bool IsDesktop => true;

    public IEnumerable<string> LocalIpv4Addresses()
    {
        try
        {
            var hostName = Environment.MachineName;
            var addrs = Dns.GetHostEntry(hostName).AddressList
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                .Select(a => a.ToString());
            return addrs.DefaultIfEmpty("127.0.0.1");
        }
        catch
        {
            return new[] { "127.0.0.1" };
        }
    }

    public string? FindTool(string name)
    {
        var envPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(envPath)) return null;
        foreach (var dir in envPath.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
            var exe = candidate + (Environment.OSVersion.Platform == PlatformID.Win32NT ? ".exe" : "");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    public ProcessStartInfo CreateProcessStartInfo(string fileName, string arguments = "")
    {
        return new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
    }

    public Process StartProcess(ProcessStartInfo startInfo)
    {
        return Process.Start(startInfo)!;
    }
}
