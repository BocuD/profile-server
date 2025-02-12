using ProfileServer;
using Serilog;

//set up serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

//read .env
string path = Path.Combine(Environment.CurrentDirectory, ".env");

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

SteamCMD steamCmd = new(username, password);

if (!steamCmd.installed)
{
    await steamCmd.RunInitialInstallation();
}

string gameId = "3365820";
string betaBranch = "jenkins";

await steamCmd.RunCommand($"app_update {gameId} -beta {betaBranch}");

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();