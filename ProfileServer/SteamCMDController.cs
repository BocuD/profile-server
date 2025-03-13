using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Discord.Rest;
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
    
    private readonly int processId = 0;
    
    private static readonly Regex ansiEscape = new(@"\x1B\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);
    
    public SteamCMDController(string username, string password)
    {
        steamCmd = new SteamCMDConPTY
        {
            Arguments = $"+login {username} {password}",
            WorkingDirectory = Environment.CurrentDirectory + "/steamcmd"
        };
        
        steamCmd.OutputDataReceived += async (sender, data) =>
        {
            try
            {
                //suspend process while processing data
                SuspendProcess(processId);
                
                if (string.IsNullOrWhiteSpace(data)) return;

                //process each line individually
                //remove all \r and ansi escape characters
                string cleanLine = ansiEscape.Replace(data, "").Replace("\r", "");
                if (string.IsNullOrWhiteSpace(cleanLine)) return;
                
                string[] cleanedLines = cleanLine.Split('\n');
                foreach (string line in cleanedLines) 
                {
                    Log.Information("[steamcmd] {line}", line);

                    if (statusMessage != 0)
                    {
                        //filter out the styling stuff only intended for the console
                        await DiscordBot.Instance.UpdateMessageContent(statusMessage, "[steamcmd] " + line.Replace("\n", ""));
                    }
                    
                    //we need 2FA
                    if (line.Contains("Two-factor code:"))
                    {
                        string twoFactorCode = await DiscordBot.Instance.Get2FACode();
                        Log.Information("2FA code received: {code}", twoFactorCode);
                        await steamCmd.WriteLineAsync(twoFactorCode);
                    }
                    
                    if (line.Contains("a Steam Guard mobile"))
                    {
                        await DiscordBot.Instance.SendMessage("[steamcmd] " + line);
                    }
                    
                    //we need to wait for the user to accept the steam guard prompt
                    if (line.Contains("Please confirm the login in the Steam Mobile app on your phone."))
                    {
                        await DiscordBot.Instance.SendMessage("[steamcmd] Please confirm the login in the Steam Mobile app on your phone.");
                    }
                    
                    if (line.Contains("Steam>")) lastCommandCompleted = true;

                    if (line.Contains("OK"))
                    {
                        if (lastMessage.Contains("Waiting for user info...") || line.Contains("Waiting for user info..."))
                        {
                            authenticated = true;
                            await DiscordBot.Instance.SendMessage("[steamcmd] Successfully authenticated with Steam.");
                            Log.Information("Successfully authenticated with Steam.");
                        }
                    }

                    if (line.Contains("FAILED"))
                    {
                        if (line.Contains("Two-factor code mismatch"))
                        {
                            //retry login
                            await steamCmd.WriteLineAsync($"login {username} {password}");
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
        
        ProcessInfo info = steamCmd.Start(1000);
        processId = info.dwProcessId;
    }

    public async Task<bool> RunCommand(string command, ulong message = 0)
    {
        if (!running)
        {
            return false;
        }
        
        if (!lastCommandCompleted || !authenticated)
        {
            //send a message to indicate the last command is still running
            await DiscordBot.Instance.SendMessage("[steamcmd] The last command is still running, please wait.");
        }
        
        while (!lastCommandCompleted || !authenticated)
        {
            await Task.Delay(100);
        }

        lastCommandCompleted = false;
        statusMessage = message;
        await steamCmd.WriteLineAsync(command);
        
        while (!lastCommandCompleted)
        {
            await Task.Delay(100);
        }
        
        statusMessage = 0;
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
    private static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
    [DllImport("kernel32.dll")]
    private static extern uint SuspendThread(IntPtr hThread);
    [DllImport("kernel32.dll")]
    private static extern int ResumeThread(IntPtr hThread);
    [DllImport("kernel32", CharSet = CharSet.Auto,SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);


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

            int suspendCount = 0;
            do
            {
                suspendCount = ResumeThread(pOpenThread);
            } while (suspendCount > 0);

            CloseHandle(pOpenThread);
        }
    }

    private ulong statusMessage;
    public async Task UpdateGame(string gameId, string betaBranch, ulong message)
    {
        await RunCommand("force_install_dir ./game", message);
        await RunCommand($"app_update {gameId} -beta {betaBranch} validate", message);
    }
}