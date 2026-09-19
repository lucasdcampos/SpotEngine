# Spot.Net

The `net/` directory contains the networking subsystem for Spot Engine, encapsulated in the `Spot.Net` library. 

## Overview
`Spot.Net` layers networking capabilities on top of the engine core (`Spot.Engine`). It provides a framework for multiplayer and network-synced experiences, supporting both client and server roles.

## Architecture
- **Transport**: Abstractions for network transport. By default, it includes WebSockets. A browser build acts exclusively as a client, while desktop builds can act as a WebSocket server (using `HttpListener`) or a client.
- **Replication**: State synchronization layer for replicating Entities and Components across the network.
- **RPC (Remote Procedure Calls)**: Mechanisms for sending network messages or invoking functions remotely.
- **Messages**: Core network message definitions.
- **NetworkManager**: The primary system managing network state, connections, and message routing.

## Platform Support
The library multi-targets `net10.0` and `net10.0-browser`. This enables the client transport and the entire replication layer to compile and run natively on desktop as well as in the browser via WebAssembly (WASM). Server-specific implementations and developer console commands are automatically stripped from the browser build since it can only ever operate as a client.
