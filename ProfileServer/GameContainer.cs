using System.Diagnostics;
using System.Globalization;
using Serilog;

namespace ProfileServer;

public class GameContainer(string workingDirectory, string executable)
{
    //start game process
    private readonly Process gameProcess = new()
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = workingDirectory + executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        }
    };
    private DateTime startTime;
    public bool isRunning => !gameProcess.HasExited;

    public async Task Run(ulong message)
    {
        startTime = DateTime.Now;
        await Task.Delay(1000);
        gameProcess.Start();
        
        await DiscordBot.Instance.UpdateMessageContent(message, "Game started");
        
        //start a task to watch the game exit and collect trace
        await Task.Run(async () =>
        {
            await gameProcess.WaitForExitAsync();
            await DiscordBot.Instance.UpdateMessageContent(message, "Game exited with code " + gameProcess.ExitCode);
            Log.Information("Game exited with code {ExitCode}", gameProcess.ExitCode);
            await GetTraceData(message);
        });
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
                    File.Copy(trace, output);

                    await DiscordBot.Instance.UpdateMessageContent(message,
                        "Trace data collected: " + Path.GetFileName(trace));
                    Log.Information("Trace data collected: {Trace}", Path.GetFileName(trace));
                }
            }
        }
    }
}