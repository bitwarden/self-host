using Bit.SelfHost.Deployments;
using Spectre.Console;

namespace Bit.SelfHost.Commands;

/// <summary>
/// Spectre.Console-backed interactive prompts. Centralizes AnsiConsole usage (and markup
/// escaping of untrusted text) rather than scattering it, mirroring the UiManager pattern in
/// bitwarden/test's Bitwarden.DBMigrationTool.
/// </summary>
public static class Prompts
{
    public static DeploymentKind SelectDeployment()
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Which [green]deployment[/] do you want to install?")
                .AddChoices("standard", "lite")
                .UseConverter(c => c switch
                {
                    "standard" => "standard  (multi-container)",
                    "lite" => "lite  (single container)",
                    _ => c,
                }));
        return DeploymentFactory.Parse(choice);
    }

    public static InstallManifest Collect(IDeployment deployment)
    {
        var manifest = new InstallManifest { Deployment = deployment.Kind.ToString().ToLowerInvariant() };
        AnsiConsole.MarkupLine($"Interactive install ([green]{manifest.Deployment}[/] deployment).\n");

        foreach (var p in deployment.InstallPrompts)
        {
            var prompt = new TextPrompt<string>($"{Markup.Escape(p.Question)}:").AllowEmpty();
            if (p.Secret) prompt.Secret();
            if (p.Default is not null) prompt.DefaultValue(p.Default);
            if (p.Validate is { } validate)
            {
                // AllowEmpty lets the empty case reach the validator for a clear message (otherwise
                // Spectre re-prompts with a generic one). The validator then rejects empty/invalid.
                prompt.Validate(v => validate(v) is { } error
                    ? ValidationResult.Error(Markup.Escape(error))
                    : ValidationResult.Success());
            }

            Cli.ApplyManifestValue(manifest, p.Key, AnsiConsole.Prompt(prompt));
        }

        AnsiConsole.WriteLine();
        return manifest;
    }
}
