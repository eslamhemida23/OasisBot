using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using RSBot.Chat;
using RSBot.Core;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Cos;
using RSBot.Core.Objects.Spawn;
using RSBot.Core.Plugins;

namespace RSBot.XControl
{
    /// <summary>
    /// Native C# party-control plugin for OasisBot.
    /// Chat event signature: (byte type, string message, string sender).
    /// </summary>
    public sealed class XControlPlugin : IPlugin
    {
        public string InternalName => "RSBot.XControl";

        public void Initialize()
        {
            XControlService.Initialize();
            EventManager.SubscribeEvent(
                "OnChatMessage",
                new System.Action<byte, string, string>(XControlService.OnChatMessage));
            EventManager.SubscribeEvent("OnTick", new System.Action(XControlService.OnTick));
            EventManager.SubscribeEvent("OnEnterGame", new System.Action(XControlService.LoadLeaders));
            Log.Notify("[xControl] Native C# plugin initialized.");
        }

        public void OnLoadCharacter()
        {
            XControlService.LoadLeaders();
        }
    }

    /// <summary>
    /// This view is discovered by PluginManager through IPluginView.
    /// </summary>
    public sealed class XControlView : UserControl, IPluginView
    {
        private readonly TextBox _leaderInput;
        private readonly Label _status;

        public string InternalName => "RSBot.XControl";
        public string DisplayName => "xControl";
        public bool DisplayAsTab => true;
        public int Index => 85;
        public bool RequireIngame => true;
        public Control View => this;

        public XControlView()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.WhiteSmoke;
            Padding = new Padding(12);

            var prompt = new Label
            {
                Text = "Leader character name:",
                AutoSize = true,
                Location = new Point(12, 16)
            };

            _leaderInput = new TextBox
            {
                Location = new Point(12, 42),
                Size = new Size(220, 25),
                PlaceholderText = "Enter leader name"
            };

            var add = new Button
            {
                Text = "Add Leader",
                Location = new Point(242, 40),
                Size = new Size(105, 28)
            };

            _status = new Label
            {
                AutoSize = true,
                Location = new Point(12, 82),
                ForeColor = Color.DimGray,
                Text = "Add the character allowed to send commands in Party chat."
            };

            add.Click += (_, _) => XControlService.AddLeader(_leaderInput.Text);

            _leaderInput.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;

                e.SuppressKeyPress = true;
                XControlService.AddLeader(_leaderInput.Text);
            };

            Controls.Add(prompt);
            Controls.Add(_leaderInput);
            Controls.Add(add);
            Controls.Add(_status);

            XControlService.StatusChanged += SetStatus;
        }

        public void Translate() { }

        private void SetStatus(string message)
        {
            if (IsDisposed)
                return;

            if (InvokeRequired)
            {
                try { BeginInvoke(new System.Action<string>(SetStatus), message); } catch { }
                return;
            }

            _status.Text = message;

            // Restore the saved leader name when the active character profile is loaded.
            if (message.StartsWith("Loaded ", StringComparison.OrdinalIgnoreCase))
                _leaderInput.Text = XControlService.GetLeaders().FirstOrDefault() ?? string.Empty;
        }
    }

    internal static class XControlService
    {
        private const string ConfigKey = "RSBot.XControl.Leaders";
        private static readonly object Sync = new();
        private static readonly HashSet<string> Leaders =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool _initialized;
        private static bool _followEnabled;
        private static string _followPlayer = string.Empty;
        private static double _followDistance = 10;
        private static int _lastFollowTick;
        private static int _lastTick;
        private static Position _lastFollowDestination;
        private static bool _hasLastFollowDestination;
        private static bool _followExactPosition;
        private static PendingTeleport _pendingTeleport;

        public static event System.Action<string> StatusChanged;

        private sealed class PendingTeleport
        {
            public uint DestinationId;
            public string SourceCodeName;
            public Position StartPosition;
            public int StartedAt;
            public bool ServerAccepted;
        }

        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;
            LoadLeaders();
        }

        public static string[] GetLeaders()
        {
            lock (Sync)
                return Leaders.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static bool AddLeader(string value)
        {
            var name = NormalizePlayerName(value);
            if (string.IsNullOrWhiteSpace(name))
            {
                SetStatus("Enter a leader character name.");
                return false;
            }

            if (Game.Player == null)
            {
                SetStatus("Enter the game before saving a leader name.");
                return false;
            }

            bool added;
            lock (Sync)
                added = Leaders.Add(name);

            if (!added)
            {
                SetStatus("Leader already added: " + name);
                return false;
            }

            if (!SaveLeaders())
            {
                SetStatus("Leader added for this session, but saving failed. Check the bot profile and try again.");
                return false;
            }

            SetStatus("Leader saved: " + name);
            return true;
        }

        public static void LoadLeaders()
        {
            try
            {
                var stored = PlayerConfig.GetArray<string>(ConfigKey, ';') ?? Array.Empty<string>();
                lock (Sync)
                {
                    Leaders.Clear();
                    foreach (var item in stored)
                    {
                        var normalized = NormalizePlayerName(item);
                        if (!string.IsNullOrWhiteSpace(normalized))
                            Leaders.Add(normalized);
                    }
                }

                SetStatus("Loaded " + Leaders.Count + " trusted leader(s).");
            }
            catch (Exception ex)
            {
                Log.Warn("[xControl] Could not load leaders: " + ex.Message);
            }
        }

        private static bool SaveLeaders()
        {
            try
            {
                var values = GetLeaders();
                PlayerConfig.SetArray(ConfigKey, values, ";");
                PlayerConfig.Save();

                // PlayerConfig silently ignores writes when its character profile is not loaded.
                // Read the value back so the UI never reports a save that did not happen.
                var persisted = PlayerConfig.GetArray<string>(ConfigKey, ';');
                return values.SequenceEqual(persisted, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Warn("[xControl] Could not save leaders: " + ex.Message);
                return false;
            }
        }

        private static void SetStatus(string message)
        {
            StatusChanged?.Invoke(message);
        }

        private static string NormalizePlayerName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var result = value.Trim();
            while (result.Length > 2)
            {
                char closing;
                if (result[0] == '[') closing = ']';
                else if (result[0] == '(') closing = ')';
                else if (result[0] == '<') closing = '>';
                else break;

                var end = result.IndexOf(closing);
                if (end <= 0)
                    break;

                result = result.Substring(end + 1).Trim();
            }

            var colon = result.IndexOf(':');
            if (colon > 0)
                result = result.Substring(0, colon).Trim();

            return result;
        }

        private static bool IsLeader(string sender)
        {
            var name = NormalizePlayerName(sender);
            lock (Sync)
                return name.Length > 0 && Leaders.Contains(name);
        }

        public static void OnChatMessage(byte type, string message, string sender)
        {
            if (!Game.Ready || Game.Player == null || string.IsNullOrWhiteSpace(message))
                return;

            // Commands are accepted from Party chat only.
            if (type != (byte)ChatType.Party)
                return;

            // The server echoes the leader's own Party message back to the leader.
            // Allow that local echo so commands such as START also execute on the leader.
            var isLocalSender = string.Equals(sender, Game.Player.Name, StringComparison.OrdinalIgnoreCase);
            if (!isLocalSender && !IsLeader(sender))
                return;

            try
            {
                HandleCommand(sender, message.Trim());
            }
            catch (Exception ex)
            {
                Log.Warn("[xControl] Command failed: " + ex.Message);
            }
        }

        private static void HandleCommand(string sender, string message)
        {
            _lastCommandSender = sender;
            if (string.IsNullOrWhiteSpace(message))
                return;

            var split = message.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var command = split[0].ToUpperInvariant();
            var args = split.Length > 1 ? split[1].Trim() : string.Empty;

            // Command keywords are case-insensitive; player and item names keep their original case.

            if (command == "XCTP")
            {
                HandleTeleportSync(sender, args);
                return;
            }

            switch (command)
            {
                case "S":
                case "START":
                    Kernel.Bot?.Start();
                    Log.Notify("[xControl] Bot start requested by " + sender);
                    break;

                case "SS":
                case "STOP":
                    Kernel.Bot?.Stop();
                    Log.Notify("[xControl] Bot stop requested by " + sender);
                    break;

                case "T":
                case "TRACE":
                    StartTrace(string.IsNullOrWhiteSpace(args) ? sender : args.TrimStart('#'));
                    break;

                case "N":
                case "NOTRACE":
                    StopTrace();
                    break;

                case "F":
                case "FOLLOW":
                    StartFollow(sender, args);
                    break;

                case "NF":
                case "NOFOLLOW":
                    StopFollow();
                    break;

                case "RE":
                case "RETURN":
                    if (IsConnected && Game.Player.State.LifeState == LifeState.Dead)
                    {
                        var revivePacket = new Packet(0x3053);
                        revivePacket.WriteByte(0x01);
                        PacketManager.SendPacket(revivePacket, PacketDestination.Server);
                        Log.Notify("[xControl] Resurrection at town requested.");
                    }
                    else
                    {
                        RunPlayerAction("return scroll", () => Game.Player.UseReturnScroll());
                    }
                    break;

                case "SIT":
                    ToggleSit();
                    break;

                case "Z":
                case "ZERK":
                    RunPlayerAction("berserk", () =>
                    {
                        if (!Game.Player.CanEnterBerzerk)
                            return false;
                        Game.Player.EnterBerzerkMode();
                        return true;
                    });
                    break;

                case "G":
                case "GETOUT":
                    if (Game.Party?.IsInParty == true)
                    {
                        Game.Party.Leave();
                        Log.Notify("[xControl] Leaving party by " + sender);
                    }
                    else Log.Warn("[xControl] Not in a party.");
                    break;

                case "STATUS":
                    ReportStatus();
                    break;

                case "HP":
                    RunPlayerAction("HP potion", () => Game.Player.UseHealthPotion());
                    break;

                case "MP":
                    RunPlayerAction("MP potion", () => Game.Player.UseManaPotion());
                    break;

                case "INV":
                    ReportInventory();
                    break;

                case "USE":
                    UseItem(args);
                    break;

                case "M":
                case "MOUNT":
                    TryMount(string.IsNullOrWhiteSpace(args) ? "transport" : args);
                    break;

                case "D":
                case "DS":
                case "DISMOUNT":
                    TryDismount(string.IsNullOrWhiteSpace(args) ? "transport" : args);
                    break;

                case "K":
                case "KILL":
                case "TERM":
                case "TERMINATE":
                    TerminateTransportPets();
                    break;

                case "MOVEON":
                    RandomMovement(args);
                    break;

                case "GETPOS":
                    ReportPosition();
                    break;

                case "SETPOS":
                    SetTrainingPosition(args);
                    break;

                case "SETAREA":
                    SetTrainingAreaByName(args);
                    break;

                case "SETRADIUS":
                    SetTrainingRadius(args);
                    break;

                case "LQ":
                    TryTeleportNamed("Kings Valley,Pharaoh tomb (beginner)");
                    break;

                case "Q1":
                    TryTeleportByIndex(1);
                    break;

                case "Q2":
                    TryTeleportByIndex(2);
                    break;

                case "Q3":
                    TryTeleportByIndex(3);
                    break;

                case "TP":
                    TryTeleportNamed(args);
                    break;

                case "CHAT":
                    SendChatCommand(args);
                    break;

                case "DC":
                case "DIS":
                    Kernel.Bot?.Stop();
                    Kernel.Proxy?.Shutdown();
                    Log.Notify("[xControl] Disconnect requested by " + sender);
                    break;

                default:
                    Log.Debug("[xControl] Unknown command: " + command);
                    break;
            }
        }

        private static bool IsConnected =>
            Game.Ready && Game.Player != null &&
            Kernel.Proxy?.IsConnectedToAgentserver == true;

        private static void RunPlayerAction(string label, Func<bool> action)
        {
            if (!IsConnected)
            {
                Log.Warn("[xControl] Cannot execute " + label + ": not connected.");
                return;
            }

            var success = action();
            Log.Notify("[xControl] " + label + (success ? " requested." : " failed or unavailable."));
        }

        private static void ReportStatus()
        {
            if (Game.Player == null)
                return;

            var p = Game.Player;
            var hp = p.MaximumHealth > 0 ? p.Health * 100 / p.MaximumHealth : 0;
            var mp = p.MaximumMana > 0 ? p.Mana * 100 / p.MaximumMana : 0;
            var party = Game.Party?.IsInParty == true ? "in party" : "solo";
            ChatManager.Send(ChatType.Private,
                $"STATUS {p.Name} Lv.{p.Level} HP {hp}% MP {mp}% {party}",
                GetCurrentCommandLeader());
        }

        private static string GetCurrentCommandLeader()
        {
            // Private responses are only used for direct status queries; party command responses
            // are safer when the current sender is not retained by the event callback.
            return _lastCommandSender ?? string.Empty;
        }

        private static string _lastCommandSender;

        private static void ReportInventory()
        {
            if (Game.Player == null)
                return;

            var inventory = Game.Player.Inventory;
            var items = inventory.GetNormalPartItems()
                .Where(item => item != null)
                .Take(20)
                .Select(item => $"{item.Slot}:{item.Record?.GetRealName() ?? item.Record?.CodeName} x{item.Amount}")
                .ToArray();

            var summary = $"INV free={inventory.FreeSlots}/{inventory.NormalPartSize}; " +
                          (items.Length == 0 ? "empty" : string.Join(" | ", items));
            SendPrivateToLeader(summary);
        }

        private static void ReportPosition()
        {
            if (Game.Player == null)
                return;

            var pos = Game.Player.Position;
            SendPrivateToLeader($"POS {Game.Player.Name} X={pos.X:F1} Y={pos.Y:F1} Region={pos.Region.Id}");
        }

        private static void UseItem(string search)
        {
            if (!IsConnected || string.IsNullOrWhiteSpace(search))
                return;

            var item = Game.Player.Inventory.FirstOrDefault(candidate =>
            {
                var code = candidate?.Record?.CodeName ?? string.Empty;
                var name = candidate?.Record?.GetRealName() ?? string.Empty;
                return code.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            });

            if (item == null)
            {
                Log.Warn("[xControl] Item not found: " + search);
                return;
            }

            Log.Notify("[xControl] Use item " + (item.Record?.GetRealName() ?? item.Record?.CodeName) +
                       ": " + (item.Use() ? "requested" : "failed"));
        }

        private static void StartTrace(string playerName)
        {
            if (!IsConnected || string.IsNullOrWhiteSpace(playerName))
                return;

            if (string.Equals(playerName, Game.Player.Name, StringComparison.OrdinalIgnoreCase))
            {
                Log.Notify("[xControl] Leader does not need to follow itself.");
                return;
            }

            var partyTarget = Game.Party?.GetMemberByName(playerName);
            if (partyTarget == null &&
                !SpawnManager.TryGetEntity<SpawnedPlayer>(
                    p => string.Equals(p.Name, playerName, StringComparison.OrdinalIgnoreCase),
                    out _))
            {
                Log.Warn("[xControl] Trace target not found: " + playerName);
                return;
            }

            _followPlayer = partyTarget?.Name ?? playerName;
            _followDistance = 0;
            _followExactPosition = true;
            _followEnabled = true;
            _lastFollowTick = 0;
            _hasLastFollowDestination = false;
            Log.Notify("[xControl] Exact-position follow started: " + _followPlayer);
        }

        private static void StopTrace()
        {
            StopFollow();
        }

        private static void StartFollow(string sender, string args)
        {
            var tokens = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var target = tokens.Length > 0 ? tokens[0].TrimStart('#') : sender;
            var distance = 10d;

            if (tokens.Length > 1 &&
                double.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                distance = Math.Clamp(parsed, 3d, 50d);

            if (string.IsNullOrWhiteSpace(target))
                return;

            _followPlayer = target;
            _followDistance = distance;
            _followExactPosition = false;
            _followEnabled = true;
            _lastFollowTick = 0;
            _hasLastFollowDestination = false;
            Log.Notify($"[xControl] Following {target} at {distance:0.#}m.");
        }

        private static void StopFollow()
        {
            _followEnabled = false;
            _followExactPosition = false;
            _followPlayer = string.Empty;
            _hasLastFollowDestination = false;
            Log.Notify("[xControl] Follow stopped.");
        }

        public static void OnTick()
        {
            if (!Game.Ready || Game.Player == null)
                return;

            var now = Environment.TickCount;
            if (now - _lastTick < 500)
                return;
            _lastTick = now;

            UpdateFollow();
            UpdatePendingTeleport();
        }

        private static bool TryGetFollowTargetPosition(out Position position)
        {
            position = default;

            // Party member positions keep updating even after the leader leaves the local
            // spawn/visibility range. SpawnManager is retained as a fallback for non-party targets.
            var partyMember = Game.Party?.GetMemberByName(_followPlayer);
            if (partyMember != null && partyMember.Position.Region.Id != 0)
            {
                position = partyMember.Position;
                return true;
            }

            if (SpawnManager.TryGetEntity<SpawnedPlayer>(
                p => string.Equals(p.Name, _followPlayer, StringComparison.OrdinalIgnoreCase),
                out var target))
            {
                position = target.Position;
                return true;
            }

            return false;
        }

        private static void UpdateFollow()
        {
            if (!_followEnabled || !IsConnected || !TryGetFollowTargetPosition(out var leader))
                return;

            var me = Game.Player.Position;

            // A follower can cross normal map sector boundaries while chasing. Do not stop
            // just because the sector IDs differ. Dungeon coordinates are local to one region.
            if (me.Region.IsDungeon != leader.Region.IsDungeon ||
                (me.Region.IsDungeon && me.Region.Id != leader.Region.Id))
                return;

            var dx = leader.X - me.X;
            var dy = leader.Y - me.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);

            Position destination;
            if (_followExactPosition)
            {
                if (distance <= 0.25)
                {
                    _hasLastFollowDestination = false;
                    return;
                }

                destination = leader;
            }
            else
            {
                if (distance <= _followDistance + 1.5 || distance < 0.01)
                    return;

                var ratio = _followDistance / distance;
                var destinationX = leader.X - dx * ratio;
                var destinationY = leader.Y - dy * ratio;
                destination = new Position(destinationX, destinationY, leader.Region)
                {
                    ZOffset = leader.ZOffset
                };
            }

            // The game's Player.MoveTo waits for a 0xB021 acknowledgement. During follow this
            // can hold the only movement worker for seconds if an acknowledgement is missed.
            // Use the exact same movement packet without a blocking callback instead.
            var distanceToDestination = me.DistanceTo(destination);
            if (distanceToDestination <= 0.25)
                return;

            // Player.MoveTo rejects destinations more than 150m away. Follow in short legs
            // so a character can catch up even after crossing several sectors.
            if (distanceToDestination > 120)
            {
                var legRatio = 100d / distanceToDestination;
                var waypointX = me.X + (destination.X - me.X) * legRatio;
                var waypointY = me.Y + (destination.Y - me.Y) * legRatio;
                var waypointZ = me.ZOffset + (destination.ZOffset - me.ZOffset) * (float)legRatio;
                destination = new Position((float)waypointX, (float)waypointY, destination.Region)
                {
                    ZOffset = waypointZ
                };
            }

            var now = Environment.TickCount;
            var destinationChanged = !_hasLastFollowDestination ||
                                     _lastFollowDestination.Region.Id != destination.Region.Id ||
                                     _lastFollowDestination.DistanceTo(destination) >= 2.0;
            var retryDue = now - _lastFollowTick >= 2500;

            // Retarget when the leader has moved, otherwise let the current move finish.
            // If no movement acknowledgement/state update arrives, retry after 2.5 seconds.
            if (now - _lastFollowTick < 750 ||
                (!destinationChanged && (!retryDue || Game.Player.Movement.Moving)))
                return;

            if (SendFollowMovement(destination))
            {
                _lastFollowDestination = destination;
                _hasLastFollowDestination = true;
                _lastFollowTick = now;
            }
        }

        private static bool SendFollowMovement(Position destination)
        {
            if (!IsConnected)
                return false;

            Packet packet;
            if (Game.Player.Vehicle != null)
            {
                // Same packet format used by Cos.MoveTo, but without blocking for 0xB021.
                packet = new Packet(0x70C5);
                packet.WriteUInt(Game.Player.Vehicle.UniqueId);
                packet.WriteByte((byte)CosCommand.Move);
                packet.WriteByte(1);
                packet.WriteUShort(destination.Region.Id);
            }
            else
            {
                packet = new Packet(0x7021);
                packet.WriteByte(1);

                // Player.MoveTo uses the current dungeon region; regular-world destinations
                // carry their calculated destination region.
                packet.WriteUShort(Game.Player.IsInDungeon
                    ? Game.Player.Position.Region.Id
                    : destination.Region.Id);
            }

            if (!destination.Region.IsDungeon)
            {
                packet.WriteShort(destination.XOffset);
                packet.WriteShort(destination.ZOffset);
                packet.WriteShort(destination.YOffset);
            }
            else
            {
                packet.WriteInt(destination.XOffset);
                packet.WriteInt(destination.ZOffset);
                packet.WriteInt(destination.YOffset);
            }

            PacketManager.SendPacket(packet, PacketDestination.Server);
            return true;
        }

        private static void RandomMovement(string args)
        {
            if (!IsConnected)
                return;

            var radius = 10;
            if (!string.IsNullOrWhiteSpace(args) &&
                int.TryParse(args, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                radius = Math.Clamp(parsed, 1, 100);

            var random = Random.Shared;
            var current = Game.Player.Position;
            var angle = random.NextDouble() * Math.PI * 2;
            var distance = Math.Sqrt(random.NextDouble()) * radius;
            var x = current.X + (float)(Math.Cos(angle) * distance);
            var y = current.Y + (float)(Math.Sin(angle) * distance);

            var destination = new Position(x, y, current.Region);
            Log.Notify("[xControl] Move request: " + Game.Player.MoveTo(destination, false));
        }

        private static void SetTrainingPosition(string args)
        {
            if (!Game.Ready || Game.Player == null)
                return;

            var tokens = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Position position;

            if (tokens.Length >= 2 &&
                float.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                position = new Position(x, y, Game.Player.Position.Region);
            else if (tokens.Length == 0)
                position = Game.Player.Position;
            else
            {
                Log.Warn("[xControl] Usage: SETPOS [X Y]");
                return;
            }

            try
            {
                PlayerConfig.Set("RSBot.Area.Region", position.Region);
                PlayerConfig.Set("RSBot.Area.X", position.XOffset);
                PlayerConfig.Set("RSBot.Area.Y", position.YOffset);
                PlayerConfig.Set("RSBot.Area.Z", position.ZOffset);
                EventManager.FireEvent("OnSetTrainingArea");
                Log.Notify("[xControl] Training position set to " + position);
            }
            catch (Exception ex)
            {
                Log.Warn("[xControl] SETPOS failed: " + ex.Message);
            }
        }

        private static void SetTrainingAreaByName(string areaName)
        {
            if (!Game.Ready || Game.Player == null || string.IsNullOrWhiteSpace(areaName))
                return;

            try
            {
                var areas = PlayerConfig.GetArray<string>("RSBot.Training.Areas");
                foreach (var rawArea in areas)
                {
                    var split = rawArea.Split("|", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (!Area.TryParse(split, out var area) ||
                        !string.Equals(area.Name, areaName.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;

                    PlayerConfig.Set("RSBot.Area.Region", area.Position.Region);
                    PlayerConfig.Set("RSBot.Area.X", area.Position.XOffset);
                    PlayerConfig.Set("RSBot.Area.Y", area.Position.YOffset);
                    PlayerConfig.Set("RSBot.Area.Z", area.Position.ZOffset);
                    PlayerConfig.Set("RSBot.Area.Radius", area.Radius);
                    EventManager.FireEvent("OnSetTrainingArea");
                    Log.Notify("[xControl] Training area selected: " + area.Name);
                    return;
                }

                Log.Warn("[xControl] Saved training area not found: " + areaName);
            }
            catch (Exception ex)
            {
                Log.Warn("[xControl] SETAREA failed: " + ex.Message);
            }
        }

        private static void SetTrainingRadius(string args)
        {
            if (!Game.Ready || !int.TryParse(args, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var radius))
            {
                Log.Warn("[xControl] Usage: SETRADIUS [5..100]");
                return;
            }

            radius = Math.Clamp(radius, 5, 100);
            PlayerConfig.Set("RSBot.Area.Radius", radius);
            EventManager.FireEvent("OnSetTrainingArea");
            Log.Notify("[xControl] Training radius set to " + radius);
        }

        private static void SendChatCommand(string args)
        {
            var parts = args.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return;

            var channel = parts[0].ToLowerInvariant();
            var message = parts[1];

            switch (channel)
            {
                case "all": ChatManager.Send(ChatType.All, message); break;
                case "party": ChatManager.Send(ChatType.Party, message); break;
                case "guild": ChatManager.Send(ChatType.Guild, message); break;
                case "union": ChatManager.Send(ChatType.Union, message); break;
                case "global": ChatManager.Send(ChatType.Global, message); break;
                case "stall": ChatManager.Send(ChatType.Stall, message); break;
                case "academy": ChatManager.Send(ChatType.Academy, message); break;
                case "private":
                {
                    var whisper = message.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (whisper.Length == 2)
                        ChatManager.Send(ChatType.Private, whisper[1], whisper[0]);
                    break;
                }
                default:
                    Log.Warn("[xControl] Unsupported chat channel: " + channel);
                    break;
            }
        }

        private static void SendPartyOrLeader(string message)
        {
            if (Game.Party?.IsInParty == true)
                ChatManager.Send(ChatType.Party, message);
            else if (!string.IsNullOrWhiteSpace(_lastCommandSender))
                ChatManager.Send(ChatType.Private, message, _lastCommandSender);
            else
                Log.Notify("[xControl] " + message);
        }

        private static void SendPrivateToLeader(string message)
        {
            if (string.IsNullOrWhiteSpace(_lastCommandSender))
            {
                Log.Notify("[xControl] " + message);
                return;
            }

            ChatManager.Send(ChatType.Private, message, _lastCommandSender);
        }

        private static void ToggleSit()
        {
            if (!IsConnected)
                return;

            var packet = new Packet(0x704F);
            packet.WriteByte(0x04);
            PacketManager.SendPacket(packet, PacketDestination.Server);
            Log.Notify("[xControl] Sit/stand request sent.");
        }

        private static bool SendMountPacket(uint uniqueId, byte flag)
        {
            if (!IsConnected || uniqueId == 0)
                return false;

            var packet = new Packet(0x70CB);
            packet.WriteByte(flag);
            packet.WriteUInt(uniqueId);
            PacketManager.SendPacket(packet, PacketDestination.Server);
            return true;
        }

        private static void TryMount(string petType)
        {
            if (!IsConnected)
                return;

            var type = (petType ?? "transport").Trim().ToLowerInvariant();
            var player = Game.Player;

            if (type == "pick" || type == "pickup" || type == "grab" || type == "ability")
            {
                Log.Warn("[xControl] Ability/pick pets cannot be mounted.");
                return;
            }

            bool success = false;

            if (type == "horse" || type == "mount" || type == "ride")
            {
                if (player.Vehicle != null)
                    success = SendMountPacket(player.Vehicle.UniqueId, 0x01);
                else
                {
                    var horseItem = player.Inventory.FirstOrDefault(item =>
                        item?.Record?.CodeName?.StartsWith("ITEM_COS_C_", StringComparison.OrdinalIgnoreCase) == true);
                    success = horseItem?.Use() ?? false;
                }
            }
            else if (type == "fellow" || type == "attack" || type == "pet")
            {
                success = player.Fellow != null && SendMountPacket(player.Fellow.UniqueId, 0x01);
            }
            else
            {
                if (player.JobTransport != null)
                    success = SendMountPacket(player.JobTransport.UniqueId, 0x01);
                else if (player.Vehicle != null)
                    success = SendMountPacket(player.Vehicle.UniqueId, 0x01);
                else
                {
                    var transportItem = player.Inventory.FirstOrDefault(item =>
                        item?.Record?.CodeName?.StartsWith("ITEM_COS_T_", StringComparison.OrdinalIgnoreCase) == true);
                    success = transportItem?.Use() ?? false;
                }
            }

            Log.Notify("[xControl] Mount " + type + (success ? " requested." : " failed or unavailable."));
        }

        private static void TryDismount(string petType)
        {
            if (!IsConnected)
                return;

            var type = (petType ?? "transport").Trim().ToLowerInvariant();
            var player = Game.Player;
            bool success;

            if (type == "pick" || type == "pickup" || type == "grab" || type == "ability")
            {
                Log.Warn("[xControl] Ability/pick pets cannot be dismounted through this command.");
                return;
            }

            if (type == "fellow" || type == "attack" || type == "pet")
                success = player.Fellow != null && SendMountPacket(player.Fellow.UniqueId, 0x00);
            else if (player.Vehicle != null)
                success = SendMountPacket(player.Vehicle.UniqueId, 0x00);
            else if (player.AbilityPet != null)
                success = SendMountPacket(player.AbilityPet.UniqueId, 0x00);
            else
                success = false;

            Log.Notify("[xControl] Dismount " + type +
                       (success ? " requested." : " failed or no matching active pet."));
        }

        private static void TerminateTransportPets()
        {
            if (!IsConnected)
                return;

            var jobTransport = Game.Player.JobTransport;
            var transport = Game.Player.Transport;

            if (jobTransport == null && transport == null)
            {
                Log.Warn("[xControl] No active transport pet to terminate.");
                return;
            }

            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(500);
                try
                {
                    jobTransport?.Terminate();
                    transport?.Terminate();
                }
                catch (Exception ex)
                {
                    Log.Warn("[xControl] Transport terminate failed: " + ex.Message);
                }
            });
        }

        private static void TryTeleportByIndex(int index)
        {
            if (!IsConnected)
                return;

            if (SpawnManager.TryGetEntities<SpawnedPortal>(
                portal => portal.Links != null && portal.Links.Count > 0,
                out var portals))
            {
                var nearestPortal = portals.OrderBy(portal => portal.DistanceToPlayer).FirstOrDefault();
                if (nearestPortal != null && TryTeleportByLinks(nearestPortal, nearestPortal.Links, index))
                    return;
            }

            if (SpawnManager.TryGetEntities<SpawnedNpcNpc>(out var npcs))
            {
                var candidates = npcs
                    .Select(npc => new
                    {
                        Npc = npc,
                        Teleport = Game.ReferenceManager.TeleportData
                            .FirstOrDefault(tp => tp.AssocRefObjId == npc.Id)
                    })
                    .Where(x => x.Teleport != null)
                    .OrderBy(x => x.Npc.DistanceToPlayer)
                    .ToArray();

                var nearest = candidates.FirstOrDefault();
                if (nearest != null &&
                    TryTeleportByLinks(nearest.Npc, nearest.Teleport.GetLinks(), index))
                    return;
            }

            Log.Warn("[xControl] No nearby portal/NPC with teleport destinations was found.");
        }

        private static bool TryTeleportByLinks(SpawnedBionic source,
            IList<RefTeleportLink> links, int index)
        {
            if (source == null || links == null || index < 1 || index > links.Count)
                return false;

            var link = links[index - 1];
            var destination = link?.Target;
            if (destination == null)
                return false;

            StopFollow();
            var destinationId = destination.ID;
            var destinationName = destination.ZoneName;
            System.Threading.Tasks.Task.Run(() =>
            {
                var ok = TeleportByEntity(source, destinationId);
                Log.Notify($"[xControl] Q{index} -> {destinationName}: {(ok ? "accepted" : "failed")}");
            });
            return true;
        }

        private static RefTeleport FindTeleport(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return Game.ReferenceManager.TeleportData.FirstOrDefault(tp =>
            {
                var npcName = tp.Character?.NameStrID;
                var translatedNpcName = string.IsNullOrWhiteSpace(npcName)
                    ? string.Empty
                    : Game.ReferenceManager.GetTranslation(npcName);

                return string.Equals(tp.CodeName, name, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(tp.ZoneName, name, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(tp.ZoneName128, name, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(translatedNpcName, name, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static void TryTeleportNamed(string args)
        {
            var parts = args.Split(new[] { ',' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                Log.Warn("[xControl] Usage: TP [SourceCodeName,DestinationCodeName]");
                return;
            }

            var sourceRef = FindTeleport(parts[0].Trim());
            var destinationRef = FindTeleport(parts[1].Trim());
            if (sourceRef == null || destinationRef == null)
            {
                Log.Warn("[xControl] Teleport source/destination not found in reference data.");
                return;
            }

            var linkExists = sourceRef.GetLinks().Any(link => link.TargetTeleport == (int)destinationRef.ID);
            if (!linkExists)
            {
                Log.Warn("[xControl] This teleport source has no link to " + destinationRef.ZoneName);
                return;
            }

            SpawnedBionic source = null;
            if (sourceRef.AssocRefObjId != 0 &&
                SpawnManager.TryGetEntity<SpawnedNpcNpc>(npc => npc.Id == sourceRef.AssocRefObjId, out var npc))
                source = npc;

            if (source == null &&
                SpawnManager.TryGetEntity<SpawnedPortal>(
                    portal => string.Equals(portal.Record?.CodeName, sourceRef.CodeName,
                        StringComparison.OrdinalIgnoreCase),
                    out var portalEntity))
                source = portalEntity;

            if (source == null)
            {
                Log.Warn("[xControl] Source teleporter is not spawned nearby: " + sourceRef.CodeName);
                return;
            }

            StopFollow();
            var sourceCodeName = sourceRef.CodeName;
            var destinationId = destinationRef.ID;
            var destinationName = destinationRef.ZoneName;
            System.Threading.Tasks.Task.Run(() =>
            {
                var success = TeleportByEntity(source, destinationId);
                Log.Notify("[xControl] TP " + sourceCodeName + " -> " + destinationName +
                           (success ? " accepted." : " failed."));
            });
        }

        private static bool TeleportByEntity(SpawnedBionic source, uint destinationId)
        {
            if (!IsConnected || source == null || destinationId == 0)
                return false;

            try
            {
                if (!source.TrySelect())
                    return false;

                var packet = new Packet(0x705A);
                packet.WriteUInt(source.UniqueId);
                packet.WriteByte(0x02);
                packet.WriteUInt(destinationId);

                var responseOpcode = (ushort)(Game.ClientType == GameClientType.Rigid ? 0x3077 : 0x3012);
                var callback = new AwaitCallback(null, responseOpcode);
                PacketManager.SendPacket(packet, PacketDestination.Server, callback);
                callback.AwaitResponse(30000);
                return callback.IsCompleted;
            }
            catch (Exception ex)
            {
                Log.Warn("[xControl] Teleport request failed: " + ex.Message);
                return false;
            }
        }

        private static void HandleTeleportSync(string sender, string args)
        {
            var split = args.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (split.Length != 2 || !uint.TryParse(split[0], out var destinationId))
            {
                Log.Warn("[xControl] Invalid XCTP command.");
                return;
            }

            var sourceCodeName = split[1].Trim();
            SpawnedBionic source = null;

            if (SpawnManager.TryGetEntity<SpawnedNpcNpc>(
                npc => string.Equals(npc.Record?.CodeName, sourceCodeName, StringComparison.OrdinalIgnoreCase),
                out var npc))
                source = npc;

            if (source == null &&
                SpawnManager.TryGetEntity<SpawnedPortal>(
                    portal => string.Equals(portal.Record?.CodeName, sourceCodeName,
                        StringComparison.OrdinalIgnoreCase),
                    out var portal))
                source = portal;

            if (source == null)
            {
                Log.Warn("[xControl] XCTP source not found nearby: " + sourceCodeName);
                return;
            }

            StopFollow();
            System.Threading.Tasks.Task.Run(() =>
            {
                var result = TeleportByEntity(source, destinationId);
                Log.Notify("[xControl] Synced teleport from " + sender + ": " +
                           (result ? "accepted" : "failed"));
            });
        }

        // Called by the passive packet observers below.
        public static void CaptureTeleportRequest(Packet packet)
        {
            if (!Game.Ready || Game.Player == null || Game.Party?.IsLeader != true)
                return;

            try
            {
                var uid = packet.ReadUInt();
                var mode = packet.ReadByte();
                var destinationId = packet.ReadUInt();
                if (mode != 0x02 || destinationId == 0)
                    return;

                if (!SpawnManager.TryGetEntity<SpawnedBionic>(uid, out var entity))
                    return;

                _pendingTeleport = new PendingTeleport
                {
                    DestinationId = destinationId,
                    SourceCodeName = entity.Record?.CodeName ?? string.Empty,
                    StartPosition = Game.Player.Position,
                    StartedAt = Environment.TickCount
                };
            }
            catch (Exception ex)
            {
                Log.Debug("[xControl] Could not inspect teleport packet: " + ex.Message);
            }
        }

        public static void ConfirmTeleportResponse()
        {
            if (_pendingTeleport != null)
                _pendingTeleport.ServerAccepted = true;
        }

        private static void UpdatePendingTeleport()
        {
            var pending = _pendingTeleport;
            if (pending == null)
                return;

            if (Environment.TickCount - pending.StartedAt > 20000)
            {
                _pendingTeleport = null;
                return;
            }

            if (!pending.ServerAccepted || string.IsNullOrWhiteSpace(pending.SourceCodeName))
                return;

            var current = Game.Player.Position;
            var moved = current.Region.Id != pending.StartPosition.Region.Id ||
                        pending.StartPosition.DistanceTo(current) > 80;

            if (!moved)
                return;

            _pendingTeleport = null;
            if (Game.Party?.IsLeader == true && Game.Party.IsInParty)
            {
                ChatManager.Send(ChatType.Party,
                    $"XCTP {pending.DestinationId} {pending.SourceCodeName}");
                Log.Notify("[xControl] Teleport synchronized to party.");
            }
        }
    }

    /// <summary>
    /// Passive observer for a leader's outbound teleport request.
    /// It does not replace or block the original packet.
    /// </summary>
    public sealed class XControlTeleportRequestObserver : IPacketHandler
    {
        public ushort Opcode => 0x705A;
        public PacketDestination Destination => PacketDestination.Server;

        public void Invoke(Packet packet)
        {
            XControlService.CaptureTeleportRequest(packet);
        }
    }

    public sealed class XControlTeleportResponseObserver : IPacketHandler
    {
        public ushort Opcode => 0x3012;
        public PacketDestination Destination => PacketDestination.Client;

        public void Invoke(Packet packet)
        {
            XControlService.ConfirmTeleportResponse();
        }
    }

    public sealed class XControlTeleportResponseRigidObserver : IPacketHandler
    {
        public ushort Opcode => 0x3077;
        public PacketDestination Destination => PacketDestination.Client;

        public void Invoke(Packet packet)
        {
            XControlService.ConfirmTeleportResponse();
        }
    }
}