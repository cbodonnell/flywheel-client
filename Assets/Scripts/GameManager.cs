using System;
using UnityEngine;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
	/*
	GameManager:
	- Manages player GameObjects, including creation, destruction, and position updates
	- Subscribes to network updates such as OnGameUpdateReceived event and handles player GameObjects accordingly
	*/

	// Reference to the player prefab
	public GameObject playerPrefab;

	// Dictionary to keep track of player GameObjects by their IDs
	private readonly Dictionary<uint, GameObject> playerGameObjects = new Dictionary<uint, GameObject>();

	// Duration over which the game interpolates between two states of a player:
	// Smaller values will make movement snappier
	// Larger value make it smoother, but potentially more laggy
	[SerializeField]
	public float smoothTimeFactor = 0.1f;

	[SerializeField]
	public float lerpTimeFactor = 0f;

	// Set to 'true' to use Lerp, 'false' for SmoothDamp
	[SerializeField]
	public bool useLerpForInterpolation = false; 

	// Define a hashset to keep track of active players for each GameUpdate
	// Similar to a 'set' in Python, data structure that stores unique values
	private HashSet<uint> activePlayerIDs = new HashSet<uint>();

	// Class to store historical state of players, used for interpolation
	public class HistoricalState
	{
		public float timestamp;
		public Vector2 position;
		public Vector2 currentVelocity;
		// Add other state data as needed, like rotation
	}

	// Keeps track of historical data, such as where a player has been:
	// Used to determine where to interpolate (i.e. target position)
	private Dictionary<uint, List<HistoricalState>> playerStateHistory = new Dictionary<uint, List<HistoricalState>>();

	// Keeps track of how fast a player is currently moving:
	// Used to determine how to interpolation (i.e. rate and smoothness of movement towards target position)
    private Dictionary<uint, Vector2> playerCurrentVelocities = new Dictionary<uint, Vector2>();

	// Unity lifecycle method that is called when script instance is being loaded, executed before the game starts and before the Start() method:
	// Commonly used for initialization tasks that need to be performed once and are independent of other objects (ex: setting initial values or conditions)
	private void Awake()
	{
		// Ensure this script persists across scene loads (if needed)
		DontDestroyOnLoad(gameObject);
	}

	// Unity lifecycle method that is called once before the first frame update but after Awake() method:
	// Commonly used for intialization that depend on other objects having been initialized
	private void Start()
	{
		// Subscribes the HandleGameUpdate method to NetworkManager's OnGameUpdateReceived event
		NetworkManager.Instance.OnGameUpdateReceived += HandleGameUpdate;
	}

	// Unity lifecycle method that is called once per frame
	private void Update()
	{
		// Delay for interpolation, should match or be roughly less than game update frequency
		// Tested sgu receipts on average 90-110 ms
		float delay = 0.08f; 

		foreach (var kvp in playerGameObjects)
		{
			uint playerId = kvp.Key;
			GameObject playerObject = kvp.Value;

			// Skip updating the local player's position since it's controlled by PlayerController
			if (playerId == NetworkManager.Instance.ClientID) continue;

			// Check if there are at least two historical states for interpolation
			if (playerStateHistory.TryGetValue(playerId, out var states) && states.Count >= 2)
			{
				HistoricalState state1 = null;
				HistoricalState state2 = null;

				// Iterate through historical states to find the appropriate states for interpolation
				for (int i = states.Count - 1; i >= 0; i--)
				{
					float convertedTimestamp = ConvertNetworkTimestamp(states[i].timestamp);

					// Select states based on the current time minus the delay
					if (convertedTimestamp <= Time.time - delay)
					{
						state1 = states[i];
						// Ensure state2 is the next state after state1, or the same if state1 is the latest
						state2 = i < states.Count - 1 ? states[i + 1] : state1;
						Debug.Log($"Selected states for player {playerId}: State1 at index {i}, State2 at index {(i < states.Count - 1 ? i + 1 : i)}");
						break;
					}
				}

				// Fallback: use the two most recent states if no states were selected above
				if (state1 == null || state2 == null)
				{
					state1 = states[states.Count - 2];
					state2 = states[states.Count - 1];
					Debug.Log($"Fallback states for player {playerId}: State1 at index {states.Count - 2}, State2 at index {states.Count - 1}");
				}

				// Perform interpolation if valid states were found
				if (state1 != null && state2 != null)
				{
					// Calculate interpolation factor 't'
					float t = (Time.time - delay - ConvertNetworkTimestamp(state1.timestamp)) / 
							(ConvertNetworkTimestamp(state2.timestamp) - ConvertNetworkTimestamp(state1.timestamp));
					t = Mathf.Clamp(t, 0, 1);

					Vector2 newPosition;
					// Choose interpolation method based on the setting
					if (useLerpForInterpolation)
					{
						// Frame rate independent Lerp using deltaTime
						float lerpFactor = Time.deltaTime / lerpTimeFactor; // Adjust smoothTime as needed
						newPosition = Vector2.Lerp(playerObject.transform.position, state2.position, lerpFactor);
					}
					else
					{
						// SmoothDamp for a smoother transition
						Vector2 currentVelocity = playerCurrentVelocities[playerId];
						newPosition = Vector2.SmoothDamp(playerObject.transform.position, 
														state2.position, 
														ref currentVelocity, 
														smoothTimeFactor);
						playerCurrentVelocities[playerId] = currentVelocity;
					}

					// Update player's position
					playerObject.transform.position = new Vector3(newPosition.x, newPosition.y, playerObject.transform.position.z);
				}
			}
		}
	}

	// Unity lifecycle method called when GameObject attached to this script is destroyed
	private void OnDestroy()
	{
		// Unsubscribes the HandleGameUpdate method to NetworkManager's OnGameUpdateReceived event
		if (NetworkManager.Instance != null)
		{
			NetworkManager.Instance.OnGameUpdateReceived -= HandleGameUpdate;
		}
	}

	// When subscribed, method is called whenever a game update is received
	public void HandleGameUpdate(ServerGameUpdatePayload payload)
	{
		HashSet<uint> updatedPlayerIDs = new HashSet<uint>();

		foreach (var playerInfo in payload.players)
		{
			uint playerId = playerInfo.Key;
			updatedPlayerIDs.Add(playerId);

			// Store historical state
			StorePlayerState(playerId, new Vector2(playerInfo.Value.p.x, playerInfo.Value.p.y), payload.timestamp);

			// Create or update players
			CreateOrUpdatePlayer(playerId);
		}

		// Check for disconnected players
		var disconnectedPlayers = new HashSet<uint>(activePlayerIDs);
		disconnectedPlayers.ExceptWith(updatedPlayerIDs);
		foreach (var playerId in disconnectedPlayers)
		{
			HandlePlayerDisconnection(playerId);
		}

		// Update the active player list
		activePlayerIDs = updatedPlayerIDs;
	}

	private void StorePlayerState(uint playerId, Vector2 position, float timestamp)
	{
		if (!playerStateHistory.ContainsKey(playerId))
		{
			playerStateHistory[playerId] = new List<HistoricalState>();
		}

		playerStateHistory[playerId].Add(new HistoricalState { position = position, timestamp = timestamp });

		// Trim the list to a reasonable size
		while (playerStateHistory[playerId].Count > 20) // for example, keep the last 20 states
		{
			playerStateHistory[playerId].RemoveAt(0);
		}

		// Update player velocities 
		if (playerStateHistory[playerId].Count >= 2)
		{
			var lastState = playerStateHistory[playerId][playerStateHistory[playerId].Count - 2];
			Vector2 positionDelta = position - lastState.position;
			float timeDelta = timestamp - lastState.timestamp;

			if (timeDelta > 0)
			{
				Vector2 newVelocity = positionDelta / timeDelta;
				playerCurrentVelocities[playerId] = newVelocity;
			}
		}
	}

	void HandlePlayerDisconnection(uint playerId)
	{
		if (playerGameObjects.TryGetValue(playerId, out GameObject playerObject))
		{
			Destroy(playerObject);
			playerGameObjects.Remove(playerId);
		}
	}

	private void CreateOrUpdatePlayer(uint playerId)
	{
		if (!playerGameObjects.TryGetValue(playerId, out GameObject playerObject))
		{
			// Instantiate new GameObject for new players
			GameObject newPlayer = Instantiate(playerPrefab, Vector2.zero, Quaternion.identity);
			newPlayer.name = "Player_" + playerId;
			playerGameObjects.Add(playerId, newPlayer);

			// Initialize currentVelocity for new player
        	playerCurrentVelocities[playerId] = Vector2.zero;

			// Add PlayerController only if it's the local player
			if (playerId == NetworkManager.Instance.ClientID)
			{
				var controller = newPlayer.AddComponent<PlayerController>();
				controller.isLocalPlayer = true;
			}
		}
	}

	// UI for connecting and disconnecting from the server
	private void OnGUI()
	{
		GUILayout.BeginArea(new Rect(10, 10, 300, 300));
		if (!NetworkManager.Instance.IsConnected)
		{
			if (GUILayout.Button("Connect"))
				NetworkManager.Instance.OnConnect();
		}
		else
		{
			if (!NetworkManager.Instance.HasClientID)
			{
				GUILayout.Label("Waiting to be assigned an ID...");
			}
			else
			{
				GUILayout.Label($"Connected as client: {NetworkManager.Instance.ClientID}");
			}

			if (GUILayout.Button("Disconnect"))
				NetworkManager.Instance.OnDisconnect();
		}
		GUILayout.EndArea();
	}

	private float ConvertNetworkTimestamp(float networkTimestamp)
	{
		// Convert from milliseconds to seconds
		double timestampInSeconds = networkTimestamp / 1000.0;
		// Calculate the time elapsed since the game started
		double elapsedTime = timestampInSeconds - (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds + Time.time;
		return (float)elapsedTime;
	}
}
