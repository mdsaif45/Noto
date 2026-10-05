// Shared by Invoke-SingleInstance.ps1 and Invoke-SingleInstanceElevated.ps1 (A17). Loaded with Add-Type -Path.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace NotoVal
{
    public static class Instance
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateFile(string n, uint a, uint s, IntPtr sa, uint d, uint f, IntPtr t);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern uint GetFinalPathNameByHandle(IntPtr h, StringBuilder b, uint n, uint f);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateNamedPipe(string n, uint o, uint m, uint i, uint ob, uint ib, uint t, IntPtr sa);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeServerProcessId(SafeHandle h, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint a, bool i, int pid);
        [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr h);
        [DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr h);
        [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(SafeProcessHandle h, int c, ref PBI i, int l, out int r);
        [StructLayout(LayoutKind.Sequential)] struct PBI { public IntPtr R1; public IntPtr Peb; public IntPtr R2a; public IntPtr R2b; public IntPtr Pid; public IntPtr Parent; }
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr p, uint a, out IntPtr t);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool CreateRestrictedToken(IntPtr t, uint f, uint nd, SID_AND_ATTRIBUTES[] d, uint np, IntPtr p, uint nr, IntPtr r, out IntPtr n);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool DuplicateTokenEx(IntPtr t, uint a, IntPtr sa, int il, int tt, out IntPtr n);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool SetTokenInformation(IntPtr t, int c, ref TOKEN_MANDATORY_LABEL i, int l);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool ImpersonateLoggedOnUser(IntPtr t);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool ImpersonateAnonymousToken(IntPtr thread);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool RevertToSelf();
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool ConvertStringSidToSid(string s, out IntPtr sid);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
        [StructLayout(LayoutKind.Sequential)] struct SID_AND_ATTRIBUTES { public IntPtr Sid; public uint Attributes; }
        [StructLayout(LayoutKind.Sequential)] struct TOKEN_MANDATORY_LABEL { public SID_AND_ATTRIBUTES Label; }
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        delegate bool EnumProc(IntPtr h, IntPtr l);

        // ------------------------------------------------ the key, computed independently of Noto's code

        public static string PipeName(string dataRoot)
        {
            IntPtr h = CreateFile(Path.GetFullPath(dataRoot), 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (h == new IntPtr(-1)) throw new Win32Exception();
            try
            {
                var b = new StringBuilder(32768);
                uint n = GetFinalPathNameByHandle(h, b, (uint)b.Capacity, 0);
                if (n == 0) throw new Win32Exception();
                string p = b.ToString();
                if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + p.Substring(8);
                else if (p.StartsWith(@"\\?\")) p = p.Substring(4);
                while (p.Length > 3 && p.EndsWith(@"\")) p = p.Substring(0, p.Length - 1);
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.ToUpperInvariant()))).Substring(0, 32);
                return "Noto.Instance." + WindowsIdentity.GetCurrent().User.Value + "." + hash;
            }
            finally { CloseHandle(h); }
        }

        public static bool MutexExists(string name)
        {
            Mutex m;
            if (!Mutex.TryOpenExisting(@"Global\" + name, out m)) return false;
            m.Dispose(); return true;
        }

        /// <summary>An unrelated process asking for ownership. True only if it got it (then it lets go).</summary>
        public static bool TryTakeMutex(string name)
        {
            using (var m = new Mutex(false, @"Global\" + name))
            {
                bool got;
                try { got = m.WaitOne(0); } catch (AbandonedMutexException) { got = true; }
                if (got) m.ReleaseMutex();
                return got;
            }
        }

        /// <summary>An unrelated process trying to create the pipe beside the owner's. Returns the Win32 error, 0 if it succeeded.</summary>
        public static int TryCreatePipe(string name)
        {
            IntPtr h = CreateNamedPipe(@"\\.\pipe\" + name, 3, 4 | 2, 255, 64, 64, 0, IntPtr.Zero);
            if (h == new IntPtr(-1)) return Marshal.GetLastWin32Error();
            CloseHandle(h); return 0;
        }

        public static bool PipeExists(string name)
        {
            return Directory.GetFiles(@"\\.\pipe\").Any(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
        }

        public static int PipeServerPid(string name)
        {
            using (var c = new NamedPipeClientStream(".", name, PipeDirection.In))
            {
                try { c.Connect(2000); } catch (Exception) { return -1; }
                uint pid; return GetNamedPipeServerProcessId(c.SafePipeHandle, out pid) ? (int)pid : -1;
            }
        }

        /// <summary>Sends raw bytes and returns the reply byte, -1 if none, -2 if the pipe could not be opened.</summary>
        public static int SendRaw(string name, byte[] message)
        {
            using (var c = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                try { c.Connect(2000); } catch (Exception) { return -2; }
                try
                {
                    using (var t = new CancellationTokenSource(3000)) c.WriteAsync(message, 0, message.Length, t.Token).Wait();
                    var b = new byte[1];
                    using (var t = new CancellationTokenSource(3000)) return c.ReadAsync(b, 0, 1, t.Token).Result == 1 ? b[0] : -1;
                }
                catch (Exception) { return -1; }
            }
        }

        public static byte[] Frame(byte version, byte kind, byte[] payload, int declared)
        {
            var m = new byte[8 + payload.Length];
            Encoding.ASCII.GetBytes("NOTO").CopyTo(m, 0); m[4] = version; m[5] = kind;
            m[6] = (byte)(declared & 0xFF); m[7] = (byte)((declared >> 8) & 0xFF);
            payload.CopyTo(m, 8); return m;
        }

        // ------------------------------------------------ clients the pipe's ACL must refuse

        /// <summary>Opens the pipe as a client Windows must refuse. Returns the Win32 error (5 = access denied), 0 if it opened.</summary>
        public static int OpenAs(string name, string who)
        {
            int result = -1;
            var t = new Thread(() =>
            {
                IntPtr token = IntPtr.Zero, restricted = IntPtr.Zero;
                try
                {
                    if (who == "anonymous")
                    {
                        if (!ImpersonateAnonymousToken(GetCurrentThread())) throw new Win32Exception();
                    }
                    else
                    {
                        if (!OpenProcessToken(GetCurrentProcess(), 0x0002 | 0x0008 | 0x0004 | 0x0001 | 0x0080, out token)) throw new Win32Exception();
                        if (who == "no-user")
                        {
                            IntPtr sid; ConvertStringSidToSid(WindowsIdentity.GetCurrent().User.Value, out sid);
                            var deny = new[] { new SID_AND_ATTRIBUTES { Sid = sid, Attributes = 0 } };
                            if (!CreateRestrictedToken(token, 0, 1, deny, 0, IntPtr.Zero, 0, IntPtr.Zero, out restricted)) throw new Win32Exception();
                        }
                        else if (who == "low")
                        {
                            if (!DuplicateTokenEx(token, 0x02000000, IntPtr.Zero, 2, 2, out restricted)) throw new Win32Exception();
                            IntPtr low; ConvertStringSidToSid("S-1-16-4096", out low);
                            var label = new TOKEN_MANDATORY_LABEL { Label = new SID_AND_ATTRIBUTES { Sid = low, Attributes = 0x20 } };
                            if (!SetTokenInformation(restricted, 25, ref label, Marshal.SizeOf(label) + 12)) throw new Win32Exception();
                        }
                        else throw new ArgumentException(who);
                        if (!ImpersonateLoggedOnUser(restricted)) throw new Win32Exception();
                    }
                    IntPtr h = CreateFile(@"\\.\pipe\" + name, 0xC0000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    result = h == new IntPtr(-1) ? Marshal.GetLastWin32Error() : 0;
                    if (h != new IntPtr(-1)) CloseHandle(h);
                }
                finally
                {
                    RevertToSelf();
                    if (restricted != IntPtr.Zero) CloseHandle(restricted);
                    if (token != IntPtr.Zero) CloseHandle(token);
                }
            });
            t.Start(); t.Join();
            return result;
        }

        // ------------------------------------------------ processes

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenThread(uint a, bool i, int tid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern int SuspendThread(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] static extern int ResumeThread(IntPtr h);
        public static void SuspendThreadId(int tid) { var h = OpenThread(0x0002, false, tid); SuspendThread(h); CloseHandle(h); }
        public static void ResumeThreadId(int tid) { var h = OpenThread(0x0002, false, tid); ResumeThread(h); CloseHandle(h); }
        public static void Suspend(int pid) { var h = OpenProcess(0x0800, false, pid); NtSuspendProcess(h); CloseHandle(h); }
        public static void Resume(int pid) { var h = OpenProcess(0x0800, false, pid); NtResumeProcess(h); CloseHandle(h); }

        public static int ParentOf(Process p)
        {
            var i = new PBI(); int r;
            return NtQueryInformationProcess(p.SafeHandle, 0, ref i, Marshal.SizeOf(i), out r) == 0 ? (int)i.Parent : -1;
        }

        /// <summary>Waits for <paramref name="count"/> new processes by that name, opening each one's handle at once.</summary>
        public static Process[] WaitNew(string name, HashSet<int> seen, int count, int timeoutMs)
        {
            var found = new List<Process>();
            var clock = Stopwatch.StartNew();
            while (found.Count < count && clock.ElapsedMilliseconds < timeoutMs)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    if (seen.Contains(p.Id)) { p.Dispose(); continue; }
                    try { var _ = p.SafeHandle; seen.Add(p.Id); found.Add(p); } catch (Exception) { p.Dispose(); }
                }
                Thread.Sleep(5);
            }
            return found.ToArray();
        }

        public static IntPtr[] VisibleWindows(int pid, bool dialogs)
        {
            var list = new List<IntPtr>();
            EnumWindows((h, l) =>
            {
                int p; GetWindowThreadProcessId(h, out p);
                if (p == pid && IsWindowVisible(h))
                {
                    var c = new StringBuilder(64); GetClassName(h, c, 64);
                    if ((c.ToString() == "#32770") == dialogs) list.Add(h);
                }
                return true;
            }, IntPtr.Zero);
            return list.ToArray();
        }

        /// <summary>
        /// Watches a process until it exits or the time runs out: the most visible non-dialog windows it ever had,
        /// and the first message box it showed (text read, then dismissed with OK) and when.
        /// </summary>
        public static string Watch(Process p, int timeoutMs, bool dismissDialog)
        {
            int maxWindows = 0; long dialogAt = -1; string dialogText = "";
            var clock = Stopwatch.StartNew();
            while (!p.HasExited && clock.ElapsedMilliseconds < timeoutMs)
            {
                maxWindows = Math.Max(maxWindows, VisibleWindows(p.Id, false).Length);
                var d = VisibleWindows(p.Id, true);
                if (d.Length > 0)
                {
                    if (dialogAt < 0) { dialogAt = clock.ElapsedMilliseconds; dialogText = DialogText(d[0]); }
                    // OK, as the user would press it; repeated until the box is gone, since a message posted
                    // while the box is still initialising can be lost. WM_CLOSE as well: OK-only boxes accept it.
                    if (dismissDialog && clock.ElapsedMilliseconds - dialogAt >= 200)
                    {
                        PostMessage(d[0], 0x0111, new IntPtr(1), IntPtr.Zero);
                        PostMessage(d[0], 0x0010, IntPtr.Zero, IntPtr.Zero);
                        Thread.Sleep(300);
                    }
                }
                Thread.Sleep(10);
            }
            bool exited = p.HasExited;
            if (exited) p.WaitForExit();
            return string.Format("exited={0};code={1};ms={2};maxWindows={3};dialogMs={4};dialog={5}",
                exited, exited ? p.ExitCode.ToString() : "", clock.ElapsedMilliseconds, maxWindows, dialogAt, dialogText.Replace(";", ","));
        }

        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint f);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        /// <summary>The class of the top-level window under a screen point.</summary>
        public static string RootClassAt(int x, int y)
        {
            var root = GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2);
            var c = new StringBuilder(64); GetClassName(root, c, 64); return c.ToString();
        }

        public static string DialogText(IntPtr dialog)
        {
            var parts = new List<string>();
            EnumChildWindows(dialog, (h, l) =>
            {
                var c = new StringBuilder(64); GetClassName(h, c, 64);
                if (c.ToString() == "Static") { var t = new StringBuilder(1024); GetWindowText(h, t, 1024); if (t.Length > 0) parts.Add(t.ToString()); }
                return true;
            }, IntPtr.Zero);
            return string.Join(" ", parts);
        }
    }

    /// <summary>A same-user process squatting a pipe name: it records what the first client sends, and never answers.</summary>
    public sealed class Squatter : IDisposable
    {
        readonly NamedPipeServerStream _pipe;
        public volatile string Received = "nothing";

        public Squatter(string name)
        {
            _pipe = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
            var t = new Thread(() =>
            {
                try
                {
                    _pipe.WaitForConnectionAsync().Wait();
                    var b = new byte[64];
                    int n = _pipe.ReadAsync(b, 0, b.Length).Result;
                    Received = BitConverter.ToString(b, 0, n);
                }
                catch (Exception e) { if (Received == "nothing") Received = "error: " + e.GetBaseException().Message; }
            }) { IsBackground = true };
            t.Start();
        }

        public void Dispose() { _pipe.Dispose(); }
    }

    /// <summary>
    /// Reads OutputDebugString — where Noto's Trace diagnostics go — and can freeze one thread the instant its
    /// process writes a given line: the writer waits for this reader before its next line, so it cannot get past it.
    /// </summary>
    public sealed class DebugLog : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateEvent(IntPtr sa, bool m, bool i, string n);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateFileMapping(IntPtr f, IntPtr sa, uint p, uint h, uint l, string n);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr MapViewOfFile(IntPtr m, uint a, uint h, uint l, UIntPtr s);
        [DllImport("kernel32.dll")] static extern bool SetEvent(IntPtr e);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        readonly IntPtr _ready, _data, _view; readonly Thread _thread; volatile bool _stop;
        readonly List<string> _lines = new List<string>();
        int _freezePid, _freezeTid; string _freezeOn; public readonly ManualResetEventSlim Frozen = new ManualResetEventSlim();

        public DebugLog()
        {
            _ready = CreateEvent(IntPtr.Zero, false, false, "DBWIN_BUFFER_READY");
            bool existed = Marshal.GetLastWin32Error() == 183;
            _data = CreateEvent(IntPtr.Zero, false, false, "DBWIN_DATA_READY");
            IntPtr map = CreateFileMapping(new IntPtr(-1), IntPtr.Zero, 4, 0, 4096, "DBWIN_BUFFER");
            _view = MapViewOfFile(map, 4, 0, 0, new UIntPtr(4096));
            if (existed) throw new InvalidOperationException("another debug-output reader is running (close DebugView or the debugger)");
            _thread = new Thread(Loop) { IsBackground = true }; _thread.Start();
        }

        public void FreezeOn(int pid, int tid, string text) { Frozen.Reset(); _freezePid = pid; _freezeTid = tid; _freezeOn = text; }

        void Loop()
        {
            SetEvent(_ready);
            while (!_stop)
            {
                if (WaitForSingleObject(_data, 100) != 0) continue;
                int pid = Marshal.ReadInt32(_view);
                string s = (Marshal.PtrToStringAnsi(_view + 4) ?? "").TrimEnd();
                if (s.StartsWith("Noto.Instance"))
                {
                    lock (_lines) _lines.Add(string.Format("{0:HH:mm:ss.fff} pid={1} {2}", DateTime.Now, pid, s));
                    if (_freezeOn != null && pid == _freezePid && s.Contains(_freezeOn)) { Instance.SuspendThreadId(_freezeTid); _freezeOn = null; Frozen.Set(); }
                }
                SetEvent(_ready);
            }
        }

        public string[] Lines(int pid) { lock (_lines) return _lines.Where(l => l.Contains(" pid=" + pid + " ")).ToArray(); }
        public string[] All() { lock (_lines) return _lines.ToArray(); }
        public void Dispose() { _stop = true; _thread.Join(); }
    }
}
