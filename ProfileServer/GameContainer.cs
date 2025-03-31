using System.Diagnostics;
using System.Globalization;
using Serilog;

namespace ProfileServer;

public class GameContainer(string workingDirectory, string executable, string args)
{
    //start game process
    private readonly Process gameProcess = new()
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = workingDirectory + executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            Arguments = args
        }
    };
    private DateTime startTime;
    public bool IsRunning => !gameProcess.HasExited;

    public async Task<bool> Run(ulong message)
    {
        startTime = DateTime.Now;
        
        //adding a delay here to ensure the trace timestamp is after the game start
        await Task.Delay(2000);

        int exitCode = -1;
        
        gameProcess.Start();
        
        await DiscordBot.Instance.UpdateMessageContent(message, "Game started");
        
        //start a task to watch the game exit and collect trace
        await Task.Run(async () =>
        {
            await gameProcess.WaitForExitAsync();
            await DiscordBot.Instance.UpdateMessageContent(message, "Game exited with code " + gameProcess.ExitCode);
            Log.Information("Game exited with code {ExitCode}", gameProcess.ExitCode);
            
            if (gameProcess.ExitCode != 0)
            {
                Log.Warning("Game exited with non-zero exit code");
                await DiscordBot.Instance.UpdateMessageContent(message, "Warning: Game exited with non-zero exit code!");
            }
            
            exitCode = gameProcess.ExitCode;

            //give the game some time to finish writing the trace
            await Task.Delay(3000);
            
            await DiscordBot.Instance.UpdateMessageContent(message, "Collecting trace data...");
            Log.Information("Collecting trace data...");
            await GetTraceData(message);
        });

        return exitCode == 0;
    }
    
    public void Stop()
    {
        if (gameProcess.HasExited) return;
        
        gameProcess.Kill();
        Log.Information("Game force stopped");
    }

    private async Task GetTraceData(ulong message)
    {
        //trace path
        string tracePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnrealEngine", "Common", "UnrealTrace", "Store", "001");
        
        //parse the timestamps of each trace in the directory
        foreach (string trace in Directory.EnumerateFiles(tracePath, "*.utrace"))
        {
            string datetime = Path.GetFileNameWithoutExtension(trace).Replace("_", "");
            if (DateTime.TryParseExact(datetime, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out DateTime traceTime))
            {
                if (traceTime > startTime)
                {
                    //ensure output directory exists
                    if (!Directory.Exists(Path.Combine(Environment.CurrentDirectory, "output")))
                    {
                        Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "output"));
                    }
                    
                    //copy the trace to the output directory
                    string output = Path.Combine(Environment.CurrentDirectory, "output", Path.GetFileName(trace));
                    File.Move(trace, output);

                    await DiscordBot.Instance.SendFile(output, "Trace data collected");

                    await DiscordBot.Instance.UpdateMessageContent(message,
                        "Trace data collected: " + Path.GetFileName(trace));

                    if (Environment.GetEnvironmentVariable("ENABLE_GIT") == "TRUE") 
                    {
                        //commit the new file to git
                        string gitPath = Path.Combine(Environment.CurrentDirectory, "output");
                        string gitCommand = $"git add {output} && git commit -m \"Trace data\" && git push";

                        ProcessStartInfo startInfo = new()
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/C {gitCommand}",
                            WorkingDirectory = gitPath,
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using Process process = new()
                        {
                            StartInfo = startInfo
                        };

                        process.Start();

                        await process.WaitForExitAsync();

                        string outputResult = await process.StandardOutput.ReadToEndAsync();

                        await DiscordBot.Instance.UpdateMessageContent(message, outputResult);
                    }

                    long fileSize = new FileInfo(output).Length;
                    
                    Log.Information("Trace data collected: {Trace} ({fileSize})", Path.GetFileName(trace), PrettyBytes(fileSize));
                }
            }
        }
    }

    private string PrettyBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024) + " KB";
        if (bytes < 1024 * 1024 * 1024) return (bytes / 1024 / 1024) + " MB";
        return (bytes / 1024 / 1024 / 1024) + " GB";
    }
}