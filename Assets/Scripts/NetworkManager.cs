using UnityEngine;
using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Concurrent;
using Newtonsoft.Json;

public class NetworkManager : MonoBehaviour
{
	/*
	NetworkManager:
	- Manages network connections, sending, and receiving data
	- Defines connection details including TCP and UDP port settings
	- Utilizes separate threads for TCP and UDP message handling
	- Singleton pattern for NetworkManager
	*/

	// TCP and UDP IP + ports, 127.0.0.1 if server is running on localhost
	private string serverHostname = "127.0.0.1";
	private int serverTcpPort = 8888;
	private int serverUdpPort = 8889;

	// Separate threads for TCP and UDP
	private const int tcpReceiveBufferSize = 1024;
	private TcpClient tcpClient;
	private Thread tcpReceiveThread;
	private UdpClient udpClient;
	private Thread udpReceiveThread;

	// Thread-safe queue to hold actions that need to be executed on the main Unity thread
	private ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

	// Enum for disconnect reasons, by default assigned ints: OnDestroy = 1, UserRequestedDisconnect = 2, ServerClosedConnection = 3
	enum DisconnectReasons
	{
		OnDestroy,
		UserRequestedDisconnect,
		ServerClosedConnection
	}

	// Used for tracking connection state, where client has an ID, and the client ID itself
	private bool isConnected = false;
	public bool IsConnected
	{
		get { return isConnected; }
	}

	private bool hasClientID = false;
	public bool HasClientID
	{
		get { return hasClientID; }
	}

	private uint clientID;
	public uint ClientID
	{
		get { return clientID; }
	}

	// Singleton pattern to ensure only one instance of NetworkManager throughout the game, global point of access, and persistence
	private static NetworkManager _Instance;
	public static NetworkManager Instance
	{
		get
		{
			if (!_Instance)
			{
				// Create new GameObject, add Networkmanager as component
				_Instance = new GameObject().AddComponent<NetworkManager>();
				_Instance.name = _Instance.GetType().ToString();
				// Ensures object is not destroyed when loading a new scene for persistence
				DontDestroyOnLoad(_Instance.gameObject);
			}
			return _Instance;
		}
	}

	public void OnConnect()
	{
		try
		{
			ConnectToServer();
		}
		catch (Exception ex)
		{
			Debug.Log($"Error connecting to server: {ex.Message}");
		}
	}

	// Establishes TCP and UDP connections and starts receive threads
	private void ConnectToServer()
	{
		tcpClient = new TcpClient();
		tcpClient.Connect(serverHostname, serverTcpPort);

		tcpReceiveThread = new Thread(ReceiveTcpMessages);
		tcpReceiveThread.Start();

		udpClient = new UdpClient();
		udpClient.Connect(serverHostname, serverUdpPort);

		udpReceiveThread = new Thread(ReceiveUdpMessages);
		udpReceiveThread.Start();

		isConnected = true;
	}

	public void OnDisconnect()
	{
		try
		{
			DisconnectFromServer(DisconnectReasons.UserRequestedDisconnect);
		}
		catch (Exception ex)
		{
			Debug.Log($"Error disconnecting from server: {ex.Message}");
		}
	}

	// Closes TCP and UDP connections and aborts receive threads
	private void DisconnectFromServer(DisconnectReasons reason)
	{
		Debug.Log("Disconnecting from server");

		if (tcpClient != null)
		{
			tcpClient.Close();
			tcpClient = null;
		}

		if (tcpReceiveThread != null)
		{
			if (reason != DisconnectReasons.ServerClosedConnection)
				// if the server closed the connection, we don't want to abort the thread
				// since the disconnect originated from the receive thread
				// TODO: there's probably a better way to handle this   
				tcpReceiveThread.Abort();
			tcpReceiveThread = null;
		}

		if (udpClient != null)
		{
			udpClient.Close();
			udpClient = null;
		}

		if (udpReceiveThread != null)
		{
			udpReceiveThread.Abort();
			udpReceiveThread = null;
		}

		clientID = 0;
		hasClientID = false;
		isConnected = false;
	}

	// Sends UDP ping to the server
	public void PingUDP()
	{
		if (!isConnected || !hasClientID)
			return;

		ClientPingMessage pingMessage = new ClientPingMessage
		{
			clientID = clientID,
			type = MessageTypes.ClientPing,
		};

		string jsonMessage = JsonUtility.ToJson(pingMessage);
		byte[] data = Encoding.UTF8.GetBytes(jsonMessage);
		udpClient.Send(data, data.Length);
	}

	// Unity lifecycle method called when GameObject attached to this script is destroyed:
	// DisconnectFromServer method is invoked with reason OnDestroy (for example from changing scences or game closed)
	void OnDestroy()
	{
		DisconnectFromServer(DisconnectReasons.OnDestroy);
	}

	// Unity lifecycle method called once per frame:
	// Handles dequeing and executing actions from mainThreadActions to ensure they are ran on the main thread 
	// *See ReceiveUDPMessages below for example usage)
	private void Update() {
		while (mainThreadActions.TryDequeue(out Action action)) {
			action.Invoke();
		}
	}

	// Listens continuously for and processses TCP messages from the server:
	// TCP Usage: reliable data transfer
	// Different from UDP because it uses a byte buffer to reads data from a persistent TCP stream
	// Message types are defined in Message.cs and handled case by case
	private void ReceiveTcpMessages()
	{
		if (tcpClient == null)
			return;

		try
		{
			NetworkStream stream = tcpClient.GetStream();
			byte[] buffer = new byte[tcpReceiveBufferSize];

			while (true)
			{
				int bytesRead = stream.Read(buffer, 0, buffer.Length);
				if (bytesRead == 0)
				break;

				string receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
				Debug.Log($"Received TCP message: {receivedMessage}");

				Message message = JsonUtility.FromJson<Message>(receivedMessage);
				Debug.Log($"Received TCP message of type: {message.type}");

				switch (message.type)
				{
				case MessageTypes.ServerAssignID:
					AssignIDMessage assignIDMessage = JsonUtility.FromJson<AssignIDMessage>(receivedMessage);
					clientID = assignIDMessage.payload.clientID;
					hasClientID = true;
					// Send a UDP ping to the server once we have our client ID
					// this shouldn't be necessary once real identity is implemented
					PingUDP();
					break;
				default:
					Debug.Log($"Unknown message type: {message.type}");
					break;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.Log($"Error receiving TCP messages: {ex.Message}");
		}

		// If we get here, assume the connection has been closed
		DisconnectFromServer(DisconnectReasons.ServerClosedConnection);
	}

	// Define a delegate for game updates, this is similar to a pointer function in other languages:
	// Can hold references to any method that takes on argument of type ServerGameUpdatePayload where 'payload' is the actual parameter
	public delegate void GameUpdateHandler(ServerGameUpdatePayload payload);

	// Define an event called OnGameUpdatedReceived suing the GameUpdateHandler delegate:
	// This can be subscribed to from other scripts such as GameManager.cs
	public event GameUpdateHandler OnGameUpdateReceived;

	// Listens continuously for and processses UDP messages from the server:
	// UDP Usage: unreliable data transfer
	// Different from TCP because there is no persistent data stream, data is just sent each time to an address, delivery is not guarenteed
	// Message types are defined in Message.cs and handled case by case
	private void ReceiveUdpMessages()
	{
		if (udpClient == null)
		{
			Debug.LogError("UDP Client is null, exiting ReceiveUdpMessages");
			return;
		}

		try
		{
			IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);

			while (true)
			{
				Debug.Log("Waiting for UDP data...");
				byte[] receivedData = udpClient.Receive(ref remoteEndpoint);
				if (receivedData.Length == 0)
				{
				Debug.LogWarning("Received empty UDP packet, continuing loop...");
				continue;
				}

				string receivedMessage = Encoding.UTF8.GetString(receivedData);

				// Log the raw JSON for debugging
				Debug.Log("Raw JSON payload: " + receivedMessage);

				// Use Json.NET for deserialization
				var message = JsonConvert.DeserializeObject<Message>(receivedMessage);
				Debug.Log($"Received UDP message of type: {message.type}");

				switch (message.type)
				{
				case MessageTypes.ServerPong:
					var pongMessage = JsonConvert.DeserializeObject<ServerPongMessage>(receivedMessage);
					Debug.Log($"Received UDP Pong from server");
					break;

				case MessageTypes.ServerGameUpdate:
					var gameUpdateMessage = JsonConvert.DeserializeObject<ServerGameUpdateMessage>(receivedMessage);
					Debug.Log($"Received UDP GameUpdate from server");

					// Enqueues method to be executed on the main thread
					mainThreadActions.Enqueue(() => {
						// Checks if there are any subscribers to OnGameUpdateReceived and then invokes the event
						// When the event is invoked, the parameter gameUpdateMessage.payload is passed to all subscribed methods
						OnGameUpdateReceived?.Invoke(gameUpdateMessage.payload);
					});
					break;

				default:
					Debug.Log($"Unknown message type: {message.type}");
					break;
				}
			}
		}
		catch (SocketException sockEx)
		{
			Debug.LogError($"SocketException in ReceiveUdpMessages: {sockEx}");
		}
		catch (Exception ex)
		{
			Debug.LogError($"Exception in ReceiveUdpMessages: {ex}");
		}
		finally
		{
			Debug.Log("Exited UDP receive loop - this should not happen unless the client is disconnecting");
		}
	}

	// Method for sending ClientPlayerUpdate (cpu) to the server:
	// Sends client timestamp and position
	public void SendClientPlayerUpdate(Vector2 position)
	{
		// Create the payload for the player update message
		ClientPlayerUpdatePayload updatePayload = new ClientPlayerUpdatePayload
		{
			timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			playerState = new PlayerState
			{
				p = new Position { x = position.x, y = position.y }
			}
		};

		// Create the player update message with the payload
		ClientPlayerUpdateMessage playerUpdateMessage = new ClientPlayerUpdateMessage
		{
			clientID = clientID,
			type = MessageTypes.ClientPlayerUpdate,
			payload = updatePayload
		};

		string jsonMessage = JsonUtility.ToJson(playerUpdateMessage);
		// Debug.Log(jsonMessage);
		byte[] data = Encoding.UTF8.GetBytes(jsonMessage);

		try
		{
			udpClient.Send(data, data.Length);
		}
		catch (Exception ex)
		{
			Debug.LogError("Error sending player update via UDP: " + ex.Message);
		}
	}
}
