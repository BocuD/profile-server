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

//set up discord
DiscordBot discordBot = new();
await discordBot.Init();

//start the web server
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog();

var app = builder.Build();

app.Run();