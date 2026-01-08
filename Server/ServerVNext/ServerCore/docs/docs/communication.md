# Communication

The communication namespace aims to provide configuration-free approaches to establishing connections to your microprocessor/robot.

The implementations are designed to behave consistently regardless of the host operating system. Preventing surprises when switching between OS's.

It also aims to be dynamic, responding to a device being added/removed from the environment, without the need to restart the program to acknowledge such changes.

## I can establish connections myself? Why should I use a communication manager.
If you are certain that the method to establish a connection is always guaranteed to be the same, or if you only need to establish a few communications that never change their state once connected, then there is no reason to use it. In fact it is beneficial and more efficient to avoid using it as it avoids efficiency reductions associated with searching in the background.

In practice, however, things don't always behave nicely. USB cables can be yanked, the network can drop, devices may stall. If you want to avoid restarting the program to account for such issues, you will need to implement something that takes such issues into account. Communication managers provided by this library aims to simplify that consideration.

## Using communication managers
All communication managers are expected to search and connect to candidate communication channels in the background. This begins after a call to `begin()`, only stopping when a `end()` is called.

Consumers of communication managers can subscribe to events that allow them to respond to established/lost communication channels appropriately.

```cs
ICommunicationManager communicationManager = new MyCommuncationManager(); // Assume an arbitrary implementation

// Subscribe to channel established events
/// It is essential that this happens *before* starting the communication manager.
/// If the communication manager is started before the event is subscribed to, then there is a chance that a channel is established in the period between starting the manager and subscribing to the event.
communicationManager.CommunicationChannelEstablished += (channel) => Console.WriteLine($"Communication channel established: {channel}");
communicationManager.CommuncationChannelLost += (channel) => Console.WriteLine($"Communication channel lost: {channel}");
communicationManager.Start(); 
```


When a channel is established, one can perform validation on the received communication. This procedure can range from an unconditional acceptance, to performing a handshake. This procedure is done outside of the manager.

```cs
void validateAndAcceptChannel(ICommuncationChannel c)
{
	List<byte> response = [];
	c.DataReceived += b => bytes.AddRange(bytes);
	c.Write("Are you the communcation channel I want?")

	while(response.Count < 10); // Wait until we get enough bytes

	if(responce is ["Yes I am!!"])
	{
		// Accept the connection
	}
}

communicationManager += validateAndAcceptChannel;
```

The specifics of ignoring a channel may vary depending on the underlying communication protocol. Refer to each manager's section to learn more.

### SerialCommunicationManager
This communication manager searches for and attempts to connect to serial ports announced by the system. This process is indiscriminate, and will attempt to connect to _ALL_ ports exposed by the system. 

This manager will periodically poll for serial ports connected to the host. If the set of serial ports changed since the last poll, then appropriate connection and disconnection events will be fired.

Because `SerialCommunicationManager` only responds to physical connection/disconnections, the manager will not attempt to re-establish a serial channel manually closed by a consumer of the channel. As such, **closing an ununsed/unwanted serial channel is recommended in order to free up system resources, and allow other processes to connect to it.** 

> `SerialCommunicationManager` does not take any configuration parameters, and the returned `SerialCommunicationChannel` does not expose anything specific to the underlying serial port.

### UDPCommunicationManager

This communication manager attempts to search for channel by periodically broadcasting a "ping" message over a network port. Candidates on the network can respond to the ping by sending a reply to the source IP endpoint. ("pong") 

By using a ping/pong approach, we avoid the need to hardcode the IP addresses of communication channel candidates, allowing connections to be made without the need of configuration, as long as all clients are listening on the same port.

While validation is expected to be implicit, as the listener will need to explicitly respond, and can take the polling message and port into account. Consumers may choose to perform additional validation. **If validation fails, one should avoid closing the channel and instead simply ignore it. Closing it will result in the manager not being able to keep track of it, resulting in the connection being re-established. There is no performance benefit to closing the `UDPCommunicationChannel`.** 

`UDPCommunicationManager` has two configurable options:

* `int port` - Providing a port is necessary for the broadcast
* `string PollMessage` - The message to be broadcasted. (Optional)

```cs
ICommunicationManager cm 
	= new UDPCommunicationManager(5000)
	  {
			PollMessage = "Hey I just met you... And this is Crazy... But here's my number... So call me maybe?"
	  }

// Subscribe to events
// ...

cm.Start();
```

If a `UDPCommunicationChannel` doesn't receive _any_ communication (including the polling message) for 10 seconds (by default), it will be considered lost due to a timeout. Ensure that the other end continues to respond to the polling message, to ensure the channel is kept alive.

### Creating your own communication manager

If the existing communication managers do not serve your needs. You can implement a alternative communication manager that implements both `ICommunicationManager` and `ICommunicationChannel`.

> While it is possible to provide an implementation that doesn't implement these interfaces. It is nonetheless recommended to do so, as it would allow developers to easily swap between communication protocols.

Implementations implementing `ICommunicationManager` are expected to search for and manage communication channels in a background thread. It should fire events when it establishes/loses a connection. 

Each `ICommunicationChannel` represents a self-contained communication channel. As long as the channel remains connected, it is expected that the other channel participant is the same participant that connected. 

> Consider sharing your communication manager by submitting a pull request!
