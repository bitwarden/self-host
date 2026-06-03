using System.CommandLine;
using Bit.SelfHost.Commands;

var root = new RootCommand("bwsh — install and manage Bitwarden self-host deployments (standard & lite).");
root.Subcommands.Add(InstallCommand.Build());
root.Subcommands.Add(ConfigCommand.Build());
root.Subcommands.Add(UpdateCommand.Build());
root.Subcommands.Add(UninstallCommand.Build());
root.Subcommands.Add(StatusCommand.Build());
root.Subcommands.Add(LogsCommand.Build());
root.Subcommands.Add(MigrateCommand.Build());
root.Subcommands.Add(BackupCommand.Build());
root.Subcommands.Add(RestoreCommand.Build());

// ProcessTerminationTimeout (default ~2s) makes System.CommandLine cancel the action's token on
// Ctrl+C/SIGTERM; we catch the resulting cancellation below and exit cleanly.
var invocation = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
try
{
    return await root.Parse(args).InvokeAsync(invocation);
}
catch (OperationCanceledException)   // Ctrl+C
{
    Console.Error.WriteLine("\nCancelled.");
    return 130; // 128 + SIGINT
}
catch (NotSupportedException ex)   // e.g. Lite not yet implemented
{
    Console.Error.WriteLine($"Not supported: {ex.Message}");
    return 4; // precondition-failed
}
catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 2; // failure
}
catch (Exception ex)   // never dump a raw stack trace at the user
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
