using System.Diagnostics;
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using Newtonsoft.Json;
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
    private GameContainer gameContainer;
    
    private readonly string gameId = Environment.GetEnvironmentVariable("STEAMGAMEID") ?? 
                                     throw new ArgumentNullException("STEAMGAMEID environment variable not set.");
    
    private readonly string betaBranch = Environment.GetEnvironmentVariable("STEAMBETABRANCH") ?? 
                                         throw new ArgumentNullException("STEAMBETABRANCH environment variable not set.");
    
    private readonly string gameExecutable = Environment.GetEnvironmentVariable("GAMEBINARY") ?? 
                                              throw new ArgumentNullException("GAMEBINARY environment variable not set.");
    
    private readonly string unrealInsightsPath = Environment.GetEnvironmentVariable("UNREALINSIGHTSPATH") ??
                                throw new ArgumentNullException("UNREALINSIGHTSPATH environment variable not set.");

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
            return;
        }

        //start steamcmd
        steamCmdController = new SteamCMDController(username, password);
    }

    private SocketGuild? guild;
    private SocketTextChannel? channel;
    
    public async Task Init()
    {
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
            await VerifyGuild();

            await VerifyUnrealInsights();
            
            await CreateGuildCommand("update-game", "Update the game to the latest version", async m =>
            {
                await steamCmdController.UpdateGame(gameId, betaBranch, m);
            });
            
            await CreateGuildCommand("start-game", "Start the game instance", async m =>
            {
                if (gameContainer is { isRunning: true })
                {
                    await UpdateMessageContent(m, "Game is already running.");
                }
                else
                {
                    gameContainer = new GameContainer(Environment.CurrentDirectory + "/steamcmd/game/", gameExecutable);
                    await gameContainer.Run(m);
                }
            });
        };
        
        client.InteractionCreated += async (interaction) =>
        {
            if (interaction is SocketSlashCommand command)
            {
                if (commands.TryGetValue(command.Data.Name, out Func<ulong, Task>? action))
                {
                    RestInteractionMessage message = null;
                    try
                    {
                        //send a response to indicate the command is being executed
                        await command.RespondAsync($">{command.Data.Name} - Executing command...\n");
                        message = await command.GetOriginalResponseAsync();

                        messageContent.Add(message.Id, message.Content);

                        await action.Invoke(message.Id);
                        
                        await UpdateMessageContent(message.Id, $"{command.Data.Name} - Command executed successfully.");
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
            }
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
    
    private async Task VerifyUnrealInsights()
    {
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
            
            Process.Start(startInfo);
        }
        else
        {
            Log.Information("UnrealInsights is already running.");
        }
    }

    private async Task VerifyGuild()
    {
        guild = client.GetGuild(guildId);
        channel = client.GetGuild(guildId)?.GetTextChannel(channelId);
        
        if (guild == null)
        {
            Log.Error("[Discord] Guild not found: {guildId}", guildId);
        }
        
        if (channel == null)
        {
            Log.Error("[Discord] Channel not found: {channelId}", channelId);
        }
    }
    
    private async Task CreateGuildCommand(string name, string description, Func<ulong, Task> action)
    {
        SlashCommandBuilder? command = new SlashCommandBuilder()
            .WithName(name)
            .WithDescription(description);
        
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
    private readonly Dictionary<string, Func<ulong, Task>> commands = new();

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

    SemaphoreSlim messageLock = new(1);
    Dictionary<ulong, string> messageContent = new();
    
    public async Task UpdateMessageContent(ulong statusMessage, string content)
    {
        await messageLock.WaitAsync();
        messageContent.TryGetValue(statusMessage, out string? currentContent);
        
        var message = await channel.GetMessageAsync(statusMessage);
        
        //modify the message with the new content
        if (message is RestUserMessage restMessage)
        {
            await restMessage.ModifyAsync(x => x.Content = currentContent + "\n" + content);
        }
        
        //update the dictionary
        messageContent[statusMessage] = currentContent + "\n" + content;
        
        messageLock.Release();
    }
}