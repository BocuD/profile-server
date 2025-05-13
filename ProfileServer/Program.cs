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
    //split at =
    int index = line.IndexOf('=');
    if (index == -1) continue;
    string name = line.Substring(0, index).Trim();
    string arg = line.Substring(index + 1).Trim();

    Environment.SetEnvironmentVariable(name, arg);
}

//set up discord
DiscordBot discordBot = new();
await discordBot.Init();

//start the web server
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog();

var app = builder.Build();

app.Run();