using System.Diagnostics;
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using ProfileServer.Database;
using Serilog;

namespace ProfileServer;

public class DiscordBot
{
    public static DiscordBot Instance;

    private static readonly string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ??
                                           throw new ArgumentNullException(
                                               "DISCORD_TOKEN environment variable not set.");

    private static readonly ulong guildId = ulong.Parse(Environment.GetEnvironmentVariable("DISCORD_GUILD") ??
                                                        throw new ArgumentNullException(
                                                            "DISCORD_GUILD_ID environment variable not set."));

    private static readonly ulong channelId = ulong.Parse(Environment.GetEnvironmentVariable("DISCORD_CHANNEL") ??
                                                          throw new ArgumentNullException(
                                                              "DISCORD_CHANNEL environment variable not set."));

    private readonly DiscordSocketClient client;    
    private readonly SteamCMDController steamCmdController;
    private GameContainer? gameContainer;
    
    private readonly string gameId = Environment.GetEnvironmentVariable("STEAMGAMEID") ?? 
                                     throw new ArgumentNullException("STEAMGAMEID environment variable not set.");
    
    private readonly string betaBranch = Environment.GetEnvironmentVariable("STEAMBETABRANCH") ?? 
                                         throw new ArgumentNullException("STEAMBETABRANCH environment variable not set.");
    
    private readonly string gameExecutable = Environment.GetEnvironmentVariable("GAMEBINARY") ?? 
                                              throw new ArgumentNullException("GAMEBINARY environment variable not set.");
    
    private readonly string gameArgs = Environment.GetEnvironmentVariable("GAMEARGS") ?? 
                                       throw new ArgumentNullException("GAMEARGS environment variable not set.");
    
    private readonly string gameFolder = Environment.GetEnvironmentVariable("GAMEFOLDER") ?? 
                                         throw new ArgumentNullException("GAMEFOLDER environment variable not set.");
    
    private readonly string unrealInsightsPath = Environment.GetEnvironmentVariable("UNREALINSIGHTSPATH") ??
                                throw new ArgumentNullException("UNREALINSIGHTSPATH environment variable not set.");

    private static PerformanceDbContext _dbContext = new();
    
    public DiscordBot()
    {
        Instance = this;
        
        client = new DiscordSocketClient();
        
        //get steam credentials
        string? username = Environment.GetEnvironmentVariable("STEAMUSERNAME");
        string? password = Environment.GetEnvironmentVariable("STEAMPASSWORD");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Log.Error("Steam username or password not set in environment!");
            throw new ArgumentNullException("Steam username or password not set in environment!");
        }

        //start steamcmd
        steamCmdController = new SteamCMDController(username, password);
    }

    private SocketGuild guild;
    private SocketTextChannel channel;
    
    public async Task Init()
    {
        await _dbContext.Database.MigrateAsync();
        
        VerifyUnrealInsights();
        
        client.Log += msg =>
        {
            Log.Information("[Discord] " + msg.Message);
            return Task.CompletedTask;
        };
        
        await client.LoginAsync(TokenType.Bot, token);
        await client.StartAsync();
        
        await client.SetStatusAsync(UserStatus.Online);

        client.Ready += async () =>
        {
            VerifyGuild();
            
            await CreateGuildCommand("update-game", "Update the game to the latest version", async (c, m) =>
            {
                await steamCmdController.UpdateGame(gameId, betaBranch, m);
                return true;
            });
            
            await CreateGuildCommand("run-game", "Start a game instance and collect trace data", async (c, m) =>
            {
                if (gameContainer is { IsRunning: true })
                {
                    await UpdateMessageContent(m, "Game is already running.");
                    return false;
                }

                VerifyUnrealInsights();
                gameContainer = new GameContainer(Environment.CurrentDirectory + "/steamcmd/game/", gameExecutable, gameArgs, gameFolder);
                return await gameContainer.Run(m);
            });
            
            await CreateGuildCommand("stop-game", "Stop the running game instance", async (c, m) =>
            {
                if (gameContainer is { IsRunning: true })
                {
                    gameContainer.Stop();
                    await UpdateMessageContent(m, "Game force stopped.");
                    return true;
                }
                else
                {
                    await UpdateMessageContent(m, "No game running.");
                    return false;
                }
            });

            await CreateGuildCommand("log-history", "Create a performance history report", async (c, m) =>
            {
                try
                {
                    var data = await _dbContext.PerformanceReports.OrderBy(x => x.created).ToListAsync();

                    //create a csv with performance data
                    var writer = new StringWriter();
                    await writer.WriteLineAsync("date,fps,game,gpu,rt");
                    float lastFrameTime = 1000000;
                    foreach (var d in data)
                    {
                        if (d.averageFrametime < lastFrameTime)
                        {
                            await writer.WriteLineAsync(
                                $"{d.averageFrametime},{d.averageGameThreadTime},{d.averageGpuTime},{d.averageRenderThreadTime}");
                            lastFrameTime = d.averageFrametime;
                        }
                    }

                    await writer.FlushAsync();

                    //write to text
                    string output = writer.ToString();
                    await File.WriteAllTextAsync("report.csv", output);

                    //upload to discord
                    await SendFile("report.csv", "Created history report");

                    string pngPath = await GameContainer.GeneratePNGFromCSV("report.csv", "report.png", m);
                    await SendFile(pngPath, "Created graph");
                    return true;
                }
                catch (Exception e)
                {
                    await UpdateMessageContent(m, $"Failed to create history data: {e.Message}, {e.StackTrace}");
                    return false;
                }
                //clean up the files
                finally
                {
                    File.Delete("report.csv");
                    File.Delete("report.png");
                }
            });
            
            // await CreateGuildCommand("run-steam-command", "Run a command in SteamCMD", async (c, m) =>
            // {
            //     string? command = c.Data.Options.First(x => x.Name == "command").Value.ToString();
            //     if (string.IsNullOrWhiteSpace(command))
            //     {
            //         await UpdateMessageContent(m, "No command provided.");
            //         return false;
            //     }
            //     await steamCmdController.RunCommand(command);
            //     return true;
            // }, new SlashCommandOptionBuilder().AddOption("command", ApplicationCommandOptionType.String, "The command to run", true));
            
            await SendMessage("ProfileServer started and ready.");
        };
        
        client.InteractionCreated += (interaction) =>
        {
            if (interaction is SocketSlashCommand command)
            {
                if (commands.TryGetValue(command.Data.Name, out Func<SocketSlashCommand, ulong, Task<bool>>? action))
                {
                    _ = Task.Run(async () => await ExecuteCommand(command, action));
                }
            }

            return Task.CompletedTask;
        };
        
        client.ButtonExecuted += async (interaction) =>
        {
            if (interaction.Data.CustomId == "2fa_code")
            {
                //create a modal with a text entry field for the 2FA code
                ModalBuilder? modal = new ModalBuilder()
                    .WithTitle("Steam 2FA Code")
                    .WithCustomId("2fa_code")
                    .AddTextInput("Steam Guard Code", "2fa_code");
                
                await interaction.RespondWithModalAsync(modal.Build());
            }
        };
        
        client.ModalSubmitted += async (interaction) =>
        {
            if (interaction.Data.CustomId == "2fa_code")
            {
                twoFactorCodeResponse = interaction.Data.Components.First(x => x.CustomId == "2fa_code").Value;
            }

            await interaction.RespondAsync("Steam Guard code received. Submitting...");
        };
    }

    private async Task ExecuteCommand(SocketSlashCommand command, Func<SocketSlashCommand, ulong, Task<bool>> action)
    {
        RestInteractionMessage? message = null;
        try
        {
            string invokingUser = command.User.Username;
            Log.Information("Executing command: {command} invoked by {user}", command.Data.Name, invokingUser);
            
            //send a response to indicate the command is being executed
            await command.RespondAsync($">{command.Data.Name} - Executing command...\n");
            message = await command.GetOriginalResponseAsync();

            messageContent.Add(message.Id, message.Content);

            bool success = await action.Invoke(command, message.Id);
            
            await UpdateMessageContent(message.Id, $"{command.Data.Name} - Command execution {(success ? "successful" : "failed")}");
            Log.Information($"Command execution {(success ? "successful" : "failed")}");
        }
        catch (Exception e)
        {
            if (message == null)
            {
                //send the error message to discord
                await command.RespondAsync($"An error occurred while executing command `{command.Data.Name}`: " + e.Message);
            }
            else
            {
                await message.ModifyAsync(x =>
                    x.Content = $"{x.Content}\n>An error occurred while executing command `{command.Data.Name}`: " + e.Message);
            }
        }
    }

    private Process? UnrealInsightsProcess = null;
    
    private async void VerifyUnrealInsights()
    {
        if (UnrealInsightsProcess is { HasExited: false })
        {
            Log.Information("UnrealInsights already running.");
            return;
        }
        
        //check if a process is running with the name UnrealInsights.exe
        if (Process.GetProcessesByName("UnrealInsights").Length == 0)
        {
            Log.Information("UnrealInsights not running. Starting...");

            //check if the file exists
            if (!File.Exists(unrealInsightsPath))
            {
                throw new FileNotFoundException("UnrealInsights executable not found at: " + unrealInsightsPath);
            }

            //start the process
            ProcessStartInfo startInfo = new()
            {
                FileName = unrealInsightsPath
            };

            UnrealInsightsProcess = Process.Start(startInfo);

            if (UnrealInsightsProcess == null)
            {
                throw new Exception("Failed to start UnrealInsights process.");
            }
        }

        Log.Information("UnrealInsights is already running.");
            
        UnrealInsightsProcess = Process.GetProcessesByName("UnrealInsights").First();
    }

    private void VerifyGuild()
    {
        SocketGuild? g = client.GetGuild(guildId);
        SocketTextChannel? c = client.GetGuild(guildId)?.GetTextChannel(channelId);

        if (g == null)
        {
            Log.Error("[Discord] Guild not found: {guildId}", guildId);
        }
        else
        {
            guild = g;
        }

        if (c == null)
        {
            Log.Error("[Discord] Channel not found: {channelId}", channelId);
        }
        else
        {
            channel = c;
        }
    }

    private async Task CreateGuildCommand(string name, string description, Func<SocketSlashCommand, ulong, Task<bool>> action, SlashCommandOptionBuilder? options = null)
    {
        SlashCommandBuilder? command = new SlashCommandBuilder()
            .WithName(name)
            .WithDescription(description);

        if (options != null)
            command.Options.Add(options);
        
        try 
        {
            await guild.CreateApplicationCommandAsync(command.Build());
        }
        catch (Exception e)
        {
            string commandJson = JsonConvert.SerializeObject(command.Build(), Formatting.Indented);

            if (e is HttpException httpException)
            {
                string httpErrors = JsonConvert.SerializeObject(httpException.Errors, Formatting.Indented);
                Log.Error("[Discord] Error creating command: " + httpException.Message + "\nHttp Errors" + httpErrors + "\nCommand structure:" + commandJson);
                return;
            }
            
            Log.Error("[Discord] Error creating command: " + e.Message + "\nCommand structure:" + commandJson);
        }
        
        commands.Add(name, action);
    }
    
    //async action version
    private readonly Dictionary<string, Func<SocketSlashCommand, ulong, Task<bool>>> commands = new();

    public async Task SendMessage(string message)
    {
        try
        {
            await channel.SendMessageAsync(message);
        }
        catch (Exception e)
        {
            Log.Error("Failed to send message: " + e.Message);
        }
    }

    private string twoFactorCodeResponse = "";
    
    //send a message with an embed to fill in a 2FA code
    public async Task<string> Get2FACode()
    {
        twoFactorCodeResponse = "";
        
        try
        {
            var embed = new EmbedBuilder()
                .WithTitle("Steam 2FA Code")
                .WithDescription("Please enter the 2FA code for your Steam account.")
                .WithColor(Color.Blue)
                .Build();

            //add a button to display a modal with a text entry field for the 2FA code
            var button = new ButtonBuilder()
                .WithLabel("Enter 2FA Code")
                .WithStyle(ButtonStyle.Primary)
                .WithCustomId("2fa_code");
            
            await channel.SendMessageAsync($"Please enter the Steam Guard code for your Steam account.", embed: embed, components: new ComponentBuilder().WithButton(button).Build());
        }
        catch (Exception e)
        {
            Log.Error("Failed to send message: " + e.Message);
            return "";
        }

        while (string.IsNullOrWhiteSpace(twoFactorCodeResponse))
        {
            await Task.Delay(100);
        }
        
        return twoFactorCodeResponse;
    }

    private readonly SemaphoreSlim messageLock = new(1);
    private readonly Dictionary<ulong, string> messageContent = new();
    
    public async Task UpdateMessageContent(ulong statusMessage, string content)
    {
        await messageLock.WaitAsync();
        messageContent.TryGetValue(statusMessage, out string? currentContent);
        
        IMessage? message = await channel.GetMessageAsync(statusMessage);

        string newMessage = currentContent + "\n" + GetDiscordRelativeTimestamp(DateTime.UtcNow) + content;
        
        //filter message to up to 10 lines
        string[] lines = newMessage.Split('\n');
        if (lines.Length > 10)
        {
            newMessage = string.Join('\n', lines.Skip(lines.Length - 10));
        }
        
        //modify the message with the new content
        if (message is RestUserMessage restMessage)
        {
            await restMessage.ModifyAsync(x => x.Content = newMessage);
        }
        
        //update the dictionary
        messageContent[statusMessage] = newMessage;
        
        messageLock.Release();
    }

    private static string GetDiscordRelativeTimestamp(DateTime dateTime)
    {
        return $"<t:{(int) dateTime.Subtract(new DateTime(1970, 1, 1)).TotalSeconds}:T>";
    }
    
    public async Task SendFile(string path, string message)
    {
        try
        {
            await channel.SendFileAsync(path, message);
        }
        catch (Exception e)
        {
            Log.Error("Failed to send file: " + e.Message);
        }
    }
    
    public async Task SendPerformanceReportEmbed(float averageFrameTime, float percentile95, float percentile99,
        float maxFrameTime, float averageGameThreadTime, float averageRenderThreadTime, float averageGpuTime, string csvFile, string pngPath)
    {
        PerformanceReport? lastReport = await _dbContext.PerformanceReports
            .OrderByDescending(x => x.created)
            .FirstOrDefaultAsync();

        EmbedBuilder? embed;
        if (lastReport != null)
        {
            string FPS(float frametime) => $"{(1000.0f / frametime):F2} FPS";
            string Delta(float oldFrametime, float newFrametime)
            {
                float delta = (newFrametime - oldFrametime) / oldFrametime * 100.0f;
                return $"{(delta > 0 ? "+" : "")}{delta:F2}%";
            }
            string FPSDelta(float newFrametime, float oldFrametime)
            {
                float newFPS = 1000.0f / newFrametime;
                float oldFPS = 1000.0f / oldFrametime;
                float delta = (newFPS - oldFPS) / oldFPS * 100.0f;
                return $"{(delta > 0 ? "+" : "")}{delta:F2}%";
            }

            embed = new EmbedBuilder()
                .WithTitle("Performance Report")
                .WithDescription("A new performance report was just generated.")
                .AddField("Game Thread", $"{averageGameThreadTime:F2} ms\n({lastReport.averageGameThreadTime:F2} ms **{Delta(lastReport.averageGameThreadTime, averageGameThreadTime)}**)", true)
                .AddField("Render Thread", $"{averageRenderThreadTime:F2} ms\n({lastReport.averageRenderThreadTime:F2} ms **{Delta(lastReport.averageRenderThreadTime, averageRenderThreadTime)}**)", true)
                .AddField("GPU", $"{averageGpuTime:F2} ms\n({lastReport.averageGpuTime:F2} ms **{Delta(lastReport.averageGpuTime, averageGpuTime)}**)", true)

                .AddField($"Average Frame Time", $"{FPS(averageFrameTime)} {averageFrameTime:F2} ms\n" +
                                                 $"({FPS(lastReport.averageFrametime)} **{FPSDelta(averageFrameTime, lastReport.averageFrametime)}**)",
                    true)
                .AddField($"95th Percentile Frame Time", $"{FPS(percentile95)} {percentile95:F2} ms\n" +
                                                         $"({FPS(lastReport.percentile95)} **{FPSDelta(percentile95, lastReport.percentile95)}**)",
                    true)
                .AddField($"99th Percentile Frame Time", $"{FPS(percentile99)} {percentile99:F2} ms\n" +
                                                         $"({FPS(lastReport.percentile99)} **{FPSDelta(percentile99, lastReport.percentile99)}**)",
                    true)
                .AddField($"Worst Frame Time", $"{FPS(maxFrameTime)} {maxFrameTime:F2} ms\n" +
                                                 $"({FPS(lastReport.maxFrameTime)} **{FPSDelta(maxFrameTime, lastReport.maxFrameTime)}**)",
                    true)
                .AddField("Last report",
                    $"[{lastReport.created}](https://discord.com/channels/{channel.Guild.Id}/{channel.Id}/{lastReport.messageId})",
                    true)
                .WithColor(Color.Green);
        }
        else
        {
            embed = new EmbedBuilder()
                .WithTitle("Performance Report")
                .WithDescription("A new performance report was just generated.")
                .AddField($"Average Frame Time", $"{averageFrameTime:F2} ms ({(1000.0f / averageFrameTime):F2} FPS)",
                    true)
                .AddField($"95th Percentile Frame Time", $"{percentile95:F2} ms ({(1000.0f / percentile95):F2} FPS)",
                    true)
                .AddField($"99th Percentile Frame Time", $"{percentile99:F2} ms ({(1000.0f / percentile99):F2} FPS)",
                    true)
                .AddField($"Worst Frame Time", $"{maxFrameTime:F2} ms ({(1000.0f / maxFrameTime):F2})", true)
                .WithColor(Color.Green);
        }

        RestUserMessage? message;

        if (!string.IsNullOrWhiteSpace(pngPath))
        {
            embed = embed.WithImageUrl($"attachment://{pngPath}");
            message = await channel.SendFileAsync(pngPath, embed: embed.Build());
        }
        else
        {
            message = await channel.SendMessageAsync(embed: embed.Build());
        }

        //add to database
        PerformanceReport report = new()
        {
            created = DateTime.UtcNow,
            csvName = csvFile,
            averageFrametime = averageFrameTime,
            percentile95 = percentile95,
            percentile99 = percentile99,
            maxFrameTime = maxFrameTime,
            messageId = message.Id,
            averageGameThreadTime = averageGameThreadTime,
            averageRenderThreadTime = averageRenderThreadTime,
            averageGpuTime = averageGpuTime
        };
        
        _dbContext.PerformanceReports.Add(report);
        await _dbContext.SaveChangesAsync();
    }
}