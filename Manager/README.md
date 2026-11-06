# OasisBot Manager

A small external Windows application for managing multiple OasisBot instances.

## Features

- Save multiple bot entries with an executable path, profile, character, and launch mode.
- Start and stop a selected entry or all entries.
- Display process state, process ID, and uptime.
- Reconnect to managed processes after the Manager is closed and reopened.
- Keep each active instance on a separate OasisBot profile.
- Save Manager entries under %LOCALAPPDATA%\OasisBotManager\instances.json.

## Requirements

- Windows 10 or later.
- .NET 8 Desktop Runtime (x86), matching the OasisBot x86 build.

## Setup

1. Extract the Manager files to a folder of your choice.
2. Replace the OasisBot.exe in your existing Build folder with the version in the BotUpdate folder included in the release bundle.
3. Run OasisBot.Manager.exe.
4. Choose Add instance.
5. Browse to Build\OasisBot.exe in your OasisBot installation.
6. Enter a unique profile name for each instance that will run at the same time.
7. Optionally enter the character name. Leave it blank to let OasisBot use its normal character selection.
8. Select the launch mode and save.
9. Select the entry and press Start, or use Start all.

The Manager does not store game passwords. It only stores the bot executable, profile, character name, and launch mode.

## Important

Each simultaneously running instance from the same installation must use a different profile. OasisBot stores the profile's main settings in User\<Profile>.rs and character settings under User\<Profile>\; running two processes on one profile can cause configuration conflicts.

Closing the Manager window does not close running bots. The Manager stores process IDs and checks that the process path and start time still match before reconnecting to them after a restart.

When an instance is started by the Manager, its normal close action saves the bot configuration and asks OasisBot to close its game-client process. If graceful close does not complete, the Manager offers a force-stop option that terminates the bot and its child processes.
