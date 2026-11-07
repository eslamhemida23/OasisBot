# OasisBot Manager

A small external Windows application for managing multiple OasisBot instances.

## Features

- Save multiple bot entries with an executable path, profile, character, launch mode, and optional per-profile game login details.
- Start and stop a selected entry or all entries.
- Start opens the Silkroad game client automatically by default.
- Stop sends a private command to the matching OasisBot process, saves its settings, and closes its game client.
- Display process state, process ID, and uptime.
- Reconnect to managed processes after the Manager is closed and reopened.
- Keep each active instance on a separate OasisBot profile.
- Save Manager entries under %LOCALAPPDATA%\OasisBotManager\instances.json.

## Requirements

- Windows 10 or later.
- .NET 8 Desktop Runtime (x86), matching the OasisBot x86 build.

## Setup

1. Extract the bundle.
2. Back up your existing OasisBot installation's Build folder.
3. Copy **all files and folders inside BotUpdate** into your existing Build folder and allow files to be overwritten. Do not delete or replace your existing Build\Data or Build\User folders; they are intentionally omitted from BotUpdate.
4. Run OasisBot.Manager.exe from the Manager folder.
5. Choose Add instance.
6. Browse to Build\OasisBot.exe in your OasisBot installation.
7. Enter a unique profile name for each instance that will run at the same time.
8. Optionally enter the character name. Leave it blank to let OasisBot use its normal character selection.
9. Select the launch mode and save.
10. Select the entry and press Start, or use Start all.

When login details are provided, the Manager writes them to Build\\User\\<Profile>\\autologin.data using OasisBot's existing account-file format. Existing accounts in that profile are preserved; entering an already saved username updates that account instead of creating a duplicate. The selected Manager character is also added to that account's character list. The login password is not stored in instances.json.\n\nThe account file uses the same legacy Blowfish encoding as OasisBot itself; this is format compatibility, not modern password-grade encryption. Protect the Windows user account and the Build\\User folder accordingly.\n\nThe new account appears in OasisBot's General → Accounts list. To use automatic login, select that account and enable Automated Login in the bot's General settings; the Manager does not silently change the profile's auto-login preferences.

When the Manager creates or repairs a profile, OasisBot copies the shared game/server settings from the configured Default profile (or another configured profile if Default is not configured). Account passwords, username/login fields, and auto-login files are not copied.

If a bot exits unexpectedly, the Manager keeps its last PID and displays the process exit code. Select the instance and use Open log to read its diagnostic log under %LOCALAPPDATA%\OasisBotManager\Logs.

## Important

Each simultaneously running instance from the same installation must use a different profile. OasisBot stores the profile's main settings in User\<Profile>.rs and character settings under User\<Profile>\; running two processes on one profile can cause configuration conflicts.

Closing the Manager window does not close running bots. The Manager stores process IDs and checks that the process path and start time still match before reconnecting to them after a restart.

When an instance is started by the Manager, its normal close action saves the bot configuration and asks OasisBot to close its game-client process. If graceful close does not complete, the Manager offers a force-stop option that terminates the bot and its child processes.
