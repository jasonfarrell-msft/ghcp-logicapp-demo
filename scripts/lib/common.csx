// Shared helpers for the demo scripts. Loaded via `#load "lib/common.csx"`.
// Requires: dotnet-script (https://github.com/dotnet-script/dotnet-script)
//
//   dotnet tool install -g dotnet-script

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

public static class Common
{
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "infra")) &&
                Directory.Exists(Path.Combine(dir.FullName, "scripts")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate repository root. Run from the repo root or any subdirectory.");
    }

    public static Dictionary<string, string> ParseArgs(IList<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) continue;
            var key = a.Substring(2);
            string value;
            if (i + 1 < args.Count && !args[i + 1].StartsWith("--"))
            {
                value = args[i + 1];
                i++;
            }
            else
            {
                value = "true";
            }
            result[key] = value;
        }
        return result;
    }

    public static string Get(Dictionary<string, string> args, string key, string @default)
        => args.TryGetValue(key, out var v) ? v : @default;

    public static bool GetFlag(Dictionary<string, string> args, string key)
        => args.TryGetValue(key, out var v) && v.Equals("true", StringComparison.OrdinalIgnoreCase);

    public static void WriteHeading(string text) => WriteColor(text, ConsoleColor.Cyan);
    public static void WriteWarn(string text) => WriteColor(text, ConsoleColor.Yellow);
    public static void WriteError(string text) => WriteColor(text, ConsoleColor.Red);
    public static void WriteSuccess(string text) => WriteColor(text, ConsoleColor.Green);

    private static void WriteColor(string text, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = prev;
    }

    public sealed class ProcResult
    {
        public int ExitCode { get; set; }
        public string Stdout { get; set; } = "";
        public string Stderr { get; set; } = "";
    }

    public static int Run(string fileName, IEnumerable<string> arguments, string workingDir = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolveExe(fileName),
            WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}");
        p.WaitForExit();
        return p.ExitCode;
    }

    public static ProcResult Capture(string fileName, IEnumerable<string> arguments, string workingDir = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolveExe(fileName),
            WorkingDirectory = workingDir ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return new ProcResult { ExitCode = p.ExitCode, Stdout = stdout, Stderr = stderr };
    }

    // On Windows, tools like `az` and `func` are .cmd shims. ProcessStartInfo without
    // UseShellExecute won't apply PATHEXT, so resolve to the absolute path here.
    public static string ResolveExe(string name)
    {
        if (Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar)) return name;

        var isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        var pathExt = isWindows
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "" };

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in pathExt)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return name;
    }

    public static bool CommandExists(string name)
    {
        var resolved = ResolveExe(name);
        return resolved != name || File.Exists(resolved);
    }
}
