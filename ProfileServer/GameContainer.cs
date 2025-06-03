using System.Diagnostics;
using System.Globalization;
using System.Management.Automation;
using Serilog;

namespace ProfileServer;

public class GameContainer(string workingDirectory, string executable, string args, string gameFolder)
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
        
        //check log folders before starting the game
        string logDirectory = $"{gameFolder}/Saved/Profiling/FPSChartStats";
        string fullLogDirectory = Path.Combine(workingDirectory, logDirectory);
        await DiscordBot.Instance.UpdateMessageContent(message, $"Checking performance log directory at {fullLogDirectory}...");
        if (!Directory.Exists(fullLogDirectory))
        {
            await DiscordBot.Instance.UpdateMessageContent(message, "Creating performance log directory...");
            Directory.CreateDirectory(fullLogDirectory);
        }
        
        //get subdirectories
        string[] subdirectories = Directory.GetDirectories(fullLogDirectory);
        
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
            await GetPerformanceData(message, fullLogDirectory, subdirectories);
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
                    
                    long fileSize = new FileInfo(output).Length;

                    if (Environment.GetEnvironmentVariable("ENABLE_GIT") == "TRUE")
                    {
                        //if bigger than 100MB, don't upload to git
                        if (fileSize > 100 * 1024 * 1024)
                        {
                            Log.Warning("Trace data is too large to upload to git: {FileSize}", PrettyBytes(fileSize));
                            await DiscordBot.Instance.UpdateMessageContent(message,
                                "Trace data is too large to upload to git: " + PrettyBytes(fileSize));
                        }
                        else
                        {
                            //commit the new file to git
                            string gitPath = Path.Combine(Environment.CurrentDirectory, "output");
                            string gitExe = "\"C:\\Program Files\\Git\\bin\\git.exe\"";
                            string gitCommand =
                                $"& {gitExe} add {output} && & {gitExe} commit -m \"Trace data\" && & {gitExe} push";

                            using (PowerShell powerShell = PowerShell.Create())
                            {
                                powerShell.AddScript($"cd {gitPath}");
                                powerShell.AddScript(gitCommand);
                                var results = powerShell.Invoke();

                                if (powerShell.HadErrors)
                                {
                                    foreach (var error in powerShell.Streams.Error)
                                    {
                                        //Git error: remote: warning: GH001: Large files detected. You may want to try Git Large File Storage - https://git-lfs.github.com/.17:34:00Git error: To https://github.com/BUAS-Game1/PaletteCleanserProfileOutput.git
                                        //remove large file detected urls
                                        string filtered = error.ToString();
                                        filtered = filtered.Replace("https://git-lfs.github.com/", "");
                                        Log.Error("Git error: {Error}", filtered);
                                        await DiscordBot.Instance.UpdateMessageContent(message, "Git error: " + error);
                                    }
                                }
                                else
                                {
                                    Log.Information("Git command executed successfully");
                                    string commandOutput = string.Join(Environment.NewLine, results);
                                    Log.Information("Git command output: {Output}", commandOutput);
                                    await DiscordBot.Instance.UpdateMessageContent(message, $"Git: {commandOutput}");
                                }
                            }
                        }
                    }

                    Log.Information("Trace data collected: {Trace} ({fileSize})", Path.GetFileName(trace), PrettyBytes(fileSize));
                }
            }
        }
    }
    
    private async Task GetPerformanceData(ulong message, string fullLogDirectory, string[] subdirectories)
    {
        //get subdirectories
        string[] updatedSubDirectories = Directory.GetDirectories(fullLogDirectory);
        
        //check if any new subdirectories were created
        List<string> newSubdirs = updatedSubDirectories.Where(directory => !subdirectories.Contains(directory)).ToList();
        
        if (newSubdirs.Count == 0)
        {
            await DiscordBot.Instance.UpdateMessageContent(message, "No new performance data found");
            return;
        }

        foreach (string directory in newSubdirs)
        {
            await DiscordBot.Instance.UpdateMessageContent(message, "New performance data found: " + directory);
            
            //get the .log file
            string[] logFiles = Directory.GetFiles(directory, "*.log");

            foreach (string logFile in logFiles)
            {
                await DiscordBot.Instance.SendFile(logFile, "Performance log collected");
                await DiscordBot.Instance.UpdateMessageContent(message,
                    "Performance log collected: " + Path.GetFileName(logFile));
            }
            
            //get the .csv file
            string[] csvFiles = Directory.GetFiles(directory, "*.csv");
            
            foreach (string csvFile in csvFiles)
            {
                await DiscordBot.Instance.SendFile(csvFile, "Performance CSV data collected");
                await DiscordBot.Instance.UpdateMessageContent(message,
                    "Performance CSV data collected: " + Path.GetFileName(csvFile));
            }
            
            //parse the .csv
            foreach (string csvFile in csvFiles)
            {
                string[] lines = await File.ReadAllLinesAsync(csvFile);
                if (lines.Length == 0) continue;
                
                //disregard the first 4 lines
                if (lines.Length > 4)
                {
                    lines = lines.Skip(4).ToArray();
                }
                
                //remove line 1, 2, 3 and 4
                lines = lines.Where((_, index) => index is 0 or > 10).ToArray();
                
                //write the modified lines to a new file
                string csvFileName = Path.GetFileNameWithoutExtension(csvFile);
                string csvFilePath = Path.Combine(directory, $"{csvFileName}_processed.csv");
                await File.WriteAllLinesAsync(csvFilePath, lines);

                //parse the first line
                string[] headers = lines[0].Split(',');
                
                List<float> frameTimes = [];
                List<float> gameThreadTimes = [];
                List<float> renderThreadTimes = [];
                List<float> gpuTimes = [];
                
                //parse the rest of the lines (skipping first 20 frames)
                for (int i = 20; i < lines.Length; i++)
                {
                    string[] values = lines[i].Split(',');
                    frameTimes.Add(float.Parse(values[1]));
                    gameThreadTimes.Add(float.Parse(values[2]));
                    renderThreadTimes.Add(float.Parse(values[3]));
                    gpuTimes.Add(float.Parse(values[4]));
                }
                
                //calculate the average frame time
                float averageFrameTime = frameTimes.Average();

                //calculate the 99th percentile frame time
                int index = (int) (frameTimes.Count * 0.99);
                float percentile99 = frameTimes.OrderBy(x => x).ElementAt(index);

                //calculate the 95th percentile frame time
                index = (int) (frameTimes.Count * 0.95);
                float percentile95 = frameTimes.OrderBy(x => x).ElementAt(index);

                //calculate maximum frame time
                float maxFrameTime = frameTimes.Max();
                
                float averageGameThreadTime = gameThreadTimes.Average();
                float averageRenderThreadTime = renderThreadTimes.Average();
                float averageGpuTime = gpuTimes.Average();
                
                //generate svg by running csvtosvg
                string csvToSvgPath = Environment.GetEnvironmentVariable("CSVTOSVGPATH") ?? "";
                string pngPath = "";
                if (!string.IsNullOrEmpty(csvToSvgPath))
                {
                    Log.Information("Generating performance preview image...");
                    //run the tool
                    string svgFile = Path.Combine(directory, "performance.svg");
                    ProcessStartInfo startInfo = new()
                    {
                        FileName = csvToSvgPath,
                        Arguments = $"-csvs {csvFilePath} -o {svgFile} -ignoreStats \"Time (sec)\" DynRes Percentile -stats *",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    //log exe path and arguments
                    Log.Information("Running csvtosvg: {Exe} {Args}", startInfo.FileName, startInfo.Arguments);
                    
                    using Process process = new() { StartInfo = startInfo };
                    process.Start();
                    
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode != 0)
                    {
                        Log.Error("Error running csvtosvg: {Error}", error);
                        await DiscordBot.Instance.UpdateMessageContent(message, "Error running csvtosvg: " + error);
                    }
                    else
                    {
                        await DiscordBot.Instance.UpdateMessageContent(message,
                            "Performance SVG data collected: " + Path.GetFileName(svgFile));
                        
                        //convert to png
                        pngPath = Path.ChangeExtension(svgFile, ".png");
                        ProcessStartInfo pngStartInfo = new()
                        {
                            FileName = "magick",
                            Arguments = $"{svgFile} {pngPath}",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        
                        using Process pngProcess = new() { StartInfo = pngStartInfo };
                        pngProcess.Start();
                        string pngOutput = await pngProcess.StandardOutput.ReadToEndAsync();
                        string pngError = await pngProcess.StandardError.ReadToEndAsync();
                        await pngProcess.WaitForExitAsync();
                        
                        if (pngProcess.ExitCode != 0)
                        {
                            Log.Error("Error running magick: {Error}", pngError);
                            await DiscordBot.Instance.UpdateMessageContent(message, "Error running magick: " + pngError);
                        }
                        else
                        {
                            await DiscordBot.Instance.UpdateMessageContent(message,
                                "Performance preview image collected: " + Path.GetFileName(pngPath));
                        }
                    }
                }
                
                //send the results to discord
                await DiscordBot.Instance.SendPerformanceReportEmbed(
                    averageFrameTime, percentile95, percentile99, maxFrameTime, averageGameThreadTime, averageRenderThreadTime, averageGpuTime, csvFile, pngPath);
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