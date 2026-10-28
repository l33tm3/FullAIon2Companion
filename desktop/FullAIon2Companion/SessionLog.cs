using System.Diagnostics;
using System.IO;

namespace FullAIon2Companion;

/// <summary>
/// When and why the app starts and ends (session-log.txt in the data folder). 2026-10-24, the user: «agrega un registro que
/// anote cuándo y por qué se cierra la app». The dev app had closed by itself three times (23 oct 18:13, 24 oct 14:18 and
/// 16:15) without a line in errors.log, startup-error.log or Windows' event log. Each run writes «start», then the close
/// request and its source (Salir, an update, the taskbar or Alt+F4, a WM_CLOSE from another program, Windows signing out),
/// then «end». A run with no «end» died without closing (killed, or a crash in native code): the next start says so, with
/// the last time live.json was written, which the app does every few seconds.
/// </summary>
public static class SessionLog
{
    static string FilePath => Path.Combine(Program.DataDirectory, "session-log.txt");
    static readonly object gate = new();
    static bool ended;

    public static void Write(string what)
    {
        lock (gate)
            try { File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {what}{Environment.NewLine}"); } catch { }
    }

    /// <summary>The last line of this run: once, from the first path that sees the end.</summary>
    public static void End(string how) { lock (gate) { if (ended) return; ended = true; } Write("end: " + how); }

    public static void Start(string version)
    {
        try
        {
            if (File.Exists(FilePath))
            {
                // The run before is judged on the whole file, then the file is cut (long enough for months of runs; the newest
                // half stays): one run that wrote a lot must not lose its own «start» before it is looked at.
                var lines = File.ReadAllLines(FilePath);
                int start = Array.FindLastIndex(lines, l => l.Contains("] start "));
                if (start >= 0 && lines[start].Split('[', ']') is { Length: > 1 } parts && int.TryParse(parts[1], out int pid))
                {
                    bool closed = lines.Skip(start + 1).Any(l => l.Contains($"[{pid}] end: "));
                    // Its own last line, not the file's: another instance may have written after it.
                    string last = lines.Skip(start).LastOrDefault(l => l.Contains($"[{pid}] ")) ?? lines[^1];
                    if (!closed && Alive(pid, lines[start]))
                        Write($"another instance [{pid}] is still running (the installed app and the dev build share this folder)");
                    // Windows ending the session anywhere in that run (the panel's close lines come after it, then Windows ends it).
                    else if (!closed && lines.Skip(start).Any(l => l.Contains($"[{pid}] Windows is ending the session") || l.Contains($"[{pid}] Windows session ending:")
                        || l.Contains($"[{pid}] Windows asks to end the session") && !l.Contains("installer")))
                        Write($"previous run [{pid}] ended with Windows (shutdown, restart or sign-out); its last line: «{last}»");
                    else if (!closed)
                    {
                        string live = Path.Combine(Program.DataDirectory, "live.json");
                        string beat = File.Exists(live) ? File.GetLastWriteTime(live).ToString("yyyy-MM-dd HH:mm:ss") : "—";
                        Write($"previous run [{pid}] ended without closing (killed, or a crash in native code); its last line: «{last}»; live.json last written {beat}");
                    }
                }
                if (new FileInfo(FilePath).Length > 512 * 1024) { var keep = File.ReadAllLines(FilePath); File.WriteAllLines(FilePath, keep[(keep.Length / 2)..]); }
            }
        }
        catch { }
        Write($"start {version} · {Environment.ProcessPath}");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => End($"process exit (code {Environment.ExitCode})");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => End("unhandled exception: " + (e.ExceptionObject as Exception)?.GetType().Name + " " + (e.ExceptionObject as Exception)?.Message);
    }
    // The run of that start line is still alive: a FullAIon2Companion process with that id, started before the line was written.
    static bool Alive(int pid, string startLine)
    {
        try
        {
            if (pid == Environment.ProcessId) return false;
            using var p = Process.GetProcessById(pid);
            if (!p.ProcessName.StartsWith("FullAIon2Companion", StringComparison.OrdinalIgnoreCase)) return false;
            return !DateTime.TryParse(startLine.Length >= 23 ? startLine[..23] : "", out var written) || p.StartTime <= written.AddSeconds(5);
        }
        catch { return false; }
    }

    /// <summary>The window messages that close the panel, logged with where they come from.</summary>
    public static void Message(int msg, IntPtr wp, IntPtr lp, string state)
    {
        long flags = lp.ToInt64();
        switch (msg)
        {
            case 0x0010: Write($"WM_CLOSE received ({state})"); break;                                   // any close: ours, the taskbar's or another program's
            case 0x0112 when (wp.ToInt64() & 0xFFF0) == 0xF060: Write($"close from the window menu, the taskbar or Alt+F4 ({state})"); break;
            // ENDSESSION_CLOSEAPP (an installer or Restart Manager closing this app, as ours does when it updates), CRITICAL
            // (a forced shutdown), LOGOFF (signing out); none of them: a shutdown or restart.
            case 0x0011: Write($"Windows asks to end the session ({((flags & 0x1) != 0 ? "an installer or Restart Manager closes the app" : (flags & 0x40000000) != 0 ? "forced shutdown" : (flags & 0x80000000) != 0 ? "sign-out" : "shutdown or restart")})"); break;
            // Windows may end the process right after this: the run ends here (End writes once). An installer or Restart Manager
            // closing only this app sends it too (ENDSESSION_CLOSEAPP): said as such.
            case 0x0016 when wp != IntPtr.Zero && (flags & 0x1) != 0: Write("an installer or Restart Manager is closing the app"); End("closed by an installer or Restart Manager"); break;
            case 0x0016 when wp != IntPtr.Zero: Write("Windows is ending the session"); End("Windows ended the session"); break;
        }
    }
}
