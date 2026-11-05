# NAME: Oasis API Test
# DESCRIPTION: Interactive smoke tests for the expanded OasisBot Python API.
# AUTHOR: OasisBot
# VERSION: 1.0.0

from RSBot import (
    core,
    player,
    entity,
    inventory,
    config,
    party,
    pets,
    chat,
    packets,
    npc,
    teleport,
    gui,
)

PLUGIN_NAME = "Oasis API Test"
_packet_handler_id = 0
_tick_count = 0


def log(message):
    core.log("[API TEST] " + str(message))


def safe_call(label, callback):
    try:
        result = callback()
        log(label + ": " + str(result))
        return result
    except Exception as exc:
        log(label + " ERROR: " + repr(exc))
        return None


def test_player():
    status = player.get_status()
    log("Player status: " + str(status))
    if not status:
        status_label.set_text("Player: not in game")
        return
    status_label.set_text(
        "HP " + str(status.get("hp_percentage", 0)) +
        "% | MP " + str(status.get("mp_percentage", 0)) +
        "% | Dead: " + str(status.get("is_dead", False))
    )


def test_inventory():
    data = inventory.get_inventory()
    items = data.get("items", data.get("inventory", []))
    log("Inventory: size=" + str(data.get("size", 0)) +
        ", item count=" + str(len(items)))
    for item in items[:10]:
        log("Item: " + str(item))


def test_party():
    members = party.get_members()
    log("Party: in_party=" + str(party.is_in_party()) +
        ", leader=" + party.get_leader_name() +
        ", members=" + str(len(members)))
    for member in members:
        log("Party member: " + str(member))


def test_pets():
    log("Active pets: " + str(pets.get_active_pets()))


def test_world():
    players_list = entity.get_players()
    npcs_list = entity.get_npcs()
    portals_list = entity.get_portals()
    log("World counts: players=" + str(len(players_list)) +
        ", NPCs=" + str(len(npcs_list)) +
        ", portals=" + str(len(portals_list)))
    for portal in portals_list[:10]:
        log("Portal: " + str(portal))


def test_teleport_destinations():
    destinations = teleport.get_destinations()
    log("Teleport destinations: " + str(len(destinations)))
    for destination in destinations[:15]:
        log("Destination: " + str(destination))


def test_npc_dialog_state():
    log("NPC dialog state: " + str(npc.get_dialog_state()))


def test_config():
    key = "OasisApiTest.LastCheck"
    config.set_string(key, "OK")
    saved = config.save()
    value = config.get_string(key, "")
    log("Config read/write: value=" + value + ", save=" + str(saved))


def test_training():
    area = training.get_training_position()
    log("Training position: " + str(area))


def send_test_chat():
    message = chat_message.get_text().strip()
    if not message:
        log("Enter a chat message first.")
        return
    sent = chat.send_all("[API TEST] " + message)
    log("All chat send returned: " + str(sent))


def invite_party_member():
    name = party_name.get_text().strip()
    if not name:
        log("Enter a character name first.")
        return
    result = party.invite_player(name)
    log("Party invite returned: " + str(result))


def leave_party():
    result = party.leave_party()
    log("Leave party returned: " + str(result))


def use_item_from_slot():
    raw_slot = item_slot.get_text().strip()
    try:
        slot = int(raw_slot)
    except ValueError:
        log("Inventory slot must be an integer.")
        return
    result = inventory.use_item(slot)
    log("Use item at slot " + str(slot) + ": " + str(result))


def start_trace():
    name = trace_name.get_text().strip()
    if not name:
        log("Enter a visible player name first.")
        return
    result = player.start_trace(name)
    log("Start trace returned: " + str(result))


def stop_trace():
    result = player.stop_trace()
    log("Stop trace returned: " + str(result) +
        " (verify this phase on your server before relying on it)")


def use_hp_potion():
    log("HP potion returned: " + str(player.use_hp_potion()))


def use_mp_potion():
    log("MP potion returned: " + str(player.use_mp_potion()))


def use_return_scroll():
    log("Return scroll returned: " + str(player.use_return_scroll()))


def use_berserk():
    log("Berserk returned: " + str(player.use_berserk()))


def set_test_training_radius():
    result = training.set_training_radius(60)
    log("Set training radius to 60 returned: " + str(result))


def talk_to_npc():
    try:
        unique_id = int(npc_uid.get_text().strip())
        option = int(npc_option.get_text().strip())
    except ValueError:
        log("NPC UID and talk option must be integers.")
        return
    log("NPC talk returned: " + str(npc.talk(unique_id, option)))


def teleport_to_destination():
    code_name = teleporter_name.get_text().strip()
    try:
        destination_id = int(destination_uid.get_text().strip())
    except ValueError:
        log("Destination ID must be an integer.")
        return
    if not code_name:
        log("Enter the teleporter NPC code name.")
        return
    result = teleport.teleport(code_name, destination_id)
    log("Teleport request returned: " + str(result))


def summon_growth():
    log("Summon growth returned: " + str(pets.summon_growth()))


def summon_fellow():
    log("Summon fellow returned: " + str(pets.summon_fellow()))


def dismount_pet():
    log("Dismount returned: " + str(pets.dismount()))


def feed_pet():
    log("Use pet hunger/satiety potion returned: " + str(pets.use_hunger_potion()))


def terminate_growth():
    log("Terminate growth returned: " + str(pets.terminate("growth")))


def register_chat_packet_monitor():
    global _packet_handler_id
    if _packet_handler_id:
        log("Chat packet monitor already registered: " + str(_packet_handler_id))
        return
    _packet_handler_id = packets.add_handler(
        0x3026,
        "server_to_client",
        "on_chat_packet",
    )
    log("Chat packet monitor ID: " + str(_packet_handler_id))


def unregister_chat_packet_monitor():
    global _packet_handler_id
    if not _packet_handler_id:
        log("No chat packet monitor is registered.")
        return
    removed = packets.remove_handler(_packet_handler_id)
    log("Chat packet monitor removed: " + str(removed))
    _packet_handler_id = 0


def on_chat_packet(opcode, direction, data_hex):
    log("Observed packet 0x%04X (%s), %d bytes" % (
        opcode, direction, len(data_hex.split())
    ))


def on_chat_message(message, sender, chat_type):
    log("Chat event from " + str(sender) +
        " [type=" + str(chat_type) + "]: " + str(message))


def on_enter_game():
    log("EVENT: entered game")


def on_disconnect():
    log("EVENT: disconnected from agent server")


def on_teleport_start():
    log("EVENT: teleport started")


def on_teleport_complete():
    log("EVENT: teleport completed")


def on_tick():
    global _tick_count
    _tick_count += 1
    if _tick_count % 120 == 0:
        log("Heartbeat: on_tick is alive")


# Build a small test panel. Action buttons only run after an explicit click.
ui = gui.GUI(PLUGIN_NAME)
ui.Label("OasisBot Python API smoke test", 12, 10, 420, 25)

status_label = ui.Label("Player: not checked", 12, 35, 520, 25)

ui.Button("Player", 12, 70, 100, 30, test_player)
ui.Button("Inventory", 120, 70, 100, 30, test_inventory)
ui.Button("Party", 228, 70, 100, 30, test_party)
ui.Button("Pets", 336, 70, 100, 30, test_pets)
ui.Button("World", 444, 70, 100, 30, test_world)

ui.Button("Teleport list", 12, 108, 120, 30, test_teleport_destinations)
ui.Button("NPC dialog", 140, 108, 120, 30, test_npc_dialog_state)
ui.Button("Config R/W", 268, 108, 120, 30, test_config)
ui.Button("Training", 396, 108, 120, 30, test_training)

ui.Label("Test All chat message:", 12, 150, 150, 22)
chat_message = ui.TextBox("API test message", 165, 146, 220, 28)
ui.Button("Send All", 395, 146, 100, 30, send_test_chat)

ui.Label("Party member name:", 12, 188, 150, 22)
party_name = ui.TextBox("", 165, 184, 220, 28)
ui.Button("Invite", 395, 184, 100, 30, invite_party_member)
ui.Button("Leave Party", 500, 184, 110, 30, leave_party)

ui.Label("Inventory slot:", 12, 226, 120, 22)
item_slot = ui.TextBox("13", 135, 222, 70, 28)
ui.Button("Use Item", 220, 222, 100, 30, use_item_from_slot)

ui.Label("Visible player name:", 12, 264, 150, 22)
trace_name = ui.TextBox("", 165, 260, 220, 28)
ui.Button("Start Trace", 395, 260, 100, 30, start_trace)
ui.Button("Stop Trace", 500, 260, 100, 30, stop_trace)

ui.Button("HP Potion", 12, 300, 100, 30, use_hp_potion)
ui.Button("MP Potion", 120, 300, 100, 30, use_mp_potion)
ui.Button("Return Scroll", 228, 300, 120, 30, use_return_scroll)
ui.Button("Berserk", 356, 300, 100, 30, use_berserk)

ui.Button("Watch Chat Packets", 12, 338, 160, 30, register_chat_packet_monitor)
ui.Button("Stop Packet Watch", 180, 338, 160, 30, unregister_chat_packet_monitor)

ui.Label("NPC UID:", 12, 378, 80, 22)
npc_uid = ui.TextBox("0", 95, 374, 95, 28)
ui.Label("Talk option:", 200, 378, 90, 22)
npc_option = ui.TextBox("8", 290, 374, 60, 28)
ui.Button("Talk to NPC", 365, 374, 125, 30, talk_to_npc)

ui.Label("Teleporter CodeName:", 12, 416, 150, 22)
teleporter_name = ui.TextBox("", 165, 412, 190, 28)
ui.Label("Destination ID:", 365, 416, 100, 22)
destination_uid = ui.TextBox("0", 465, 412, 75, 28)
ui.Button("Teleport", 548, 412, 90, 30, teleport_to_destination)

ui.Button("Summon Growth", 12, 450, 130, 30, summon_growth)
ui.Button("Summon Fellow", 150, 450, 130, 30, summon_fellow)
ui.Button("Dismount", 288, 450, 100, 30, dismount_pet)
ui.Button("Feed Pet", 396, 450, 100, 30, feed_pet)
ui.Button("Terminate Growth", 504, 450, 135, 30, terminate_growth)

ui.Button("Set Training Radius = 60", 12, 488, 190, 30, set_test_training_radius)

log("Loaded successfully. Use the read-only test buttons first.")
