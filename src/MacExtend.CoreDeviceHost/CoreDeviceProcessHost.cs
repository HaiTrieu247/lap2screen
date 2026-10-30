using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;
using Microsoft.Win32.SafeHandles;

namespace MacExtend.CoreDeviceHost
{
    public sealed class CoreDeviceHostException : Exception
    {
        public CoreDeviceHostException(string message) : base(message)
        {
        }
    }

    public enum ReadyFailure
    {
        None,
        ExitedEarly,
        ForeignListener,
        TimedOut,
        Cancelled,
    }

    public enum StopExit
    {
        /// <summary>Every process in the job had already exited when the stop began.</summary>
        AlreadyExited,

        /// <summary>One Ctrl+C was delivered and the job emptied within the grace period.</summary>
        Graceful,

        /// <summary>The job had to be terminated; serve-web's cleanup (and the device's media release) did not finish.</summary>
        Killed,
    }

    /// <summary>
    /// One Python child and everything it starts, held in a kill-on-close job object. The child is created suspended
    /// with a hidden console of its own, put in the job, then resumed, so the venv launcher's interpreter and its
    /// descendants are in the job from their first instruction. A graceful stop sends exactly one CTRL_C_EVENT to
    /// that console through the probe's signal-ctrl-c helper; pymobiledevice3 11.47.0 turns the first Ctrl+C into a
    /// cancellation that runs serve-web's cleanup, and a second one aborts that cleanup.
    /// </summary>
    public sealed class CoreDeviceProcessHost : IDisposable
    {
        private const string ViewerTitle = "<title>pymobiledevice3 screen</title>";

        private readonly SafeKernelHandle _job;
        private readonly object _lock = new object();
        private readonly List<Thread> _readers = new List<Thread>();
        private SafeKernelHandle _root;
        private int _rootPid;
        private int _listeningPort;
        private ServeWebLogKind _lastFatal = ServeWebLogKind.Other;
        private bool _shutdownComplete;
        private string _failedStep;
        private bool _anyStepFailed;
        private Task<StopExit> _stop;

        public CoreDeviceProcessHost()
        {
            _job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
            if (_job.IsInvalid) throw new CoreDeviceHostException("CreateJobObject failed: " + Win32Message());
            var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            if (!NativeMethods.SetInformationJobObject(_job, NativeMethods.JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf(typeof(NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
            {
                var message = Win32Message();
                _job.Dispose();
                throw new CoreDeviceHostException("SetInformationJobObject failed: " + message);
            }
        }

        /// <summary>Classified status lines from the child's stdout/stderr; raw lines are never kept.</summary>
        public event Action<ServeWebLogLine> StatusLine;

        public int RootPid => _rootPid;

        public string CodecString { get; private set; }

        /// <summary>serve-web logged "shutdown complete" and no cleanup step reported a timeout or an error.</summary>
        public bool CleanupReportedComplete
        {
            get { lock (_lock) return _shutdownComplete && !_anyStepFailed; }
        }

        public string FailedCleanupStep
        {
            get { lock (_lock) return _failedStep; }
        }

        public ServeWebLogKind LastFatal
        {
            get { lock (_lock) return _lastFatal; }
        }

        /// <summary>
        /// Starts serve-web through <see cref="LaunchInJob"/>. <paramref name="started"/> sees the PID before the
        /// assignment, so a failure can be traced.
        /// </summary>
        public void Start(string executable, IReadOnlyList<string> args, IDictionary<string, string> environment, bool failAssign, Action<int> started)
        {
            lock (_lock)
            {
                if (_root != null) throw new InvalidOperationException("already started");
            }
            var child = LaunchInJob(executable, args, environment, failAssign, started);
            lock (_lock)
            {
                _root = child.Process;
                _rootPid = child.Pid;
            }
            StartReader(child.Stdout);
            StartReader(child.Stderr);
        }

        /// <summary>
        /// Runs a short command (the version check) in a job of its own, launched like serve-web, and returns its
        /// stdout, or throws. The output is returned to the caller only; it is never printed as is.
        /// </summary>
        public static string RunToCompletion(string executable, IReadOnlyList<string> args, IDictionary<string, string> environment, TimeSpan timeout, bool failAssign, Action<int> started)
        {
            using (var host = new CoreDeviceProcessHost())
            {
                var stdout = new StringBuilder();
                var child = host.LaunchInJob(executable, args, environment, failAssign, started);
                try
                {
                    var outReader = Task.Run(() => ReadAll(child.Stdout, stdout));
                    var errReader = Task.Run(() => ReadAll(child.Stderr, null));
                    if (NativeMethods.WaitForSingleObject(child.Process, (uint)timeout.TotalMilliseconds) != NativeMethods.WAIT_OBJECT_0)
                    {
                        throw new CoreDeviceHostException($"{Path.GetFileName(executable)} did not finish within {timeout.TotalSeconds:0} s");
                    }
                    NativeMethods.GetExitCodeProcess(child.Process, out var exitCode);
                    // a descendant that outlived the command would hold the pipes open
                    NativeMethods.TerminateJobObject(host._job, 1);
                    Task.WaitAll(new Task[] { outReader, errReader }, 5000);
                    if (exitCode != 0) throw new CoreDeviceHostException($"{Path.GetFileName(executable)} exited with code {exitCode}");
                    lock (stdout) return stdout.ToString();
                }
                finally
                {
                    NativeMethods.TerminateJobObject(host._job, 1);
                    child.Dispose();
                }
            }
        }

        /// <summary>
        /// Runs a short command (usbmux list, amfi, mounter, lockdown remotepairing) in a job of its own, launched like
        /// serve-web, and waits up to <paramref name="timeout"/>; past that the job is terminated. stdout comes back
        /// whole (JSON from print_json); stderr lines are only classified with <see cref="CliLog"/>.
        /// </summary>
        public static CommandResult RunCommand(string executable, IReadOnlyList<string> args, IDictionary<string, string> environment, TimeSpan timeout)
        {
            using (var host = new CoreDeviceProcessHost())
            {
                var stdout = new StringBuilder();
                var errors = new List<CliError>();
                var child = host.LaunchInJob(executable, args, environment, false, _ => { });
                try
                {
                    var outReader = Task.Run(() => ReadAll(child.Stdout, stdout));
                    var errReader = Task.Run(() => ReadLines(child.Stderr, line =>
                    {
                        var error = CliLog.Parse(line);
                        if (error != CliError.None) lock (errors) errors.Add(error);
                    }));
                    var finished = NativeMethods.WaitForSingleObject(child.Process, (uint)timeout.TotalMilliseconds) == NativeMethods.WAIT_OBJECT_0;
                    uint exitCode = 1;
                    if (finished) NativeMethods.GetExitCodeProcess(child.Process, out exitCode);
                    // a descendant that outlived the command would hold the pipes open
                    NativeMethods.TerminateJobObject(host._job, 1);
                    Task.WaitAll(new Task[] { outReader, errReader }, 5000);
                    lock (errors)
                    lock (stdout)
                    {
                        return new CommandResult(finished ? (int)exitCode : -1, stdout.ToString(), CliLog.Pick(errors), !finished);
                    }
                }
                finally
                {
                    NativeMethods.TerminateJobObject(host._job, 1);
                    child.Dispose();
                }
            }
        }

        /// <summary>
        /// When set, every stdout/stderr line goes here instead of <see cref="ServeWebLog"/> (tunneld uses it). The handler
        /// must classify the line and drop it: raw lines carry the UDID and addresses.
        /// </summary>
        public Action<string> RawLineHandler { get; set; }

        public int ActiveProcessCount()
        {
            var buffer = Marshal.AllocHGlobal(NativeMethods.AccountingInfoSize);
            try
            {
                if (!NativeMethods.QueryInformationJobObject(_job, NativeMethods.JobObjectBasicAccountingInformation, buffer, NativeMethods.AccountingInfoSize, out _))
                {
                    throw new CoreDeviceHostException("QueryInformationJobObject failed: " + Win32Message());
                }
                return Marshal.ReadInt32(buffer, NativeMethods.AccountingActiveProcessesOffset);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public bool IsEmpty => ActiveProcessCount() == 0;

        public IReadOnlyList<int> JobProcessIds()
        {
            var capacity = 32;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var size = NativeMethods.ProcessIdListHeader + capacity * IntPtr.Size;
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (NativeMethods.QueryInformationJobObject(_job, NativeMethods.JobObjectBasicProcessIdList, buffer, size, out _))
                    {
                        var count = Marshal.ReadInt32(buffer, 4);
                        var pids = new List<int>(count);
                        for (var i = 0; i < count; i++) pids.Add((int)Marshal.ReadIntPtr(buffer, NativeMethods.ProcessIdListHeader + i * IntPtr.Size).ToInt64());
                        return pids;
                    }
                    if (Marshal.GetLastWin32Error() != NativeMethods.ERROR_MORE_DATA) throw new CoreDeviceHostException("QueryInformationJobObject failed: " + Win32Message());
                    capacity = Math.Max(capacity * 2, Marshal.ReadInt32(buffer, 0) + 8);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            throw new CoreDeviceHostException("the job's process list kept growing");
        }

        /// <summary>
        /// Ready means: the job still runs, serve-web logged its "Open http://127.0.0.1:PORT/" line, every listener
        /// on the port belongs to the job (so a server that grabbed the port first is never taken for serve-web),
        /// and GET / returns the viewer page. Nothing here says the video decodes or touches arrive.
        /// </summary>
        public async Task<ReadyFailure> WaitReadyAsync(int port, TimeSpan timeout, CancellationToken token, Action<string> report)
        {
            var watch = Stopwatch.StartNew();
            using (var http = new HttpClient(CoreDeviceClient.CreateHandler()) { Timeout = TimeSpan.FromSeconds(3) })
            {
                var lastReport = TimeSpan.Zero;
                while (watch.Elapsed < timeout)
                {
                    if (token.IsCancellationRequested) return ReadyFailure.Cancelled;
                    if (IsEmpty) return ReadyFailure.ExitedEarly;
                    if (Volatile.Read(ref _listeningPort) == port)
                    {
                        var owners = TcpListeners.Owners(port);
                        if (owners.Count > 0)
                        {
                            var members = new HashSet<int>(JobProcessIds());
                            if (owners.Any(o => !members.Contains(o.Pid)))
                            {
                                report($"a process outside serve-web's job listens on port {port} (pid {string.Join(",", owners.Where(o => !members.Contains(o.Pid)).Select(o => o.Pid))}); refusing it");
                                return ReadyFailure.ForeignListener;
                            }
                            if (owners.Any(o => o.Loopback4) && await ServesViewerAsync(http, port, token).ConfigureAwait(false)) return ReadyFailure.None;
                        }
                    }
                    if (watch.Elapsed - lastReport >= TimeSpan.FromSeconds(10))
                    {
                        lastReport = watch.Elapsed;
                        if (lastReport > TimeSpan.Zero) report($"still starting ({watch.Elapsed.TotalSeconds:0} s of {timeout.TotalSeconds:0} s)");
                    }
                    try
                    {
                        await Task.Delay(250, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return ReadyFailure.Cancelled;
                    }
                }
            }
            return ReadyFailure.TimedOut;
        }

        /// <summary>
        /// The one stop path. The first call sends one Ctrl+C, waits up to <paramref name="grace"/> for the whole job
        /// to exit, then terminates it; every later call returns the same task.
        /// </summary>
        public Task<StopExit> StopAsync(string helperExecutable, TimeSpan grace, Action<string> report)
        {
            lock (_lock)
            {
                if (_stop == null) _stop = Task.Run(() => StopCore(helperExecutable, grace, report));
                return _stop;
            }
        }

        public bool IsStopping
        {
            get { lock (_lock) return _stop != null; }
        }

        public void Kill() => NativeMethods.TerminateJobObject(_job, 1);

        /// <summary>Lets the readers take in the last lines of an exited child before its error is explained.</summary>
        public void WaitForOutput(TimeSpan timeout)
        {
            Thread[] readers;
            lock (_lock) readers = _readers.ToArray();
            var watch = Stopwatch.StartNew();
            foreach (var reader in readers)
            {
                var left = timeout - watch.Elapsed;
                if (left <= TimeSpan.Zero || !reader.Join(left)) return;
            }
        }

        public bool WaitEmpty(TimeSpan timeout)
        {
            var watch = Stopwatch.StartNew();
            while (!IsEmpty)
            {
                if (watch.Elapsed >= timeout) return false;
                Thread.Sleep(100);
            }
            return true;
        }

        public void Dispose()
        {
            // kill-on-close: whatever is still in the job dies with this handle
            _job.Dispose();
            lock (_lock) _root?.Dispose();
        }

        private StopExit StopCore(string helperExecutable, TimeSpan grace, Action<string> report)
        {
            if (IsEmpty) return StopExit.AlreadyExited;

            var signal = SignalOnce(helperExecutable, report);
            if (signal == SignalResult.NotSent)
            {
                report("Ctrl+C could not be delivered to serve-web; terminating it (media cleanup on the device did not run)");
                Kill();
                WaitEmpty(TimeSpan.FromSeconds(10));
                return StopExit.Killed;
            }
            if (signal == SignalResult.Unknown) report("the Ctrl+C helper failed after it may have signalled; not signalling again, waiting for serve-web to exit");

            var watch = Stopwatch.StartNew();
            var nextReport = TimeSpan.FromSeconds(5);
            while (!IsEmpty)
            {
                if (watch.Elapsed >= grace)
                {
                    report($"serve-web did not finish its cleanup within {grace.TotalSeconds:0} s; terminating it (media cleanup on the device is not confirmed)");
                    Kill();
                    WaitEmpty(TimeSpan.FromSeconds(10));
                    return StopExit.Killed;
                }
                if (watch.Elapsed >= nextReport)
                {
                    report($"waiting for serve-web cleanup: {nextReport.TotalSeconds:0} s of {grace.TotalSeconds:0} s");
                    nextReport += TimeSpan.FromSeconds(5);
                }
                Thread.Sleep(100);
            }
            return StopExit.Graceful;
        }

        private enum SignalResult
        {
            Sent,
            NotSent,
            Unknown,
        }

        // helper exit codes (Program.SignalCtrlC)
        public const int HelperSent = 0;
        public const int HelperAttachFailed = 10;
        public const int HelperGenerateFailed = 11;
        public const int HelperBadArguments = 12;

        private SignalResult SignalOnce(string helperExecutable, Action<string> report)
        {
            // the venv launcher first, then any other live member: they all share the child's hidden console
            var pids = JobProcessIds().OrderBy(p => p == _rootPid ? 0 : 1).ThenBy(p => p).ToList();
            foreach (var pid in pids)
            {
                // the open handle keeps the PID from being reused while the helper attaches to it
                using (var process = NativeMethods.OpenProcess(NativeMethods.SYNCHRONIZE | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid))
                {
                    if (process.IsInvalid) continue;
                    if (!NativeMethods.IsProcessInJob(process, _job, out var inJob) || !inJob) continue;
                    if (NativeMethods.WaitForSingleObject(process, 0) != NativeMethods.WAIT_TIMEOUT) continue;

                    var exit = RunHelper(helperExecutable, pid);
                    switch (exit)
                    {
                        case HelperSent:
                            report($"sent one Ctrl+C to serve-web's console (pid {pid})");
                            return SignalResult.Sent;
                        case HelperAttachFailed:
                            // nothing was sent: this process has no console to attach to; try the next one
                            continue;
                        case HelperGenerateFailed:
                            return SignalResult.NotSent;
                        default:
                            return SignalResult.Unknown;
                    }
                }
            }
            return SignalResult.NotSent;
        }

        private static int RunHelper(string helperExecutable, int pid)
        {
            ChildProcess helper;
            try
            {
                helper = Launch(helperExecutable, WindowsCommandLineOf(helperExecutable, CtrlCHelper.Command, pid.ToString()), null, redirect: false, flags: NativeMethods.CREATE_NO_WINDOW);
            }
            catch (CoreDeviceHostException)
            {
                return HelperAttachFailed;
            }
            using (helper)
            {
                if (NativeMethods.ResumeThread(helper.Thread) == uint.MaxValue)
                {
                    NativeMethods.TerminateProcess(helper.Process, 1);
                    // it never ran, so it cannot have signalled
                    return HelperAttachFailed;
                }
                if (NativeMethods.WaitForSingleObject(helper.Process, 10000) != NativeMethods.WAIT_OBJECT_0)
                {
                    NativeMethods.TerminateProcess(helper.Process, 1);
                    return -1;
                }
                return NativeMethods.GetExitCodeProcess(helper.Process, out var code) ? (int)code : -1;
            }
        }

        private static string WindowsCommandLineOf(string executable, params string[] args) => CoreDeviceArguments.CommandLine(executable, args);

        /// <summary>
        /// The one way a Python child starts: created suspended with a hidden console of its own (with none, the venv
        /// launcher's interpreter would get a visible one), put in this job, then resumed, so the launcher and every
        /// process it starts are in the job from their first instruction. If the assignment or the resume fails, the
        /// child is terminated and waited for here: it is never left behind suspended outside the job.
        /// </summary>
        private ChildProcess LaunchInJob(string executable, IReadOnlyList<string> args, IDictionary<string, string> environment, bool failAssign, Action<int> started)
        {
            var child = Launch(executable, CoreDeviceArguments.CommandLine(executable, args), CoreDeviceArguments.EnvironmentBlock(environment), redirect: true, flags: NativeMethods.CREATE_NEW_CONSOLE);
            try
            {
                started?.Invoke(child.Pid);
                if (failAssign) throw new CoreDeviceHostException("job assignment refused (--test-fail-assign)");
                if (!NativeMethods.AssignProcessToJobObject(_job, child.Process)) throw new CoreDeviceHostException("AssignProcessToJobObject failed: " + Win32Message());
                if (NativeMethods.ResumeThread(child.Thread) == uint.MaxValue) throw new CoreDeviceHostException("ResumeThread failed: " + Win32Message());
            }
            catch
            {
                // still suspended: it has run nothing and started nothing, so ending this one process is enough
                NativeMethods.TerminateProcess(child.Process, 1);
                NativeMethods.WaitForSingleObject(child.Process, 5000);
                child.Dispose();
                throw;
            }
            child.Thread.Dispose();
            return child;
        }

        private static async Task<bool> ServesViewerAsync(HttpClient http, int port, CancellationToken token)
        {
            try
            {
                using (var response = await http.GetAsync($"http://{CoreDeviceArguments.BindAddress}:{port}/", token).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode != 200) return false;
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return body.IndexOf(ViewerTitle, StringComparison.Ordinal) >= 0;
                }
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException)
            {
                return false;
            }
        }

        private void StartReader(SafeFileHandle pipe)
        {
            var thread = new Thread(() =>
            {
                using (var stream = new FileStream(pipe, FileAccess.Read, 4096, false))
                using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var raw = RawLineHandler;
                        if (raw != null) raw(line);
                        else OnLine(ServeWebLog.Parse(line));
                    }
                }
            })
            {
                IsBackground = true,
                Name = "serve-web output",
            };
            lock (_lock) _readers.Add(thread);
            thread.Start();
        }

        private void OnLine(ServeWebLogLine line)
        {
            if (line.Kind == ServeWebLogKind.Other) return;
            lock (_lock)
            {
                switch (line.Kind)
                {
                    case ServeWebLogKind.Listening:
                        Volatile.Write(ref _listeningPort, line.Port);
                        break;
                    case ServeWebLogKind.CodecString:
                        CodecString = line.Detail;
                        break;
                    case ServeWebLogKind.ShutdownComplete:
                        _shutdownComplete = true;
                        break;
                    case ServeWebLogKind.ShutdownStepFailed:
                        _anyStepFailed = true;
                        if (_failedStep == null || line.Detail == "stop_all_streams") _failedStep = line.Detail ?? "unknown step";
                        break;
                    case ServeWebLogKind.MediaInUse:
                    case ServeWebLogKind.ServiceUnavailable:
                    case ServeWebLogKind.DeviceNotFound:
                    case ServeWebLogKind.Error:
                        if (_lastFatal == ServeWebLogKind.Other || _lastFatal == ServeWebLogKind.Error) _lastFatal = line.Kind;
                        break;
                }
            }
            StatusLine?.Invoke(line);
        }

        private static void ReadLines(SafeFileHandle pipe, Action<string> onLine)
        {
            using (var stream = new FileStream(pipe, FileAccess.Read, 4096, false))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            {
                string line;
                while ((line = reader.ReadLine()) != null) onLine(line);
            }
        }

        private static void ReadAll(SafeFileHandle pipe, StringBuilder into)
        {
            using (var stream = new FileStream(pipe, FileAccess.Read, 4096, false))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            {
                var text = reader.ReadToEnd();
                if (into != null)
                {
                    lock (into) into.Append(text);
                }
            }
        }

        private sealed class ChildProcess : IDisposable
        {
            public int Pid;
            public SafeKernelHandle Process;
            public SafeKernelHandle Thread;
            public SafeFileHandle Stdout;
            public SafeFileHandle Stderr;

            /// <summary>On the success path the reader threads own the pipes; Dispose is for the failure paths.</summary>
            public void Dispose()
            {
                Thread?.Dispose();
                Process?.Dispose();
                Stdout?.Dispose();
                Stderr?.Dispose();
            }
        }

        /// <summary>
        /// CreateProcessW, always suspended. With <paramref name="redirect"/>, stdin is a pipe whose writer is closed
        /// at once (EOF) and stdout/stderr are pipes; the handle list lets the child inherit exactly those three
        /// and nothing else (in particular no job handle). Without it, nothing is inherited.
        /// </summary>
        private static ChildProcess Launch(string executable, string commandLine, string environmentBlock, bool redirect, uint flags)
        {
            SafeFileHandle stdinRead = null, stdinWrite = null, stdoutRead = null, stdoutWrite = null, stderrRead = null, stderrWrite = null;
            var attributes = IntPtr.Zero;
            var handleList = IntPtr.Zero;
            var environment = IntPtr.Zero;
            try
            {
                var startup = new NativeMethods.STARTUPINFOEX();
                startup.StartupInfo.cb = Marshal.SizeOf(redirect ? typeof(NativeMethods.STARTUPINFOEX) : typeof(NativeMethods.STARTUPINFO));
                var creation = NativeMethods.CREATE_SUSPENDED | flags;
                if (redirect)
                {
                    CreateInheritablePipe(out stdinRead, out stdinWrite, childReads: true);
                    CreateInheritablePipe(out stdoutRead, out stdoutWrite, childReads: false);
                    CreateInheritablePipe(out stderrRead, out stderrWrite, childReads: false);

                    var size = IntPtr.Zero;
                    NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                    attributes = Marshal.AllocHGlobal(size);
                    if (!NativeMethods.InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new CoreDeviceHostException("InitializeProcThreadAttributeList failed: " + Win32Message());
                    handleList = Marshal.AllocHGlobal(3 * IntPtr.Size);
                    Marshal.WriteIntPtr(handleList, 0, stdinRead.DangerousGetHandle());
                    Marshal.WriteIntPtr(handleList, IntPtr.Size, stdoutWrite.DangerousGetHandle());
                    Marshal.WriteIntPtr(handleList, 2 * IntPtr.Size, stderrWrite.DangerousGetHandle());
                    if (!NativeMethods.UpdateProcThreadAttribute(attributes, 0, NativeMethods.PROC_THREAD_ATTRIBUTE_HANDLE_LIST, handleList, (IntPtr)(3 * IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                    {
                        throw new CoreDeviceHostException("UpdateProcThreadAttribute failed: " + Win32Message());
                    }
                    startup.lpAttributeList = attributes;
                    startup.StartupInfo.dwFlags = NativeMethods.STARTF_USESTDHANDLES | NativeMethods.STARTF_USESHOWWINDOW;
                    startup.StartupInfo.wShowWindow = NativeMethods.SW_HIDE;
                    startup.StartupInfo.hStdInput = stdinRead.DangerousGetHandle();
                    startup.StartupInfo.hStdOutput = stdoutWrite.DangerousGetHandle();
                    startup.StartupInfo.hStdError = stderrWrite.DangerousGetHandle();
                    creation |= NativeMethods.EXTENDED_STARTUPINFO_PRESENT;
                }
                if (environmentBlock != null)
                {
                    environment = Marshal.StringToHGlobalUni(environmentBlock);
                    creation |= NativeMethods.CREATE_UNICODE_ENVIRONMENT;
                }

                var commandBuffer = new StringBuilder(commandLine, commandLine.Length + 1);
                if (!NativeMethods.CreateProcess(executable, commandBuffer, IntPtr.Zero, IntPtr.Zero, redirect, creation, environment, null, ref startup, out var info))
                {
                    throw new CoreDeviceHostException($"could not start {Path.GetFileName(executable)}: " + Win32Message());
                }
                return new ChildProcess
                {
                    Pid = info.dwProcessId,
                    Process = new SafeKernelHandle(info.hProcess),
                    Thread = new SafeKernelHandle(info.hThread),
                    Stdout = stdoutRead,
                    Stderr = stderrRead,
                };
            }
            catch
            {
                stdoutRead?.Dispose();
                stderrRead?.Dispose();
                throw;
            }
            finally
            {
                if (attributes != IntPtr.Zero)
                {
                    NativeMethods.DeleteProcThreadAttributeList(attributes);
                    Marshal.FreeHGlobal(attributes);
                }
                if (handleList != IntPtr.Zero) Marshal.FreeHGlobal(handleList);
                if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
                // the child holds its own copies now; ours would keep the pipes from ever reporting EOF
                stdinRead?.Dispose();
                stdinWrite?.Dispose();
                stdoutWrite?.Dispose();
                stderrWrite?.Dispose();
            }
        }

        private static void CreateInheritablePipe(out SafeFileHandle read, out SafeFileHandle write, bool childReads)
        {
            var security = new NativeMethods.SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf(typeof(NativeMethods.SECURITY_ATTRIBUTES)), bInheritHandle = true };
            if (!NativeMethods.CreatePipe(out read, out write, ref security, 0)) throw new CoreDeviceHostException("CreatePipe failed: " + Win32Message());
            // only the child's end stays inheritable
            var parentEnd = childReads ? (SafeHandle)write : read;
            if (!NativeMethods.SetHandleInformation(parentEnd, NativeMethods.HANDLE_FLAG_INHERIT, 0)) throw new CoreDeviceHostException("SetHandleInformation failed: " + Win32Message());
        }

        private static string Win32Message() => new Win32Exception(Marshal.GetLastWin32Error()).Message;
    }
}

namespace MacExtend.CoreDeviceHost
{
    public struct PortListener
    {
        public int Pid;

        /// <summary>Bound to 127.0.0.1, as serve-web's --bind makes it.</summary>
        public bool Loopback4;
    }

    /// <summary>Who listens on a TCP port (IPv4 and IPv6, every local address), from GetExtendedTcpTable.</summary>
    public static class TcpListeners
    {
        // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, owningPid
        private const int Row4Size = 24, Row4Local = 4, Row4Port = 8, Row4Pid = 20;
        // MIB_TCP6ROW_OWNER_PID: localAddr[16], localScope, localPort, remoteAddr[16], remoteScope, remotePort, state, owningPid
        private const int Row6Size = 56, Row6Port = 20, Row6Pid = 52;

        public static IReadOnlyList<PortListener> Owners(int port)
        {
            var owners = new List<PortListener>();
            Read(NativeMethods.AF_INET, port, owners);
            Read(NativeMethods.AF_INET6, port, owners);
            return owners;
        }

        public static bool IsFree(int port) => Owners(port).Count == 0;

        private static void Read(int family, int port, List<PortListener> into)
        {
            var size = 0;
            NativeMethods.GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, NativeMethods.TCP_TABLE_OWNER_PID_LISTENER, 0);
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var buffer = Marshal.AllocHGlobal(Math.Max(size, 4));
                try
                {
                    var rc = NativeMethods.GetExtendedTcpTable(buffer, ref size, false, family, NativeMethods.TCP_TABLE_OWNER_PID_LISTENER, 0);
                    if (rc == NativeMethods.ERROR_INSUFFICIENT_BUFFER) continue; // the table grew between calls
                    if (rc != 0) throw new CoreDeviceHostException($"GetExtendedTcpTable failed ({rc})");
                    var rows = Marshal.ReadInt32(buffer);
                    for (var i = 0; i < rows; i++)
                    {
                        var row = buffer + 4 + i * (family == NativeMethods.AF_INET ? Row4Size : Row6Size);
                        var portOffset = family == NativeMethods.AF_INET ? Row4Port : Row6Port;
                        // dwLocalPort holds the port in network byte order in its first two bytes
                        var rowPort = (Marshal.ReadByte(row, portOffset) << 8) | Marshal.ReadByte(row, portOffset + 1);
                        if (rowPort != port) continue;
                        into.Add(new PortListener
                        {
                            Pid = Marshal.ReadInt32(row, family == NativeMethods.AF_INET ? Row4Pid : Row6Pid),
                            Loopback4 = family == NativeMethods.AF_INET
                                && Marshal.ReadByte(row, Row4Local) == 127 && Marshal.ReadByte(row, Row4Local + 1) == 0
                                && Marshal.ReadByte(row, Row4Local + 2) == 0 && Marshal.ReadByte(row, Row4Local + 3) == 1,
                        });
                    }
                    return;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            throw new CoreDeviceHostException("GetExtendedTcpTable kept growing");
        }
    }
}
