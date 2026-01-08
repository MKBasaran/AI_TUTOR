# Running multiple servers

A server can host multiple sessions/robots simultaneously without any significant issues. However, there may be scenarios where running multiple servers is more practical.

The primary reasons to consider a multi-server configuration:

* When multiple sessions are simultaneously active, servers may become overloaded. Running multiple servers can serve as a load-balancing mechanism.
* To better control and divide external resources
    + Some plugins may not be able to assign peripherals without a configuration step, and opt for zero-config alternatives.
    + Peripherals connection may be physically impractical (long cables)

To accommodate such issues, it is possible to run multiple instances of the server on the same network.

## Server behaviour
All servers on the same network will identify itself to every EDMO robot connected to the network, and all robots will also announce their presence in response. This essentially means that all servers see all robots on the network.

> This is an intentional choice to allow for another server to act as a fallback in the event of an unrecoverable fault on a server.

When an EDMO robot announces its presence, it will also provide information on whether it another server has an active session with it. This is used by the server to prevent a session from being created for an occupied robot. [See: EDMO Identification](Communication/fundamentals.md#identification-command)

Upon starting a session, the robot will mark itself as "locked" using the identifier provided by the server that started the session, this is released when the session is ended by the same server. The robot will announce its updated state following a session start/end.

The lock is weak, and will expire after 10 seconds if the lock is not refreshed by the lock-holder. This is a failsafe prevent the robot from being unusable if a server fails to properly end a session. The lock is refreshed upon sending updated oscillator parameters.



