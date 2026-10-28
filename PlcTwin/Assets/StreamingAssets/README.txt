PlcTwin - Mitsubishi digital twin (MC protocol 3E binary over TCP, or Modbus TCP)
================================================================================

Config: plc_twin_config.json (this folder; in a built player: PlcTwin_Data/StreamingAssets/).
Edit it with a text editor and restart the app. An invalid config shows 'CONFIG ERROR' in the state line.
Changes made in the app UI (transport, ip, port) last for the session only; they are never written back to the file.
  transport         mc3e (real PLC, MC protocol 3E) or modbus (Modbus TCP server)
  mc3e              ip (numeric IPv4 only, no hostnames), port (must match the PLC open setting, default 5010),
                    monitorTimer250ms (>= 1, PLC monitoring timer in 250 ms units), pollMs (>= 10)
  modbus            ip, port (default 127.0.0.1:502), unitId (0..255), pollMs (>= 10),
                    writeTableBase / readTableBase (first D number of the server's write table / read table)
  connectTimeoutMs  > 0
  timeoutMs         per request; must be >= mc3e.monitorTimer250ms*250+500
  map               first D word of each block: inBits (4 words), inWords (5), outBits (4), outWords (7)
                    defaults D0, D40, D100, D140. Blocks must not overlap.
  panelVisible      show the status panel at start (the 'Panel' button toggles it; display only)
An old config (conveyor/tower/sensor sections) is no longer supported and is rejected with an error.

Use
  The app starts Disconnected: the red 3D buttons drive the line (hold the left mouse button), Metal/Nonmetal add a
  chip to the magazine, Reset (offline only) puts the line back to its power-on state and removes every chip,
  a click on a chip outside the magazine removes it. Connect: the PLC drives the line and the
  3D buttons only change color. Disconnect or a link error: the servo stops, the vacuum drops, the 3D buttons work again.
  The window is resizable (default 1387 x 1000); the layout scales to fit.
  Look: the default is style a (modern industrial). Start 'PlcTwin.exe -style b' (realistic kit) or '-style c' (blueprint,
  outlines do not work with the Blender model) to try the others. Only the 3D equipment changes.
  The servo starts at D40 = 140 (home); D1.0 (emit sensor) is no longer simulated and stays 0.
  The stopper rests DOWN (D0.8 ON); D100.7 ON raises it (D0.9 ON) and it then holds the chips at the stop point.
  View: the Default / Front / Iso / Top buttons (bottom row, right of Panel) move the camera to a fixed view;
  start in one with 'PlcTwin.exe -view front' (default, front, iso or top; e.g. '-view front -style a').
  The mouse never moves the camera.
  On quit the app writes nothing: D0-D3/D40-D44 keep the last written values.

Device numbering
  D numbers are DECIMAL (D100 = 100). Only D devices are used.

Data direction
  The app WRITES inBits (D0-D3) and inWords (D40-D44) to the PLC; the ladder must not write D0-D3/D40-D44
  (no MOV/OUT/SET into those words).
  The app READS outBits (D100-D103) and outWords (D140-D146), which the ladder writes.
  While the app is connected it rewrites D0-D3/D40-D44 every cycle, so forcing them from GX Works2 (device test)
  is overwritten within one poll; disconnect the app first.

GX Works2 simulator via ModbusServerMitsubishi
  Start GX Simulator2, start ModbusServerMitsubishi (GX-Works2 target). On its Setting tab check the D0000 / D0100
  table bases: they must equal modbus.writeTableBase / readTableBase (defaults 0 / 100). Select Modbus in the app
  (127.0.0.1:502) and press Connect.

Modbus server notes
  - A read returns the whole 80-word table; the app always reads it with start 0.
  - The server IGNORES the start address of a write (FC16): every write lands at the first word of the write table.
    So in Modbus mode every write sends D0 up to D44 in one frame, and D4-D39 are written as 0. Do not use D4-D39
    for anything else in Modbus mode. (mc3e is unaffected: it writes only D0-D3 and D40-D44.)
    Whether the server's write table actually reaches the PLC is not verified yet (user sentinel test).
  - The server serves a single client; do not connect a second tool at the same time.
  - The real server needs about 310 ms per request, so one Modbus cycle (2 writes + 1 read) takes about 0.9 s
    (measured 0.93 s); modbus.pollMs only adds waiting on top of that. Unchanged blocks are resent about once a second.
  - Each table is 80 words: with the defaults the write table is D0-D79 and the read table D100-D179
    (writeTableBase / readTableBase); every map block must lie inside its table.

Port
  Default 5010 for mc3e. Ports in the reserved range (about 5000-5009, and 1388H = 5000 upward) must not be
  used for the open setting. TO BE CONFIRMED against the CPU/module manual for your exact model;
  if needed change mc3e.port in the config and the PLC open setting together.

PLC notes
  - Writing during RUN must be enabled on the PLC (QJ71E71: 'Enable Write at RUN'; built-in Ethernet:
    'Enable online change'). If a write is rejected, the state line shows the end code in hex.

LAN SAFETY
  With write-during-RUN enabled, ANYONE on the same LAN can write to this PLC over MC protocol.
  Use a lab network only. Never connect it to a production network.
