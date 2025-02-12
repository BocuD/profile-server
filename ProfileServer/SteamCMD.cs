using System.Diagnostics;
using System.IO.Compression;
using Serilog;

namespace ProfileServer;

public class SteamCMD
{
    public bool installed = false;
    public bool loggedIn = false;
    public bool lastCommandFinished = true;
    public Process? process = null;
    
    private readonly string username = "";
    private readonly string password = "";
    
    public SteamCMD(string username, string password)
    {
        this.username = username;
        this.password = password;
        
        //check if steamcmd is installed
        installed = File.Exists("steamcmd/steamcmd.exe");

        if (!installed)
        {
            Log.Warning("SteamCMD is not installed. Please install it to continue.");
        }
    }

    public async Task RunInitialInstallation()
    {
        Log.Information("Downloading SteamCMD...");
        
        //download steamcmd
        var client = new HttpClient();
        var response = await client.GetStreamAsync("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip");
        using var fs = new FileStream("steamcmd.zip", FileMode.Create);
        await response.CopyToAsync(fs);
        fs.Close();
        
        Log.Information("Download complete. Extracting..");
    
        //extract steamcmd
        ZipFile.ExtractToDirectory("steamcmd.zip", ".");
        File.Delete("steamcmd.zip");
        
        //create the steamcmd directory
        Directory.CreateDirectory("steamcmd");
        File.Move("steamcmd.exe", "steamcmd/steamcmd.exe");
        
        Log.Information("SteamCMD downloaded. Running initial install...");
        
        //run steamcmd to install the initial files
        ProcessStartInfo startInfo = new()
        {
            FileName = "steamcmd/steamcmd.exe",
            Arguments = "+quit",
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        
        Process process = new()
        {
            StartInfo = startInfo
        };
        
        //direct output to the console
        process.OutputDataReceived += (sender, args) => Log.Information("SteamCMD: " + args.Data);
        process.ErrorDataReceived += (sender, args) => Log.Error("SteamCMD: " + args.Data);
        
        process.Start();
        
        //wait for steamcmd to finish
        await process.WaitForExitAsync();

        installed = true;
    }

    public async Task<bool> RunCommand(string command)
    {
        if (!installed)
        {
            Log.Error("SteamCMD is not installed. Cannot run command.");
            return false;
        }

        if (process == null)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "steamcmd/steamcmd.exe",
                Arguments = $"+login {username} {password}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };

            process = new()
            {
                StartInfo = startInfo
            };

            //set up output handlers
            process.OutputDataReceived += HandleSteamOutput;
            process.ErrorDataReceived += HandleSteamError;

            process.Start();

            //set up standard output reader
            process.BeginOutputReadLine();
        }

        while (!loggedIn)
        {
            await Task.Delay(100);
        }
        
        while (!lastCommandFinished)
        {
            await Task.Delay(100);
        }
        
        Log.Information("[steamcmd IN] Steam>" + command);
        await process.StandardInput.WriteLineAsync(command);
        lastCommandFinished = false;
        return true;
    }

    private void HandleSteamOutput(object sender, DataReceivedEventArgs args)
    {
        Log.Information("[steamcmd] " + args.Data);
        
        //check the command output to determine state
        // if (args.Data?.Contains("Loading Steam API...OK") == true)
        // {
        //     loggedIn = true;
        //     Log.Information("steamcmd logged in.");
        // }

        if (args.Data?.Contains("Steam Guard Mobile Authenticator") == true)
        {
            Log.Warning("SteamCMD: Steam Guard is enabled. Please enter the code.");
            
            //prompt for steam guard code
            string? code = Console.ReadLine();
            
            //send the code to steamcmd
            process?.StandardInput.WriteLine(code);
        }
    }

    private void HandleSteamError(object sender, DataReceivedEventArgs args)
    {
        Log.Error("SteamCMD: " + args.Data);
    }
}