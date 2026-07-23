# Task: Update the Windows desktop controller for the current flight-control protocol

Use this document as the implementation brief for the Windows desktop application that connects to the RemoteController over a serial port. Inspect the existing Visual Studio solution first, then adapt these requirements to its current architecture and UI framework. Do not replace working application structure unnecessarily.

## Goal

Update the desktop application so it:

- sends only complete, correctly encoded 64-byte binary packets;
- sends heartbeat packets while the application considers the aircraft armed;
- serializes all writes so heartbeat, configuration, ARM, and DISARM packets can never interleave;
- handles serial disconnects and application shutdown safely;
- optionally allows desktop heartbeat transmission to be disabled for bench testing;
- clearly warns that the embedded heartbeat failsafe is configured independently;
- reads RemoteController diagnostic text without treating it as command acknowledgement.

## Serial-port settings

Configure the port as follows:

| Setting | Value |
|---|---:|
| Baud rate | 115200 |
| Data bits | 8 |
| Parity | None |
| Stop bits | One |
| Handshake/flow control | None |

Open the serial port once and give one service/component exclusive ownership of it. Do not let individual views or button handlers write directly to the port.

## Binary framing rules

Every command is exactly 64 bytes. There is no length prefix, delimiter, JSON, text encoding, or newline in the outbound command protocol.

- Allocate a new zero-filled 64-byte array for every packet.
- Populate the documented fields and leave all reserved bytes as zero.
- Write the complete array in one serialized operation.
- Never use `WriteLine`, `StreamWriter`, ASCII conversion, or string concatenation for commands.
- Never make several asynchronous writes for one packet.
- A partial packet can desynchronize the current firmware's fixed-size UART framing, so cancellation must occur between packets, not during a packet write.

## Packet types

| Byte 0 | Packet | Remaining fields |
|---:|---|---|
| 1 | Configuration | Bytes 1-42 as documented below; bytes 43-63 zero |
| 2 | Control | Byte 1 is the command; bytes 2-63 zero |
| 3 | Heartbeat | Bytes 1-63 zero |

Control commands:

| Byte 1 | Command |
|---:|---|
| 0 | DISARM |
| 1 | ARM |
| 2 | TAKEOFF to the fixed 50 cm hover target |

Create a single protocol class/module with constants and methods similar to:

```text
PacketSize = 64
BuildConfigurationPacket(...)
BuildArmPacket()
BuildDisarmPacket()
BuildTakeoffPacket()
BuildHeartbeatPacket()
```

Do not duplicate packet-building logic in UI event handlers.

## Configuration-packet layout

All 16-bit values are little-endian: low byte first, then high byte. Target values are signed 16-bit integers. Scaled PID values are unsigned 16-bit integers unless the field is explicitly one byte.

| Bytes | Field | Encoding |
|---:|---|---|
| 0 | Packet type | `1` |
| 1 | Arm throttle | Unsigned byte; all motors start at this value after ARM |
| 2 | Minimum speed | Unsigned byte |
| 3 | Maximum speed | Unsigned byte |
| 4 | Maximum angle | Unsigned byte |
| 5-6 | Pitch target | Signed Int16, little-endian |
| 7-8 | Roll target | Signed Int16, little-endian |
| 9-10 | Yaw target | Signed Int16, little-endian |
| 11-12 | Gz target | Signed Int16, little-endian |
| 13-14 | Reserved | Zero; takeoff and altitude hold target is fixed at 50 cm |
| 15 | Pitch Kp | `round(value * 100)`, UInt8 |
| 16-17 | Pitch Ki | `round(value * 10000)`, UInt16 LE |
| 18-19 | Pitch Kd | `round(value * 1000)`, UInt16 LE |
| 20 | Roll Kp | `round(value * 100)`, UInt8 |
| 21-22 | Roll Ki | `round(value * 10000)`, UInt16 LE |
| 23-24 | Roll Kd | `round(value * 1000)`, UInt16 LE |
| 25 | Gz Kp | `round(value * 100)`, UInt8 |
| 26-27 | Gz Ki | `round(value * 1000)`, UInt16 LE |
| 28-29 | Gz Kd | `round(value * 10000)`, UInt16 LE |
| 30 | Altitude Kp | `round(value * 100)`, UInt8 |
| 31-32 | Altitude Ki | `round(value * 1000)`, UInt16 LE |
| 33-34 | Altitude Kd | `round(value * 10000)`, UInt16 LE |
| 35 | Reserved | Zero |
| 36-37 | Maximum PID I part | `round(value * 10)`, UInt16 LE |
| 38-39 | Maximum PID output | `round(value * 10)`, UInt16 LE |
| 40 | Reserved | Zero |
| 41-42 | Takeoff throttle ramp | `%/s` encoded as `round(value * 10)`, UInt16 LE |
| 43-63 | Reserved | Zero |

Validate values before conversion. Do not silently wrap an out-of-range number during a cast.

At minimum, enforce the firmware's current safety constraints:

- minimum speed: 0-100;
- maximum speed: 0-100 and greater than or equal to minimum speed;
- arm throttle: between minimum and maximum speed;
- maximum angle: 1-90;
- takeoff throttle ramp: 1.0-30.0 %/s;
- maximum PID I part: 0-100;
- maximum PID output: 0-100;
- all scaled fields must fit their destination UInt8/UInt16;
- pitch, roll, yaw, and Gz targets must fit Int16.

Throttle workflow:

- `DISARMED`: motor output is zero.
- `ARMED`: all four motors run equally at the configured arm throttle.
- `TAKEOFF`: firmware ramps collective upward from arm throttle toward maximum speed.
- When SRF05 reaches the fixed 50 cm target, firmware captures the achieved collective.
- `FLYING`: altitude hold keeps that captured collective as feed-forward and applies altitude PID correction around it.

The firmware rejects configuration changes while flying. Disable the configuration-send action while the desktop state is armed, and require an explicit DISARM first.

## Heartbeat behavior

The normal and recommended setting is heartbeat enabled.

- Send a heartbeat packet every 250 ms while the serial port is open and the desktop state is armed.
- Use a monotonic/background timer, not a UI timer that can be delayed by rendering or modal dialogs.
- The maximum expected interval, including scheduling jitter and queued writes, must stay safely below 750 ms.
- Do not enqueue another heartbeat if an unsent heartbeat is already queued. Coalesce redundant heartbeats instead of building a backlog.
- Start heartbeat scheduling immediately after the ARM packet has been written.
- Stop heartbeat scheduling only after a requested DISARM packet has been written, or immediately after a serial failure/disconnect.
- Configuration, ARM, and DISARM packets also count as valid link activity in the embedded firmware, but do not use repeated ARM packets as a heartbeat substitute.

Current embedded safety timing is:

- RemoteController sends a one-shot DISARM after 750 ms without a valid upstream packet.
- FlightController independently disarms after 1000 ms without a heartbeat or valid control packet.

Both firmware projects have a compile-time flag named `HEARTBEAT_FAILSAFE_ENABLED`. It defaults to `1`. This flag is not negotiated over serial and the desktop cannot detect its value.

Add a desktop application setting such as `HeartbeatTransmissionEnabled`, defaulting to `true`. This setting controls only whether the desktop sends heartbeat packets; it does not disable either embedded failsafe.

Safety rules for the setting:

- Only allow it to change while disarmed.
- Show a persistent, prominent warning while desktop heartbeat transmission is disabled.
- Explain that both embedded projects must separately be built with `HEARTBEAT_FAILSAFE_ENABLED=0` if operation without heartbeats is intentionally required.
- If either embedded failsafe remains enabled, arming without desktop heartbeats will result in automatic DISARM.

Do not add a runtime packet that claims to enable or disable the embedded failsafe; no such command exists in the current protocol.

## Single-writer architecture

Implement one outbound packet queue and one writer task/service. All packet sources must use it:

- configuration action;
- ARM action;
- DISARM action;
- heartbeat scheduler;
- shutdown/disconnect safety action.

Use the solution's existing dependency-injection and async patterns where available. In a modern .NET application, a `Channel<T>`, a dedicated writer loop, and `CancellationToken`/`PeriodicTimer` are suitable. A lock or semaphore around unrelated button-handler writes is less desirable because it does not provide prioritization, coalescing, or lifecycle control.

Required queue behavior:

1. Never allow packet bytes to interleave.
2. DISARM has the highest priority.
3. Do not discard configuration, ARM, or DISARM packets silently.
4. Keep at most one pending heartbeat and drop/coalesce only redundant heartbeats.
5. Do not allow heartbeats to continue accumulating while the port is slow or disconnected.
6. Report write exceptions to the connection/state service and UI.

The RemoteController buffers only a small number of complete packets. Do not burst several redundant commands or heartbeats back-to-back.

## ARM, DISARM, disconnect, and shutdown lifecycle

Maintain an explicit connection/control state rather than inferring everything from button appearance. Names may be adapted to the existing application, but distinguish at least:

- disconnected;
- connected/disarmed;
- ARM requested;
- DISARM requested or connected/disarmed after the write completes.

Required behavior:

- ARM: require an open port and valid configuration/state, write one ARM packet, then start heartbeats if enabled.
- DISARM: enqueue/write DISARM at highest priority, stop heartbeats after that write completes, and update the UI state.
- Application close or intentional port close while armed: attempt one prioritized DISARM write with a short bounded shutdown deadline, then close the port. Do not hang application shutdown indefinitely.
- Unexpected disconnect or write failure: stop heartbeat scheduling, mark the connection unavailable, display a prominent warning, and state that the embedded timeout should disarm the aircraft when its failsafe is enabled.
- Reconnection must return the desktop to a conservative disarmed/unknown state. Never automatically restore an armed state or automatically send ARM.

Do not send several DISARM packets rapidly. The receiver has a small queue and no hardware flow control.

## Incoming serial diagnostics

RemoteController transmits newline-delimited diagnostic text on the serial TX direction. Treat this as logs only.

- Read asynchronously without blocking the UI.
- Accumulate partial reads and split complete lines on `\n`; trim an optional `\r`.
- Do not assume one serial data-received event equals one complete line.
- Bound the receive buffer and UI log history.
- Marshal UI updates onto the UI thread using the framework's normal dispatcher mechanism.

These messages are not end-to-end acknowledgements. A LoRa TX-complete result only means the transmitter completed its radio operation; it does not prove that FlightController received or accepted the command. Until a bidirectional acknowledgement protocol is added, label UI state as locally requested/command sent rather than remotely confirmed.

## Tests to add

Add automated tests around the protocol builder and connection service. Tests must not require physical motors or an armed aircraft.

### Packet tests

- Every builder returns exactly 64 bytes.
- ARM equals `[2, 1, 0, ...]`.
- DISARM equals `[2, 0, 0, ...]`.
- Heartbeat equals `[3, 0, 0, ...]`.
- Reserved bytes remain zero.
- Signed targets use correct little-endian two's-complement encoding, including a negative test value.
- Every configuration scale factor and byte offset matches the table above.
- Invalid or overflowing values are rejected rather than wrapped.

### Concurrency/lifecycle tests

- Concurrent heartbeat and UI commands never interleave bytes.
- Only one heartbeat can remain pending.
- DISARM is prioritized over a pending heartbeat.
- Heartbeats begin after ARM write completion and stop after DISARM write completion.
- No heartbeats are produced when the desktop heartbeat setting is disabled.
- A simulated serial exception stops scheduling, marks the connection disconnected, and informs the UI state model.
- Shutdown has a bounded duration even when a serial write stalls.

Use an injectable serial transport interface so these tests can use a fake transport instead of a real COM port.

## Acceptance criteria

The change is complete when:

- all outbound packets go through one protocol builder and one serialized writer;
- the desktop sends a 64-byte heartbeat every 250 ms while armed by default;
- the UI remains responsive during serial reads/writes;
- disabling desktop heartbeat transmission is only possible while disarmed and displays a warning;
- disconnect and shutdown paths do not silently leave the UI showing an armed/connected state;
- configuration values are validated and encoded exactly as documented;
- automated tests cover packet layout, scheduling, write serialization, and failure handling;
- existing application features continue to work and the full Visual Studio solution builds without new warnings.

For physical integration testing, remove propellers and use a bench-safe setup. First verify packet bytes with a fake or loopback transport, then verify that stopping desktop heartbeats causes the expected embedded DISARM when the firmware failsafes are enabled.
