# bwsh install — live progress table marching to healthy.
# Render from POC/bwsh/:  vhs docs/install.tape   (setup: docs/README.md)
# Pre-pull the images first so the GIF shows orchestration, not downloads.

Output docs/images/install.gif

Require bwsh

Set Shell "bash"
Set FontSize 16
Set Width 1000
Set Height 460
Set Padding 24
Set TypingSpeed 60ms
Set WindowBar Colorful
Set BorderRadius 8
Set Theme "Dracula"

Hide
Type "export PS1='$ '"
Enter
Type "clear"
Enter
Show

Type "bwsh install --manifest bitwarden.yaml"
Sleep 700ms
Enter
Sleep 90s   # pulling -> creating -> starting -> healthy, then the vault URL
