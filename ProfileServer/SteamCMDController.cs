using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using SteamCMD.ConPTY;
using SteamCMD.ConPTY.Interop.Definitions;

namespace ProfileServer;

public class SteamCMDController
{
    private SteamCMDConPTY steamCmd;

    private bool running = false;
    private bool authenticated = false;
    private bool lastCommandCompleted = false;
    private string lastMessage = "";
    
    private int processId = 0;

    private string username;
    private string password;
    
    public SteamCMDController(string username, string password)
    {
        steamCmd = new SteamCMDConPTY
        {
            Arguments = $"+login {username} {password}",
            WorkingDirectory = Environment.CurrentDirectory + "/steamcmd"
        };
        
        steamCmd.OutputDataReceived += (sender, data) =>
        {
            try
            {
                SuspendProcess(processId);
                
                if (string.IsNullOrWhiteSpace(data)) return;

                //suspend process while processing data
                
                //process each line individually
                string[] lines = data.Split('\n');

                foreach (string line in lines)
                {
                    //remove all \r and \n characters
                    string cleanLine = line.Replace("\r", "").Replace("\n", "");
                    if (string.IsNullOrWhiteSpace(cleanLine)) continue;
                    Log.Information("[steamcmd] {line}", cleanLine);
                    
                    //we need 2FA
                    if (line.Contains("Two-factor code:"))
                    {
                        string twoFactorCode = Console.ReadLine();
                        steamCmd.WriteLine(twoFactorCode);
                    }
                    
                    if (line.Contains("Steam>")) lastCommandCompleted = true;

                    if (line.Contains("OK"))
                    {
                        if (lastMessage.Contains("Waiting for user info...") || line.Contains("OK"))
                        {
                            authenticated = true;
                        }
                    }

                    if (line.Contains("FAILED"))
                    {
                        if (line.Contains("Two-factor code mismatch"))
                        {
                            ResumeProcess(processId);
                            
                            //retry login
                            steamCmd.WriteLine($"login {username} {password}");
                        }
                    }

                    lastMessage = line;
                }
            }
            finally
            {
                ResumeProcess(processId);
            }
        };
        
        steamCmd.Exited += (sender, args) =>
        {
            Log.Information("SteamCMD exited.");
            running = false;
        };

        running = true;
        
        //ensure the steamcmd directory exists
        if (!Directory.Exists("steamcmd"))
        {
            Directory.CreateDirectory("steamcmd");
        }
        
        ProcessInfo info = steamCmd.Start();
        processId = info.dwProcessId;
    }

    public async Task<bool> RunCommand(string command)
    {
        if (!running)
        {
            return false;
        }
        
        while (!lastCommandCompleted || !authenticated)
        {
            await Task.Delay(100);
        }

        await steamCmd.WriteLineAsync(command);
        return true;
    }
    
    
    [Flags]
    public enum ThreadAccess : int
    {
        TERMINATE = (0x0001),
        SUSPEND_RESUME = (0x0002),
        GET_CONTEXT = (0x0008),
        SET_CONTEXT = (0x0010),
        SET_INFORMATION = (0x0020),
        QUERY_INFORMATION = (0x0040),
        SET_THREAD_TOKEN = (0x0080),
        IMPERSONATE = (0x0100),
        DIRECT_IMPERSONATION = (0x0200)
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
    [DllImport("kernel32.dll")]
    static extern uint SuspendThread(IntPtr hThread);
    [DllImport("kernel32.dll")]
    static extern int ResumeThread(IntPtr hThread);
    [DllImport("kernel32", CharSet = CharSet.Auto,SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);


    private static void SuspendProcess(int pid)
    {
        var process = Process.GetProcessById(pid); // throws exception if process does not exist

        foreach (ProcessThread pT in process.Threads)
        {
            IntPtr pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);

            if (pOpenThread == IntPtr.Zero)
            {
                continue;
            }

            SuspendThread(pOpenThread);

            CloseHandle(pOpenThread);
        }
    }

    public static void ResumeProcess(int pid)
    {
        var process = Process.GetProcessById(pid);

        if (process.ProcessName == string.Empty)
            return;

        foreach (ProcessThread pT in process.Threads)
        {
            IntPtr pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);

            if (pOpenThread == IntPtr.Zero)
            {
                continue;
            }

            var suspendCount = 0;
            do
            {
                suspendCount = ResumeThread(pOpenThread);
            } while (suspendCount > 0);

            CloseHandle(pOpenThread);
        }
    }
}