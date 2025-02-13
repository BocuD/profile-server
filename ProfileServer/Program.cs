using System.Diagnostics;
using ProfileServer;
using Serilog;

//set up serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

Log.Information("Starting ProfileServer...");

//read .env
string path = Path.Combine(Environment.CurrentDirectory, ".env");

if (!File.Exists(path))
{
    Log.Error("No .env file found!");
    return;
}

foreach (string line in File.ReadAllLines(path))
{
    string[] parts = line.Split('=');
    if (parts.Length == 2)
    {
        Environment.SetEnvironmentVariable(parts[0], parts[1]);
    }
}

string? username = Environment.GetEnvironmentVariable("STEAMUSERNAME");
string? password = Environment.GetEnvironmentVariable("STEAMPASSWORD");

if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
{
    Log.Error("Steam username or password not set in environment!");
    return;
}

SteamCMDController steamCmdController = new(username, password);

string? gameId = Environment.GetEnvironmentVariable("STEAMGAMEID");
string? betaBranch = Environment.GetEnvironmentVariable("STEAMBETABRANCH");

if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(betaBranch))
{
    Log.Error("Steam game ID or beta branch not set in environment!");
    return;
}

//handle getting the latest game version and updating it
await steamCmdController.RunCommand("force_install_dir ./game");
await steamCmdController.RunCommand($"app_update {gameId} -beta {betaBranch} validate");
await steamCmdController.RunCommand("quit");

//start game process
Process gameProcess = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = Environment.CurrentDirectory + "/steamcmd/game/" + "Team1GardenProject.exe",
        WorkingDirectory = Environment.CurrentDirectory + "/steamcmd/game",
        UseShellExecute = false
    }
};

gameProcess.Start();

await gameProcess.WaitForExitAsync();

//start the web server
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog();

var app = builder.Build();

app.Run();