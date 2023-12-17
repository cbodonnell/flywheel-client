using System;

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

