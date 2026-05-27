EQ2Emu QuestParser - Linux desktop launch

If Ubuntu Files says "Could not display eq2emu-questparser" or "There is no
app installed for executable files", the file manager is trying to open the
raw Linux executable as a document. Mark it executable and launch it from a
terminal:

  chmod +x eq2emu-questparser run-eq2emu-questparser.sh install-desktop-launcher.sh eq2emu-questparser.desktop
  ./eq2emu-questparser

You can also run:

  sh run-eq2emu-questparser.sh

To add EQ2Emu QuestParser to your desktop app launcher:

  sh install-desktop-launcher.sh

After installing, search for "EQ2Emu QuestParser" in your app launcher. If you
double-click eq2emu-questparser.desktop directly from Files, Ubuntu may still
ask you to right-click it and choose "Allow Launching".
