using System;
using System.Collections.Generic;

public static class MessageTypes
{
	public const string ServerAssignID = "aid";
	public const string ClientPing = "cp";
	public const string ServerPong = "sp";
	public const string ClientPlayerUpdate = "cpu";
	public const string ServerGameUpdate = "sgu";
}

[Serializable]
public class Message
{
	public uint clientID;
	public string type;
}

[Serializable]
public class AssignIDMessage : Message
{
	public AssignIDPayload payload;
}

[Serializable]
public class AssignIDPayload
{
	public uint clientID;
}

[Serializable]
public class ClientPingMessage : Message {}

[Serializable]
public class ServerPongMessage : Message {}

[Serializable]
public class ServerGameUpdateMessage : Message
{
	public ServerGameUpdatePayload payload;
}

[Serializable]
public class ServerGameUpdatePayload
{
	public long timestamp;
	public Dictionary<uint, PlayerState> players;
}

[Serializable]
public class PlayerState
{
	public Position p;
}

[Serializable]
public class Position
{
	public float x;
	public float y;
}

public class ClientPlayerUpdateMessage : Message
{
	public ClientPlayerUpdatePayload payload;
}

[Serializable]
public class ClientPlayerUpdatePayload
{
	public long timestamp;
	public PlayerState playerState;
}
