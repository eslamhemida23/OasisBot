# OasisBot Python API

The Python bridge exposes the registered C# API modules as namespaced objects. Import the modules you need:

```python
from RSBot import (
    core,
    player,
    entity,
    inventory,
    config,
    training,
    quest,
    party,
    pets,
    chat,
    packets,
    npc,
    teleport,
    gui,
)
```

The legacy flat API remains available for compatibility (for example, `RSBot.start_bot()`). Prefer the module namespaces for new scripts because similarly named methods can exist in different modules.

## Player

```python
status = player.get_status()
print(status["hp_percentage"], status["mp_percentage"], status["is_dead"])

player.use_hp_potion()
player.use_mp_potion()
player.use_return_scroll()
player.use_berserk()

# Coordinates are world coordinates; region is the numeric region ID.
player.move_to(6500.0, 1200.0, 25000, False)

player.start_trace("CharacterName")
player.stop_trace()
```

`get_status()` returns the character name, current and maximum HP/MP, percentages, death and berserk state, level, coordinates, and region. `move_to` returns the Core movement result; it does not calculate a cross-region route.

Trace uses the existing `0x7074` object-action packet. The start format is already present in OasisBot's Party command code. The stop phase uses phase `2` (cancel/complete); validate this behavior against the target server before relying on automated trace cancellation.

## Party

```python
if party.is_in_party():
    print(party.get_leader_name())
    for member in party.get_members():
        print(member["name"], member["hp_percentage"])

party.invite_player("CharacterName")
party.leave_party()
party.banish("MemberName")  # leader only
```

## Inventory

```python
items = inventory.get_inventory()
item = inventory.get_item_at(13)
matches = inventory.find_item("potion")

inventory.use_item(13)
inventory.use_item_on_slot(13, 6)
inventory.move_item(13, 14, 0)  # amount 0 means move the full stack
```

Item-use and move methods return a success result from the existing Core operation. Check the slot and item data before invoking them.

## Pets

```python
active = pets.get_active_pets()
pets.summon_growth()
pets.summon_fellow()
pets.summon_vehicle()
pets.summon_ability_pet()
pets.revive_growth()
pets.revive_fellow()
pets.use_hunger_potion()
pets.dismount()
pets.terminate("growth")
```

Supported pet type strings for `terminate` are `growth`, `fellow`, `transport`, `job_transport`, `ability`, and `vehicle`. For `use_hp_potion`, the currently supported values are `growth`, `fellow`, `transport`, and `job_transport`.

## Packets

```python
# A callback at module scope receives (opcode, direction, data_hex).
def on_interesting_packet(opcode, direction, data_hex):
    print(opcode, direction, data_hex)

handler_id = packets.add_handler(0x3026, "server_to_client", "on_interesting_packet")
# Remove one registration, or all registrations:
packets.remove_handler(handler_id)
packets.remove_all_handlers()

# Send raw bytes. Supply a payload that matches the selected opcode.
packets.send_server_hex(opcode, "PAYLOAD_HEX")
```

Packet directions are `client_to_server` and `server_to_client`. Registered handlers are passive observers: they receive the opcode, direction, and packet payload as hex, but cannot modify, replace, or suppress the original packet. For lower-level raw byte-array sending, the existing `core.send_server(opcode, data, encrypted)` and `core.send_client(opcode, data, encrypted)` methods remain available.

## Chat

```python
chat.send_all("Hello")
chat.send_party("Party message")
chat.send_guild("Guild message")
chat.send_union("Union message")
chat.send_private("CharacterName", "Private message")
chat.send_global("Global message")  # requires a global chat item
```

`send_message(channel, message, receiver=None)` also accepts the numeric `ChatType` values defined by OasisBot, but validates supported outgoing channels.

## NPC interaction

```python
npc_data = npc.get_npc(npc_uid)
print(npc_data["servername"], npc_data["options"])

npc.select(npc_uid)
npc.talk(npc_uid, 8)  # TalkOption.TeleportTarget
dialog = npc.get_dialog_state()
npc.deselect(npc_uid)
```

`talk(uid, option)` selects the NPC, sends the Core's `0x7046` talk request, and waits for the `0xB046` response. Common `TalkOption` values include Store=1, Quest=2, Storage=3, Repair=4, SetReturnPoint=7, TeleportTarget=8, TeleportReturnPoint=9, WithdrawSkill=10, Stable=11, Trade=12, Guild=15, Trader=20, Hunter=22, TeleportLastLocation=28, and TeleportGuide=30. The valid values depend on the NPC and server.

## World and teleport

```python
players = entity.get_players()
npcs = entity.get_npcs()
portals = entity.get_portals()
nearby = entity.get_entity_by_id(123456)

destinations = teleport.get_destinations()
links = teleport.get_links(portal_uid)
teleport.teleport_by_uid(npc_uid, destination_id)
# Or resolve the NPC by code name and destination by code name:
teleport.teleport_by_name("NPC_CODENAME", "DESTINATION_CODENAME")
```

`entity.get_entity_by_id` returns a dictionary with `found=False` when the entity is not currently known to the spawn manager. Teleporting requires the teleporter NPC to be spawned and selectable in the current area.

## Configuration and training

```python
threshold = config.get_int("MyPlugin.HPThreshold", 45)
config.set_int("MyPlugin.HPThreshold", threshold)
config.set_bool("MyPlugin.Enabled", True)
config.set_string("MyPlugin.Mode", "safe")
config.save()

training.set_training_radius(60)
training.set_training_position(6500.0, 1200.0, 25000, 60)
training.move_to(6510.0, 1210.0, 25000)
```

Configuration methods read and write the active character profile through `PlayerConfig`. Call `config.save()` to persist changes. Training radius is clamped to the Core-supported range of 5–100.

## Python plugin event callbacks

Define any callbacks your script needs at module level:

| Callback | Arguments | Meaning |
|---|---|---|
| `on_enter_game()` | none | Character entered the game |
| `on_disconnect()` | none | Agent server disconnected |
| `on_teleport_start()` | none | Teleportation started |
| `on_teleport_complete()` | none | Teleportation completed |
| `on_teleported(state)` | `1` start, `2` complete | Backward-compatible teleport callback |
| `on_chat_message(message, sender, chat_type)` | string, string, int | Parsed chat response; self All/AllGM messages are also emitted |
| `on_packet_from_client(opcode, data_hex)` | string, string | Packet observed from the game client |
| `on_packet_from_server(opcode, data_hex)` | string, string | Packet observed from the server |
| `event_loop()` | none | Existing Python timer callback |
| `on_tick()` | none | Additional timer callback, normally every 500 ms |

The chat callback is fed by the parsed ChatResponse handler. Notice messages and the currently unimplemented NPC chat branch are not included. The callback name passed to `packets.add_handler` is invoked with `(opcode, direction, data_hex)` whenever a matching packet is observed.

## Notes

- This API layer reuses Core operations where they already exist; it does not replace Core's safety checks or server-side restrictions.
- Boolean return values indicate whether the local operation was accepted or its existing callback completed. They do not guarantee that the game server accepted every resulting action unless a response is explicitly awaited.
- Teleport, item movement, chat, party actions, and trace are live game actions. Test with a low-risk character and verify the target server's packet behavior before unattended use.
- The GUI API remains available through `gui` and the legacy `RSBot.GUI` entry point.
