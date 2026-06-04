# bwsh status — health table, versions, vault URL.
# Render from POC/bwsh/:  vhs docs/status.tape   (setup: docs/README.md)

Output docs/images/status.gif

Require bwsh

Set Shell "bash"
Set FontSize 16
Set Width 1000
Set Height 640
Set Padding 24
Set TypingSpeed 60ms
Set WindowBar Colorful
Set BorderRadius 8
Set Theme "Dracula"

# Clean prompt so the recording shows the tool, not the shell.
Hide
Type "export PS1='$ '"
Enter
Type "clear"
Enter
Show

Type "bwsh status"
Sleep 700ms
Enter
Sleep 5s
