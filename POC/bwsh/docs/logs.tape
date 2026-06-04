# bwsh logs — per-service logs + the --export support bundle.
# Render from POC/bwsh/:  vhs docs/logs.tape   (setup: docs/README.md)

Output docs/images/logs.gif

Require bwsh

Set Shell "bash"
Set FontSize 16
Set Width 1200
Set Height 800
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

Type "bwsh logs identity -d lite"
Sleep 700ms
Enter
Sleep 3s

Type "bwsh logs --export support-bundle.zip -d lite"
Sleep 700ms
Enter
Sleep 3s
