# Chat App (TCP, async/await, WinForms) — team of 2

## Run
Requires .NET 8 SDK (client needs Windows).

    # terminal 1 - server (default port 5000)
    dotnet run --project ChatServer -- 5000

    # terminal 2 and 3 - clients
    dotnet run --project ChatClient

In each client: Host `127.0.0.1`, Port `5000`, choose a different name, click Connect.
Other computers on the same LAN: use the server machine's IP and open port 5000 in the firewall.

## Protocol (text, UTF-8, one message per line)
| Direction | Line |
|---|---|
| client -> server | `JOIN|name`, `MSG|text`, `QUIT` |
| server -> client | `SYS|text`, `MSG|name|text`, `USERS|a,b,c`, `ERR|text` |

## Work split
| Person | Files | Syllabus topics |
|---|---|---|
| A - Server | ChatServer/* | TCP server, multi-client, shared dictionary, broadcast, error handling (Ch.2, Ch.5) |
| B - Client | ChatClient/* | TCP client, async/await, UI-thread safety, cancellation (Ch.2, Ch.4) |

Both agree on the protocol table above, so each can test alone
(Person B can test against the server, or with `telnet 127.0.0.1 5000` typing `JOIN|test`).

## Ideas to extend (extra marks)
- Private messages `PM|target|text`
- Message history saved to a file
- Typing indicator, emoji, file sending
- TLS (`SslStream`) for secure chat (Ch.6 security)
