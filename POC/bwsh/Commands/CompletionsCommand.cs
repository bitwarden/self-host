using System.CommandLine;

namespace Bit.SelfHost.Commands;

public static class CompletionsCommand
{
    public static Command Build()
    {
        var cmd = new Command("completions", "Print a shell completion script (zsh or bash).");

        var shell = new Argument<string>("shell") { Description = "Shell to generate completions for (zsh, bash)." };
        shell.AcceptOnlyFromAmong("zsh", "bash");
        cmd.Arguments.Add(shell);

        cmd.SetAction(parseResult =>
        {
            Console.WriteLine(parseResult.GetValue(shell) == "bash" ? Bash : Zsh);
            return 0;
        });

        return cmd;
    }

    // Both scripts delegate back to bwsh's own `[suggest]` directive for suggestions, so they stay
    // correct as commands change and need no dotnet-suggest global tool.
    private const string Zsh = """
        #compdef bwsh
        # zsh completion for bwsh.
        #
        # Load in the current shell:
        #   source <(bwsh completions zsh)
        # Or install persistently:
        #   bwsh completions zsh > "${fpath[1]}/_bwsh"   # then restart zsh

        _bwsh() {
          local -a items
          # bwsh answers completion requests itself via the [suggest] directive.
          items=( ${(f)"$(command bwsh "[suggest:${CURSOR}]" "$BUFFER" 2>/dev/null)"} )
          if (( ${#items} )); then
            compadd -- $items
          else
            _files   # fall back to paths (e.g. --manifest, restore <archive>, --root)
          fi
        }

        compdef _bwsh bwsh
        """;

    private const string Bash = """
        # bash completion for bwsh.
        #
        # Load in the current shell:
        #   source <(bwsh completions bash)
        # Or install persistently (with bash-completion installed):
        #   bwsh completions bash > /usr/local/etc/bash_completion.d/bwsh

        _bwsh() {
          local IFS=$'\n'
          local cur="${COMP_WORDS[COMP_CWORD]}"
          # bwsh answers completion requests itself via the [suggest] directive.
          local items
          items="$(command bwsh "[suggest:${COMP_POINT}]" "${COMP_LINE}" 2>/dev/null)"
          COMPREPLY=( $(compgen -W "${items}" -- "${cur}") )
        }

        # -o default falls back to filenames when there are no matches (e.g. --manifest, restore).
        complete -o default -F _bwsh bwsh
        """;
}
